using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace LightflowStudio;

public partial class MainWindow
{
    private readonly WorkspaceRestorationRequest _workspaceRestoration = new();
    private WorkspaceContinuationState? _savedContinuation;
    private bool _restoringWorkspace;
    private bool _workspaceTreeRevealSuppressed;
    private WorkspaceGridState? _pendingWorkspaceGrid;
    private WorkspaceGridState? _playerBrowserGrid;
    private bool _workspacePlayerPlaceholder;
    private bool _workspaceClosed;

    private void PrepareWorkspacePlayerPresentation()
    {
        if (_savedContinuation?.Player is not { } player) return;
        _workspacePlayerPlaceholder = true;
        EnsurePlayerViewerHost();
        _playerViewerHost!.AssetNameText.Text = player.Asset.Name;
        SetBrowserPresentationMode(BrowserPresentationMode.PlayerViewer);
    }

    private void InitializeWorkspaceContinuation()
    {
        _savedContinuation = _workspaceState.Current.Continuation;
        _restoringWorkspace = true;
        if (_savedContinuation is { } saved)
        {
            _browserQueryScope = _workspaceState.Current.Layout?.BrowserCollectionId is { } collection
                ? $"collection:{collection:D}"
                : _workspaceState.Current.Browser is { } folder ? $"folder:{folder.RootId:D}:{folder.RelativeFolder}" : null;
            _browserGrid.SetQuery(saved.Query);
            _lockedBrowserQuery = saved.QueryLocked ? saved.Query : null;
            _synchronizingBrowserQuery = true;
            try { BrowserSearchBox.Text = saved.Query.SearchText; BrowserSortCombo.SelectedIndex = (int)saved.Query.SortMode; }
            finally { _synchronizingBrowserQuery = false; }
            BrowserQueryLockButton.IsChecked = saved.QueryLocked;
            SyncBrowserQueryToolbarVisuals();
        }
        // Actual input revokes startup intent before routed controls perform their action. Focus/layout do not.
        AddHandler(Mouse.PreviewMouseDownEvent, new MouseButtonEventHandler((_, _) => WorkspaceUserInteraction()), true);
        AddHandler(Mouse.PreviewMouseWheelEvent, new MouseWheelEventHandler((_, _) => WorkspaceUserInteraction()), true);
        AddHandler(Keyboard.PreviewKeyDownEvent, new System.Windows.Input.KeyEventHandler((_, _) => WorkspaceUserInteraction()), true);
        LocationChanged += (_, _) => ScheduleWorkspaceCapture();
        SizeChanged += (_, _) => ScheduleWorkspaceCapture();
        BrowserFolderScrollViewer.ScrollChanged += (_, _) => ScheduleWorkspaceCapture();
        BrowserGridRows.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((_, _) =>
        {
            ApplyPendingWorkspaceGrid();
            ScheduleWorkspaceCapture();
        }));
        BrowserGridRows.LayoutUpdated += (_, _) => ApplyPendingWorkspaceGrid();
    }

    private void WorkspaceUserInteraction()
    {
        // A hidden Browser may still await layout after Player restoration has completed. Once the
        // Browser is active, new input owns its viewport even if that queued layout never ran.
        if (_browserPresentation == BrowserPresentationMode.Grid) _pendingWorkspaceGrid = null;
        if (_restoringWorkspace)
        {
            _workspaceRestoration.Cancel();
            _pendingWorkspaceGrid = null;
            _restoringWorkspace = false;
            BrowserGridRows.Opacity = 1;
            if (_workspacePlayerPlaceholder)
            {
                _workspacePlayerPlaceholder = false;
                SetBrowserPresentationMode(BrowserPresentationMode.Grid);
            }
        }
        _workspaceTreeRevealSuppressed = false;
        ScheduleWorkspaceCapture();
    }

    private void ScheduleWorkspaceCapture()
    {
        if (_workspaceClosed || _restoringWorkspace || !IsLoaded) return;
        _workspaceSaveTimer.Stop();
        _workspaceSaveTimer.Start();
    }

    private WorkspaceGridState CaptureWorkspaceGrid()
    {
        var viewer = FindBrowserGridScrollViewer();
        var offset = viewer?.VerticalOffset ?? _browserGridScrollOffset;
        var height = _browserGrid.Rows.Count > 0 ? (viewer?.ExtentHeight ?? 0) / _browserGrid.Rows.Count : 0;
        var row = height > 0 ? Math.Clamp((int)(offset / height), 0, _browserGrid.Rows.Count - 1) : 0;
        return new()
        {
            SelectedAssetIds = _browserGrid.SelectedAssetIdsInBrowserOrder,
            AnchorAssetId = _browserGrid.SelectionAnchorAssetId,
            TopAssetId = _browserGrid.Rows.Count > row ? _browserGrid.Rows[row].Tiles.FirstOrDefault()?.AssetId : null,
            VerticalOffset = offset,
            WithinRowOffset = height > 0 ? offset - row * height : 0
        };
    }

    private void CaptureWorkspaceContinuation()
    {
        // Closing before a pending startup request completes must not overwrite its unmaterialized intent.
        if (_restoringWorkspace) return;
        var player = _browserPresentation == BrowserPresentationMode.PlayerViewer;
        var grid = player && _playerBrowserGrid is not null ? _playerBrowserGrid : CaptureWorkspaceGrid();
        _workspaceState.SetContinuation(new()
        {
            Query = _browserGrid.Query with { SearchText = BrowserSearchBox.Text },
            QueryLocked = _lockedBrowserQuery is not null,
            ExpandedFolders = _browserTree.KnownFolders().Where(node => node.IsExpanded && node.RootId is not null)
                .Select(node => new WorkspaceBrowserLocationState { RootId = node.RootId!.Value, RelativeFolder = node.RelativeFolder ?? "" }).ToArray(),
            TreeVerticalOffset = BrowserFolderScrollViewer.VerticalOffset,
            TreeHorizontalOffset = BrowserFolderScrollViewer.HorizontalOffset,
            Grid = grid,
            Player = player ? _playerViewerHost?.CaptureWorkspaceState() : null
        });
    }

    private async Task RestoreWorkspaceContinuationAsync()
    {
        if (!_workspaceRestoration.IsCurrent) return;
        var token = _workspaceRestoration.Token;
        var saved = _savedContinuation;
        try
        {
            if (saved is not null) BrowserGridRows.Opacity = 0;
            if (_workspaceState.Current.Layout?.BrowserCollectionId is { } collectionId)
                await LoadCollectionScopeCoreAsync(collectionId, token, restoring: true);
            else await RestoreBrowserLocationAsync(_workspaceState.Current.Browser);
            token.ThrowIfCancellationRequested();

            if (saved is null && _lastLoadedBrowserState?.Location is { } legacyLocation)
            {
                _restoringWorkspace = false;
                await RevealBrowserTreeAncestorsAsync(legacyLocation, _browserUiGeneration);
            }

            if (saved is not null)
            {
                // Hydrate through the ordinary shared projection readers before restoring result-dependent state.
                var ids = _browserGrid.ScopeAssetIds;
                if (_activeCollectionScope is not null) await LoadCollectionPreviewStateAsync(ids, _browserUiGeneration);
                else await ApplyBrowserPreviewRecordsAsync(ids.ToHashSet(), ids.ToHashSet(), _browserUiGeneration,
                    () => _workspaceRestoration.IsCurrent, _lastLoadedBrowserState?.CatalogAssets);
                await LoadBrowserAssetStatesAsync(ids.Select(id => new CatalogReconciliationItem(id, "",
                    CatalogReconciliationItemStatus.Unchanged)).ToArray(), _browserUiGeneration, _browserAssetStateRevision);
                token.ThrowIfCancellationRequested();
                _browserGrid.ReapplyQuery();
                _browserGrid.RestoreWorkspaceSelection(saved.Grid);
                _pendingWorkspaceGrid = saved.Grid;
                if (saved.Player is null) BrowserGridRows.Opacity = 1;
                UpdateBrowserStatusText();
                UpdateInspectorContext();
                // The current folder's ancestor chain uses the existing lazy model; saved expansion then
                // controls the final branch flags, including intentionally collapsed ancestors.
                await WorkspaceTreeRestoration.RestoreAsync(_browserTree, _storage.MediaRoots, _storage.MediaFolders,
                    saved.ExpandedFolders, token, action =>
                    {
                        token.ThrowIfCancellationRequested();
                        _synchronizingBrowserTree = true;
                        try { action(); } finally { _synchronizingBrowserTree = false; }
                    });
                token.ThrowIfCancellationRequested();
                _synchronizingBrowserTree = true;
                try
                {
                    var expanded = saved.ExpandedFolders.Select(f => $"{f.RootId}:{f.RelativeFolder}").ToHashSet(StringComparer.OrdinalIgnoreCase);
                    foreach (var node in _browserTree.KnownFolders().Where(n => n.RootId is not null))
                        node.IsExpanded = expanded.Contains($"{node.RootId}:{node.RelativeFolder}");
                }
                finally { _synchronizingBrowserTree = false; }
                SyncBrowserTreeRecursiveIcons();
                _workspaceTreeRevealSuppressed = true;
                await Dispatcher.InvokeAsync(() =>
                {
                    if (!token.IsCancellationRequested)
                    {
                        BrowserFolderScrollViewer.UpdateLayout();
                        BrowserFolderScrollViewer.ScrollToVerticalOffset(saved.TreeVerticalOffset);
                        BrowserFolderScrollViewer.ScrollToHorizontalOffset(saved.TreeHorizontalOffset);
                        ApplyPendingWorkspaceGrid();
                    }
                }, DispatcherPriority.Loaded);
                token.ThrowIfCancellationRequested();
                if (saved.Player is { } player) await RestoreWorkspacePlayerAsync(player, token);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            _activityLogFile.TryAppend($"[Workspace] Continuation unavailable: {exception.Message}");
        }
        finally
        {
            _restoringWorkspace = false;
            if (_workspacePlayerPlaceholder && !token.IsCancellationRequested)
            {
                _workspacePlayerPlaceholder = false;
                SetBrowserPresentationMode(BrowserPresentationMode.Grid);
            }
            BrowserGridRows.Opacity = 1;
            ScheduleWorkspaceCapture();
        }
    }

    private void ApplyPendingWorkspaceGrid()
    {
        if (_pendingWorkspaceGrid is not { } saved || !_workspaceRestoration.IsCurrent ||
            FindBrowserGridScrollViewer() is not { ViewportHeight: > 0, ExtentHeight: > 0 } viewer || _browserGrid.Rows.Count == 0) return;
        var row = _browserGrid.Rows.Select((value, index) => (value, index))
            .FirstOrDefault(pair => saved.TopAssetId is not null && pair.value.Tiles.Any(tile => tile.AssetId == saved.TopAssetId));
        _browserGridScrollOffset = saved.RestoreOffset(row.value is null ? null : row.index,
            _browserGrid.Rows.Count, viewer.ExtentHeight, viewer.ScrollableHeight);
        _pendingWorkspaceGrid = null;
        viewer.ScrollToVerticalOffset(_browserGridScrollOffset);
    }

    private async Task RestoreWorkspacePlayerAsync(WorkspacePlayerState player, CancellationToken token)
    {
        var resolved = await _storage.MediaAssets.GetAsync(player.Asset.AssetId!.Value, token);
        token.ThrowIfCancellationRequested();
        if (resolved is null || !resolved.SourceExists && resolved.RootAvailability == MediaRootAvailability.Online) return;
        var asset = player.Asset with { RootId = resolved.Asset.RootId, RelativePath = resolved.Asset.RelativePath,
            Key = resolved.Asset.RelativePathKey, Name = System.IO.Path.GetFileName(resolved.Asset.RelativePath) };
        var path = new MediaPathResolution(asset.RootId, asset.RelativePath, asset.Key, resolved.PhysicalPath,
            resolved.RootAvailability, resolved.SourceExists, resolved.Diagnostic);
        await OpenResolvedBrowserPlayerAsync(asset, path, player, token);
    }
}
