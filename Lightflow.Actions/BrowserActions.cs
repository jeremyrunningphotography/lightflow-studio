namespace Lightflow.Actions;

public enum BrowserScopeKind { Folder, Collection, SmartCollection }
public sealed record BrowserScopeIdentity(BrowserScopeKind Kind, Guid Id, string RelativeFolder = "", bool IncludeSubfolders = false);
public sealed record BrowserActionTarget(Guid SessionId, long Generation, BrowserScopeIdentity Scope, long ProjectionGeneration, long PresentationGeneration = 0);
public sealed record BrowserActionContext(BrowserActionTarget? Target, bool Presented, bool InteractionAvailable,
    IReadOnlyList<Guid> SelectedAssetIds, Guid? CurrentAssetId, bool HasItems);
public enum BrowserMovement { Previous, Next, First, Last }
public enum ClassificationFlag { Rejected = -1, Unflagged = 0, Picked = 1 }
public enum ClassificationColorLabel { Red = 1, Yellow, Green, Blue, Purple }
public sealed record NavigateSelectionArguments(BrowserMovement Movement, bool Extend = false, int Distance = 1) : ActionArguments;
public sealed record OpenBrowserArguments(Guid AssetId) : ActionArguments;
public sealed record SetRatingArguments(int Rating, bool ToggleCurrent = false) : ActionArguments;
public sealed record SetFlagArguments(ClassificationFlag Flag) : ActionArguments;
public sealed record StepFlagArguments(TraversalDirection Direction) : ActionArguments;
public sealed record SetColorLabelArguments(ClassificationColorLabel? Label) : ActionArguments;
public sealed record BrowserActionInvocation(string ActionId, ActionArguments Arguments, ActionInputSource Source,
    Guid InvocationId, BrowserActionTarget? Target, ActionPhase Phase = ActionPhase.Invoke, bool IsRepeat = false);

/// <summary>Calls run on the application interaction dispatcher. Focus admission belongs to the input adapter.</summary>
public interface IBrowserActionPort
{
    BrowserActionContext Context { get; }
    Task<ActionResult> NavigateAsync(BrowserActionTarget target, NavigateSelectionArguments arguments, CancellationToken token);
    Task<ActionResult> OpenAsync(BrowserActionTarget target, OpenBrowserArguments? arguments, CancellationToken token);
    Task<ActionResult> ClassifyAsync(BrowserActionTarget target, IReadOnlyList<Guid> capturedAssetIds,
        ActionArguments arguments, CancellationToken token);
}

