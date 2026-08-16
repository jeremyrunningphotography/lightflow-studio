using System.IO;

namespace LightflowStudio;

internal enum CatalogReconciliationStatus
{
    Succeeded,
    RootNotFound,
    RootUnavailable,
    FolderUnavailable,
    InvalidRequest,
    EnumerationFailed,
    Canceled,
    Failed
}

internal enum CatalogReconciliationItemStatus { New, Unchanged, Changed, Missing }

internal sealed record CatalogReconciliationItem(
    Guid AssetId,
    string RelativePath,
    CatalogReconciliationItemStatus Status);

internal sealed record CatalogReconciliationResult(
    CatalogReconciliationStatus Status,
    Guid RootId,
    string RelativeFolder,
    IReadOnlyList<CatalogReconciliationItem> Items,
    int UnsupportedCount = 0,
    string? Diagnostic = null)
{
    public bool Succeeded => Status == CatalogReconciliationStatus.Succeeded;
    public int NewCount => Items.Count(item => item.Status == CatalogReconciliationItemStatus.New);
    public int UnchangedCount => Items.Count(item => item.Status == CatalogReconciliationItemStatus.Unchanged);
    public int ChangedCount => Items.Count(item => item.Status == CatalogReconciliationItemStatus.Changed);
    public int MissingCount => Items.Count(item => item.Status == CatalogReconciliationItemStatus.Missing);
}

internal interface ICatalogReconciliationService
{
    Task<CatalogReconciliationResult> ReconcileAsync(MediaFolderEnumerationRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Explicit, non-recursive Catalog refresh. Enumeration must finish authoritatively before any missing-state
/// inference is allowed. Visible-file observations may be retained after a later cancellation, but unseen assets
/// are never marked missing unless every enumerated supported source was reconciled successfully.
/// </summary>
internal sealed class CatalogReconciliationService(
    IMediaFolderEnumerator folders,
    IMediaAssetService assets) : ICatalogReconciliationService
{
    public async Task<CatalogReconciliationResult> ReconcileAsync(MediaFolderEnumerationRequest request,
        CancellationToken cancellationToken = default)
    {
        var folder = request.RelativeFolder?.Trim() ?? string.Empty;
        MediaFolderEnumerationResult enumeration;
        try
        {
            enumeration = await folders.EnumerateAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return Result(CatalogReconciliationStatus.Canceled, request, folder,
                "Catalog refresh was canceled before folder enumeration completed.");
        }

        if (!enumeration.Succeeded)
            return Result(Map(enumeration.Status), request, folder, enumeration.Diagnostic);
        folder = enumeration.RelativeFolder;

        var supported = enumeration.Entries
            .Where(entry => !entry.IsDirectory && entry.MediaType.IsKnown)
            .ToArray();
        var unsupportedCount = enumeration.Entries.Count(entry => !entry.IsDirectory && !entry.MediaType.IsKnown);
        IReadOnlyList<MediaAsset> existing;
        try
        {
            existing = (await assets.ListAsync(cancellationToken).ConfigureAwait(false))
                .Where(asset => asset.RootId == request.RootId && IsDirectChild(asset.RelativePath, folder))
                .ToArray();
        }
        catch (OperationCanceledException)
        {
            return Result(CatalogReconciliationStatus.Canceled, request, folder,
                "Catalog refresh was canceled before reconciliation began.");
        }
        catch (Exception exception)
        {
            return Result(CatalogReconciliationStatus.Failed, request, folder,
                $"Catalog assets could not be read: {exception.Message}");
        }

        var byPath = existing.ToDictionary(asset => asset.RelativePathKey, StringComparer.Ordinal);
        var seen = enumeration.Entries
            .Where(entry => !entry.IsDirectory)
            .Select(entry => entry.RelativePathKey)
            .ToHashSet(StringComparer.Ordinal);
        var changes = new List<CatalogReconciliationItem>();

        try
        {
            foreach (var entry in supported)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!byPath.TryGetValue(entry.RelativePathKey, out var prior))
                {
                    var created = await assets.CreateAsync(request.RootId, entry.RelativePath,
                        MediaTypeName(entry.MediaType.Category), cancellationToken).ConfigureAwait(false);
                    if (!created.Succeeded || created.Asset is null)
                        return Failed(request, folder, changes, unsupportedCount, created.Diagnostic);
                    changes.Add(new(created.Asset.Asset.AssetId, created.Asset.Asset.RelativePath,
                        CatalogReconciliationItemStatus.New));
                    continue;
                }

                var observed = await assets.ObserveAsync(prior.AssetId, cancellationToken).ConfigureAwait(false);
                if (!observed.Succeeded || observed.Asset is null)
                    return Failed(request, folder, changes, unsupportedCount, observed.Diagnostic);
                var current = observed.Asset.Asset;
                var status = prior.SourceStatus == MediaAssetSourceStatus.Available &&
                    prior.FileSizeBytes == current.FileSizeBytes &&
                    prior.LastWriteUtcTicks == current.LastWriteUtcTicks &&
                    prior.Fingerprint == current.Fingerprint
                        ? CatalogReconciliationItemStatus.Unchanged
                        : CatalogReconciliationItemStatus.Changed;
                changes.Add(new(prior.AssetId, observed.Asset.Asset.RelativePath,
                    status));
            }

            var missing = existing.Where(asset => !seen.Contains(asset.RelativePathKey)).ToArray();
            cancellationToken.ThrowIfCancellationRequested();
            var updated = await assets.MarkMissingAsync(missing.Select(asset => asset.AssetId).ToArray(),
                cancellationToken).ConfigureAwait(false);
            if (updated != missing.Length)
                return Failed(request, folder, changes, unsupportedCount,
                    "Catalog refresh could not update every missing source atomically.");
            changes.AddRange(missing.Select(asset => new CatalogReconciliationItem(
                asset.AssetId, asset.RelativePath, CatalogReconciliationItemStatus.Missing)));
        }
        catch (OperationCanceledException)
        {
            return new(CatalogReconciliationStatus.Canceled, request.RootId, folder, changes,
                unsupportedCount, "Catalog refresh was canceled; unseen assets were not inferred to be missing.");
        }
        catch (Exception exception)
        {
            return Failed(request, folder, changes, unsupportedCount, exception.Message);
        }

        return new(CatalogReconciliationStatus.Succeeded, request.RootId, folder, changes,
            unsupportedCount);
    }

