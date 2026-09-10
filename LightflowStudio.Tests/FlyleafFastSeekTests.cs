using Xunit;

namespace LightflowStudio.Tests;

public sealed partial class FlyleafPlaybackIntegrationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FastSeek_LongGopWithBFramesOrVfrSettlesOnDecodedTarget(bool vfr)
    {
        var dependencies = PlaybackDependencyLocator.FindSharedLibraries()
            ?? throw new InvalidOperationException("Run scripts/Get-PlaybackDependencies.ps1 first.");
        var fixture = Path.Combine(_root, vfr ? "seek-vfr.mkv" : "seek-bframes.mkv");
        var ffmpeg = Path.Combine(dependencies, "ffmpeg.exe");
        if (vfr) GenerateVfrFixture(ffmpeg, fixture);
        else Run(ffmpeg, "-hide_banner", "-loglevel", "error", "-y",
            "-f", "lavfi", "-i", "testsrc2=size=160x96:rate=30:duration=12",
            "-c:v", "mpeg4", "-g", "300", "-bf", "2", "-q:v", "3", fixture);
        var pts = ProbeVideoPts(Path.Combine(dependencies, "ffprobe.exe"), fixture);
        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();
            await using var playback = new MediaPlaybackService(new FlyleafPlaybackBackend(dependencies));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            await playback.OpenAsync(fixture, timeout.Token);
            foreach (var index in new[] { pts.Count - 2, 1, pts.Count / 2, 0, pts.Count - 1, pts.Count / 3 })
            {
                await playback.SeekAsync(pts[index], timeout.Token);
                Assert.Equal(MediaPlaybackState.Paused, playback.Snapshot.State);
                AssertTimestamp(pts[index], playback.Snapshot.DisplayedTimestamp!);
            }
            // A burst must settle at the last request even when native work is already underway.
            var first = playback.SeekAsync(pts[^2], timeout.Token);
            var latest = playback.SeekAsync(pts[2], timeout.Token);
            try { await first; } catch (OperationCanceledException) { }
            await latest;
            AssertTimestamp(pts[2], playback.Snapshot.DisplayedTimestamp!);
            await playback.CloseAsync(timeout.Token);
        });
        using (File.Open(fixture, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
    }
}
