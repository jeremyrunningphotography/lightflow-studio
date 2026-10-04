namespace Lightflow.Actions;

public enum ActionPhase { Invoke, Begin, End, Cancel }
public enum ActionRepeatPolicy { Suppress, BoundedRelative, Session }
public enum ActionExecutionPolicy { SingleFlight, Coalesced, Momentary, Serialized }
public enum ActionArgumentShape { None, FrameDirection, Boundary, TraversalDirection, BrowserNavigation, Rating, Flag, ColorLabel, BrowserOpen, Volume, ReviewSpeed, ReviewZoom, PresentationToggle, LevelDirection, ExportEntry, PanelSurface }
public enum WorkingRangeBoundary { In, Out }
public enum TraversalDirection { Previous = -1, Next = 1 }
public enum ActionInputKind { Transport, Keyboard, Controller }
public enum ActionOutcome { Completed, NoChange, Ineligible, Cancelled, Superseded, Busy, Failed }
public enum ActionUnavailableReason { None, UnknownAction, InvalidArguments, InvalidPhase, NoPlayer, SourceUnavailable, InactivePresentation, ModalInteraction, ColorInactive, WorkingRangeUnavailable, ReviewSetUnavailable, MarkerServiceUnavailable, TimestampUnavailable, NoBrowser, SelectionUnavailable, AudioUnavailable, ViewportUnavailable, NoSelection, SurfaceUnavailable, ExportUnavailable, OperationInProgress }
public sealed record ActionDescriptor(string Id, string Label, string Category, ActionArgumentShape Arguments,
    bool Bindable, IReadOnlyList<ActionPhase> Phases, ActionRepeatPolicy Repeat, ActionExecutionPolicy Execution);
public abstract record ActionArguments;
public sealed record NoActionArguments : ActionArguments
{
    public static readonly NoActionArguments Instance = new();
}
/// <summary>One previous (-1) or next (+1) frame request. Accumulation is bounded by the Player queue.</summary>
public sealed record FrameStepArguments(int Direction) : ActionArguments;
public sealed record SetBoundaryArguments(WorkingRangeBoundary Boundary) : ActionArguments;
public sealed record TraverseArguments(TraversalDirection Direction) : ActionArguments;
public sealed record ActionInputSource(string Id, ActionInputKind Kind);
/// <summary>Transient target identity; never a visual object or a persisted device identity.</summary>
public sealed record PlayerActionTarget(Guid SessionId, long Generation, Guid? AssetId);
public sealed record PlayerActionContext(PlayerActionTarget? Target, bool PlayerPresented, bool InteractionAvailable,
    bool SourceReady, bool ColorActive, bool ReviewReady = false);
public sealed record ActionInvocation(string ActionId, ActionArguments Arguments, ActionInputSource Source,
    Guid InvocationId, PlayerActionTarget? Target, ActionPhase Phase = ActionPhase.Invoke, bool IsRepeat = false);
public sealed record ActionResult(ActionOutcome Outcome, ActionUnavailableReason Reason = ActionUnavailableReason.None, string? Diagnostic = null);
public sealed record ActionEligibility(bool Available, ActionUnavailableReason Reason = ActionUnavailableReason.None);

/// <summary>
/// Narrow application/presentation port. Implementations retain their authoritative lease, range and presentation
/// paths. The Windows implementation owns WPF retained frames; this interface does not make playback presentation neutral.
/// Calls and session lifecycle are serialized by the application's interaction dispatcher.
/// </summary>
public interface IPlayerActionPort
{
    PlayerActionContext Context { get; }
    Task TogglePlaybackAsync(PlayerActionTarget target, CancellationToken token);
    /// <summary>Completes when the bounded, coalesced batch settles; not a promise of one decoded step per input event.</summary>
    Task StepFrameAsync(PlayerActionTarget target, int direction, CancellationToken token);
    /// <summary>Recompute the current authoritative pipeline on false; do not restore a captured assignment.</summary>
    void SetColorBypass(PlayerActionTarget target, bool bypass);
    ActionEligibility ReviewEligibility(string actionId);
    Task<ActionResult> SetBoundaryAsync(PlayerActionTarget target, WorkingRangeBoundary boundary, CancellationToken token);
    Task<ActionResult> TraverseReviewAsync(PlayerActionTarget target, TraversalDirection direction, CancellationToken token);
    Task<ActionResult> CreateSubclipAsync(PlayerActionTarget target, CancellationToken token);
    Task<ActionResult> AddMarkerAsync(PlayerActionTarget target, CancellationToken token);
    Task<ActionResult> NavigateMarkerAsync(PlayerActionTarget target, TraversalDirection direction, CancellationToken token);
}
