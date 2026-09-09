using System.Diagnostics;
using Xunit;

namespace LightflowStudio.Tests;

public sealed partial class FlyleafPlaybackIntegrationTests
{
    [Fact]
    public async Task NativeReviewFullscreen_RetainsSurfaceSourceAndNaturalEndNotification()
    {
        var dependencies = PlaybackDependencyLocator.FindSharedLibraries()!;
        var fixture = Path.Combine(_root, "native-review.mkv");
        GenerateCfrFixture(Path.Combine(dependencies, "ffmpeg.exe"), fixture, 1);
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
            try
            {
                var asset = new PlayerViewerAsset(Guid.NewGuid(), "clip.mkv", "clip.mkv", "clip.mkv", MediaPresentationKind.Video);
                await host.OpenAsync(asset, new(asset.RootId, asset.RelativePath, asset.Key, fixture, MediaRootAvailability.Online, true));
                await Task.Delay(100);
                var view = Assert.IsType<MediaPlaybackView>(host.VideoHost.Children[0]);
                var input = view.InputSurface;
                var info = service!.SourceInfo;
                host.ToggleFullscreen();
                await Task.Delay(100);
                Assert.Same(input, view.InputSurface);
                Assert.Same(info, service.SourceInfo);
                host.ExitFullscreen();
                await Task.Delay(100);
                Assert.Same(input, view.InputSurface);
                Assert.Same(layout, window.Content);
                var ended = false;
                service.StateChanged += (_, snapshot) => ended |= snapshot.State == MediaPlaybackState.Ended;
                await service.SetReviewOptionsAsync(new(4));
                await service.PlayAsync();
                await WaitUntilAsync(() => ended, "native end-of-source event");
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
                    // Decoded timestamps are milliseconds in Matroska; each visible step is five 120fps frames.
                    Assert.All(steps, delta => Assert.InRange(delta, 0.040, 0.084));
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
