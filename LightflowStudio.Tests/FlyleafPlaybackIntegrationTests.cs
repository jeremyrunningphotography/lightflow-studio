using System.Diagnostics;
using System.Globalization;
using System.Windows.Threading;
using LightflowStudio;
using Xunit;

namespace LightflowStudio.Tests;

[Collection("STA dispatcher tests")]
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
            // Flyleaf's own player engine creates (and, on disposal, shuts down) its own
            // System.Windows.Application if none exists yet when it first needs one — WPF permits only one
            // Application per process, ever, and once shut down, Application.Current goes back to null with no
            // way to construct a replacement. Establishing ours first, here, means Flyleaf finds one already
            // present and (being a well-behaved WPF citizen) reuses rather than owns/tears it down, so it stays
            // valid for every other live-WPF test (Browser*LiveInteractionTests) later in the same process.
            TestWpfApplication.EnsureLoaded();
            await using var backend = new FlyleafPlaybackBackend(dependencies);
            await using var playback = new MediaPlaybackService(backend);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            await playback.OpenAsync(fixture, timeout.Token);
            Assert.Equal(MediaPlaybackState.Paused, playback.Snapshot.State);
            var openMetrics = Assert.IsType<MediaPlaybackOpenMetrics>(playback.SourceInfo!.OpenMetrics);
            Assert.True(openMetrics.SourceOpen > TimeSpan.Zero);
            Assert.True(openMetrics.FirstFrameSettle > TimeSpan.Zero);
            Assert.True(openMetrics.Total >= openMetrics.SourceOpen + openMetrics.FirstFrameSettle);
            Console.WriteLine(
                $"OPEN_METRICS source={openMetrics.SourceOpen.TotalMilliseconds:n0}ms " +
                $"firstFrame={openMetrics.FirstFrameSettle.TotalMilliseconds:n0}ms total={openMetrics.Total.TotalMilliseconds:n0}ms");
            Assert.False(playback.SourceInfo!.UsesHardwareDecode,
                "The FFV1 fixture must open through software decoding when hardware acceleration is requested.");
            AssertTimestamp(expectedPts[0], playback.Snapshot.DisplayedTimestamp!);

            await playback.StepForwardAsync(timeout.Token);
            AssertTimestamp(expectedPts[1], playback.Snapshot.DisplayedTimestamp!);
            await playback.StepForwardAsync(timeout.Token);
            AssertTimestamp(expectedPts[2], playback.Snapshot.DisplayedTimestamp!);
            var reversePresentation = new List<MediaPresentationTimestamp>();
            playback.FramePresented += (_, timestamp) => reversePresentation.Add(timestamp);
            await playback.StepBackwardAsync(timeout.Token);
            AssertTimestamp(expectedPts[1], playback.Snapshot.DisplayedTimestamp!);
            Assert.Single(reversePresentation);
            AssertTimestamp(expectedPts[1], reversePresentation[0]);

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
    public async Task StepForward_RepeatedlyAtTheLastDecodedFrameStaysBoundedInsteadOfHangingForTheFullInternalTimeout()
    {
        // Flyleaf's ShowFrameNext synchronously updates CurTime or leaves it unchanged at the end boundary.
        // The backend must return that stable boundary immediately rather than waiting for an event that cannot fire.
        var dependencies = PlaybackDependencyLocator.FindSharedLibraries()
            ?? throw new InvalidOperationException("Run scripts/Get-PlaybackDependencies.ps1 before integration tests.");
        var fixture = Path.Combine(_root, "end-of-clip.mkv");
        GenerateCfrFixture(Path.Combine(dependencies, "ffmpeg.exe"), fixture); // 1 second @ 10fps = 10 frames

        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();
            await using var backend = new FlyleafPlaybackBackend(dependencies);
            await using var playback = new MediaPlaybackService(backend);
            using var openTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            await playback.OpenAsync(fixture, openTimeout.Token);
            await playback.SeekAsync(playback.SourceInfo!.Duration, openTimeout.Token);

            // Walk beyond the true last frame and prove the unchanged synchronous boundary returns promptly.
            var elapsed = System.Diagnostics.Stopwatch.StartNew();
            for (var attempt = 0; attempt < 3; attempt++)
                await playback.StepForwardAsync(openTimeout.Token);
            // Two seconds leaves ample decode jitter while excluding the removed ten-second wait.
            Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(2),
                $"3 forward steps at/past the end of clip took {elapsed.Elapsed}; the synchronous native boundary should return without a completion timeout.");
        });
    }

    [Fact]
    public async Task RapidAlternatingFrameStepRequests_ThroughFrameStepQueueNeverHangOrCrashTheRealEngine()
    {
        // Reproduces the hands-on report against #132: rapidly clicking Previous/Next Frame could hang or
        // crash the application. Proven root cause (see FrameStepQueue's doc comment): the old per-click call
        // pattern could let a new StepForwardAsync/StepBackwardAsync request reach FlyleafPlaybackBackend
        // before the previous one's native ShowFrameNext() decode had genuinely finished — cancelling the C#
        // wait for that signal never told the native engine to abandon it. FrameStepQueue.RequestStep never
        // starts request N+1 until request N has genuinely returned, so this fires a real rapid-fire burst —
        // alternating direction, faster than a human could click, including runs at both the clip start and
        // end — directly against the real Flyleaf engine and requires the whole thing to complete cleanly
        // within a bounded time, with no hang and (trivially, since the test process is still running to
        // check it) no crash.
        var dependencies = PlaybackDependencyLocator.FindSharedLibraries()
            ?? throw new InvalidOperationException("Run scripts/Get-PlaybackDependencies.ps1 before integration tests.");
        var fixture = Path.Combine(_root, "rapid-step.mkv");
        GenerateCfrFixture(Path.Combine(dependencies, "ffmpeg.exe"), fixture, durationSeconds: 3); // 3s @ 10fps = 30 frames

        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();
            await using var backend = new FlyleafPlaybackBackend(dependencies);
            await using var playback = new MediaPlaybackService(backend);
            using var openTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await playback.OpenAsync(fixture, openTimeout.Token);

            var queue = new FrameStepQueue();
            var errors = new List<Exception>();
            var elapsed = Stopwatch.StartNew();

            // Rapid-fire forward from the start (stresses the start boundary), then rapid-fire alternating
            // (stresses genuine reentrancy under direction changes), then seek to the end and rapid-fire
            // forward again (stresses the end boundary) — all issued in tight loops, not sequentially awaited.
            // Real decode work is genuinely slow in this environment, so these bursts are deliberately smaller
            // than the isolated fake-backed tests above — still far faster than a human could physically
            // click, which is the property under test, not a specific burst size.
            for (var i = 0; i < 8; i++) queue.RequestStep(playback, forward: true, errors.Add);
            await WaitUntilIdleAsync(queue);

            for (var i = 0; i < 12; i++) queue.RequestStep(playback, forward: i % 2 == 0, errors.Add);
            await WaitUntilIdleAsync(queue);

            using var seekTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await playback.SeekAsync(playback.SourceInfo!.Duration, seekTimeout.Token);
            for (var i = 0; i < 6; i++) queue.RequestStep(playback, forward: true, errors.Add);
            await WaitUntilIdleAsync(queue);

            Assert.Empty(errors);
            Assert.Equal(MediaPlaybackState.Paused, playback.Snapshot.State);
            Assert.True(elapsed.Elapsed < TimeSpan.FromMinutes(2),
                $"Rapid alternating frame-step burst took {elapsed.Elapsed} against the real engine; expected it to complete promptly rather than hang.");
        });
    }

    [Fact]
    public async Task ClosingImmediatelyAfterAFrameStepRequest_NeverHangsOrCrashesTheRealEngine()
    {
        // Covers "Back/Esc while a step is in flight" from #132's lifecycle-safety requirements. This is a
        // narrower, pre-existing race than the rapid-click one above (present since #52-#55's original Trim
        // editor, not introduced by FrameStepQueue): MediaPlaybackService.CloseAsync cancels the current
        // operation's C# wait and releases its semaphore as soon as that cancellation is observed, not once
        // the backend's fire-and-forget native ShowFrameNext() decode has actually finished — the same
        // "cancelling the wait doesn't abort the native decode" gap FrameStepQueue's own doc comment describes
        // for the rapid-click case, just triggered by a close instead of a second step. Repeating the
        // request-then-immediately-close sequence many times against a real source maximizes the chance of
        // actually landing inside that narrow window if it is unsafe in practice.
        var dependencies = PlaybackDependencyLocator.FindSharedLibraries()
            ?? throw new InvalidOperationException("Run scripts/Get-PlaybackDependencies.ps1 before integration tests.");

        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();

            for (var attempt = 0; attempt < 12; attempt++)
            {
                var fixture = Path.Combine(_root, $"close-during-step-{attempt}.mkv");
                GenerateCfrFixture(Path.Combine(dependencies, "ffmpeg.exe"), fixture, durationSeconds: 2);

                await using var backend = new FlyleafPlaybackBackend(dependencies);
                await using var playback = new MediaPlaybackService(backend);
                using var openTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                await playback.OpenAsync(fixture, openTimeout.Token);

                // Fire the step but do not await it — this is exactly the "genuinely in flight" window a real
                // rapid Next-Frame-then-Back sequence produces, then close immediately behind it.
                var step = playback.StepForwardAsync();
                await playback.CloseAsync();
                try { await step; } catch (Exception) { /* superseded/cancelled by the close is expected */ }
            }
        });
    }

    [Fact]
    public async Task TwoSequentialPreviousFrameClicksOnHardwareDecodedRealVideoWithLiveRenderSurfaceNeverCrash()
    {
        // Every other backward-stepping test in this file uses the FFV1 fixture, which the codebase's own
        // existing assertion confirms forces SOFTWARE decode (see
        // VfrFixture_UsesDecodedPtsForSeekAndForwardBackwardStepping's Assert.False(UsesHardwareDecode)) and
        // never attaches the presentation surface to a real, shown window (MediaPlaybackView is always
        // constructed standalone elsewhere, never added to a visible Window) — so none of them exercise the
        // GPU-driven DXVA2/D3D11VA hardware decode path or the live D3D11 render surface that
        // FlyleafPlaybackBackend.CreatePlayer() actually requests (config.Video.VideoAcceleration = true) and
        // that PlayerViewerHost actually uses. This covers real H.264 content with hardware decode and a
        // genuinely shown, rendering window: open, then exactly two sequential (fully awaited, not rapid)
        // Previous Frame clicks through the full production stack (MediaPlaybackService -> FrameStepQueue), at
        // several positions including near end-of-source and far from any keyframe.
        var dependencies = PlaybackDependencyLocator.FindSharedLibraries()
            ?? throw new InvalidOperationException("Run scripts/Get-PlaybackDependencies.ps1 before integration tests.");
        var fixture = Path.Combine(_root, "two-click-h264.mkv");
        GenerateH264Fixture(Path.Combine(dependencies, "ffmpeg.exe"), fixture, durationSeconds: 30, keyframeIntervalFrames: 300); // 10s GOP @ 30fps: sparse keyframes force a long forward walk during reconstruction

        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();
            await using var playback = new MediaPlaybackService(new FlyleafPlaybackBackend(dependencies));
            using var openTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await playback.OpenAsync(fixture, openTimeout.Token);

            using var view = new MediaPlaybackView(playback);
            var window = new System.Windows.Window { Content = view, Width = 320, Height = 240, ShowActivated = false, ShowInTaskbar = false };
            window.Show();
            try
            {
                await Task.Delay(300); // let the render surface actually initialize/attach a frame
                Assert.True(playback.SourceInfo!.UsesHardwareDecode, "This fixture is expected to hardware-decode; the test would not cover the reported scenario otherwise.");

                foreach (var seconds in new[] { 29.5, 15.0, 5.0 })
                {
                    await playback.SeekAsync(TimeSpan.FromSeconds(seconds), openTimeout.Token);
                    var queue = new FrameStepQueue();
                    var errors = new List<Exception>();

                    queue.RequestStep(playback, forward: false, errors.Add);
                    await WaitUntilIdleAsync(queue);
                    queue.RequestStep(playback, forward: false, errors.Add);
                    await WaitUntilIdleAsync(queue);

                    Assert.Empty(errors);
                    Assert.Equal(MediaPlaybackState.Paused, playback.Snapshot.State);
                }
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public async Task RapidPreviousFrameClicksOnHardwareDecodedRealVideoWithLiveRenderSurfaceNeverCrash()
    {
        // Rapid-clicking counterpart to the two-click test above: many backward requests fired through
        // FrameStepQueue without waiting between them (matching a user holding/mashing Previous Frame), still
        // against real hardware-decoded H.264 content with a live rendering surface attached.
        var dependencies = PlaybackDependencyLocator.FindSharedLibraries()
            ?? throw new InvalidOperationException("Run scripts/Get-PlaybackDependencies.ps1 before integration tests.");
        var fixture = Path.Combine(_root, "rapid-h264.mkv");
        GenerateH264Fixture(Path.Combine(dependencies, "ffmpeg.exe"), fixture, durationSeconds: 30, keyframeIntervalFrames: 300);

        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();
            await using var playback = new MediaPlaybackService(new FlyleafPlaybackBackend(dependencies));
            using var openTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await playback.OpenAsync(fixture, openTimeout.Token);

            using var view = new MediaPlaybackView(playback);
            var window = new System.Windows.Window { Content = view, Width = 320, Height = 240, ShowActivated = false, ShowInTaskbar = false };
            window.Show();
            try
            {
                await Task.Delay(300);
                Assert.True(playback.SourceInfo!.UsesHardwareDecode, "This fixture is expected to hardware-decode; the test would not cover the reported scenario otherwise.");
                await playback.SeekAsync(TimeSpan.FromSeconds(20), openTimeout.Token);

                var queue = new FrameStepQueue();
                var errors = new List<Exception>();
                for (var i = 0; i < 15; i++) queue.RequestStep(playback, forward: false, errors.Add);
                await WaitUntilIdleAsync(queue);

                Assert.Empty(errors);
                Assert.Equal(MediaPlaybackState.Paused, playback.Snapshot.State);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public async Task RapidBackwardOnlyFrameStepRequests_MidClipNeverHangOrCrashTheRealEngine()
    {
        // Reproduces the reported rapid Previous Frame path against real decode work. Requests are unawaited
        // between clicks and must serialize to settled predecessors without overlap, timeout, hang, or crash.
        var dependencies = PlaybackDependencyLocator.FindSharedLibraries()
            ?? throw new InvalidOperationException("Run scripts/Get-PlaybackDependencies.ps1 before integration tests.");
        var fixture = Path.Combine(_root, "rapid-backward-mid.mkv");
        GenerateCfrFixture(Path.Combine(dependencies, "ffmpeg.exe"), fixture, durationSeconds: 12); // 12s @ 10fps = 120 frames

        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();
            await using var backend = new FlyleafPlaybackBackend(dependencies);
            await using var playback = new MediaPlaybackService(backend);
            using var openTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await playback.OpenAsync(fixture, openTimeout.Token);
            await playback.SeekAsync(TimeSpan.FromSeconds(11.5), openTimeout.Token); // inside the confirmed trailing "trouble zone"

            var queue = new FrameStepQueue();
            var errors = new List<Exception>();
            var elapsed = Stopwatch.StartNew();

            for (var i = 0; i < 10; i++) queue.RequestStep(playback, forward: false, errors.Add);
            await WaitUntilIdleAsync(queue);

            Assert.Empty(errors);
            Assert.Equal(MediaPlaybackState.Paused, playback.Snapshot.State);
            Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(30),
                $"10 rapid-fire backward-only steps starting in the trailing trouble zone took {elapsed.Elapsed} against the real engine; expected the per-internal-step bound to keep this responsive, not stall for the internal 10s-per-step fallback.");
        });
    }

    [Fact]
    public async Task RapidBackwardOnlyFrameStepRequests_NearClipStartNeverHangOrCrashTheRealEngine()
    {
        // The backward-reconstruction boundary case: stepping back repeatedly starting very close to frame 0,
        // where the search window can run out of clip before finding ~8 predecessors and the original<=0 fast
        // path only covers exactly the first frame, not "close to" it.
        var dependencies = PlaybackDependencyLocator.FindSharedLibraries()
            ?? throw new InvalidOperationException("Run scripts/Get-PlaybackDependencies.ps1 before integration tests.");
        var fixture = Path.Combine(_root, "rapid-backward-start.mkv");
        GenerateCfrFixture(Path.Combine(dependencies, "ffmpeg.exe"), fixture, durationSeconds: 2); // 2s @ 10fps = 20 frames

        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();
            await using var backend = new FlyleafPlaybackBackend(dependencies);
            await using var playback = new MediaPlaybackService(backend);
            using var openTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await playback.OpenAsync(fixture, openTimeout.Token);
            await playback.SeekAsync(TimeSpan.FromMilliseconds(400), openTimeout.Token); // a few frames in, not frame 0

            var queue = new FrameStepQueue();
            var errors = new List<Exception>();
            var elapsed = Stopwatch.StartNew();

            for (var i = 0; i < 8; i++) queue.RequestStep(playback, forward: false, errors.Add);
            await WaitUntilIdleAsync(queue);

            Assert.Empty(errors);
            Assert.Equal(MediaPlaybackState.Paused, playback.Snapshot.State);
            Assert.True(elapsed.Elapsed < TimeSpan.FromMinutes(3),
                $"8 rapid-fire backward-only steps near clip start took {elapsed.Elapsed} against the real engine; expected completion rather than a hang.");
        });
    }

    [Fact]
    public async Task AlternatingForwardAndBackward_AfterSeveralBackwardStepsNeverHangsOrCrashesTheRealEngine()
    {
        var dependencies = PlaybackDependencyLocator.FindSharedLibraries()
            ?? throw new InvalidOperationException("Run scripts/Get-PlaybackDependencies.ps1 before integration tests.");
        var fixture = Path.Combine(_root, "alternating-after-backward.mkv");
        GenerateCfrFixture(Path.Combine(dependencies, "ffmpeg.exe"), fixture, durationSeconds: 3);

        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();
            await using var backend = new FlyleafPlaybackBackend(dependencies);
            await using var playback = new MediaPlaybackService(backend);
            using var openTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await playback.OpenAsync(fixture, openTimeout.Token);
            await playback.SeekAsync(TimeSpan.FromSeconds(2), openTimeout.Token);

            var queue = new FrameStepQueue();
            var errors = new List<Exception>();

            for (var i = 0; i < 5; i++) queue.RequestStep(playback, forward: false, errors.Add);
            await WaitUntilIdleAsync(queue);

            for (var i = 0; i < 8; i++) queue.RequestStep(playback, forward: i % 2 == 0, errors.Add);
            await WaitUntilIdleAsync(queue);

            Assert.Empty(errors);
            Assert.Equal(MediaPlaybackState.Paused, playback.Snapshot.State);
        });
    }

    [Fact]
    public async Task ClosingImmediatelyAfterABackwardStepRequest_NeverHangsOrCrashesTheRealEngine()
    {
        // Backward-direction counterpart to ClosingImmediatelyAfterAFrameStepRequest above — now that backward
        // stepping can legitimately run for longer (no external wall-clock cutoff), a close/Back landing while
        // its reconstruction is genuinely in flight is a real, not just theoretical, window.
        var dependencies = PlaybackDependencyLocator.FindSharedLibraries()
            ?? throw new InvalidOperationException("Run scripts/Get-PlaybackDependencies.ps1 before integration tests.");

        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();

            for (var attempt = 0; attempt < 8; attempt++)
            {
                var fixture = Path.Combine(_root, $"close-during-backward-step-{attempt}.mkv");
                GenerateCfrFixture(Path.Combine(dependencies, "ffmpeg.exe"), fixture, durationSeconds: 2);

                await using var backend = new FlyleafPlaybackBackend(dependencies);
                await using var playback = new MediaPlaybackService(backend);
                using var openTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                await playback.OpenAsync(fixture, openTimeout.Token);
                await playback.SeekAsync(TimeSpan.FromSeconds(1.5), openTimeout.Token);

                var step = playback.StepBackwardAsync();
                await playback.CloseAsync();
                try { await step; } catch (Exception) { /* superseded/cancelled by the close is expected */ }
            }
        });
    }

    [Fact]
    public async Task SourceSwitch_AfterRepeatedBackwardSteppingNeverHangsOrCrashesTheRealEngine()
    {
        var dependencies = PlaybackDependencyLocator.FindSharedLibraries()
            ?? throw new InvalidOperationException("Run scripts/Get-PlaybackDependencies.ps1 before integration tests.");
        var first = Path.Combine(_root, "backward-then-switch-a.mkv");
        var second = Path.Combine(_root, "backward-then-switch-b.mkv");
        GenerateCfrFixture(Path.Combine(dependencies, "ffmpeg.exe"), first, durationSeconds: 3);
        GenerateCfrFixture(Path.Combine(dependencies, "ffmpeg.exe"), second, durationSeconds: 3);

        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();
            await using var backend = new FlyleafPlaybackBackend(dependencies);
            await using var playback = new MediaPlaybackService(backend);
            using var openTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            await playback.OpenAsync(first, openTimeout.Token);
            await playback.SeekAsync(TimeSpan.FromSeconds(2), openTimeout.Token);
            var queue = new FrameStepQueue();
            var errors = new List<Exception>();
            for (var i = 0; i < 5; i++) queue.RequestStep(playback, forward: false, errors.Add);
            // Deliberately does not wait for the backward burst to fully drain before switching sources —
            // exactly what PlayerViewerHost/TrimEditorWindow's ReleaseCurrentAsync (FrameStepQueue.Reset, then
            // opening a new source) does when the user opens a different asset mid-step.
            queue.Reset();

            await playback.OpenAsync(second, openTimeout.Token);
            Assert.Equal(Path.GetFullPath(second), playback.SourceInfo!.SourcePath);
            Assert.Equal(MediaPlaybackState.Paused, playback.Snapshot.State);
        });
    }

    [Fact]
    public async Task VolumeAndMutePersistAcrossASourceSwitch()
    {
        // Volume/mute belong to the shared backend rather than one source-specific output instance.
        var dependencies = PlaybackDependencyLocator.FindSharedLibraries()
            ?? throw new InvalidOperationException("Run scripts/Get-PlaybackDependencies.ps1 before integration tests.");
        var first = Path.Combine(_root, "volume-first.mkv");
        var second = Path.Combine(_root, "volume-second.mkv");
        GenerateCfrFixture(Path.Combine(dependencies, "ffmpeg.exe"), first);
        GenerateCfrFixture(Path.Combine(dependencies, "ffmpeg.exe"), second);

        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();
            var backend = new FlyleafPlaybackBackend(dependencies);
            await using var playback = new MediaPlaybackService(backend);
            using var openTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            await playback.OpenAsync(first, openTimeout.Token);
            playback.Volume = 42;
            playback.Mute = true;

            Assert.Equal(42, playback.Volume);
            Assert.True(playback.Mute);

            // A new source creates a fresh native Player (see FlyleafPlaybackBackend.CreatePlayer) — the
            // volume/mute choice must survive that, matching how a physical volume knob behaves regardless of
            // what's currently loaded.
            await playback.OpenAsync(second, openTimeout.Token);
            Assert.Equal(42, playback.Volume);
            Assert.True(playback.Mute);
        });
    }

    [Fact]
    public async Task AudioPlayback_DecodesSelectedStreamToBoundedOutputAndStopsOnPause()
    {
        // Proves the partial replacement at its real decode boundary: bundled FFmpeg maps the selected stream
        // and produces PCM, while a deterministic output fake avoids depending on the test runner's audio device.
        var dependencies = PlaybackDependencyLocator.FindSharedLibraries()
            ?? throw new InvalidOperationException("Run scripts/Get-PlaybackDependencies.ps1 before integration tests.");
        var fixture = Path.Combine(_root, "audio-investigation.mkv");
        GenerateCfrFixture(Path.Combine(dependencies, "ffmpeg.exe"), fixture, durationSeconds: 3);

        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();
            var output = new RecordingAudioOutput();
            await using var backend = new FlyleafPlaybackBackend(dependencies, () => output);
            using var openTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var opened = await backend.OpenAsync(fixture, openTimeout.Token);
            Assert.True(opened.Source.AudioStreams.Count > 0, "This fixture is expected to carry an audio track.");

            backend.Volume = 42;
            backend.Mute = true;
            await backend.PlayAsync(openTimeout.Token);
            await WaitUntilAsync(() => output.BytesAdded > 0, "decoded PCM to reach the audio output");
            Assert.True(output.Played);
            Assert.Equal(0, output.Volume);

            backend.Mute = false;
            Assert.Equal(0.42f, output.Volume, 0.01f);

            await backend.PauseAsync(openTimeout.Token);
            Assert.True(output.Stopped);
        });
    }

    [Fact]
    public async Task FrameStepping_NeverResumesPlaybackEvenWhenAStepIsRequestedWhilePlaying()
    {
        // Frame stepping stops the audio companion and never restarts it in either direction, including during
        // backward reconstruction's internal seeks and forward walk.
        var dependencies = PlaybackDependencyLocator.FindSharedLibraries()
            ?? throw new InvalidOperationException("Run scripts/Get-PlaybackDependencies.ps1 before integration tests.");
        var fixture = Path.Combine(_root, "stepping-silence.mkv");
        GenerateCfrFixture(Path.Combine(dependencies, "ffmpeg.exe"), fixture, durationSeconds: 6);

        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();
            var audioOutput = new RecordingAudioOutput();
            await using var backend = new FlyleafPlaybackBackend(dependencies, () => audioOutput);
            var playerField = typeof(FlyleafPlaybackBackend).GetField("_player", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?? throw new InvalidOperationException("_player field not found");
            using var openTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await backend.OpenAsync(fixture, openTimeout.Token);
            var player = (FlyleafLib.MediaPlayer.Player)(playerField.GetValue(backend) ?? throw new InvalidOperationException("player is null"));

            // 3.0s in a 6s clip: comfortably clear of the confirmed trailing "trouble zone" near end-of-source
            // (see StepBackwardAsync's own doc comment) — this test is about play/pause state, not stepping
            // near a boundary, so it deliberately avoids that separately-documented territory.
            await backend.SeekAsync(TimeSpan.FromSeconds(3.0), openTimeout.Token);
            await backend.PlayAsync(openTimeout.Token);
            Assert.True(player.IsPlaying);

            await backend.StepBackwardAsync(openTimeout.Token);
            Assert.False(player.IsPlaying, "Backward stepping must leave playback paused, never resumed.");
            Assert.True(audioOutput.Stopped);

            await backend.PlayAsync(openTimeout.Token);
            Assert.True(player.IsPlaying);

            await backend.StepForwardAsync(openTimeout.Token);
            Assert.False(player.IsPlaying, "Forward stepping must leave playback paused, never resumed.");
        });
    }

    private static async Task WaitUntilIdleAsync(FrameStepQueue queue)
    {
        var deadline = DateTime.UtcNow.AddSeconds(90);
        while (queue.IsDraining)
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("Timed out waiting for the frame-step queue to finish draining against the real engine.");
            await Task.Delay(15);
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition, string waitingFor)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException($"Timed out waiting for {waitingFor}.");
            await Task.Delay(10);
        }
    }

    private sealed class RecordingAudioOutput : IPlaybackAudioOutput
    {
        public int BytesAdded { get; private set; }
        public bool Played { get; private set; }
        public bool Stopped { get; private set; }
        public TimeSpan BufferedDuration => TimeSpan.Zero;
        public float Volume { get; set; }
        public void AddSamples(byte[] buffer, int offset, int count) => BytesAdded += count;
        public void Play() => Played = true;
        public void Stop() => Stopped = true;
        public void Dispose() { }
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
            TestWpfApplication.EnsureLoaded();
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

    private static void GenerateCfrFixture(string ffmpeg, string output, int durationSeconds = 1) => Run(ffmpeg,
        "-hide_banner", "-loglevel", "error", "-y",
        "-f", "lavfi", "-i", $"testsrc2=size=160x90:rate=10:duration={durationSeconds}",
        "-f", "lavfi", "-i", $"sine=frequency=440:duration={durationSeconds}",
        "-c:v", "ffv1", "-c:a", "pcm_s16le", "-shortest", output);

    private static void GenerateVfrFixture(string ffmpeg, string output) => Run(ffmpeg,
        "-hide_banner", "-loglevel", "error", "-y",
        "-f", "lavfi", "-i", "testsrc2=size=160x90:rate=20:duration=2",
        "-f", "lavfi", "-i", "sine=frequency=660:duration=2",
        "-filter:v", "setpts=if(lt(N\\,10)\\,N/(20*TB)\\,(0.5+(N-10)/7)/TB)",
        "-fps_mode", "vfr", "-c:v", "ffv1", "-c:a", "pcm_s16le", "-shortest", output);

    private static void GenerateH264Fixture(string ffmpeg, string output, int durationSeconds, int keyframeIntervalFrames = 30) => Run(ffmpeg,
        "-hide_banner", "-loglevel", "error", "-y",
        "-f", "lavfi", "-i", $"testsrc2=size=640x360:rate=30:duration={durationSeconds}",
        "-f", "lavfi", "-i", $"sine=frequency=440:duration={durationSeconds}",
        "-c:v", "libopenh264", "-g", keyframeIntervalFrames.ToString(CultureInfo.InvariantCulture), "-c:a", "aac", "-shortest", output);

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

/// <summary>
/// Every test that drives StaDispatcher's one shared background STA thread — real media playback here, real
/// WPF Window/Application construction in the Browser*LiveInteractionTests classes — shares this single
/// collection. xUnit only serializes tests *within* one collection; different collections are free to run
/// concurrently on separate xUnit worker threads, and since BeginInvoke-queued async callbacks yield control
/// back to the dispatcher's own queue at their first await (returning to the message loop before the whole
/// callback finishes), two callbacks queued from two different, concurrently-running collections can genuinely
/// interleave their bodies on that one shared thread — which is exactly how two Application/Window-related
/// callbacks each seeing a consistent snapshot of Application.Current could still race WPF's "only one
/// Application per process, ever" restriction. One collection removes that interleaving entirely.
/// </summary>
[CollectionDefinition("STA dispatcher tests", DisableParallelization = true)]
public sealed class StaDispatcherTestsCollection;

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
