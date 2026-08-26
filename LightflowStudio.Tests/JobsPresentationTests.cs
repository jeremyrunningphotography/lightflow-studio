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
    public void StatusCountsIndependentFileJobsAndRouteAlwaysTargetsFullJobsCompatibility()
    {
        var jobs = new[] { Snapshot(1, JobState.Running), Snapshot(2, JobState.Running) }
            .Concat(Enumerable.Range(3, 8).Select(order => Snapshot(order, JobState.Queued))).ToList();
        Assert.Equal("Jobs · 2 exporting · 8 waiting", JobsPresentation.StatusText(jobs));
        Assert.Equal(JobsRoute.FullJobsCompatibility, JobsPresentation.Route());
        var statusHandler = MethodBody(MainWindowSource(), "private void JobsStatus_Click");
        Assert.Contains("ShellWorkspace.History", statusHandler);
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
        var row = template.Elements().Single(element => element.Name.LocalName == "Grid");
        var reorder = template.Descendants().Where(element => element.Name.LocalName == "Button" &&
            ((string?)element.Attribute("AutomationProperties.Name"))?.StartsWith("Move waiting Job", StringComparison.Ordinal) == true).ToList();

        Assert.Null(drawer.Attribute("Width"));
        Assert.Equal(WorkspaceState.MinJobsDrawerWidth.ToString(), (string?)drawer.Attribute("MinWidth"));
        Assert.Equal("620", (string?)drawer.Attribute("MaxWidth"));
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
        Assert.Equal("{Binding Progress, Mode=OneWay}", (string?)progress.Attribute("Value"));
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
        Assert.Contains("foreach (var id in intended) _exportScheduler.Cancel(id);", source);
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
    public void DrawerResize_UsesCleanBoundaryHitTargetAndColumnOwnedBounds()
    {
        var document = DrawerDocument();
        var splitter = Named(document, "JobsDrawerSplitter");
        var column = Named(document, "JobsDrawerColumn");
        var list = Named(document, "JobsDrawerList");

        Assert.Equal("8", (string?)splitter.Attribute("Width"));
        Assert.Equal("Transparent", (string?)splitter.Attribute("Background"));
        Assert.Equal("SizeWE", (string?)splitter.Attribute("Cursor"));
        Assert.DoesNotContain(splitter.Descendants(), element => element.Name.LocalName is "Thumb" or "Path" or "Ellipse");
        Assert.Equal("620", (string?)column.Attribute("MaxWidth"));
        Assert.Equal(WorkspaceState.MinJobsDrawerWidth.ToString(), (string?)Named(document, "JobsDrawer").Attribute("MinWidth"));
        Assert.Equal("Disabled", (string?)list.Attribute("ScrollViewer.HorizontalScrollBarVisibility"));
        Assert.Contains("SetJobsDrawerWidth(_jobsDrawerWidth)", MainWindowSource());
    }

    [Fact]
    public void DrawerHeader_UsesCompactActiveExportsLabelAndKeepsControlsOnOneLine()
    {
        var document = DrawerDocument();
        var combo = Named(document, "MaximumExportsCombo");
        var button = Named(document, "JobsCancelAllButton");
        var header = combo.Parent!;

        Assert.Contains(header.Elements(), element => (string?)element.Attribute("Text") == "Active exports");
        Assert.DoesNotContain(header.Descendants(), element => (string?)element.Attribute("Text") == "Maximum simultaneous exports");
        Assert.Equal("1", (string?)combo.Attribute("Grid.Column"));
        Assert.Equal("2", (string?)button.Attribute("Grid.Column"));
        Assert.Equal("65", (string?)button.Attribute("MinWidth"));
        Assert.Contains("simultaneously", (string?)combo.Attribute("ToolTip"));
        Assert.Equal(340, WorkspaceState.MinJobsDrawerWidth);
    }

    [Fact]
    public void DisclosureAndTerminalRows_UseLightflowStateAndHideWaitingControls()
    {
        var template = Named(DrawerDocument(), "JobsDrawerList").Descendants()
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
        var template = Named(DrawerDocument(), "JobsDrawerList").Descendants()
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
        Assert.Contains("JobsPresentation.Reconcile(_jobsDrawerCards, cards)", apply);
        Assert.DoesNotContain("_jobsDrawerCards.Clear", apply);
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
        var template = Named(DrawerDocument(), "JobsDrawerList").Descendants()
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
    public void DrawerPullOwnsToggleAndShellColumnsPushMainContent()
    {
        var document = DrawerDocument();
        var pull = Named(document, "JobsDrawerPullButton");
        var main = Named(document, "MainTabs");
        var drawer = Named(document, "JobsDrawer");
        var splitter = Named(document, "JobsDrawerSplitter");
        var source = MainWindowSource();

        Assert.Equal("JobsDrawerPull_Click", (string?)pull.Attribute("Click"));
        Assert.Equal("Right", (string?)pull.Attribute("HorizontalAlignment"));
        Assert.Equal("0,0,28,0", (string?)main.Attribute("Margin"));
        Assert.Equal("2", (string?)drawer.Attribute("Grid.Column"));
        Assert.Equal("1", (string?)splitter.Attribute("Grid.Column"));
        Assert.Equal("PreviousAndNext", (string?)splitter.Attribute("ResizeBehavior"));
        Assert.DoesNotContain(document.Descendants(), element =>
            (string?)element.Attribute("Click") == "JobsDrawerClose_Click");
        Assert.Contains("OpenJobsDrawer", MethodBody(source, "private void JobsDrawerPull_Click"));
        Assert.Contains("CloseJobsDrawer(true)", MethodBody(source, "private void JobsDrawerPull_Click"));
        var acceptedStart = source.IndexOf("_exportScheduler.SubmissionAccepted", StringComparison.Ordinal);
        var acceptedEnd = source.IndexOf("_workspaceState =", acceptedStart, StringComparison.Ordinal);
        var accepted = source[acceptedStart..acceptedEnd];
        Assert.Equal(1, accepted.Split("OpenJobsDrawer();", StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain("OpenJobsDrawer", MethodBody(source, "private void ExportScheduler_Changed"));
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
        var bulk = MethodBody(source, "private void JobsCancelAll_Click");
        Assert.Contains("IsDismissibleDrawerRow", bulk);
        Assert.DoesNotContain("_jobHistory", bulk);
        Assert.DoesNotContain(DrawerDocument().Descendants(), element => (string?)element.Attribute("Content") == "Clear finished");
    }

    [Fact]
    public void JobsConfirmations_UseReusableDarkDialogInsteadOfNativeMessageBox()
    {
        var source = MainWindowSource();
        var cancel = MethodBody(source, "private void JobsCancel_Click");
        var cancelAll = MethodBody(source, "private void JobsCancelAll_Click");
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

    private static XDocument DrawerDocument() => XDocument.Load(Path.Combine(FindRepositoryRoot(), "LightflowStudio", "MainWindow.xaml"));
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
