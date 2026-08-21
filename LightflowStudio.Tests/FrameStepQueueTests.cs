using System.Windows;
using Xunit;

namespace LightflowStudio.Tests;

/// <summary>
/// Tests FrameStepQueue's own concurrency-safety contract directly against a fake IMediaPlaybackService —
/// the layer that actually failed under rapid clicking (see FrameStepQueue's doc comment for the proven root
/// cause). MediaPlaybackService's own semaphore already serializes concurrent *entry* into the backend, so a
/// test built on the real service could pass even with the old, broken per-click call pattern; testing
/// FrameStepQueue's own guarantee — never starting request N+1 until request N has genuinely returned — in
/// isolation is what actually pins the fix.
/// </summary>
public sealed class FrameStepQueueTests
{
    [Fact]
    public async Task RapidForwardRequests_NeverOverlapAndAllEventuallyApply()
    {
        var service = new CountingFakeService();
        var queue = new FrameStepQueue();
        var errors = new List<Exception>();

        // Within FrameStepQueue's pending-delta clamp (20): every one of these should eventually apply.
        // RapidForwardRequests_BeyondTheClampStillNeverOverlaps below covers the "burst exceeds the clamp"
        // case, where not every individual request can be expected to survive by design (see RequestStep's
        // own doc comment on the bounded-backlog policy).
        for (var i = 0; i < 15; i++) queue.RequestStep(service, forward: true, errors.Add);

        await WaitUntilIdleAsync(queue);

        Assert.Empty(errors);
        Assert.Equal(1, service.MaxObservedConcurrentSteps);
        Assert.Equal(15, service.ForwardCalls);
        Assert.Equal(0, service.BackwardCalls);
    }

    [Fact]
    public async Task RapidBackwardRequests_NeverOverlapAndAllEventuallyApply()
    {
        var service = new CountingFakeService();
        var queue = new FrameStepQueue();
        var errors = new List<Exception>();

        for (var i = 0; i < 15; i++) queue.RequestStep(service, forward: false, errors.Add);

        await WaitUntilIdleAsync(queue);

        Assert.Empty(errors);
        Assert.Equal(1, service.MaxObservedConcurrentSteps);
        Assert.Equal(15, service.BackwardCalls);
        Assert.Equal(0, service.ForwardCalls);
    }

    [Fact]
    public async Task BeforeBackwardStepAsync_IsAwaitedToCompletionBeforeTheBackwardStepIsIssued()
    {
        // A caller (PlayerViewerHost) uses this hook to capture a "freeze frame" before backward reconstruction
        // can move the underlying position — that only works if the hook is genuinely awaited, not merely
        // started, before the step itself begins. A fake hook that itself asserts ordering (records to a
        // shared log both the hook running and the step running) pins that this isn't a fire-and-forget race.
        var service = new CountingFakeService();
        var queue = new FrameStepQueue();
        var log = new List<string>();
        queue.BeforeBackwardStepAsync = async () =>
        {
            await Task.Delay(30);
            log.Add("hook");
        };
        service.OnBackwardCalled = () => log.Add("step");

        queue.RequestStep(service, forward: false, _ => { });
        await WaitUntilIdleAsync(queue);

        Assert.Equal(["hook", "step"], log);
    }

    [Fact]
    public async Task BeforeBackwardStepAsync_IsNeverAwaitedForAForwardStep()
    {
        var service = new CountingFakeService();
        var queue = new FrameStepQueue();
        var hookCalls = 0;
        queue.BeforeBackwardStepAsync = () => { hookCalls++; return Task.CompletedTask; };

        queue.RequestStep(service, forward: true, _ => { });
        await WaitUntilIdleAsync(queue);

        Assert.Equal(0, hookCalls);
        Assert.Equal(1, service.ForwardCalls);
    }

