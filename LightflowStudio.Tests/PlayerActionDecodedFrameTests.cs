using Lightflow.Actions;
using System.Windows;
using Xunit;

namespace LightflowStudio.Tests;

public sealed partial class FlyleafPlaybackIntegrationTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task SemanticController_RangeUsesActualCfrOrVfrPresentedPts(bool vfr)
    {
        var dependencies = PlaybackDependencyLocator.FindSharedLibraries() ?? throw new InvalidOperationException("Prepare playback dependencies.");
        var fixture = Path.Combine(_root, vfr ? "semantic-range-vfr.mkv" : "semantic-range-cfr.mkv");
        if (vfr) GenerateVfrFixture(Path.Combine(dependencies, "ffmpeg.exe"), fixture);
        else GenerateCfrFixture(Path.Combine(dependencies, "ffmpeg.exe"), fixture, durationSeconds: 3);
        var pts = ProbeVideoPts(Path.Combine(dependencies, "ffprobe.exe"), fixture);
        await StaDispatcher.RunAsync(async () => {
            TestWpfApplication.EnsureLoaded(); var ranges = new SemanticRangeStore();
            var service = new MediaPlaybackService(new FlyleafPlaybackBackend(dependencies, () => new RecordingAudioOutput()));
            await using var coordinator = new MediaPlaybackCoordinator(() => service);
            var host = new PlayerViewerHost(coordinator, ranges);
            var window = new Window { Content = host, Width = 1000, Height = 700, Left = -32000, ShowActivated = false, ShowInTaskbar = false }; window.Show();
            try {
                var asset = new PlayerViewerAsset(Guid.NewGuid(), Path.GetFileName(fixture), fixture, "semantic-range", MediaPresentationKind.Video, Guid.NewGuid());
                await host.OpenAsync(asset, new(asset.RootId, asset.RelativePath, asset.Key, fixture, MediaRootAvailability.Online, true));
                await service.SeekAsync(pts[3]);
                var input = new ActionInputSource("range-controller", ActionInputKind.Controller);
                var step = await host.SemanticActions.InvokeAsync(new(PlayerActions.StepFrame, new FrameStepArguments(-1), input, Guid.NewGuid(), host.ActionTarget));
                Assert.Equal(ActionOutcome.Completed, step.Outcome);
                var displayed = service.Snapshot.DisplayedTimestamp!;
                Assert.Contains(pts, timestamp => Close(timestamp, displayed.Position));
                foreach (var boundary in new[] { WorkingRangeBoundary.In, WorkingRangeBoundary.Out }) {
                    var result = await host.SemanticActions.InvokeAsync(new(PlayerActions.SetBoundary, new SetBoundaryArguments(boundary), input, Guid.NewGuid(), host.ActionTarget));
                    Assert.Equal(ActionOutcome.Completed, result.Outcome);
                    Assert.Equal(displayed.Position, boundary == WorkingRangeBoundary.In ? ranges.Range!.In : ranges.Range!.Out);
                }
            } finally { await host.CloseAsync(); window.Close(); }
        });
    }

    private sealed class SemanticRangeStore : IMediaRangeStore
    {
        public MediaRange? Range;
        public Task<MediaRange?> RestoreAsync(Guid assetId, CancellationToken cancellationToken = default) => Task.FromResult<MediaRange?>(null);
        public Task SaveAsync(Guid assetId, MediaRange? range, CancellationToken cancellationToken = default) { Range = range; return Task.CompletedTask; }
    }

    [Fact]
    public async Task SemanticController_UsesDecodedPresentedFramesAndStaysSilentPausedAtBoundaries()
    {
        var dependencies = PlaybackDependencyLocator.FindSharedLibraries() ?? throw new InvalidOperationException("Prepare playback dependencies.");
        var fixture = Path.Combine(_root, "semantic-player.mkv");
        GenerateCfrFixture(Path.Combine(dependencies, "ffmpeg.exe"), fixture, durationSeconds: 3);
        var timestamps = ProbeVideoPts(Path.Combine(dependencies, "ffprobe.exe"), fixture);
        await StaDispatcher.RunAsync(async () => {
            TestWpfApplication.EnsureLoaded(); var audio = new RecordingAudioOutput();
            var service = new MediaPlaybackService(new FlyleafPlaybackBackend(dependencies, () => audio));
            await using var coordinator = new MediaPlaybackCoordinator(() => service);
            var host = new PlayerViewerHost(coordinator);
            var window = new Window { Content = host, Width = 1000, Height = 700, Left = -32000, ShowActivated = false, ShowInTaskbar = false }; window.Show();
            try {
                var asset = new PlayerViewerAsset(Guid.NewGuid(), Path.GetFileName(fixture), fixture, "semantic-player", MediaPresentationKind.Video);
                await host.OpenAsync(asset, new(asset.RootId, asset.RelativePath, asset.Key, fixture, MediaRootAvailability.Online, true));
                window.UpdateLayout();
                await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                async Task Step(int direction) {
                    var result = await host.SemanticActions.InvokeAsync(new(PlayerActions.StepFrame, new FrameStepArguments(direction),
                        new("decoded-controller", ActionInputKind.Controller), Guid.NewGuid(), host.ActionTarget));
                    Assert.True(result.Outcome == ActionOutcome.Completed, $"{result.Outcome}: {result.Diagnostic}"); Assert.Equal(MediaPlaybackState.Paused, service.Snapshot.State); Assert.Null(service.Snapshot.Error);
                }
                await Step(1); AssertTimestamp(timestamps[1], service.Snapshot.DisplayedTimestamp!);
                await Step(1); AssertTimestamp(timestamps[2], service.Snapshot.DisplayedTimestamp!);
                host.PositionSlider.Value = 1000;
                await WaitUntilAsync(() => Close(TimeSpan.FromSeconds(1), service.Snapshot.DisplayedTimestamp!.Position), "middle seek");
                await Step(-1); AssertTimestamp(timestamps.Single(t => Close(TimeSpan.FromSeconds(.9), t)), service.Snapshot.DisplayedTimestamp!);
                Assert.Equal(Visibility.Visible, host.SteppedFrameSurface.Visibility);
                Assert.NotNull(host.SteppedFrameSurface.Source); Assert.False(audio.Played); Assert.Equal(0, audio.BytesAdded);
                host.PositionSlider.Value = timestamps[^1].TotalMilliseconds;
                await WaitUntilAsync(() => Close(timestamps[^1], service.Snapshot.DisplayedTimestamp!.Position), "last frame");
                await Step(1); AssertTimestamp(timestamps[^1], service.Snapshot.DisplayedTimestamp!);
                Assert.False(audio.Played); Assert.Equal(0, audio.BytesAdded);
            } finally { await host.CloseAsync(); window.Close(); }
        });
    }
}