public sealed class BrowserActions(IBrowserActionPort port)
{
    public const string NavigateSelection = "browser.navigate-selection";
    public const string OpenCurrent = "browser.open-current";
    public const string SetRating = "asset.set-rating";
    public const string SetFlag = "asset.set-flag";
    public const string StepFlag = "asset.step-flag";
    public const string SetColorLabel = "asset.set-color-label";
    private readonly HashSet<string> _relativeExecuting = [];
    private (BrowserActionTarget Target, Guid Invocation)? _opening;
    private static ActionDescriptor Describe(string id, string label, ActionArgumentShape shape, bool relative = false) =>
        new(id, label, "Browser", shape, true, Array.AsReadOnly(new[] { ActionPhase.Invoke }),
            relative ? ActionRepeatPolicy.BoundedRelative : ActionRepeatPolicy.Suppress, id is SetRating or SetFlag or SetColorLabel ? ActionExecutionPolicy.Serialized : ActionExecutionPolicy.SingleFlight);
    public static IReadOnlyList<ActionDescriptor> Descriptors { get; } = Array.AsReadOnly(new[] {
        Describe(NavigateSelection, "Navigate Browser selection", ActionArgumentShape.BrowserNavigation, true),
        Describe(OpenCurrent, "Open Browser media", ActionArgumentShape.BrowserOpen),
        Describe(SetRating, "Set Asset rating", ActionArgumentShape.Rating),
        Describe(SetFlag, "Set Asset flag", ActionArgumentShape.Flag),
        Describe(StepFlag, "Step Asset flag", ActionArgumentShape.TraversalDirection, true),
        Describe(SetColorLabel, "Set Asset Color label", ActionArgumentShape.ColorLabel)
    });
    public ActionEligibility Eligibility(string id, BrowserActionTarget? target)
    {
        var context = port.Context;
        if (!Descriptors.Any(d => d.Id == id)) return new(false, ActionUnavailableReason.UnknownAction);
        if (target is null || context.Target is null) return new(false, ActionUnavailableReason.NoBrowser);
        if (target != context.Target) return new(false, ActionUnavailableReason.SourceUnavailable);
        if (!context.Presented) return new(false, ActionUnavailableReason.InactivePresentation);
        if (!context.InteractionAvailable) return new(false, ActionUnavailableReason.ModalInteraction);
        if (id is NavigateSelection or OpenCurrent ? !context.HasItems : context.SelectedAssetIds.Count == 0)
            return new(false, ActionUnavailableReason.SelectionUnavailable);
        return new(true);
    }
    public async Task<ActionResult> InvokeAsync(BrowserActionInvocation invocation, CancellationToken token = default)
    {
        var descriptor = Descriptors.FirstOrDefault(d => d.Id == invocation.ActionId);
        if (descriptor is null) return new(ActionOutcome.Ineligible, ActionUnavailableReason.UnknownAction);
        if (invocation.Phase != ActionPhase.Invoke) return new(ActionOutcome.Ineligible, ActionUnavailableReason.InvalidPhase);
        var valid = invocation.Arguments switch {
            NavigateSelectionArguments a => invocation.ActionId == NavigateSelection && Enum.IsDefined(a.Movement) && a.Distance is >= 1 and <= 10000,
            SetRatingArguments a => invocation.ActionId == SetRating && a.Rating is >= 0 and <= 5,
            SetFlagArguments a => invocation.ActionId == SetFlag && Enum.IsDefined(a.Flag),
            StepFlagArguments a => invocation.ActionId == StepFlag && Enum.IsDefined(a.Direction),
            SetColorLabelArguments a => invocation.ActionId == SetColorLabel && (a.Label is null || Enum.IsDefined(a.Label.Value)),
            OpenBrowserArguments a => invocation.ActionId == OpenCurrent && a.AssetId != Guid.Empty,
            NoActionArguments => invocation.ActionId == OpenCurrent,
            _ => false
        };
        if (!valid || invocation.InvocationId == Guid.Empty || string.IsNullOrWhiteSpace(invocation.Source.Id))
            return new(ActionOutcome.Ineligible, ActionUnavailableReason.InvalidArguments);
        if (token.IsCancellationRequested) return new(ActionOutcome.Cancelled);
        if (invocation.Target is not null && invocation.Target != port.Context.Target) return new(ActionOutcome.Superseded);
        var eligible = Eligibility(invocation.ActionId, invocation.Target);
        if (!eligible.Available) return new(ActionOutcome.Ineligible, eligible.Reason);
        if (invocation.IsRepeat && descriptor.Repeat == ActionRepeatPolicy.Suppress) return new(ActionOutcome.NoChange);
        var relative = invocation.ActionId is NavigateSelection or StepFlag;
        if (relative && _relativeExecuting.Contains(invocation.ActionId) || invocation.ActionId == OpenCurrent && _opening?.Target == invocation.Target) return new(ActionOutcome.Busy);
        // Freeze selection before the first await; durable work retains these identities through context replacement.
        var ids = port.Context.SelectedAssetIds.Distinct().ToArray();
        var target = invocation.Target!;
        if (relative) _relativeExecuting.Add(invocation.ActionId);
        if (invocation.ActionId == OpenCurrent) _opening = (target, invocation.InvocationId);
        try {
            return await (invocation.ActionId switch {
                NavigateSelection => port.NavigateAsync(target, (NavigateSelectionArguments)invocation.Arguments, token),
                OpenCurrent => port.OpenAsync(target, invocation.Arguments as OpenBrowserArguments, token),
                _ => port.ClassifyAsync(target, ids, invocation.Arguments, token)
            });
        }
        catch (OperationCanceledException) { return new(port.Context.Target == target ? ActionOutcome.Cancelled : ActionOutcome.Superseded); }
        catch (Exception e) { return new(port.Context.Target == target ? ActionOutcome.Failed : ActionOutcome.Superseded, Diagnostic: e.Message); }
        finally { if (relative) _relativeExecuting.Remove(invocation.ActionId); if (invocation.ActionId == OpenCurrent && _opening?.Invocation == invocation.InvocationId) _opening = null; }
    }
}