    [Fact]
    public async Task DrainCompleted_FiresOnceAfterTheWholeBurstSettlesNotBetweenEachIndividualStep()
    {
        var service = new CountingFakeService();
        var queue = new FrameStepQueue();
        var completedCount = 0;
        queue.DrainCompleted += () => completedCount++;

        for (var i = 0; i < 5; i++) queue.RequestStep(service, forward: false, _ => { });
        await WaitUntilIdleAsync(queue);

        Assert.Equal(1, completedCount);
        Assert.Equal(5, service.BackwardCalls);
    }

    [Fact]
    public async Task DrainCompleted_DoesNotFireForAGenerationResetHasSuperseded()
    {
        var service = new CountingFakeService();
        var queue = new FrameStepQueue();
        var completedCount = 0;
        queue.DrainCompleted += () => completedCount++;
        var releaseFirstStep = new TaskCompletionSource();
        service.OnBackwardCalled = () => releaseFirstStep.TrySetResult();

        queue.RequestStep(service, forward: false, _ => { });
        await releaseFirstStep.Task; // the single in-flight step has started, matching a Reset landing mid-step
        queue.Reset();
        await WaitUntilIdleAsync(queue);

        // The completed step's own drain loop must not report completion for a generation Reset already moved
        // past — a caller (PlayerViewerHost) uses DrainCompleted to un-freeze presentation, and un-freezing for
        // a superseded generation could reveal a stale frame from the asset being torn down.
        Assert.Equal(0, completedCount);
    }

    [Fact]
    public async Task RapidForwardRequests_BeyondTheClampStillNeverOverlaps()
    {
        // A genuinely extreme burst (60 requests issued faster than the drain loop could possibly keep up,
        // simulating e.g. a stuck/auto-repeating key) — the pending-delta clamp means not every individual
        // request is guaranteed to survive, but the one property that must never fail regardless is that the
        // engine never sees two step calls in flight at once.
        var service = new CountingFakeService();
        var queue = new FrameStepQueue();
        var errors = new List<Exception>();

        for (var i = 0; i < 60; i++) queue.RequestStep(service, forward: true, errors.Add);

        await WaitUntilIdleAsync(queue);

        Assert.Empty(errors);
        Assert.Equal(1, service.MaxObservedConcurrentSteps);
        Assert.True(service.ForwardCalls > 0);
        Assert.Equal(0, service.BackwardCalls);
    }

    [Fact]
    public async Task RapidAlternatingRequests_NeverOverlapAndNetDeltaDrainsToZero()
    {
        var service = new CountingFakeService();
        var queue = new FrameStepQueue();
        var errors = new List<Exception>();

        // +1 +1 -1 +1 -1 -1 ... an irregular alternating pattern, not a clean cancel-out.
        var pattern = new[] { true, true, false, true, false, false, true, true, true, false };
        for (var round = 0; round < 5; round++)
            foreach (var forward in pattern)
                queue.RequestStep(service, forward, errors.Add);

        await WaitUntilIdleAsync(queue);

        Assert.Empty(errors);
        Assert.Equal(1, service.MaxObservedConcurrentSteps);
        Assert.Equal(0, queue.PendingDelta);
    }

    [Fact]
    public void RequestStep_ClampsUnboundedAccumulationRatherThanGrowingWithoutBound()
    {
        // A never-completing service call keeps the queue permanently "draining" on its first request, so
        // every subsequent request in this test only ever accumulates into the pending delta — proving the
        // clamp holds regardless of how many requests arrive before the engine can process even one.
        var service = new CountingFakeService(stepDelay: Timeout.InfiniteTimeSpan);
        var queue = new FrameStepQueue();
        var errors = new List<Exception>();

        for (var i = 0; i < 200; i++) queue.RequestStep(service, forward: true, errors.Add);

        Assert.True(queue.PendingDelta <= 20, $"Expected the pending delta to be clamped to a small bound, was {queue.PendingDelta}.");
        Assert.True(queue.PendingDelta > 0);
    }

