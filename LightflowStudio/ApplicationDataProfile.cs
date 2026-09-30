using System.IO;
using System.Diagnostics;
using System.Collections.Concurrent;
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
        RequireLexicallyContained(root, path);
        RejectLinkedAncestors(path);
    }

    // An initialization result is an explicit capability shared by App and storage, not a global
    // "already checked" bit. Independent sessions/tests still validate their selected profile.
    public static InitializedDataProfile Initialize(LightflowStorageLocations locations,
        IProfileInitializationFileSystem? fileSystem = null)
    {
        if (!locations.IsIsolated) return new(locations, 0, 0);
        var invocation = Interlocked.Increment(ref _validationInvocations);
        var timer = Stopwatch.StartNew();
        fileSystem ??= new ProfileInitializationFileSystem();
        var inspected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var root = locations.ApplicationDataDirectory;
        // Resolve is pure, and callers constructing a profile directly receive the same checks.
        var selected = Resolve(["--data-root", root]);
        foreach (var path in StartupPaths(locations))
        {
            RequireLexicallyContained(selected.ApplicationDataDirectory, path);
            InspectAncestors(path, fileSystem.Attributes, inspected);
        }
        foreach (var directory in new[] { root, locations.CatalogDirectory, locations.CatalogBackupsDirectory,
            locations.PreviewsDirectory, locations.TemporaryDirectory })
        {
            fileSystem.CreateDirectory(directory);
            fileSystem.ProbeWrite(directory);
        }
        IsolatedRoots.TryAdd(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)), 0);
        var result = new InitializedDataProfile(locations, inspected.Count, timer.Elapsed.TotalMilliseconds, invocation);
        StartupDiagnostics.Note($"Isolated profile validation: completed invocations={result.ValidationInvocations} inspectedPaths={result.InspectedPaths} descendantEnumerations=0 duration={result.ElapsedMilliseconds:F1}ms");
        return result;
    }

    private static IEnumerable<string> StartupPaths(LightflowStorageLocations locations)
    {
        // Fixed ownership inventory. Neither arbitrary files nor cache descendants are enumerated.
        foreach (var property in typeof(LightflowStorageLocations).GetProperties()
                     .Where(property => property.PropertyType == typeof(string)))
            yield return (string)property.GetValue(locations)!;
        foreach (var name in new[] { "export-defaults.json", "export-jobs.v2.json", "encoding-resources",
            "encoding-resources/luts", "Catalog Backups", "Screengrabs", "startup-session.state",
            "settings.json.tmp", "export-defaults.json.tmp", "export-jobs.v2.json.tmp",
            "activity.log.1", "activity.log.2", "activity.log.3",
            "preview-quiescence.json", "regeneration.json", "regeneration-restart.json", "backup-path-verification.jsonl" })
            yield return Path.Combine(locations.ApplicationDataDirectory, name);
        foreach (var database in new[] { locations.CatalogDatabasePath, locations.PreviewsDatabasePath })
            foreach (var suffix in new[] { "-wal", "-shm", "-journal", ".startup-state" })
                yield return database + suffix;
    }

    private static int _validationInvocations;
    private static readonly ConcurrentDictionary<string, byte> IsolatedRoots = new(StringComparer.OrdinalIgnoreCase);

    // Registered only for isolated profiles. Normal installed startup does no isolation filesystem work.
    // Resolve only the actual path being used; untouched derived-cache descendants are irrelevant.
    internal static string GuardAccess(string path)
    {
        if (IsolatedRoots.IsEmpty) return path;
        var full = Path.GetFullPath(path);
        foreach (var root in IsolatedRoots.Keys)
            if (MediaPathSemantics.Contains(root, full))
            {
                RequireContained(root, path);
                break;
            }
        return path;
    }

    internal static IEnumerable<string> EnumerateOwnedFiles(string root, string pattern, SearchOption option)
    {
        if (!IsolatedRoots.Keys.Any(owned => MediaPathSemantics.Contains(owned, root)))
        {
            foreach (var path in Directory.EnumerateFiles(root, pattern, option)) yield return path;
            yield break;
        }
        GuardAccess(root);
        // Explicit maintenance may enumerate; it must never follow links into another profile.
        var pending = new Stack<string>(); pending.Push(root);
        while (pending.TryPop(out var directory))
        {
            GuardAccess(directory);
            foreach (var path in Directory.EnumerateFiles(directory, pattern)) yield return GuardAccess(path);
            if (option != SearchOption.AllDirectories) continue;
            foreach (var child in Directory.EnumerateDirectories(directory))
            { GuardAccess(child); pending.Push(child); }
        }
    }

    private static void RequireLexicallyContained(string root, string path)
    {
        var full = Path.GetFullPath(path);
        if (!MediaPathSemantics.Contains(root, full))
            throw new ArgumentException("Isolated storage locations must remain beneath --data-root.");
        // Win32 aliases must not turn a lexically contained path into a different file/stream.
        if (path.StartsWith(@"\\") || path.AsSpan(2).Contains(':') || path.Split('\\', '/').Any(part =>
                part != "." && part != ".." && (part.EndsWith(' ') || part.EndsWith('.'))))
            throw new ArgumentException("Isolated storage paths cannot use device paths, streams or ambiguous names.");
    }

    private static void RejectLinkedAncestors(string path) =>
        InspectAncestors(path, ProfileInitializationFileSystem.ReadAttributes, null);

    private static void InspectAncestors(string path, Func<string, FileAttributes?> attributes, HashSet<string>? inspected)
    {
        // Walk from volume toward leaf: never inspect a descendant through an unchecked junction.
        var ancestors = new Stack<string>();
        for (var current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
            ancestors.Push(current);
        foreach (var current in ancestors)
        {
            if (inspected is not null && !inspected.Add(current)) continue;
            if (attributes(current) is { } value && (value & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Isolated profiles cannot use links or junctions: " + current);
        }
    }
}

internal sealed record InitializedDataProfile(LightflowStorageLocations Locations, int InspectedPaths, double ElapsedMilliseconds, int ValidationInvocations = 0)
{
    internal void RequireProfile(LightflowStorageLocations locations)
    {
        if (Locations != locations) throw new ArgumentException("The validated profile does not match the requested storage profile.");
    }
}

// Deliberately has no descendant-enumeration API. Tests assert an operation budget, not elapsed time.
internal interface IProfileInitializationFileSystem
{
    FileAttributes? Attributes(string path);
    void CreateDirectory(string path);
    void ProbeWrite(string directory);
}
internal sealed class ProfileInitializationFileSystem : IProfileInitializationFileSystem
{
    public FileAttributes? Attributes(string path) => ReadAttributes(path);
    internal static FileAttributes? ReadAttributes(string path)
    {
        try { return File.GetAttributes(path); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }
    public void CreateDirectory(string path) => Directory.CreateDirectory(path);
    public void ProbeWrite(string directory)
    {
        var probe = Path.Combine(directory, ".write-probe-" + Guid.NewGuid().ToString("N"));
        using var stream = new FileStream(probe, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
            1, FileOptions.DeleteOnClose);
        stream.WriteByte(1); stream.Flush(true);
    }
}
