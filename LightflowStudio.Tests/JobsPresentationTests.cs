using LightflowStudio;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class JobsPresentationTests
{
    [Fact]
    public void StatusAndRoute_CountIndependentFileJobs()
    {
        var jobs = new[] { Snapshot(1, JobState.Running), Snapshot(2, JobState.Running) }
            .Concat(Enumerable.Range(3, 8).Select(order => Snapshot(order, JobState.Queued))).ToList();
        Assert.Equal("Jobs · 2 exporting · 8 waiting", JobsPresentation.StatusText(jobs));
        Assert.Equal(JobsRoute.Drawer, JobsPresentation.Route(jobs));
        Assert.Equal(JobsRoute.HistoryCompatibility, JobsPresentation.Route([Snapshot(1, JobState.Completed)]));
    }

    [Fact]
    public void VisibleJobs_PreserveSchedulerOrderAndBoundOnlyTerminalFeedback()
    {
        var jobs = Enumerable.Range(1, 12).Select(order => Snapshot(order, JobState.Completed)).ToList();
        jobs.Add(Snapshot(20, JobState.Queued));
        var visible = JobsPresentation.VisibleJobs(jobs);
        Assert.Equal(9, visible.Count);
        Assert.Equal(visible.Select(job => job.QueueOrder).Order(), visible.Select(job => job.QueueOrder));
        Assert.Contains(visible, job => job.State == JobState.Queued);
    }

    [Fact]
    public void Card_ExposesFilenameEtaSettingsActionsAndExpansionState()
    {
        var card = JobsPresentation.Card(Snapshot(1, JobState.Queued), true);
        Assert.Equal("output-1.mp4", card.Name);
        Assert.Equal("Waiting", card.State);
        Assert.True(card.IsExpanded);
        Assert.True(card.CanPause);
        Assert.True(card.CanReorder);
        Assert.Contains("1080p", card.ResolutionAndFrameRate);
        Assert.Contains("H264", card.CodecAndContainer);
    }

    [Fact]
    public void StatePresentation_NeverReliesOnColorAlone()
    {
        var cases = new[] { (JobState.Running, "◔", "Exporting"), (JobState.Queued, "○", "Waiting"),
            (JobState.Completed, "✓", "Completed"), (JobState.Failed, "!", "Failed"),
            (JobState.Cancelled, "×", "Cancelled") };
        foreach (var (state, glyph, stateText) in cases)
        {
            Assert.Equal(glyph, JobsPresentation.Glyph(state));
            Assert.Equal(stateText, JobsPresentation.StateText(state));
        }
    }

    private static ExportJobSnapshot Snapshot(int order, JobState state)
    {
        var item = new JobItemDefinition(Guid.NewGuid(), $@"C:\input-{order}.mp4", 100, new MediaRange(TimeSpan.FromMinutes(1)));
        var options = new EncodingJobOptions(@"C:\", @"C:\out", OutputResolution.FullHd, RecoveryStrategy.Normal,
            new EncodingOptions(), null, "", false, true, false);
        var plan = new JobPlanItem(item, [$@"C:\out\output-{order}.mp4"], JobPlanDisposition.Process,
            JobWorkEstimate.Determinate(JobWorkUnit.MediaDuration, 60), []);
        var definition = new ExportJobDefinition(item.Id, Guid.NewGuid(), order, DateTimeOffset.Now, options, plan);
        return new(definition, state, state == JobState.Running ? 42 : null, DateTimeOffset.Now,
            JobsPresentation.IsTerminal(state) ? DateTimeOffset.Now.AddMinutes(order) : null,
            TimeSpan.FromSeconds(12), state == JobState.Running ? TimeSpan.FromSeconds(20) : null, [], [], null);
    }
}
