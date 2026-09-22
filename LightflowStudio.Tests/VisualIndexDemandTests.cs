using Xunit;

namespace LightflowStudio.Tests;

public sealed class VisualIndexDemandTests
{
    [Fact]
    public async Task DecoderCapacityIsBoundedWhileForegroundBypassesLongBackgroundBacklog()
    {
        using var queue = new DerivedFrameDemands(2);
        using var starts = new SemaphoreSlim(0);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var foregroundStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var order = new System.Collections.Concurrent.ConcurrentQueue<string>();
        var requests = new List<Task<string?>>();
        for (var i = 0; i < 20; i++)
        {
            var key = "background-" + i;
            var queued = i >= 2;
            requests.Add(queue.RequestAsync(key, ThumbnailPriority.Background, async token =>
            {
                order.Enqueue(key); starts.Release(); await release.Task.WaitAsync(token);
                if (queued) await foregroundStarted.Task.WaitAsync(token);
                return key;
            }, default));
        }
        await starts.WaitAsync(TimeSpan.FromSeconds(5)); await starts.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, order.Count);
        requests.Add(queue.RequestAsync("foreground", ThumbnailPriority.Visible, _ =>
        { order.Enqueue("foreground"); foregroundStarted.SetResult(); return Task.FromResult<string?>("foreground"); }, default));
        release.SetResult();
        await Task.WhenAll(requests).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.InRange(Array.IndexOf(order.ToArray(), "foreground"), 2, 3);
    }
    [Fact]
    public async Task ForegroundPromotesSharedQueuedFrameAheadOfBackgroundWithoutDuplicateDecode()
    {
        using var queue = new DerivedFrameDemands(1);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var order = new List<string>();
        var first = queue.RequestAsync("running", ThumbnailPriority.Background, async token =>
        { entered.SetResult(); await release.Task.WaitAsync(token); return "running"; }, default);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task<string?> Record(string key, ThumbnailPriority priority) => queue.RequestAsync(key, priority, _ =>
        { lock (order) order.Add(key); return Task.FromResult<string?>(key); }, default);
        var older = Record("older", ThumbnailPriority.Background);
        var shared = Record("shared", ThumbnailPriority.Background);
        var foreground = Record("shared", ThumbnailPriority.Visible);
        var current = Record("current", ThumbnailPriority.Visible);
        release.SetResult();
        await Task.WhenAll(first, older, shared, foreground, current).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(["shared", "current", "older"], order);
        Assert.Equal(await shared, await foreground);
    }

    [Fact]
    public async Task CancellingOneSubscriberKeepsSharedForegroundWorkAlive()
    {
        using var queue = new DerivedFrameDemands(1);
        using var backgroundCancellation = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        async Task<string?> Render(CancellationToken token)
        { Interlocked.Increment(ref calls); entered.SetResult(); await release.Task.WaitAsync(token); return "frame"; }
        var background = queue.RequestAsync("same", ThumbnailPriority.Background, Render, backgroundCancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var foreground = queue.RequestAsync("same", ThumbnailPriority.Visible, Render, default);
        backgroundCancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => background);
        release.SetResult();
        Assert.Equal("frame", await foreground.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task LastCancellationStopsRunningWorkAndCancelledQueuedDemandDoesNotDecode()
    {
        using var queue = new DerivedFrameDemands(1);
        using var cancellation = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var running = queue.RequestAsync("running", ThumbnailPriority.Background, async token =>
        {
            entered.SetResult();
            try { await Task.Delay(Timeout.Infinite, token); return null; }
            finally { stopped.SetResult(); }
        }, cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var queued = queue.RequestAsync("queued", ThumbnailPriority.Background, _ => throw new Exception("Must not decode"), cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued);
        await stopped.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("next", await queue.RequestAsync("next", ThumbnailPriority.Visible, _ => Task.FromResult<string?>("next"), default));
    }

    [Fact]
    public async Task JobsPrepareUnionPerVideoReportMixedResultsAndRetryOnlyFailedVideo()
    {
        var good = Guid.NewGuid(); var bad = Guid.NewGuid();
        var frames = new Frames { Failing = bad };
        await using var jobs = new VisualIndexJobs(frames, (_, _) => Task.FromResult(Metadata()));
        jobs.Initialize();
        jobs.Queue(new(VisualIndexJobs.Capability, [good, bad]), id => id == good ? "good.mov" : "bad.mov");
        await WaitUntilAsync(jobs, () => jobs.Jobs.Count == 2 && jobs.Jobs.All(j => JobsPresentation.IsTerminal(j.State)));
        var successful = jobs.Jobs.Single(j => j.Options.AssetId == good);
        var failed = jobs.Jobs.Single(j => j.Options.AssetId == bad);
        Assert.Equal(JobState.Completed, successful.State);
        Assert.Equal(JobState.Failed, failed.State);
        Assert.Contains("Preparing 12, 24, and 48 frame indexes", failed.Detail);
        Assert.DoesNotContain("assigned Color", failed.Detail);
        Assert.DoesNotContain(failed.Issue, failed.Detail);
        Assert.True(failed.Card(false).CanRetry); Assert.True(failed.WorkspaceItem().CanRetry);
        Assert.DoesNotContain("decoder secret", failed.Detail);
        Assert.Equal(100, successful.Runtime.Progress.OverallPercent);
        var union = VisualIndexSampling.PlanAll(TimeSpan.FromSeconds(60), 25);
        Assert.True(union.Count < 12 + 24 + 48);
        Assert.Equal(union, frames.Requests[good]);
        Assert.All(VisualIndexSampling.Counts, count => Assert.All(VisualIndexSampling.Plan(TimeSpan.FromSeconds(60), 25, count), p => Assert.Contains(p, union)));
        frames.Failing = null;
        Assert.True(jobs.Retry(failed.JobId));
        await WaitUntilAsync(jobs, () => jobs.Jobs.Count == 3 && jobs.Jobs.All(j => JobsPresentation.IsTerminal(j.State)));
        Assert.Equal(2, jobs.Jobs.Count(j => j.State == JobState.Completed));
        Assert.Equal(union.Count, frames.Requests[good].Count);
        Assert.All(frames.Priorities, priority => Assert.Equal(ThumbnailPriority.Background, priority));
    }

    [Fact]
    public async Task VideoCancellationDoesNotCancelAnotherJobAndChangedColorFailsTruthfully()
    {
        var blocked = Guid.NewGuid(); var good = Guid.NewGuid();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var frames = new Frames { Current = false };
        await using var jobs = new VisualIndexJobs(frames, async (id, token) =>
        {
            if (id == blocked) { entered.SetResult(); await Task.Delay(Timeout.Infinite, token); }
            return Metadata();
        });
        jobs.Initialize();
        jobs.Queue(new(VisualIndexJobs.Capability, [blocked, good]), _ => "clip.mov");
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        jobs.Cancel(jobs.Jobs.Single(j => j.Options.AssetId == blocked).JobId);
        await WaitUntilAsync(jobs, () => jobs.Jobs.All(j => JobsPresentation.IsTerminal(j.State)));
        Assert.Equal(JobState.Cancelled, jobs.Jobs.Single(j => j.Options.AssetId == blocked).State);
        Assert.Contains("color changed", jobs.Jobs.Single(j => j.Options.AssetId == good).Issue);
    }

    private static DerivedMetadataResult Metadata() => new(DerivedMetadataStatus.Current,
        new(DerivedMediaKind.Video, "mov", 60, 0, 10, null, new("h264", null, 100, 100, 25, null, null, null, null, null), null, null));
    private static async Task WaitUntilAsync(VisualIndexJobs jobs, Func<bool> predicate)
    {
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Check() { if (predicate()) ready.TrySetResult(); }
        jobs.Changed += Check;
        try { Check(); await ready.Task.WaitAsync(TimeSpan.FromSeconds(10)); }
        finally { jobs.Changed -= Check; }
    }
    private sealed class Frames : IPositionFrameService
    {
        internal Guid? Failing;
        internal bool Current = true;
        internal readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, List<TimeSpan>> Requests = new();
        internal readonly System.Collections.Concurrent.ConcurrentBag<ThumbnailPriority> Priorities = [];
        public Task<MediaAssetResolution?> PrepareAsync(Guid id, CancellationToken token)
        {
            var now = DateTimeOffset.UtcNow;
            return Task.FromResult<MediaAssetResolution?>(new(new(id, Guid.NewGuid(), "clip.mov", "CLIP.MOV", "video", 10, 20,
                new(1, "source"), MediaAssetSourceStatus.Available, now, now, now), MediaRootAvailability.Online, "clip.mov", true));
        }
        public Task<string?> GetAsync(MediaAssetResolution? source, TimeSpan position, CancellationToken token) => throw new NotSupportedException();
        public Task<string?> GetAsync(PositionFrameContext context, TimeSpan position, ThumbnailPriority priority, CancellationToken token)
        {
            var id = context.Source!.Asset.AssetId;
            Priorities.Add(priority);
            Requests.GetOrAdd(id, _ => []).Add(position);
            if (id == Failing) throw new Exception("decoder secret");
            return Task.FromResult<string?>("frame.jpg");
        }
        public Task<bool> IsCurrentAsync(PositionFrameContext context, CancellationToken token) => Task.FromResult(Current);
        public void Dispose() { }
    }
}
