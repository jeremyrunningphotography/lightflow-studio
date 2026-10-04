using Lightflow.Actions;
using System.Windows;

namespace LightflowStudio;

public partial class MainWindow
{
    private ReviewShellActions? _reviewShellActions;
    private readonly Guid _reviewShellSession = Guid.NewGuid();
    private long _reviewShellRevision;
    private string? _reviewShellSignature;
    internal ReviewShellActions ShellActions => _reviewShellActions ??= new(new WindowsReviewShellPort(this));
    internal ReviewShellTarget ShellActionTarget
    {
        get {
            // Observe the committed #351 Browser authority, never focus or visual containers.
            var browser = BrowserSemanticContext;
            var signature = string.Join("|", _workspaceClosed, MainTabs.SelectedIndex, _browserPresentation, _browserLayoutMode,
                browser.Target, _browserUiGeneration, _playerViewerHost?.ActionTarget,
                string.Join(",", browser.SelectedAssetIds),
                string.Join(",", _playerViewerHost?.SelectedSubclipIds.OrderBy(id => id).ToArray() ?? []),
                string.Join(",", _playerViewerHost?.AllSubclipIds ?? []));
            if (signature != _reviewShellSignature) { _reviewShellSignature = signature; _reviewShellRevision++; }
            return new(_reviewShellSession, _reviewShellRevision);
        }
    }
    private static readonly ActionInputSource ReviewShellUi = new("review.shell-ui", ActionInputKind.Transport);
    private static readonly ActionInputSource ReviewShellKeyboard = new("review.shell-keyboard", ActionInputKind.Keyboard);
    internal Task<ActionResult> DispatchShellActionAsync(string id, ActionArguments arguments, ActionInputSource? source = null, bool repeat = false) =>
        ShellActions.InvokeAsync(new(id, arguments, source ?? ReviewShellUi, Guid.NewGuid(), ShellActionTarget, IsRepeat: repeat));
    private Task<ActionResult> OpenExportEntryAsync(ExportEntry entry) => DispatchShellActionAsync(ReviewShellActions.Export, new ExportEntryArguments(entry));
    // Async preparation must yield to a dialog opened by another application workflow before presentation.
    internal ActionResult? CheckExportPresentationAdmission(Func<bool>? contextCurrent, CancellationToken token)
    {
        if (contextCurrent is null) return null; // Preserve existing standalone/legacy event consumers.
        if (!contextCurrent() || _workspaceClosed) return new(ActionOutcome.Superseded);
        if (token.IsCancellationRequested) return new(ActionOutcome.Cancelled);
        if (!IsEnabled || System.Windows.Interop.ComponentDispatcher.IsThreadModal)
            return new(ActionOutcome.Ineligible, ActionUnavailableReason.ModalInteraction);
        return null;
    }
    private sealed class WindowsReviewShellPort(MainWindow window) : IReviewShellPort
    {
        public ReviewShellContext Context => new(window.ShellActionTarget,
            !window._workspaceClosed && window.MainTabs.SelectedIndex == ShellDestinationSelection.Index(ShellDestination.Home),
            window.IsEnabled && !System.Windows.Interop.ComponentDispatcher.IsThreadModal);
        private bool Player => window.PlayerOwnsShortcutContext();
        public ActionEligibility Eligibility(string id, ActionArguments arguments)
        {
            var fullscreen = window._playerViewerHost?.IsFullscreen == true;
            if (id == ReviewShellActions.ThumbnailSize) {
                var available = !Player && window._browserLayoutMode != BrowserLayoutMode.Details;
                return new(available, available ? ActionUnavailableReason.None : ActionUnavailableReason.InactivePresentation);
            }
            if (id is ReviewShellActions.TogglePanel or ReviewShellActions.ShowPanel) {
                if (fullscreen) return new(false, ActionUnavailableReason.InactivePresentation);
                if (arguments is PanelSurfaceArguments panel && !window.HomeRightPanel.IsSurfaceAvailable(Key(panel.Surface)))
                    return new(false, ActionUnavailableReason.SurfaceUnavailable);
                return new(true);
            }
            if (window._browserEncodingHandoffCts is not null) return new(false, ActionUnavailableReason.OperationInProgress);
            if (arguments is not ExportEntryArguments export) return new(false, ActionUnavailableReason.InvalidArguments);
            var browserContext = window.BrowserSemanticContext;
            var browser = !Player && browserContext.Presented;
            if (export.Entry is ExportEntry.BrowserVideos or ExportEntry.BrowserSubclips) {
                if (browserContext.Target is null) return new(false, ActionUnavailableReason.NoBrowser);
                if (!browserContext.InteractionAvailable) return new(false, ActionUnavailableReason.ModalInteraction);
            }
            var video = Player && window._playerViewerHost?.CurrentAsset is { Kind: MediaPresentationKind.Video, AssetId: not null } &&
                window._playerViewerHost.SemanticActions.Eligibility(PlayerActions.PlayPause, window._playerViewerHost.ActionTarget).Available;
            var eligible = export.Entry switch {
                ExportEntry.BrowserVideos => browser && window.CurrentBrowserSelectionActions().CanExport &&
                    (window._lastLoadedBrowserState?.Location is not null || window._activeCollectionScope is not null),
                ExportEntry.BrowserSubclips => browser && window.CurrentBrowserSelectionActions().CanExport,
                ExportEntry.PlayerVideo => video && (window._lastLoadedBrowserState?.Location is not null || window._activeCollectionScope is not null),
                ExportEntry.PlayerSelectedSubclips => video && window._playerViewerHost!.SelectedSubclipIds.Count > 0,
                ExportEntry.PlayerAllSubclips => video && window._playerViewerHost!.AllSubclipIds.Count > 0,
                _ => false
            };
            return new(eligible, eligible ? ActionUnavailableReason.None : ActionUnavailableReason.ExportUnavailable);
        }
        private void Check(ReviewShellTarget target, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (window.ShellActionTarget != target) throw new OperationCanceledException("Review context changed.");
        }
        private static string Key(ReviewPanelSurface surface) => surface switch {
            ReviewPanelSurface.Inspector => "inspector", ReviewPanelSurface.Jobs => "jobs",
            ReviewPanelSurface.Subclips => "subclips", _ => "visual-index"
        };
        public Task<ActionResult> StepThumbnailsAsync(ReviewShellTarget target, int direction, CancellationToken token)
        {
            Check(target, token);
            var next = BrowserGridLayout.StepLevel(window._browserThumbnailSize, direction);
            if (next == window._browserThumbnailSize) return Task.FromResult(new ActionResult(ActionOutcome.NoChange));
            window.ApplyBrowserThumbnailSize(next);
            return Task.FromResult(new ActionResult(ActionOutcome.Completed));
        }
        public Task<ActionResult> TogglePanelAsync(ReviewShellTarget target, CancellationToken token)
        {
            Check(target, token); window.SetRightPanelOpen(!window._rightPanelOpen);
            return Task.FromResult(new ActionResult(ActionOutcome.Completed));
        }
        public Task<ActionResult> ShowPanelAsync(ReviewShellTarget target, ReviewPanelSurface surface, CancellationToken token)
        {
            Check(target, token);
            var changed = !window._rightPanelOpen || window.HomeRightPanel.ActiveSurface != Key(surface);
            window.HomeRightPanel.SelectSurface(Key(surface)); window.SetRightPanelOpen(true);
            return Task.FromResult(new ActionResult(changed ? ActionOutcome.Completed : ActionOutcome.NoChange));
        }
        public async Task<ActionResult> OpenExportAsync(ReviewShellTarget target, ExportEntry entry, CancellationToken token)
        {
            Check(target, token);
            var host = window._playerViewerHost;
            var player = entry is ExportEntry.PlayerVideo or ExportEntry.PlayerSelectedSubclips or ExportEntry.PlayerAllSubclips;
            var assets = player ? new[] { host!.CurrentAsset!.AssetId!.Value } : window.BrowserSemanticContext.SelectedAssetIds.ToArray();
            var location = window._lastLoadedBrowserState?.Location;
            var context = location is null ? null : new CapabilitySourceContext(location.RootId, location.RelativeFolder);
            Func<bool> current = () => window.ShellActionTarget == target;
            var playerTarget = host?.ActionTarget;
            if (player) host!.SetExportEntryBusy(entry, true);
            try {
                if (entry is ExportEntry.BrowserVideos or ExportEntry.PlayerVideo)
                    return await window.ApplyEncodingHandoffAsync(new CapabilityInvocation("video.encode", assets, context), current, token);
                var selected = entry == ExportEntry.PlayerSelectedSubclips ? host!.AllSubclipIds.Where(host.SelectedSubclipIds.Contains).ToArray() :
                    entry == ExportEntry.PlayerAllSubclips ? host!.AllSubclipIds.ToArray() : null;
                return await window.ApplySubclipExportHandoffAsync(new(entry == ExportEntry.BrowserSubclips ? SubclipExportEntryKind.BrowserSources : SubclipExportEntryKind.PlayerSelection,
                    assets, selected, context, IncludeNoSubclipSources: entry == ExportEntry.BrowserSubclips), current, token);
            }
            finally { if (player && host!.ActionTarget == playerTarget) host.SetExportEntryBusy(entry, false); }
        }
    }
}
