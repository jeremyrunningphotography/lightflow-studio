using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace LightflowStudio;

public partial class MainWindow
{
    private SmartCollectionDefinition? _activeSmartCollection;
    private BrowserNavigationSession? _smartNavigation;
    private bool _smartUpdating;
    private long _smartCandidateRefresh;
    private void UpdateSmartCollectionEmptyState() =>
        BrowserEmptyState.Visibility = _browserGrid.TotalCount == 0 ? Visibility.Visible : Visibility.Collapsed;

    private void SmartSourceMembershipChanged(object? sender, Guid collectionId) => Dispatcher.BeginInvoke(async () =>
    {
        if (_activeSmartCollection is { } smart && smart.Source.CollectionId == collectionId)
            await RefreshSmartCandidatesAsync(smart);
    });

    private async Task RefreshSmartCandidatesAsync(SmartCollectionDefinition smart)
    {
        var generation = _browserUiGeneration;
        var refresh = ++_smartCandidateRefresh;
        try
        {
            BrowserCollectionScope scope;
            if (smart.Source.CollectionId is { } collectionId)
                scope = (await _browserCollectionScopes.LoadAsync(collectionId)) with { Collection = smart.Organization };
            else
            {
                var assets = await _storage.MediaAssets.ListScopeAsync(smart.Source.RootId!.Value, smart.Source.RelativeFolder!, smart.Source.IncludeSubfolders);
                var available = assets.Where(asset => asset.SourceStatus == MediaAssetSourceStatus.Available).Select(asset => asset.AssetId).ToHashSet();
                var entries = BrowserCatalogScope.Entries(assets, _storage.MediaTypes).Select(entry => entry with
                    { IsAvailable = entry.AssetId is { } id && available.Contains(id) }).ToArray();
                scope = new(smart.Organization, entries,
                    assets.Select(asset => new CatalogReconciliationItem(asset.AssetId, asset.RelativePath, CatalogReconciliationItemStatus.Unchanged)).ToArray(),
                    entries.Count(entry => !entry.IsAvailable), null);
            }
            if (generation == _browserUiGeneration && refresh == _smartCandidateRefresh && _activeSmartCollection?.SmartCollectionId == smart.SmartCollectionId)
                ApplySmartScope(scope, generation);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or KeyNotFoundException)
        { if (generation == _browserUiGeneration) { _browserValidationFailure = ex.Message; UpdateBrowserStatusText(); } }
    }

    private SmartCollectionSource? CurrentSmartSource() => _activeSmartCollection is not null ? null :
        _activeCollectionScope is { } collection
            ? new(SmartCollectionSourceKind.Collection, CollectionId: collection.Collection.CollectionId)
            : _lastLoadedBrowserState?.Location is { } folder
                ? new(SmartCollectionSourceKind.Folder, folder.RootId, folder.RelativeFolder,
                    IncludeSubfolders: _lastLoadedBrowserState.Mode == BrowserScopeMode.IncludeSubfolders) : null;

    private async void BrowserCreateSmartCollection_Click(object sender, RoutedEventArgs e) =>
        await RunCollectionActionAsync(() => ShowSmartCollectionEditorAsync(saveView: true));
    private async void BrowserNewSmartCollection_Click(object sender, RoutedEventArgs e) =>
        await RunCollectionActionAsync(() => ShowSmartCollectionEditorAsync(parent: sender is MenuItem ? CollectionActionNode?.Id : null));
    private async void BrowserEditSmartCollection_Click(object sender, RoutedEventArgs e)
    {
        if (CollectionActionNode is not { IsSmartCollection: true } node) return;
        await RunCollectionActionAsync(async () => await ShowSmartCollectionEditorAsync(
            editing: await _storage.SmartCollections.GetSmartCollectionAsync(node.Id)));
    }

    private async void BrowserFolderNewSmartCollection_Click(object sender, RoutedEventArgs e)
    {
        if (_locationActionNode is not { RootId: { } rootId, RelativeFolder: { } relative }) return;
        var current = CurrentSmartSource();
        var source = new SmartCollectionSource(SmartCollectionSourceKind.Folder, rootId, relative,
            IncludeSubfolders: current is { Kind: SmartCollectionSourceKind.Folder } && current.RootId == rootId &&
                string.Equals(current.RelativeFolder, relative, StringComparison.OrdinalIgnoreCase) && current.IncludeSubfolders);
        await RunCollectionActionAsync(() => ShowSmartCollectionEditorAsync(sourceOverride: source));
    }

