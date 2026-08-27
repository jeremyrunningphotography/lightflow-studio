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
    public async Task StatusJobs_ActivatesEmptyWorkspaceAndDrawerRemainsIndependent()
    {
        await RunAsync(seedHistoryCount: 0, async window =>
        {
            Assert.Empty(window.HistoryList.Items);
            RaiseClick(window.JobsStatusButton);
            await RealizeJobsWorkspaceAsync(window);

            Assert.Equal(ShellDestinationSelection.Index(ShellDestination.Jobs), window.MainTabs.SelectedIndex);
            Assert.True(window.IsVisible);
            Assert.Equal(Visibility.Collapsed, window.JobsDrawer.Visibility);

            RaiseClick(window.FullJobsQueueGateButton);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.Equal("Resume Queue", window.FullJobsQueueGateButton.Content);
            Assert.Equal("Resume Queue", window.JobsQueueGateButton.Content);
            Assert.Contains("Queue paused", window.JobsStatusButton.Content.ToString());
            Assert.Same(window.FindResource("ShellSelectionBrush"), window.FullJobsQueueGateButton.Background);

            RaiseClick(window.JobsDrawerPullButton);
            Assert.Equal(Visibility.Visible, window.JobsDrawer.Visibility);
            RaiseClick(window.JobsQueueGateButton);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.Equal("Pause Queue", window.FullJobsQueueGateButton.Content);
            Assert.DoesNotContain("Queue paused", window.JobsStatusButton.Content.ToString());
            RaiseClick(window.JobsDrawerPullButton);
            Assert.Equal(Visibility.Collapsed, window.JobsDrawer.Visibility);
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
            Assert.Equal(new Thickness(2), focusedChrome.BorderThickness);

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
            var details = window.HistoryDetails.Text;
            var drawerWidth = window.JobsDrawerColumn.Width;
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
            Assert.Equal(details, window.HistoryDetails.Text);
            Assert.Equal(drawerWidth, window.JobsDrawerColumn.Width);
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
    public async Task DrawerConsumesWidthAndBrowserGroupsReflowWithoutLosingWideLocationsPreference()
    {
        await RunAsync(seedHistoryCount: 0, async window =>
        {
            Assert.Equal(1120, window.ActualWidth, 1);
            Assert.Equal(520, window.BrowserNavigationColumn.ActualWidth, 1);
            var playerHost = window.BrowserPlayerHost;
            Assert.Equal(0, Grid.GetRow(window.BrowserNavigationToolbar));
            Assert.Equal(2, Grid.GetRow(window.BrowserQueryToolbar));
            Assert.Equal(4, Grid.GetRow(window.BrowserSelectionActionToolbar));

            RaiseClick(window.JobsDrawerPullButton);
            await RealizeJobsWorkspaceAsync(window);
            Assert.Equal(Visibility.Visible, window.JobsDrawer.Visibility);
            Assert.Equal(380, window.JobsDrawerColumn.ActualWidth, 1);
            Assert.True(window.BrowserNavigationColumn.ActualWidth < 520);
            Assert.Equal(0, Grid.GetRow(window.BrowserNavigationToolbar));
            Assert.Equal(2, Grid.GetRow(window.BrowserQueryToolbar));
            Assert.Equal(4, Grid.GetRow(window.BrowserSelectionActionToolbar));
            AssertContained(window.BrowserCenter, window.BrowserWorkspaceRoot);
            AssertContained(window.BrowserBrowseToolbar, window.BrowserCenter);
            AssertContained(window.BrowserSelectionActionToolbar, window.BrowserCenter);
            AssertContained(window.BrowserGridHost, window.BrowserCenter);
            AssertContained(playerHost, window.BrowserCenter);

            foreach (var drawerWidth in new[] { 340d, 380d, 610d })
            {
                window.JobsDrawerColumn.Width = new GridLength(drawerWidth);
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

            RaiseClick(window.JobsDrawerPullButton);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            window.UpdateLayout();
            Assert.Equal(Visibility.Collapsed, window.JobsDrawer.Visibility);
            Assert.Equal(520, window.BrowserNavigationColumn.ActualWidth, 1);
            AssertContained(window.BrowserCenter, window.BrowserWorkspaceRoot);
            Assert.Same(playerHost, window.BrowserPlayerHost);
        }, persistedLocationsWidth: 520, persistedDrawerWidth: 380, windowWidth: 1120);
    }

    [Fact]
    public async Task LowerBrowserControls_SwitchWholeGroupsBetweenWideAndDrawerConstrainedLayouts()
    {
        await RunAsync(seedHistoryCount: 0, async window =>
        {
            Assert.Equal(0, Grid.GetRow(window.BrowserNavigationToolbar));
            Assert.Equal(2, Grid.GetRow(window.BrowserQueryToolbar));
            Assert.Equal(2, Grid.GetRow(window.BrowserSelectionActionToolbar));
            Assert.Equal(0, Grid.GetColumn(window.BrowserQueryToolbar));
            Assert.Equal(1, Grid.GetColumn(window.BrowserSelectionActionToolbar));

            RaiseClick(window.JobsDrawerPullButton);
            await RealizeJobsWorkspaceAsync(window);
            Assert.Equal(2, Grid.GetRow(window.BrowserQueryToolbar));
            Assert.Equal(4, Grid.GetRow(window.BrowserSelectionActionToolbar));
            Assert.Equal(0, Grid.GetColumn(window.BrowserSelectionActionToolbar));
            Assert.Equal(2, Grid.GetColumnSpan(window.BrowserSelectionActionToolbar));
            AssertContained(window.BrowserQueryToolbar, window.BrowserBrowseToolbar);
            AssertContained(window.BrowserSelectionActionToolbar, window.BrowserBrowseToolbar);
            AssertContained(window.BrowserGridHost, window.BrowserCenter);

            RaiseClick(window.JobsDrawerPullButton);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            window.UpdateLayout();
            Assert.Equal(2, Grid.GetRow(window.BrowserSelectionActionToolbar));
            Assert.Equal(1, Grid.GetColumn(window.BrowserSelectionActionToolbar));
        }, persistedLocationsWidth: 280, persistedDrawerWidth: 380, windowWidth: 1800);
    }

    private static async Task RunAsync(int seedHistoryCount, Func<MainWindow, Task> body,
        double? persistedJobsListWidth = null, double? persistedLocationsWidth = null,
        double? persistedDrawerWidth = null, double? windowWidth = null)
    {
        var root = Path.Combine(Path.GetTempPath(), $"lightflow-jobs-live-{Guid.NewGuid():N}");
        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();
            var startup = await LightflowStorageCoordinator.StartAsync(root);
            Assert.True(startup.IsReady, startup.Diagnostic);
            var storage = startup.Coordinator!;
            if (persistedJobsListWidth is not null || persistedLocationsWidth is not null || persistedDrawerWidth is not null)
                WorkspaceStateStore.Save(storage.Locations.WorkspaceStatePath,
                    new WorkspaceState { Layout = new() { FullJobsListPaneWidth = persistedJobsListWidth,
                        BrowserLocationsPaneWidth = persistedLocationsWidth, JobsDrawerWidth = persistedDrawerWidth } });
            var history = new JobHistoryStore(storage.Locations.JobHistoryPath);
            for (var index = 0; index < seedHistoryCount; index++) history.Add(HistoryRecord());
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
                await WaitUntilAsync(() => window.IsLoaded && window.HistoryList.ItemsSource is not null);
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

    private static EncodingJobHistoryRecord HistoryRecord()
    {
        var id = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var completed = DateTimeOffset.Now;
        var item = new JobItemDefinition(itemId, @"C:\media\source.mp4", 100,
            new MediaRange(TimeSpan.FromMinutes(1)));
        var options = new EncodingJobOptions(@"C:\media", @"C:\output", OutputResolution.FullHd,
            RecoveryStrategy.Normal, new EncodingOptions(), null, "", false, true, false);
        var definition = new JobDefinition<EncodingJobOptions>(id, "video.encode", completed.AddMinutes(-2), options, [item]);
        var planItem = new JobPlanItem(item, [@"C:\output\source.mp4"], JobPlanDisposition.Process,
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
