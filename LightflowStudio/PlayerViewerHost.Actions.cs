using Lightflow.Actions;
using System.Windows;
using System.Windows.Input;

namespace LightflowStudio;

public partial class PlayerViewerHost
{
    private readonly Guid _actionSessionId = Guid.NewGuid();
    private PlayerActions _actions = null!;
    private Guid? _keyboardColorSession;
    private bool _actionSourceReady;
    private Window? _actionWindow;
    private PlayerReviewFocus? _reviewFocus;
    private static readonly ActionInputSource KeyboardActionSource = new("player.keyboard", ActionInputKind.Keyboard);
    private static readonly ActionInputSource TransportActionSource = new("player.transport", ActionInputKind.Transport);
    internal Func<bool>? ActionPresentationActive { get; set; }
    internal PlayerActions SemanticActions => _actions;
    internal PlayerActionTarget? ActionTarget => _service is null && _currentAsset is null ? null : new(_actionSessionId, _generation, _currentAsset?.AssetId);

    private void InitializeActions()
    {
        _actions = new(new WindowsPlayerActionPort(this));
        Loaded += (_, _) => AttachActionWindow();
        Unloaded += (_, _) => { CancelPlayerActionSessions(); DetachActionWindow(); };
        IsEnabledChanged += (_, _) => { if (!IsEnabled) CancelPlayerActionSessions(); };
    }
    private void AttachActionWindow()
    {
        var window = Window.GetWindow(this);
        if (ReferenceEquals(window, _actionWindow)) return;
        DetachActionWindow();
        _actionWindow = window;
        if (window is null) return;
        _reviewFocus = new(window, () => IsVisible && (ActionPresentationActive?.Invoke() ?? true), () => Focus());
        window.Deactivated += ActionWindowDeactivated;
        window.IsEnabledChanged += ActionWindowEnabledChanged;
        System.Windows.Interop.ComponentDispatcher.EnterThreadModal += ActionThreadModal;
    }
    private void DetachActionWindow()
    {
        if (_actionWindow is not { } window) return;
        _reviewFocus?.Dispose();
        _reviewFocus = null;
        window.Deactivated -= ActionWindowDeactivated;
        window.IsEnabledChanged -= ActionWindowEnabledChanged;
        System.Windows.Interop.ComponentDispatcher.EnterThreadModal -= ActionThreadModal;
        _actionWindow = null;
    }
    private void ActionWindowDeactivated(object? sender, EventArgs e) => CancelPlayerActionSessions();
    private void ActionThreadModal(object? sender, EventArgs e) => CancelPlayerActionSessions();
    private void ActionWindowEnabledChanged(object sender, DependencyPropertyChangedEventArgs e)
    { if (_actionWindow?.IsEnabled == false) CancelPlayerActionSessions(); }
    internal void CancelPlayerActionSessions()
    {
        var result = _actions.CancelGestures();
        if (result.Outcome == ActionOutcome.Failed) SetStatus(result.Diagnostic);
        _keyboardColorSession = null;
    }
    private async Task DispatchTransportAsync(string action, ActionArguments arguments)
    {
        var invocation = new ActionInvocation(action, arguments, TransportActionSource, Guid.NewGuid(), ActionTarget);
        PresentActionResult(invocation, await _actions.InvokeAsync(invocation));
    }
    private bool DispatchKeyboardAction(Key key, bool repeat)
    {
        var action = key switch { Key.Space => PlayerActions.PlayPause, Key.Left or Key.Right => PlayerActions.StepFrame, _ => PlayerActions.ColorBypass };
        var phase = key == Key.C ? ActionPhase.Begin : ActionPhase.Invoke;
        if (key == Key.C)
        {
            if (!_actions.Eligibility(action, ActionTarget).Available) return false;
            _keyboardColorSession ??= Guid.NewGuid();
        }
        ActionArguments arguments = key is Key.Left or Key.Right ? new FrameStepArguments(key == Key.Left ? -1 : 1) : NoActionArguments.Instance;
        _ = DispatchKeyboardAsync(new(action, arguments, KeyboardActionSource, _keyboardColorSession ?? Guid.NewGuid(), ActionTarget, phase, repeat));
        return true;
    }
    private async Task DispatchKeyboardAsync(ActionInvocation invocation)
    {
        PresentActionResult(invocation, await _actions.InvokeAsync(invocation));
    }
    private void PresentActionResult(ActionInvocation invocation, ActionResult result)
    {
        if (ActionTarget != invocation.Target) return;
        if (result.Outcome == ActionOutcome.Failed) SetStatus(result.Diagnostic ?? "Player action failed.");
        else if (result.Outcome == ActionOutcome.NoChange && result.Diagnostic is not null) SetStatus(result.Diagnostic);
        else if (result.Outcome == ActionOutcome.Ineligible && result.Reason == ActionUnavailableReason.WorkingRangeUnavailable)
            SetStatus(CurrentSubclipCreationEligibility().Problem);
    }
    private bool DispatchReviewKeyboard(string action, ActionArguments arguments, bool repeat)
    {
        _ = DispatchKeyboardAsync(new(action, arguments, KeyboardActionSource, Guid.NewGuid(), ActionTarget, IsRepeat: repeat));
        return true;
    }
    private bool EndKeyboardColorSession()
    {
        if (_keyboardColorSession is not { } session) return false;
        _keyboardColorSession = null;
        _ = DispatchKeyboardAsync(new(PlayerActions.ColorBypass, NoActionArguments.Instance, KeyboardActionSource,
            session, ActionTarget, ActionPhase.End));
        return true;
    }

