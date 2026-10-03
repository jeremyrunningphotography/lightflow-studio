using System.Windows;
using System.Windows.Input;
using Xunit;

namespace LightflowStudio.Tests;

public sealed partial class FlyleafPlaybackIntegrationTests
{
    [Fact]
    public async Task ArrowInput_RoutedForwardStepsAndBoundaryRequestsRemainSilentAndPaused()
    {
        var dependencies = PlaybackDependencyLocator.FindSharedLibraries()
            ?? throw new InvalidOperationException("Run scripts/Get-PlaybackDependencies.ps1 before integration tests.");
        var fixture = Path.Combine(_root, "arrow-stepping.mkv");
        GenerateCfrFixture(Path.Combine(dependencies, "ffmpeg.exe"), fixture, durationSeconds: 3);
        var timestamps = ProbeVideoPts(Path.Combine(dependencies, "ffprobe.exe"), fixture);
        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();
            var audio = new RecordingAudioOutput();
            var backend = new FlyleafPlaybackBackend(dependencies, () => audio);
            var service = new MediaPlaybackService(backend);
            await using var coordinator = new MediaPlaybackCoordinator(() => service);
            var host = new PlayerViewerHost(coordinator);
            var window = new Window { Content = host, Left = -32000, Top = -32000, ShowActivated = false, ShowInTaskbar = false };
            window.Show();
            try
            {
                var asset = new PlayerViewerAsset(Guid.NewGuid(), Path.GetFileName(fixture), fixture, "arrow-stepping", MediaPresentationKind.Video);
                await host.OpenAsync(asset, new(asset.RootId, asset.RelativePath, asset.Key, fixture, MediaRootAvailability.Online, true));
                Assert.NotEmpty(service.SourceInfo!.AudioStreams);
                var queue = (FrameStepQueue)typeof(PlayerViewerHost).GetField("_frameStepQueue",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(host)!;
                async Task Step(Key key)
                {
                    Assert.True(PlayerViewerHostLeaseTests.RaisePlayerKey(host, window, key).Handled);
                    await queue.WaitUntilIdleAsync().WaitAsync(TimeSpan.FromSeconds(30));
                    Assert.Equal(MediaPlaybackState.Paused, service.Snapshot.State);
                    Assert.Null(service.Snapshot.Error);
                }
                AssertTimestamp(timestamps[0], service.Snapshot.DisplayedTimestamp!);
                await Step(Key.Left);
                AssertTimestamp(timestamps[0], service.Snapshot.DisplayedTimestamp!);
                await Step(Key.Right);
                AssertTimestamp(timestamps[1], service.Snapshot.DisplayedTimestamp!);
                await Step(Key.Right);
                AssertTimestamp(timestamps[2], service.Snapshot.DisplayedTimestamp!);
                Assert.False(audio.Played);
                Assert.Equal(0, audio.BytesAdded);

                host.PositionSlider.Value = timestamps[^1].TotalMilliseconds;
                await WaitUntilAsync(() => Close(timestamps[^1], service.Snapshot.DisplayedTimestamp!.Position), "last decoded frame");
                await Step(Key.Right);
                AssertTimestamp(timestamps[^1], service.Snapshot.DisplayedTimestamp!);
                await Step(Key.Right);
                AssertTimestamp(timestamps[^1], service.Snapshot.DisplayedTimestamp!);
                Assert.False(audio.Played);
                Assert.Equal(0, audio.BytesAdded);

                host.PositionSlider.Value = 1000;
                await WaitUntilAsync(() => Close(TimeSpan.FromSeconds(1), service.Snapshot.DisplayedTimestamp!.Position), "middle seek");
                await service.PlayAsync();
                await WaitUntilAsync(() => audio.BytesAdded > 0, "audio playing before step");
                await Step(Key.Right);
                Assert.True(audio.Stopped);
            }
            finally { await host.CloseAsync(); window.Close(); }
        });
    }
}
