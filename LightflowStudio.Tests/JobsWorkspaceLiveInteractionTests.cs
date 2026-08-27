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

            Assert.Equal(ShellWorkspaceSelection.Index(ShellWorkspace.History), window.MainTabs.SelectedIndex);
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

            window.MainTabs.SelectedIndex = ShellWorkspaceSelection.Index(ShellWorkspace.Browser);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            RaiseClick(window.JobsStatusButton);
            await RealizeJobsWorkspaceAsync(window);

            Assert.Equal(ShellWorkspaceSelection.Index(ShellWorkspace.History), window.MainTabs.SelectedIndex);
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
            Assert.Equal(ShellWorkspaceSelection.Index(ShellWorkspace.Browser), window.MainTabs.SelectedIndex);
            RaiseClick(window.JobsStatusButton);
            await RealizeJobsWorkspaceAsync(window);
            Assert.Equal(2, window.HistoryList.Items.Count);
        });
    }

    private static async Task RunAsync(int seedHistoryCount, Func<MainWindow, Task> body)
    {
        var root = Path.Combine(Path.GetTempPath(), $"lightflow-jobs-live-{Guid.NewGuid():N}");
        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();
            var startup = await LightflowStorageCoordinator.StartAsync(root);
            Assert.True(startup.IsReady, startup.Diagnostic);
            var storage = startup.Coordinator!;
            var history = new JobHistoryStore(storage.Locations.JobHistoryPath);
            for (var index = 0; index < seedHistoryCount; index++) history.Add(HistoryRecord());
            var window = new MainWindow(storage, startup.Status, startup.Diagnostic)
            {
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -32000,
                Top = -32000,
                ShowInTaskbar = false
            };
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
