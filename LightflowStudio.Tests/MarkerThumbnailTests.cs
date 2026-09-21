using System.Windows.Media;
using System.Windows.Media.Imaging;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class MarkerThumbnailTests
{
    [Fact]
    public async Task PositionFrames_ReuseExactMarkerCacheOfflineAndRebuildAfterPreviewClear()
    {
        var root = Path.Combine(Path.GetTempPath(), "position-frames-" + Guid.NewGuid());
        try
        {
            var now = DateTimeOffset.UtcNow;
            var source = new MediaAsset(Guid.NewGuid(), Guid.NewGuid(), "clip.mov", "CLIP.MOV", "video", 10, 20,
                new(1, "original"), MediaAssetSourceStatus.Available, now, now, now);
            var assets = new Assets(source); var renderer = new Renderer();
            using var operations = new PreviewOperationCoordinator();
            using var frames = new PositionFrameService(assets, () => LightflowStorageLocations.Create(root), renderer, operations);
            using var markers = new MarkerThumbnailService(assets, () => LightflowStorageLocations.Create(root), renderer, operations);
            var position = TimeSpan.FromTicks(123456789);
            var prepared = await frames.PrepareAsync(source.AssetId, default);
            var path = await frames.GetAsync(prepared, position, default);
            Assert.Equal(path, await markers.GetAsync(new(Guid.NewGuid(), source.AssetId, position, "", 1, now, now), default));
            Assert.Single(renderer.Positions);
            Assert.Equal(position, renderer.Positions[0]);
            var offline = prepared! with { SourceExists = false, PhysicalPath = null, RootAvailability = MediaRootAvailability.Unavailable };
            Assert.Equal(path, await frames.GetAsync(offline, position, default));
            Assert.Null(await frames.GetAsync(offline, position + TimeSpan.FromTicks(1), default));
            Assert.Single(renderer.Positions);
            using (await operations.EnterMaintenanceAsync()) File.Delete(path!);
            Assert.Equal(path, await frames.GetAsync(prepared, position, default));
            Assert.Equal(2, renderer.Positions.Count);
            Assert.All(Directory.GetFiles(root, "*", SearchOption.AllDirectories), file => Assert.EndsWith(".jpg", file));
            Assert.NotEqual(PositionFrameService.Identity(source, position), PositionFrameService.Identity(source, position + TimeSpan.FromTicks(1)));
            Assert.NotEqual(PositionFrameService.Identity(source, position), PositionFrameService.Identity(source with { LastWriteUtcTicks = 21 }, position));
            using var canceled = new CancellationTokenSource(); canceled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => frames.GetAsync(prepared, position, canceled.Token));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task PositionFrames_CancellationRemovesTemporaryOutputAndReleasesMaintenance()
    {
        var root = Path.Combine(Path.GetTempPath(), "position-cancel-" + Guid.NewGuid());
        try
        {
            var now = DateTimeOffset.UtcNow;
            var source = new MediaAsset(Guid.NewGuid(), Guid.NewGuid(), "clip.mov", "CLIP.MOV", "video", 10, 20,
                new(1, "original"), MediaAssetSourceStatus.Available, now, now, now);
            var assets = new Assets(source); var renderer = new Renderer();
            using var operations = new PreviewOperationCoordinator();
            using var frames = new PositionFrameService(assets, () => LightflowStorageLocations.Create(root), renderer, operations);
            using var canceled = new CancellationTokenSource();
            renderer.AfterRender = canceled.Cancel;
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => frames.GetAsync(new(source, MediaRootAvailability.Online, "fixture.mov", true), TimeSpan.Zero, canceled.Token));
            using var maintenance = await operations.EnterMaintenanceAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Empty(Directory.GetFiles(root, "*", SearchOption.AllDirectories));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task MarkerFrames_AreExactRebuildableAndRejectChangedOrUnavailableSources()
    {
        var root = Path.Combine(Path.GetTempPath(), "marker-frames-" + Guid.NewGuid());
        try
        {
            var now = DateTimeOffset.UtcNow;
            var source = new MediaAsset(Guid.NewGuid(), Guid.NewGuid(), "clip.mov", "CLIP.MOV", "video", 10, 20,
                new(1, "original"), MediaAssetSourceStatus.Available, now, now, now);
            var marker = new TimelineMarker(Guid.NewGuid(), source.AssetId, TimeSpan.FromTicks(123456789), "", 1, now, now);
            var assets = new Assets(source); var renderer = new Renderer();
            using var cache = new MarkerThumbnailService(assets, () => LightflowStorageLocations.Create(root), renderer);
            var path = await cache.GetAsync(marker, default);
            Assert.NotNull(path);
            Assert.StartsWith(Path.GetFullPath(root), path);
            Assert.Equal(marker.Position, Assert.Single(renderer.Positions));
            Assert.Equal(path, await cache.GetAsync(marker with { Name = "Renamed", Revision = 2 }, default));
            Assert.Single(renderer.Positions);
            File.Delete(path!);
            Assert.Equal(path, await cache.GetAsync(marker, default));
            Assert.Equal(2, renderer.Positions.Count);
            assets.Available = false;
            Assert.Null(await cache.GetAsync(marker, default));
            assets.Available = true;
            renderer.AfterRender = () => assets.Source = source with { Fingerprint = new(1, "changed") };
            Assert.Null(await cache.GetAsync(marker with { Position = TimeSpan.FromTicks(123456790) }, default));
            Assert.Empty(Directory.GetFiles(root, "*.lightflow", SearchOption.AllDirectories));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    private sealed class Assets(MediaAsset source) : IMediaAssetService
    {
        public MediaAsset Source = source;
        public bool Available = true;
        public Task<MediaAssetOperationResult> ObserveAsync(Guid id, CancellationToken token = default) =>
            Task.FromResult(Available ? new MediaAssetOperationResult(MediaAssetOperationStatus.Succeeded,
                new(Source, MediaRootAvailability.Online, "fixture.mov", true)) : new(MediaAssetOperationStatus.SourceMissing));
        public Task<MediaAssetOperationResult> CreateAsync(Guid root, string path, string mediaType = "unknown", CancellationToken token = default) => throw new NotSupportedException();
        public Task<MediaAssetResolution?> GetAsync(Guid id, CancellationToken token = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<MediaAsset>> ListAsync(CancellationToken token = default) => throw new NotSupportedException();
        public Task<MediaAssetResolution?> FindAsync(Guid root, string path, CancellationToken token = default) => throw new NotSupportedException();
        public Task<int> MarkMissingAsync(IReadOnlyCollection<Guid> ids, CancellationToken token = default) => throw new NotSupportedException();
    }
    private sealed class Renderer : IThumbnailRenderer
    {
        public List<TimeSpan> Positions { get; } = [];
        public Action? AfterRender;
        public Task<ThumbnailRenderResult> RenderAsync(string source, string type, TimeSpan position, string destination, CancellationToken token = default)
        {
            Positions.Add(position);
            var bitmap = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgr24, null, new byte[] { 10, 20, 30 }, 3);
            var encoder = new JpegBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var file = File.Create(destination)) encoder.Save(file);
            AfterRender?.Invoke();
            return Task.FromResult(new ThumbnailRenderResult(ThumbnailGenerationStatus.Succeeded));
        }
    }
}
