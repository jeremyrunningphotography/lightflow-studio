using Lightflow.Domain;
using Lightflow.Application;
using Lightflow.Actions;
using System.Windows;
using System.Windows.Input;

namespace LightflowStudio;

public partial class MainWindow
{
    private readonly Guid _browserActionSession = Guid.NewGuid();
    private BrowserActions? _browserActions;
    private long _browserActionCommittedGeneration;
    private long _browserActionPresentationGeneration;
    internal BrowserActions BrowserSemanticActions => _browserActions ??= new(new BrowserActionPort(this));
    internal BrowserActionContext BrowserSemanticContext
    {
        get {
            BrowserScopeIdentity? scope = _activeSmartCollection is { } smart
                ? new(BrowserScopeKind.SmartCollection, smart.SmartCollectionId)
                : _activeCollectionScope is { } collection
                    ? new(BrowserScopeKind.Collection, collection.Collection.CollectionId)
                    : _lastLoadedBrowserState?.Location is { } folder
                        ? new(BrowserScopeKind.Folder, folder.RootId, folder.RelativeFolder,
                            _lastLoadedBrowserState.Mode == BrowserScopeMode.IncludeSubfolders) : null;
            var target = scope is null || _browserActionCommittedGeneration != _browserUiGeneration ? null : new BrowserActionTarget(_browserActionSession,
                _browserUiGeneration, scope, _browserGrid.ProjectionGeneration, _browserActionPresentationGeneration);
            return new(target, MainTabs.SelectedIndex == 0 && _browserPresentation == BrowserPresentationMode.Grid,
                IsEnabled && !System.Windows.Interop.ComponentDispatcher.IsThreadModal && !BrowserNavigationPending,
                _browserGrid.SelectedAssetIdsInBrowserOrder.ToArray(), _browserKeyboardCurrentAssetId,
                _browserGrid.Tiles.Count > 0);
        }
    }
    private Task<ActionResult> InvokeBrowserActionAsync(string id, ActionArguments arguments, bool repeat = false,
        ActionInputKind kind = ActionInputKind.Transport) => BrowserSemanticActions.InvokeAsync(new(id, arguments,
            new(kind == ActionInputKind.Keyboard ? "browser.keyboard" : "browser.ui", kind), Guid.NewGuid(),
            BrowserSemanticContext.Target, IsRepeat: repeat));

    private sealed class BrowserActionPort(MainWindow owner) : IBrowserActionPort
    {
        public BrowserActionContext Context => owner.BrowserSemanticContext;
        public Task<ActionResult> NavigateAsync(BrowserActionTarget target, NavigateSelectionArguments arguments, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (Context.Target != target) return Task.FromResult(new ActionResult(ActionOutcome.Superseded));
            var index = BrowserSelectionNavigation.Destination(owner._browserGrid, owner._browserKeyboardCurrentAssetId, arguments);
            if (index < 0) return Task.FromResult(new ActionResult(ActionOutcome.NoChange));
            var accepted = arguments.Extend ? owner._browserGrid.SelectRange(index) : owner._browserGrid.SelectSingle(index);
            if (!accepted) return Task.FromResult(new ActionResult(ActionOutcome.Cancelled));
            owner._browserKeyboardCurrentAssetId = owner._browserGrid.Tiles[index].AssetId;
            if (owner._browserKeyboardCurrentAssetId is { } id) owner.RevealBrowserAsset(id);
            owner.UpdateBrowserStatusText();
            return Task.FromResult(new ActionResult(ActionOutcome.Completed));
        }
        public async Task<ActionResult> OpenAsync(BrowserActionTarget target, OpenBrowserArguments? arguments, CancellationToken token)
        {
            if (Context.Target != target) return new(ActionOutcome.Superseded);
            // Preserve established Enter/Open ordering and the captured compatible review subset.
            var tile = arguments is { } open ? owner._browserGrid.Tiles.FirstOrDefault(t => t.AssetId == open.AssetId)
                : owner._browserGrid.Tiles.FirstOrDefault(t => t.IsSelected);
            if (tile is null) return new(ActionOutcome.NoChange);
            return await owner.OpenBrowserPlayerViewerActionAsync(tile, target, token);
        }
        public async Task<ActionResult> ClassifyAsync(BrowserActionTarget target, IReadOnlyList<Guid> ids,
            ActionArguments arguments, CancellationToken token)
        {
            if (Context.Target != target) return new(ActionOutcome.Superseded);
            return await new AssetClassificationService(owner._storage.AssetClassifications, owner._storage.Mutations)
                .ExecuteAsync(ids, arguments, values => {
                    if (!owner.IsSameBrowserContext(target)) return new ActionResult(ActionOutcome.Superseded);
                    owner.PublishBrowserClassifications(values);
                    return new ActionResult(ActionOutcome.Completed);
                }, token);
        }
    }
    private bool IsSameBrowserContext(BrowserActionTarget? target) => target is not null &&
        BrowserSemanticContext.Target is { } current && target with { ProjectionGeneration = current.ProjectionGeneration, PresentationGeneration = current.PresentationGeneration } == current;

