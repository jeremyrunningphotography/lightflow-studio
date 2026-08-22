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
    public async Task SavedIn_OpensPausedOnThatAuthoritativeTimestamp()
    {
        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();
            var backend = new FakeBackend();
            await using var coordinator = new MediaPlaybackCoordinator(() => new MediaPlaybackService(backend));
            var assetId = Guid.NewGuid();
            var savedIn = TimeSpan.FromTicks(123_456_789);
            var store = new FakeRangeStore(new MediaRange(TimeSpan.FromSeconds(60), savedIn, TimeSpan.FromSeconds(40)));
            var host = new PlayerViewerHost(coordinator, store);
            var asset = new PlayerViewerAsset(Guid.NewGuid(), "clip.mp4", "clip.mp4", "clip.mp4",
                MediaPresentationKind.Video, assetId);
            var resolution = new MediaPathResolution(asset.RootId, asset.RelativePath, asset.Key,
                Path.GetFullPath("clip.mp4"), MediaRootAvailability.Online, true);

            await host.OpenAsync(asset, resolution);

            Assert.Equal(assetId, store.RestoredAssetId);
            Assert.Equal([savedIn], backend.SeekPositions);
            Assert.Equal(["seek", "presentation"], backend.OpenPresentationOperations);
            Assert.Equal(0, backend.PlayCallCount);
            Assert.Equal("Play", host.PlayPauseButton.Content);
            Assert.Equal(savedIn.TotalMilliseconds, host.PositionSlider.Value, 3);
            Assert.Equal("Active", host.SetInButton.Tag);
            Assert.Equal("Active", host.SetOutButton.Tag);
            Assert.True(host.SetInButton.IsEnabled);
            Assert.True(host.SetOutButton.IsEnabled);

            host.SetInButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            await WaitUntilAsync(() => store.SaveCount == 1, "active Set In replacement save");
            Assert.Equal("Active", host.SetInButton.Tag);
            Assert.True(host.SetInButton.IsEnabled);

            host.ClearInButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            await WaitUntilAsync(() => store.SaveCount == 2, "clear In save");
            Assert.Null(store.SavedRange?.In);
            Assert.Null(host.SetInButton.Tag);
            Assert.Equal("Active", host.SetOutButton.Tag);
        });
    }

    [Fact]
    public async Task RangeWithoutSavedIn_PreservesNormalOpeningPosition()
    {
        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();
            var backend = new FakeBackend();
            await using var coordinator = new MediaPlaybackCoordinator(() => new MediaPlaybackService(backend));
            var store = new FakeRangeStore(new MediaRange(TimeSpan.FromSeconds(60), Out: TimeSpan.FromSeconds(40)));
            var host = new PlayerViewerHost(coordinator, store);
            var asset = new PlayerViewerAsset(Guid.NewGuid(), "clip.mp4", "clip.mp4", "clip.mp4",
                MediaPresentationKind.Video, Guid.NewGuid());
            var resolution = new MediaPathResolution(asset.RootId, asset.RelativePath, asset.Key,
                Path.GetFullPath("clip.mp4"), MediaRootAvailability.Online, true);

            await host.OpenAsync(asset, resolution);

            Assert.Empty(backend.SeekPositions);
            Assert.Equal(["presentation"], backend.OpenPresentationOperations);
            Assert.Equal(0, backend.PlayCallCount);
            Assert.Equal(TimeSpan.Zero.TotalMilliseconds, host.PositionSlider.Value);
            Assert.Null(host.SetInButton.Tag);
            Assert.Equal("Active", host.SetOutButton.Tag);
        });
    }

    [Fact]
    public async Task PreviousFrame_RetainsCurrentFrameWhileNativeSurfaceReconstructsThenPublishesSettledFrame()
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

            host.PreviousFrameButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

            await WaitUntilAsync(() => backend.Operations.Count == 3, "Previous Frame presentation handoff");
            Assert.Equal(["capture", "backward", "capture"], backend.Operations);
            Assert.Equal(Visibility.Hidden, host.VideoHost.Visibility);
            Assert.Equal(Visibility.Visible, host.SteppedFrameSurface.Visibility);
            Assert.NotNull(host.SteppedFrameSurface.Source);

            host.PreviousFrameButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            await WaitUntilAsync(() => backend.Operations.Count == 5, "repeated Previous Frame presentation");
            Assert.Equal(["capture", "backward", "capture", "backward", "capture"], backend.Operations);
        });
    }

    [Fact]
    public async Task Screengrab_AfterPreviousFrameSavesRetainedDecodedFrameWithoutAnotherBackendCapture()
    {
        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();
            var backend = new FakeBackend();
            var screengrabs = new FakeScreengrabService();
            var folders = new FakeFolderLauncher();
            await using var coordinator = new MediaPlaybackCoordinator(() => new MediaPlaybackService(backend));
            var host = new PlayerViewerHost(coordinator, screengrabService: screengrabs, folderLauncher: folders);
            var asset = new PlayerViewerAsset(Guid.NewGuid(), "clip.mp4", "clip.mp4", "clip.mp4", MediaPresentationKind.Video);
            var resolution = new MediaPathResolution(asset.RootId, asset.RelativePath, asset.Key,
                Path.GetFullPath("clip.mp4"), MediaRootAvailability.Online, true);
            await host.OpenAsync(asset, resolution);

            host.PreviousFrameButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            await WaitUntilAsync(() => backend.Operations.Count == 3, "Previous Frame presentation handoff");
            var pauseCallsBeforeCapture = backend.PauseCallCount;
            host.ScreengrabButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            await WaitUntilAsync(() => screengrabs.SavedFrame is not null, "screengrab save");

            Assert.Equal(["capture", "backward", "capture"], backend.Operations);
            Assert.Equal(0, backend.PlayCallCount);
            Assert.Equal(pauseCallsBeforeCapture, backend.PauseCallCount);
            Assert.Equal((1, 1), (screengrabs.SavedFrame!.Width, screengrabs.SavedFrame.Height));
            Assert.Equal(Visibility.Collapsed, host.ScreengrabFeedbackText.Visibility);
            Assert.Equal(Visibility.Visible, host.ScreengrabSuccessButton.Visibility);

            host.ScreengrabSuccessButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Assert.Equal(Path.Combine(Path.GetTempPath(), "screengrabs"), folders.OpenedDirectory);
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

    private sealed class FakeBackend(TimeSpan? duration = null, bool hasAudio = false) : IMediaPlaybackBackend
    {
        public List<string> Operations { get; } = [];
        public List<string> OpenPresentationOperations { get; } = [];
        public List<TimeSpan> SeekPositions { get; } = [];
        public int PlayCallCount { get; private set; }
        public int PauseCallCount { get; private set; }
        public event EventHandler<MediaPresentationTimestamp>? FramePresented { add { } remove { } }
        public event EventHandler<MediaPlaybackError>? Failed { add { } remove { } }
        public int Volume { get; set; } = 100;
        public bool Mute { get; set; }
        public FrameworkElement CreatePresentationSurface()
        {
            OpenPresentationOperations.Add("presentation");
            return new();
        }
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
        public Task<MediaPresentationTimestamp> SeekAsync(TimeSpan position, CancellationToken token)
        {
            OpenPresentationOperations.Add("seek");
            SeekPositions.Add(position);
            return Task.FromResult(new MediaPresentationTimestamp(position));
        }
        public Task<MediaPresentationTimestamp> StepForwardAsync(CancellationToken token) => Task.FromResult(new MediaPresentationTimestamp(TimeSpan.Zero));
        public Task<MediaPresentationTimestamp> StepBackwardAsync(CancellationToken token)
        {
            Operations.Add("backward");
            return Task.FromResult(new MediaPresentationTimestamp(TimeSpan.Zero));
        }
        public Task<MediaDecodedFrame> GetFrameAsync(TimeSpan position, CancellationToken token) => throw new NotSupportedException();
        public Task<MediaDecodedFrame> CapturePresentedFrameAsync(CancellationToken token)
        {
            Operations.Add("capture");
            return Task.FromResult(new MediaDecodedFrame(new(TimeSpan.Zero), 1, 1, 4, [0, 0, 0, 255]));
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeRangeStore(MediaRange? restored) : IMediaRangeStore
    {
        public Guid? RestoredAssetId { get; private set; }
        public int SaveCount { get; private set; }
        public MediaRange? SavedRange { get; private set; }

        public Task<MediaRange?> RestoreAsync(Guid assetId, CancellationToken cancellationToken = default)
        {
            RestoredAssetId = assetId;
            return Task.FromResult(restored);
        }

        public Task SaveAsync(Guid assetId, MediaRange? range, CancellationToken cancellationToken = default)
        {
            SaveCount++;
            SavedRange = range;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeScreengrabService : IFrameScreengrabService
    {
        public MediaDecodedFrame? SavedFrame { get; private set; }

        public Task<FrameScreengrabResult> SaveAsync(string sourcePath, MediaDecodedFrame frame,
            CancellationToken cancellationToken = default)
        {
            SavedFrame = frame;
            return Task.FromResult(new FrameScreengrabResult(
                Path.Combine(Path.GetTempPath(), "screengrabs", "frame.png"),
                frame.Timestamp, frame.Width, frame.Height));
        }
    }

    private sealed class FakeFolderLauncher : IFolderLauncher
    {
        public string? OpenedDirectory { get; private set; }
        public void Open(string directory) => OpenedDirectory = directory;
    }
}
