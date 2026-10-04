using Lightflow.Actions;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class ReviewPresentationPolicyTests
{
    private static ActionInvocation Call(Port port, string id, ActionArguments arguments) =>
        new(id, arguments, new("presentation-controller", ActionInputKind.Controller), Guid.NewGuid(), port.Context.Target);
    [Fact]
    public async Task PendingSpeedIsSingleFlightAndErrorsCancellationStayStructured()
    {
        var release = new TaskCompletionSource<ActionResult>(); var port = new Port { Completion = release.Task };
        var actions = new ReviewPresentationActions(port); var call = Call(port, ReviewPresentationActions.Speed, new SpeedArguments(ReviewSpeed.Double));
        var pending = actions.InvokeAsync(call);
        Assert.Equal(ActionOutcome.Busy, (await actions.InvokeAsync(call with { InvocationId = Guid.NewGuid() })).Outcome);
        release.SetResult(new(ActionOutcome.Completed)); Assert.Equal(ActionOutcome.Completed, (await pending).Outcome);
        port.Completion = Task.FromException<ActionResult>(new InvalidOperationException("backend"));
        Assert.Equal(ActionOutcome.Failed, (await actions.InvokeAsync(call)).Outcome);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        var calls = port.Calls; Assert.Equal(ActionOutcome.Cancelled, (await actions.InvokeAsync(call, cancelled.Token)).Outcome); Assert.Equal(calls, port.Calls);
        port.Completion = Task.FromCanceled<ActionResult>(cancelled.Token);
        Assert.Equal(ActionOutcome.Cancelled, (await actions.InvokeAsync(call)).Outcome);
        release = new TaskCompletionSource<ActionResult>(); port.Completion = release.Task;
        pending = actions.InvokeAsync(call);
        port.Context = port.Context with { Target = port.Context.Target! with { Generation = 2 } };
        release.SetResult(new(ActionOutcome.Completed)); Assert.Equal(ActionOutcome.Superseded, (await pending).Outcome);
        Assert.Equal(ActionOutcome.Superseded, (await actions.InvokeAsync(call)).Outcome);
    }
    [Fact]
    public async Task TypedValuesAndPhasesAreValidatedBeforeAnyPresentationMutation()
    {
        var port = new Port(); var actions = new ReviewPresentationActions(port);
        foreach (var (id, arguments) in new (string, ActionArguments)[] {
            (ReviewPresentationActions.Speed, new SpeedArguments((ReviewSpeed)6)),
            (ReviewPresentationActions.Zoom, new ZoomArguments((ReviewZoom)6)),
            (ReviewPresentationActions.StepZoom, new LevelArguments(0)),
            (ReviewPresentationActions.Toggle, new PresentationToggleArguments((PresentationToggle)6)),
            (ReviewPresentationActions.Volume, NoActionArguments.Instance) })
            Assert.Equal(ActionUnavailableReason.InvalidArguments, (await actions.InvokeAsync(Call(port, id, arguments))).Reason);
        Assert.Equal(ActionUnavailableReason.UnknownAction, (await actions.InvokeAsync(Call(port, "missing", NoActionArguments.Instance))).Reason);
        Assert.Equal(ActionUnavailableReason.InvalidPhase, (await actions.InvokeAsync(Call(port, ReviewPresentationActions.Zoom, new ZoomArguments(ReviewZoom.Fit)) with { Phase = ActionPhase.Begin })).Reason);
        Assert.Equal(0, port.Calls);
    }
    [Fact]
    public async Task OnlyBoundedRelativeInputsAcceptRepeats()
    {
        var port = new Port(); var actions = new ReviewPresentationActions(port);
        foreach (var (id, arguments) in new (string, ActionArguments)[] {
            (ReviewPresentationActions.Speed, new SpeedArguments(ReviewSpeed.Normal)),
            (ReviewPresentationActions.Zoom, new ZoomArguments(ReviewZoom.Fit)),
            (ReviewPresentationActions.Toggle, new PresentationToggleArguments(PresentationToggle.Mute)),
            (ReviewPresentationActions.Volume, new VolumeArguments(AdjustmentMode.Set, 50)) })
            Assert.Equal(ActionOutcome.NoChange, (await actions.InvokeAsync(Call(port, id, arguments) with { IsRepeat = true })).Outcome);
        Assert.Equal(0, port.Calls);
        Assert.Equal(ActionOutcome.Completed, (await actions.InvokeAsync(Call(port, ReviewPresentationActions.Volume, new VolumeArguments(AdjustmentMode.Relative, 5)) with { IsRepeat = true })).Outcome);
        Assert.Equal(ActionOutcome.Completed, (await actions.InvokeAsync(Call(port, ReviewPresentationActions.StepZoom, new LevelArguments(-1)) with { IsRepeat = true })).Outcome);
        Assert.Equal(2, port.Calls);
    }
    private sealed class Port : IReviewPresentationPort
    {
        public PlayerActionContext Context { get; set; } = new(new(Guid.NewGuid(), 1, Guid.NewGuid()), true, true, true, false);
        public Task<ActionResult> Completion { get; set; } = Task.FromResult(new ActionResult(ActionOutcome.Completed));
        public int Calls { get; private set; }
        private Task<ActionResult> Execute() { Calls++; return Completion; }
        public ActionEligibility Eligibility(string id, ActionArguments arguments) => new(true);
        public Task<ActionResult> ChangeVolumeAsync(PlayerActionTarget target, VolumeArguments arguments, CancellationToken token) => Execute();
        public Task<ActionResult> ChangeSpeedAsync(PlayerActionTarget target, ReviewSpeed speed, CancellationToken token) => Execute();
        public Task<ActionResult> ChangeZoomAsync(PlayerActionTarget target, ReviewZoom zoom, CancellationToken token) => Execute();
        public Task<ActionResult> StepZoomAsync(PlayerActionTarget target, int direction, CancellationToken token) => Execute();
        public Task<ActionResult> ToggleAsync(PlayerActionTarget target, PresentationToggle toggle, CancellationToken token) => Execute();
    }
}
