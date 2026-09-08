using LightflowStudio;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Threading;
using System.Xml.Linq;
using Xunit;

namespace LightflowStudio.Tests;

[Collection("STA dispatcher tests")]
public sealed class JobsPresentationTests
{
    [Fact]
    public void StatusCountsIndependentFileJobsAndStatusActionTargetsFullJobsDirectly()
    {
        var jobs = new[] { Snapshot(1, JobState.Running), Snapshot(2, JobState.Running) }
            .Concat(Enumerable.Range(3, 8).Select(order => Snapshot(order, JobState.Queued))).ToList();
        Assert.Equal("Jobs · 2 exporting · 8 waiting", JobsPresentation.StatusText(jobs));
        Assert.Equal("Jobs · Queue paused · 2 exporting · 8 waiting", JobsPresentation.StatusText(jobs, true));
        Assert.Equal("Jobs · Queue paused", JobsPresentation.StatusText([], true));
        var statusHandler = MethodBody(MainWindowSource(), "internal void JobsStatus_Click");
        Assert.Contains("ShellDestination.Jobs", statusHandler);
        Assert.DoesNotContain("Compatibility", statusHandler);
        Assert.DoesNotContain("OpenJobsDrawer", statusHandler);
        Assert.DoesNotContain("CloseJobsDrawer", statusHandler);
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
        var details = Assert.IsType<ExportJobDetailsPresentation>(card.Details);
        Assert.Contains("1080p", details.Video);
        Assert.Contains("H264", details.Format);
    }

    [Theory]
    [InlineData((int)FileOperationKind.Copy)]
    [InlineData((int)FileOperationKind.Move)]
    public void PromotedFolderJobs_UseFilesystemDetails(int kindValue)
    {
        var kind = (FileOperationKind)kindValue;
        var source = new FileOperationSource(null, @"C:\media\folder", null, true);
        var intent = new FileOperationIntent(Guid.NewGuid(), kind, [source], @"C:\destination",
            DateTimeOffset.UtcNow.AddSeconds(-2), null, false, FileOperationExecution.Job);
        var snapshot = new FileOperationJobSnapshot(intent, FileOperationState.Running, 0, 2048,
            source.Path, []);

        var card = JobsPresentation.Card(snapshot, true);
        var details = Assert.IsType<FileSystemJobDetailsPresentation>(card.Details);

        Assert.Equal(kind.ToString(), details.Operation);
        Assert.Equal(source.Path, details.SourceSummary);
        Assert.Equal(@"C:\destination", details.Destination);
        Assert.Equal("0 of 1 item", details.ItemProgress);
        Assert.Equal(source.Path, details.CurrentItem);
        Assert.Contains("2,048 bytes", details.ByteProgress);
    }

    [Fact]
    public void PromotedMultiFileJob_UsesOneFilesystemModelInDrawerAndFullJobs()
    {
        var sources = new[]
        {
            new FileOperationSource(Guid.NewGuid(), @"C:\media\one.mov", 100),
            new FileOperationSource(Guid.NewGuid(), @"C:\media\two.mov", 200),
            new FileOperationSource(Guid.NewGuid(), @"C:\media\three.mov", 300)
        };
        var intent = new FileOperationIntent(Guid.NewGuid(), FileOperationKind.Copy, sources, @"C:\destination",
            DateTimeOffset.UtcNow.AddSeconds(-3), 600, false, FileOperationExecution.Job);
        var snapshot = new FileOperationJobSnapshot(intent, FileOperationState.Running, 1, 100,
            sources[1].Path, []);

        var drawer = Assert.IsType<FileSystemJobDetailsPresentation>(JobsPresentation.Card(snapshot, true).Details);
        var workspace = Assert.IsType<FileSystemJobDetailsPresentation>(Assert.Single(
            JobsWorkspacePresentation.ProjectFileOperations([snapshot], [])).DetailPresentation);

        Assert.Equal(drawer, workspace);
        Assert.Equal("1 of 3 items", drawer.ItemProgress);
        Assert.Contains("3 selected items", drawer.SourceSummary);
        Assert.Equal("100 of 600 bytes", drawer.ByteProgress);
    }

    [Fact]
    public void CapabilityTemplates_KeepExportRowsOutOfFilesystemDetailsAndServeBothJobsSurfaces()
    {
        var document = DrawerDocument();
        var templates = document.Descendants().Where(element => element.Name.LocalName == "DataTemplate").ToList();
        var export = templates.Single(element => ((string?)element.Attribute("DataType"))?.Contains(
            "ExportJobDetailsPresentation", StringComparison.Ordinal) == true);
        var filesystem = templates.Single(element => ((string?)element.Attribute("DataType"))?.Contains(
            "FileSystemJobDetailsPresentation", StringComparison.Ordinal) == true);
        var exportLabels = export.Descendants().Select(element => (string?)element.Attribute("Text")).ToHashSet();
        var filesystemLabels = filesystem.Descendants().Select(element => (string?)element.Attribute("Text")).ToHashSet();

        Assert.All(new[] { "VIDEO", "FORMAT", "QUALITY", "AUDIO", "COLOR" }, label =>
        {
            Assert.Contains(label, exportLabels);
            Assert.DoesNotContain(label, filesystemLabels);
        });
        Assert.All(new[] { "OPERATION", "SOURCE", "DESTINATION", "ITEMS", "CURRENT", "BYTES" },
            label => Assert.Contains(label, filesystemLabels));
        var drawerContent = Named(document, "CompactJobsList").Descendants().Single(element =>
            element.Name.LocalName == "ContentControl" && (string?)element.Attribute("Content") == "{Binding Details}");
        var fullContent = Named(document, "HistoryDetails");
        Assert.Equal("ContentControl", drawerContent.Name.LocalName);
        Assert.Equal("ContentControl", fullContent.Name.LocalName);
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
        var drawer = Named(document, "CompactJobsList");
        var list = Named(document, "CompactJobsList");
        var template = list.Descendants().Single(element => element.Name.LocalName == "DataTemplate");
        var card = template.Elements().Single(element => element.Name.LocalName == "Border");
        var reorder = template.Descendants().Where(element => element.Name.LocalName == "Button" &&
            ((string?)element.Attribute("AutomationProperties.Name"))?.StartsWith("Move waiting Job", StringComparison.Ordinal) == true).ToList();

        Assert.Null(drawer.Attribute("Width"));
        Assert.Null(drawer.Attribute("MinWidth"));
        Assert.Null(drawer.Attribute("MaxWidth"));
        Assert.Equal("0,0,16,0", (string?)list.Attribute("Padding"));
        Assert.Equal("{StaticResource DrawerCard}", (string?)card.Attribute("Style"));
        Assert.Equal("0,0,0,7", (string?)card.Attribute("Margin"));
        Assert.Equal(2, reorder.Count);
        Assert.All(reorder, button => { Assert.Equal("22", (string?)button.Attribute("Width")); Assert.Equal("22", (string?)button.Attribute("Height")); });
        Assert.All(reorder, button => Assert.NotNull(button.Attribute("ToolTip")));
    }

