using System.Diagnostics;
using System.Globalization;
using System.Windows.Threading;
using LightflowStudio;
using Xunit;

namespace LightflowStudio.Tests;

[Collection("Flyleaf playback integration")]
public sealed class FlyleafPlaybackIntegrationTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("lightflow-playback-integration-").FullName;

    [Fact]
    public async Task VfrFixture_UsesDecodedPtsForSeekAndForwardBackwardStepping()
    {
        var dependencies = PlaybackDependencyLocator.FindSharedLibraries()
            ?? throw new InvalidOperationException("Run scripts/Get-PlaybackDependencies.ps1 before integration tests.");
        var fixture = Path.Combine(_root, "vfr.mkv");
        GenerateVfrFixture(Path.Combine(dependencies, "ffmpeg.exe"), fixture);
        var expectedPts = ProbeVideoPts(Path.Combine(dependencies, "ffprobe.exe"), fixture);
        Assert.True(expectedPts.Count >= 8);
        Assert.True(expectedPts.Zip(expectedPts.Skip(1), (a, b) => b - a).Distinct().Count() > 1);
        var a = Path.Combine(_root, "A.mkv");
        var b = Path.Combine(_root, "B.mkv");
        var c = Path.Combine(_root, "C.mkv");
        GenerateCfrFixture(Path.Combine(dependencies, "ffmpeg.exe"), a);
        File.Copy(a, b);
        File.Copy(a, c);
        var corrupt = Path.Combine(_root, "corrupt.mp4");
        File.WriteAllText(corrupt, "not media");

        await StaDispatcher.RunAsync(async () =>
        {
            await using var backend = new FlyleafPlaybackBackend(dependencies);
            await using var playback = new MediaPlaybackService(backend);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            await playback.OpenAsync(fixture, timeout.Token);
            Assert.Equal(MediaPlaybackState.Paused, playback.Snapshot.State);
            Assert.False(playback.SourceInfo!.UsesHardwareDecode,
                "The FFV1 fixture must open through software decoding when hardware acceleration is requested.");
            AssertTimestamp(expectedPts[0], playback.Snapshot.DisplayedTimestamp!);

            await playback.StepForwardAsync(timeout.Token);
            AssertTimestamp(expectedPts[1], playback.Snapshot.DisplayedTimestamp!);
            await playback.StepForwardAsync(timeout.Token);
            AssertTimestamp(expectedPts[2], playback.Snapshot.DisplayedTimestamp!);
            await playback.StepBackwardAsync(timeout.Token);
            AssertTimestamp(expectedPts[1], playback.Snapshot.DisplayedTimestamp!);

            await playback.SeekAsync(expectedPts[5], timeout.Token);
            Assert.Contains(expectedPts, expected => Close(expected, playback.Snapshot.DisplayedTimestamp!.Position));
            Assert.Equal(MediaPlaybackState.Paused, playback.Snapshot.State);

            var frame = await playback.GetFrameAsync(expectedPts[4], timeout.Token);
            Assert.True(frame.Width > 0 && frame.Height > 0);
            Assert.Equal(frame.Stride * frame.Height, frame.BgraPixels.Length);
            Assert.Contains(expectedPts, expected => Close(expected, frame.Timestamp.Position));

            await playback.OpenAsync(corrupt, timeout.Token);
            Assert.Equal(MediaPlaybackState.Failed, playback.Snapshot.State);
            await playback.OpenAsync(a, timeout.Token);
            await playback.OpenAsync(b, timeout.Token);
            await playback.OpenAsync(c, timeout.Token);
            Assert.True(playback.SourceInfo is not null,
                $"Latest source failed: {playback.Snapshot.Error?.Message} {playback.Snapshot.Error?.Diagnostic}");
            Assert.Equal(Path.GetFullPath(c), playback.SourceInfo.SourcePath);

            for (var index = 0; index < 5; index++)
            {
                await playback.CloseAsync(timeout.Token);
                await playback.OpenAsync(c, timeout.Token);
            }
            await playback.CloseAsync(timeout.Token);

            await using var coordinator = new MediaPlaybackCoordinator(() =>
                new MediaPlaybackService(new FlyleafPlaybackBackend(dependencies)));
            await using (var trimPlayback = new TrimEditorPlayback(coordinator))
            {
                var trimService = await trimPlayback.OpenAsync(fixture, timeout.Token);
                Assert.Equal(MediaPlaybackState.Paused, trimService.Snapshot.State);
                await trimService.StepForwardAsync(timeout.Token);
                var selection = new TrimSelection(trimService.SourceInfo!.Duration);
                Assert.True(selection.SetIn(trimService.Snapshot.DisplayedTimestamp!.Position));
                await trimService.StepForwardAsync(timeout.Token);
                Assert.True(selection.SetOut(trimService.Snapshot.DisplayedTimestamp!.Position));
                var applied = selection.Apply();
                AssertTimestamp(expectedPts[1], new(applied!.EffectiveIn));
                AssertTimestamp(expectedPts[2], new(applied.EffectiveOut));
            }
        });

        foreach (var path in new[] { a, b, c, corrupt })
            using (File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
    }

    [Fact]
    public async Task PresentationSurface_AttachesAcrossRepeatedSameAndDifferentSourceEditorSessions()
    {
        var dependencies = PlaybackDependencyLocator.FindSharedLibraries()
            ?? throw new InvalidOperationException("Run scripts/Get-PlaybackDependencies.ps1 before integration tests.");
        var first = Path.Combine(_root, "first.mkv");
        var second = Path.Combine(_root, "second.mkv");
        GenerateCfrFixture(Path.Combine(dependencies, "ffmpeg.exe"), first);
        GenerateCfrFixture(Path.Combine(dependencies, "ffmpeg.exe"), second);

        await StaDispatcher.RunAsync(async () =>
        {
            await using var coordinator = new MediaPlaybackCoordinator(() =>
                new MediaPlaybackService(new FlyleafPlaybackBackend(dependencies)));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            var surfaces = new List<object>();

            foreach (var source in new[] { first, first, second, first })
            {
                await using var editor = new TrimEditorPlayback(coordinator);
                var playback = await editor.OpenAsync(source, timeout.Token);
                using var view = new MediaPlaybackView(playback);
                Assert.NotNull(view.Content);
                surfaces.Add(view.Content);
                await playback.PlayAsync(timeout.Token);
                await Task.Delay(100, timeout.Token);
                await playback.PauseAsync(timeout.Token);
                Assert.Equal(MediaPlaybackState.Paused, playback.Snapshot.State);
            }
            Assert.Equal(4, surfaces.Distinct().Count());
        });
    }

    private static void GenerateCfrFixture(string ffmpeg, string output) => Run(ffmpeg,
        "-hide_banner", "-loglevel", "error", "-y",
        "-f", "lavfi", "-i", "testsrc2=size=160x90:rate=10:duration=1",
        "-f", "lavfi", "-i", "sine=frequency=440:duration=1",
        "-c:v", "ffv1", "-c:a", "pcm_s16le", "-shortest", output);

    private static void GenerateVfrFixture(string ffmpeg, string output) => Run(ffmpeg,
        "-hide_banner", "-loglevel", "error", "-y",
        "-f", "lavfi", "-i", "testsrc2=size=160x90:rate=20:duration=2",
        "-f", "lavfi", "-i", "sine=frequency=660:duration=2",
        "-filter:v", "setpts=if(lt(N\\,10)\\,N/(20*TB)\\,(0.5+(N-10)/7)/TB)",
        "-fps_mode", "vfr", "-c:v", "ffv1", "-c:a", "pcm_s16le", "-shortest", output);

    private static IReadOnlyList<TimeSpan> ProbeVideoPts(string ffprobe, string source)
    {
        var output = Run(ffprobe, "-v", "error", "-select_streams", "v:0", "-show_entries",
            "frame=best_effort_timestamp_time", "-of", "csv=p=0", source);
        return output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(value => TimeSpan.FromSeconds(double.Parse(value.Trim(), CultureInfo.InvariantCulture)))
            .ToList();
    }

    private static string Run(string executable, params string[] arguments)
    {
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var process = Process.Start(info) ?? throw new InvalidOperationException($"Could not start {executable}.");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException(stderr);
        return stdout;
    }

    private static void AssertTimestamp(TimeSpan expected, MediaPresentationTimestamp actual) =>
        Assert.True(Close(expected, actual.Position), $"Expected decoded PTS {expected}, received {actual.Position}.");

    private static bool Close(TimeSpan expected, TimeSpan actual) => Math.Abs((expected - actual).TotalMilliseconds) <= 1;

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }
}

[CollectionDefinition("Flyleaf playback integration", DisableParallelization = true)]
public sealed class FlyleafPlaybackIntegrationCollection;

internal static class StaDispatcher
{
    private static readonly Lazy<Dispatcher> TestDispatcher = new(CreateDispatcher);

    public static Task RunAsync(Func<Task> action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        TestDispatcher.Value.BeginInvoke(async () =>
        {
            try { await action(); completion.TrySetResult(); }
            catch (Exception exception) { completion.TrySetException(exception); }
        });
        return completion.Task;
    }

    private static Dispatcher CreateDispatcher()
    {
        var ready = new TaskCompletionSource<Dispatcher>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            ready.SetResult(dispatcher);
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        return ready.Task.GetAwaiter().GetResult();
    }
}
