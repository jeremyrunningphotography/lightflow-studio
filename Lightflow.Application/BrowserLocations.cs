using LightflowStudio;

namespace Lightflow.Application;

/// <summary>Filesystem identities are transient navigation, never Catalog ingestion intent.</summary>
public sealed record BrowserFileLocation(string Path, string Name, bool CanPrefetch = true);
public sealed record BrowserLocationListing(MediaFolderEnumerationStatus Status,
    IReadOnlyList<BrowserFileLocation> Folders);

public interface IBrowserLocationProvider
{
    Task<IReadOnlyList<BrowserFileLocation>> GetLocationsAsync(CancellationToken token);
    Task<BrowserLocationListing> GetFoldersAsync(string absolutePath, CancellationToken token, bool userInitiated = false);
}

public sealed record BrowserCatalogScope(Guid RootId, string RelativeFolder);

public static class BrowserLocationMapping
{
    /// <summary>Read-only most-specific mapping. No natural anchor creation or reconciliation.</summary>
    public static BrowserCatalogScope? Find(string path, IEnumerable<MediaRootInfo> roots,
        StringComparison comparison)
    {
        var match = roots.Where(root => root.PhysicalPath is { } mapped &&
                (string.Equals(path, mapped.TrimEnd('/'), comparison) ||
                 path.StartsWith(mapped.TrimEnd('/') + "/", comparison)))
            .OrderByDescending(root => root.PhysicalPath!.Length).FirstOrDefault();
        if (match is null) return null;
        return new(match.RootId, path.Length == match.PhysicalPath!.TrimEnd('/').Length
            ? "" : path[(match.PhysicalPath.TrimEnd('/').Length + 1)..]);
    }
}
