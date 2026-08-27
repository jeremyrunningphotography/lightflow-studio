using System.Diagnostics;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class GlobalExportSchedulerTests
{
    [Fact]
    public async Task HighFrequencyProgressHasBoundedDurableWritesButEveryRuntimeUpdateRemainsObservable()
    {
        var store = new MemoryQueueStore();
        var changed = 0;
        await using var scheduler = new GlobalExportScheduler(1, () => new((item, _, progress, _) =>
        {
            for (var value = 0; value <= 1000; value++) progress.Report(value / 10d);
            return Task.FromResult(new JobItemResult<EncodingItemResult>(item.Definition.Id, JobState.Completed,
                item.OutputPaths, [], [], new(0, TimeSpan.FromSeconds(1), null, TimeSpan.FromSeconds(1))));
        }, () => { }), store);
        scheduler.Changed += _ => changed++;

        scheduler.Admit(Proposal("progress", 1));
        await WaitUntilAsync(() => scheduler.Jobs.Single().State == JobState.Completed);

        Assert.True(changed >= 1000, $"Expected every in-memory progress notification; observed {changed}.");
        Assert.InRange(store.SaveCount, 20, 25);
        var checkpoint = Assert.Single(store.Jobs);
        Assert.Equal(JobState.Completed, checkpoint.State);
        Assert.Equal(100, checkpoint.ProgressPercent);
    }

    [Fact]
    public async Task SubmissionCreatesOneIndependentJobPerSourceAndSharesGlobalCeiling()
    {
        var harness = new Harness(2);
        var admission = harness.Scheduler.Admit(Proposal("one", 10));
        Assert.True(admission.Accepted);
        Assert.Equal(10, admission.Jobs.Count);
        await WaitUntilAsync(() => harness.Scheduler.Jobs.Count(x => x.State == JobState.Running) == 2);
        Assert.Equal(8, harness.Scheduler.Jobs.Count(x => x.State == JobState.Queued));
        Assert.Single(harness.Scheduler.Jobs.Select(x => x.Definition.SubmissionId).Distinct());
        await harness.DisposeAsync();
    }

    [Fact]
    public async Task MultiJobAdmissionPublishesOneSubmissionAcceptedAutoRevealSignal()
    {
        var harness = new Harness(1);
        var accepted = new List<Guid>();
        harness.Scheduler.SubmissionAccepted += accepted.Add;

        var admission = harness.Scheduler.Admit(Proposal("auto-reveal", 3));

        Assert.True(admission.Accepted);
        Assert.Equal(3, admission.Jobs.Count);
        Assert.Equal(admission.Jobs[0].Definition.SubmissionId, Assert.Single(accepted));
        await harness.DisposeAsync();
    }

    [Fact]
    public async Task SeparateSubmissionsUseOneQueueAndLiveIncreaseStartsMore()
    {
        var harness = new Harness(1);
        harness.Scheduler.Admit(Proposal("a", 2));
        harness.Scheduler.Admit(Proposal("b", 2));
        await WaitUntilAsync(() => harness.Running == 1);
        harness.Scheduler.MaxSimultaneousExports = 3;
        await WaitUntilAsync(() => harness.Running == 3);
        Assert.Equal(3, harness.MaximumObserved);
        await harness.DisposeAsync();
    }

    [Fact]
    public async Task ConcurrencyChangePublishesApplicationPolicyForPersistence()
    {
        var persisted = 0;
        await using var scheduler = new GlobalExportScheduler(2, () => new((item, _, _, _) =>
            Task.FromResult(new JobItemResult<EncodingItemResult>(item.Definition.Id, JobState.Completed,
                item.OutputPaths, [], [], null)), () => { }), persistMaximum: value => persisted = value);
        scheduler.MaxSimultaneousExports = 5;
        Assert.Equal(5, persisted);
        Assert.Equal(5, scheduler.MaxSimultaneousExports);
    }

    [Fact]
    public async Task LiveDecreaseDoesNotTerminateRunningJobsOrStartMoreUntilBelowLimit()
    {
        var harness = new Harness(4);
        harness.Scheduler.Admit(Proposal("a", 6));
        await WaitUntilAsync(() => harness.Running == 4);
        harness.Scheduler.MaxSimultaneousExports = 2;
        Assert.Equal(4, harness.Running);
        harness.CompleteOne();
        await WaitUntilAsync(() => harness.Running == 3);
        Assert.Equal(3, harness.Running);
        harness.CompleteOne();
        await WaitUntilAsync(() => harness.Running == 2);
        Assert.Equal(2, harness.Running);
        await harness.DisposeAsync();
    }

    [Fact]
    public async Task ReservationCollisionIsCaseInsensitiveAndAdmissionIsAllOrNothing()
    {
        var harness = new Harness(1);
        Assert.True(harness.Scheduler.Admit(Proposal("first", 1, "C:\\Output")).Accepted);
        var conflicting = Proposal("second", 2, "c:\\output");
        var first = conflicting.Jobs[0];
        conflicting = conflicting with { Jobs = [first with { PlanItem = first.PlanItem with { OutputPaths = ["c:\\output\\FIRST-1.MP4"] } }, conflicting.Jobs[1]] };
        var rejected = harness.Scheduler.Admit(conflicting);
        Assert.False(rejected.Accepted);
        Assert.Single(harness.Scheduler.Jobs);
        Assert.Equal("export.queue-reserved", Assert.Single(rejected.Issues).Code);
        await harness.DisposeAsync();
    }

    [Fact]
    public async Task SameFilenameInDifferentDirectoriesIsAllowed()
    {
        var harness = new Harness(1);
        Assert.True(harness.Scheduler.Admit(Proposal("same", 1, "C:\\one", "clip.mp4")).Accepted);
        Assert.True(harness.Scheduler.Admit(Proposal("same", 1, "C:\\two", "clip.mp4")).Accepted);
        Assert.Equal(2, harness.Scheduler.Jobs.Count);
        await harness.DisposeAsync();
    }

    [Fact]
    public async Task FinalAndPartialPathsShareTheSameReservationNamespace()
    {
        var harness = new Harness(1);
        Assert.True(harness.Scheduler.Admit(Proposal("clip", 1)).Accepted);
        var proposed = Proposal("other", 1);
        var job = proposed.Jobs[0];
        var ownedPartial = EncodingOutputLifecycle.PartialPathFor(harness.Scheduler.Jobs[0].OutputPath);
        proposed = proposed with { Jobs = [job with { PlanItem = job.PlanItem with { OutputPaths = [ownedPartial] } }] };
        var rejected = harness.Scheduler.Admit(proposed);
        Assert.False(rejected.Accepted);
        Assert.Equal("export.queue-reserved", Assert.Single(rejected.Issues).Code);
        await harness.DisposeAsync();
    }

    [Fact]
    public async Task QueueTimeFileAppearanceRejectsWholeSubmissionUnlessOverwriteIsAuthorized()
    {
        var visible = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var scheduler = new GlobalExportScheduler(1, () => new((item, _, _, _) =>
            Task.FromResult(new JobItemResult<EncodingItemResult>(item.Definition.Id, JobState.Completed,
                item.OutputPaths, [], [], null)), () => { }), inspectOutput: path => new(visible.Contains(path), 1));
        var proposal = Proposal("late", 2);
        visible.Add(proposal.Jobs[0].OutputPath);
        var recipe = proposal.Jobs[0].Recipe with { OverwriteExistingFiles = false };
        proposal = proposal with { Jobs = proposal.Jobs.Select(job => job with { Recipe = recipe }).ToList() };
        Assert.False(scheduler.Admit(proposal).Accepted);
        Assert.Empty(scheduler.Jobs);
    }

    [Fact]
    public async Task ConcurrentAdmissionsCannotRaceTheSameReservation()
    {
        var harness = new Harness(1);
        var proposals = Enumerable.Range(0, 12).Select(_ => Proposal("race", 1, "C:\\output", "same.mp4")).ToList();
        var results = await Task.WhenAll(proposals.Select(proposal => Task.Run(() => harness.Scheduler.Admit(proposal))));
        Assert.Single(results, result => result.Accepted);
        Assert.Single(harness.Scheduler.Jobs);
        await harness.DisposeAsync();
    }

    [Fact]
    public async Task WaitingPauseResumeAndCancelAreIndependent()
    {
        var harness = new Harness(1);
        harness.Scheduler.Admit(Proposal("jobs", 3));
        await WaitUntilAsync(() => harness.Scheduler.Jobs.Count(x => x.State == JobState.Running) == 1);
        var waiting = harness.Scheduler.Jobs.Where(x => x.State == JobState.Queued).ToList();
        Assert.True(harness.Scheduler.Pause(waiting[0].JobId));
        Assert.True(harness.Scheduler.Cancel(waiting[1].JobId));
        Assert.Equal(JobState.Paused, harness.Scheduler.Jobs.Single(x => x.JobId == waiting[0].JobId).State);
        Assert.Equal(JobState.Cancelled, harness.Scheduler.Jobs.Single(x => x.JobId == waiting[1].JobId).State);
        Assert.True(harness.Scheduler.Resume(waiting[0].JobId));
        Assert.Equal(JobState.Queued, harness.Scheduler.Jobs.Single(x => x.JobId == waiting[0].JobId).State);
        await harness.DisposeAsync();
    }

    [Fact]
    public async Task PausedQueueAdmitsDurablyWithoutStartingAndResumeHonorsQueueOrderAndCeiling()
    {
        var persisted = false;
        var harness = new Harness(2, isQueuePaused: true, persistQueuePaused: value => persisted = value);
        var admission = harness.Scheduler.Admit(Proposal("held", 4));
        Assert.True(admission.Accepted);
        Assert.True(harness.Scheduler.IsQueuePaused);
        Assert.All(admission.Jobs, job => Assert.Equal(JobState.Queued,
            harness.Scheduler.Jobs.Single(snapshot => snapshot.JobId == job.JobId).State));
        Assert.Empty(harness.Started);

        var last = admission.Jobs[^1].JobId;
        Assert.True(harness.Scheduler.MoveWaiting(last, -1));
        var expectedFirstClaims = harness.Scheduler.Jobs.Where(job => job.State == JobState.Queued)
            .OrderBy(job => job.QueueOrder).Take(2).Select(job => job.JobId).ToList();
        Assert.True(harness.Scheduler.ResumeQueue());
        await WaitUntilAsync(() => harness.Running == 2);

        Assert.False(persisted);
        // Both eligible Jobs are claimed under one scheduler decision, but their independent executor tasks may
        // enter the harness in either order. Verify the claimed set and ceiling without imposing thread timing.
        Assert.Equal(expectedFirstClaims.Order(), harness.Started.Order());
        Assert.Equal(2, harness.MaximumObserved);
        await harness.DisposeAsync();
    }

    [Fact]
    public async Task PausingQueueLetsRunningFinishButBlocksEveryClaimTriggerUntilResume()
    {
        var persisted = false;
        var harness = new Harness(2, persistQueuePaused: value => persisted = value);
        harness.Scheduler.Admit(Proposal("gate", 4));
        await WaitUntilAsync(() => harness.Running == 2);

        Assert.True(harness.Scheduler.PauseQueue());
        Assert.True(persisted);
        harness.Scheduler.MaxSimultaneousExports = 4;
        harness.Scheduler.Admit(Proposal("new", 1));
        harness.CompleteOne();
        harness.CompleteOne();
        await WaitUntilAsync(() => harness.Running == 0);

        Assert.Equal(2, harness.Started.Count);
        Assert.Equal(3, harness.Scheduler.Jobs.Count(job => job.State == JobState.Queued));
        var individuallyPaused = harness.Scheduler.Jobs.First(job => job.State == JobState.Queued).JobId;
        Assert.True(harness.Scheduler.Pause(individuallyPaused));
        Assert.True(harness.Scheduler.Resume(individuallyPaused));
        Assert.True(harness.Scheduler.Cancel(individuallyPaused));
        Assert.Equal(2, harness.Started.Count);

        Assert.True(harness.Scheduler.ResumeQueue());
        await WaitUntilAsync(() => harness.Running == 2);
        Assert.Equal(4, harness.Started.Count);
        await harness.DisposeAsync();
    }

    [Fact]
    public async Task PersistedQueueGateSurvivesRestartAndRunningRecoveryStillNeedsAttention()
    {
        var store = new MemoryQueueStore();
        var proposal = Proposal("restart-held", 2);
        store.Save([
            new(proposal.Jobs[0], JobState.Running, 22, DateTimeOffset.Now, null, TimeSpan.Zero, [], [], null),
            new(proposal.Jobs[1], JobState.Queued, null, null, null, TimeSpan.Zero, [], [], null)]);
        var starts = 0;
        await using var restored = new GlobalExportScheduler(2, () =>
        {
            Interlocked.Increment(ref starts);
            throw new UnreachableException();
        }, store, isQueuePaused: true);

        Assert.True(restored.IsQueuePaused);
        Assert.Contains(restored.Jobs, job => job.State == JobState.NeedsAttention);
        Assert.Contains(restored.Jobs, job => job.State == JobState.Queued);
        restored.MaxSimultaneousExports = 3;
        Assert.Equal(0, starts);
    }

    [Fact]
    public async Task WaitingReorderChangesNextClaimAndRunningCannotMove()
    {
        var harness = new Harness(1);
        harness.Scheduler.Admit(Proposal("order", 3));
        await WaitUntilAsync(() => harness.Running == 1);
        var ordered = harness.Scheduler.Jobs.OrderBy(x => x.QueueOrder).ToList();
        Assert.False(harness.Scheduler.MoveWaiting(ordered[0].JobId, 1));
        Assert.True(harness.Scheduler.MoveWaiting(ordered[2].JobId, -1));
        harness.CompleteOne();
        await WaitUntilAsync(() => harness.Started.Count >= 2);
        Assert.Equal(ordered[2].JobId, harness.Started[1]);
        await harness.DisposeAsync();
    }

    [Fact]
    public async Task RunningCancelTerminatesOnlySelectedJobAndSiblingContinues()
    {
        var harness = new Harness(2);
        harness.Scheduler.Admit(Proposal("cancel", 3));
        await WaitUntilAsync(() => harness.Running == 2);
        var running = harness.Scheduler.Jobs.Where(x => x.State == JobState.Running).ToList();
        Assert.True(harness.Scheduler.Cancel(running[0].JobId));
        await WaitUntilAsync(() => harness.Scheduler.Jobs.Single(x => x.JobId == running[0].JobId).State == JobState.Cancelled);
        Assert.NotEqual(JobState.Cancelled, harness.Scheduler.Jobs.Single(x => x.JobId == running[1].JobId).State);
        await harness.DisposeAsync();
    }

    [Fact]
    public async Task ExplicitCancelIsTerminalAndDurable()
    {
        var store = new MemoryQueueStore();
        var harness = new Harness(1, store);
        var completed = new List<ExportJobSnapshot>();
        harness.Scheduler.Completed += completed.Add;
        harness.Scheduler.Admit(Proposal("explicit-cancel", 1));
        await WaitUntilAsync(() => harness.Running == 1);
        var id = harness.Scheduler.Jobs.Single().JobId;
        Assert.True(harness.Scheduler.Cancel(id));
        await WaitUntilAsync(() => harness.Scheduler.Jobs.Single().State == JobState.Cancelled);
        Assert.Equal(JobState.Cancelled, Assert.Single(store.Jobs).State);
        Assert.Equal(JobState.Cancelled, Assert.Single(completed).State);
        await harness.DisposeAsync();
    }

    [Fact]
    public async Task ShutdownKeepsRunningRecoverableAndWritesNoTerminalCompletion()
    {
        var store = new MemoryQueueStore();
        var harness = new Harness(1, store);
        var completed = new List<ExportJobSnapshot>();
        harness.Scheduler.Completed += completed.Add;
        var proposal = Proposal("shutdown", 1);
        harness.Scheduler.Admit(proposal);
        await WaitUntilAsync(() => harness.Running == 1);

        await harness.DisposeAsync();

        Assert.Empty(completed);
        Assert.Equal(JobState.Running, Assert.Single(store.Jobs).State);
        var restarts = 0;
        await using var restored = new GlobalExportScheduler(1, () =>
        {
            Interlocked.Increment(ref restarts);
            return new((item, _, _, _) => Task.FromResult(new JobItemResult<EncodingItemResult>(item.Definition.Id,
                JobState.Completed, item.OutputPaths, [], [], null)), () => { });
        }, store);
        Assert.Equal(JobState.NeedsAttention, restored.Jobs.Single().State);
        restored.MaxSimultaneousExports = 2;
        Assert.Equal(0, restarts);
        Assert.False(restored.Admit(proposal).Accepted);
    }

    [Fact]
    public async Task WaitingAndPausedSurviveShutdownWhileRunningBecomesNeedsAttention()
    {
        var store = new MemoryQueueStore();
        var harness = new Harness(1, store);
        harness.Scheduler.Admit(Proposal("survive", 3));
        await WaitUntilAsync(() => harness.Running == 1);
        var waiting = harness.Scheduler.Jobs.Where(job => job.State == JobState.Queued).ToList();
        Assert.True(harness.Scheduler.Pause(waiting[0].JobId));
        await harness.DisposeAsync();

        await using var restored = new GlobalExportScheduler(1, () => throw new UnreachableException(), store);
        Assert.Contains(restored.Jobs, job => job.State == JobState.NeedsAttention);
        Assert.Contains(restored.Jobs, job => job.State == JobState.Paused);
        Assert.Contains(restored.Jobs, job => job.State == JobState.Queued);
    }

    [Fact]
    public async Task SkipDispositionIsRevalidatedAtomicallyAtAdmission()
    {
        var plan = PlanWithSkippedOutputs("skip", 2);
        var present = plan.Items.Select(item => item.OutputPaths.Single()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        await using var scheduler = new GlobalExportScheduler(1, () => throw new UnreachableException(),
            inspectOutput: path => new(present.Contains(path), 1));
        var accepted = scheduler.Admit(ExportSubmissionProposal.FromPlan(plan));
        Assert.True(accepted.Accepted);
        Assert.All(accepted.Jobs, job => Assert.Equal(JobState.Skipped, job.State));

        await using var changed = new GlobalExportScheduler(1, () => throw new UnreachableException(),
            inspectOutput: path => new(!path.EndsWith("skip-1.mp4", StringComparison.OrdinalIgnoreCase), 1));
        var rejected = changed.Admit(ExportSubmissionProposal.FromPlan(plan));
        Assert.False(rejected.Accepted);
        Assert.Equal("export.queue-skip-changed", Assert.Single(rejected.Issues).Code);
        Assert.Empty(changed.Jobs);
    }

    [Fact]
    public async Task ModernCoordinatorWritesHistoryForEachCompletedMediaJob()
    {
        var harness = new Harness(2);
        var history = new MemoryHistory();
        await using var coordinator = new ExportJobCoordinator(harness.Scheduler, history);
        coordinator.Queue(Plan("history", 3));
        await WaitUntilAsync(() => harness.Running == 2);
        harness.CompleteAll();
        await WaitUntilAsync(() => history.Records.Count == 3);
        Assert.All(history.Records, record => Assert.Single(record.Plan.Items));
        Assert.Equal(3, history.Records.Select(record => record.JobId).Distinct().Count());
    }

    [Fact]
    public async Task DurableRestoreKeepsOrderPausedAndReclassifiesRunningAsNeedsAttention()
    {
        var store = new MemoryQueueStore();
        var first = Proposal("restore", 2).Jobs;
        store.Save([
            new(first[0] with { QueueOrder = 4 }, JobState.Running, 42, DateTimeOffset.Now, null, TimeSpan.FromSeconds(2), [], [], null),
            new(first[1] with { QueueOrder = 7 }, JobState.Paused, null, null, null, TimeSpan.Zero, [], [], null)]);
        var harness = new Harness(2, store);
        Assert.Equal(JobState.NeedsAttention, harness.Scheduler.Jobs[0].State);
        Assert.Equal(JobState.Paused, harness.Scheduler.Jobs[1].State);
        Assert.Equal([4L, 7L], harness.Scheduler.Jobs.Select(x => x.QueueOrder));
        await harness.DisposeAsync();
    }

    [Fact]
    public async Task RestartDoesNotRestoreTerminalSchedulerSnapshots()
    {
        var store = new MemoryQueueStore();
        var jobs = Proposal("terminal-restart", 5).Jobs;
        var states = new[] { JobState.Completed, JobState.CompletedWithWarnings, JobState.Skipped,
            JobState.Failed, JobState.Cancelled };
        store.Save(jobs.Select((job, index) => new ExportJobCheckpoint(job, states[index], 100,
            DateTimeOffset.Now, DateTimeOffset.Now, TimeSpan.Zero, [], [], null)).ToList());

        await using var restored = new GlobalExportScheduler(1, () => throw new UnreachableException(), store);

        Assert.Empty(restored.Jobs);
    }

    private static ExportSubmissionProposal Proposal(string prefix, int count, string output = "C:\\output", string? sameName = null) =>
        ExportSubmissionProposal.FromPlan(Plan(prefix, count, output, sameName));

    private static JobPlan<EncodingJobOptions> Plan(string prefix, int count, string output = "C:\\output", string? sameName = null)
    {
        var options = new EncodingJobOptions("C:\\input", output, OutputResolution.Source,
            RecoveryStrategy.Normal, new EncodingOptions(), null, "", false, true, false);
        var definition = EncodingJobPlanner.Define(options, Enumerable.Range(1, count).Select(index =>
            new EncodingSource($"C:\\input\\{(sameName is null ? prefix + "-" + index : Path.GetFileNameWithoutExtension(sameName))}.mp4",
                1, TimeSpan.FromSeconds(1), CapabilityOrder: index,
                RestoredName: sameName is null ? null : new(Path.GetFileNameWithoutExtension(sameName), index, null, null))));
        return EncodingJobPlanner.Plan(definition, _ => new(false, 0));
    }

    private static JobPlan<EncodingJobOptions> PlanWithSkippedOutputs(string prefix, int count)
    {
        var options = new EncodingJobOptions("C:\\input", "C:\\output", OutputResolution.Source,
            RecoveryStrategy.Normal, new EncodingOptions(), null, "", false, false, false);
        var definition = EncodingJobPlanner.Define(options, Enumerable.Range(1, count).Select(index =>
            new EncodingSource($"C:\\input\\{prefix}-{index}.mp4", 1, TimeSpan.FromSeconds(1), CapabilityOrder: index)));
        return EncodingJobPlanner.Plan(definition, _ => new(true, 100));
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        for (var i = 0; i < 200 && !predicate(); i++) await Task.Delay(10);
        Assert.True(predicate());
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly object _sync = new();
        private readonly Dictionary<Guid, TaskCompletionSource<JobItemResult<EncodingItemResult>>> _gates = [];
        private int _running;
        public Harness(int maximum, IExportQueueStore? store = null, bool isQueuePaused = false,
            Action<bool>? persistQueuePaused = null)
        {
            Scheduler = new(maximum, () => new(async (item, _, _, token) =>
            {
                var gate = new TaskCompletionSource<JobItemResult<EncodingItemResult>>(TaskCreationOptions.RunContinuationsAsynchronously);
                lock (_sync) { _gates[item.Definition.Id] = gate; Started.Add(item.Definition.Id); _running++; MaximumObserved = Math.Max(MaximumObserved, _running); }
                using var registration = token.Register(() => gate.TrySetCanceled(token));
                try { return await gate.Task; }
                finally { lock (_sync) _running--; }
            }, () => { }), store, isQueuePaused: isQueuePaused, persistQueuePaused: persistQueuePaused);
        }
        public GlobalExportScheduler Scheduler { get; }
        public List<Guid> Started { get; } = [];
        public int MaximumObserved { get; private set; }
        public int Running { get { lock (_sync) return _running; } }
        public void CompleteOne()
        {
            TaskCompletionSource<JobItemResult<EncodingItemResult>> gate; Guid id;
            lock (_sync) { (id, gate) = _gates.First(pair => !pair.Value.Task.IsCompleted); }
            gate.TrySetResult(new(id, JobState.Completed, [], [], [], new(0, TimeSpan.FromSeconds(1), null, TimeSpan.FromSeconds(1))));
        }
        public void CompleteAll()
        {
            while (true)
            {
                TaskCompletionSource<JobItemResult<EncodingItemResult>>[] gates;
                lock (_sync) gates = _gates.Values.Where(gate => !gate.Task.IsCompleted).ToArray();
                if (gates.Length == 0)
                {
                    if (Scheduler.Jobs.All(job => job.State is not JobState.Queued and not JobState.Running)) return;
                    Thread.Sleep(5); continue;
                }
                foreach (var gate in gates)
                {
                    var id = _gates.Single(pair => ReferenceEquals(pair.Value, gate)).Key;
                    gate.TrySetResult(new(id, JobState.Completed, [], [], [], new(0, TimeSpan.FromSeconds(1), null, TimeSpan.FromSeconds(1))));
                }
            }
        }
        public ValueTask DisposeAsync() => Scheduler.DisposeAsync();
    }

    private sealed class MemoryQueueStore : IExportQueueStore
    {
        private IReadOnlyList<ExportJobCheckpoint> _jobs = [];
        public int SaveCount { get; private set; }
        public IReadOnlyList<ExportJobCheckpoint> Jobs => _jobs;
        public IReadOnlyList<ExportJobCheckpoint> Load() => _jobs;
        public void Save(IReadOnlyList<ExportJobCheckpoint> jobs) { SaveCount++; _jobs = jobs.ToList(); }
    }

    private sealed class MemoryHistory : IJobHistoryStore
    {
        public List<EncodingJobHistoryRecord> Records { get; } = [];
        public IReadOnlyList<EncodingJobHistoryRecord> Load() => Records;
        public void Add(EncodingJobHistoryRecord record) { lock (Records) Records.Add(record); }
    }
}
