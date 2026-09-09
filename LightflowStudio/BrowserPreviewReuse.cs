namespace LightflowStudio;

internal static class BrowserPreviewReuse
{
    public static bool Matches(MediaAsset source, PreviewRecord preview) =>
        source.AssetId == preview.AssetId &&
        source.SourceStatus == MediaAssetSourceStatus.Available && source.Fingerprint is { } fingerprint &&
        preview.Source.FileSizeBytes == source.FileSizeBytes &&
        preview.Source.LastWriteUtcTicks == source.LastWriteUtcTicks &&
        preview.Source.FingerprintVersion == fingerprint.Version && preview.Source.Fingerprint == fingerprint.Value;
}