    private void PublishBrowserClassifications(IEnumerable<AssetClassification> values)
    {
        InvalidateInspector();
        foreach (var value in values) {
            _browserAssetStateRevisions[value.AssetId] = ++_browserAssetStateRevision;
            _browserGrid.ApplyClassification(value);
            _playerViewerHost?.ApplyCommittedClassification(value);
        }
        _browserGrid.ReapplyQuery();
        UpdateBrowserStatusText();
    }
    internal bool TryHandleBrowserClassificationShortcut(Key key, ModifierKeys modifiers, DependencyObject? input, bool repeat = false) =>
        TryHandleConfiguredBrowserShortcut(key, modifiers, input, repeat);
    private bool BrowserOwnsLocalKey(Key key, ModifierKeys modifiers, DependencyObject? input) =>
        BrowserPickerOwnsNumber(key, input) ||
        PlayerKeyboardOwnership.Within(input, HomeRightPanel) ||
        PlayerKeyboardOwnership.Within(input, BrowserFolderTree) ||
        PlayerKeyboardOwnership.Within(input, BrowserCollectionTree) ||
        PlayerKeyboardOwnership.Owns(key, modifiers, input, BrowserGridRows, BrowserGridRows);

    private bool BrowserPickerOwnsNumber(Key key, DependencyObject? input)
    {
        if (key < Key.D0 || key > Key.D5) return false;
        for (var current = input; current is not null; current = PlayerKeyboardOwnership.Parent(current)) {
            if (ReferenceEquals(current, BrowserGridRows)) return false;
            if (current is System.Windows.Controls.TabControl tabs && tabs.SelectedContent is DependencyObject content &&
                PlayerKeyboardOwnership.Within(input, content)) return false;
            if (current is System.Windows.Controls.Primitives.Selector or System.Windows.Controls.ComboBoxItem) return true;
        }
        return false;
    }
}

internal static class BrowserSelectionNavigation
{
    internal static int Destination(BrowserGridModel grid, Guid? currentId, NavigateSelectionArguments arguments)
    {
        if (grid.Tiles.Count == 0) return -1;
        var current = grid.Tiles.FirstOrDefault(t => t.AssetId == currentId)
            ?? grid.Tiles.FirstOrDefault(t => t.IsSelected) ?? grid.Tiles[0];
        return arguments.Movement switch {
            BrowserMovement.First => 0,
            BrowserMovement.Last => grid.Tiles.Count - 1,
            BrowserMovement.Previous => Math.Max(0, current.Index - arguments.Distance),
            _ => Math.Min(grid.Tiles.Count - 1, current.Index + arguments.Distance)
        };
    }
}
