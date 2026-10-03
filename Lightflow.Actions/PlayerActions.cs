namespace Lightflow.Actions;

/// <summary>Player semantic actions. No key dispatch, persisted bindings, decoder or Catalog ownership.</summary>
public sealed class PlayerActions(IPlayerActionPort port) : IDisposable
{
    public const string PlayPause = "player.play-pause";
    public const string StepFrame = "player.step-frame";
    public const string ColorBypass = "player.color-bypass";
    public const string SetBoundary = "player.set-boundary";
    public const string TraverseReview = "player.traverse-review";
    public const string CreateSubclip = "subclip.create-from-working-range";
    public const string AddMarker = "marker.add";
    public const string NavigateMarker = "marker.navigate";
    private static ActionDescriptor Discrete(string id, string label, ActionArgumentShape shape,
        ActionRepeatPolicy repeat = ActionRepeatPolicy.Suppress) => new(id, label, "Player", shape, true,
            Array.AsReadOnly(new[] { ActionPhase.Invoke }), repeat, ActionExecutionPolicy.SingleFlight);
    public static IReadOnlyList<ActionDescriptor> Descriptors { get; } = Array.AsReadOnly(new[] {
        new ActionDescriptor(PlayPause, "Play / Pause", "Player", ActionArgumentShape.None, true,
            Array.AsReadOnly(new[] { ActionPhase.Invoke }), ActionRepeatPolicy.Suppress, ActionExecutionPolicy.SingleFlight),
        new ActionDescriptor(StepFrame, "Step frame", "Player", ActionArgumentShape.FrameDirection, true,
            Array.AsReadOnly(new[] { ActionPhase.Invoke }), ActionRepeatPolicy.BoundedRelative, ActionExecutionPolicy.Coalesced),
        new ActionDescriptor(ColorBypass, "Compare Original (hold)", "Player", ActionArgumentShape.None, true,
            Array.AsReadOnly(new[] { ActionPhase.Begin, ActionPhase.End, ActionPhase.Cancel }), ActionRepeatPolicy.Session, ActionExecutionPolicy.Momentary),
        Discrete(SetBoundary, "Set working range boundary", ActionArgumentShape.Boundary),
        Discrete(TraverseReview, "Traverse review set", ActionArgumentShape.TraversalDirection, ActionRepeatPolicy.BoundedRelative),
        Discrete(CreateSubclip, "Create Subclip from working range", ActionArgumentShape.None),
        Discrete(AddMarker, "Add marker", ActionArgumentShape.None),
        Discrete(NavigateMarker, "Navigate markers", ActionArgumentShape.TraversalDirection, ActionRepeatPolicy.BoundedRelative)
    });
    private (ActionInputSource Source, Guid Identity, PlayerActionTarget Target)? _gesture;
    private bool _toggling;
    private bool _disposed;
    private readonly HashSet<string> _executing = [];
    private static bool IsReviewAction(string id) => id is SetBoundary or TraverseReview or CreateSubclip or AddMarker or NavigateMarker;

    public ActionEligibility Eligibility(string actionId, PlayerActionTarget? target)
    {
        var context = port.Context;
        if (!Descriptors.Any(d => d.Id == actionId)) return new(false, ActionUnavailableReason.UnknownAction);
        if (_disposed || target is null || context.Target is null) return new(false, ActionUnavailableReason.NoPlayer);
        if (target != context.Target) return new(false, ActionUnavailableReason.SourceUnavailable);
        if (!context.PlayerPresented) return new(false, ActionUnavailableReason.InactivePresentation);
        if (!context.InteractionAvailable) return new(false, ActionUnavailableReason.ModalInteraction);
        if (actionId == TraverseReview ? !context.ReviewReady : !context.SourceReady) return new(false, ActionUnavailableReason.SourceUnavailable);
        if (IsReviewAction(actionId)) return port.ReviewEligibility(actionId);
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
            (descriptor.Arguments == ActionArgumentShape.FrameDirection && invocation.Arguments is not FrameStepArguments { Direction: -1 or 1 }) ||
            (descriptor.Arguments == ActionArgumentShape.Boundary && invocation.Arguments is not SetBoundaryArguments { Boundary: WorkingRangeBoundary.In or WorkingRangeBoundary.Out }) ||
            (descriptor.Arguments == ActionArgumentShape.TraversalDirection && invocation.Arguments is not TraverseArguments { Direction: TraversalDirection.Previous or TraversalDirection.Next }))
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
        if (IsReviewAction(invocation.ActionId))
        {
            if (!_executing.Add(invocation.ActionId)) return new(ActionOutcome.Busy);
            try
            {
                return await (invocation.ActionId switch {
                    SetBoundary => port.SetBoundaryAsync(target, ((SetBoundaryArguments)invocation.Arguments).Boundary, token),
                    TraverseReview => port.TraverseReviewAsync(target, ((TraverseArguments)invocation.Arguments).Direction, token),
                    CreateSubclip => port.CreateSubclipAsync(target, token),
                    AddMarker => port.AddMarkerAsync(target, token),
                    _ => port.NavigateMarkerAsync(target, ((TraverseArguments)invocation.Arguments).Direction, token)
                });
            }
            catch (OperationCanceledException) { return new(port.Context.Target == target ? ActionOutcome.Cancelled : ActionOutcome.Superseded); }
            catch (Exception error) { return new(port.Context.Target == target ? ActionOutcome.Failed : ActionOutcome.Superseded, Diagnostic: error.Message); }
            finally { _executing.Remove(invocation.ActionId); }
        }
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
