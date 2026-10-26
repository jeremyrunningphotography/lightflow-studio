using LightflowStudio;
namespace Lightflow.Application;

/// <summary>Read-only application projection over the active lifecycle's existing repositories.</summary>
public sealed record BrowserCatalogSnapshot(Guid CatalogId, IReadOnlyList<MediaRootInfo> Roots,
    IReadOnlyList<MediaAsset> Assets);

public interface IBrowserCatalogSession
{
    Task<BrowserCatalogSnapshot> ReadBrowserCatalogAsync(Guid? rootId = null, string folder = "",
        bool recursive = false, CancellationToken cancellationToken = default);
}

public sealed record BrowserFolderSnapshot(Guid RootId, string RelativeFolder, bool Recursive,
    MediaFolderEnumerationStatus Status, IReadOnlyList<MediaFolderEntry> Folders,
    IReadOnlyList<MediaAsset> Assets, string? Diagnostic = null);

public interface IBrowserSourceAvailability
{
    Task<bool> FileExistsAsync(MediaRootInfo root, string relativePath, CancellationToken token);
}

/// <summary>Read-only composition of the existing folder enumerator, Catalog scope query and recursive rules.
/// It never reconciles, ingests or schedules derived work.</summary>
public sealed class BrowserCatalogReader(IMediaAssetRepository assets,
    IBrowserRecursiveRootRepository recursiveRoots, IMediaFolderEnumerator folders)
{
    public async Task<BrowserFolderSnapshot> ReadAsync(Guid rootId, string folder, CancellationToken token)
    {
        if (folder.Length > 0 && !PortablePathIdentityValidator.ValidateRelativePath(folder,token).IsSafe)
            throw new ArgumentException("This relative folder needs review.",nameof(folder));
        var relative = folder.Length == 0 ? "" : CatalogRelativePaths.NormalizeRelativePath(folder);
        var listing = await folders.EnumerateAsync(new(rootId, relative), token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (!listing.Succeeded) return new(rootId, relative, false, listing.Status, [], [], listing.Diagnostic);
        var recursive = BrowserRecursiveRootLogic.IsEffectivelyRecursive(
            await recursiveRoots.ListAsync(token).ConfigureAwait(false), rootId, relative);
        var content = await assets.ListScopeAsync(rootId, relative, recursive, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        return new(rootId, relative, recursive, listing.Status,
            listing.Entries.Where(entry => entry.IsDirectory).ToArray(), content, listing.Diagnostic);
    }
}

public interface IBrowserFolderSession : IBrowserCatalogSession
{
    Task<MediaFolderEnumerationResult> ReadBrowserFoldersAsync(Guid rootId, string folder, CancellationToken token = default);
    Task<BrowserFolderSnapshot> ReadBrowserFolderAsync(Guid rootId, string folder, CancellationToken token = default);
    Task<bool> IsBrowserSourceAvailableAsync(Guid rootId, Guid assetId, CancellationToken token = default);
}

/// <summary>Visited-folder discovery request; expanding Locations never invokes ingestion.</summary>
public interface IBrowserDiscoverySession : IBrowserFolderSession
{
    Task SetBrowserRecursiveAsync(Guid rootId, string folder, bool enabled, CancellationToken token = default);
    Task<BrowserFolderSnapshot> DiscoverBrowserFolderAsync(string? absolutePath, Guid rootId,
        string folder, CancellationToken token = default);
}
