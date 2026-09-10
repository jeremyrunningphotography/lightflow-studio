using Xunit;

namespace LightflowStudio.Tests;

public sealed partial class PlayerViewerHostLeaseTests
{
    [Fact]
    public async Task WorkspacePlayer_CaptureWhilePlayingReopensAtDisplayedPositionPaused()
    {
        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();
            var backend = new FakeBackend();
            await using var coordinator = new MediaPlaybackCoordinator(() => new MediaPlaybackService(backend));
            var first = new PlayerViewerHost(coordinator);
            var asset = new PlayerViewerAsset(Guid.NewGuid(), "clip.mp4", "key", "clip.mp4", MediaPresentationKind.Video, Guid.NewGuid());
            var resolved = new MediaPathResolution(asset.RootId, asset.RelativePath, asset.Key, Path.GetFullPath("clip.mp4"), MediaRootAvailability.Online, true);
            await first.OpenAsync(asset, resolved);
            first.PlayPauseButton.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            await WaitUntilAsync(() => backend.PlayCallCount == 1, "play before closing");
            backend.Present(7.25);
            var saved = first.CaptureWorkspaceState()!;
            Assert.Equal(TimeSpan.FromSeconds(7.25), saved.Position);
            await first.CloseAsync();
            var second = new PlayerViewerHost(coordinator);
            await second.OpenAsync(asset, resolved, continuation: saved);
            try
            {
                Assert.Equal(1, backend.PlayCallCount); // The relaunch issued no additional Play.
                Assert.Equal(saved.Position, second.CaptureWorkspaceState()!.Position);
                Assert.Equal("Play", second.PlayPauseButton.Content);
            }
            finally { await second.CloseAsync(); }
        });
    }

    [Fact]
    public async Task WorkspacePlayer_SubclipSelectionDoesNotReplaceSavedPlayheadOrWriteCatalogIntent()
    {
        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();
            var backend = new FakeBackend();
            await using var coordinator = new MediaPlaybackCoordinator(() => new MediaPlaybackService(backend));
            var ranges = new FakeRangeStore(null);
            var clips = new FakeSubclipService();
            var asset = new PlayerViewerAsset(Guid.NewGuid(), "clip.mp4", "key", "clip.mp4", MediaPresentationKind.Video, Guid.NewGuid());
            var clipId = Guid.NewGuid(); var now = DateTimeOffset.UtcNow;
            clips.Items.Add(new(clipId, asset.AssetId!.Value, "Take", 0, TimeSpan.FromSeconds(3),
                TimeSpan.FromSeconds(25), TimeSpan.FromSeconds(60), 1, now, now));
            var host = new PlayerViewerHost(coordinator, ranges, clips);
            // Let WPF establish the existing ItemsSource binding; these fake repositories complete synchronously.
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.DataBind);
            await host.OpenAsync(asset, new(asset.RootId, asset.RelativePath, asset.Key, Path.GetFullPath("clip.mp4"),
                MediaRootAvailability.Online, true), continuation: new()
                {
                    Asset = asset, Position = TimeSpan.FromSeconds(19.5), ActiveSubclipId = clipId,
                    SelectedSubclipIds = [clipId, Guid.NewGuid()]
                });
            try
            {
                Assert.Equal(new[] { TimeSpan.FromSeconds(19.5) }, backend.SeekPositions);
                Assert.Equal(clipId, host.ActiveSubclipId);
                Assert.Equal(new[] { clipId }, host.SelectedSubclipIds);
                Assert.Equal(0, ranges.SaveCount);
                Assert.Equal(0, clips.CreateCount);
            }
            finally { await host.CloseAsync(); }
        });
    }

    [Theory]
    [InlineData(null, 40, 0)]
    [InlineData(2d, 40, 40)]
    [InlineData(2d, 100000, 0)]
    public async Task WorkspacePlayer_FitAndZoomRestoreWithSourceBoundedPan(double? zoom, double pan, double expectedPan)
    {
        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();
            var backend = new FakeBackend();
            await using var coordinator = new MediaPlaybackCoordinator(() => new MediaPlaybackService(backend));
            var host = new PlayerViewerHost(coordinator);
            host.Measure(new System.Windows.Size(900, 700));
            host.Arrange(new System.Windows.Rect(0, 0, 900, 700));
            host.UpdateLayout();
            var asset = new PlayerViewerAsset(Guid.NewGuid(), "clip.mp4", "key", "clip.mp4", MediaPresentationKind.Video, Guid.NewGuid());
            await host.OpenAsync(asset, new(asset.RootId, asset.RelativePath, asset.Key, Path.GetFullPath("clip.mp4"),
                MediaRootAvailability.Online, true), continuation: new() { Asset = asset, PixelZoom = zoom, PanX = pan, PanY = pan });
            try
            {
                host.UpdateLayout();
                var saved = host.CaptureWorkspaceState()!;
                Assert.Equal(zoom, saved.PixelZoom);
                if (pan < 100000) Assert.Equal(expectedPan, saved.PanX, 3);
                else Assert.InRange(saved.PanX, 0, 1920);
                Assert.Equal(0, backend.PlayCallCount);
            }
            finally { await host.CloseAsync(); }
        });
    }

    [Fact]
    public async Task WorkspacePlayer_CancelAfterBackendOpenDoesNotPublishSurfaceAndReleasesLease()
    {
        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();
            var backend = new FakeBackend();
            await using var coordinator = new MediaPlaybackCoordinator(() => new MediaPlaybackService(backend));
            using var request = new WorkspaceRestorationRequest();
            var host = new PlayerViewerHost(coordinator, openMilestone: milestone =>
            {
                if (milestone == PlayerOpenMilestone.PlaybackBackendOpenCompleted) request.Cancel();
            });
            var asset = new PlayerViewerAsset(Guid.NewGuid(), "clip.mp4", "key", "clip.mp4", MediaPresentationKind.Video, Guid.NewGuid());
            var resolved = new MediaPathResolution(asset.RootId, asset.RelativePath, asset.Key, Path.GetFullPath("clip.mp4"), MediaRootAvailability.Online, true);
            await host.OpenAsync(asset, resolved, request.Token, new() { Asset = asset, Position = TimeSpan.FromSeconds(5) });
            Assert.DoesNotContain("presentation", backend.OpenPresentationOperations);
            Assert.Equal(0, backend.PlayCallCount);
            await host.CloseAsync();
            var next = new PlayerViewerHost(coordinator);
            await next.OpenAsync(asset, resolved);
            try { Assert.True(next.PositionSlider.IsEnabled); }
            finally { await next.CloseAsync(); }
        });
    }

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