    [Fact]
    public async Task Reset_StopsTheDrainLoopBeforeApplyingFurtherQueuedSteps()
    {
        // A slow-but-finite step lets Reset() land while the queue is still mid-drain (one step already
        // dispatched, several more still pending) — proving teardown discards the backlog rather than
        // continuing to apply steps against a session that is being torn down.
        var service = new CountingFakeService(stepDelay: TimeSpan.FromMilliseconds(200));
        var queue = new FrameStepQueue();
        var errors = new List<Exception>();

        for (var i = 0; i < 10; i++) queue.RequestStep(service, forward: true, errors.Add);
        Assert.True(queue.IsDraining);

        queue.Reset();
        Assert.Equal(0, queue.PendingDelta);

        // The single already-in-flight call still completes (it was already dispatched to the "engine" before
        // Reset ran) — wait for it, then confirm nothing further was ever applied.
        await WaitUntilAsync(() => service.ForwardCalls >= 1, "the already-in-flight step to settle");
        await Task.Delay(50); // give a broken implementation a window to (wrongly) keep draining
        Assert.Equal(1, service.ForwardCalls);
    }

    [Fact]
    public async Task RequestStepForANewSession_StartsItsOwnDrainImmediatelyEvenWhileThePreviousSessionsLoopIsStillFinishing()
    {
        // Reproduces a real sequence: user steps near the end of video A (a slow step is genuinely in flight),
        // opens video B (ReleaseCurrentAsync calls Reset — the old drain loop for A hasn't noticed yet, since
        // it's still awaiting its one in-flight call), and immediately presses Next Frame on B. B's request
        // must start its own drain right away rather than silently accumulating behind a stale "is draining"
        // flag that only belongs to A's now-abandoned loop — proving Reset's generation bump, not a plain
        // "one loop at a time" bool, is what actually gates a new drain start.
        var oldSession = new CountingFakeService(stepDelay: TimeSpan.FromMilliseconds(300));
        var newSession = new CountingFakeService();
        var queue = new FrameStepQueue();
        var errors = new List<Exception>();

        queue.RequestStep(oldSession, forward: true, errors.Add); // starts draining against A
        queue.Reset(); // simulates ReleaseCurrentAsync when opening B — A's loop is still mid-await
        queue.RequestStep(newSession, forward: true, errors.Add); // B's own Next Frame click

        await WaitUntilAsync(() => newSession.ForwardCalls >= 1, "B's step request to be applied rather than silently dropped");

        Assert.Empty(errors);
        Assert.Equal(1, newSession.ForwardCalls);
    }

    [Fact]
    public async Task EngineExceptions_AreObservedThroughOnErrorRatherThanEscaping()
    {
        var service = new CountingFakeService(throwOnStep: true);
        var queue = new FrameStepQueue();
        var errors = new List<Exception>();

        queue.RequestStep(service, forward: true, errors.Add);
        await WaitUntilIdleAsync(queue);

        Assert.Single(errors);
        Assert.IsType<InvalidOperationException>(errors[0]);
    }

    [Fact]
    public async Task OnErrorItselfThrowing_StillReleasesTheDrainLoopRatherThanStrandingTheQueue()
    {
        // If the caller's own error-reporting callback throws (e.g. a UI-thread exception setting status
        // text), the drain loop's ownership must still be released in a finally — otherwise every subsequent
        // RequestStep for this generation would see a permanently "already draining" loop that in fact died,
        // and frame stepping would be silently, permanently inert for the rest of the session.
        var service = new CountingFakeService(throwOnStep: true);
        var queue = new FrameStepQueue();

        queue.RequestStep(service, forward: true, _ => throw new InvalidOperationException("onError itself failed."));
        await WaitUntilAsync(() => !queue.IsDraining, "the loop to release ownership despite onError throwing", timeoutMs: 2000);

        // A second request, after the first loop's onError blew up, must still actually apply — proving the
        // queue recovered rather than being stuck reporting "draining" forever.
        var errors = new List<Exception>();
        queue.RequestStep(service, forward: true, errors.Add);
        await WaitUntilIdleAsync(queue);
        Assert.Single(errors);
    }

