using System.Diagnostics;
using Xunit;

namespace LightflowStudio.Tests;

public sealed partial class FlyleafPlaybackIntegrationTests
{
    [Fact]
    public async Task ReviewReconfiguration_Diagnostic_PreservesSourceAndPlaybackState()
    {
        var dependencies = PlaybackDependencyLocator.FindSharedLibraries()!;
        var fixture = Path.Combine(_root, "interaction-latency.mkv");
        Run(Path.Combine(dependencies, "ffmpeg.exe"), "-hide_banner", "-loglevel", "error", "-y",
            "-f", "lavfi", "-i", "testsrc2=size=640x360:rate=60000/1001:duration=8",
            "-f", "lavfi", "-i", "sine=frequency=440:duration=8", "-c:v", "libopenh264", "-c:a", "pcm_s16le", fixture);
        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();
            await using var backend = new FlyleafPlaybackBackend(dependencies, () => new RecordingAudioOutput());
            await using var service = new MediaPlaybackService(backend);
            await service.OpenAsync(fixture);
            var source = service.SourceInfo;
            using var presentation = service.CreatePresentation();
            var window = new System.Windows.Window { Content = presentation.Surface, Width = 800, Height = 500,
                Left = -32000, ShowActivated = false, ShowInTaskbar = false };
            window.Show();
            try
            {
                await service.PlayAsync();
                await Task.Delay(300);
                foreach (var options in new[] { new PlaybackReviewOptions(2), new PlaybackReviewOptions(2, 1, new(25, 1)), new PlaybackReviewOptions() })
                {
                    var seeksBefore = backend.NativeSeekCount;
                    var timer = Stopwatch.StartNew();
                    await service.SetReviewOptionsAsync(options);
                    Console.WriteLine($"RECONFIG {options}: {timer.Elapsed.TotalMilliseconds:0}ms");
                    Assert.Equal(MediaPlaybackState.Playing, service.Snapshot.State);
                    Assert.Same(source, service.SourceInfo);
                    Assert.Equal(options == new PlaybackReviewOptions(2) ? 0 : 1, backend.NativeSeekCount - seeksBefore);
                }
                var seekCount = backend.NativeSeekCount;
                var seek = Stopwatch.StartNew();
                await service.SeekAsync(TimeSpan.FromSeconds(2));
                Console.WriteLine($"SEEK: {seek.Elapsed.TotalMilliseconds:0}ms");
                Assert.Equal(1, backend.NativeSeekCount - seekCount);
                var pause = Stopwatch.StartNew();
                await service.PauseAsync();
                Console.WriteLine($"PAUSE: {pause.Elapsed.TotalMilliseconds:0}ms");
            }
            finally { window.Content = null; window.Close(); }
        });
    }

    [Fact]
    public async Task NonDivisorCadence_SelectsRealSourceFramesWithoutChangingClock_AndPauseRestoresInspection()
    {
        var dependencies = PlaybackDependencyLocator.FindSharedLibraries()!;
        var fixture = Path.Combine(_root, "fractional-cadence.mkv");
        Run(Path.Combine(dependencies, "ffmpeg.exe"), "-hide_banner", "-loglevel", "error", "-y",
            "-f", "lavfi", "-i", "testsrc2=size=160x90:rate=60000/1001:duration=6", "-c:v", "ffv1", fixture);
        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();
            await using var backend = new FlyleafPlaybackBackend(dependencies);
            await using var service = new MediaPlaybackService(backend);
            await service.OpenAsync(fixture);
            using var presentation = service.CreatePresentation();
            var window = new System.Windows.Window { Content = presentation.Surface, Width = 320, Height = 200,
                Left = -32000, ShowActivated = false, ShowInTaskbar = false };
            window.Show();
            try
            {
                foreach (var speed in new[] { 0.5, 1, 2 })
                {
                    await service.SeekAsync(TimeSpan.Zero);
                    await service.SetReviewOptionsAsync(new(speed, 1, new MediaFrameRate(25, 1)));
                    var frames = new List<double>();
                    void Frame(object? sender, MediaPresentationTimestamp timestamp) => frames.Add(timestamp.Position.TotalSeconds);
                    service.FramePresented += Frame;
                    await service.PlayAsync();
                    var timer = Stopwatch.StartNew();
                    await Task.Delay(1200);
                    var elapsed = timer.Elapsed.TotalSeconds;
                    service.FramePresented -= Frame;
                    Assert.InRange(service.Snapshot.DisplayedTimestamp!.Position.TotalSeconds / elapsed, speed * 0.7, speed * 1.3);
                    var steps = frames.Zip(frames.Skip(1), (a, b) => b - a).Where(delta => delta > 0.002).ToArray();
                    Assert.Contains(steps, delta => delta > 0.045 && delta < 0.055);
                    Assert.All(steps, delta => Assert.InRange(delta, 0.032, 0.101));
                    await service.PauseAsync();
                    await service.SeekAsync(TimeSpan.FromSeconds(1));
                    var before = service.Snapshot.DisplayedTimestamp!.Position;
                    await service.StepForwardAsync();
                    Assert.InRange((service.Snapshot.DisplayedTimestamp!.Position - before).TotalSeconds, 0.015, 0.018);
                }
            }
            finally { window.Content = null; window.Close(); }
        });
    }

    [Fact]
    public async Task ZoomDropdown_ChangesActualNativeRendererViewport()
    {
        var dependencies = PlaybackDependencyLocator.FindSharedLibraries()!;
        var fixture = Path.Combine(_root, "zoom.mkv");
        Run(Path.Combine(dependencies, "ffmpeg.exe"), "-hide_banner", "-loglevel", "error", "-y",
            "-f", "lavfi", "-i", "testsrc2=size=1920x1440:rate=30:duration=3", "-c:v", "libopenh264", fixture);
        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();
            var backend = new FlyleafPlaybackBackend(dependencies);
            MediaPlaybackService? service = null;
            await using var coordinator = new MediaPlaybackCoordinator(() => service = new MediaPlaybackService(backend));
            var host = new PlayerViewerHost(coordinator);
            var layout = new System.Windows.Controls.Grid(); layout.Children.Add(host);
            var window = new System.Windows.Window { Content = layout, Width = 1100, Height = 800, Left = -32000, ShowActivated = false, ShowInTaskbar = false };
            window.Show();
            try
            {
                var asset = new PlayerViewerAsset(Guid.NewGuid(), "clip.mkv", "clip.mkv", "clip.mkv", MediaPresentationKind.Video);
                await host.OpenAsync(asset, new(asset.RootId, asset.RelativePath, asset.Key, fixture, MediaRootAvailability.Online, true));
                await service!.SeekAsync(TimeSpan.FromSeconds(2));
                await Task.Delay(200);
                var fit = backend.RenderedViewport;
                var view = Assert.IsType<MediaPlaybackView>(host.VideoHost.Children[0]);
                var fullFrame = await view.CaptureFrameAsync();
                host.ZoomChoice.SelectedIndex = 2;
                await Task.Delay(200);
                var actual = backend.RenderedViewport;
                Console.WriteLine($"ZOOM renderer={backend.ActiveVideoProcessor} fit={fit} actual100={actual}");
                Assert.NotEqual(fit, actual);
                Assert.InRange(actual.Width, 1918, 1922);
                host.PanViewport(90, 40);
                await Task.Delay(150);
                var zoomedCapture = await view.CaptureFrameAsync();
                Assert.Equal(fullFrame.BgraPixels, zoomedCapture.BgraPixels);
                for (var step = 0; step < 3; step++)
                {
                    var prior = host.SteppedFrameSurface.Source;
                    var timestamp = service.Snapshot.DisplayedTimestamp!.Position;
                    host.PreviousFrameButton.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                    await WaitUntilAsync(() => host.SteppedFrameSurface.Source is not null && !ReferenceEquals(prior, host.SteppedFrameSurface.Source), "retained frame");
                    await WaitUntilAsync(() => service.Snapshot.DisplayedTimestamp!.Position < timestamp, "settled predecessor timestamp");
                    host.PanViewport(15, 10);
                    await Task.Delay(150);
                    Console.WriteLine($"STEP {step} media={host.MediaSurfaceHost.RenderSize} retained={host.SteppedFrameSurface.RenderSize} viewport={backend.RenderedViewport} transform={host.SteppedFrameSurface.RenderTransform.Value}");
                    Assert.InRange(backend.RenderedViewport.Width, 1918, 1922);
                    Assert.InRange(host.SteppedFrameSurface.ActualWidth * host.SteppedFrameSurface.RenderTransform.Value.M11, 1918, 1922);
                }
            }
            finally { await host.CloseAsync(); window.Close(); }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeReviewFullscreen_RetainsSurfaceSourceAndNaturalEndNotification(bool playing)
    {
        var dependencies = PlaybackDependencyLocator.FindSharedLibraries()!;
        var fixture = Path.Combine(_root, "native-review.mkv");
        GenerateCfrFixture(Path.Combine(dependencies, "ffmpeg.exe"), fixture, 12);
        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();
            MediaPlaybackService? service = null;
            await using var coordinator = new MediaPlaybackCoordinator(() => service =
                new MediaPlaybackService(new FlyleafPlaybackBackend(dependencies, () => new RecordingAudioOutput())));
            var host = new PlayerViewerHost(coordinator);
            var layout = new System.Windows.Controls.Grid(); layout.Children.Add(host);
            var window = new System.Windows.Window { Content = layout, Width = 1000, Height = 700,
                Left = -32000, ShowActivated = false, ShowInTaskbar = false, Opacity = 0 };
            window.Show();
            if (playing) window.WindowState = System.Windows.WindowState.Maximized;
            try
            {
                var asset = new PlayerViewerAsset(Guid.NewGuid(), "clip.mkv", "clip.mkv", "clip.mkv", MediaPresentationKind.Video);
                await host.OpenAsync(asset, new(asset.RootId, asset.RelativePath, asset.Key, fixture, MediaRootAvailability.Online, true));
                await Task.Delay(100);
                var view = Assert.IsType<MediaPlaybackView>(host.VideoHost.Children[0]);
                var input = view.InputSurface;
                var info = service!.SourceInfo;
                var size = host.RenderSize;
                if (playing) await service.PlayAsync();
                for (var cycle = 0; cycle < 3; cycle++)
                {
                    Console.WriteLine($"CYCLE {cycle} ENTER");
                    host.ToggleFullscreen();
                    await Task.Delay(100);
                    Assert.Same(input, view.InputSurface);
                    Assert.Same(info, service.SourceInfo);
                    var nativeHost = Assert.IsType<FlyleafLib.Controls.WPF.FlyleafHost>(view.Content);
                    Assert.IsType<PlayerFullscreenOverlay>(nativeHost.Overlay.Content);
                    Assert.Equal(System.Windows.Visibility.Collapsed, host.TransportBar.Visibility);
                    if (cycle == 0) host.TryHandleShortcut(System.Windows.Input.Key.Escape, host);
                    else if (cycle == 1) ((PlayerFullscreenOverlay)nativeHost.Overlay.Content).ExitButton.RaiseEvent(
                        new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                    else host.ToggleFullscreen();
                    Console.WriteLine($"CYCLE {cycle} EXIT");
                    await Task.Delay(100);
                    Assert.Same(input, view.InputSurface);
                    Assert.Same(layout, window.Content);
                    window.UpdateLayout();
                    Console.WriteLine($"CYCLE {cycle} SIZE {host.RenderSize} expected={size} native={((System.Windows.Window)input).ActualWidth} view={view.ActualWidth}");
                    Assert.Equal(size, host.RenderSize);
                    Assert.Equal(System.Windows.Visibility.Visible, host.PlayerHeader.Visibility);
                    Assert.Equal(System.Windows.Visibility.Visible, host.TransportBar.Visibility);
                    var expectedOrigin = view.PointToScreen(new System.Windows.Point());
                    var actualOrigin = input.PointToScreen(new System.Windows.Point());
                    Console.WriteLine($"NATIVE origin={actualOrigin} expected={expectedOrigin} size={input.RenderSize} expected={view.RenderSize}");
                    Assert.InRange(Math.Abs(actualOrigin.Y - expectedOrigin.Y), 0, 2);
                    Assert.InRange(Math.Abs(input.ActualHeight - view.ActualHeight), 0, 2);
                    Assert.InRange(Math.Abs(((System.Windows.Window)input).ActualWidth - view.ActualWidth), 0, 2);
                    Assert.Equal(playing ? MediaPlaybackState.Playing : MediaPlaybackState.Paused, service.Snapshot.State);
                }
                var ended = false;
                service.StateChanged += (_, snapshot) => ended |= snapshot.State == MediaPlaybackState.Ended;
                await service.SeekAsync(TimeSpan.FromSeconds(10));
                await service.SetReviewOptionsAsync(new(4));
                await service.PlayAsync();
                await WaitUntilAsync(() => ended, "native end-of-source event");
                await host.OpenAsync(asset with { Name = "next clip" }, new(asset.RootId, asset.RelativePath, asset.Key, fixture, MediaRootAvailability.Online, true));
                window.UpdateLayout();
                Assert.Equal(size, host.RenderSize);
                Assert.False(host.IsFullscreen);
            }
            finally { await host.CloseAsync(); window.Close(); }
        });
    }

    [Fact]
    public async Task ReviewSpeed_AudioCompanionProducesTempoAdjustedPcmAtEveryRate()
    {
        var dependencies = PlaybackDependencyLocator.FindSharedLibraries()!;
        var fixture = Path.Combine(_root, "tempo.wav");
        Run(Path.Combine(dependencies, "ffmpeg.exe"), "-hide_banner", "-loglevel", "error", "-y",
            "-f", "lavfi", "-i", "sine=frequency=440:duration=2", "-c:a", "pcm_s16le", fixture);
        foreach (var speed in PlaybackReviewOptions.Speeds)
        {
            var output = new RecordingAudioOutput();
            await using var audio = new FfmpegAudioPlayback(dependencies, () => output);
            await audio.StartAsync(fixture, 0, TimeSpan.Zero, CancellationToken.None, speed);
            await WaitUntilAsync(() => audio.ActiveProcessId is null, "tempo audio completion");
            await audio.StopAsync();
            var seconds = output.BytesAdded / (48000d * 2 * 2);
            Assert.InRange(seconds, 2 / speed * 0.9, 2 / speed * 1.1);
            Assert.True(output.Played);
            Assert.True(output.Stopped);
            Console.WriteLine($"AUDIO speed={speed} outputSeconds={seconds:0.000} expected={2 / speed:0.000}");
        }
    }

    [Fact]
    public async Task ReviewSpeedAndCadence_UseRealVideoClockAndPreserveFrameInspection()
    {
        var dependencies = PlaybackDependencyLocator.FindSharedLibraries()!;
        var fixture = Path.Combine(_root, "cadence.mkv");
        Run(Path.Combine(dependencies, "ffmpeg.exe"), "-hide_banner", "-loglevel", "error", "-y",
            "-f", "lavfi", "-i", "testsrc2=size=160x90:rate=120:duration=12", "-c:v", "ffv1", fixture);
        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();
            await using var backend = new FlyleafPlaybackBackend(dependencies);
            await using var service = new MediaPlaybackService(backend);
            await service.OpenAsync(fixture);
            using var presentation = service.CreatePresentation();
            var window = new System.Windows.Window { Content = presentation.Surface, Width = 320, Height = 200,
                Left = -32000, ShowActivated = false, ShowInTaskbar = false };
            window.Show();
            try
            {
                await Task.Delay(100);
                Assert.NotSame(presentation.Surface, presentation.InputSurface); // Actual native Window seam.
                Assert.Equal(120, service.SourceInfo!.FrameRate);
                foreach (var speed in PlaybackReviewOptions.Speeds)
                {
                    await service.SeekAsync(TimeSpan.Zero);
                    await service.SetReviewOptionsAsync(new(speed, 5));
                    var frames = new List<double>();
                    void Frame(object? sender, MediaPresentationTimestamp timestamp) => frames.Add(timestamp.Position.TotalSeconds);
                    service.FramePresented += Frame;
                    await service.PlayAsync();
                    var timer = Stopwatch.StartNew();
                    await Task.Delay(1100);
                    var elapsed = timer.Elapsed.TotalSeconds;
                    service.FramePresented -= Frame;
                    var position = service.Snapshot.DisplayedTimestamp!.Position;
                    await service.PauseAsync();
                    Assert.InRange(position.TotalSeconds / elapsed, speed * 0.5, speed * 1.5);
                    var steps = frames.Zip(frames.Skip(1), (a, b) => b - a).Where(delta => delta > 0.002).ToArray();
                    Assert.NotEmpty(steps);
                    // Matroska quantizes to milliseconds. The display can miss a presentation under
                    // load, but every observed gap must remain a multiple of five source frames.
                    Assert.Contains(steps, delta => Math.Abs(delta - 5d / 120) < 0.0011);
                    Assert.All(steps, delta => Assert.InRange(Math.Abs(delta - Math.Round(delta * 120 / 5) * 5 / 120), 0, 0.0011));
                    await service.SeekAsync(TimeSpan.FromSeconds(1));
                    var before = service.Snapshot.DisplayedTimestamp!.Position;
                    await service.StepForwardAsync();
                    Assert.InRange((service.Snapshot.DisplayedTimestamp!.Position - before).TotalSeconds, 0.007, 0.010);
                    Console.WriteLine($"VIDEO speed={speed} elapsed={elapsed:0.000} pts={position.TotalSeconds:0.000} cadenceFrames={frames.Count}");
                }
                await service.OpenAsync(fixture);
                await service.PlayAsync();
                await Task.Delay(500);
                Assert.InRange(service.Snapshot.DisplayedTimestamp!.Position.TotalSeconds, 0.2, 0.8);
                await service.PauseAsync();
            }
            finally { window.Content = null; window.Close(); }
        });
    }
}