    [Fact]
    public void ExpandedCard_PreservesFullPathAndUsesOneProgressValueWithoutTimingOverlap()
    {
        var template = Named(DrawerDocument(), "CompactJobsList").Descendants()
            .Single(element => element.Name.LocalName == "DataTemplate");
        var path = DrawerDocument().Descendants().Single(element => (string?)element.Attribute("Text") == "{Binding OutputPath}");
        var progress = template.Descendants().Single(element => element.Name.LocalName == "ProgressBar");
        var percentage = template.Descendants().Single(element => ((string?)element.Attribute("Text"))?.Contains("Progress, StringFormat", StringComparison.Ordinal) == true);
        var timingGrid = percentage.Parent!;

        Assert.Equal("Wrap", (string?)path.Attribute("TextWrapping"));
        Assert.Equal("{Binding OutputPath}", (string?)path.Attribute("ToolTip"));
        Assert.Equal("{Binding Progress, Mode=OneWay}", (string?)progress.Attribute("Value"));
        Assert.Contains("{Binding Progress", (string?)percentage.Attribute("Text"));
        Assert.Equal("1", (string?)percentage.Attribute("Grid.Column"));
        Assert.Equal(2, timingGrid.Element(timingGrid.Name.Namespace + "Grid.ColumnDefinitions")!.Elements().Count());
    }

    [Fact]
    public void Expansion_UsesDedicatedAccessibleCommandAndNeverUnloadLifecycleEvents()
    {
        var template = Named(DrawerDocument(), "CompactJobsList").Descendants()
            .Single(element => element.Name.LocalName == "DataTemplate");
        var toggle = template.Descendants().Single(element => (string?)element.Attribute("Click") == "JobExpansionToggle_Click");
        var detail = template.Descendants().Single(element => element.Name.LocalName == "Border" &&
            ((string?)element.Attribute("Visibility"))?.Contains("IsExpanded", StringComparison.Ordinal) == true);
        var source = MainWindowSource();

        Assert.Contains("Toggle details for", (string?)toggle.Attribute("AutomationProperties.Name"));
        Assert.Contains("BoolToVisibility", (string?)detail.Attribute("Visibility"));
        Assert.DoesNotContain(template.Descendants(), element => element.Name.LocalName == "Expander");
        Assert.DoesNotContain("private void JobExpanded", source);
        Assert.DoesNotContain("private void JobCollapsed", source);
        Assert.Contains("var expanded = _expandedJobIds.Add(id);", source);
        Assert.Contains("SetExpanded(expanded)", MethodBody(source, "JobExpansionToggle_Click"));
        Assert.DoesNotContain("ApplyJobsPresentation", MethodBody(source, "JobExpansionToggle_Click"));
        Assert.DoesNotContain("Children.OfType", MethodBody(source, "JobExpansionToggle_Click"));
    }

    [Fact]
    public void BulkAction_UsesOnlyAuthoritativeActiveStatesAndUpdatesFromSchedulerSnapshot()
    {
        var states = new[] { JobState.Queued, JobState.Paused, JobState.Running, JobState.NeedsAttention,
            JobState.Completed, JobState.CompletedWithWarnings, JobState.Skipped, JobState.Failed, JobState.Cancelled };
        var cancellable = JobsPresentation.CancellableJobs(states.Select((state, index) => Snapshot(index + 1, state)));
        Assert.Equal([JobState.Queued, JobState.Paused, JobState.Running, JobState.NeedsAttention], cancellable.Select(job => job.State));
        var bulkCancellable = JobsPresentation.BulkCancellableJobs(states.Select((state, index) => Snapshot(index + 1, state)));
        Assert.Equal([JobState.Queued, JobState.Paused, JobState.Running], bulkCancellable.Select(job => job.State));

        var source = MainWindowSource();
        Assert.Contains("JobsPresentation.BulkCancellableJobs(jobs).Select(job => job.JobId).ToList()", source);
        Assert.Contains("else _exportScheduler.Cancel(id);", source);
        Assert.Contains("Cancel all {intended.Count} active", source);
        Assert.Contains("job.OutputPath", source);

        var apply = MethodBody(source, "private void ApplyJobsPresentation");
        Assert.Contains("JobsPresentation.BulkCancellableJobs(jobs)", apply);
        Assert.Contains("JobsCancelAllButton.Content = cancelAll ? \"Cancel all\" : \"Clear all\"", apply);
        Assert.Contains("JobsCancelAllButton.IsEnabled = bulkAction != JobsBulkAction.None", apply);
        Assert.DoesNotContain("JobsCancelAllButton.Visibility", apply);
        var button = Named(DrawerDocument(), "JobsCancelAllButton");
        Assert.Equal("Clear all", (string?)button.Attribute("Content"));
        Assert.Equal("False", (string?)button.Attribute("IsEnabled"));
        Assert.Null(button.Attribute("Visibility"));
    }