    private static bool IsDirectChild(string relativePath, string folder)
    {
        var parent = Path.GetDirectoryName(relativePath.Replace('/', Path.DirectorySeparatorChar)) ?? string.Empty;
        var normalizedParent = string.IsNullOrEmpty(parent)
            ? string.Empty
            : MediaPathSemantics.NormalizeRelativePath(parent);
        return string.Equals(FolderKey(normalizedParent), FolderKey(folder), StringComparison.Ordinal);
    }

    private static string FolderKey(string folder) => string.IsNullOrEmpty(folder)
        ? string.Empty
        : MediaPathSemantics.RelativePathKey(folder);

    private static string MediaTypeName(MediaTypeCategory category) => category switch
    {
        MediaTypeCategory.StillImage => "image",
        MediaTypeCategory.RawImage => "raw-image",
        MediaTypeCategory.Video => "video",
        MediaTypeCategory.Audio => "audio",
        _ => "unknown"
    };

    private static CatalogReconciliationStatus Map(MediaFolderEnumerationStatus status) => status switch
    {
        MediaFolderEnumerationStatus.RootNotFound => CatalogReconciliationStatus.RootNotFound,
        MediaFolderEnumerationStatus.RootUnavailable => CatalogReconciliationStatus.RootUnavailable,
        MediaFolderEnumerationStatus.FolderNotFound or MediaFolderEnumerationStatus.FolderUnavailable or
            MediaFolderEnumerationStatus.AccessDenied or MediaFolderEnumerationStatus.LinkedPathRejected =>
            CatalogReconciliationStatus.FolderUnavailable,
        MediaFolderEnumerationStatus.InvalidPath => CatalogReconciliationStatus.InvalidRequest,
        _ => CatalogReconciliationStatus.EnumerationFailed
    };

    private static CatalogReconciliationResult Result(CatalogReconciliationStatus status,
        MediaFolderEnumerationRequest request, string folder, string? diagnostic) =>
        new(status, request.RootId, folder, [], Diagnostic: diagnostic);

    private static CatalogReconciliationResult Failed(MediaFolderEnumerationRequest request, string folder,
        IReadOnlyList<CatalogReconciliationItem> changes, int unsupportedCount, string? diagnostic) =>
        new(CatalogReconciliationStatus.Failed, request.RootId, folder, changes, unsupportedCount,
            diagnostic ?? "Catalog reconciliation failed.");
}