    private async Task<IReadOnlyList<BrowserGridTile>> LoadSmartFilterValuesAsync(SmartCollectionSource source)
    {
        // Catalog/Preview reads only: choosing a Source in the editor does not navigate or start discovery.
        IReadOnlyList<MediaAsset> assets;
        if (source.Kind == SmartCollectionSourceKind.Folder)
            assets = await _storage.MediaAssets.ListScopeAsync(source.RootId!.Value, source.RelativeFolder!, source.IncludeSubfolders);
        else
        {
            var ids = (await _storage.Collections.ListMembershipsAsync(source.CollectionId!.Value)).Select(m => m.AssetId).ToHashSet();
            assets = (await _storage.MediaAssets.ListAsync()).Where(a => ids.Contains(a.AssetId)).ToArray();
        }
        var model = new BrowserGridModel();
        model.Populate(BrowserCatalogScope.Entries(assets, _storage.MediaTypes));
        var assetIds = assets.Select(a => a.AssetId).ToArray();
        model.ApplyAssetIdentities(assets.Select(a => new CatalogReconciliationItem(a.AssetId, a.RelativePath, CatalogReconciliationItemStatus.Unchanged)).ToArray());
        model.ApplyAssetStates(await _storage.BrowserAssetStates.GetQueryStatesAsync(assetIds));
        if (_storage.Previews is { } previews)
            foreach (var (id, record) in await previews.GetManyAsync(assetIds))
                if (record.MetadataState == PreviewComponentState.Current)
                    model.ApplyMetadata(id, BrowserQueryEngine.ExtractMetadata(record.MetadataJson));
        return model.Tiles;
    }

    private async Task ShowSmartCollectionEditorAsync(bool saveView = false, Guid? parent = null, SmartCollectionDefinition? editing = null, SmartCollectionSource? sourceOverride = null)
    {
        var nodes = BrowserCollectionTreeModel.Flatten(_browserCollectionTree.Roots);
        var dialog = new SmartCollectionDialog(_storage.BrowserLocations, await _storage.MediaRoots.ListAsync(),
            BrowserCollectionPlacement.Options(_browserCollectionTree.Roots),
            nodes.Where(node => node.IsCollection).Select(node => (node.Id, CollectionDisplayPath(node.Id))).ToArray(),
            editing?.Organization.Name ?? "", editing?.Organization.ParentCollectionSetId ?? parent,
            editing?.Source ?? sourceOverride ?? CurrentSmartSource(), editing?.Query ?? (saveView ? BrowserQueryIntent.Capture(_browserGrid.Query with { SearchText = BrowserSearchBox.Text }) : new()),
            editing is not null, LoadSmartFilterValuesAsync) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        await RunCollectionActionAsync(async () =>
        {
            var saved = await _storage.SmartCollections.SaveSmartCollectionAsync(dialog.CollectionName, dialog.ParentSetId,
                dialog.Source!, dialog.Query, editing?.SmartCollectionId, editing?.Organization.Revision);
            await RefreshCollectionsAsync(saved.SmartCollectionId);
            if (editing is not null && _activeSmartCollection?.SmartCollectionId == saved.SmartCollectionId && editing.Source == saved.Source)
            {
                _activeSmartCollection = saved;
                _browserGrid.SetDefiningQuery(saved.Query.ToQuery());
                if (_activeCollectionScope is { } scope) _activeCollectionScope = scope with { Collection = saved.Organization };
                BrowserCurrentPath.Text = $"Collections / {CollectionDisplayPath(saved.SmartCollectionId)}";
                UpdateBrowserStatusText();
            }
            else if (editing is null || _activeSmartCollection?.SmartCollectionId == saved.SmartCollectionId)
                await LoadCollectionScopeAsync(saved.SmartCollectionId);
        });
    }