    /// <summary>Windows presentation adapter; all existing lease, range and retained-frame behavior stays here.</summary>
    private sealed class WindowsPlayerActionPort(PlayerViewerHost host) : IPlayerActionPort
    {
        private sealed class StepBatch { public Exception? Error; }
        private StepBatch? _stepBatch;
        public PlayerActionContext Context => new(host.ActionTarget, host.ActionPresentationActive?.Invoke() ?? true,
            host.IsEnabled && (Window.GetWindow(host)?.IsEnabled ?? true) && !System.Windows.Interop.ComponentDispatcher.IsThreadModal,
            host._actionSourceReady && host._service?.Snapshot is { SourcePath: not null, State: MediaPlaybackState.Paused or MediaPlaybackState.Playing or MediaPlaybackState.Ended or MediaPlaybackState.Seeking },
            host._colorActive, host._currentAsset is not null);
        private static ActionEligibility Available(bool available, ActionUnavailableReason reason) => new(available, available ? ActionUnavailableReason.None : reason);
        public ActionEligibility ReviewEligibility(string actionId) => actionId switch {
            PlayerActions.TraverseReview => Available(host._reviewSet is not null && host._reviewResolver is not null, ActionUnavailableReason.ReviewSetUnavailable),
            PlayerActions.CreateSubclip => Available(host.CurrentSubclipCreationEligibility().CanCreate, ActionUnavailableReason.WorkingRangeUnavailable),
            PlayerActions.AddMarker or PlayerActions.NavigateMarker when host._markers is null || host._currentAsset?.AssetId is null => new(false, ActionUnavailableReason.MarkerServiceUnavailable),
            PlayerActions.SetBoundary or PlayerActions.AddMarker or PlayerActions.NavigateMarker => Available(
                (host._retainedSteppedFrame?.Timestamp ?? host._service?.Snapshot.DisplayedTimestamp) is { IsDecodedPresentationTimestamp: true }, ActionUnavailableReason.TimestampUnavailable),
            _ => new(false, ActionUnavailableReason.UnknownAction)
        };
        public Task<ActionResult> SetBoundaryAsync(PlayerActionTarget target, WorkingRangeBoundary boundary, CancellationToken token)
        { Check(target, token); return host.SetBoundaryAsync(target, boundary, token); }
        public Task<ActionResult> CreateSubclipAsync(PlayerActionTarget target, CancellationToken token)
        { Check(target, token); return host.CreateSubclipAsync(target, token); }
        public Task<ActionResult> AddMarkerAsync(PlayerActionTarget target, CancellationToken token)
        { Check(target, token); return host.AddMarkerAsync(target, token); }
        public Task<ActionResult> TraverseReviewAsync(PlayerActionTarget target, TraversalDirection direction, CancellationToken token)
        {
            Check(target, token);
            if (host._reviewSet is not { } review || (direction == TraversalDirection.Previous ? !review.CanPrevious : !review.CanNext))
                return Task.FromResult(new ActionResult(ActionOutcome.NoChange));
            return host.SelectReviewAssetAsync(review.Items[review.CurrentIndex + (int)direction].Asset.AssetId!.Value, token);
        }
        public async Task<ActionResult> NavigateMarkerAsync(PlayerActionTarget target, TraversalDirection direction, CancellationToken token)
        {
            Check(target, token);
            var position = (host._retainedSteppedFrame?.Timestamp ?? host._service?.Snapshot.DisplayedTimestamp)!.Position;
            var marker = direction == TraversalDirection.Previous
                ? MarkerNavigation.Previous(host._markerItems, position) : MarkerNavigation.Next(host._markerItems, position);
            if (marker is null) return new(ActionOutcome.NoChange);
            var result = await host.SeekMarkerAsync(marker, token);
            return host.ActionTarget == target ? result : new(ActionOutcome.Superseded);
        }
        private void Check(PlayerActionTarget target, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (host.ActionTarget != target) throw new OperationCanceledException("Player session changed.");
        }
        public async Task TogglePlaybackAsync(PlayerActionTarget target, CancellationToken token)
        {
            Check(target, token);
            await host.TogglePlaybackAsync(token);
        }
        public async Task StepFrameAsync(PlayerActionTarget target, int direction, CancellationToken token)
        {
            Check(target, token);
            if (!host._frameStepQueue.IsDraining) _stepBatch = new();
            var batch = _stepBatch!;
            host._frameStepQueue.RequestStep(host.ExecutePresentedStepAsync, direction > 0, error => {
                batch.Error = error;
                host.SetStatus(error.Message);
            });
            await host._frameStepQueue.WaitUntilIdleAsync(token);
            Check(target, token);
            if (batch.Error is not null) throw batch.Error;
        }
        public void SetColorBypass(PlayerActionTarget target, bool bypass)
        {
            if (host.ActionTarget != target) return;
            host._momentaryColorBypass = bypass;
            if (bypass) host.RestoreLiveVideoSurface();
            host._service?.SetColorPipeline(host._colorPipeline, !host._colorActive || bypass);
        }
    }
}