    [Theory]
    [InlineData((int)JobState.Running, true)]
    [InlineData((int)JobState.Queued, true)]
    [InlineData((int)JobState.Paused, true)]
    [InlineData((int)JobState.NeedsAttention, false)]
    [InlineData((int)JobState.Completed, false)]
    public void BulkAction_ActiveDecisionMatchesProductStates(int state, bool expected) =>
        Assert.Equal(expected, JobsPresentation.IsBulkActive((JobState)state));

    [Theory]
    [InlineData((int)JobState.NeedsAttention, true)]
    [InlineData((int)JobState.Completed, true)]
    [InlineData((int)JobState.CompletedWithWarnings, true)]
    [InlineData((int)JobState.Skipped, true)]
    [InlineData((int)JobState.Cancelled, true)]
    [InlineData((int)JobState.Failed, true)]
    [InlineData((int)JobState.Running, false)]
    [InlineData((int)JobState.Queued, false)]
    [InlineData((int)JobState.Paused, false)]
    public void BulkAction_ClearDecisionIncludesOnlyDismissibleRows(int state, bool expected) =>
        Assert.Equal(expected, JobsPresentation.IsDismissibleDrawerRow((JobState)state));

    [Fact]
    public void BulkAction_ContextRulesCoverActiveRecoveryTerminalMixedAndEmptySnapshots()
    {
        Assert.Equal(JobsBulkAction.CancelAll, JobsPresentation.BulkAction([Snapshot(1, JobState.Running)]));
        Assert.Equal(JobsBulkAction.CancelAll, JobsPresentation.BulkAction([Snapshot(1, JobState.Queued)]));
        Assert.Equal(JobsBulkAction.CancelAll, JobsPresentation.BulkAction([Snapshot(1, JobState.Paused)]));
        Assert.Equal(JobsBulkAction.ClearAll, JobsPresentation.BulkAction([Snapshot(1, JobState.NeedsAttention)]));
        Assert.Equal(JobsBulkAction.ClearAll, JobsPresentation.BulkAction([Snapshot(1, JobState.Completed)]));
        Assert.Equal(JobsBulkAction.CancelAll, JobsPresentation.BulkAction([
            Snapshot(1, JobState.Running), Snapshot(2, JobState.NeedsAttention), Snapshot(3, JobState.Failed)]));
        Assert.Equal(JobsBulkAction.None, JobsPresentation.BulkAction([]));
    }

    [Fact]
    public void JobsUsesSharedPanelResizeAndRetiresDedicatedState()
    {
        var document = DrawerDocument();
        var splitter = Named(document, "RightPanelSplitter");
        Assert.Equal("8", (string?)splitter.Attribute("Width"));
        Assert.Equal("PreviousAndNext", (string?)splitter.Attribute("ResizeBehavior"));
        Assert.DoesNotContain("JobsDrawer", MainWindowSource());
        Assert.Contains("SetRightPanel(_rightPanelPreferredWidth", MainWindowSource());
    }

    [Fact]
    public void CompactHeader_KeepsConcurrencyAndQueueControlsUsableAtSharedPanelWidths()
    {
        var document = DrawerDocument();
        var combo = Named(document, "MaximumExportsCombo");
        var button = Named(document, "JobsCancelAllButton");
        var header = combo.Parent!;

        Assert.Contains(header.Elements(), element => (string?)element.Attribute("Text") == "Active exports");
        Assert.DoesNotContain(header.Descendants(), element => (string?)element.Attribute("Text") == "Maximum simultaneous exports");
        Assert.Equal("1", (string?)combo.Attribute("Grid.Column"));
        Assert.Equal("WrapPanel", button.Parent!.Name.LocalName);
        Assert.Equal("65", (string?)button.Attribute("MinWidth"));
        Assert.Contains("simultaneously", (string?)combo.Attribute("ToolTip"));
        Assert.Equal(280, WorkspaceState.MinRightPanelWidth);
    }

    [Fact]
    public void DisclosureAndTerminalRows_UseLightflowStateAndHideWaitingControls()
    {
        var template = Named(DrawerDocument(), "CompactJobsList").Descendants()
            .Single(element => element.Name.LocalName == "DataTemplate");
        var carets = template.Descendants().Where(element => (string?)element.Attribute("Text") is "›" or "⌄").ToList();
        var reorder = template.Descendants().Single(element => ((string?)element.Attribute("Visibility"))?.Contains("CanReorder", StringComparison.Ordinal) == true);

        Assert.Equal(2, carets.Count);
        Assert.All(carets, caret => Assert.Contains("IsExpanded", (string?)caret.Attribute("Visibility")));
        Assert.All(carets, caret => Assert.Equal("Center", (string?)caret.Attribute("VerticalAlignment")));
        Assert.Contains("BoolToVisibility", (string?)reorder.Attribute("Visibility"));
        Assert.DoesNotContain(template.Descendants(), element => element.Name.LocalName == "Expander");
    }

