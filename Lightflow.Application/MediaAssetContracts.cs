namespace LightflowStudio;
public enum MediaAssetSourceStatus { Unknown, Available, Missing }
public enum MediaAssetOperationStatus { Succeeded, AlreadyExists, RootNotFound, RootUnavailable, SourceMissing, NotFound, Failed }

public sealed record SourceFingerprint(int Version, string Value);

public sealed record MediaAsset(
    Guid AssetId,
    Guid RootId,
    string RelativePath,
    string RelativePathKey,
    string MediaType,
    long FileSizeBytes,
    long LastWriteUtcTicks,
    SourceFingerprint? Fingerprint,
    MediaAssetSourceStatus SourceStatus,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc,
    DateTimeOffset? LastSeenUtc);

public sealed record MediaAssetResolution(
    MediaAsset Asset,
    MediaRootAvailability RootAvailability,
    string? PhysicalPath,
    bool SourceExists,
    string? Diagnostic = null);

public sealed record MediaAssetOperationResult(
    MediaAssetOperationStatus Status,
    MediaAssetResolution? Asset = null,
    string? Diagnostic = null)
{
    public bool Succeeded => Status == MediaAssetOperationStatus.Succeeded;
}

public interface IMediaAssetRepository
{
    async Task<IReadOnlyList<MediaAsset>> ListScopeAsync(Guid rootId, string folder, bool recursive,
        CancellationToken cancellationToken = default) =>
        (await ListAsync(cancellationToken).ConfigureAwait(false)).Where(asset =>
            MediaAssetScope.Contains(asset, rootId, folder, recursive)).ToArray();
    Task<MediaAsset?> GetAsync(Guid assetId, CancellationToken cancellationToken = default);
    // Raw Catalog projection: omit absent IDs, retain missing sources, and perform no filesystem resolution.
    Task<IReadOnlyDictionary<Guid, MediaAsset>> GetManyAsync(IReadOnlyCollection<Guid> assetIds, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MediaAsset>> ListAsync(CancellationToken cancellationToken = default);
    Task<MediaAsset?> FindAsync(Guid rootId, string relativePathKey, CancellationToken cancellationToken = default);
    Task<MediaAssetOperationStatus> CreateAsync(MediaAsset asset, CancellationToken cancellationToken = default);
    Task<bool> UpdateObservationAsync(Guid assetId, long size, long lastWriteUtcTicks,
        SourceFingerprint fingerprint, DateTimeOffset observedUtc, CancellationToken cancellationToken = default);
    Task<bool> MarkMissingAsync(Guid assetId, DateTimeOffset observedUtc, CancellationToken cancellationToken = default);
    Task<int> MarkMissingAsync(IReadOnlyCollection<Guid> assetIds, DateTimeOffset observedUtc,
        CancellationToken cancellationToken = default);
    Task<MediaAssetOperationStatus> RelocateAsync(Guid assetId, Guid rootId, string relativePath,
        DateTimeOffset observedUtc, CancellationToken cancellationToken = default);
}