    [Fact]
    public async Task OperationCanceledException_FromTheEngineIsSwallowedNotReportedAsAnError()
    {
        // PlaybackFrameStep.RunAsync already swallows OperationCanceledException (matching
        // MediaPlaybackService's "a superseded/cancelled request completes quietly" contract) — the queue
        // must not turn that into a spurious onError call.
        var service = new CountingFakeService(throwCancelled: true);
        var queue = new FrameStepQueue();
        var errors = new List<Exception>();

        queue.RequestStep(service, forward: true, errors.Add);
        await WaitUntilIdleAsync(queue);

        Assert.Empty(errors);
    }

    [Fact]
    public async Task RapidRequests_ThroughTheRealMediaPlaybackServiceNeverOverlapAtTheBackendEitherAndProduceNoUnhandledExceptions()
    {
        // The isolated tests above pin FrameStepQueue's own contract; this proves the full real pipeline
        // (FrameStepQueue -> PlaybackFrameStep -> the real MediaPlaybackService, including its semaphore,
        // generation, and "latest supersedes previous" cancellation machinery -> the backend) stays safe
        // together — the actual integration point where the original bug lived, per FrameStepQueue's own doc
        // comment on the proven root cause.
        var backend = new SlowFakeBackend(TimeSpan.FromMilliseconds(20));
        await using var service = new MediaPlaybackService(backend);
        await service.OpenAsync(Path.GetFullPath("clip.mp4"));
        var queue = new FrameStepQueue();
        var errors = new List<Exception>();

        for (var round = 0; round < 40; round++) queue.RequestStep(service, forward: round % 3 != 0, errors.Add);

        await WaitUntilIdleAsync(queue);

        Assert.Empty(errors);
        Assert.Equal(1, backend.MaxObservedConcurrentSteps);
        Assert.Equal(MediaPlaybackState.Paused, service.Snapshot.State);
    }

    private sealed class SlowFakeBackend(TimeSpan stepDelay) : IMediaPlaybackBackend
    {
        private int _activeSteps;
        public int MaxObservedConcurrentSteps { get; private set; }
        public event EventHandler<MediaPresentationTimestamp>? FramePresented { add { } remove { } }
        public event EventHandler<MediaPlaybackError>? Failed { add { } remove { } }
        public int Volume { get; set; } = 100;
        public bool Mute { get; set; }
        public FrameworkElement CreatePresentationSurface() => new();
        public void ReleasePresentationSurface(FrameworkElement surface) { }
        public void CancelPending() { }
        public Task<PlaybackBackendOpened> OpenAsync(string sourcePath, CancellationToken token)
        {
            var source = new MediaPlaybackSourceInfo(sourcePath, TimeSpan.FromSeconds(60), TimeSpan.Zero, 1920, 1080, [], null, false);
            return Task.FromResult(new PlaybackBackendOpened(source, new(TimeSpan.Zero)));
        }
        public Task CloseAsync(CancellationToken token) => Task.CompletedTask;
        public Task PlayAsync(CancellationToken token) => Task.CompletedTask;
        public Task PauseAsync(CancellationToken token) => Task.CompletedTask;
        public Task<MediaPresentationTimestamp> SeekAsync(TimeSpan position, CancellationToken token) =>
            Task.FromResult(new MediaPresentationTimestamp(position));
        public Task<MediaPresentationTimestamp> StepForwardAsync(CancellationToken token) => StepAsync(token);
        public Task<MediaPresentationTimestamp> StepBackwardAsync(CancellationToken token) => StepAsync(token);
        public Task<MediaDecodedFrame> GetFrameAsync(TimeSpan position, CancellationToken token) => throw new NotSupportedException();
        public Task<MediaDecodedFrame> SnapshotCurrentFrameAsync(CancellationToken token) => throw new NotSupportedException();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private async Task<MediaPresentationTimestamp> StepAsync(CancellationToken token)
        {
            var active = Interlocked.Increment(ref _activeSteps);
            MaxObservedConcurrentSteps = Math.Max(MaxObservedConcurrentSteps, active);
            try
            {
                await Task.Delay(stepDelay, token).ConfigureAwait(false);
                return new MediaPresentationTimestamp(TimeSpan.FromMilliseconds(active));
            }
            finally { Interlocked.Decrement(ref _activeSteps); }
        }
    }

