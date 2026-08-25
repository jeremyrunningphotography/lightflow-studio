using System.Diagnostics;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class ExportJobCoordinatorTests
{
    [Fact]
    public async Task QueueReturnsImmediatelySupportsMultipleJobsAndRecordsHistoryOnce()
    {
        var runtime = new ApplicationJobsRuntime<EncodingJobOptions, EncodingItemResult>();
        var history = new MemoryHistory();
        var gates = new Dictionary<Guid, TaskCompletionSource<JobItemResult<EncodingItemResult>>>();
        var coordinator = new LegacyExportJobCoordinator(runtime, history, () => new(
            (item, _, _, _) => gates[item.Definition.Id].Task, () => { }));
        var first = Plan("first"); var second = Plan("second");
        foreach (var item in first.Items.Concat(second.Items))
            gates[item.Definition.Id] = new(TaskCreationOptions.RunContinuationsAsynchronously);

        var firstRuntime = coordinator.Queue(first);
        var secondRuntime = coordinator.Queue(second);
        Assert.Equal(2, coordinator.ActiveCount);
        Assert.False(firstRuntime.Completion.IsCompleted);
        Assert.False(secondRuntime.Completion.IsCompleted);
        Assert.Equal(2, runtime.Jobs.Count);

        Complete(first, gates); Complete(second, gates);
        await Task.WhenAll(firstRuntime.Completion, secondRuntime.Completion);
        await WaitUntilAsync(() => history.Records.Count == 2);
        Assert.Equal(2, history.Records.Select(x => x.JobId).Distinct().Count());
        Assert.Equal(0, coordinator.ActiveCount);
        await coordinator.DisposeAsync();
    }

    [Fact]
    public async Task DisposeTerminatesEveryActiveExecutor()
    {
        var runtime = new ApplicationJobsRuntime<EncodingJobOptions, EncodingItemResult>();
        var terminated = 0;
        var coordinator = new LegacyExportJobCoordinator(runtime, new MemoryHistory(), () => new(
            async (item, _, _, token) => { await Task.Delay(Timeout.InfiniteTimeSpan, token); throw new UnreachableException(); },
            () => Interlocked.Increment(ref terminated)));
        coordinator.Queue(Plan("a")); coordinator.Queue(Plan("b"));
        await coordinator.DisposeAsync();
        Assert.Equal(2, terminated);
    }

    private static JobPlan<EncodingJobOptions> Plan(string name)
    {
        var options = new EncodingJobOptions("C:\\input", "C:\\output", OutputResolution.Source,
            RecoveryStrategy.Normal, new EncodingOptions(), null, "", false, true, false);
        var definition = EncodingJobPlanner.Define(options,
            [new EncodingSource($"C:\\input\\{name}.mp4", 1, TimeSpan.FromSeconds(1))]);
        return EncodingJobPlanner.Plan(definition, _ => new(false, 0));
    }
    private static void Complete(JobPlan<EncodingJobOptions> plan,
        Dictionary<Guid, TaskCompletionSource<JobItemResult<EncodingItemResult>>> gates)
    {
        foreach (var item in plan.Items) gates[item.Definition.Id].SetResult(new(item.Definition.Id,
            JobState.Completed, item.OutputPaths, [], [], new(0, TimeSpan.FromSeconds(1), null, TimeSpan.FromSeconds(1))));
    }
    private static async Task WaitUntilAsync(Func<bool> predicate)
    { for (var i=0; i<100 && !predicate(); i++) await Task.Delay(10); Assert.True(predicate()); }
    private sealed class MemoryHistory : IJobHistoryStore
    {
        public List<EncodingJobHistoryRecord> Records { get; } = [];
        public IReadOnlyList<EncodingJobHistoryRecord> Load() => Records;
        public void Add(EncodingJobHistoryRecord record) { lock (Records) Records.Add(record); }
    }
}