    private async Task LoadSmartCollectionScopeAsync(SmartCollectionDefinition definition, CancellationToken token, bool restoring)
    {
        if (!restoring) { WorkspaceUserInteraction(); _pendingWorkspaceGrid = null; }
        if (!TryLeaveInspectorContext()) { RestoreLoadedBrowserSelection(); return; }
        _browserNavigation.CancelPending();
        _collectionScopeCts?.Cancel(); _collectionScopeCts?.Dispose();
        var request = _collectionScopeCts = CancellationTokenSource.CreateLinkedTokenSource(token);
        var generation = ++_browserUiGeneration;
        _smartNavigation?.Dispose(); _smartNavigation = null;
        _activeSmartCollection = definition;
        _browserValidationFailure = null;
        _browserGrid.SetDefiningQuery(definition.Query.ToQuery());
        _smartUpdating = definition.Source.Kind == SmartCollectionSourceKind.Folder;
        bool Current() => generation == _browserUiGeneration && !request.IsCancellationRequested;
        var acceptProgress = true;
        try
        {
            if (definition.Source.CollectionId is { } sourceId)
            {
                var scope = await _browserCollectionScopes.LoadAsync(sourceId, request.Token);
                if (Current()) ApplySmartScope(scope with { Collection = definition.Organization }, generation);
                return;
            }
            async Task PresentKnownAsync()
            {
                var source = definition.Source;
                var assets = await _storage.MediaAssets.ListScopeAsync(source.RootId!.Value, source.RelativeFolder!, source.IncludeSubfolders, request.Token);
                if (!Current() || !acceptProgress) return;
                var available = assets.Where(asset => asset.SourceStatus == MediaAssetSourceStatus.Available).Select(asset => asset.AssetId).ToHashSet();
                var entries = BrowserCatalogScope.Entries(assets, _storage.MediaTypes).Select(entry => entry with
                    { IsAvailable = entry.AssetId is { } id && available.Contains(id) }).ToArray();
                var items = assets.Select(asset => new CatalogReconciliationItem(asset.AssetId, asset.RelativePath,
                    CatalogReconciliationItemStatus.Unchanged)).ToArray();
                ApplySmartScope(new(_activeSmartCollection?.Organization ?? definition.Organization, entries, items,
                    entries.Count(entry => !entry.IsAvailable), null), generation);
            }
            await PresentKnownAsync();
            if (!Current()) return;
            var navigation = _smartNavigation = new(_storage.MediaRoots, _storage.BrowserLocations, _storage.MediaDiscovery,
                _storage.MediaFolders, _storage.BrowserRecursiveRoots, _storage.RecursiveMediaDiscovery, _storage.MediaAssets, _storage.MediaTypes);
            // Reuse bounded recursive discovery; publish its newly Cataloged candidates periodically, without a second walk.
            var progressQueued = 0;
            var lastPublish = DateTime.MinValue;
            navigation.RecursiveScopeProgressChanged += (_, _) =>
            {
                if (Interlocked.CompareExchange(ref progressQueued, 1, 0) != 0) return;
                Dispatcher.BeginInvoke(async () =>
                {
                    try
                    {
                        if (!Current() || !acceptProgress || DateTime.UtcNow - lastPublish < TimeSpan.FromMilliseconds(350)) return;
                        lastPublish = DateTime.UtcNow;
                        await PresentKnownAsync();
                    }
                    catch (Exception ex) when (ex is OperationCanceledException or IOException or InvalidOperationException) { }
                    finally { Interlocked.Exchange(ref progressQueued, 0); }
                });
            };
            var result = await navigation.NavigateSourceAsync(definition.Source.RootId!.Value, definition.Source.RelativeFolder!,
                definition.Source.IncludeSubfolders, request.Token);
            acceptProgress = false;
            ++_smartCandidateRefresh;
            if (!Current()) return;
            if (result?.Status is BrowserFolderStatus.Ready or BrowserFolderStatus.Empty)
            {
                var items = result.Reconciliation?.Items ?? [];
                _browserGrid.InvalidateChangedAssets(items);
                ApplySmartScope(new(_activeSmartCollection?.Organization ?? definition.Organization, result.RecursiveMediaEntries ?? result.Entries,
                    items, 0, result.DerivedWork), generation);
            }
            else
            {
                _browserValidationFailure = result?.Diagnostic ?? "Smart Collection Source is unavailable. Showing known media.";
                if (_activeCollectionScope is { } known && result?.Status is BrowserFolderStatus.RootUnavailable or BrowserFolderStatus.RootNotFound
                    or BrowserFolderStatus.FolderNotFound or BrowserFolderStatus.FolderUnavailable or BrowserFolderStatus.AccessDenied)
                    ApplySmartScope(known with { Entries = known.Entries.Select(entry => entry with { IsAvailable = false }).ToArray(),
                        UnavailableCount = known.Entries.Count }, generation);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException)
        { if (Current()) _browserValidationFailure = ex.Message; }
        finally
        {
            acceptProgress = false;
            if (Current())
            {
                _smartUpdating = false;
                BrowserWorkingIndicator.Visibility = Visibility.Collapsed;
                UpdateBrowserStatusText();
            }
        }
    }

    private void ApplySmartScope(BrowserCollectionScope scope, long generation)
    {
        if (scope.DerivedWork is null && _activeCollectionScope?.Collection.CollectionId == scope.Collection.CollectionId)
            scope = scope with { DerivedWork = _activeCollectionScope.DerivedWork };
        ApplyCollectionScope(scope, generation);
        BrowserIncludeSubfoldersButton.IsChecked = _activeSmartCollection?.Source.IncludeSubfolders == true;
        BrowserWorkingIndicator.Visibility = _smartUpdating ? Visibility.Visible : Visibility.Collapsed;
        BrowserEmptyTitle.Text = "No matching media in this Smart Collection";
        BrowserEmptyMessage.Text = "Edit the Smart Collection to change its Source or defining filters.";
        UpdateBrowserStatusText();
    }
}
