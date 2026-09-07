using LightflowStudio;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class AssetCopyDataTests : IDisposable
{
    private readonly string _temporary = Path.Combine(Path.GetTempPath(), $"lightflow-copy-{Guid.NewGuid():N}");

    [Fact]
    public async Task Clone_CreatesIndependentAssetOwnedStateAndInspectedMetadata()
    {
        var storage = (await LightflowStorageCoordinator.StartAsync(Path.Combine(_temporary, "app"))).Coordinator!;
        try
        {
            var media = Path.Combine(_temporary, "media"); Directory.CreateDirectory(media);
            var root = (await storage.MediaRoots.CreateAsync("Media", media)).Root!;
            await File.WriteAllTextAsync(Path.Combine(media, "source.mp4"), "source");
            await File.WriteAllTextAsync(Path.Combine(media, "copy.mp4"), "source");
            var source = (await storage.MediaAssets.CreateAsync(root.RootId, "source.mp4", "video")).Asset!.Asset;
            var copy = (await storage.MediaAssets.CreateAsync(root.RootId, "copy.mp4", "video")).Asset!.Asset;
            await storage.AssetClassifications.SaveAsync(new(source.AssetId, 4, AssetFlag.Picked, AssetColorLabel.Blue,
                ["client", "select"], 0));
            await storage.Previews!.ObserveSourceAsync(source.AssetId, new(source.FileSizeBytes, source.LastWriteUtcTicks,
                source.Fingerprint!.Version, source.Fingerprint.Value));
            await storage.Previews.SetMetadataAsync(source.AssetId, new(7, PreviewComponentState.Current,
                PayloadJson: "{\"codec\":\"hevc\"}", RawPayloadJson: "{\"streams\":[]}"));
            var preferred = await storage.PreferredPreviewFrames.SetAsync(source.AssetId,
                new(TimeSpan.FromSeconds(17)), TimeSpan.FromMinutes(1));

            await storage.AssetCopies.CloneAsync(source.AssetId, copy);

            var states = await storage.BrowserAssetStates.GetQueryStatesAsync([source.AssetId, copy.AssetId]);
            Assert.Equal(4, states[copy.AssetId].Classification!.Rating);
            Assert.Equal(["client", "select"], states[copy.AssetId].Classification!.Keywords);
            var copiedPreview = await storage.Previews.GetAsync(copy.AssetId);
            Assert.Equal("{\"codec\":\"hevc\"}", copiedPreview!.MetadataJson);
            Assert.Equal(PreviewComponentState.Missing, copiedPreview.ThumbnailState);
            var copiedPreferred = await storage.PreferredPreviewFrames.GetAsync(copy.AssetId);
            Assert.Equal(preferred.Position, copiedPreferred!.Position);
            Assert.Equal(preferred.Revision, copiedPreferred.Revision);
            var renderer = new CopyPositionRenderer();
            using (var thumbnails = new ThumbnailGenerationService(storage.MediaAssets, storage.Previews,
                       storage.Locations, renderer, preferredFrames: storage.PreferredPreviewFrames))
                Assert.Equal(ThumbnailGenerationStatus.Succeeded,
                    (await thumbnails.GenerateAsync(new(copy.AssetId, ForceRefresh: true))).Status);
            Assert.Equal(preferred.Position, renderer.Position);

            await storage.AssetClassifications.SaveAsync(states[source.AssetId].Classification! with { Rating = 1 });
            states = await storage.BrowserAssetStates.GetQueryStatesAsync([source.AssetId, copy.AssetId]);
            Assert.Equal(1, states[source.AssetId].Classification!.Rating);
            Assert.Equal(4, states[copy.AssetId].Classification!.Rating);
        }
        finally { await storage.DisposeAsync(); }
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_temporary)) Directory.Delete(_temporary, true); } catch { }
    }

    private sealed class CopyPositionRenderer : IThumbnailRenderer
    {
        public TimeSpan? Position { get; private set; }
        public Task<ThumbnailRenderResult> RenderAsync(string sourcePath, string mediaType, TimeSpan videoPosition,
            string destinationPath, CancellationToken cancellationToken = default)
        {
            Position = videoPosition;
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            var bitmap = BitmapSource.Create(8, 6, 96, 96, PixelFormats.Bgr24, null, new byte[8 * 6 * 3], 8 * 3);
            var encoder = new JpegBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(destinationPath); encoder.Save(stream);
            return Task.FromResult(new ThumbnailRenderResult(ThumbnailGenerationStatus.Succeeded));
        }
    }
}
