using LightflowStudio;
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

            await storage.AssetCopies.CloneAsync(source.AssetId, copy);

            var states = await storage.BrowserAssetStates.GetQueryStatesAsync([source.AssetId, copy.AssetId]);
            Assert.Equal(4, states[copy.AssetId].Classification!.Rating);
            Assert.Equal(["client", "select"], states[copy.AssetId].Classification!.Keywords);
            var copiedPreview = await storage.Previews.GetAsync(copy.AssetId);
            Assert.Equal("{\"codec\":\"hevc\"}", copiedPreview!.MetadataJson);
            Assert.Equal(PreviewComponentState.Missing, copiedPreview.ThumbnailState);

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
}
