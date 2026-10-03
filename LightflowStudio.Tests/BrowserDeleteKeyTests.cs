using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Xunit;

namespace LightflowStudio.Tests;

[Collection("STA dispatcher tests")]
public sealed class BrowserDeleteKeyTests
{
    [Theory]
    [InlineData(false, 1, true)]
    [InlineData(true, 1, true)]
    [InlineData(false, 2, true)]
    [InlineData(true, 2, true)]
    [InlineData(false, 2, false)]
    [InlineData(true, 2, false)]
    public async Task StaticCollection_DeleteUsesConfirmedMembershipWorkflowAndPersists(
        bool details, int count, bool accept)
    {
        await WithWindow(async (window, storage, directory) =>
        {
            var root = (await storage.MediaRoots.CreateAsync("Source", directory)).Root!;
            var collection = await storage.Collections.CreateCollectionAsync("Picks");
            var other = await storage.Collections.CreateCollectionAsync("Other");
            var ids = new List<Guid>();
            for (var i = 0; i < count; i++)
            {
                var path = $"media-{i}.png";
                File.WriteAllBytes(Path.Combine(directory, path), Convert.FromBase64String(
                    "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a4f8AAAAASUVORK5CYII="));
                ids.Add((await storage.MediaAssets.CreateAsync(root.RootId, path, "image")).Asset!.Asset.AssetId);
            }
            await storage.Collections.AddMembershipsAsync(collection.CollectionId, ids);
            await storage.Collections.AddMembershipsAsync(other.CollectionId, ids);
            await InvokeTask(window, "RefreshCollectionsAsync", new object[] { null! });
            await InvokeTask(window, "LoadCollectionScopeAsync", collection.CollectionId);
            window.ApplyBrowserLayout(details ? BrowserLayoutMode.Details : BrowserLayoutMode.Grid, false);
            Assert.True(Field<BrowserGridModel>(window, "_browserGrid").SelectAll());
            var confirmations = 0;
            await window.HandleBrowserDeleteKeyAsync(window.BrowserGridRows, false, true,
                () => window.RemoveBrowserSelectionFromActiveCollectionAsync(dialog =>
                {
                    confirmations++;
                    Assert.Equal("Remove from Collection", dialog.Title);
                    Assert.Equal($"Remove {count} media item{(count == 1 ? "" : "s")} from “Picks”?", dialog.Heading);
                    Assert.Equal("Remove", dialog.Action);
                    Assert.Equal("Keep in Collection", dialog.Cancel);
                    return accept;
                }), _ => Unexpected(), (_, _) => Unexpected());
            Assert.Equal(1, confirmations);
            Assert.Equal(accept ? 0 : count, (await storage.Collections.ListMembershipsAsync(collection.CollectionId)).Count);
            Assert.Equal(count, (await storage.Collections.ListMembershipsAsync(other.CollectionId)).Count);
            foreach (var id in ids)
            {
                var asset = await storage.MediaAssets.GetAsync(id);
                Assert.Equal(id, asset!.Asset.AssetId);
                Assert.True(File.Exists(asset.PhysicalPath));
                Assert.Equal(Convert.FromBase64String(
                    "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a4f8AAAAASUVORK5CYII="),
                    File.ReadAllBytes(asset.PhysicalPath));
            }
            // Reopen the actual Catalog after the window/storage are disposed by the fixture.
            return async reopened => Assert.Equal(accept ? 0 : count,
                (await reopened.Collections.ListMembershipsAsync(collection.CollectionId)).Count);
        });
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task FolderScope_PreservesMediaDeleteAndPermanentFlag(bool details, bool permanent)
    {
        await WithWindow(async (window, storage, directory) =>
        {
            Field<BrowserScopeSelection>(window, "_browserScopeSelection").ActivateFolder();
            window.ApplyBrowserLayout(details ? BrowserLayoutMode.Details : BrowserLayoutMode.Grid, false);
            var calls = 0;
            await window.HandleBrowserDeleteKeyAsync(window.BrowserGridRows, false, permanent, Unexpected,
                value => { Assert.Equal(permanent, value); calls++; return Task.CompletedTask; }, (_, _) => Unexpected());
            Assert.Equal(1, calls);
            return null;
        });
    }

    [Fact]
    public async Task SmartCollection_AndPlayerFromCollection_DoNotFallBackToFileDeletion()
    {
        await WithWindow(async (window, storage, directory) =>
        {
            var root = (await storage.MediaRoots.CreateAsync("Source", directory)).Root!;
            var source = await storage.Collections.CreateCollectionAsync("Source Collection");
            var path = Path.Combine(directory, "member.png");
            File.WriteAllBytes(path, Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a4f8AAAAASUVORK5CYII="));
            var asset = (await storage.MediaAssets.CreateAsync(root.RootId, "member.png", "image")).Asset!.Asset;
            await storage.Collections.AddMembershipsAsync(source.CollectionId, [asset.AssetId]);
            var smart = await storage.SmartCollections.SaveSmartCollectionAsync("Computed", null,
                new(SmartCollectionSourceKind.Collection, CollectionId: source.CollectionId), new());
            await InvokeTask(window, "RefreshCollectionsAsync", new object[] { null! });
            await InvokeTask(window, "LoadCollectionScopeAsync", smart.SmartCollectionId);
            var grid = Field<BrowserGridModel>(window, "_browserGrid");
            Assert.Single(grid.Tiles);
            Assert.True(grid.SelectAll());
            foreach (var layout in new[] { BrowserLayoutMode.Grid, BrowserLayoutMode.Details })
            {
                window.ApplyBrowserLayout(layout, false);
                await window.HandleBrowserDeleteKeyAsync(window.BrowserGridRows, false, false, Unexpected,
                    _ => Unexpected(), (_, _) => Unexpected());
            }
            Assert.Empty(await storage.Collections.ListMembershipsAsync(smart.SmartCollectionId));
            Assert.Equal(smart.Source, (await storage.SmartCollections.GetSmartCollectionAsync(smart.SmartCollectionId))!.Source);
            await InvokeTask(window, "LoadCollectionScopeAsync", source.CollectionId);
            await InvokeTask(window, "OpenBrowserPlayerViewerAsync", Assert.Single(grid.Tiles));
            Assert.Equal(BrowserPresentationMode.PlayerViewer, Field<BrowserPresentationMode>(window, "_browserPresentation"));
            await window.HandleBrowserDeleteKeyAsync(window.BrowserGridRows, false, false, Unexpected,
                _ => Unexpected(), (_, _) => Unexpected());
            return null;
        });
    }

    [Fact]
    public async Task EditorsOwnDeleteInFolderAndCollectionScopes()
    {
        await WithWindow(async (window, storage, directory) =>
        {
            foreach (var collection in new[] { false, true })
            {
                var scope = Field<BrowserScopeSelection>(window, "_browserScopeSelection");
                if (collection) scope.ActivateCollection(); else scope.ActivateFolder();
                foreach (var owner in new DependencyObject[] { new TextBox(), new RichTextBox(), new ComboBox { IsEditable = true } })
                    await window.HandleBrowserDeleteKeyAsync(owner, false, false, Unexpected,
                        _ => Unexpected(), (_, _) => Unexpected());
            }
            return null;
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FocusedFolderTarget_RetainsExistingDeletionPolicy(bool permanent)
    {
        await WithWindow(async (window, storage, directory) =>
        {
            var tree = Field<BrowserTreeModel>(window, "_browserTree");
            var target = new BrowserTreeNode("Target", directory);
            tree.Roots.Add(target);
            tree.RequestSelection(target);
            var calls = 0;
            await window.HandleBrowserDeleteKeyAsync(window.BrowserFolderTree, true, permanent, Unexpected,
                _ => Unexpected(), (sources, value) =>
                {
                    Assert.Equal(permanent, value);
                    Assert.Equal(directory, Assert.Single(sources).Path);
                    Assert.True(sources[0].IsDirectory);
                    calls++;
                    return Task.CompletedTask;
                });
            Assert.Equal(1, calls);
            return null;
        });
    }

    [Fact]
    public async Task EmptyCollectionSelection_DoesNotConfirmOrFallBack()
    {
        await WithWindow(async (window, storage, directory) =>
        {
            var collection = await storage.Collections.CreateCollectionAsync("Empty");
            await InvokeTask(window, "RefreshCollectionsAsync", new object[] { null! });
            await InvokeTask(window, "LoadCollectionScopeAsync", collection.CollectionId);
            await window.HandleBrowserDeleteKeyAsync(window.BrowserGridRows, false, false,
                () => window.RemoveBrowserSelectionFromActiveCollectionAsync(_ => throw new Xunit.Sdk.XunitException("No selection to confirm")),
                _ => Unexpected(), (_, _) => Unexpected());
            return null;
        });
    }

    [Fact]
    public async Task ExplicitCollectionFileDelete_RemainsAvailableAndUsesFileWorkflow()
    {
        await WithWindow(async (window, storage, directory) =>
        {
            var root = (await storage.MediaRoots.CreateAsync("Source", directory)).Root!;
            var path = Path.Combine(directory, "explicit.png");
            File.WriteAllText(path, "source");
            var asset = (await storage.MediaAssets.CreateAsync(root.RootId, "explicit.png", "image")).Asset!.Asset;
            var collection = await storage.Collections.CreateCollectionAsync("Picks");
            await storage.Collections.AddMembershipsAsync(collection.CollectionId, [asset.AssetId]);
            await InvokeTask(window, "RefreshCollectionsAsync", new object[] { null! });
            await InvokeTask(window, "LoadCollectionScopeAsync", collection.CollectionId);
            Field<BrowserGridModel>(window, "_browserGrid").SelectAll();
            var menu = Assert.Single(((ContextMenu)window.FindResource("BrowserAssetContextMenu")).Items.OfType<MenuItem>(),
                item => Equals(item.Header, "Delete"));
            Assert.True(menu.IsEnabled);
            Assert.Equal("Delete", menu.InputGestureText);
            var sources = await (Task<IReadOnlyList<FileOperationSource>>)Invoke(window, "SelectedFileOperationSourcesAsync")!;
            Assert.Equal(asset.AssetId, Assert.Single(sources).AssetId);
            var calls = 0;
            await BrowserDeleteOperation.RunAsync(sources, false, _ => true,
                dialog => { Assert.Equal("Move to Recycle Bin", dialog.Title); return true; },
                (kind, captured) =>
                {
                    Assert.Equal(FileOperationKind.Recycle, kind);
                    Assert.Equal(path, Assert.Single(captured).Path);
                    calls++;
                    return Task.CompletedTask;
                });
            Assert.Equal(1, calls);
            Assert.True(File.Exists(path));
            Assert.Single(await storage.Collections.ListMembershipsAsync(collection.CollectionId));
            return null;
        });
    }
    private static Task Unexpected() => throw new Xunit.Sdk.XunitException("Unexpected mutation workflow invoked.");
    private static object? Invoke(object target, string name, params object[] args) => target.GetType()
        .GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, args);
    private static Task InvokeTask(object target, string name, params object[] args) => (Task)Invoke(target, name, args)!;
    private static T Field<T>(object target, string name) => (T)target.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;

    private static async Task WithWindow(Func<MainWindow, LightflowStorageCoordinator, string,
        Task<Func<LightflowStorageCoordinator, Task>?>> action)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "delete-key-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            await StaDispatcher.RunAsync(async () =>
            {
                TestWpfApplication.EnsureLoaded();
                var appRoot = Path.Combine(directory, "app");
                var startup = await LightflowStorageCoordinator.StartAsync(appRoot);
                Assert.True(startup.IsReady, startup.Diagnostic);
                Func<LightflowStorageCoordinator, Task>? restart;
                await using (var storage = startup.Coordinator!)
                {
                    await storage.MediaMonitoring!.DisposeAsync();
                    storage.SaveSettings(storage.Settings with { BackupCatalogOnClose = false });
                    var window = new MainWindow(storage, startup.Status, startup.Diagnostic) { IsHitTestVisible = false };
                    try { restart = await action(window, storage, directory); }
                    finally { window.Close(); }
                }
                if (restart is not null)
                {
                    await using var reopened = (await LightflowStorageCoordinator.StartAsync(appRoot)).Coordinator!;
                    await restart(reopened);
                }
            });
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
