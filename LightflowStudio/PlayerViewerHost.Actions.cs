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
    private static readonly ActionInputSource KeyboardActionSource = new("player.keyboard", ActionInputKind.Keyboard);
    private static readonly ActionInputSource TransportActionSource = new("player.transport", ActionInputKind.Transport);
    internal Func<bool>? ActionPresentationActive { get; set; }
    internal PlayerActions SemanticActions => _actions;
    internal PlayerActionTarget? ActionTarget => _service is null ? null : new(_actionSessionId, _generation, _currentAsset?.AssetId);

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
        window.Deactivated += ActionWindowDeactivated;
        window.IsEnabledChanged += ActionWindowEnabledChanged;
        System.Windows.Interop.ComponentDispatcher.EnterThreadModal += ActionThreadModal;
    }
    private void DetachActionWindow()
    {
        if (_actionWindow is not { } window) return;
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
        var result = await _actions.InvokeAsync(new(action, arguments, TransportActionSource, Guid.NewGuid(), ActionTarget));
        if (result.Outcome == ActionOutcome.Failed) SetStatus(result.Diagnostic ?? "Player action failed.");
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
        var result = await _actions.InvokeAsync(invocation);
        if (result.Outcome == ActionOutcome.Failed) SetStatus(result.Diagnostic ?? "Player action failed.");
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
            host._colorActive);
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
