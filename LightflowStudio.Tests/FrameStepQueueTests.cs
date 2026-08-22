using System.Windows;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class FrameStepQueueTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RapidRequests_AreSerialized(bool forward)
    {
        var service = new CountingService();
        var queue = new FrameStepQueue();
        var errors = new List<Exception>();

        for (var i = 0; i < 15; i++) queue.RequestStep(service, forward, errors.Add);
        await WaitUntilIdleAsync(queue);

        Assert.Empty(errors);
        Assert.Equal(1, service.MaxConcurrency);
        Assert.Equal(15, forward ? service.ForwardCalls : service.BackwardCalls);
    }

    [Fact]
    public async Task ExtremeInput_IsBoundedAndNeverOverlaps()
    {
        var service = new CountingService(TimeSpan.FromMilliseconds(10));
        var queue = new FrameStepQueue();

        for (var i = 0; i < 100; i++) queue.RequestStep(service, true, _ => { });
        Assert.InRange(queue.PendingDelta, 0, 20);
        await WaitUntilIdleAsync(queue);

        Assert.Equal(1, service.MaxConcurrency);
        Assert.InRange(service.ForwardCalls, 1, 21);
    }

    [Fact]
    public async Task OppositeDirections_CoalesceAsNetIntent()
    {
        var service = new CountingService(TimeSpan.FromMilliseconds(20));
        var queue = new FrameStepQueue();
        queue.RequestStep(service, true, _ => { });
        for (var i = 0; i < 5; i++) queue.RequestStep(service, true, _ => { });
        for (var i = 0; i < 3; i++) queue.RequestStep(service, false, _ => { });

        await WaitUntilIdleAsync(queue);

        Assert.Equal(1, service.MaxConcurrency);
        Assert.Equal(3, service.ForwardCalls);
        Assert.Equal(0, service.BackwardCalls);
    }

    [Fact]
    public async Task Reset_DiscardsPendingOldSessionIntentAndErrors()
    {
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var oldService = new CountingService(onStep: async () =>
        {
            firstStarted.TrySetResult();
            await releaseFirst.Task;
            throw new InvalidOperationException("stale");
        });
        var newService = new CountingService();
        var queue = new FrameStepQueue();
        var errors = new List<Exception>();

        queue.RequestStep(oldService, true, errors.Add);
        queue.RequestStep(oldService, true, errors.Add);
        await firstStarted.Task;
        queue.Reset();
        queue.RequestStep(newService, false, errors.Add);
        releaseFirst.TrySetResult();
        await WaitUntilIdleAsync(queue);

        Assert.Empty(errors);
        Assert.Equal(1, oldService.ForwardCalls);
        Assert.Equal(1, newService.BackwardCalls);
    }

    [Fact]
    public async Task CurrentSessionFailure_IsReportedAndDrainContinues()
    {
        var service = new CountingService(failFirst: true);
        var queue = new FrameStepQueue();
        var errors = new List<Exception>();
        queue.RequestStep(service, true, errors.Add);
        queue.RequestStep(service, true, errors.Add);

        await WaitUntilIdleAsync(queue);

        Assert.Single(errors);
        Assert.Equal(2, service.ForwardCalls);
    }

    [Fact]
    public async Task WaitUntilIdleAsync_CompletesOnlyAfterQueuedStepsSettle()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new CountingService(onStep: () => release.Task);
        var queue = new FrameStepQueue();
        queue.RequestStep(service, true, _ => { });

        var idle = queue.WaitUntilIdleAsync();
        Assert.False(idle.IsCompleted);
        release.SetResult();
        await idle;

        Assert.False(queue.IsDraining);
        Assert.Equal(1, service.ForwardCalls);
    }

    private static async Task WaitUntilIdleAsync(FrameStepQueue queue)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (queue.IsDraining)
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("Frame-step queue did not become idle.");
            await Task.Delay(10);
        }
    }

    private sealed class CountingService(
        TimeSpan? delay = null,
        bool failFirst = false,
        Func<Task>? onStep = null) : IMediaPlaybackService
    {
        private int _active;
        private int _calls;
        public int ForwardCalls { get; private set; }
        public int BackwardCalls { get; private set; }
        public int MaxConcurrency { get; private set; }
        public MediaPlaybackSourceInfo? SourceInfo => new("test", TimeSpan.FromSeconds(10), TimeSpan.Zero, 1, 1, [], null, false);
        public MediaPlaybackSnapshot Snapshot { get; } = new(MediaPlaybackState.Paused, "test", null, TimeSpan.Zero);
        public event EventHandler<MediaPlaybackSnapshot>? StateChanged { add { } remove { } }
        public event EventHandler<MediaPresentationTimestamp>? FramePresented { add { } remove { } }
        public int Volume { get; set; } = 100;
        public bool Mute { get; set; }
        public MediaPlaybackPresentation CreatePresentation() => new(
            new FrameworkElement(), _ => { }, _ => throw new NotSupportedException());
        public Task OpenAsync(string sourcePath, CancellationToken token = default) => Task.CompletedTask;
        public Task CloseAsync(CancellationToken token = default) => Task.CompletedTask;
        public Task PlayAsync(CancellationToken token = default) => Task.CompletedTask;
        public Task PauseAsync(CancellationToken token = default) => Task.CompletedTask;
        public Task SeekAsync(TimeSpan position, CancellationToken token = default) => Task.CompletedTask;
        public Task<MediaDecodedFrame> GetFrameAsync(TimeSpan position, CancellationToken token = default) => throw new NotSupportedException();
        public Task StepForwardAsync(CancellationToken token = default) => StepAsync(true, token);
        public Task StepBackwardAsync(CancellationToken token = default) => StepAsync(false, token);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private async Task StepAsync(bool forward, CancellationToken token)
        {
            if (forward) ForwardCalls++; else BackwardCalls++;
            var active = Interlocked.Increment(ref _active);
            MaxConcurrency = Math.Max(MaxConcurrency, active);
            try
            {
                if (onStep is not null) await onStep();
                if (delay is { } duration) await Task.Delay(duration, token);
                if (failFirst && Interlocked.Increment(ref _calls) == 1) throw new InvalidOperationException("step failed");
            }
            finally { Interlocked.Decrement(ref _active); }
        }
    }
}
