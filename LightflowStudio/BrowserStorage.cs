using System.IO;

namespace LightflowStudio;

internal enum BrowserStorageKind { Volume, ManagedRoot }

internal sealed record BrowserStorageEntry(
    string Key,
    string DisplayName,
    string? PhysicalPath,
    BrowserStorageKind Kind,
    MediaRootAvailability Availability,
    Guid? RootId = null,
    string? Diagnostic = null)
{
    public string DisplayKind => Kind == BrowserStorageKind.Volume ? "Storage" : "Library";
}

internal interface IBrowserStorageProvider
{
    Task<IReadOnlyList<BrowserStorageEntry>> ListAsync(CancellationToken cancellationToken = default);
}

internal sealed record BrowserVolume(string RootPath, string DisplayName, bool IsReady, string? Diagnostic = null);

internal interface IBrowserVolumeProvider
{
    IReadOnlyList<BrowserVolume> ListVolumes();
}

internal interface IBrowserDrive
{
    string Name { get; }
    string RootPath { get; }
    bool IsReady { get; }
    string VolumeLabel { get; }
}

internal interface IBrowserDriveSource
{
    IReadOnlyList<IBrowserDrive> ListDrives();
}

internal sealed class WindowsBrowserDrive(DriveInfo drive) : IBrowserDrive
{
    public string Name => drive.Name;
    public string RootPath => drive.RootDirectory.FullName;
    public bool IsReady => drive.IsReady;
    public string VolumeLabel => drive.VolumeLabel;
}

internal sealed class WindowsBrowserDriveSource : IBrowserDriveSource
{
    public IReadOnlyList<IBrowserDrive> ListDrives() =>
        DriveInfo.GetDrives().Select(drive => (IBrowserDrive)new WindowsBrowserDrive(drive)).ToArray();
}

internal sealed class WindowsBrowserVolumeProvider : IBrowserVolumeProvider
{
    private readonly IBrowserDriveSource _source;

    public WindowsBrowserVolumeProvider() : this(new WindowsBrowserDriveSource()) { }
    internal WindowsBrowserVolumeProvider(IBrowserDriveSource source) => _source = source;

    public IReadOnlyList<BrowserVolume> ListVolumes()
    {
        var volumes = new List<BrowserVolume>();
        IReadOnlyList<IBrowserDrive> drives;
        try { drives = _source.ListDrives(); }
        catch (Exception exception) when (IsExpectedDriveFailure(exception)) { return volumes; }
        foreach (var drive in drives)
        {
            var name = SafeName(drive);
            try
            {
                var ready = drive.IsReady;
                var label = ready && !string.IsNullOrWhiteSpace(drive.VolumeLabel)
                    ? $"{drive.VolumeLabel} ({name.TrimEnd('\\')})"
                    : name.TrimEnd('\\');
                volumes.Add(new(drive.RootPath, label, ready,
                    ready ? null : "This drive is not currently ready."));
            }
            catch (Exception exception) when (IsExpectedDriveFailure(exception))
            {
                var root = SafeRootPath(drive);
                if (root is not null)
                    volumes.Add(new(root, name.TrimEnd('\\'), false,
                        $"This drive is unavailable: {exception.Message}"));
            }
        }
        return volumes;
    }

    private static string SafeName(IBrowserDrive drive)
    {
        try { return drive.Name; }
        catch (Exception exception) when (IsExpectedDriveFailure(exception)) { return "Unavailable storage"; }
    }

    private static string? SafeRootPath(IBrowserDrive drive)
    {
        try { return drive.RootPath; }
        catch (Exception exception) when (IsExpectedDriveFailure(exception)) { return null; }
    }

    private static bool IsExpectedDriveFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or System.Security.SecurityException or
            InvalidOperationException or ArgumentException;
}

