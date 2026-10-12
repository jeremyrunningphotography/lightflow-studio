
namespace LightflowStudio;

internal interface IAssetCopyDataService
{
    Task CloneAsync(Guid sourceAssetId, MediaAsset destination, CancellationToken cancellationToken = default);
}

/// <summary>Clones AssetId-owned durable intent while keeping the copied asset independently mutable.</summary>
internal sealed class AssetCopyDataService(Func<CatalogDatabaseSession?> session, IPreviewStoreService? previews)
    : IAssetCopyDataService
{
    public async Task CloneAsync(Guid sourceAssetId, MediaAsset destination,
        CancellationToken cancellationToken = default)
    {
        await (session() ?? throw new InvalidOperationException("The Catalog is unavailable.")).Mutations.RunAsync(async () => {
        await new CatalogAssetCopyStore(session).CloneCatalogAsync(sourceAssetId, destination.AssetId, cancellationToken)
            .ConfigureAwait(false);
        if (previews is null) return;
        var source = await previews.GetAsync(sourceAssetId, cancellationToken).ConfigureAwait(false);
        if (source?.MetadataProbeVersion is not { } version ||
            source.MetadataJson is null && source.RawMetadataJson is null)
            return;
        var identity = new PreviewSourceIdentity(destination.FileSizeBytes, destination.LastWriteUtcTicks,
            destination.Fingerprint?.Version ?? source.Source.FingerprintVersion,
            destination.Fingerprint?.Value ?? source.Source.Fingerprint);
        await previews.ObserveSourceAsync(destination.AssetId, identity, cancellationToken).ConfigureAwait(false);
        await previews.SetMetadataAsync(destination.AssetId, new(version, source.MetadataState,
            PayloadJson: source.MetadataJson, RawPayloadJson: source.RawMetadataJson), cancellationToken)
            .ConfigureAwait(false);
    }, cancellationToken);
    }

}
