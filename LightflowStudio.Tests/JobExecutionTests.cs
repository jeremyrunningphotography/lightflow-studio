using LightflowStudio;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class JobExecutionTests
{
    [Fact]
    public void ItemStateTransitions_AreExplicitAndRetryableWhereSupported()
    {
        var item = new JobItemExecution<string>(PlanItem("one", 10));

        item.Queue();
        item.Start();
        item.ReportProgress(40);
        item.Fail("temporary failure");
        item.Retry();

        Assert.Equal(JobState.Queued, item.State);
        Assert.Null(item.ProgressPercent);
        Assert.Empty(item.Errors);
        Assert.Throws<InvalidOperationException>(() => item.Complete());
    }

    [Fact]
    public void AggregateProgress_UsesMeaningfulWorkWeightsRatherThanItemCount()
    {
        var execution = Execution(PlanItem("short", 45), PlanItem("long", 135));
        execution.Queue();
        execution.MarkStarted();
        var shortItem = execution.Items[0];
        shortItem.Start();
        shortItem.ReportProgress(100);
        shortItem.Complete();
        var longItem = execution.Items[1];
        longItem.Start();
        longItem.ReportProgress(50);

        var progress = execution.Progress(longItem);

        Assert.Equal(62.5, progress.OverallPercent);
        Assert.Equal(50, progress.CurrentItemPercent);
        Assert.Equal(112.5, progress.CompletedWork);
        Assert.Equal(180, progress.TotalWork);
    }

    [Fact]
    public void AggregateProgress_RemainsIndeterminateWhenWorkCannotBeEstimated()
    {
        var item = new JobPlanItem(
            new JobItemDefinition(Guid.NewGuid(), "unknown"),
            ["unknown.out"],
            JobPlanDisposition.Process,
            JobWorkEstimate.Indeterminate(JobWorkUnit.Bytes),
            []);
        var execution = ExecutionWithUnit(JobWorkUnit.Bytes, item);
        execution.Queue();
        execution.Items[0].Start();
        execution.Items[0].ReportProgress(25);

        var progress = execution.Progress(execution.Items[0]);

        Assert.Null(progress.OverallPercent);
        Assert.Null(progress.TotalWork);
        Assert.Equal(25, progress.CurrentItemPercent);
    }

    [Fact]
    public void AggregateStateAndResultSummary_DescribeMixedTerminalOutcomes()
    {
        var skipped = PlanItem("skip", 1, JobPlanDisposition.Skip);
        var execution = Execution(PlanItem("ok", 1), PlanItem("warning", 1), PlanItem("fail", 1), PlanItem("cancel", 1), skipped);
        execution.Queue();
        execution.MarkStarted();
        Complete(execution.Items[0]);
        execution.Items[1].Start();
        execution.Items[1].CompleteWithWarnings(["Recovered damaged frames"]);
        execution.Items[2].Start();
        execution.Items[2].Fail("Encoder failed");
        execution.Items[3].Cancel();

        var result = execution.Result();

        Assert.Equal(JobState.Failed, result.State);
        Assert.Equal(5, result.Summary.Total);
        Assert.Equal(1, result.Summary.Completed);
        Assert.Equal(1, result.Summary.CompletedWithWarnings);
        Assert.Equal(1, result.Summary.Skipped);
        Assert.Equal(1, result.Summary.Cancelled);
        Assert.Equal(1, result.Summary.Failed);
        Assert.Contains("Recovered damaged frames", result.Warnings);
        Assert.Contains("Encoder failed", result.Errors);
    }

    [Fact]
    public async Task Runner_CancellationStopsSchedulingAndPreservesCompletedResults()
    {
        var execution = Execution(PlanItem("first", 1), PlanItem("second", 1), PlanItem("third", 1));
        using var cancellation = new JobCancellation();
        var calls = 0;

        var result = await SequentialJobRunner.RunAsync(execution, (item, _, token) =>
        {
            calls++;
            if (calls == 1) return Task.FromResult(new JobItemResult<string>(item.Definition.Id, JobState.Completed, item.OutputPaths, [], [], "done"));
            cancellation.Cancel();
            throw new OperationCanceledException(token);
        }, cancellation);

        Assert.Equal(2, calls);
        Assert.Equal(JobState.Completed, result.Items[0].State);
        Assert.Equal(JobState.Cancelled, result.Items[1].State);
        Assert.Equal(JobState.Cancelled, result.Items[2].State);
        Assert.Equal(JobState.Cancelled, result.State);
    }

    private static void Complete(JobItemExecution<string> item)
    {
        item.Start();
        item.Complete("done");
    }

    private static JobExecution<string, string> Execution(params JobPlanItem[] items)
        => ExecutionWithUnit(JobWorkUnit.MediaDuration, items);

    private static JobExecution<string, string> ExecutionWithUnit(JobWorkUnit workUnit, params JobPlanItem[] items)
    {
        var definition = new JobDefinition<string>(Guid.NewGuid(), "test", DateTimeOffset.Now, "options", items.Select(item => item.Definition).ToList());
        return new(new JobPlan<string>(definition, DateTimeOffset.Now, items, [], workUnit));
    }

    private static JobPlanItem PlanItem(string source, double work, JobPlanDisposition disposition = JobPlanDisposition.Process) =>
        new(new JobItemDefinition(Guid.NewGuid(), source), [$"{source}.out"], disposition,
            JobWorkEstimate.Determinate(JobWorkUnit.MediaDuration, work), []);
}
