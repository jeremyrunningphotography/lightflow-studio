using System.Runtime.CompilerServices;

namespace LightflowStudio;

// No retry timer: subsequent demand can retry after this persisted deadline. Reads never extend it.
internal static class PreviewRetryPolicy
{
    internal static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(30);
    private sealed class Gates
    {
        public AssetPreviewGenerationGate Metadata { get; } = new();
        public AssetPreviewGenerationGate Thumbnail { get; } = new();
    }
    private static readonly ConditionalWeakTable<IPreviewStoreService, Gates> StoreGates = new();
    internal static AssetPreviewGenerationGate MetadataGate(IPreviewStoreService store) => StoreGates.GetOrCreateValue(store).Metadata;
    internal static AssetPreviewGenerationGate ThumbnailGate(IPreviewStoreService store) => StoreGates.GetOrCreateValue(store).Thumbnail;
    internal static bool MetadataDeferred(PreviewRecord preview, int version, DateTimeOffset now) =>
        preview.MetadataState == PreviewComponentState.Failed && preview.MetadataProbeVersion == version &&
        preview.MetadataRetryAfterUtc > now;
    internal static bool ThumbnailDeferred(PreviewRecord preview, string identity, DateTimeOffset now) =>
        preview.ThumbnailState == PreviewComponentState.Failed &&
        preview.ThumbnailGeneratorVersion == ThumbnailGenerationService.CurrentGeneratorVersion &&
        string.Equals(preview.ThumbnailVisualIdentity ?? PreviewVisualIdentity.Original, identity, StringComparison.Ordinal) &&
        preview.ThumbnailRetryAfterUtc > now;
}