internal sealed class BrowserStorageProvider(IMediaRootService roots, IBrowserVolumeProvider volumes)
    : IBrowserStorageProvider
{
    public async Task<IReadOnlyList<BrowserStorageEntry>> ListAsync(CancellationToken cancellationToken = default)
    {
        var entries = new List<BrowserStorageEntry>();
        var volumePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var volume in await Task.Run(volumes.ListVolumes, cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string path;
            try { path = MediaPathSemantics.NormalizeRootPath(volume.RootPath); }
            catch (ArgumentException) { continue; }
            volumePaths.Add(path);
            entries.Add(new($"volume:{path.ToUpperInvariant()}", volume.DisplayName, path,
                BrowserStorageKind.Volume, volume.IsReady ? MediaRootAvailability.Online : MediaRootAvailability.Unavailable,
                Diagnostic: volume.Diagnostic));
        }

        try
        {
            foreach (var root in await roots.ListAsync(cancellationToken).ConfigureAwait(false))
            {
                if (root.PhysicalPath is not null &&
                    volumePaths.Contains(MediaPathSemantics.NormalizeRootPath(root.PhysicalPath))) continue;
                entries.Add(new($"root:{root.RootId:D}", root.DisplayName, root.PhysicalPath,
                    BrowserStorageKind.ManagedRoot, root.Availability, root.RootId, root.Diagnostic));
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or MachineIdentityException)
        {
            // Volumes remain browseable entry points while Catalog diagnostics stay explicit on navigation.
        }

        return entries.OrderBy(entry => entry.Kind).ThenBy(entry => entry.DisplayName,
            StringComparer.OrdinalIgnoreCase).ToArray();
    }
}

internal enum BrowserLocationResolutionStatus
{
    Ready,
    CatalogUnavailable,
    FolderUnavailable,
    InvalidPath,
    Failed
}

internal sealed record BrowserLocationResolution(
    BrowserLocationResolutionStatus Status,
    Guid? RootId = null,
    string? RootName = null,
    string? RootPath = null,
    string RelativeFolder = "",
    string? Diagnostic = null)
{
    public bool Succeeded => Status == BrowserLocationResolutionStatus.Ready && RootId is not null;
}

internal interface IBrowserLocationResolver
{
    Task<BrowserLocationResolution> ResolveAsync(string absoluteFolder,
        CancellationToken cancellationToken = default);
}

internal interface IBrowserLocationFileSystem
{
    bool DirectoryExists(string path);
}

internal sealed class BrowserLocationFileSystem : IBrowserLocationFileSystem
{
    public bool DirectoryExists(string path) => Directory.Exists(path);
}

/// <summary>
/// Converts Explorer-familiar absolute folders into stable logical RootId + relative-folder locations.
/// Existing containing mappings win by specificity; otherwise one natural volume/share anchor is established.
/// </summary>
internal sealed class BrowserLocationResolver(IMediaRootService roots, IBrowserLocationFileSystem fileSystem)
    : IBrowserLocationResolver
{
    public async Task<BrowserLocationResolution> ResolveAsync(string absoluteFolder,
        CancellationToken cancellationToken = default)
    {
        string folder;
        try { folder = MediaPathSemantics.NormalizeRootPath(absoluteFolder); }
        catch (ArgumentException exception) { return new(BrowserLocationResolutionStatus.InvalidPath, Diagnostic: exception.Message); }

        bool exists;
        try { exists = await Task.Run(() => fileSystem.DirectoryExists(folder), cancellationToken).ConfigureAwait(false); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new(BrowserLocationResolutionStatus.FolderUnavailable, Diagnostic: exception.Message);
        }
        if (!exists)
            return new(BrowserLocationResolutionStatus.FolderUnavailable, Diagnostic: "This folder is not currently available.");

        try
        {
            var existing = FindMostSpecific(await roots.ListAsync(cancellationToken).ConfigureAwait(false), folder);
            if (existing is not null) return ToResolution(existing, folder);

            string anchor;
            try { anchor = NaturalAnchor(folder); }
            catch (ArgumentException exception)
            {
                return new(BrowserLocationResolutionStatus.InvalidPath, Diagnostic: exception.Message);
            }
            var created = await roots.CreateBrowserAnchorAsync(AnchorName(anchor), anchor, cancellationToken)
                .ConfigureAwait(false);
            if (!created.Succeeded || created.Root is null)
                return new(BrowserLocationResolutionStatus.Failed,
                    Diagnostic: created.Diagnostic ?? "Lightflow could not establish a Catalog identity for this location.");

            // Creation is serialized, but re-evaluate so a more-specific concurrent mapping remains authoritative.
            var resolved = FindMostSpecific(await roots.ListAsync(cancellationToken).ConfigureAwait(false), folder)
                ?? created.Root;
            return ToResolution(resolved, folder);
        }
        catch (InvalidOperationException exception)
        {
            return new(BrowserLocationResolutionStatus.CatalogUnavailable, Diagnostic: exception.Message);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new(BrowserLocationResolutionStatus.Failed, Diagnostic: exception.Message);
        }
    }

    internal static string NaturalAnchor(string absoluteFolder)
    {
        var folder = MediaPathSemantics.NormalizeRootPath(absoluteFolder);
        var root = Path.GetPathRoot(folder);
        if (string.IsNullOrWhiteSpace(root)) throw new ArgumentException("The location has no stable volume or share root.");
        return MediaPathSemantics.NormalizeRootPath(root);
    }

    private static MediaRootInfo? FindMostSpecific(IEnumerable<MediaRootInfo> candidates, string folder) =>
        candidates.Where(root => root.Availability == MediaRootAvailability.Online &&
                                 !string.IsNullOrWhiteSpace(root.PhysicalPath) &&
                                 MediaPathSemantics.Contains(root.PhysicalPath, folder))
            .OrderByDescending(root => MediaPathSemantics.NormalizeRootPath(root.PhysicalPath!).Length)
            .FirstOrDefault();

    private static BrowserLocationResolution ToResolution(MediaRootInfo root, string folder) =>
        new(BrowserLocationResolutionStatus.Ready, root.RootId, root.DisplayName, root.PhysicalPath,
            MediaPathSemantics.RelativeFolder(root.PhysicalPath!, folder));

    private static string AnchorName(string anchor)
    {
        if (anchor.StartsWith("\\\\", StringComparison.Ordinal))
            return $"Network share {anchor.TrimEnd('\\')}";
        return $"Drive {anchor.TrimEnd('\\')}";
    }
}
