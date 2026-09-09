using Xunit;

namespace LightflowStudio.Tests;

public sealed partial class PlayerViewerHostLeaseTests
{
    [Theory]
    [InlineData(19.5, 19.5)]
    [InlineData(120, 60)]
    public async Task WorkspacePlayer_SeeksOnceBeforePresentationAndNeverAutoplays(double savedSeconds, double expectedSeconds)
    {
        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();
            var backend = new FakeBackend(hasAudio: true);
            await using var coordinator = new MediaPlaybackCoordinator(() => new MediaPlaybackService(backend));
            var host = new PlayerViewerHost(coordinator, new FakeRangeStore(new(TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(3), null)));
            var asset = new PlayerViewerAsset(Guid.NewGuid(), "clip.mp4", "key", "clip.mp4", MediaPresentationKind.Video, Guid.NewGuid());
            var state = new WorkspacePlayerState
            {
                Asset = asset, Position = TimeSpan.FromSeconds(savedSeconds), Loop = true,
                PixelZoom = 2, Speed = 0.5, Cadence = new(24000, 1001), Volume = 35, Muted = true
            };
            await host.OpenAsync(asset, new(asset.RootId, asset.RelativePath, asset.Key, Path.GetFullPath("clip.mp4"),
                MediaRootAvailability.Online, true), continuation: state);
            try
            {
                Assert.Equal(new[] { TimeSpan.FromSeconds(expectedSeconds) }, backend.SeekPositions);
                Assert.Equal(0, backend.PlayCallCount);
                Assert.True(backend.OpenPresentationOperations.IndexOf("seek") < backend.OpenPresentationOperations.IndexOf("presentation"));
                var actual = host.CaptureWorkspaceState()!;
                Assert.Equal(state.Asset, actual.Asset);
                Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), actual.Position);
                Assert.Equal(state.Cadence, actual.Cadence);
                Assert.Equal(0.5, actual.Speed);
                Assert.True(actual.Loop);
                Assert.Equal(35, actual.Volume);
                Assert.True(actual.Muted);
                Assert.False(host.IsFullscreen);
            }
            finally { await host.CloseAsync(); }
        });
    }

    [Fact]
    public async Task WorkspacePlayer_InvalidSourceCadenceFallsBackToSource()
    {
        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();
            var backend = new FakeBackend();
            await using var coordinator = new MediaPlaybackCoordinator(() => new MediaPlaybackService(backend));
            var host = new PlayerViewerHost(coordinator);
            var asset = new PlayerViewerAsset(Guid.NewGuid(), "clip.mp4", "key", "clip.mp4", MediaPresentationKind.Video, Guid.NewGuid());
            await host.OpenAsync(asset, new(asset.RootId, asset.RelativePath, asset.Key, Path.GetFullPath("clip.mp4"),
                MediaRootAvailability.Online, true), continuation: new() { Asset = asset, Cadence = new(240, 1) });
            try { Assert.Null(host.CaptureWorkspaceState()!.Cadence); Assert.Equal(1, backend.Options.FrameDivisor); }
            finally { await host.CloseAsync(); }
        });
    }

    [Fact]
    public async Task WorkspacePlayer_CanceledRequestCannotAssignSourceOrAcquirePlayback()
    {
        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();
            var backend = new FakeBackend();
            await using var coordinator = new MediaPlaybackCoordinator(() => new MediaPlaybackService(backend));
            var host = new PlayerViewerHost(coordinator);
            var asset = new PlayerViewerAsset(Guid.NewGuid(), "clip.mp4", "key", "clip.mp4", MediaPresentationKind.Video, Guid.NewGuid());
            using var request = new WorkspaceRestorationRequest();
            request.Cancel();
            await host.OpenAsync(asset, new(asset.RootId, asset.RelativePath, asset.Key, Path.GetFullPath("clip.mp4"),
                MediaRootAvailability.Online, true), request.Token, new() { Asset = asset });
            Assert.Null(host.CurrentAsset);
            Assert.DoesNotContain("open", backend.OpenPresentationOperations);
        });
    }
}
