using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace LightflowStudio.Tests;

/// <summary>Test-only policy. No environment value can select a production Catalog or shared pairing directory.</summary>
internal sealed record PremiereAcceptanceIsolation(string Root, string ProjectPath, string MediaPath,
    LightflowStorageLocations Locations)
{
    internal static string Workspace([CallerFilePath] string source = "") => Path.GetDirectoryName(Path.GetDirectoryName(source))!;
    internal static string FixtureArea => Path.Combine(Workspace(), ".cache", "premiere-acceptance");
    internal string RunDirectory => Locations.ApplicationDataDirectory;
    internal string PairingDirectory => Locations.PremierePairingDirectory;

    internal static PremiereAcceptanceIsolation Validate(string root, string project, string media)
    {
        // Resolve supplies the established absolute-local-path and normal-profile overlap rules.
        var canonical = ApplicationDataProfile.Resolve(["--data-root", root]).ApplicationDataDirectory;
        ApplicationDataProfile.RequireContained(FixtureArea, canonical);
        if (string.Equals(canonical, FixtureArea, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Use a disposable fixture subdirectory beneath the acceptance area.");
        ValidateFile(canonical, project, ".prproj");
        ValidateFile(canonical, media, ".mov");
        // Inspect existing fixture state before any directory creation, Catalog opening or bridge startup.
        InspectTree(canonical);
        var locations = ApplicationDataProfile.Resolve(["--data-root", Path.Combine(canonical, "runs", Guid.NewGuid().ToString("N"))]);
        ApplicationDataProfile.RequireContained(canonical, locations.ApplicationDataDirectory);
        return new(canonical, Path.GetFullPath(project), Path.GetFullPath(media), locations);
    }

    private static void InspectTree(string directory)
    {
        ApplicationDataProfile.RequireContained(directory, directory);
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            // GetAttributes also rejects dangling reparse entries; do not follow them recursively.
            var attributes = File.GetAttributes(entry);
            if (attributes.HasFlag(FileAttributes.ReparsePoint))
                throw new IOException("Acceptance fixtures cannot contain links or junctions: " + entry);
            if (attributes.HasFlag(FileAttributes.Directory)) InspectTree(entry);
        }
    }

    private static void ValidateFile(string root, string path, string extension)
    {
        var canonical = ApplicationDataProfile.Resolve(["--data-root", path]).ApplicationDataDirectory;
        ApplicationDataProfile.RequireContained(root, canonical);
        if (!string.Equals(Path.GetExtension(canonical), extension, StringComparison.OrdinalIgnoreCase) || !File.Exists(canonical))
            throw new ArgumentException($"A contained disposable {extension} fixture is required: {path}");
        using var handle = File.OpenHandle(canonical, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (!GetFileInformationByHandle(handle, out var information) || information.NumberOfLinks != 1)
            throw new IOException("Disposable fixtures must be ordinary files with one filesystem link: " + path);
    }

    internal void Revalidate(string activeProject)
    {
        // Premiere returns extended drive paths; normalize only that observed spelling,
        // then apply exactly the same local-path, overlap and reparse policy.
        if (activeProject.StartsWith(@"\\?\", StringComparison.Ordinal) && activeProject.Length > 6
            && char.IsAsciiLetter(activeProject[4]) && activeProject[5] == ':' && activeProject[6] == '\\')
            activeProject = activeProject[4..];
        ValidateFile(Root, activeProject, ".prproj");
        ValidateFile(Root, MediaPath, ".mov");
        if (PremiereProtocol.PathKey(activeProject) != PremiereProtocol.PathKey(ProjectPath))
            throw new InvalidOperationException("Active Premiere project is not the selected disposable fixture.");
        InspectTree(Root);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileInformation
    {
        public uint Attributes, CreationLow, CreationHigh, AccessLow, AccessHigh, WriteLow, WriteHigh;
        public uint Volume, SizeHigh, SizeLow, NumberOfLinks, IndexHigh, IndexLow;
    }
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInformation information);
}
