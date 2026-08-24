using System.Collections.Concurrent;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class JobRuntimeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "LightflowStudioTests", Guid.NewGuid().ToString("N"));

    public JobRuntimeTests() => Directory.CreateDirectory(_root);

    [Theory]
    [InlineData(0)]
    [InlineData(9)]
    public void ParallelismOutsideBounds_IsRejected(int value) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => EncodingJobConcurrency.Validate(value));

    [Fact]
    public async Task ConcurrencyOne_PreservesPlanOrderAndNeverOverlaps()
    {
        var gate = new ControlledExecutor();
        await using var runtime = Runtime(3, 1, gate.ExecuteAsync);
        var completion = runtime.StartAsync();

        Assert.Equal(0, await gate.Started.Reader.ReadAsync());
        Assert.Equal(1, gate.MaximumActive);
        gate.Release(0);
        Assert.Equal(1, await gate.Started.Reader.ReadAsync());
        gate.Release(1);
        Assert.Equal(2, await gate.Started.Reader.ReadAsync());
        gate.Release(2);

        var result = await completion;
        Assert.Equal(new[] { 0, 1, 2 }, result.Items.Select(item => item.Data));
    }

    [Fact]
    public async Task ParallelismActuallyOverlapsAndNeverExceedsBound()
    {
        var gate = new ControlledExecutor();
        await using var runtime = Runtime(5, 2, gate.ExecuteAsync);
        var completion = runtime.StartAsync();

        var first = await gate.Started.Reader.ReadAsync();
        var second = await gate.Started.Reader.ReadAsync();
        Assert.NotEqual(first, second);
        Assert.Equal(2, gate.MaximumActive);
        gate.Release(first);
        var third = await gate.Started.Reader.ReadAsync();
        gate.Release(second);
        var fourth = await gate.Started.Reader.ReadAsync();
        gate.Release(third);
        var fifth = await gate.Started.Reader.ReadAsync();
        gate.Release(fourth);
        gate.Release(fifth);

        await completion;
        Assert.Equal(2, gate.MaximumActive);
    }

    [Fact]
    public async Task PauseDrainsActiveWorkAndResumeStartsWaitingWork()
    {
        var gate = new ControlledExecutor();
        await using var runtime = Runtime(4, 2, gate.ExecuteAsync);
        var completion = runtime.StartAsync();
        var first = await gate.Started.Reader.ReadAsync();
        var second = await gate.Started.Reader.ReadAsync();

        runtime.Pause();
        Assert.Equal(JobState.Pausing, runtime.Snapshot().State);
        gate.Release(first);
        gate.Release(second);
        await Eventually(() => runtime.Snapshot().State == JobState.Paused);
        Assert.Equal(2, runtime.Snapshot().Counts.Waiting);

        runtime.Resume();
        var third = await gate.Started.Reader.ReadAsync();
        var fourth = await gate.Started.Reader.ReadAsync();
        gate.Release(third);
        gate.Release(fourth);
        Assert.Equal(JobState.Completed, (await completion).State);
    }

    [Fact]
    public async Task RepeatedPauseResumeDoesNotRematerializeDefinition()
    {
        var plan = Plan(2);
        var original = plan.Definition.Items.Select(item => (item.Id, item.SourceIdentity, item.MediaRange, item.AssignedColor)).ToArray();
        var gate = new ControlledExecutor();
        await using var runtime = new JobRuntime<EncodingJobOptions, int>(plan, 1, gate.ExecuteAsync);
        var completion = runtime.StartAsync();
        var first = await gate.Started.Reader.ReadAsync();
        runtime.Pause();
        runtime.Pause();
        gate.Release(first);
        await Eventually(() => runtime.Snapshot().State == JobState.Paused);
        runtime.Resume();
        runtime.Resume();
        var second = await gate.Started.Reader.ReadAsync();
        gate.Release(second);
        await completion;
        Assert.Equal(original, plan.Definition.Items.Select(item => (item.Id, item.SourceIdentity, item.MediaRange, item.AssignedColor)).ToArray());
    }

    [Fact]
    public async Task CancelStopsWaitingItemsAndCancelsActiveItems()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var active = 0;
        async Task<JobItemResult<int>> Execute(JobPlanItem item, IProgress<double> _, CancellationToken token)
        {
            if (Interlocked.Increment(ref active) == 2) started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return Completed(item);
        }

        await using var runtime = Runtime(5, 2, Execute);
        var completion = runtime.StartAsync();
        await started.Task;
        runtime.Cancel();
        var result = await completion;

        Assert.Equal(5, result.Summary.Cancelled);
        Assert.Equal(JobState.Cancelled, result.State);
    }

    [Fact]
    public async Task CancelWhileFullyPausedCompletesNormallyWithoutStartingWaitingItems()
    {
        var gate = new ControlledExecutor();
        await using var runtime = Runtime(4, 2, gate.ExecuteAsync);
        var observer = new SnapshotObserver<int>();
        using var subscription = runtime.Subscribe(observer);
        var completion = runtime.StartAsync();
        var first = await gate.Started.Reader.ReadAsync();
        var second = await gate.Started.Reader.ReadAsync();
        runtime.Pause();
        gate.Release(first);
        gate.Release(second);
        await observer.WaitForAsync(snapshot => snapshot.State == JobState.Paused);

        runtime.Cancel();
        var result = await completion;

        Assert.Equal(TaskStatus.RanToCompletion, completion.Status);
        Assert.Equal(JobState.Cancelled, result.State);
        Assert.Equal(2, result.Summary.Completed);
        Assert.Equal(2, result.Summary.Cancelled);
        Assert.Equal(2, gate.StartCount);
        Assert.All(result.Items.Skip(2), item => Assert.Equal(JobState.Cancelled, item.State));
    }

    [Fact]
    public async Task CancelWhilePausingCancelsActiveAndWaitingWithoutStartingMoreWork()
    {
        var gate = new ControlledExecutor();
        await using var runtime = Runtime(5, 2, gate.ExecuteAsync);
        var observer = new SnapshotObserver<int>();
        using var subscription = runtime.Subscribe(observer);
        var completion = runtime.StartAsync();
        var first = await gate.Started.Reader.ReadAsync();
        _ = await gate.Started.Reader.ReadAsync();
        runtime.Pause();
        gate.Release(first);
        await observer.WaitForAsync(snapshot => snapshot.State == JobState.Pausing
                                                && snapshot.Counts.Running == 1
                                                && snapshot.Counts.Completed == 1);

        runtime.Cancel();
        var cancelling = await observer.WaitForAsync(snapshot => snapshot.State == JobState.Cancelling);
        var result = await completion;

        Assert.Null(cancelling.Eta);
        Assert.Equal(TaskStatus.RanToCompletion, completion.Status);
        Assert.Equal(JobState.Cancelled, result.State);
        Assert.Equal(1, result.Summary.Completed);
        Assert.Equal(4, result.Summary.Cancelled);
        Assert.Equal(2, gate.StartCount);
    }

    [Fact]
    public async Task ActiveTimeIncludesDrainExcludesFullPauseAndFeedsResumedEta()
    {
        var clock = new ManualActiveClock();
        var gate = new ControlledExecutor();
        var plan = Plan(2);
        await using var runtime = new JobRuntime<EncodingJobOptions, int>(plan, 1, gate.ExecuteAsync, clock);
        var observer = new SnapshotObserver<int>();
        using var subscription = runtime.Subscribe(observer);
        var completion = runtime.StartAsync();
        var first = await gate.Started.Reader.ReadAsync();
        clock.Advance(TimeSpan.FromSeconds(10));

        runtime.Pause();
        Assert.True(clock.IsRunning);
        clock.Advance(TimeSpan.FromSeconds(5));
        gate.Release(first);
        var paused = await observer.WaitForAsync(snapshot => snapshot.State == JobState.Paused);
        Assert.False(clock.IsRunning);
        Assert.Equal(TimeSpan.FromSeconds(15), paused.Elapsed);
        clock.Advance(TimeSpan.FromMinutes(3));
        Assert.Equal(TimeSpan.FromSeconds(15), runtime.Snapshot().Elapsed);

        runtime.Resume();
        _ = await gate.Started.Reader.ReadAsync();
        clock.Advance(TimeSpan.FromSeconds(5));
        gate.Report(1, 50);
        var resumed = runtime.Snapshot();
        Assert.Equal(TimeSpan.FromSeconds(20), resumed.Elapsed);
        Assert.Equal(TimeSpan.FromSeconds(10), resumed.Eta);

        runtime.Pause();
        Assert.True(clock.IsRunning);
        clock.Advance(TimeSpan.FromSeconds(3));
        gate.Release(1);
        var result = await completion;
        Assert.Equal(JobState.Completed, result.State);
        Assert.Equal(TimeSpan.FromSeconds(23), runtime.Snapshot().Elapsed);
        Assert.False(clock.IsRunning);
    }

    [Fact]
    public async Task FailureDoesNotCorruptSiblingResultsOrOrdering()
    {
        var plan = Plan(4);
        await using var runtime = new JobRuntime<EncodingJobOptions, int>(plan, 3,
            (item, _, _) => Task.FromResult(Index(item) == 1
                ? new JobItemResult<int>(item.Definition.Id, JobState.Failed, item.OutputPaths, [], ["broken"], 1)
                : Completed(item)));

        var result = await runtime.StartAsync();
        Assert.Equal(plan.Items.Select(item => item.Definition.Id), result.Items.Select(item => item.ItemId));
        Assert.Equal(JobState.Failed, result.Items[1].State);
        Assert.Equal(3, result.Summary.Completed);
    }

    [Fact]
    public async Task IndependentProgressProducesMonotonicWeightedAggregate()
    {
        var reports = new ConcurrentDictionary<int, IProgress<double>>();
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releases = Enumerable.Range(0, 2).Select(_ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).ToArray();
        async Task<JobItemResult<int>> Execute(JobPlanItem item, IProgress<double> progress, CancellationToken _)
        {
            reports[Index(item)] = progress;
            if (reports.Count == 2) ready.SetResult();
            await releases[Index(item)].Task;
            return Completed(item);
        }
        await using var runtime = Runtime(2, 2, Execute);
        var completion = runtime.StartAsync();
        await ready.Task;
        reports[0].Report(50);
        var first = runtime.Snapshot();
        reports[1].Report(25);
        var second = runtime.Snapshot();
        Assert.Equal(50, first.Items[0].ProgressPercent);
        Assert.Equal(0, first.Items[1].ProgressPercent ?? 0);
        Assert.True(second.Progress.OverallPercent >= first.Progress.OverallPercent);
        releases[0].SetResult(); releases[1].SetResult();
        Assert.Equal(100, (await completion).Items.Count == 2 ? runtime.Snapshot().Progress.OverallPercent : 0);
    }

    [Fact]
    public void RuntimeStore_RoundTripsAndRunningRecoveryNeedsAttention()
    {
        var plan = Plan(2);
        var items = plan.Items.Select((item, index) => new JobItemRuntimeSnapshot<int>(item.Definition.Id, index,
            index == 0 ? JobState.Completed : JobState.Running, index == 0 ? 100 : 25,
            DateTimeOffset.UtcNow, index == 0 ? DateTimeOffset.UtcNow : null, [], [], index == 0 ? 0 : -1)).ToList();
        var snapshot = Snapshot(plan, JobState.Running, items);
        var store = new JobRuntimeStore<EncodingJobOptions, int>(Path.Combine(_root, "jobs.json"));
        store.Save(plan, snapshot, false);

        var recovered = Assert.IsType<RecoveredJob<EncodingJobOptions, int>>(store.Load((_, _) => []));
        Assert.Equal(JobRecoveryDisposition.NeedsAttention, recovered.Disposition);
        Assert.Equal(JobState.Completed, recovered.Checkpoint.Runtime.Items[0].State);

        store.Save(plan, snapshot with { State = JobState.Cancelling }, false);
        Assert.Equal(JobRecoveryDisposition.NeedsAttention, store.Load((_, _) => [])!.Disposition);
    }

    [Fact]
    public void RuntimeStore_PreservesPausedAndWaitingPoliciesAndRejectsStaleResources()
    {
        var plan = Plan(1);
        var item = new JobItemRuntimeSnapshot<int>(plan.Items[0].Definition.Id, 0, JobState.Queued, null, null, null, [], [], -1);
        var store = new JobRuntimeStore<EncodingJobOptions, int>(Path.Combine(_root, "jobs.json"));
        store.Save(plan, Snapshot(plan, JobState.Paused, [item]), true);
        Assert.Equal(JobRecoveryDisposition.Paused, store.Load((_, _) => [])!.Disposition);

        store.Save(plan, Snapshot(plan, JobState.Queued, [item]), false);
        Assert.Equal(JobRecoveryDisposition.Waiting, store.Load((_, _) => [])!.Disposition);
        Assert.Equal(JobRecoveryDisposition.NeedsAttention, store.Load((_, _) =>
            [new JobIssue("source.stale", "Source changed.", JobIssueSeverity.Error)])!.Disposition);
    }

    [Fact]
    public void RuntimeStore_PreservesMultipleJobsWithoutOverwritingSiblings()
    {
        var first = Plan(1);
        var second = Plan(2);
        var store = new JobRuntimeStore<EncodingJobOptions, int>(Path.Combine(_root, "jobs.json"));
        store.Save(first, Snapshot(first, JobState.Queued,
            [new(first.Items[0].Definition.Id, 0, JobState.Queued, null, null, null, [], [], -1)]), false);
        store.Save(second, Snapshot(second, JobState.Paused, second.Items.Select((item, index) =>
            new JobItemRuntimeSnapshot<int>(item.Definition.Id, index, JobState.Queued, null, null, null, [], [], -1)).ToList()), true);

        var recovered = store.LoadAll((_, _) => []);
        Assert.Equal(2, recovered.Count);
        Assert.Contains(recovered, job => job.Disposition == JobRecoveryDisposition.Waiting);
        Assert.Contains(recovered, job => job.Disposition == JobRecoveryDisposition.Paused);
    }

    [Fact]
    public async Task ApplicationRuntimePublishesTypedShellWideStateAndShutsDownWorkers()
    {
        var checkpoints = 0;
        await using var service = new ApplicationJobsRuntime<EncodingJobOptions, int>((_, _, _) => checkpoints++);
        var gate = new ControlledExecutor();
        var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        service.Changed += jobs => { if (jobs.Any(job => job.State == JobState.Running)) changed.TrySetResult(); };
        var plan = Plan(3);
        service.Queue(plan, 2, gate.ExecuteAsync);
        await changed.Task;

        Assert.Single(service.Jobs);
        Assert.True(checkpoints > 0);
        Assert.True(service.Cancel(plan.Definition.Id));
        await Eventually(() => service.Jobs[0].State == JobState.Cancelled);
    }

    [Fact]
    public void EncodingRecoveryRejectsChangedSourceAndMismatchedExistingOutput()
    {
        var source = Path.Combine(_root, "source.mp4");
        var output = Path.Combine(_root, "output.mp4");
        File.WriteAllText(source, "changed");
        File.WriteAllText(output, "not-a-materialized-output");
        var options = new EncodingJobOptions(_root, _root, OutputResolution.Source, RecoveryStrategy.Normal,
            new EncodingOptions(), null, "_out", false, false, false);
        var definition = new JobItemDefinition(Guid.NewGuid(), source, 1, SourceLastWriteUtcTicks: 1);
        var item = new JobPlanItem(definition, [output], JobPlanDisposition.Process,
            JobWorkEstimate.Determinate(JobWorkUnit.Items, 1), []);

        var issues = EncodingJobRecovery.Revalidate(item, options, Path.Combine(_root, "identities"));

        Assert.Contains(issues, issue => issue.Code == "jobs.source-size-changed");
        Assert.Contains(issues, issue => issue.Code == "jobs.source-modified");
        Assert.Contains(issues, issue => issue.Code == "jobs.output-identity-changed");
    }

    private JobRuntime<EncodingJobOptions, int> Runtime(int count, int concurrency,
        Func<JobPlanItem, IProgress<double>, CancellationToken, Task<JobItemResult<int>>> execute) =>
        new(Plan(count), concurrency, execute);

    private JobPlan<EncodingJobOptions> Plan(int count)
    {
        var options = new EncodingJobOptions(_root, _root, OutputResolution.Source, RecoveryStrategy.Normal,
            new EncodingOptions(), null, "_out", false, false, false, ParallelExports: 2);
        var definitions = Enumerable.Range(0, count).Select(index => new JobItemDefinition(
            Guid.NewGuid(), Path.Combine(_root, $"source-{index}.mp4"), 1,
            new MediaRange(TimeSpan.FromSeconds(index + 1)))).ToList();
        var definition = new JobDefinition<EncodingJobOptions>(Guid.NewGuid(), "video.encode", DateTimeOffset.UtcNow, options, definitions);
        var items = definitions.Select((item, index) => new JobPlanItem(item,
            [Path.Combine(_root, $"output-{index}.mp4")], JobPlanDisposition.Process,
            JobWorkEstimate.Determinate(JobWorkUnit.MediaDuration, index + 1), [])).ToList();
        return new(definition, DateTimeOffset.UtcNow, items, [], JobWorkUnit.MediaDuration);
    }

    private static JobItemResult<int> Completed(JobPlanItem item) =>
        new(item.Definition.Id, JobState.Completed, item.OutputPaths, [], [], Index(item));
    private static int Index(JobPlanItem item) => int.Parse(Path.GetFileNameWithoutExtension(item.Definition.SourceIdentity).Split('-')[1]);
    private static JobRuntimeSnapshot<int> Snapshot(JobPlan<EncodingJobOptions> plan, JobState state, IReadOnlyList<JobItemRuntimeSnapshot<int>> items) =>
        new(plan.Definition.Id, state, plan.Definition.CreatedAt, DateTimeOffset.UtcNow, null, TimeSpan.Zero, null,
            new JobProgressSnapshot(0, null, 0, plan.Items.Count, plan.WorkUnit),
            new JobRuntimeCounts(items.Count, items.Count(item => item.State == JobState.Queued), items.Count(item => item.State == JobState.Running),
                items.Count(item => item.State == JobState.Completed), 0, 0, 0), items, [], []);

    private static async Task Eventually(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition()) await Task.Delay(10, timeout.Token);
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    private sealed class ControlledExecutor
    {
        private readonly ConcurrentDictionary<int, TaskCompletionSource> _releases = new();
        private int _active;
        private int _startCount;
        private readonly ConcurrentDictionary<int, IProgress<double>> _progress = new();
        public System.Threading.Channels.Channel<int> Started { get; } = System.Threading.Channels.Channel.CreateUnbounded<int>();
        public int MaximumActive { get; private set; }
        public int StartCount => Volatile.Read(ref _startCount);

        public async Task<JobItemResult<int>> ExecuteAsync(JobPlanItem item, IProgress<double> progress, CancellationToken token)
        {
            var index = Index(item);
            var release = _releases.GetOrAdd(index, _ => new(TaskCreationOptions.RunContinuationsAsynchronously));
            _progress[index] = progress;
            Interlocked.Increment(ref _startCount);
            var active = Interlocked.Increment(ref _active);
            MaximumActive = Math.Max(MaximumActive, active);
            await Started.Writer.WriteAsync(index, token);
            try { await release.Task.WaitAsync(token); }
            finally { Interlocked.Decrement(ref _active); }
            progress.Report(100);
            return Completed(item);
        }

        public void Release(int index) => _releases.GetOrAdd(index, _ => new()).TrySetResult();
        public void Report(int index, double percent) => _progress[index].Report(percent);
    }

    private sealed class ManualActiveClock : IJobActiveClock
    {
        private readonly object _sync = new();
        private TimeSpan _elapsed;
        private bool _isRunning;
        public bool IsRunning { get { lock (_sync) return _isRunning; } }
        public TimeSpan Elapsed { get { lock (_sync) return _elapsed; } }
        public void Start() { lock (_sync) _isRunning = true; }
        public void Stop() { lock (_sync) _isRunning = false; }
        public void Advance(TimeSpan value) { lock (_sync) if (_isRunning) _elapsed += value; }
    }

    private sealed class SnapshotObserver<TData> : IJobRuntimeObserver<TData>
    {
        private readonly System.Threading.Channels.Channel<JobRuntimeSnapshot<TData>> _snapshots =
            System.Threading.Channels.Channel.CreateUnbounded<JobRuntimeSnapshot<TData>>();
        public void OnChanged(JobRuntimeSnapshot<TData> snapshot) => _snapshots.Writer.TryWrite(snapshot);
        public async Task<JobRuntimeSnapshot<TData>> WaitForAsync(Func<JobRuntimeSnapshot<TData>, bool> predicate)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (await _snapshots.Reader.ReadAsync(timeout.Token) is { } snapshot)
                if (predicate(snapshot)) return snapshot;
            throw new InvalidOperationException("The expected runtime snapshot was not observed.");
        }
    }
}
