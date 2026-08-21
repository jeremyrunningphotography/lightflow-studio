using System.Windows;
using Xunit;

namespace LightflowStudio.Tests;

/// <summary>
/// #110: MediaPlaybackCoordinator allows only one active session at a time — a different consumer (e.g.
/// TrimEditorWindow) acquiring the lease while PlayerViewerHost's video is still open forcibly closes that
/// source out from under it. These tests prove PlayerViewerHost notices (via its own StateChanged subscription
/// observing an externally-published Empty snapshot) and recovers — raising BackRequested and resetting its
/// asset — rather than silently leaving stale transport controls enabled over a source that no longer exists.
/// </summary>
[Collection("STA dispatcher tests")]
public sealed class PlayerViewerHostLeaseTests
{
    [Fact]
    public async Task AnotherConsumerAcquiringTheLease_RaisesBackRequestedAndClearsTheCurrentAsset()
    {
        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();
            var backend = new FakeBackend();
            await using var coordinator = new MediaPlaybackCoordinator(() => new MediaPlaybackService(backend));
            var host = new PlayerViewerHost(coordinator);
            var backRequestedCount = 0;
            host.BackRequested += (_, _) => backRequestedCount++;

            var asset = new PlayerViewerAsset(Guid.NewGuid(), "clip.mp4", "clip.mp4", "clip.mp4", MediaPresentationKind.Video);
            var resolution = new MediaPathResolution(asset.RootId, asset.RelativePath, asset.Key,
                Path.GetFullPath("clip.mp4"), MediaRootAvailability.Online, true);
            await host.OpenAsync(asset, resolution);
            Assert.Equal("clip.mp4", host.CurrentAsset?.Name);

            // A different owner acquiring the coordinator forcibly closes PlayerViewerHost's still-open source,
            // exactly like TrimEditorPlayback.OpenAsync would when the user opens Trim on an unrelated file.
            await using var intruderLease = await coordinator.AcquireAsync(Guid.NewGuid());

            await WaitUntilAsync(() => backRequestedCount > 0, "PlayerViewerHost to notice the stolen lease");
            Assert.Null(host.CurrentAsset);
        });
    }

    [Fact]
    public async Task InvalidSource_ReleasesTheLeaseBeforeThrowingSoSpaceCannotReachTheRejectedSession()
    {
        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();
            // A zero Duration is exactly what OpenVideoAsync treats as "could not be decoded for preview" —
            // the same outcome a corrupt/unsupported real file produces (MediaPlaybackService.OpenAsync
            // swallows a genuine backend failure into a Failed snapshot rather than throwing).
            var backend = new FakeBackend(duration: TimeSpan.Zero);
            await using var coordinator = new MediaPlaybackCoordinator(() => new MediaPlaybackService(backend));
            var host = new PlayerViewerHost(coordinator);
            // KeyEventArgs requires a real PresentationSource — a bare, never-shown UserControl has none, so
            // it needs a real (offscreen) Window, matching BrowserPlayerViewerLiveInteractionTests' pattern.
            var window = new Window
            {
                Content = host, WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -32000, Top = -32000, ShowInTaskbar = false, Width = 400, Height = 300
            };
            window.Show();
            try
            {
                var asset = new PlayerViewerAsset(Guid.NewGuid(), "broken.mp4", "broken.mp4", "broken.mp4", MediaPresentationKind.Video);
                var resolution = new MediaPathResolution(asset.RootId, asset.RelativePath, asset.Key,
                    Path.GetFullPath("broken.mp4"), MediaRootAvailability.Online, true);
                await host.OpenAsync(asset, resolution);

                // Before the fix, _service stayed assigned to the rejected session, so Space's
                // "_service is not null" guard alone would still invoke Play/Pause on it despite the failure.
                Space(host, window);
                Assert.Equal(0, backend.PlayCallCount);
                Assert.Equal(0, backend.PauseCallCount);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public async Task VideoOnlySource_LeavesVolumeAndMuteControlsDisabledButKeepsTheRestOfTransportUsable()
    {
        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();
            var backend = new FakeBackend(hasAudio: false);
            await using var coordinator = new MediaPlaybackCoordinator(() => new MediaPlaybackService(backend));
            var host = new PlayerViewerHost(coordinator);

            var asset = new PlayerViewerAsset(Guid.NewGuid(), "silent.mp4", "silent.mp4", "silent.mp4", MediaPresentationKind.Video);
            var resolution = new MediaPathResolution(asset.RootId, asset.RelativePath, asset.Key,
                Path.GetFullPath("silent.mp4"), MediaRootAvailability.Online, true);
            await host.OpenAsync(asset, resolution);

            Assert.False(host.MuteButton.IsEnabled);
            Assert.False(host.VolumeSlider.IsEnabled);
            Assert.True(host.PlayPauseButton.IsEnabled);
        });
    }

    [Fact]
    public async Task SourceWithAudio_EnablesVolumeAndMuteAndClickingMuteTogglesTheSharedSession()
    {
        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();
            var backend = new FakeBackend(hasAudio: true);
            await using var coordinator = new MediaPlaybackCoordinator(() => new MediaPlaybackService(backend));
            var host = new PlayerViewerHost(coordinator);

            var asset = new PlayerViewerAsset(Guid.NewGuid(), "clip.mp4", "clip.mp4", "clip.mp4", MediaPresentationKind.Video);
            var resolution = new MediaPathResolution(asset.RootId, asset.RelativePath, asset.Key,
                Path.GetFullPath("clip.mp4"), MediaRootAvailability.Online, true);
            await host.OpenAsync(asset, resolution);

            Assert.True(host.MuteButton.IsEnabled);
            Assert.True(host.VolumeSlider.IsEnabled);
            Assert.Equal(100, host.VolumeSlider.Value);
            Assert.False(backend.Mute);

            host.MuteButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Assert.True(backend.Mute);

            host.MuteButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Assert.False(backend.Mute);

            host.VolumeSlider.Value = 40;
            Assert.Equal(40, backend.Volume);
        });
    }

    [Fact]
    public async Task PreviousFrame_FreezesTheSurfaceOnASnapshotWhileReconstructingAndUnfreezesOnceSettled()
    {
        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();
            // Long enough that the assertions below can observe the frozen state mid-step without a race
            // against the fake's own completion.
            var backend = new FakeBackend(stepBackwardDelay: TimeSpan.FromMilliseconds(200));
            await using var coordinator = new MediaPlaybackCoordinator(() => new MediaPlaybackService(backend));
            var host = new PlayerViewerHost(coordinator);

            var asset = new PlayerViewerAsset(Guid.NewGuid(), "clip.mp4", "clip.mp4", "clip.mp4", MediaPresentationKind.Video);
            var resolution = new MediaPathResolution(asset.RootId, asset.RelativePath, asset.Key,
                Path.GetFullPath("clip.mp4"), MediaRootAvailability.Online, true);
            await host.OpenAsync(asset, resolution);

            Assert.Equal(Visibility.Collapsed, host.FreezeOverlay.Visibility);

            host.PreviousFrameButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

            await WaitUntilAsync(() => host.FreezeOverlay.Visibility == Visibility.Visible,
                "the freeze overlay to appear before backward reconstruction settles");
            Assert.Equal(1, backend.SnapshotCallCount);
            Assert.NotNull(host.FreezeOverlay.Source);

            await WaitUntilAsync(() => host.FreezeOverlay.Visibility == Visibility.Collapsed,
                "the freeze overlay to disappear once the step settles");
            Assert.Null(host.FreezeOverlay.Source);
        });
    }

    [Fact]
    public async Task NextFrame_NeverFreezesTheSurface()
    {
        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();
            var backend = new FakeBackend();
            await using var coordinator = new MediaPlaybackCoordinator(() => new MediaPlaybackService(backend));
            var host = new PlayerViewerHost(coordinator);

            var asset = new PlayerViewerAsset(Guid.NewGuid(), "clip.mp4", "clip.mp4", "clip.mp4", MediaPresentationKind.Video);
            var resolution = new MediaPathResolution(asset.RootId, asset.RelativePath, asset.Key,
                Path.GetFullPath("clip.mp4"), MediaRootAvailability.Online, true);
            await host.OpenAsync(asset, resolution);

            host.NextFrameButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            await Task.Delay(100); // the fake's forward step is instant; this only needs to outlast its own dispatch

            Assert.Equal(0, backend.SnapshotCallCount);
            Assert.Equal(Visibility.Collapsed, host.FreezeOverlay.Visibility);
        });
    }

    private static void Space(PlayerViewerHost host, Window window) => host.RaiseEvent(
        new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), 0, System.Windows.Input.Key.Space)
        { RoutedEvent = UIElement.PreviewKeyDownEvent });

    private static async Task WaitUntilAsync(Func<bool> condition, string waitingFor, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException($"Timed out waiting for {waitingFor}.");
            await Task.Delay(10);
        }
    }

    private sealed class FakeBackend(TimeSpan? duration = null, bool hasAudio = false, TimeSpan? stepBackwardDelay = null) : IMediaPlaybackBackend
    {
        public int PlayCallCount { get; private set; }
        public int PauseCallCount { get; private set; }
        public int SnapshotCallCount { get; private set; }
        public event EventHandler<MediaPresentationTimestamp>? FramePresented { add { } remove { } }
        public event EventHandler<MediaPlaybackError>? Failed { add { } remove { } }
        public int Volume { get; set; } = 100;
        public bool Mute { get; set; }
        public FrameworkElement CreatePresentationSurface() => new();
        public void ReleasePresentationSurface(FrameworkElement surface) { }
        public void CancelPending() { }
        public Task<PlaybackBackendOpened> OpenAsync(string sourcePath, CancellationToken token)
        {
            var audioStreams = hasAudio ? new[] { new MediaAudioStreamInfo(0, null, null, 2, true) } : [];
            var source = new MediaPlaybackSourceInfo(sourcePath, duration ?? TimeSpan.FromSeconds(60), TimeSpan.Zero, 1920, 1080, audioStreams, hasAudio ? 0 : null, false);
            return Task.FromResult(new PlaybackBackendOpened(source, new(TimeSpan.Zero)));
        }
        public Task CloseAsync(CancellationToken token) => Task.CompletedTask;
        public Task PlayAsync(CancellationToken token) { PlayCallCount++; return Task.CompletedTask; }
        public Task PauseAsync(CancellationToken token) { PauseCallCount++; return Task.CompletedTask; }
        public Task<MediaPresentationTimestamp> SeekAsync(TimeSpan position, CancellationToken token) =>
            Task.FromResult(new MediaPresentationTimestamp(position));
        public Task<MediaPresentationTimestamp> StepForwardAsync(CancellationToken token) => Task.FromResult(new MediaPresentationTimestamp(TimeSpan.Zero));
        public async Task<MediaPresentationTimestamp> StepBackwardAsync(CancellationToken token)
        {
            if (stepBackwardDelay is { } delay) await Task.Delay(delay, token);
            return new MediaPresentationTimestamp(TimeSpan.Zero);
        }
        public Task<MediaDecodedFrame> SnapshotCurrentFrameAsync(CancellationToken token)
        {
            SnapshotCallCount++;
            return Task.FromResult(new MediaDecodedFrame(new(TimeSpan.Zero), 1, 1, 4, [0, 0, 0, 255]));
        }
        public Task<MediaDecodedFrame> GetFrameAsync(TimeSpan position, CancellationToken token) => throw new NotSupportedException();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
