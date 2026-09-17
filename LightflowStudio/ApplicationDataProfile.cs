using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace LightflowStudio;

/// <summary>Process profile selection, before logging, activation, or persisted settings are opened.</summary>
internal static class ApplicationDataProfile
{
    public static LightflowStorageLocations Resolve(string[] arguments)
    {
        string? root = null;
        for (var index = 0; index < arguments.Length; index++)
        {
            var argument = arguments[index];
            if (!argument.StartsWith("--data-root", StringComparison.OrdinalIgnoreCase)) continue;
            if (argument != "--data-root" || root is not null || ++index == arguments.Length ||
                string.IsNullOrWhiteSpace(arguments[index]) || arguments[index].StartsWith("--"))
                throw new ArgumentException("Use --data-root <absolute-directory> exactly once (case-sensitive, separate value).");
            root = arguments[index];
        }
        if (root is null) return LightflowStorageLocations.CreateDefault();
        if (!Path.IsPathFullyQualified(root) || root.IndexOfAny(Path.GetInvalidPathChars()) >= 0 ||
            root.Contains('"') || root.Contains('*') || root.Contains('?') || root.StartsWith(@"\\") ||
            root.AsSpan(2).Contains(':') || root.Split('\\', '/').Any(part =>
                part != "." && part != ".." && (part.EndsWith(' ') || part.EndsWith('.'))))
            throw new ArgumentException("--data-root requires an absolute local directory without device paths or ambiguous trailing dots/spaces.");
        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        if (LightflowStorageLocations.PathsOverlap(root, LightflowStorageLocations.CreateDefault().ApplicationDataDirectory))
            throw new ArgumentException("--data-root must be separate from the normal application profile.");
        return LightflowStorageLocations.CreateAtRoot(root) with { IsIsolated = true };
    }

    public static string InstanceIdentity(LightflowStorageLocations locations) => !locations.IsIsolated
        ? WindowsApplicationInstanceCoordinator.ApplicationIdentity
        : WindowsApplicationInstanceCoordinator.ApplicationIdentity + "." + Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(locations.ApplicationDataDirectory.ToUpperInvariant())));

    public static void RequireContained(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar))
            throw new ArgumentException("Isolated storage locations must remain beneath --data-root.");
        RejectLinkedAncestors(path);
    }

    public static void Initialize(LightflowStorageLocations locations)
    {
        if (!locations.IsIsolated) return;
        var root = locations.ApplicationDataDirectory;
        RejectLinkedAncestors(root);
        Directory.CreateDirectory(root);
        // Reject existing junctions/symlinks before opening any profile file or following a directory.
        InspectTree(root);
        foreach (var directory in new[] { root, locations.CatalogDirectory, locations.CatalogBackupsDirectory,
            locations.PreviewsDirectory, locations.TemporaryDirectory })
        {
            RequireContained(root, directory);
            Directory.CreateDirectory(directory);
            var probe = Path.Combine(directory, ".write-probe-" + Guid.NewGuid().ToString("N"));
            using var stream = new FileStream(probe, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
                1, FileOptions.DeleteOnClose);
            stream.WriteByte(1);
            stream.Flush(true);
        }
    }

    private static void InspectTree(string directory)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Isolated profiles cannot contain links or junctions: " + entry);
            if ((attributes & FileAttributes.Directory) != 0) InspectTree(entry);
        }
    }

    private static void RejectLinkedAncestors(string path)
    {
        for (var current = path; current is not null; current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Isolated profiles cannot use links or junctions: " + current);
    }
}
