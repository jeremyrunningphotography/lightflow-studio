namespace LightflowStudio;

internal sealed class DerivedWorkScheduler : SharedDerivedWorkScheduler
{
    public DerivedWorkScheduler(IMediaAssetService assets, IPreviewStoreService previews,
        IDerivedMediaMetadataService metadata, IThumbnailGenerationService thumbnails,
        int maximumConcurrency = 2, bool ownsGenerators = false,
        IPreviewOperationCoordinator? operations = null, IAssetColorStore? colors = null,
        Func<string,bool>? artifactExists = null, IPreferredPreviewFrameStore? preferredFrames = null,
        Func<string,bool>? excludedPath = null, TimeProvider? time = null)
        : base(assets,previews,metadata,thumbnails,maximumConcurrency,ownsGenerators,operations,
            colors is null ? null : new WindowsThumbnailColorSource(colors,null),artifactExists,preferredFrames,excludedPath,time) { }
}
