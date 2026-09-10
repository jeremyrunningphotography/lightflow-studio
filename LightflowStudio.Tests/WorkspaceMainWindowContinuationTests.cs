using System.Reflection;
using System.Windows;
using Xunit;

namespace LightflowStudio.Tests;

[Collection("STA dispatcher tests")]
public sealed class WorkspaceMainWindowContinuationTests
{
    [Theory]
    [InlineData("browser")]
    [InlineData("relocated")]
    [InlineData("deleted")]
    [InlineData("offline")]
    public async Task PlayerIdentity_UsesCurrentCatalogMappingAndFallsBackWithoutSubstitution(string scenario)
    {
        await WithStorage(async (directory, storage, startup) =>
        {
            var media = Path.Combine(directory, "media");
            Directory.CreateDirectory(media);
            WriteImage(Path.Combine(media, "old.png"));
            var root = (await storage.MediaRoots.CreateAsync("Library", media)).Root!;
            var asset = (await storage.MediaAssets.CreateAsync(root.RootId, "old.png", "image")).Asset!.Asset;
            var saved = new WorkspacePlayerState
            {
                Asset = new(root.RootId, "old.png", asset.RelativePathKey, "old.png", MediaPresentationKind.Image, asset.AssetId),
                PixelZoom = 2
            };
            var workspace = new WorkspaceStateService(storage.Locations.WorkspaceStatePath);
            workspace.SetBrowserLocation(root.RootId, "", media);
            workspace.SetContinuation(new() { Player = scenario == "browser" ? null : saved });
            workspace.Save();
            if (scenario == "relocated")
            {
                File.Move(Path.Combine(media, "old.png"), Path.Combine(media, "new.png"));
                Assert.True((await storage.MediaAssets.RelocateAsync(asset.AssetId, root.RootId, "new.png")).Succeeded);
                var remapped = Path.Combine(directory, "remapped");
                Directory.Move(media, remapped);
                Assert.True((await storage.MediaRoots.RemapAsync(root.RootId, remapped)).Succeeded);
            }
            if (scenario == "deleted") File.Delete(Path.Combine(media, "old.png"));
            if (scenario == "offline") Directory.Move(media, Path.Combine(directory, "disconnected"));
            var window = new MainWindow(storage, startup.Status, startup.Diagnostic);
            try
            {
                await InvokeTask(window, "RefreshBrowserStorageAsync");
                await InvokeTask(window, "RestoreWorkspaceContinuationAsync");
                var player = Field<PlayerViewerHost?>(window, "_playerViewerHost");
                if (scenario is "browser" or "deleted")
                {
                    Assert.Equal(BrowserPresentationMode.Grid, Field<BrowserPresentationMode>(window, "_browserPresentation"));
                    Assert.Null(player?.CurrentAsset);
                }
                else
                {
                    Assert.Equal(BrowserPresentationMode.PlayerViewer, Field<BrowserPresentationMode>(window, "_browserPresentation"));
                    Assert.Equal(asset.AssetId, player!.CurrentAsset!.AssetId);
                    Assert.Equal(scenario == "relocated" ? "new.png" : "old.png", player.CurrentAsset.RelativePath);
                    Assert.False(player.IsFullscreen);
                    if (scenario == "relocated") Assert.NotNull(player.ImageSurface.Source);
                    else Assert.Null(player.ImageSurface.Source);
                }
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public async Task Browser_RestoresRecursiveQuerySelectionAndLayoutThroughNormalAuthorities()
    {
        await WithStorage(async (directory, storage, startup) =>
        {
            var media = Path.Combine(directory, "media");
            Directory.CreateDirectory(Path.Combine(media, "child"));
            var root = (await storage.MediaRoots.CreateAsync("Library", media)).Root!;
            var ids = new List<Guid>();
            foreach (var path in new[] { "keep-a.png", "child/keep-b.png", "hidden.png" })
            {
                WriteImage(Path.Combine(media, path));
                ids.Add((await storage.MediaAssets.CreateAsync(root.RootId, path, "image")).Asset!.Asset.AssetId);
            }
            await storage.BrowserRecursiveRoots.EnableAsync(root.RootId, "");
            var workspace = new WorkspaceStateService(storage.Locations.WorkspaceStatePath);
            workspace.SetBrowserLocation(root.RootId, "", media);
            workspace.SetBrowserViewMode(BrowserViewMode.Hybrid);
            workspace.SetBrowserThumbnailSizeLevel(4);
            workspace.SetRightPanel(410, true, "inspector");
            workspace.SetContinuation(new()
            {
                Query = new() { SearchText = "keep", SortDescending = true,
                    Filters = [BrowserFilterPredicate.ForMediaType(MediaTypeCategory.StillImage)] },
                Grid = new() { SelectedAssetIds = [.. ids, Guid.NewGuid()], AnchorAssetId = ids[1] }
            });
            workspace.Save();
            var window = new MainWindow(storage, startup.Status, startup.Diagnostic);
            try
            {
                await InvokeTask(window, "RefreshBrowserStorageAsync");
                await InvokeTask(window, "RestoreWorkspaceContinuationAsync");
                var grid = Field<BrowserGridModel>(window, "_browserGrid");
                Assert.True(window.BrowserIncludeSubfoldersButton.IsChecked);
                Assert.Equal(new[] { ids[1], ids[0] }, grid.SelectedAssetIdsInBrowserOrder);
                Assert.Equal(ids[1], grid.SelectionAnchorAssetId);
                Assert.Equal(3, grid.TotalCount);
                Assert.Equal(2, grid.VisibleCount);
                Assert.Equal(BrowserViewMode.Hybrid, grid.ViewMode);
                Assert.Equal(4, (int)Field<BrowserThumbnailSize>(window, "_browserThumbnailSize"));
                Assert.True(Field<bool>(window, "_rightPanelOpen"));
                Assert.Equal(410, Field<double>(window, "_rightPanelPreferredWidth"));
                Assert.Null(Field<PlayerViewerHost?>(window, "_playerViewerHost"));
            }
            finally { window.Close(); }
        });
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Filmstrip_RestoresOrderedFolderOrCollectionReviewAndOpeningSelection(bool collectionScope, bool subset)
    {
        await WithStorage(async (directory, storage, startup) =>
        {
            var media = Directory.CreateDirectory(Path.Combine(directory, "media")).FullName;
            var root = (await storage.MediaRoots.CreateAsync("Library", media)).Root!;
            var assets = new List<PlayerViewerAsset>();
            foreach (var name in new[] { "keep-a.png", "keep-b.png", "keep-c.png", "hidden.png" })
            {
                WriteImage(Path.Combine(media, name));
                var asset = (await storage.MediaAssets.CreateAsync(root.RootId, name, "image")).Asset!.Asset;
                assets.Add(new(root.RootId, name, asset.RelativePathKey, name, MediaPresentationKind.Image, asset.AssetId));
            }
            var workspace = new WorkspaceStateService(storage.Locations.WorkspaceStatePath);
            workspace.SetBrowserLocation(root.RootId, "", media);
            if (collectionScope)
            {
                var collection = await storage.Collections.CreateCollectionAsync("Review");
                await storage.Collections.AddMembershipsAsync(collection.CollectionId, assets.Select(a => a.AssetId!.Value).Reverse().ToArray());
                workspace.SetBrowserCollectionState(collection.CollectionId, new HashSet<Guid>());
            }
            workspace.SetPlayerFilmstripVisible(false);
            var selected = subset ? new[] { assets[0].AssetId!.Value, assets[2].AssetId!.Value } : new[] { assets[0].AssetId!.Value };
            workspace.SetContinuation(new()
            {
                Query = new() { SearchText = "keep", SortDescending = true },
                Grid = new() { SelectedAssetIds = selected, AnchorAssetId = selected[0] },
                Player = new() { Asset = assets[0] }
            });
            workspace.Save();
            var window = new MainWindow(storage, startup.Status, startup.Diagnostic);
            try
            {
                await InvokeTask(window, "RefreshBrowserStorageAsync");
                await InvokeTask(window, "RestoreWorkspaceContinuationAsync");
                var grid = Field<BrowserGridModel>(window, "_browserGrid");
                var player = Field<PlayerViewerHost>(window, "_playerViewerHost");
                Assert.False(player.FilmstripVisible);
                Assert.Equal(Visibility.Collapsed, player.FilmstripChrome.Visibility);
                Assert.Equal(subset, player.ReviewSet!.IsSelectionSubset);
                Assert.Equal(grid.Tiles.Where(t => !subset || t.IsSelected).Select(t => t.AssetId),
                    player.ReviewSet.Items.Select(item => item.Asset.AssetId));
                Assert.Equal(subset ? 2 : 3, player.ReviewSet.Items.Count);
                var destination = player.ReviewSet.Items[0].Asset.AssetId!.Value;
                await player.SelectReviewAssetAsync(destination);
                Assert.Equal(destination, player.CurrentAsset!.AssetId);
                Assert.NotNull(player.ImageSurface.Source);
                // Exercise the real Back handler and its async teardown.
                player.BackButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                await window.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                Assert.Equal(BrowserPresentationMode.Grid, Field<BrowserPresentationMode>(window, "_browserPresentation"));
                Assert.Equal(subset ? selected.Order() : new[] { destination }, grid.SelectedAssetIdsInBrowserOrder.Order());
                Assert.Equal("keep", grid.Query.SearchText);
                Assert.True(grid.Query.SortDescending);
                Assert.Null(player.ReviewSet);
            }
            finally { window.Close(); }
        });
    }

    private static void WriteImage(string path) => File.WriteAllBytes(path,
        Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a4f8AAAAASUVORK5CYII="));

    private static async Task WithStorage(Func<string, LightflowStorageCoordinator, StorageStartupResult, Task> action)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"lightflow-workspace-final-{Guid.NewGuid():N}");
        try
        {
            await StaDispatcher.RunAsync(async () =>
            {
                TestWpfApplication.EnsureLoaded();
                var startup = await LightflowStorageCoordinator.StartAsync(Path.Combine(directory, "app"));
                Assert.True(startup.IsReady, startup.Diagnostic);
                await using var storage = startup.Coordinator!;
                await action(directory, storage, startup);
            });
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActualStartupBoundary_HydratesQueryAndIndependentBranchesOrYieldsToInput(bool cancel)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"lightflow-workspace-247-{Guid.NewGuid():N}");
        try
        {
            await StaDispatcher.RunAsync(async () =>
            {
                TestWpfApplication.EnsureLoaded();
                var media = Path.Combine(directory, "media");
                Directory.CreateDirectory(Path.Combine(media, "active"));
                Directory.CreateDirectory(Path.Combine(media, "archive", "old"));
                var startup = await LightflowStorageCoordinator.StartAsync(Path.Combine(directory, "app"));
                Assert.True(startup.IsReady, startup.Diagnostic);
                await using var storage = startup.Coordinator!;
                var root = (await storage.MediaRoots.CreateAsync("Library", media)).Root!;
                var workspace = new WorkspaceStateService(storage.Locations.WorkspaceStatePath);
                workspace.SetBrowserLocation(root.RootId, "active", Path.Combine(media, "active"));
                workspace.SetContinuation(new()
                {
                    Query = new() { SearchText = "remember me", SortMode = BrowserSortMode.ModifiedDate, SortDescending = true },
                    QueryLocked = true,
                    ExpandedFolders = [new() { RootId = root.RootId }, new() { RootId = root.RootId, RelativeFolder = "archive" }]
                });
                workspace.Save();
                var window = new MainWindow(storage, startup.Status, startup.Diagnostic);
                try
                {
                    // Call the same startup boundary without opening a desktop window or replaying mouse actions.
                    Assert.Equal("remember me", window.BrowserSearchBox.Text);
                    Assert.True(window.BrowserQueryLockButton.IsChecked);
                    await InvokeTask(window, "RefreshBrowserStorageAsync");
                    if (cancel)
                    {
                        Invoke(window, "WorkspaceUserInteraction");
                        window.BrowserSearchBox.Text = "new work";
                    }
                    await InvokeTask(window, "RestoreWorkspaceContinuationAsync");
                    if (cancel)
                    {
                        Assert.Equal("new work", window.BrowserSearchBox.Text);
                        Assert.Null(Field<BrowserTreeModel>(window, "_browserTree").SelectedNode);
                    }
                    else
                    {
                        var tree = Field<BrowserTreeModel>(window, "_browserTree");
                        Assert.Equal(Path.Combine(media, "active"), tree.SelectedNode!.AbsolutePath);
                        Assert.True(tree.FindByPath(Path.Combine(media, "archive"))!.IsExpanded);
                        Assert.NotNull(tree.FindByPath(Path.Combine(media, "archive", "old")));
                        Assert.False(tree.SelectedNode.IsExpanded);
                        Assert.Equal("remember me", Field<BrowserGridModel>(window, "_browserGrid").Query.SearchText);
                        // Layout can still be pending after the startup task completes (notably after Player).
                        SetField(window, "_pendingWorkspaceGrid", new WorkspaceGridState { VerticalOffset = 400 });
                        Invoke(window, "WorkspaceUserInteraction");
                        Assert.Null(Field<WorkspaceGridState?>(window, "_pendingWorkspaceGrid"));
                    }
                }
                finally { window.Close(); }
            });
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    private static object? Invoke(object target, string method) => target.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(target, null);
    private static Task InvokeTask(object target, string method) => (Task)Invoke(target, method)!;
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(target)!;
    private static void SetField(object target, string name, object? value) => target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(target, value);
}