    private static async Task WaitUntilIdleAsync(FrameStepQueue queue) =>
        await WaitUntilAsync(() => !queue.IsDraining, "the queue to finish draining");

    private static async Task WaitUntilAsync(Func<bool> condition, string waitingFor, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException($"Timed out waiting for {waitingFor}.");
            await Task.Delay(5);
        }
    }

    private sealed class CountingFakeService : IMediaPlaybackService
    {
        private readonly TimeSpan _stepDelay;
        private readonly bool _throwOnStep;
        private readonly bool _throwCancelled;
        private int _activeSteps;

        public CountingFakeService(TimeSpan? stepDelay = null, bool throwOnStep = false, bool throwCancelled = false)
        {
            _stepDelay = stepDelay ?? TimeSpan.FromMilliseconds(15);
            _throwOnStep = throwOnStep;
            _throwCancelled = throwCancelled;
        }

        public int ForwardCalls { get; private set; }
        public int BackwardCalls { get; private set; }
        public int MaxObservedConcurrentSteps { get; private set; }
        public Action? OnBackwardCalled { get; set; }

        public MediaPlaybackSnapshot Snapshot { get; } = new(MediaPlaybackState.Paused, "clip.mp4", new(TimeSpan.Zero), TimeSpan.FromSeconds(60));
        public int Volume { get; set; } = 100;
        public bool Mute { get; set; }
        public MediaPlaybackSourceInfo? SourceInfo { get; } = new("clip.mp4", TimeSpan.FromSeconds(60), TimeSpan.Zero, 1920, 1080, [], null, false);
        public event EventHandler<MediaPlaybackSnapshot>? StateChanged { add { } remove { } }
        public event EventHandler<MediaPresentationTimestamp>? FramePresented { add { } remove { } }
        public MediaPlaybackPresentation CreatePresentation() => throw new NotSupportedException();
        public Task OpenAsync(string sourcePath, CancellationToken token = default) => throw new NotSupportedException();
        public Task CloseAsync(CancellationToken token = default) => Task.CompletedTask;
        public Task PlayAsync(CancellationToken token = default) => throw new NotSupportedException();
        public Task PauseAsync(CancellationToken token = default) => throw new NotSupportedException();
        public Task SeekAsync(TimeSpan position, CancellationToken token = default) => throw new NotSupportedException();
        public Task<MediaDecodedFrame> GetFrameAsync(TimeSpan position, CancellationToken token = default) => throw new NotSupportedException();
        public Task<MediaDecodedFrame> SnapshotCurrentFrameAsync(CancellationToken token = default) => throw new NotSupportedException();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public Task StepForwardAsync(CancellationToken token = default) => StepAsync(forward: true, token);
        public Task StepBackwardAsync(CancellationToken token = default) => StepAsync(forward: false, token);

        private async Task StepAsync(bool forward, CancellationToken token)
        {
            var active = Interlocked.Increment(ref _activeSteps);
            MaxObservedConcurrentSteps = Math.Max(MaxObservedConcurrentSteps, active);
            // Signals "genuinely started, not yet complete" — fired before the delay so a caller synchronizing
            // on it (see DrainCompleted_DoesNotFireForAGenerationResetHasSuperseded) gets the full _stepDelay
            // window to act while the step is still in flight, not a race against this same step's own
            // near-simultaneous completion.
            if (!forward) OnBackwardCalled?.Invoke();
            try
            {
                if (_throwCancelled) throw new OperationCanceledException();
                await Task.Delay(_stepDelay, token).ConfigureAwait(false);
                if (_throwOnStep) throw new InvalidOperationException("Simulated engine failure.");
                if (forward) ForwardCalls++; else BackwardCalls++;
            }
            finally { Interlocked.Decrement(ref _activeSteps); }
        }
    }
}