    [Fact]
    public void DisclosureGutter_IsFullHeightAndStopsBeforeIndependentRowTargets()
    {
        var template = Named(DrawerDocument(), "CompactJobsList").Descendants()
            .Single(element => element.Name.LocalName == "DataTemplate");
        var toggle = template.Descendants().Single(element =>
            (string?)element.Attribute("Click") == "JobExpansionToggle_Click");
        var identityGrid = toggle.Parent!;
        var columns = identityGrid.Element(identityGrid.Name.Namespace + "Grid.ColumnDefinitions")!.Elements().ToList();

        Assert.Equal("0", (string?)toggle.Attribute("Grid.Column"));
        Assert.Equal("Stretch", (string?)toggle.Attribute("HorizontalAlignment"));
        Assert.Equal("Stretch", (string?)toggle.Attribute("VerticalAlignment"));
        Assert.Equal("22", (string?)columns[0].Attribute("Width"));
        Assert.Contains("IsExpanded", (string?)toggle.Attribute("AutomationProperties.ItemStatus"));
        Assert.DoesNotContain(toggle.Descendants(), element => element.Name.LocalName == "JobsRadialProgress");
        Assert.DoesNotContain(toggle.Descendants(), element =>
            (string?)element.Attribute("Text") == "{Binding Name}");
        Assert.DoesNotContain(toggle.Descendants(), element =>
            ((string?)element.Attribute("Click"))?.StartsWith("JobsMove", StringComparison.Ordinal) == true);

        var handler = MethodBody(MainWindowSource(), "JobExpansionToggle_Click");
        Assert.Equal(1, handler.Split("_expandedJobIds.Add(id)", StringSplitOptions.None).Length - 1);
        Assert.Equal(1, handler.Split("_expandedJobIds.Remove(id)", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void LiveRefreshUpdatesStableCardWithoutReplacingDisclosureTargetOrExpansionState()
    {
        var snapshot = Snapshot(1, JobState.Running);
        var initial = JobsPresentation.Card(snapshot, false);
        var identity = initial;
        for (var activation = 0; activation < 20; activation++)
        {
            var requested = !initial.IsExpanded;
            initial.SetExpanded(requested);
            var refresh = JobsPresentation.Card(snapshot with { ProgressPercent = activation + 1 }, requested);
            initial.Apply(refresh);
            Assert.Same(identity, initial);
            Assert.Equal(requested, initial.IsExpanded);
            Assert.Equal(activation + 1, initial.Progress);
        }

        var source = MainWindowSource();
        var apply = MethodBody(source, "private void ApplyJobsPresentation");
        Assert.Contains("JobsPresentation.Reconcile(_compactJobsCards, cards)", apply);
        Assert.DoesNotContain("_compactJobsCards.Clear", apply);
    }

    [Fact]
    public void MultiJobAdmissionAndRefreshPreserveCardIdentityAndAuthoritativeOrder()
    {
        var snapshots = Enumerable.Range(1, 3).Select(order => Snapshot(order, JobState.Queued)).ToList();
        var cards = new ObservableCollection<JobCardPresentation>();
        JobsPresentation.Reconcile(cards, snapshots.Select(job => JobsPresentation.Card(job, false)).ToList());
        var identities = cards.ToDictionary(card => card.JobId);

        var running = snapshots.Select((job, index) => job with
        {
            State = index == 0 ? JobState.Running : JobState.Queued,
            ProgressPercent = index == 0 ? 35 : null
        }).ToList();
        JobsPresentation.Reconcile(cards, running.Select(job => JobsPresentation.Card(job,
            identities[job.JobId].IsExpanded)).ToList());

        Assert.Equal(3, cards.Count);
        Assert.All(cards, card => Assert.Same(identities[card.JobId], card));
        Assert.Equal(35, cards[0].Progress);

        var reordered = new[] { running[2], running[1], running[0] };
        JobsPresentation.Reconcile(cards, reordered.Select(job => JobsPresentation.Card(job,
            identities[job.JobId].IsExpanded)).ToList());
        Assert.Equal(reordered.Select(job => job.JobId), cards.Select(card => card.JobId));
        Assert.All(cards, card => Assert.Same(identities[card.JobId], card));
    }

    [Fact]
    public void DrawerBindingsAreExplicitOneWayForStableReadOnlyPresentationProperties()
    {
        var template = Named(DrawerDocument(), "CompactJobsList").Descendants()
            .Single(element => element.Name.LocalName == "DataTemplate");
        var radial = template.Descendants()
            .Single(element => element.Name.LocalName == "JobsRadialProgress");
        var stateRun = template.Descendants().Single(element => element.Name.LocalName == "Run" &&
            ((string?)element.Attribute("Text"))?.Contains("Binding State", StringComparison.Ordinal) == true);
        var etaRun = template.Descendants().Single(element => element.Name.LocalName == "Run" &&
            ((string?)element.Attribute("Text"))?.Contains("Binding Eta", StringComparison.Ordinal) == true);
        var progressBar = template.Descendants().Single(element => element.Name.LocalName == "ProgressBar");
        Assert.Equal("{Binding Progress, Mode=OneWay}", (string?)radial.Attribute("Progress"));
        Assert.Equal("{Binding State, Mode=OneWay}", (string?)radial.Attribute("State"));
        Assert.Equal("{Binding State, Mode=OneWay}", (string?)radial.Attribute("AutomationProperties.Name"));
        Assert.Equal("{Binding State, Mode=OneWay}", (string?)stateRun.Attribute("Text"));
        Assert.Equal("{Binding Eta, Mode=OneWay}", (string?)etaRun.Attribute("Text"));
        Assert.All(template.Descendants().Where(element => element.Name.LocalName == "Run" &&
            ((string?)element.Attribute("Text"))?.StartsWith("{Binding", StringComparison.Ordinal) == true),
            run => Assert.Contains("Mode=OneWay", (string?)run.Attribute("Text")));
        Assert.Equal("{Binding Progress, Mode=OneWay}", (string?)progressBar.Attribute("Value"));
        Assert.True(typeof(JobCardPresentation).GetProperty(nameof(JobCardPresentation.State))!
            .GetSetMethod(nonPublic: true)!.IsPrivate);
        Assert.True(((FrameworkPropertyMetadata)Run.TextProperty.GetMetadata(typeof(Run))).BindsTwoWayByDefault);
        Assert.True(((FrameworkPropertyMetadata)ProgressBar.ValueProperty.GetMetadata(typeof(ProgressBar))).BindsTwoWayByDefault);
    }

    [Fact]
    public async Task RadialOneWayBindingsActivateAndRefreshAgainstStablePrivateSetCard()
    {
        await StaDispatcher.RunAsync(async () =>
        {
            var snapshot = Snapshot(1, JobState.Queued);
            var card = JobsPresentation.Card(snapshot, false);
            var radial = new JobsRadialProgress { DataContext = card };
            var statusRun = new Run { DataContext = card };
            var etaRun = new Run { DataContext = card };
            var progressBar = new ProgressBar { DataContext = card };
            BindingOperations.SetBinding(radial, JobsRadialProgress.ProgressProperty,
                new Binding(nameof(JobCardPresentation.Progress)) { Mode = BindingMode.OneWay });
            BindingOperations.SetBinding(radial, JobsRadialProgress.StateProperty,
                new Binding(nameof(JobCardPresentation.State)) { Mode = BindingMode.OneWay });
            BindingOperations.SetBinding(statusRun, Run.TextProperty,
                new Binding(nameof(JobCardPresentation.State)) { Mode = BindingMode.OneWay });
            BindingOperations.SetBinding(etaRun, Run.TextProperty,
                new Binding(nameof(JobCardPresentation.Eta)) { Mode = BindingMode.OneWay });
            BindingOperations.SetBinding(progressBar, ProgressBar.ValueProperty,
                new Binding(nameof(JobCardPresentation.Progress)) { Mode = BindingMode.OneWay });
            radial.Measure(new System.Windows.Size(21, 21));
            await Dispatcher.Yield(DispatcherPriority.DataBind);
            Assert.Equal("Waiting", radial.State);
            Assert.Equal("Waiting", statusRun.Text);
            Assert.Equal("", etaRun.Text);

            card.Apply(JobsPresentation.Card(snapshot with
            {
                State = JobState.Running, ProgressPercent = 42, Eta = TimeSpan.FromSeconds(20)
            }, false));
            await Dispatcher.Yield(DispatcherPriority.DataBind);
            Assert.Equal("Exporting", radial.State);
            Assert.Equal(42, radial.Progress);
            Assert.Equal("Exporting", statusRun.Text);
            Assert.Equal("About 0:20 remaining", etaRun.Text);
            Assert.Equal(42, progressBar.Value);
        });
    }

    [Fact]
    public void UnexpectedInterfaceErrorGateCoalescesReentrancyAndCanReset()
    {
        var gate = new UnexpectedInterfaceErrorGate();
        var admitted = Enumerable.Range(0, 64).AsParallel().Count(_ => gate.TryEnter());
        Assert.Equal(1, admitted);
        Assert.False(gate.TryEnter());
        gate.Exit();
        Assert.True(gate.TryEnter());

        var source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "LightflowStudio", "App.xaml.cs"));
        var handler = MethodBody(source, "private void OnDispatcherUnhandledException");
        Assert.True(handler.IndexOf("ActivityLog.TryAppend", StringComparison.Ordinal) <
            handler.IndexOf("TryEnter", StringComparison.Ordinal));
        Assert.Equal(1, handler.Split("MessageBox.Show", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void SubmissionRevealsJobsThroughSharedPanelWithoutProgressReopeningIt()
    {
        var source = MainWindowSource();
        var reveal = MethodBody(source, "internal void OpenJobsPanel");
        Assert.Contains("HomeRightPanel.SelectSurface(\"jobs\")", reveal);
        Assert.Contains("SetRightPanelOpen(true)", reveal);
        Assert.DoesNotContain("MainTabs", reveal);
        var acceptedStart = source.IndexOf("_exportScheduler.SubmissionAccepted", StringComparison.Ordinal);
        var acceptedEnd = source.IndexOf("_workspaceState =", acceptedStart, StringComparison.Ordinal);
        Assert.Contains("OpenJobsPanel();", source[acceptedStart..acceptedEnd]);
        Assert.DoesNotContain("OpenJobsPanel", MethodBody(source, "private void ExportScheduler_Changed"));
    }

    [Fact]
    public void ObsoletePullAndBodyStylesAreRemoved()
    {
        var app = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "LightflowStudio", "App.xaml"));
        Assert.DoesNotContain("DrawerPullButton", app);
        Assert.DoesNotContain("DrawerBody", app);
        Assert.DoesNotContain(DrawerDocument().Descendants(), element => ((string?)element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml")))?.StartsWith("JobsDrawer") == true);
    }

    [Fact]
    public void FullJobsSelectionChromeOverridesSystemBlueAndSeparatesSelectionHoverAndFocus()
    {
        var document = DrawerDocument();
        var list = Named(document, "HistoryList");
        Assert.Equal("{StaticResource JobsListItemStyle}", (string?)list.Attribute("ItemContainerStyle"));
        var style = document.Descendants().Single(element =>
            (string?)element.Attribute(XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml")) == "JobsListItemStyle");
        var text = style.ToString();
        Assert.Contains("ShellSelectionBrush", text);
        Assert.Contains("ShellRaisedBrush", text);
        Assert.Contains("ShellFocusBrush", text);
        Assert.Contains("SelectionRail", text);
        Assert.DoesNotContain("HighlightBrush", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SystemColors", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(style.Descendants(), element => (string?)element.Attribute("Property") == "IsSelected");
        Assert.Contains(style.Descendants(), element => (string?)element.Attribute("Property") == "IsKeyboardFocused");
        Assert.Contains(style.Descendants(), element => (string?)element.Attribute("Property") == "IsMouseOver");
        var radial = list.Descendants().Single(element => element.Name.LocalName == "JobsRadialProgress");
        Assert.Equal("{Binding StateText, Mode=OneWay}", (string?)radial.Attribute("State"));
        Assert.DoesNotContain("IsSelected", radial.ToString());
    }

    [Fact]
    public void QueueGateIsGlobalAccessibleAndAvailableInBothJobsSurfaces()
    {
        var document = DrawerDocument();
        foreach (var name in new[] { "FullJobsQueueGateButton", "JobsQueueGateButton" })
        {
            var button = Named(document, name);
            Assert.Equal("Pause Queue", (string?)button.Attribute("Content"));
            Assert.Equal("JobsQueueGate_Click", (string?)button.Attribute("Click"));
            Assert.Contains("running exports continue", (string?)button.Attribute("ToolTip"), StringComparison.OrdinalIgnoreCase);
            Assert.Equal("Pause Queue", (string?)button.Attribute("AutomationProperties.Name"));
        }
        Assert.Contains("IsQueuePaused", MethodBody(MainWindowSource(), "internal void JobsQueueGate_Click"));
        Assert.NotEqual(JobsRadialProgress.StateColor("Exporting"), JobsRadialProgress.StateColor("Waiting"));
    }

    [Fact]
    public void FullJobsSearchUsesNonTextWatermarkAndAccessibleLightweightAffordance()
    {
        var document = DrawerDocument();
        var search = Named(document, "JobsSearchText");
        var placeholder = Named(document, "JobsSearchPlaceholder");
        var icon = Named(document, "JobsSearchIcon");

        Assert.Null(search.Attribute("Text"));
        Assert.Equal("Search Jobs", (string?)search.Attribute("AutomationProperties.Name"));
        Assert.Contains("ElementName=JobsSearchText", (string?)placeholder.Attribute("Visibility"));
        Assert.Contains("StringEmptyToVisibility", (string?)placeholder.Attribute("Visibility"));
        Assert.Equal("Search Jobs…", (string?)placeholder.Attribute("Text"));
        Assert.Equal("False", (string?)placeholder.Attribute("IsHitTestVisible"));
        Assert.Equal("False", (string?)icon.Attribute("IsHitTestVisible"));
        Assert.DoesNotContain("Key.A", MethodBody(MainWindowSource(), "private void MainWindow_PreviewKeyDown"));
    }

    [Fact]
    public void FullJobsUserFacingVocabularyDoesNotExposeHistoryPersistenceTerms()
    {
        var document = DrawerDocument();
        var visibleAttributes = document.Descendants().SelectMany(element => element.Attributes())
            .Where(attribute => attribute.Name.LocalName is "Text" or "Content" or "ToolTip" or
                "AutomationProperties.Name" or "AutomationProperties.HelpText")
            .Select(attribute => attribute.Value).ToList();
        Assert.DoesNotContain(visibleAttributes, value => value.Contains("history", StringComparison.OrdinalIgnoreCase));
        var source = MainWindowSource();
        Assert.DoesNotContain("Clear selected history", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Clear durable Job History", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Open full Jobs history", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("IJobHistoryStore", source);
        Assert.Contains("EncodingHistoryRerun", source);
    }

    [Fact]
    public void ClearAll_IsTransientAndCanDismissNeedsAttentionButNeverActiveWork()
    {
        Assert.True(JobsPresentation.IsDismissibleDrawerRow(JobState.Completed));
        Assert.True(JobsPresentation.IsDismissibleDrawerRow(JobState.NeedsAttention));
        Assert.False(JobsPresentation.IsDismissibleDrawerRow(JobState.Queued));
        Assert.False(JobsPresentation.IsDismissibleDrawerRow(JobState.Running));
        Assert.False(JobsPresentation.IsDismissibleDrawerRow(JobState.Paused));

        var completed = Snapshot(1, JobState.Completed);
        var waiting = Snapshot(2, JobState.Queued);
        Assert.Equal([waiting.JobId], JobsPresentation.VisibleJobs([completed, waiting], new HashSet<Guid> { completed.JobId }).Select(job => job.JobId));
        var source = MainWindowSource();
        Assert.Contains("_dismissedTerminalJobIds.Add(job.JobId)", source);
        var bulk = MethodBody(source, "internal void JobsCancelAll_Click");
        Assert.Contains("IsDismissibleDrawerRow", bulk);
        Assert.DoesNotContain("_jobHistory", bulk);
        Assert.DoesNotContain(DrawerDocument().Descendants(), element => (string?)element.Attribute("Content") == "Clear finished");
    }

    [Fact]
    public void JobsConfirmations_UseReusableDarkDialogInsteadOfNativeMessageBox()
    {
        var source = MainWindowSource();
        var cancel = MethodBody(source, "internal void JobsCancel_Click");
        var cancelAll = MethodBody(source, "internal void JobsCancelAll_Click");
        var dialog = XDocument.Load(Path.Combine(FindRepositoryRoot(), "LightflowStudio", "ConfirmationDialog.xaml"));

        Assert.Contains("ConfirmationDialog.Confirm", cancel);
        Assert.Contains("ConfirmationDialog.Confirm", cancelAll);
        Assert.DoesNotContain("MessageBox", cancel + cancelAll);
        Assert.Equal("{StaticResource WindowBrush}", (string?)dialog.Root!.Attribute("Background"));
        Assert.Contains(dialog.Descendants(), element => (string?)element.Attribute("IsDefault") == "True");
        Assert.Contains(dialog.Descendants(), element => (string?)element.Attribute("IsCancel") == "True");
    }

    [Fact]
    public void FullWorkspace_DeduplicatesModernTerminalJobByStableJobIdAndSchedulerWins()
    {
        var current = Snapshot(1, JobState.Completed);
        var history = History(current.Definition.PlanItem.Definition, current.JobId, JobState.Completed);

        var item = Assert.Single(JobsWorkspacePresentation.Project([current], [history]));

        Assert.True(item.IsCurrent);
        Assert.Equal(history.JobId, item.HistoryRecordId);
        Assert.Equal(current.JobId, item.JobId);
    }

    [Theory]
    [InlineData((int)JobState.Completed)]
    [InlineData((int)JobState.CompletedWithWarnings)]
    [InlineData((int)JobState.Skipped)]
    [InlineData((int)JobState.Failed)]
    [InlineData((int)JobState.Cancelled)]
    public void FullWorkspace_DeletedModernTerminalJobStaysSuppressedWhileDrawerRemainsIndependent(int stateValue)
    {
        var state = (JobState)stateValue;
        var current = Snapshot(1, state) with
        {
            Warnings = state == JobState.Cancelled ? ["Could not remove incomplete output"] : [],
            Errors = state == JobState.Failed ? ["Encoding failed"] : []
        };
        var history = History(current.Definition.PlanItem.Definition, current.JobId, state);
        var initial = Assert.Single(JobsWorkspacePresentation.Project([current], [history]));
        Assert.True(initial.CanRemoveHistory);

        var deletedHistoryIds = JobsWorkspacePresentation.BackingHistoryRecordIds([initial]);
        var tombstones = JobsWorkspacePresentation.TerminalSchedulerJobIdsForDeletedHistory([initial], deletedHistoryIds);
        Assert.Equal(new HashSet<Guid> { current.JobId }, tombstones);
        Assert.Empty(JobsWorkspacePresentation.Project([current], [], suppressedTerminalJobIds: tombstones));
        Assert.Empty(JobsWorkspacePresentation.Project([current], [], suppressedTerminalJobIds: tombstones));
        Assert.Single(JobsPresentation.VisibleJobs([current]));
    }

    [Theory]
    [InlineData((int)JobState.Queued)]
    [InlineData((int)JobState.Running)]
    [InlineData((int)JobState.Paused)]
    [InlineData((int)JobState.NeedsAttention)]
    public void FullWorkspace_TombstonesNeverHideActiveOrRecoverableJobs(int stateValue)
    {
        var state = (JobState)stateValue;
        var current = Snapshot(1, state);
        var saved = History(current.Definition.PlanItem.Definition, current.JobId, JobState.Completed);
        var projected = Assert.Single(JobsWorkspacePresentation.Project([current], [saved],
            suppressedTerminalJobIds: new HashSet<Guid> { current.JobId }));
        Assert.False(projected.CanRemoveHistory);
    }

    [Fact]
    public void FullWorkspace_DeletingSelectedTerminalJobsSuppressesOnlyThoseJobIdsAndReconcilesSelection()
    {
        var deleted = Snapshot(1, JobState.Completed);
        var surviving = Snapshot(2, JobState.Cancelled);
        var tombstones = new HashSet<Guid> { deleted.JobId };

        var projected = JobsWorkspacePresentation.Project([deleted, surviving], [],
            suppressedTerminalJobIds: tombstones);

        Assert.Equal([surviving.JobId], projected.Select(item => item.JobId));
        Assert.Equal(new HashSet<Guid> { surviving.JobId }, JobsWorkspacePresentation.SurvivingSelection(
            [deleted.JobId, surviving.JobId], projected));
    }

    [Fact]
    public void FullJobsUsesInvisiblePersistedSplitterAndResilientTimingColumn()
    {
        var document = DrawerDocument();
        var splitter = Named(document, "FullJobsPaneSplitter");
        var listColumn = Named(document, "FullJobsListColumn");
        var timing = Named(document, "HistoryList").Descendants().Single(element =>
            ((string?)element.Attribute("Text"))?.Contains("Binding Timing", StringComparison.Ordinal) == true);

        Assert.Equal("Transparent", (string?)splitter.Attribute("Background"));
        Assert.Equal("SizeWE", (string?)splitter.Attribute("Cursor"));
        Assert.Equal("PreviousAndNext", (string?)splitter.Attribute("ResizeBehavior"));
        Assert.Equal("False", (string?)splitter.Attribute("Focusable"));
        Assert.Contains(splitter.Descendants(), element => element.Name.LocalName == "Grid" &&
            (string?)element.Attribute("Background") == "Transparent");
        Assert.Equal(WorkspaceState.MinFullJobsListPaneWidth.ToString(), (string?)listColumn.Attribute("MinWidth"));
        Assert.Equal(WorkspaceState.MaxFullJobsListPaneWidth.ToString(), (string?)listColumn.Attribute("MaxWidth"));
        Assert.Equal("104", (string?)timing.Attribute("Width"));
        Assert.Equal("CharacterEllipsis", (string?)timing.Attribute("TextTrimming"));
        Assert.Equal("{Binding Timing, Mode=OneWay}", (string?)timing.Attribute("ToolTip"));
        var source = MainWindowSource();
        Assert.Contains("SetFullJobsListPaneWidth", source);
        Assert.Contains("_deletedFullJobsTerminalJobIds", source);
        var active = Assert.Single(JobsWorkspacePresentation.Project([Snapshot(1, JobState.Running)], []));
        Assert.Equal("ETA 00:00:20", active.Timing);
    }

    [Fact]
    public void FullWorkspace_ProjectsLegacyChildrenButKeepsBackingRecordIndivisible()
    {
        var first = Snapshot(1, JobState.Completed).Definition.PlanItem.Definition;
        var second = Snapshot(2, JobState.Failed).Definition.PlanItem.Definition;
        var record = History([first, second], Guid.NewGuid(), JobState.Failed);

        var items = JobsWorkspacePresentation.Project([], [record]);

        Assert.Equal(2, items.Count);
        Assert.All(items, item => Assert.True(item.IsLegacyProjection));
        Assert.All(items, item => Assert.Equal(record.JobId, item.HistoryRecordId));
        Assert.Equal(new HashSet<Guid> { record.JobId }, JobsWorkspacePresentation.BackingHistoryRecordIds(items));
        Assert.All(items, item =>
        {
            Assert.Contains("Older Jobs saved together", item.LegacyNote);
            Assert.DoesNotContain("History", item.LegacyNote, StringComparison.OrdinalIgnoreCase);
        });
        var scope = JobsWorkspacePresentation.RemovalScope([record]);
        Assert.Contains("2 saved Jobs", scope);
        Assert.DoesNotContain("History", scope, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FullWorkspace_FiltersAndSearchesCurrentAndHistoricalJobsTogether()
    {
        var waiting = Snapshot(1, JobState.Queued);
        var failedItem = Snapshot(2, JobState.Failed).Definition.PlanItem.Definition;
        var failed = History(failedItem, Guid.NewGuid(), JobState.Failed);

        Assert.Single(JobsWorkspacePresentation.Project([waiting], [failed], filter: JobsWorkspaceFilter.Waiting));
        Assert.Single(JobsWorkspacePresentation.Project([waiting], [failed], "input-2", JobsWorkspaceFilter.Failed));
    }

    [Fact]
    public void FullWorkspace_SelectionEligibilityRequiresTheCompleteSelection()
    {
        var waiting = WorkspaceItem(1, JobState.Queued, current: true);
        var paused = WorkspaceItem(2, JobState.Paused, current: true);
        var attention = WorkspaceItem(3, JobState.NeedsAttention, current: true);
        var history = WorkspaceItem(4, JobState.Completed, current: false, history: true);

        Assert.True(JobsSelectionEligibility.For([waiting]).CanPause);
        Assert.True(JobsSelectionEligibility.For([paused]).CanResume);
        Assert.True(JobsSelectionEligibility.For([waiting, paused, attention]).CanCancel);
        Assert.False(JobsSelectionEligibility.For([waiting, history]).CanCancel);
        Assert.False(JobsSelectionEligibility.For([history, waiting]).CanClearHistory);
        Assert.True(JobsSelectionEligibility.For([history]).CanClearHistory);
        Assert.False(JobsSelectionEligibility.For([]).CanCancel);
    }

    [Fact]
    public void FullWorkspace_SelectionSurvivesByJobIdentityAndIntersectsTheVisibleSet()
    {
        var first = WorkspaceItem(1, JobState.Running, current: true);
        var second = WorkspaceItem(2, JobState.Queued, current: true);
        var selected = new[] { first.JobId, second.JobId };

        Assert.Equal(selected.ToHashSet(), JobsWorkspacePresentation.SurvivingSelection(selected, [first, second]));
        Assert.Equal(new HashSet<Guid> { second.JobId }, JobsWorkspacePresentation.SurvivingSelection(selected, [second]));
        Assert.Empty(JobsWorkspacePresentation.SurvivingSelection(selected, []));
    }

    private static XDocument DrawerDocument()
    {
        var document = XDocument.Load(Path.Combine(FindRepositoryRoot(), "LightflowStudio", "MainWindow.xaml"));
        document.Root!.Add(XDocument.Load(Path.Combine(FindRepositoryRoot(), "LightflowStudio", "CompactJobsView.xaml")).Root);
        return document;
    }
    private static string MainWindowSource() => File.ReadAllText(Path.Combine(FindRepositoryRoot(), "LightflowStudio", "MainWindow.xaml.cs"));
    private static string MethodBody(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        var next = source.IndexOf("\n    private ", start + signature.Length, StringComparison.Ordinal);
        return source[start..(next < 0 ? source.Length : next)];
    }
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

    private static EncodingJobHistoryRecord History(JobItemDefinition item, Guid id, JobState state) => History([item], id, state);

    private static EncodingJobHistoryRecord History(IReadOnlyList<JobItemDefinition> items, Guid id, JobState state)
    {
        var completed = DateTimeOffset.Now;
        var options = new EncodingJobOptions(@"C:\", @"C:\out", OutputResolution.FullHd, RecoveryStrategy.Normal,
            new EncodingOptions(), null, "", false, true, false);
        var definition = new JobDefinition<EncodingJobOptions>(id, "video.encode", completed.AddMinutes(-2), options, items);
        var plans = items.Select((item, index) => new JobPlanItem(item, [$@"C:\out\output-{index + 1}.mp4"],
            JobPlanDisposition.Process, JobWorkEstimate.Determinate(JobWorkUnit.MediaDuration, 60), [])).ToList();
        var plan = new JobPlan<EncodingJobOptions>(definition, completed.AddMinutes(-1), plans, [], JobWorkUnit.MediaDuration);
        var results = plans.Select(item => new JobItemResult<EncodingItemResult>(item.Definition.Id, state,
            item.OutputPaths, [], state == JobState.Failed ? ["failed"] : [], null)).ToList();
        var summary = new JobResultSummary(items.Count, state == JobState.Completed ? items.Count : 0, 0, 0, 0,
            state == JobState.Failed ? items.Count : 0);
        var result = new JobResult<EncodingItemResult>(id, state, completed.AddMinutes(-1), completed, results,
            summary, [], state == JobState.Failed ? ["failed"] : []);
        return new(id, "video.encode", definition.CreatedAt, result.StartedAt, completed, state, definition, plan, result);
    }

    private static JobsWorkspaceItem WorkspaceItem(int order, JobState state, bool current, bool history = false)
    {
        var id = Guid.Parse($"00000000-0000-0000-0000-{order:D12}");
        return new(id, history ? id : null, null, current, false, $"Job {order}", "Export", state, null, "Now",
            $@"C:\input-{order}.mp4", $@"C:\output-{order}.mp4", "", "Details", DateTimeOffset.Now, order);
    }
}
