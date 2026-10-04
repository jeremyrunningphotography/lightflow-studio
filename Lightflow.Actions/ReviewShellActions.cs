namespace Lightflow.Actions;

public enum ExportEntry { BrowserVideos, BrowserSubclips, PlayerVideo, PlayerSelectedSubclips, PlayerAllSubclips }
public enum ReviewPanelSurface { Inspector, Jobs, Subclips, VisualIndex }
public sealed record ExportEntryArguments(ExportEntry Entry) : ActionArguments;
public sealed record PanelSurfaceArguments(ReviewPanelSurface Surface) : ActionArguments;
/// <summary>Transient application context version, independent of keys, controls and device identity.</summary>
public sealed record ReviewShellTarget(Guid SessionId, long Revision);
public sealed record ReviewShellContext(ReviewShellTarget Target, bool HomePresented, bool InteractionAvailable);
public sealed record ReviewShellInvocation(string ActionId, ActionArguments Arguments, ActionInputSource Source,
    Guid InvocationId, ReviewShellTarget Target, ActionPhase Phase = ActionPhase.Invoke, bool IsRepeat = false);
public interface IReviewShellPort
{
    ReviewShellContext Context { get; }
    ActionEligibility Eligibility(string actionId, ActionArguments arguments);
    Task<ActionResult> StepThumbnailsAsync(ReviewShellTarget target, int direction, CancellationToken token);
    Task<ActionResult> TogglePanelAsync(ReviewShellTarget target, CancellationToken token);
    Task<ActionResult> ShowPanelAsync(ReviewShellTarget target, ReviewPanelSurface surface, CancellationToken token);
    /// <summary>Completes when existing preparation and configuration modal return; never means a Job was queued.</summary>
    Task<ActionResult> OpenExportAsync(ReviewShellTarget target, ExportEntry entry, CancellationToken token);
}
public sealed class ReviewShellActions(IReviewShellPort port)
{
    public const string ThumbnailSize = "browser.step-thumbnail-size";
    public const string TogglePanel = "review.toggle-right-panel";
    public const string ShowPanel = "review.show-panel";
    public const string Export = "export.open";
    private readonly HashSet<string> _executing = [];
    public static IReadOnlyList<ActionDescriptor> Descriptors { get; } = Array.AsReadOnly(new[] {
        Describe(ThumbnailSize, "Step Browser thumbnail size", ActionArgumentShape.LevelDirection, true),
        Describe(TogglePanel, "Toggle Right Panel", ActionArgumentShape.None),
        Describe(ShowPanel, "Show review panel", ActionArgumentShape.PanelSurface),
        Describe(Export, "Open Export configuration", ActionArgumentShape.ExportEntry)
    });
    private static ActionDescriptor Describe(string id, string label, ActionArgumentShape shape, bool relative = false) =>
        new(id, label, "Review", shape, true, Array.AsReadOnly(new[] { ActionPhase.Invoke }),
            relative ? ActionRepeatPolicy.BoundedRelative : ActionRepeatPolicy.Suppress, ActionExecutionPolicy.SingleFlight);
    private static bool Valid(string id, ActionArguments arguments) => id switch {
        ThumbnailSize => arguments is LevelArguments { Direction: -1 or 1 },
        TogglePanel => arguments is NoActionArguments,
        ShowPanel => arguments is PanelSurfaceArguments p && Enum.IsDefined(p.Surface),
        Export => arguments is ExportEntryArguments e && Enum.IsDefined(e.Entry),
        _ => false
    };
    public ActionEligibility Eligibility(string id, ReviewShellTarget target, ActionArguments arguments)
    {
        if (!Valid(id, arguments)) return new(false, Descriptors.Any(d => d.Id == id) ? ActionUnavailableReason.InvalidArguments : ActionUnavailableReason.UnknownAction);
        var context = port.Context;
        if (context.Target != target) return new(false, ActionUnavailableReason.SourceUnavailable);
        if (!context.HomePresented) return new(false, ActionUnavailableReason.InactivePresentation);
        if (!context.InteractionAvailable) return new(false, ActionUnavailableReason.ModalInteraction);
        return port.Eligibility(id, arguments);
    }
    public async Task<ActionResult> InvokeAsync(ReviewShellInvocation invocation, CancellationToken token = default)
    {
        if (invocation.Phase != ActionPhase.Invoke) return new(ActionOutcome.Ineligible, ActionUnavailableReason.InvalidPhase);
        if (invocation.InvocationId == Guid.Empty || string.IsNullOrWhiteSpace(invocation.Source.Id)) return new(ActionOutcome.Ineligible, ActionUnavailableReason.InvalidArguments);
        if (token.IsCancellationRequested) return new(ActionOutcome.Cancelled);
        if (port.Context.Target != invocation.Target) return new(ActionOutcome.Superseded);
        var eligibility = Eligibility(invocation.ActionId, invocation.Target, invocation.Arguments);
        if (!eligibility.Available) return new(eligibility.Reason == ActionUnavailableReason.OperationInProgress ? ActionOutcome.Busy : ActionOutcome.Ineligible, eligibility.Reason);
        if (invocation.IsRepeat && invocation.ActionId != ThumbnailSize) return new(ActionOutcome.NoChange);
        if (!_executing.Add(invocation.ActionId)) return new(ActionOutcome.Busy);
        try {
            return await (invocation.ActionId switch {
                ThumbnailSize => port.StepThumbnailsAsync(invocation.Target, ((LevelArguments)invocation.Arguments).Direction, token),
                TogglePanel => port.TogglePanelAsync(invocation.Target, token),
                ShowPanel => port.ShowPanelAsync(invocation.Target, ((PanelSurfaceArguments)invocation.Arguments).Surface, token),
                _ => port.OpenExportAsync(invocation.Target, ((ExportEntryArguments)invocation.Arguments).Entry, token)
            });
        }
        catch (OperationCanceledException) { return new(port.Context.Target == invocation.Target ? ActionOutcome.Cancelled : ActionOutcome.Superseded); }
        catch (Exception error) { return new(port.Context.Target == invocation.Target ? ActionOutcome.Failed : ActionOutcome.Superseded, Diagnostic: error.Message); }
        finally { _executing.Remove(invocation.ActionId); }
    }
}
