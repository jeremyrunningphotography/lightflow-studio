namespace LightflowStudio;
public interface IMediaAssetService
{
    async Task<IReadOnlyList<MediaAsset>> ListScopeAsync(Guid rootId, string folder, bool recursive,
        CancellationToken cancellationToken = default) =>
        (await ListAsync(cancellationToken).ConfigureAwait(false)).Where(asset =>
            MediaAssetScope.Contains(asset, rootId, folder, recursive)).ToArray();
    Task<MediaAssetOperationResult> CreateAsync(Guid rootId, string relativePath, string mediaType = "unknown",
        CancellationToken cancellationToken = default);
    Task<MediaAssetResolution?> GetAsync(Guid assetId, CancellationToken cancellationToken = default);
    // Raw Catalog projection: omit absent IDs, retain missing sources, and perform no filesystem resolution.
    Task<IReadOnlyDictionary<Guid, MediaAsset>> GetManyAsync(IReadOnlyCollection<Guid> assetIds, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MediaAsset>> ListAsync(CancellationToken cancellationToken = default);
    Task<MediaAssetResolution?> FindAsync(Guid rootId, string relativePath, CancellationToken cancellationToken = default);
    Task<MediaAssetOperationResult> ObserveAsync(Guid assetId, CancellationToken cancellationToken = default);
    Task<int> MarkMissingAsync(IReadOnlyCollection<Guid> assetIds, CancellationToken cancellationToken = default);
    Task<MediaAssetOperationResult> RelocateAsync(Guid assetId, Guid rootId, string relativePath,
        CancellationToken cancellationToken = default) => Task.FromResult(new MediaAssetOperationResult(
            MediaAssetOperationStatus.Failed, Diagnostic: "Catalog relocation is not supported by this asset provider."));
}

public interface ISourceFingerprintService
{
    Task<SourceFingerprint> CreateAsync(string path, CancellationToken cancellationToken = default);
}
