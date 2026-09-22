using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Xunit;

namespace LightflowStudio.Tests;

/// <summary>Exercises the real MainWindow route and deferred Jobs row-template realization.</summary>
[Collection("STA dispatcher tests")]
public sealed class JobsWorkspaceLiveInteractionTests
{
    [Fact]
    public Task VisualIndexFinishedWhileJobsHiddenAppearsOnOpeningWorkspace() => RunAsync(0, async window =>
    {
        var jobs = (VisualIndexJobs)typeof(MainWindow).GetField("_visualIndexJobs",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(window)!;
        // A missing source finishes quickly and exercises the real adapter's terminal notification.
        jobs.Queue(new(VisualIndexJobs.Capability, [Guid.NewGuid()]), _ => "missing-index.mp4");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (jobs.Jobs.Count == 0 || jobs.Jobs.Any(job => !JobsPresentation.IsTerminal(job.State)))
            await Task.Delay(10, timeout.Token);
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Assert.Empty(window.HistoryList.Items);
        RaiseClick(window.JobsStatusButton);
        await RealizeJobsWorkspaceAsync(window);
        var item = Assert.IsType<JobsWorkspaceItem>(Assert.Single(window.HistoryList.Items));
        Assert.Equal(jobs.Jobs.Single().JobId, item.JobId);
        Assert.Equal("missing-index.mp4", item.Name);
        var card = Assert.IsType<JobCardPresentation>(Assert.Single(CompactJobs(window).CompactJobsList.Items));
        Assert.True(card.CanRetry);
        Assert.False(card.ShowInlineRetry);
        Assert.True(card.CanClear);
        window.JobsClear_Click(new System.Windows.Controls.Button { Tag = card.JobId }, new RoutedEventArgs());
        Assert.Empty(CompactJobs(window).CompactJobsList.Items);
        Assert.Single(window.HistoryList.Items); // compact Clear never removes the full-view result
    });

    [Fact]
    public Task ContextMenusTargetTheirRowPreserveSelectionAndMixedCleanupUsesTypedStores() => RunAsync(2, async window =>
    {
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var visual = (VisualIndexJobs)typeof(MainWindow).GetField("_visualIndexJobs", flags)!.GetValue(window)!;
        var files = (FileOperationJobs)typeof(MainWindow).GetField("_fileOperationJobs", flags)!.GetValue(window)!;
        var fileHistory = (FileOperationHistoryStore)typeof(FileOperationJobs).GetField("_history", flags)!.GetValue(files)!;
        var intent = new FileOperationIntent(Guid.NewGuid(), FileOperationKind.Move, [new(null, @"C:\original.mov")],
            @"C:\outputs", DateTimeOffset.UtcNow, null, false, FileOperationExecution.Job);
        fileHistory.Complete(intent, new(intent.OperationId, FileOperationState.Completed, 1, 10, [], DateTimeOffset.UtcNow));
        var assetId = Guid.NewGuid();
        visual.Queue(new(VisualIndexJobs.Capability, [assetId]), _ => "missing-index.mp4");
        await WaitUntilAsync(() => visual.Jobs.Count == 1 && JobsPresentation.IsTerminal(visual.Jobs[0].State));
        RaiseClick(window.JobsStatusButton);
        await RealizeJobsWorkspaceAsync(window);
        Assert.Equal(4, window.HistoryList.Items.Count);
        window.HistoryList.SelectAll();
        Assert.True(window.JobsClearHistoryButton.IsEnabled);
        var visualItem = window.HistoryList.Items.Cast<JobsWorkspaceItem>().Single(item => item.Capability == "Visual Index");
        var row = Assert.IsType<ListBoxItem>(window.HistoryList.ItemContainerGenerator.ContainerFromItem(visualItem));
        window.JobRow_PreviewMouseRightButtonDown(row, new System.Windows.Input.MouseButtonEventArgs(
            System.Windows.Input.Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Right)
            { RoutedEvent = UIElement.PreviewMouseRightButtonDownEvent });
        Assert.Equal(4, window.HistoryList.SelectedItems.Count);
        window.JobRow_ContextMenuOpening(row, null!);
        Assert.Equal(new[] { "Retry", "Remove Job…" }, row.ContextMenu.Items.Cast<MenuItem>().Select(item => item.Header));
        row.ContextMenu.Items.Cast<MenuItem>().Single(item => Equals(item.Header, "Retry"))
            .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        await WaitUntilAsync(() => visual.Jobs.Count == 2 && visual.Jobs.All(job => JobsPresentation.IsTerminal(job.State)));
        Assert.All(visual.Jobs, job => Assert.Equal(assetId, job.Options.AssetId));
        await RealizeJobsWorkspaceAsync(window);

        var fileItem = window.HistoryList.Items.Cast<JobsWorkspaceItem>().Single(item => item.JobId == intent.OperationId);
        var fileRow = Assert.IsType<ListBoxItem>(window.HistoryList.ItemContainerGenerator.ContainerFromItem(fileItem));
        window.JobRow_ContextMenuOpening(fileRow, null!);
        Assert.Equal("Remove Job…", Assert.IsType<MenuItem>(Assert.Single(fileRow.ContextMenu.Items)).Header);
        window.HistoryList.UnselectAll();
        window.HistoryList.SelectedItem = visualItem;
        window.JobRow_PreviewMouseRightButtonDown(fileRow, new System.Windows.Input.MouseButtonEventArgs(
            System.Windows.Input.Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Right)
            { RoutedEvent = UIElement.PreviewMouseRightButtonDownEvent });
        Assert.Equal(intent.OperationId, Assert.IsType<JobsWorkspaceItem>(Assert.Single(window.HistoryList.SelectedItems)).JobId);

        window.HistoryList.SelectAll();
        Assert.True(window.JobsClearHistoryButton.IsEnabled);
        var confirmation = window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
        {
            var dialog = Assert.Single(System.Windows.Application.Current.Windows.OfType<ConfirmationDialog>());
            RaiseClick(dialog.ConfirmButton);
        }));
        RaiseClick(window.JobsClearHistoryButton);
        await confirmation;
        await RealizeJobsWorkspaceAsync(window);
        Assert.Empty(window.HistoryList.Items);
        Assert.Empty(fileHistory.Load());
        RaiseClick(window.RefreshHistoryButton);
        Assert.Empty(window.HistoryList.Items);
        Assert.Equal(2, visual.Jobs.Count); // presentation cleanup never destroys capability results
        Assert.Equal(2, CompactJobs(window).CompactJobsList.Items.Count);
        RaiseClick(CompactJobs(window).JobsClearAllButton);
        Assert.Empty(CompactJobs(window).CompactJobsList.Items);
    });

    [Fact]
    public Task ClearAllProtectsWaitingWorkAndDoesNotChangeQueueGateOrRecovery() => RunAsync(1, async window =>
    {
        var scheduler = (GlobalExportScheduler)typeof(MainWindow).GetField("_exportScheduler",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(window)!;
        scheduler.PauseQueue();
        Guid QueueWaiting()
        {
            var record = HistoryRecord();
            var target = Path.Combine(Path.GetTempPath(), $"jobs-queue-{Guid.NewGuid():N}.mp4");
            var item = record.Plan.Items[0] with
            {
                Definition = record.Plan.Items[0].Definition with { SourceIdentity = target + ".source" },
                OutputPaths = [target]
            };
            var plan = record.Plan with { Definition = record.Definition with { Items = [item.Definition] }, Items = [item] };
            var admission = scheduler.Admit(ExportSubmissionProposal.FromPlan(plan));
            Assert.True(admission.Accepted);
            return Assert.Single(admission.Jobs).JobId;
        }
        var waitingId = QueueWaiting();
        var cancelledId = QueueWaiting();
        scheduler.Cancel(cancelledId);
        await WaitUntilAsync(() => CompactJobs(window).CompactJobsList.Items.Count == 2);
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Assert.True(CompactJobs(window).JobsClearAllButton.IsEnabled);
        Assert.True(CompactJobs(window).JobsCancelAllButton.IsEnabled);
        RaiseClick(CompactJobs(window).JobsClearAllButton);
        Assert.Equal(waitingId, Assert.IsType<JobCardPresentation>(Assert.Single(CompactJobs(window).CompactJobsList.Items)).JobId);
        Assert.Equal(JobState.Queued, scheduler.Jobs.Single(job => job.JobId == waitingId).State);
        Assert.True(scheduler.IsQueuePaused);

        RaiseClick(window.JobsStatusButton);
        await RealizeJobsWorkspaceAsync(window);
        Assert.Equal(3, window.HistoryList.Items.Count);
        window.HistoryList.SelectAll();
        Assert.False(window.JobsClearHistoryButton.IsEnabled);
        Assert.True(window.JobsClearAllHistoryButton.IsEnabled);
        var confirmation = window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
            RaiseClick(Assert.Single(System.Windows.Application.Current.Windows.OfType<ConfirmationDialog>()).ConfirmButton)));
        RaiseClick(window.JobsClearAllHistoryButton);
        await confirmation;
        await RealizeJobsWorkspaceAsync(window);
        Assert.Equal(waitingId, Assert.IsType<JobsWorkspaceItem>(Assert.Single(window.HistoryList.Items)).JobId);
        RaiseClick(window.RefreshHistoryButton);
        Assert.Single(window.HistoryList.Items);
        Assert.True(scheduler.IsQueuePaused);
        Assert.Equal(JobState.Queued, scheduler.Jobs.Single(job => job.JobId == waitingId).State);
        Assert.Equal(JobState.Cancelled, scheduler.Jobs.Single(job => job.JobId == cancelledId).State);
        scheduler.Cancel(waitingId);
    });

    [Fact]
    public Task ExportOutputLinkAndContextRerunWorkWithNoLutFolder() => RunAsync(0, async window =>
    {
        var root = Path.Combine(Path.GetTempPath(), "jobs-rerun-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var source = Path.Combine(root, "original.mp4");
            var output = Path.Combine(root, "output with spaces.mp4");
            File.WriteAllBytes(source, new byte[100]);
            File.WriteAllText(output, "completed output");
            var history = (IJobHistoryStore)typeof(MainWindow).GetField("_jobHistory",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(window)!;
            history.Add(HistoryRecord(source, output));
            RaiseClick(window.RefreshHistoryButton);
            RaiseClick(window.JobsStatusButton);
            await RealizeJobsWorkspaceAsync(window);
            window.HistoryList.SelectedIndex = 0;
            await RealizeJobsWorkspaceAsync(window);
            Assert.Equal(Visibility.Visible, window.JobsClearButton.Visibility);
            Assert.All(new[] { window.JobsPauseButton, window.JobsResumeButton, window.JobsRetryButton,
                window.JobsCancelButton, window.HistoryRerunButton }, button => Assert.Equal(Visibility.Collapsed, button.Visibility));
            System.Diagnostics.ProcessStartInfo? request = null;
            window.OpenJobOutputFolder = value => request = value;
            var path = Descendants(window.HistoryDetails).OfType<TextBlock>()
                .SelectMany(block => block.Inlines.OfType<System.Windows.Documents.Hyperlink>()).Single();
            path.RaiseEvent(new RoutedEventArgs(System.Windows.Documents.Hyperlink.ClickEvent));
            Assert.NotNull(request);
            Assert.Equal("explorer.exe", request.FileName);
            Assert.Equal($"/select,\"{output}\"", request.Arguments);
            Assert.True(request.UseShellExecute);
            Assert.Null(JobOutputLocation.RevealRequest(Path.Combine(root, "missing.mp4")));
            var row = Assert.IsType<ListBoxItem>(window.HistoryList.ItemContainerGenerator.ContainerFromIndex(0));
            window.JobRow_ContextMenuOpening(row, null!);
            row.ContextMenu.Items.Cast<MenuItem>().Single(item => Equals(item.Header, "Review & Rerun…"))
                .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.Equal(ShellDestinationSelection.Index(ShellDestination.CompatibilityExportReview), window.MainTabs.SelectedIndex);
            Assert.Equal(LutCatalog.NoLut, window.LutSelection.SelectedItem);
            Assert.True(window.IsVisible);
            Assert.Equal("completed output", File.ReadAllText(output));
        }
        finally { Directory.Delete(root, true); }

        static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
        {
            for (var index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); index++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, index);
                yield return child;
                foreach (var value in Descendants(child)) yield return value;
            }
        }
    });

    [Fact]
    public Task StartupCompletion_WaitsForHistoryAfterEarlyItemsSourceBinding() => StaDispatcher.RunAsync(async () =>
    {
        TestWpfApplication.EnsureLoaded();
        var root = Path.Combine(Path.GetTempPath(), $"lightflow-startup-gate-{Guid.NewGuid():N}");
        var startup = await LightflowStorageCoordinator.StartAsync(root);
        var storage = startup.Coordinator!;
        new JobHistoryStore(storage.Locations.JobHistoryPath).Add(HistoryRecord());
        var gate = new GatedBrowserStorageProvider(storage.BrowserStorage);
        // Replace only the fixture's storage enumeration, holding startup at its real first asynchronous boundary.
        typeof(LightflowStorageCoordinator).GetField("<BrowserStorage>k__BackingField",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(storage, gate);
        storage.SaveSettings(storage.Settings with { BackupCatalogOnClose = false });
            var window = new MainWindow(storage, startup.Status, startup.Diagnostic)
            { Left = -32000, Top = -32000, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual };
        try
        {
            Assert.False(window.StartupCompletion.IsCompleted);
            window.Show();
            await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(window.IsLoaded);
            Assert.NotNull(window.HistoryList.ItemsSource);
            Assert.Empty(window.HistoryList.Items);
            Assert.False(window.StartupCompletion.IsCompleted);
            gate.Release.TrySetResult();
            Assert.True(await window.StartupCompletion.WaitAsync(TimeSpan.FromSeconds(30)));
            Assert.IsType<JobsWorkspaceItem>(Assert.Single(window.HistoryList.Items));
        }
        finally
        {
            gate.Release.TrySetResult();
            try { await window.StartupCompletion.WaitAsync(TimeSpan.FromSeconds(30)); }
            finally
            {
                window.Close();
                await storage.DisposeAsync();
                try { Directory.Delete(root, true); } catch (IOException) { }
            }
        }
    });

    private sealed class GatedBrowserStorageProvider(IBrowserStorageProvider inner) : IBrowserStorageProvider
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<IReadOnlyList<BrowserStorageEntry>> ListAsync(CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return await inner.ListAsync(cancellationToken);
        }
    }

    [Fact]
    public async Task StatusJobs_ActivatesEmptyWorkspaceAndCompactJobsSharesQueueControls()
    {
        await RunAsync(seedHistoryCount: 0, async window =>
        {
            Assert.Empty(window.HistoryList.Items);
            RaiseClick(window.JobsStatusButton);
            await RealizeJobsWorkspaceAsync(window);

            Assert.Equal(ShellDestinationSelection.Index(ShellDestination.Jobs), window.MainTabs.SelectedIndex);
            Assert.True(window.IsVisible);
            Assert.Equal(Visibility.Collapsed, window.HomeRightPanel.Visibility);

            Assert.False(window.FullJobsQueueGateButton.IsEnabled);
            Assert.False(CompactJobs(window).JobsQueueGateButton.IsEnabled);
            RaiseClick(window.FullJobsQueueGateButton);
            Assert.Equal("Pause Queue", window.FullJobsQueueGateButton.Content);
            // A persisted pause must remain resumable even after its work finishes.
            var scheduler = (GlobalExportScheduler)typeof(MainWindow).GetField("_exportScheduler",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(window)!;
            scheduler.PauseQueue();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.Equal("Resume Queue", window.FullJobsQueueGateButton.Content);
            Assert.Equal("Resume Queue", CompactJobs(window).JobsQueueGateButton.Content);
            Assert.Contains("Queue paused", window.JobsStatusButton.Content.ToString());
            Assert.Same(window.FindResource("ShellSelectionBrush"), window.FullJobsQueueGateButton.Background);

            RaiseClick(window.JobsBackToBrowserButton);
            ToggleJobsPanel(window);
            Assert.Equal(Visibility.Visible, window.HomeRightPanel.Visibility);
            RaiseClick(CompactJobs(window).JobsQueueGateButton);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.Equal("Pause Queue", window.FullJobsQueueGateButton.Content);
            Assert.DoesNotContain("Queue paused", window.JobsStatusButton.Content.ToString());
            ToggleJobsPanel(window);
            Assert.Equal(Visibility.Collapsed, window.HomeRightPanel.Visibility);
        });
    }

    [Fact]
    public async Task StatusJobs_RealizesDurableHistoryRowAndCanNavigateAwayAndBack()
    {
        await RunAsync(seedHistoryCount: 1, async window =>
        {
            RaiseClick(window.JobsStatusButton);
            await RealizeJobsWorkspaceAsync(window);

            var item = Assert.IsType<JobsWorkspaceItem>(Assert.Single(window.HistoryList.Items));
            Assert.NotNull(item.HistoryRecord);
            Assert.NotNull(window.HistoryList.ItemContainerGenerator.ContainerFromItem(item));
            Assert.True(window.IsVisible);

            window.MainTabs.SelectedIndex = ShellDestinationSelection.Index(ShellDestination.Home);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            RaiseClick(window.JobsStatusButton);
            await RealizeJobsWorkspaceAsync(window);

            Assert.Equal(ShellDestinationSelection.Index(ShellDestination.Jobs), window.MainTabs.SelectedIndex);
            Assert.True(window.IsVisible);
        });
    }

    [Fact]
    public async Task FullJobs_ExtendedSelectionSurvivesRefreshFiltersDeterministicallyAndBackPreservesShell()
    {
        await RunAsync(seedHistoryCount: 2, async window =>
        {
            RaiseClick(window.JobsStatusButton);
            RaiseClick(window.RefreshHistoryButton);
            await RealizeJobsWorkspaceAsync(window);

            Assert.Equal(SelectionMode.Extended, window.HistoryList.SelectionMode);
            Assert.Equal(2, window.HistoryList.Items.Count);
            Assert.Equal(Visibility.Visible, window.JobsSearchPlaceholder.Visibility);
            window.HistoryList.SelectAll();
            Assert.Equal(2, window.HistoryList.SelectedItems.Count);
            Assert.True(window.JobsClearHistoryButton.IsEnabled);

            var containers = window.HistoryList.Items.Cast<object>().Select(item =>
                Assert.IsType<ListBoxItem>(window.HistoryList.ItemContainerGenerator.ContainerFromItem(item))).ToList();
            Assert.All(containers, container =>
            {
                var chrome = Assert.IsType<Border>(container.Template.FindName("Chrome", container));
                var rail = Assert.IsType<Border>(container.Template.FindName("SelectionRail", container));
                Assert.Same(window.FindResource("ShellSelectionBrush"), chrome.Background);
                Assert.Equal(Visibility.Visible, rail.Visibility);
            });
            containers[0].Focus();
            await Dispatcher.Yield(DispatcherPriority.Render);
            var focusedChrome = Assert.IsType<Border>(containers[0].Template.FindName("Chrome", containers[0]));
            Assert.Equal(new Thickness(1), focusedChrome.BorderThickness);
            Assert.Same(window.FindResource("MutedTextBrush"), focusedChrome.BorderBrush);

            window.JobsSearchText.Text = "source";
            await Dispatcher.Yield(DispatcherPriority.DataBind);
            Assert.Equal(Visibility.Collapsed, window.JobsSearchPlaceholder.Visibility);
            window.JobsSearchText.Focus();
            window.JobsSearchText.SelectAll();
            Assert.Equal(window.JobsSearchText.Text.Length, window.JobsSearchText.SelectionLength);
            Assert.Equal(2, window.HistoryList.SelectedItems.Count);
            window.JobsSearchText.Clear();
            await Dispatcher.Yield(DispatcherPriority.DataBind);
            Assert.Equal(Visibility.Visible, window.JobsSearchPlaceholder.Visibility);

            var selectedIds = window.HistoryList.SelectedItems.Cast<JobsWorkspaceItem>().Select(item => item.JobId).ToHashSet();
            RaiseClick(window.RefreshHistoryButton);
            Assert.Equal(selectedIds, window.HistoryList.SelectedItems.Cast<JobsWorkspaceItem>().Select(item => item.JobId).ToHashSet());

            window.JobsSearchText.Text = "does-not-match-any-job";
            await Dispatcher.Yield(DispatcherPriority.DataBind);
            Assert.Empty(window.HistoryList.SelectedItems);
            Assert.False(window.JobsClearHistoryButton.IsEnabled);
            window.JobsSearchText.Clear();
            await Dispatcher.Yield(DispatcherPriority.DataBind);
            Assert.Empty(window.HistoryList.SelectedItems);

            RaiseClick(window.JobsBackToBrowserButton);
            Assert.Equal(ShellDestinationSelection.Index(ShellDestination.Home), window.MainTabs.SelectedIndex);
            RaiseClick(window.JobsStatusButton);
            await RealizeJobsWorkspaceAsync(window);
            Assert.Equal(2, window.HistoryList.Items.Count);
        });
    }

    [Fact]
    public async Task FullJobs_InvisibleSplitterResizesOnlyItsPanesAndPreservesSelectionAndDetails()
    {
        await RunAsync(seedHistoryCount: 2, async window =>
        {
            RaiseClick(window.JobsStatusButton);
            await RealizeJobsWorkspaceAsync(window);
            window.HistoryList.SelectedIndex = 0;
            var selected = Assert.IsType<JobsWorkspaceItem>(window.HistoryList.SelectedItem);
            var details = window.HistoryDetails.Content;
            var panelWidth = window.RightPanelColumn.Width;
            var browserWidth = window.BrowserNavigationColumn.Width;
            var original = window.FullJobsListColumn.ActualWidth;

            window.FullJobsPaneSplitter.RaiseEvent(new System.Windows.Controls.Primitives.DragStartedEventArgs(0, 0)
                { RoutedEvent = System.Windows.Controls.Primitives.Thumb.DragStartedEvent });
            window.FullJobsPaneSplitter.RaiseEvent(new System.Windows.Controls.Primitives.DragDeltaEventArgs(40, 0)
                { RoutedEvent = System.Windows.Controls.Primitives.Thumb.DragDeltaEvent });
            window.FullJobsPaneSplitter.RaiseEvent(new System.Windows.Controls.Primitives.DragCompletedEventArgs(40, 0, false)
                { RoutedEvent = System.Windows.Controls.Primitives.Thumb.DragCompletedEvent });
            await RealizeJobsWorkspaceAsync(window);

            Assert.True(window.FullJobsListColumn.ActualWidth > original);
            Assert.Equal(selected.JobId, Assert.IsType<JobsWorkspaceItem>(window.HistoryList.SelectedItem).JobId);
            Assert.Equal(details, window.HistoryDetails.Content);
            Assert.Equal(panelWidth, window.RightPanelColumn.Width);
            Assert.Equal(browserWidth, window.BrowserNavigationColumn.Width);
            Assert.False(window.FullJobsPaneSplitter.Focusable);
            Assert.True(VirtualizingPanel.GetIsVirtualizing(window.HistoryList));

            RaiseSplitterDrag(window.FullJobsPaneSplitter, -10000);
            await RealizeJobsWorkspaceAsync(window);
            Assert.True(window.FullJobsListColumn.ActualWidth >= WorkspaceState.MinFullJobsListPaneWidth);
            RaiseSplitterDrag(window.FullJobsPaneSplitter, 10000);
            await RealizeJobsWorkspaceAsync(window);
            Assert.True(window.FullJobsListColumn.ActualWidth <= WorkspaceState.MaxFullJobsListPaneWidth);
            var detailsColumn = window.FullJobsListColumn.Parent is Grid owner ? owner.ColumnDefinitions[2] : null;
            Assert.NotNull(detailsColumn);
            Assert.True(detailsColumn.ActualWidth >= 320);
        });
    }

    [Fact]
    public async Task FullJobs_RestoresPersistedListPaneWidth()
    {
        await RunAsync(seedHistoryCount: 0, window =>
        {
            Assert.Equal(590, window.FullJobsListColumn.Width.Value);
            return Task.CompletedTask;
        }, persistedJobsListWidth: 590);
    }

    [Fact]
    public async Task SharedPanelConsumesWidthAndBrowserGroupsReflowWithoutLosingWideLocationsPreference()
    {
        await RunAsync(seedHistoryCount: 0, async window =>
        {
            Assert.Equal(1120, window.ActualWidth, 1);
            Assert.Equal(520, window.BrowserNavigationColumn.ActualWidth, 1);
            var playerHost = window.BrowserPlayerHost;
            Assert.Equal(0, Grid.GetRow(window.BrowserNavigationToolbar));
            Assert.Equal(2, Grid.GetRow(window.BrowserQueryToolbar));
            Assert.Equal(4, Grid.GetRow(window.BrowserSelectionActionToolbar));

            ToggleJobsPanel(window);
            await RealizeJobsWorkspaceAsync(window);
            Assert.Equal(Visibility.Visible, window.HomeRightPanel.Visibility);
            Assert.Equal(380, window.RightPanelColumn.ActualWidth, 1);
            Assert.True(window.BrowserNavigationColumn.ActualWidth < 520);
            Assert.Equal(0, Grid.GetRow(window.BrowserNavigationToolbar));
            Assert.Equal(2, Grid.GetRow(window.BrowserQueryToolbar));
            Assert.Equal(4, Grid.GetRow(window.BrowserSelectionActionToolbar));
            AssertContained(window.BrowserCenter, window.BrowserWorkspaceRoot);
            AssertContained(window.BrowserBrowseToolbar, window.BrowserCenter);
            AssertContained(window.BrowserSelectionActionToolbar, window.BrowserCenter);
            AssertContained(window.BrowserGridHost, window.BrowserCenter);
            AssertContained(playerHost, window.BrowserCenter);

            foreach (var panelWidth in new[] { 340d, 380d, 600d })
            {
                window.RightPanelColumn.Width = new GridLength(panelWidth);
                window.RightPanelSplitter.RaiseEvent(new System.Windows.Controls.Primitives.DragCompletedEventArgs(0, 0, false)
                    { RoutedEvent = System.Windows.Controls.Primitives.Thumb.DragCompletedEvent });
                window.UpdateLayout();
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                window.UpdateLayout();
                AssertContained(window.BrowserCenter, window.BrowserWorkspaceRoot);
                AssertContained(window.BrowserBrowseToolbar, window.BrowserCenter);
                AssertContained(window.BrowserSelectionActionToolbar, window.BrowserCenter);
                AssertContained(playerHost, window.BrowserCenter);
                AssertContained(window.BrowserIncludeSubfoldersButton, window.BrowserNavigationToolbar);
                AssertContained(window.BrowserMediaTypeGroup, window.BrowserQueryToolbar);
                AssertContained(window.BrowserSearchGroup, window.BrowserQueryToolbar);
                AssertContained(window.BrowserFilterButton, window.BrowserQueryToolbar);
                AssertContained(window.BrowserSortGroup, window.BrowserQueryToolbar);
                AssertContained(window.BrowserColorActions, window.BrowserSelectionActionToolbar);
                AssertContained(window.BrowserExportButton, window.BrowserSelectionActionToolbar);
            }

            ToggleJobsPanel(window);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            window.UpdateLayout();
            Assert.Equal(Visibility.Collapsed, window.HomeRightPanel.Visibility);
            Assert.Equal(520, window.BrowserNavigationColumn.ActualWidth, 1);
            AssertContained(window.BrowserCenter, window.BrowserWorkspaceRoot);
            Assert.Same(playerHost, window.BrowserPlayerHost);
        }, persistedLocationsWidth: 520, persistedPanelWidth: 380, windowWidth: 1120);
    }

    [Fact]
    public async Task LowerBrowserControls_SwitchWholeGroupsBetweenWideAndDrawerConstrainedLayouts()
    {
        await RunAsync(seedHistoryCount: 0, async window =>
        {
            Assert.Equal(0, Grid.GetRow(window.BrowserNavigationToolbar));
            Assert.Equal(2, Grid.GetRow(window.BrowserQueryToolbar));
            var startsWide = UsesCombinedLowerRow(window);
            Assert.Equal(ExpectedSelectionActionRow(window), Grid.GetRow(window.BrowserSelectionActionToolbar));
            Assert.Equal(0, Grid.GetColumn(window.BrowserQueryToolbar));
            Assert.Equal(startsWide ? 1 : 0, Grid.GetColumn(window.BrowserSelectionActionToolbar));
            Assert.Equal(startsWide ? 1 : 2, Grid.GetColumnSpan(window.BrowserSelectionActionToolbar));
            Assert.Equal(2, Grid.GetColumnSpan(window.BrowserNavigationToolbar));
            var navigationBounds = window.BrowserNavigationToolbar.TransformToAncestor(window.BrowserBrowseToolbar)
                .TransformBounds(new Rect(new Point(), window.BrowserNavigationToolbar.RenderSize));
            Assert.Equal(window.BrowserBrowseToolbar.ActualWidth, navigationBounds.Right, 1);
            if (startsWide)
            {
                Assert.True(window.BrowserNavigationToolbar.ActualWidth > window.BrowserQueryToolbar.ActualWidth);
                Assert.True(window.BrowserCurrentPath.ActualWidth > 300);
                Assert.Equal(window.BrowserQueryToolbar.ActualHeight, window.BrowserSelectionActionToolbar.ActualHeight, 1);
            }
            else
            {
                AssertContained(window.BrowserQueryToolbar, window.BrowserBrowseToolbar);
                AssertContained(window.BrowserSelectionActionToolbar, window.BrowserBrowseToolbar);
            }
            AssertRow2ControlHeights(window);

            ToggleJobsPanel(window);
            await RealizeJobsWorkspaceAsync(window);
            Assert.Equal(2, Grid.GetRow(window.BrowserQueryToolbar));
            Assert.Equal(4, Grid.GetRow(window.BrowserSelectionActionToolbar));
            navigationBounds = window.BrowserNavigationToolbar.TransformToAncestor(window.BrowserBrowseToolbar)
                .TransformBounds(new Rect(new Point(), window.BrowserNavigationToolbar.RenderSize));
            Assert.Equal(window.BrowserBrowseToolbar.ActualWidth, navigationBounds.Right, 1);
            AssertRow2ControlHeights(window);
            Assert.Equal(0, Grid.GetColumn(window.BrowserSelectionActionToolbar));
            Assert.Equal(2, Grid.GetColumnSpan(window.BrowserSelectionActionToolbar));
            AssertContained(window.BrowserQueryToolbar, window.BrowserBrowseToolbar);
            AssertContained(window.BrowserSelectionActionToolbar, window.BrowserBrowseToolbar);
            AssertContained(window.BrowserGridHost, window.BrowserCenter);

            ToggleJobsPanel(window);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            window.UpdateLayout();
            var endsWide = UsesCombinedLowerRow(window);
            Assert.Equal(ExpectedSelectionActionRow(window), Grid.GetRow(window.BrowserSelectionActionToolbar));
            Assert.Equal(endsWide ? 1 : 0, Grid.GetColumn(window.BrowserSelectionActionToolbar));
            Assert.Equal(endsWide ? 1 : 2, Grid.GetColumnSpan(window.BrowserSelectionActionToolbar));
        }, persistedLocationsWidth: 280, persistedPanelWidth: 380, windowWidth: 1800);
    }

    [Fact]
    public async Task CompactJobsRetainsCardsWhileHiddenAndUsesSharedWidthWithoutClippingControls()
    {
        await RunAsync(0, async window =>
        {
            await RealizeJobsWorkspaceAsync(window);
            var view = CompactJobs(window);
            var apply = typeof(MainWindow).GetMethod("ApplyJobsPresentation", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            var queued = Snapshot(1, JobState.Queued);
            apply.Invoke(window, new object[] { new[] { queued } });
            window.OpenJobsPanel();
            await RealizeJobsWorkspaceAsync(window);
            var card = Assert.IsType<JobCardPresentation>(Assert.Single(view.CompactJobsList.Items));
            window.JobExpansionToggle_Click(new Button { Tag = queued.JobId }, new RoutedEventArgs());
            Assert.True(card.IsExpanded);
            window.HomeRightPanel.SelectSurface("inspector");
            apply.Invoke(window, new object[] { new[] { queued with { State = JobState.Running, ProgressPercent = 42 } } });
            Assert.Equal("inspector", window.HomeRightPanel.ActiveSurface);
            Assert.Same(card, Assert.Single(view.CompactJobsList.Items));
            Assert.Equal(42, card.Progress);
            Assert.True(card.IsExpanded);
            window.HomeRightPanel.SelectSurface("jobs");
            window.RightPanelColumn.Width = new GridLength(280);
            window.RightPanelSplitter.RaiseEvent(new System.Windows.Controls.Primitives.DragCompletedEventArgs(0, 0, false)
                { RoutedEvent = System.Windows.Controls.Primitives.Thumb.DragCompletedEvent });
            await RealizeJobsWorkspaceAsync(window);
            AssertContained(view.MaximumExportsCombo, view);
            AssertContained(view.JobsQueueGateButton, view);
            AssertContained(view.JobsCancelAllButton, view);
            ToggleJobsPanel(window);
            apply.Invoke(window, new object[] { new[] { queued with { State = JobState.Completed, ProgressPercent = 100 } } });
            Assert.Equal(Visibility.Collapsed, window.HomeRightPanel.Visibility);
            Assert.Equal("Completed", card.State);
            Assert.True(card.IsExpanded);
            window.OpenJobsPanel();
            Assert.Same(view, ((TabItem)window.HomeRightPanel.SurfaceTabs.SelectedItem).Content);
            Assert.Same(card, Assert.Single(view.CompactJobsList.Items));
        });
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

    private static CompactJobsView CompactJobs(MainWindow window) => (CompactJobsView)window.HomeRightPanel.SurfaceTabs.Items.Cast<TabItem>().Single(tab => Equals(tab.Tag, "jobs")).Content;

    private static void ToggleJobsPanel(MainWindow window)
    {
        if (window.HomeRightPanel.Visibility == Visibility.Visible)
        {
            window.RightPanelToggle.IsChecked = false;
            RaiseClick(window.RightPanelToggle);
        }
        else window.OpenJobsPanel();
    }

    private static async Task RunAsync(int seedHistoryCount, Func<MainWindow, Task> body,
        double? persistedJobsListWidth = null, double? persistedLocationsWidth = null,
        double? persistedPanelWidth = null, double? windowWidth = null)
    {
        var root = Path.Combine(Path.GetTempPath(), $"lightflow-jobs-live-{Guid.NewGuid():N}");
        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();
            var startup = await LightflowStorageCoordinator.StartAsync(root);
            Assert.True(startup.IsReady, startup.Diagnostic);
            var storage = startup.Coordinator!;
            if (persistedJobsListWidth is not null || persistedLocationsWidth is not null || persistedPanelWidth is not null)
                WorkspaceStateStore.Save(storage.Locations.WorkspaceStatePath,
                    new WorkspaceState { Layout = new() { FullJobsListPaneWidth = persistedJobsListWidth,
                        BrowserLocationsPaneWidth = persistedLocationsWidth, RightPanelWidth = persistedPanelWidth } });
            var history = new JobHistoryStore(storage.Locations.JobHistoryPath);
            for (var index = 0; index < seedHistoryCount; index++) history.Add(HistoryRecord());
            storage.SaveSettings(storage.Settings with { BackupCatalogOnClose = false });
            var window = new MainWindow(storage, startup.Status, startup.Diagnostic)
            {
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -32000,
                Top = -32000,
                ShowInTaskbar = false
            };
            if (windowWidth is { } requestedWidth) window.Width = requestedWidth;
            try
            {
                window.Show();
                Assert.True(await window.StartupCompletion.WaitAsync(TimeSpan.FromSeconds(30)), "Window startup failed.");
                Assert.Equal(seedHistoryCount, window.HistoryList.Items.Count);
                await body(window);
            }
            finally
            {
                window.Close();
                await storage.DisposeAsync();
            }
        });
        try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { }
    }

    private static async Task RealizeJobsWorkspaceAsync(MainWindow window)
    {
        window.UpdateLayout();
        window.HistoryList.UpdateLayout();
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        window.UpdateLayout();
        window.HistoryList.UpdateLayout();
    }

    private static void RaiseClick(System.Windows.Controls.Primitives.ButtonBase button) =>
        button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

    private static void RaiseSplitterDrag(GridSplitter splitter, double horizontalChange)
    {
        splitter.RaiseEvent(new System.Windows.Controls.Primitives.DragStartedEventArgs(0, 0)
            { RoutedEvent = System.Windows.Controls.Primitives.Thumb.DragStartedEvent });
        splitter.RaiseEvent(new System.Windows.Controls.Primitives.DragDeltaEventArgs(horizontalChange, 0)
            { RoutedEvent = System.Windows.Controls.Primitives.Thumb.DragDeltaEvent });
        splitter.RaiseEvent(new System.Windows.Controls.Primitives.DragCompletedEventArgs(horizontalChange, 0, false)
            { RoutedEvent = System.Windows.Controls.Primitives.Thumb.DragCompletedEvent });
    }

    private static void AssertRow2ControlHeights(MainWindow window)
    {
        const double expected = 34;
        foreach (var control in new FrameworkElement[] { window.BrowserMediaTypeGroup, window.BrowserSearchGroup,
                     window.BrowserFilterButton, window.BrowserSortGroup, window.BrowserCameraLutCombo,
                     window.BrowserCreativeLutCombo, window.BrowserExportButton })
            Assert.Equal(expected, control.ActualHeight, 1);
    }

    private static int ExpectedSelectionActionRow(MainWindow window) =>
        UsesCombinedLowerRow(window) ? 2 : 4;

    private static bool UsesCombinedLowerRow(MainWindow window) => window.BrowserCenter.ActualWidth >= 1120;

    private static void AssertContained(FrameworkElement child, FrameworkElement ancestor)
    {
        var bounds = child.TransformToAncestor(ancestor).TransformBounds(new Rect(child.RenderSize));
        Assert.True(bounds.Left >= -1, $"{child.Name} begins outside {ancestor.Name}: {bounds}");
        Assert.True(bounds.Right <= ancestor.ActualWidth + 1,
            $"{child.Name} extends beyond {ancestor.Name}: {bounds.Right:0.##} > {ancestor.ActualWidth:0.##}");
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 20000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("Timed out waiting for the Jobs workspace test window.");
            await Task.Delay(25);
        }
    }

    private static EncodingJobHistoryRecord HistoryRecord(string? sourcePath = null, string? outputPath = null)
    {
        var id = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var completed = DateTimeOffset.Now;
        var item = new JobItemDefinition(itemId, sourcePath ?? @"C:\media\source.mp4", 100,
            new MediaRange(TimeSpan.FromMinutes(1)), SourceLastWriteUtcTicks: sourcePath is null ? null : File.GetLastWriteTimeUtc(sourcePath).Ticks);
        var options = new EncodingJobOptions(@"C:\media", @"C:\output", OutputResolution.FullHd,
            RecoveryStrategy.Normal, new EncodingOptions(), null, "", false, true, false);
        var definition = new JobDefinition<EncodingJobOptions>(id, "video.encode", completed.AddMinutes(-2), options, [item]);
        var planItem = new JobPlanItem(item, [outputPath ?? @"C:\output\source.mp4"], JobPlanDisposition.Process,
            JobWorkEstimate.Determinate(JobWorkUnit.MediaDuration, 60), []);
        var plan = new JobPlan<EncodingJobOptions>(definition, completed.AddMinutes(-1), [planItem], [], JobWorkUnit.MediaDuration);
        var itemResult = new JobItemResult<EncodingItemResult>(itemId, JobState.Completed,
            planItem.OutputPaths, [], [], new EncodingItemResult(0, TimeSpan.FromMinutes(1), item.MediaRange, TimeSpan.FromMinutes(1)));
        var result = new JobResult<EncodingItemResult>(id, JobState.Completed, completed.AddMinutes(-1), completed,
            [itemResult], new JobResultSummary(1, 1, 0, 0, 0, 0), [], []);
        return new(id, "video.encode", definition.CreatedAt, result.StartedAt, completed, JobState.Completed,
            definition, plan, result);
    }
}
