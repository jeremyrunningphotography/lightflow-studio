using System.IO;

namespace LightflowStudio;

internal static class BrowserCatalogScope
{
    public static IReadOnlyList<MediaFolderEntry> Entries(IReadOnlyList<MediaAsset> assets, IMediaTypeRegistry types) =>
        assets.Where(asset => asset.SourceStatus != MediaAssetSourceStatus.Missing)
            .Select(asset => new MediaFolderEntry(asset.RootId, asset.RelativePath, asset.RelativePathKey,
                Path.GetFileName(asset.RelativePath), false, types.Classify(new(Path.GetFileName(asset.RelativePath))),
                asset.FileSizeBytes, new DateTimeOffset(asset.LastWriteUtcTicks, TimeSpan.Zero), AssetId: asset.AssetId))
            .Where(BrowserGridModel.IsPresentable).ToArray();
}
