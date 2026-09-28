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
                var node = Assert.Single(BrowserCollectionTreeModel.Flatten(tree.Roots), item => item.Id == createdId);
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
                // Exercise both navigation callers with the newly published durable hierarchy.
                var media = Directory.CreateDirectory(Path.Combine(directory, "media")).FullName;
                var root = (await storage.MediaRoots.CreateAsync("Fixture", media)).Root!;
                var members = new List<Guid>();
                foreach (var name in new[] { "z-last.jpg", "a-first.jpg", "m-middle.jpg" })
                {
                    await File.WriteAllTextAsync(Path.Combine(media, name), "fixture");
                    var asset = (await storage.MediaAssets.CreateAsync(root.RootId, name, "image")).Asset!.Asset;
                    members.Add(asset.AssetId);
                    await storage.Collections.AddMembershipAsync(createdId, asset.AssetId);
                }
                File.Delete(Path.Combine(media, "a-first.jpg"));
                await storage.MediaAssets.MarkMissingAsync([members[1]]);
                var smart = await storage.SmartCollections.SaveSmartCollectionAsync("Same source", null,
                    new(SmartCollectionSourceKind.Collection, CollectionId: createdId), new());
                await (Task)typeof(MainWindow).GetMethod("RefreshCollectionsAsync", flags)!.Invoke(window, new object?[] { createdId })!;
                async Task Navigate(Guid id) => await ((Task)typeof(MainWindow).GetMethod("LoadCollectionScopeAsync", flags)!
                    .Invoke(window, [id])!).WaitAsync(TimeSpan.FromSeconds(10));
                await Navigate(createdId);
                var grid = (BrowserGridModel)typeof(MainWindow).GetField("_browserGrid", flags)!.GetValue(window)!;
                Assert.Equal(members, grid.Tiles.Select(tile => tile.AssetId!.Value));
                Assert.False(grid.Tiles.Single(tile => tile.AssetId == members[1]).IsAvailable);
                await Navigate(smart.SmartCollectionId);
                scope = (BrowserCollectionScope)typeof(MainWindow).GetField("_activeCollectionScope", flags)!.GetValue(window)!;
                Assert.Equal(members, scope.Entries.Select(entry => entry.AssetId!.Value));
                Assert.Equal(3, grid.TotalCount);
                grid.SetQuery(BrowserQuery.Default with { SearchText = "first" });
                Assert.Equal(1, grid.VisibleCount);
                grid.SetQuery(BrowserQuery.Default with { SortMode = BrowserSortMode.Name });
                Assert.Equal([members[1], members[2], members[0]], grid.Tiles.Select(tile => tile.AssetId!.Value));
                await Navigate(createdId);
                Assert.Equal(3, (await storage.Collections.ListMembershipsAsync(createdId)).Count);
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
                var persisted = Assert.Single(await reopened.Collections.ListCollectionsAsync(parent), item => !item.IsSmartCollection);
                Assert.Equal(createdId, persisted.CollectionId);
                Assert.Equal("Created collection", persisted.Name);
                Assert.Equal(3, (await reopened.Collections.ListMembershipsAsync(createdId)).Count);
            }
            finally { await reopened.DisposeAsync(); }
        });
    }
}
