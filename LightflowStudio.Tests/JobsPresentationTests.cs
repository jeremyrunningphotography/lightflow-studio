using LightflowStudio;
using System.Xml.Linq;
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

    [Fact]
    public void DrawerRows_AreDenseAndReorderButtonsRemainCompactFocusTargets()
    {
        var document = DrawerDocument();
        var drawer = Named(document, "JobsDrawer");
        var list = Named(document, "JobsDrawerList");
        var template = list.Descendants().Single(element => element.Name.LocalName == "DataTemplate");
        var row = template.Elements().Single(element => element.Name.LocalName == "StackPanel");
        var reorder = template.Descendants().Where(element => element.Name.LocalName == "Button" &&
            ((string?)element.Attribute("AutomationProperties.Name"))?.StartsWith("Move waiting Job", StringComparison.Ordinal) == true).ToList();

        Assert.Equal("380", (string?)drawer.Attribute("Width"));
        Assert.Equal("0,0,16,0", (string?)list.Attribute("Padding"));
        Assert.Equal("0,0,0,4", (string?)row.Attribute("Margin"));
        Assert.Equal(2, reorder.Count);
        Assert.All(reorder, button => { Assert.Equal("22", (string?)button.Attribute("Width")); Assert.Equal("22", (string?)button.Attribute("Height")); });
        Assert.All(reorder, button => Assert.NotNull(button.Attribute("ToolTip")));
    }

    [Fact]
    public void ExpandedCard_PreservesFullPathAndUsesOneProgressValueWithoutTimingOverlap()
    {
        var template = Named(DrawerDocument(), "JobsDrawerList").Descendants()
            .Single(element => element.Name.LocalName == "DataTemplate");
        var path = template.Descendants().Single(element => (string?)element.Attribute("Text") == "{Binding OutputPath}");
        var progress = template.Descendants().Single(element => element.Name.LocalName == "ProgressBar");
        var percentage = template.Descendants().Single(element => ((string?)element.Attribute("Text"))?.Contains("Progress, StringFormat", StringComparison.Ordinal) == true);
        var timingGrid = percentage.Parent!;

        Assert.Equal("Wrap", (string?)path.Attribute("TextWrapping"));
        Assert.Equal("{Binding OutputPath}", (string?)path.Attribute("ToolTip"));
        Assert.Equal("{Binding Progress}", (string?)progress.Attribute("Value"));
        Assert.Contains("{Binding Progress", (string?)percentage.Attribute("Text"));
        Assert.Equal("1", (string?)percentage.Attribute("Grid.Column"));
        Assert.Equal(2, timingGrid.Element(timingGrid.Name.Namespace + "Grid.ColumnDefinitions")!.Elements().Count());
    }

    [Fact]
    public void Expansion_UsesDedicatedAccessibleCommandAndNeverUnloadLifecycleEvents()
    {
        var template = Named(DrawerDocument(), "JobsDrawerList").Descendants()
            .Single(element => element.Name.LocalName == "DataTemplate");
        var toggle = template.Descendants().Single(element => (string?)element.Attribute("Click") == "JobExpansionToggle_Click");
        var detail = template.Descendants().Single(element => ((string?)element.Attribute("Visibility"))?.Contains("IsExpanded", StringComparison.Ordinal) == true);
        var source = MainWindowSource();

        Assert.Contains("Toggle details for", (string?)toggle.Attribute("AutomationProperties.Name"));
        Assert.Contains("BoolToVisibility", (string?)detail.Attribute("Visibility"));
        Assert.DoesNotContain(template.Descendants(), element => element.Name.LocalName == "Expander");
        Assert.DoesNotContain("private void JobExpanded", source);
        Assert.DoesNotContain("private void JobCollapsed", source);
        Assert.Contains("var expanded = _expandedJobIds.Add(id);", source);
        Assert.Contains("row.Children.OfType<Border>().Single().Visibility", source);
    }

    [Fact]
    public void CancellableJobs_ExcludeEveryTerminalStateAndCancelAllUsesSchedulerSnapshot()
    {
        var states = new[] { JobState.Queued, JobState.Paused, JobState.Running, JobState.NeedsAttention,
            JobState.Completed, JobState.CompletedWithWarnings, JobState.Skipped, JobState.Failed, JobState.Cancelled };
        var cancellable = JobsPresentation.CancellableJobs(states.Select((state, index) => Snapshot(index + 1, state)));
        Assert.Equal([JobState.Queued, JobState.Paused, JobState.Running, JobState.NeedsAttention], cancellable.Select(job => job.State));

        var source = MainWindowSource();
        Assert.Contains("JobsPresentation.CancellableJobs(_exportScheduler.Jobs).Select(job => job.JobId).ToList()", source);
        Assert.Contains("foreach (var id in intended) _exportScheduler.Cancel(id);", source);
        Assert.Contains("Cancel all {intended.Count} cancellable", source);
        Assert.Contains("job.OutputPath", source);
    }

    private static XDocument DrawerDocument() => XDocument.Load(Path.Combine(FindRepositoryRoot(), "LightflowStudio", "MainWindow.xaml"));
    private static string MainWindowSource() => File.ReadAllText(Path.Combine(FindRepositoryRoot(), "LightflowStudio", "MainWindow.xaml.cs"));
    private static XElement Named(XDocument document, string name) => document.Descendants().Single(element =>
        (string?)element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml")) == name);
    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "LightflowStudio"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
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
