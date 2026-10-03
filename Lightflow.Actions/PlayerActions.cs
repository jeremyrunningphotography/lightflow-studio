namespace Lightflow.Actions;

/// <summary>First Player vertical slice. No key dispatch, persisted bindings, decoder or Catalog ownership.</summary>
public sealed class PlayerActions(IPlayerActionPort port) : IDisposable
{
    public const string PlayPause = "player.play-pause";
    public const string StepFrame = "player.step-frame";
    public const string ColorBypass = "player.color-bypass";
    public static IReadOnlyList<ActionDescriptor> Descriptors { get; } = Array.AsReadOnly(new[] {
        new ActionDescriptor(PlayPause, "Play / Pause", "Player", ActionArgumentShape.None, true,
            Array.AsReadOnly(new[] { ActionPhase.Invoke }), ActionRepeatPolicy.Suppress, ActionExecutionPolicy.SingleFlight),
        new ActionDescriptor(StepFrame, "Step frame", "Player", ActionArgumentShape.FrameDirection, true,
            Array.AsReadOnly(new[] { ActionPhase.Invoke }), ActionRepeatPolicy.BoundedRelative, ActionExecutionPolicy.Coalesced),
        new ActionDescriptor(ColorBypass, "Compare Original (hold)", "Player", ActionArgumentShape.None, true,
            Array.AsReadOnly(new[] { ActionPhase.Begin, ActionPhase.End, ActionPhase.Cancel }), ActionRepeatPolicy.Session, ActionExecutionPolicy.Momentary)
    });
    private (ActionInputSource Source, Guid Identity, PlayerActionTarget Target)? _gesture;
    private bool _toggling;
    private bool _disposed;

    public ActionEligibility Eligibility(string actionId, PlayerActionTarget? target)
    {
        var context = port.Context;
        if (!Descriptors.Any(d => d.Id == actionId)) return new(false, ActionUnavailableReason.UnknownAction);
        if (_disposed || target is null || context.Target is null) return new(false, ActionUnavailableReason.NoPlayer);
        if (target != context.Target) return new(false, ActionUnavailableReason.SourceUnavailable);
        if (!context.PlayerPresented) return new(false, ActionUnavailableReason.InactivePresentation);
        if (!context.InteractionAvailable) return new(false, ActionUnavailableReason.ModalInteraction);
        if (!context.SourceReady) return new(false, ActionUnavailableReason.SourceUnavailable);
        if (actionId == ColorBypass && !context.ColorActive) return new(false, ActionUnavailableReason.ColorInactive);
        return new(true);
    }

    public async Task<ActionResult> InvokeAsync(ActionInvocation invocation, CancellationToken token = default)
    {
        var descriptor = Descriptors.FirstOrDefault(d => d.Id == invocation.ActionId);
        if (descriptor is null) return new(ActionOutcome.Ineligible, ActionUnavailableReason.UnknownAction);
        if (!descriptor.Phases.Contains(invocation.Phase)) return new(ActionOutcome.Ineligible, ActionUnavailableReason.InvalidPhase);
        if (invocation.InvocationId == Guid.Empty || string.IsNullOrWhiteSpace(invocation.Source.Id) ||
            (descriptor.Arguments == ActionArgumentShape.None && invocation.Arguments is not NoActionArguments) ||
            (descriptor.Arguments == ActionArgumentShape.FrameDirection && invocation.Arguments is not FrameStepArguments { Direction: -1 or 1 }))
            return new(ActionOutcome.Ineligible, ActionUnavailableReason.InvalidArguments);
        // Release retains its original target even after eligibility or current presentation changes.
        if (invocation.ActionId == ColorBypass && invocation.Phase is ActionPhase.End or ActionPhase.Cancel)
        {
            if (_gesture is not { } gesture || gesture.Source != invocation.Source || gesture.Identity != invocation.InvocationId)
                return new(ActionOutcome.NoChange);
            try
            {
                EndGesture();
                return new(invocation.Phase == ActionPhase.Cancel ? ActionOutcome.Cancelled : ActionOutcome.Completed);
            }
            catch (Exception error) { return new(ActionOutcome.Failed, Diagnostic: error.Message); }
        }
        if (token.IsCancellationRequested) return new(ActionOutcome.Cancelled);
        if (invocation.Target is not null && port.Context.Target != invocation.Target) return new(ActionOutcome.Superseded);
        var eligibility = Eligibility(invocation.ActionId, invocation.Target);
        if (!eligibility.Available) return new(ActionOutcome.Ineligible, eligibility.Reason);
        if (invocation.IsRepeat && descriptor.Repeat is ActionRepeatPolicy.Suppress or ActionRepeatPolicy.Session) return new(ActionOutcome.NoChange);
        var target = invocation.Target!;
        if (invocation.ActionId == ColorBypass)
        {
            if (_gesture is { } existing)
                return new(existing.Source == invocation.Source && existing.Identity == invocation.InvocationId ? ActionOutcome.NoChange : ActionOutcome.Busy);
            try
            {
                port.SetColorBypass(target, true);
                _gesture = (invocation.Source, invocation.InvocationId, target);
                return new(ActionOutcome.Completed);
            }
            catch (Exception error)
            {
                // A presentation adapter may fail after applying part of the transient override.
                try { port.SetColorBypass(target, false); } catch { }
                return new(ActionOutcome.Failed, Diagnostic: error.Message);
            }
        }
        if (invocation.ActionId == PlayPause && _toggling) return new(ActionOutcome.Busy);
        try
        {
            if (invocation.ActionId == PlayPause)
            {
                _toggling = true;
                await port.TogglePlaybackAsync(target, token);
            }
            else await port.StepFrameAsync(target, ((FrameStepArguments)invocation.Arguments).Direction, token);
            return new(port.Context.Target == target ? ActionOutcome.Completed : ActionOutcome.Superseded);
        }
        catch (OperationCanceledException) { return new(port.Context.Target == target ? ActionOutcome.Cancelled : ActionOutcome.Superseded); }
        catch (Exception error) { return new(port.Context.Target == target ? ActionOutcome.Failed : ActionOutcome.Superseded, Diagnostic: error.Message); }
        finally { if (invocation.ActionId == PlayPause) _toggling = false; }
    }

    public ActionResult CancelSource(ActionInputSource source)
    {
        return _gesture?.Source == source ? CancelGestures() : new(ActionOutcome.NoChange);
    }
    public ActionResult CancelGestures()
    {
        if (_gesture is null) return new(ActionOutcome.NoChange);
        try { EndGesture(); return new(ActionOutcome.Cancelled); }
        catch (Exception error) { return new(ActionOutcome.Failed, Diagnostic: error.Message); }
    }
    private void EndGesture()
    {
        if (_gesture is not { } gesture) return;
        _gesture = null;
        port.SetColorBypass(gesture.Target, false);
    }
    public void Dispose() { if (_disposed) return; CancelGestures(); _disposed = true; }
}
