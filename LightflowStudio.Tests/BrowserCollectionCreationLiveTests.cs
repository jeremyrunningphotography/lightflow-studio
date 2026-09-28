using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Xunit;

namespace LightflowStudio.Tests;

[Collection("STA dispatcher tests")]
public sealed class BrowserCollectionCreationLiveTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CreationPublishesHierarchyBeforeNavigationAndSurvivesRestart(bool nested)
    {
        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();
            var directory = Path.Combine(AppContext.BaseDirectory, "collection-creation-tests", Guid.NewGuid().ToString("N"));
            var startup = await LightflowStorageCoordinator.StartAsync(directory);
            var storage = startup.Coordinator!;
            await storage.MediaMonitoring!.DisposeAsync();
            storage.SaveSettings(storage.Settings with { BackupCatalogOnClose = false });
            var outer = await storage.Collections.CreateSetAsync("Outer");
            var inner = await storage.Collections.CreateSetAsync("Inner", outer.CollectionSetId);
            var parent = nested ? inner.CollectionSetId : (Guid?)null;
            var window = new MainWindow(storage, startup.Status, startup.Diagnostic)
            {
                Left = -32000, Top = -32000, ShowInTaskbar = false, Width = 1440, Height = 900,
                WindowStartupLocation = WindowStartupLocation.Manual
            };
            Guid createdId;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            try
            {
                window.Show();
                Assert.True(await window.StartupCompletion.WaitAsync(TimeSpan.FromSeconds(30)));
                var tree = (BrowserCollectionTreeModel)typeof(MainWindow).GetField("_browserCollectionTree", flags)!.GetValue(window)!;
                Assert.All(BrowserCollectionTreeModel.Flatten(tree.Roots).Where(node => node.IsSet), node => Assert.False(node.IsExpanded));
                var operation = (Task)typeof(MainWindow).GetMethod("CreateBrowserCollectionAsync", flags)!
                    .Invoke(window, ["Created collection", parent])!;
                await operation.WaitAsync(TimeSpan.FromSeconds(10));
                window.UpdateLayout();

                var durable = Assert.Single(await storage.Collections.ListCollectionsAsync(parent));
                createdId = durable.CollectionId;
                var node = Assert.Single(BrowserCollectionTreeModel.Flatten(tree.Roots).Where(item => item.Id == createdId));
                Assert.Equal(parent, node.ParentSetId);
                Assert.True(node.IsSelected);
                Assert.Same(node, tree.SelectedNode);
                var scope = (BrowserCollectionScope)typeof(MainWindow).GetField("_activeCollectionScope", flags)!.GetValue(window)!;
                Assert.Equal(createdId, scope.Collection.CollectionId);
                Assert.Equal(nested ? "Collections / Outer / Inner / Created collection" : "Collections / Created collection", window.BrowserCurrentPath.Text);
                Assert.Equal(Visibility.Visible, window.BrowserGridRows.Visibility);
                Assert.Equal(Visibility.Visible, window.BrowserEmptyState.Visibility);
                Assert.DoesNotContain(Application.Current.Windows.Cast<Window>(), item => item != window && item.IsVisible);
                if (nested)
                    Assert.All(BrowserCollectionTreeModel.Flatten(tree.Roots).Where(item => item.IsSet), item => Assert.True(item.IsExpanded));
                var containers = (IEnumerable<TreeViewItem>)typeof(MainWindow).GetMethod("CollectionTreeItems", BindingFlags.Static | BindingFlags.NonPublic)!
                    .Invoke(window, [window.BrowserCollectionTree])!;
                Assert.Contains(containers, item => ReferenceEquals(item.DataContext, node) && item.IsVisible);
            }
            finally
            {
                window.Close();
                await storage.DisposeAsync();
            }

            var reopened = (await LightflowStorageCoordinator.StartAsync(directory)).Coordinator!;
            try
            {
                await reopened.MediaMonitoring!.DisposeAsync();
                var persisted = Assert.Single(await reopened.Collections.ListCollectionsAsync(parent));
                Assert.Equal(createdId, persisted.CollectionId);
                Assert.Equal("Created collection", persisted.Name);
            }
            finally { await reopened.DisposeAsync(); }
        });
    }
}
