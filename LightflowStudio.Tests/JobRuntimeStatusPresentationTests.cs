using Xunit;

namespace LightflowStudio.Tests;

public sealed class JobRuntimeStatusPresentationTests
{
    [Fact]
    public void RunningShowsAggregateConcurrentActivityInsteadOfOneCurrentFile()
    {
        var text = Describe(JobState.Running, running: 2, waiting: 5, completed: 3);

        Assert.Equal("2 exporting • 5 waiting • 3 complete • active", text);
    }

    [Fact]
    public void PausingExplainsDrainContractAndPreservesAllCounts()
    {
        var text = Describe(JobState.Pausing, running: 2, waiting: 5, completed: 3);

        Assert.Equal("2 exporting (draining; no new exports will start) • 5 waiting • 3 complete • pausing", text);
    }

    [Fact]
    public void PausedShowsZeroExportingAndRemainingWaitingWork()
    {
        var text = Describe(JobState.Paused, running: 0, waiting: 5, completed: 3);

        Assert.Equal("0 exporting • 5 waiting • 3 complete • paused", text);
    }

    [Fact]
    public void CancellingShowsActiveTerminationAndAlreadyCancelledWaitingItems()
    {
        var text = Describe(JobState.Cancelling, running: 2, waiting: 0, completed: 3, cancelled: 5);

        Assert.Equal("2 exporting (cancelling) • 0 waiting • 3 complete • 5 cancelled • cancelling", text);
    }

    [Theory]
    [InlineData((int)JobState.Completed, "0 exporting • 0 waiting • 8 complete • completed")]
    [InlineData((int)JobState.CompletedWithWarnings, "0 exporting • 0 waiting • 8 complete • completed with warnings")]
    [InlineData((int)JobState.Failed, "0 exporting • 0 waiting • 6 complete • 2 failed • failed")]
    [InlineData((int)JobState.Cancelled, "0 exporting • 0 waiting • 3 complete • 5 cancelled • cancelled")]
    public void TerminalStatesRemainTruthful(int stateValue, string expected)
    {
        var state = (JobState)stateValue;
        var failed = state == JobState.Failed ? 2 : 0;
        var cancelled = state == JobState.Cancelled ? 5 : 0;
        var completed = failed > 0 ? 6 : cancelled > 0 ? 3 : 8;

        Assert.Equal(expected, Describe(state, 0, 0, completed, failed, cancelled));
    }

    private static string Describe(JobState state, int running, int waiting, int completed,
        int failed = 0, int cancelled = 0) => JobRuntimeStatusPresentation.Describe(state,
        new JobRuntimeCounts(running + waiting + completed + failed + cancelled, waiting, running,
            completed, failed, cancelled, 0));
}
