namespace LightflowStudio;

internal static class BrowserPreviewReuse
{
    // Failed replacement retains the prior artifact; its warning remains independently visible.
    public static bool HasRetainedThumbnail(PreviewRecord preview, IPreviewStoreService store)
    {
        if (preview.ThumbnailRelativePath is null) return false;
        if (preview.ThumbnailState == PreviewComponentState.Current) return true;
        if (preview.ThumbnailState != PreviewComponentState.Failed ||
            preview.ThumbnailGeneratorVersion != ThumbnailGenerationService.CurrentGeneratorVersion) return false;
        // A failed attempt after source/Color/version invalidation must not revive an obsolete artifact.
        var expected = store.GetArtifactPath(preview.AssetId, PreviewArtifactKind.Thumbnail,
            ThumbnailGenerationService.CurrentGeneratorVersion, preview.Source, "jpg",
            preview.ThumbnailVisualIdentity ?? PreviewVisualIdentity.Original);
        return string.Equals(System.IO.Path.GetFileName(expected),
            System.IO.Path.GetFileName(preview.ThumbnailRelativePath), StringComparison.OrdinalIgnoreCase);
    }

    public static bool Matches(MediaAsset source, PreviewRecord preview) =>
        source.AssetId == preview.AssetId &&
        source.SourceStatus == MediaAssetSourceStatus.Available && source.Fingerprint is { } fingerprint &&
        preview.Source.FileSizeBytes == source.FileSizeBytes &&
        preview.Source.LastWriteUtcTicks == source.LastWriteUtcTicks &&
        preview.Source.FingerprintVersion == fingerprint.Version && preview.Source.Fingerprint == fingerprint.Value;
}
