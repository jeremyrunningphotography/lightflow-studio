using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Lightflow.Actions;
using LightflowStudio;
using Xunit;

namespace LightflowStudio.Tests;

[Collection("STA dispatcher tests")]
public sealed class BrowserActionIntegrationTests
{
    private static readonly ActionInputSource Controller = new("direct-controller", ActionInputKind.Controller);
    private static Task<ActionResult> Invoke(MainWindow window, string id, ActionArguments args) =>
        window.BrowserSemanticActions.InvokeAsync(new(id, args, Controller, Guid.NewGuid(), window.BrowserSemanticContext.Target));
    [Theory]
    [InlineData(BrowserScopeKind.Folder, false)]
    [InlineData(BrowserScopeKind.Folder, true)]
    [InlineData(BrowserScopeKind.Collection, false)]
    [InlineData(BrowserScopeKind.Collection, true)]
    [InlineData(BrowserScopeKind.SmartCollection, false)]
    [InlineData(BrowserScopeKind.SmartCollection, true)]
    public Task DirectController_ClassificationPersistsMultiSelectionAndSmartMembership(BrowserScopeKind kind, bool details) =>
        WithWindow(async (window, storage, directory) => {
            var (root, ids, collection) = await Seed(storage, directory);
            await Load(window, storage, directory, root, collection, kind);
            window.ApplyBrowserLayout(details ? BrowserLayoutMode.Details : BrowserLayoutMode.Grid, false);
            var grid = Field<BrowserGridModel>(window, "_browserGrid");
            Assert.Equal(kind, window.BrowserSemanticContext.Target!.Scope.Kind);
            grid.SelectSingle(0);
            await Invoke(window, BrowserActions.NavigateSelection, new NavigateSelectionArguments(BrowserMovement.Next));
            Assert.True(grid.Tiles[1].IsSelected);
            await Invoke(window, BrowserActions.NavigateSelection, new NavigateSelectionArguments(BrowserMovement.Previous));
            Assert.True(grid.Tiles[0].IsSelected);
            var staleOpen = new BrowserActionInvocation(BrowserActions.OpenCurrent, NoActionArguments.Instance,
                Controller, Guid.NewGuid(), window.BrowserSemanticContext.Target);
            grid.SetQuery(new() { SortMode = BrowserSortMode.Name, SortDescending = true });
            Assert.Equal(ActionOutcome.Superseded, (await window.BrowserSemanticActions.InvokeAsync(staleOpen)).Outcome);
            grid.SelectAll();
            for (var rating = 0; rating <= 5; rating++) {
                Assert.Equal(ActionOutcome.Completed, (await Invoke(window, BrowserActions.SetRating, new SetRatingArguments(rating))).Outcome);
                Assert.All((await storage.AssetClassifications.GetAsync(ids)).Values, value => Assert.Equal(rating, value.Rating));
                if (kind == BrowserScopeKind.SmartCollection) grid.SelectAll();
            }
            foreach (var flag in Enum.GetValues<ClassificationFlag>()) {
                await Invoke(window, BrowserActions.SetFlag, new SetFlagArguments(flag));
                Assert.All((await storage.AssetClassifications.GetAsync(ids)).Values, value => Assert.Equal((AssetFlag)flag, value.Flag));
            }
            for (var i = 0; i < 4; i++) await Invoke(window, BrowserActions.StepFlag, new StepFlagArguments(TraversalDirection.Previous));
            Assert.All((await storage.AssetClassifications.GetAsync(ids)).Values, value => Assert.Equal(AssetFlag.Rejected, value.Flag));
            for (var i = 0; i < 4; i++) await Invoke(window, BrowserActions.StepFlag, new StepFlagArguments(TraversalDirection.Next));
            Assert.All((await storage.AssetClassifications.GetAsync(ids)).Values, value => Assert.Equal(AssetFlag.Picked, value.Flag));
            foreach (var label in Enum.GetValues<ClassificationColorLabel>()) {
                await Invoke(window, BrowserActions.SetColorLabel, new SetColorLabelArguments(label));
                Assert.All((await storage.AssetClassifications.GetAsync(ids)).Values, value => Assert.Equal((AssetColorLabel)label, value.ColorLabel));
            }
            await Invoke(window, BrowserActions.SetColorLabel, new SetColorLabelArguments(null));
            Assert.All((await storage.AssetClassifications.GetAsync(ids)).Values, value => { Assert.Null(value.ColorLabel); Assert.Equal(["keep"], value.Keywords); });
            Assert.Equal(2, (await storage.Collections.ListMembershipsAsync(collection)).Count);
            if (kind == BrowserScopeKind.SmartCollection) {
                // The seeded defining rule is rating <= 5. Lower its threshold through a new saved definition,
                // then remove derived results with an underlying rating mutation, never membership mutation.
                var smart = await storage.SmartCollections.SaveSmartCollectionAsync("High", null,
                    new(SmartCollectionSourceKind.Collection, CollectionId: collection),
                    new() { Filters = [BrowserFilterPredicate.ForRating(BrowserNumberComparison.GreaterThanOrEqual, 4)] });
                await Method(window, "RefreshCollectionsAsync", new object[] { null! });
                await Method(window, "RefreshCollectionsAsync", new object[] { null! });
                await Method(window, "LoadSmartCollectionScopeAsync", smart, CancellationToken.None, false);
                await Hydrate(window, ids); grid.SelectAll();
                await Invoke(window, BrowserActions.SetRating, new SetRatingArguments(0));
                Assert.Empty(grid.Tiles);
                Assert.Equal(2, (await storage.Collections.ListMembershipsAsync(collection)).Count);
            }
            return async reopened => {
                var values = await reopened.AssetClassifications.GetAsync(ids);
                Assert.All(values.Values, value => { Assert.Equal(kind == BrowserScopeKind.SmartCollection ? 0 : 5, value.Rating); Assert.Equal(AssetFlag.Picked, value.Flag); Assert.Null(value.ColorLabel); Assert.Equal(["keep"], value.Keywords); });
            };
        });
    [Fact]
    public Task PendingMutation_CapturesSelectionAndCannotPublishReplacementScope() => WithWindow(async (window, storage, directory) => {
        var (root, ids, collection) = await Seed(storage, directory);
        await Load(window, storage, directory, root, collection, BrowserScopeKind.Collection);
        var grid = Field<BrowserGridModel>(window, "_browserGrid"); grid.SelectAll();
        using var barrier = await storage.Mutations.QuiesceAsync();
        var pending = Invoke(window, BrowserActions.SetRating, new SetRatingArguments(4));
        Assert.False(pending.IsCompleted);
        grid.SelectSingle(0);
        Set(window, "_browserUiGeneration", window.BrowserSemanticContext.Target!.Generation + 1);
        grid.Populate(BrowserDetailsTests.Entries(1));
        barrier.Dispose();
        Assert.Equal(ActionOutcome.Superseded, (await pending).Outcome);
        Assert.All((await storage.AssetClassifications.GetAsync(ids)).Values, value => Assert.Equal(4, value.Rating));
        Assert.Null(grid.Tiles[0].Classification);
        return null;
    });
    [Fact]
    public Task RapidActionsAndKeywordWriter_PreserveAllFieldsAndPublishNewestRevision() => WithWindow(async (window, storage, directory) => {
        var (root, ids, collection) = await Seed(storage, directory);
        await Load(window, storage, directory, root, collection, BrowserScopeKind.Collection);
        var grid = Field<BrowserGridModel>(window, "_browserGrid"); grid.SelectAll();
        var rating = Invoke(window, BrowserActions.SetRating, new SetRatingArguments(3));
        var flag = Invoke(window, BrowserActions.SetFlag, new SetFlagArguments(ClassificationFlag.Rejected));
        var label = Invoke(window, BrowserActions.SetColorLabel, new SetColorLabelArguments(ClassificationColorLabel.Blue));
        var keyword = Method(window, "AddSelectedBrowserKeywordAsync", "new");
        await Task.WhenAll(rating, flag, label, keyword);
        var values = await storage.AssetClassifications.GetAsync(ids);
        foreach (var value in values.Values) {
            Assert.Equal(3, value.Rating); Assert.Equal(AssetFlag.Rejected, value.Flag); Assert.Equal(AssetColorLabel.Blue, value.ColorLabel);
            Assert.Equal(["keep", "new"], value.Keywords);
            var tile = grid.Tiles.Single(t => t.AssetId == value.AssetId);
            Assert.Equal(value.Revision, tile.Classification!.Revision);
            Assert.Equal(value.Rating, tile.Classification.Rating); Assert.Equal(value.Flag, tile.Classification.Flag);
            Assert.Equal(value.ColorLabel, tile.Classification.ColorLabel); Assert.Equal(value.Keywords, tile.Classification.Keywords);
        }
        return null;
    });
    [Fact]
    public Task KeyboardTranslation_PreservesEditorsTreesDropdownsListsMenusAndRepeat() => WithWindow(async (window, storage, directory) => {
        var (root, ids, collection) = await Seed(storage, directory);
        await Load(window, storage, directory, root, collection, BrowserScopeKind.Collection);
        Field<BrowserGridModel>(window, "_browserGrid").SelectAll();
        foreach (var owner in new DependencyObject[] { new TextBox(), new ComboBox { IsEditable = true },
            new ComboBox { IsDropDownOpen = true }, new MenuItem(), window.BrowserFolderTree, window.BrowserCollectionTree })
            Assert.False(window.TryHandleBrowserClassificationShortcut(Key.D3, ModifierKeys.None, owner));
        foreach (var owner in new DependencyObject[] { new ComboBox(), new ListBox(), new Slider(), new MenuItem(),
            window.BrowserFolderTree, window.BrowserCollectionTree })
            Assert.False(window.TryHandleBrowserClassificationShortcut(Key.Up, ModifierKeys.Control, owner));
        Assert.True(window.TryHandleBrowserClassificationShortcut(Key.D3, ModifierKeys.None, window.BrowserGridRows));
        await WaitFor(async () => (await storage.AssetClassifications.GetAsync(ids)).Values.All(v => v.Rating == 3));
        Assert.True(window.TryHandleBrowserClassificationShortcut(Key.D4, ModifierKeys.None, window.BrowserGridRows, repeat: true));
        Assert.All((await storage.AssetClassifications.GetAsync(ids)).Values, v => Assert.Equal(3, v.Rating));
        Assert.True(window.TryHandleBrowserClassificationShortcut(Key.Up, ModifierKeys.Control, window.BrowserGridRows));
        await WaitFor(async () => (await storage.AssetClassifications.GetAsync(ids)).Values.All(v => v.Flag == AssetFlag.Picked));
        return null;
    });
    [Fact]
    public Task ControllerOpen_CapturesSelectedReviewSetAndHonorsDirtyInspectorGuard() => WithWindow(async (window, storage, directory) => {
        var (root, ids, collection) = await Seed(storage, directory);
        await Load(window, storage, directory, root, collection, BrowserScopeKind.Collection);
        var grid = Field<BrowserGridModel>(window, "_browserGrid"); grid.SelectSingle(0);
        var inspector = Field<MediaInspectorView>(window, "_inspector");
        var editor = (InspectorDescriptionEditor)inspector.DescriptionSection.DataContext;
        await editor.SetContextAsync([ids[0]], false);
        editor.Fields[0].Text = "draft";
        inspector.ConfirmTransition = _ => false;
        Assert.Equal(ActionOutcome.Cancelled, (await Invoke(window, BrowserActions.NavigateSelection,
            new NavigateSelectionArguments(BrowserMovement.Next))).Outcome);
        Assert.Equal(ActionOutcome.Cancelled, (await Invoke(window, BrowserActions.OpenCurrent, NoActionArguments.Instance)).Outcome);
        Assert.True(editor.HasDraft); Assert.True(grid.Tiles[0].IsSelected);
        inspector.ConfirmDescriptions = _ => true;
        await editor.ApplyAsync();
        Assert.False(editor.HasDraft);
        grid.SelectAll();
        var captured = grid.SelectedAssetIdsInBrowserOrder.ToArray();
        Assert.Equal(ActionOutcome.Completed, (await Invoke(window, BrowserActions.OpenCurrent, NoActionArguments.Instance)).Outcome);
        var host = Field<PlayerViewerHost>(window, "_playerViewerHost");
        Assert.Equal(captured[0], host.CurrentAsset!.AssetId);
        var review = Field<PlayerReviewSet>(host, "_reviewSet");
        Assert.Equal(captured, review.Items.Select(item => item.Asset.AssetId!.Value));
        await host.CloseAsync();
        return null;
    });
    [Fact]
    public Task ControllerOpenExplicitAsset_PreservesClickedMemberOfCapturedSubset() => WithWindow(async (window, storage, directory) => {
        var (root, ids, collection) = await Seed(storage, directory);
        await Load(window, storage, directory, root, collection, BrowserScopeKind.Collection);
        var grid = Field<BrowserGridModel>(window, "_browserGrid"); grid.SelectAll();
        var captured = grid.SelectedAssetIdsInBrowserOrder.ToArray();
        Assert.Equal(ActionOutcome.Completed, (await Invoke(window, BrowserActions.OpenCurrent, new OpenBrowserArguments(captured[1]))).Outcome);
        var host = Field<PlayerViewerHost>(window, "_playerViewerHost");
        Assert.Equal(captured[1], host.CurrentAsset!.AssetId);
        var review = Field<PlayerReviewSet>(host, "_reviewSet");
        Assert.True(review.IsSelectionSubset);
        Assert.Equal(captured, review.Items.Select(item => item.Asset.AssetId!.Value));
        await host.CloseAsync();
        return null;
    });
    [Fact]
    public Task PendingBrowserClassification_RefreshesSameAssetOpenedBeforeCommit() => WithWindow(async (window, storage, directory) => {
        var (root, ids, collection) = await Seed(storage, directory);
        await Load(window, storage, directory, root, collection, BrowserScopeKind.Collection);
        var grid = Field<BrowserGridModel>(window, "_browserGrid"); grid.SelectSingle(0);
        using var barrier = await storage.Mutations.QuiesceAsync();
        var pending = Invoke(window, BrowserActions.SetRating, new SetRatingArguments(4));
        Assert.False(pending.IsCompleted);
        await Invoke(window, BrowserActions.OpenCurrent, NoActionArguments.Instance);
        var host = Field<PlayerViewerHost>(window, "_playerViewerHost");
        Assert.Equal(0, Field<AssetClassification>(host, "_classification").Rating);
        barrier.Dispose();
        Assert.Equal(ActionOutcome.Completed, (await pending).Outcome);
        Assert.Equal(4, Field<AssetClassification>(host, "_classification").Rating);
        await host.CloseAsync();
        return null;
    });
    [Theory]
    [InlineData(BrowserScopeKind.Folder)]
    [InlineData(BrowserScopeKind.Collection)]
    [InlineData(BrowserScopeKind.SmartCollection)]
    public Task ReconciledShellTargetTracksBrowserAuthorityWithoutOwningSelection(BrowserScopeKind kind) => WithWindow(async (window, storage, directory) => {
        var (root, ids, collection) = await Seed(storage, directory);
        await Load(window, storage, directory, root, collection, kind);
        var grid = Field<BrowserGridModel>(window, "_browserGrid"); grid.SelectSingle(0);
        var browser = window.BrowserSemanticContext.Target;
        var shell = window.ShellActionTarget;
        var selection = window.BrowserSemanticContext.SelectedAssetIds.ToArray();
        await window.ShellActions.InvokeAsync(new(ReviewShellActions.ThumbnailSize, new LevelArguments(1), Controller, Guid.NewGuid(), shell));
        Assert.Equal(browser, window.BrowserSemanticContext.Target);
        Assert.Equal(selection, window.BrowserSemanticContext.SelectedAssetIds);
        Assert.Equal(shell, window.ShellActionTarget);
        await Invoke(window, BrowserActions.NavigateSelection, new NavigateSelectionArguments(BrowserMovement.Next));
        Assert.NotEqual(selection, window.BrowserSemanticContext.SelectedAssetIds);
        Assert.Equal(ActionOutcome.Superseded, (await window.ShellActions.InvokeAsync(new(ReviewShellActions.Export,
            new ExportEntryArguments(ExportEntry.BrowserSubclips), Controller, Guid.NewGuid(), shell))).Outcome);
        shell = window.ShellActionTarget;
        grid.SetQuery(new() { SortMode = BrowserSortMode.Name, SortDescending = true });
        Assert.NotEqual(browser, window.BrowserSemanticContext.Target);
        Assert.Equal(ActionOutcome.Superseded, window.CheckExportPresentationAdmission(() => window.ShellActionTarget == shell, CancellationToken.None)!.Outcome);
        shell = window.ShellActionTarget;
        Set(window, "_browserUiGeneration", window.BrowserSemanticContext.Target!.Generation + 1);
        Assert.Null(window.BrowserSemanticContext.Target);
        Assert.NotEqual(shell, window.ShellActionTarget);
        Assert.Equal(ActionUnavailableReason.NoBrowser, window.ShellActions.Eligibility(ReviewShellActions.Export,
            window.ShellActionTarget, new ExportEntryArguments(ExportEntry.BrowserSubclips)).Reason);
        return null;
    });
    private static async Task WaitFor(Func<Task<bool>> condition) {
        for (var i = 0; i < 100; i++) { if (await condition()) return; await Task.Delay(10); }
        Assert.True(await condition());
    }
    private static async Task<(Guid Root, Guid[] Ids, Guid Collection)> Seed(LightflowStorageCoordinator storage, string directory) {
        var root = (await storage.MediaRoots.CreateAsync("Source", directory)).Root!;
        var ids = new List<Guid>();
        for (var i = 0; i < 2; i++) {
            var name = $"media-{i}.png";
            File.WriteAllBytes(Path.Combine(directory, name), Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a4f8AAAAASUVORK5CYII="));
            var id = (await storage.MediaAssets.CreateAsync(root.RootId, name, "image")).Asset!.Asset.AssetId;
            await storage.AssetClassifications.SaveAsync(new(id, 0, AssetFlag.Unflagged, null, ["keep"])); ids.Add(id);
        }
        var collection = await storage.Collections.CreateCollectionAsync("Source collection");
        await storage.Collections.AddMembershipsAsync(collection.CollectionId, ids);
        return (root.RootId, ids.ToArray(), collection.CollectionId);
    }
    private static async Task Load(MainWindow window, LightflowStorageCoordinator storage, string directory, Guid root, Guid collection, BrowserScopeKind kind) {
        await Method(window, "RefreshCollectionsAsync", new object[] { null! });
        if (kind == BrowserScopeKind.Folder) {
            var navigation = Field<BrowserNavigationSession>(window, "_browserNavigation");
            await Method(window, "RunBrowserNavigationAsync", (Func<Task<BrowserFolderState?>>)(() => navigation.NavigateToPathAsync(directory)));
        } else if (kind == BrowserScopeKind.Collection) await Method(window, "LoadCollectionScopeAsync", collection);
        else {
            var smart = await storage.SmartCollections.SaveSmartCollectionAsync("All ratings", null,
                new(SmartCollectionSourceKind.Collection, CollectionId: collection),
                new() { Filters = [BrowserFilterPredicate.ForRating(BrowserNumberComparison.LessThanOrEqual, 5)] });
            await Method(window, "RefreshCollectionsAsync", new object[] { null! });
            await Method(window, "LoadSmartCollectionScopeAsync", smart, CancellationToken.None, false);
        }
        await Hydrate(window, (await storage.Collections.ListMembershipsAsync(collection)).Select(m => m.AssetId).ToArray());
    }
    private static async Task Hydrate(MainWindow window, Guid[] ids) {
        var generation = Field<long>(window, "_browserUiGeneration");
        var revision = Field<long>(window, "_browserAssetStateRevision");
        await Method(window, "LoadBrowserAssetStatesAsync", ids.Select((id,i) => new CatalogReconciliationItem(id, $"media-{i}.png", CatalogReconciliationItemStatus.Unchanged)).ToArray(), generation, revision);
    }
    private static T Field<T>(object o, string name) => (T)o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(o)!;
    private static void Set(object o, string name, object value) => o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(o, value);
    private static Task Method(object o, string name, params object[] args) => (Task)o.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(o, args)!;
    private static async Task WithWindow(Func<MainWindow, LightflowStorageCoordinator, string,
        Task<Func<LightflowStorageCoordinator, Task>?>> action)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "browser-actions-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            await StaDispatcher.RunAsync(async () =>
            {
                TestWpfApplication.EnsureLoaded();
                var appRoot = Path.Combine(directory, "app");
                Func<LightflowStorageCoordinator, Task>? restart;
                var startup = await LightflowStorageCoordinator.StartAsync(appRoot);
                await using (var storage = startup.Coordinator!)
                {
                    await storage.MediaMonitoring!.DisposeAsync();
                    storage.SaveSettings(storage.Settings with { BackupCatalogOnClose = false });
                    var window = new MainWindow(storage, startup.Status, startup.Diagnostic);
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
