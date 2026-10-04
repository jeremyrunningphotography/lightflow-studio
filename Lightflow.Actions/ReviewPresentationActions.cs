namespace Lightflow.Actions;

public enum PresentationToggle { Mute, Loop, Fullscreen, Filmstrip }
public enum ReviewZoom { Fit, Half, ActualPixels, Double, Quadruple }
public enum ReviewSpeed { Eighth, Quarter, Half, Normal, Double, Quadruple }
public enum AdjustmentMode { Set, Relative }
public sealed record VolumeArguments(AdjustmentMode Mode, int Percent) : ActionArguments;
public sealed record SpeedArguments(ReviewSpeed Speed) : ActionArguments;
public sealed record ZoomArguments(ReviewZoom Zoom) : ActionArguments;
public sealed record PresentationToggleArguments(PresentationToggle Toggle) : ActionArguments;
public sealed record LevelArguments(int Direction) : ActionArguments;

/// <summary>Replaceable viewport/chrome seam; playback and persisted intent remain application-owned.</summary>
public interface IReviewPresentationPort
{
    PlayerActionContext Context { get; }
    ActionEligibility Eligibility(string actionId, ActionArguments arguments);
    Task<ActionResult> ChangeVolumeAsync(PlayerActionTarget target, VolumeArguments arguments, CancellationToken token);
    Task<ActionResult> ChangeSpeedAsync(PlayerActionTarget target, ReviewSpeed speed, CancellationToken token);
    Task<ActionResult> ChangeZoomAsync(PlayerActionTarget target, ReviewZoom zoom, CancellationToken token);
    Task<ActionResult> StepZoomAsync(PlayerActionTarget target, int direction, CancellationToken token);
    Task<ActionResult> ToggleAsync(PlayerActionTarget target, PresentationToggle toggle, CancellationToken token);
}

public sealed class ReviewPresentationActions(IReviewPresentationPort port)
{
    public const string Volume = "player.volume";
    public const string Speed = "player.review-speed";
    public const string Zoom = "viewer.zoom";
    public const string StepZoom = "viewer.step-zoom";
    public const string Toggle = "player.presentation-toggle";
    private readonly HashSet<string> _executing = [];
    private static ActionDescriptor Describe(string id, string label, ActionArgumentShape shape, bool relative = false) =>
        new(id, label, "Presentation", shape, true, Array.AsReadOnly(new[] { ActionPhase.Invoke }),
            relative ? ActionRepeatPolicy.BoundedRelative : ActionRepeatPolicy.Suppress, ActionExecutionPolicy.SingleFlight);
    public static IReadOnlyList<ActionDescriptor> Descriptors { get; } = Array.AsReadOnly(new[] {
        Describe(Volume, "Playback volume (%)", ActionArgumentShape.Volume, true),
        Describe(Speed, "Playback speed", ActionArgumentShape.ReviewSpeed),
        Describe(Zoom, "Viewer zoom / Fit", ActionArgumentShape.ReviewZoom),
        Describe(StepZoom, "Step viewer zoom", ActionArgumentShape.LevelDirection, true),
        Describe(Toggle, "Toggle review presentation", ActionArgumentShape.PresentationToggle)
    });
    public ActionEligibility Eligibility(string id, PlayerActionTarget? target, ActionArguments arguments)
    {
        if (!Valid(id, arguments)) return new(false, Descriptors.Any(d => d.Id == id) ? ActionUnavailableReason.InvalidArguments : ActionUnavailableReason.UnknownAction);
        var context = port.Context;
        if (target is null || context.Target is null) return new(false, ActionUnavailableReason.NoPlayer);
        if (target != context.Target) return new(false, ActionUnavailableReason.SourceUnavailable);
        if (!context.PlayerPresented) return new(false, ActionUnavailableReason.InactivePresentation);
        if (!context.InteractionAvailable) return new(false, ActionUnavailableReason.ModalInteraction);
        return port.Eligibility(id, arguments);
    }
    private static bool Valid(string id, ActionArguments arguments) => id switch {
        Volume => arguments is VolumeArguments { Mode: AdjustmentMode.Set, Percent: >= 0 and <= 100 } or VolumeArguments { Mode: AdjustmentMode.Relative, Percent: >= -100 and <= 100 },
        Speed => arguments is SpeedArguments s && Enum.IsDefined(s.Speed),
        Zoom => arguments is ZoomArguments z && Enum.IsDefined(z.Zoom),
        StepZoom => arguments is LevelArguments { Direction: -1 or 1 },
        Toggle => arguments is PresentationToggleArguments t && Enum.IsDefined(t.Toggle),
        _ => false
    };
    public async Task<ActionResult> InvokeAsync(ActionInvocation invocation, CancellationToken token = default)
    {
        if (invocation.Phase != ActionPhase.Invoke) return new(ActionOutcome.Ineligible, ActionUnavailableReason.InvalidPhase);
        if (invocation.InvocationId == Guid.Empty || string.IsNullOrWhiteSpace(invocation.Source.Id)) return new(ActionOutcome.Ineligible, ActionUnavailableReason.InvalidArguments);
        if (token.IsCancellationRequested) return new(ActionOutcome.Cancelled);
        if (invocation.Target is not null && invocation.Target != port.Context.Target) return new(ActionOutcome.Superseded);
        var eligibility = Eligibility(invocation.ActionId, invocation.Target, invocation.Arguments);
        if (!eligibility.Available) return new(ActionOutcome.Ineligible, eligibility.Reason);
        if (invocation.IsRepeat && (invocation.ActionId is not (Volume or StepZoom) || invocation.Arguments is VolumeArguments { Mode: AdjustmentMode.Set })) return new(ActionOutcome.NoChange);
        if (!_executing.Add(invocation.ActionId)) return new(ActionOutcome.Busy);
        try {
            var target = invocation.Target!;
            var result = await (invocation.ActionId switch {
                Volume => port.ChangeVolumeAsync(target, (VolumeArguments)invocation.Arguments, token),
                Speed => port.ChangeSpeedAsync(target, ((SpeedArguments)invocation.Arguments).Speed, token),
                Zoom => port.ChangeZoomAsync(target, ((ZoomArguments)invocation.Arguments).Zoom, token),
                StepZoom => port.StepZoomAsync(target, ((LevelArguments)invocation.Arguments).Direction, token),
                _ => port.ToggleAsync(target, ((PresentationToggleArguments)invocation.Arguments).Toggle, token)
            });
            return port.Context.Target == target ? result : new(ActionOutcome.Superseded);
        }
        catch (OperationCanceledException) { return new(invocation.Target == port.Context.Target ? ActionOutcome.Cancelled : ActionOutcome.Superseded); }
        catch (Exception error) { return new(invocation.Target == port.Context.Target ? ActionOutcome.Failed : ActionOutcome.Superseded, Diagnostic: error.Message); }
        finally { _executing.Remove(invocation.ActionId); }
    }
}
