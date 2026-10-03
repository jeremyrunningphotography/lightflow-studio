using Lightflow.Actions;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class PlayerSemanticActionTests
{
    private static readonly ActionInputSource Controller = new("fake-controller", ActionInputKind.Controller);
    private static ActionInvocation Call(FakePort port, string action, ActionArguments? arguments = null,
        ActionPhase phase = ActionPhase.Invoke, Guid? identity = null, bool repeat = false) =>
        new(action, arguments ?? NoActionArguments.Instance, Controller, identity ?? Guid.NewGuid(), port.Context.Target, phase, repeat);

    [Fact]
    public void MetadataIsDiscoverableAndAssemblyHasNoPlatformReferences()
    {
        Assert.Equal(3, PlayerActions.Descriptors.Count);
        Assert.Equal(3, PlayerActions.Descriptors.Select(x => x.Id).Distinct().Count());
        Assert.All(PlayerActions.Descriptors, action => { Assert.True(action.Bindable); Assert.NotEmpty(action.Label); Assert.Equal("Player", action.Category); });
        Assert.Equal(ActionRepeatPolicy.BoundedRelative, PlayerActions.Descriptors.Single(x => x.Id == PlayerActions.StepFrame).Repeat);
        Assert.DoesNotContain(typeof(PlayerActions).Assembly.GetReferencedAssemblies(), reference =>
            reference.Name!.Contains("Windows", StringComparison.OrdinalIgnoreCase) || reference.Name.Contains("Presentation", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task DirectControllerTogglesSamePortAndSuppressesRepeat()
    {
        var port = new FakePort(); using var actions = new PlayerActions(port);
        Assert.Equal(ActionOutcome.Completed, (await actions.InvokeAsync(Call(port, PlayerActions.PlayPause))).Outcome);
        Assert.True(port.Playing);
        Assert.Equal(ActionOutcome.NoChange, (await actions.InvokeAsync(Call(port, PlayerActions.PlayPause, repeat: true))).Outcome);
        Assert.True(port.Playing);
        await actions.InvokeAsync(Call(port, PlayerActions.PlayPause)); Assert.False(port.Playing); Assert.Equal(2, port.ToggleCount);
    }

    [Fact]
    public async Task ToggleBusyFailureAndCancellationAreTruthful()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var port = new FakePort { BeforeToggle = () => release.Task }; using var actions = new PlayerActions(port);
        var first = actions.InvokeAsync(Call(port, PlayerActions.PlayPause));
        Assert.Equal(ActionOutcome.Busy, (await actions.InvokeAsync(Call(port, PlayerActions.PlayPause))).Outcome);
        release.SetResult(); Assert.Equal(ActionOutcome.Completed, (await first).Outcome);
        port.BeforeToggle = () => throw new InvalidOperationException("failed port");
        Assert.Equal(ActionOutcome.Failed, (await actions.InvokeAsync(Call(port, PlayerActions.PlayPause))).Outcome);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.Equal(ActionOutcome.Cancelled, (await actions.InvokeAsync(Call(port, PlayerActions.PlayPause), cancelled.Token)).Outcome);
    }

    [Theory]
    [InlineData(false, true, true, ActionUnavailableReason.InactivePresentation)]
    [InlineData(true, false, true, ActionUnavailableReason.ModalInteraction)]
    [InlineData(true, true, false, ActionUnavailableReason.SourceUnavailable)]
    public async Task EligibilityUsesApplicationState(bool presented, bool available, bool ready, ActionUnavailableReason reason)
    {
        var port = new FakePort(); port.Context = port.Context with { PlayerPresented = presented, InteractionAvailable = available, SourceReady = ready };
        using var actions = new PlayerActions(port);
        var result = await actions.InvokeAsync(Call(port, PlayerActions.PlayPause));
        Assert.Equal(ActionOutcome.Ineligible, result.Outcome); Assert.Equal(reason, result.Reason); Assert.Equal(0, port.ToggleCount);
    }

    [Fact]
    public async Task MissingAndReplacedTargetsNeverOperateOnNewSource()
    {
        var port = new FakePort(); using var actions = new PlayerActions(port);
        var old = Call(port, PlayerActions.StepFrame, new FrameStepArguments(-1));
        port.Context = port.Context with { Target = port.Context.Target! with { Generation = 2 } };
        Assert.Equal(ActionOutcome.Superseded, (await actions.InvokeAsync(old)).Outcome); Assert.Empty(port.Steps);
        port.Context = port.Context with { Target = null };
        Assert.Equal(ActionUnavailableReason.NoPlayer, (await actions.InvokeAsync(Call(port, PlayerActions.PlayPause))).Reason);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public async Task FrameDirectionsAreTypedAndReachPresentationPort(int direction)
    {
        var port = new FakePort { Playing = true }; using var actions = new PlayerActions(port);
        Assert.Equal(ActionOutcome.Completed, (await actions.InvokeAsync(Call(port, PlayerActions.StepFrame, new FrameStepArguments(direction)))).Outcome);
        Assert.Equal([direction], port.Steps); Assert.False(port.Playing);
    }
    [Theory]
    [InlineData(-20)] [InlineData(0)] [InlineData(20)]
    public async Task InvalidRelativeArgumentsCannotCreateWork(int direction)
    {
        var port = new FakePort(); using var actions = new PlayerActions(port);
        Assert.Equal(ActionUnavailableReason.InvalidArguments, (await actions.InvokeAsync(Call(port, PlayerActions.StepFrame, new FrameStepArguments(direction)))).Reason);
        Assert.Empty(port.Steps);
    }
    [Fact]
    public async Task UnknownActionWrongShapeAndPhaseAreRejected()
    {
        var port = new FakePort(); using var actions = new PlayerActions(port);
        Assert.Equal(ActionUnavailableReason.UnknownAction, (await actions.InvokeAsync(Call(port, "unknown"))).Reason);
        Assert.Equal(ActionUnavailableReason.InvalidArguments, (await actions.InvokeAsync(Call(port, PlayerActions.PlayPause, new FrameStepArguments(1)))).Reason);
        Assert.Equal(ActionUnavailableReason.InvalidPhase, (await actions.InvokeAsync(Call(port, PlayerActions.ColorBypass))).Reason);
        Assert.Equal(ActionUnavailableReason.InvalidArguments, (await actions.InvokeAsync(Call(port, PlayerActions.PlayPause) with { InvocationId = Guid.Empty })).Reason);
        Assert.Equal(0, port.ToggleCount);
    }

    [Fact]
    public async Task InFlightSourceReplacementReportsSuperseded()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var port = new FakePort { BeforeToggle = () => release.Task }; using var actions = new PlayerActions(port);
        var operation = actions.InvokeAsync(Call(port, PlayerActions.PlayPause));
        port.Context = port.Context with { Target = port.Context.Target! with { Generation = 2 } }; release.SetResult();
        Assert.Equal(ActionOutcome.Superseded, (await operation).Outcome);
    }

    [Theory]
    [InlineData(ActionPhase.End, ActionOutcome.Completed)]
    [InlineData(ActionPhase.Cancel, ActionOutcome.Cancelled)]
    public async Task GestureReleaseRecomputesCurrentColorOnOriginalTarget(ActionPhase phase, ActionOutcome outcome)
    {
        var port = new FakePort(); using var actions = new PlayerActions(port);
        var begin = Call(port, PlayerActions.ColorBypass, phase: ActionPhase.Begin);
        Assert.Equal(ActionOutcome.Completed, (await actions.InvokeAsync(begin)).Outcome); Assert.True(port.Bypassed);
        port.ColorRevision = 5;
        port.Context = port.Context with { PlayerPresented = false, InteractionAvailable = false, ColorActive = false };
        Assert.Equal(outcome, (await actions.InvokeAsync(begin with { Phase = phase, Target = null })).Outcome);
        Assert.False(port.Bypassed); Assert.Equal(5, port.RestoredRevision); Assert.Equal(0, port.CatalogWrites);
        Assert.Equal(begin.Target, port.LastRestoredTarget);
    }
    [Fact]
    public async Task GestureIsIdempotentAndDoesNotStealAnotherAdaptersSession()
    {
        var port = new FakePort(); using var actions = new PlayerActions(port);
        var begin = Call(port, PlayerActions.ColorBypass, phase: ActionPhase.Begin);
        await actions.InvokeAsync(begin);
        Assert.Equal(ActionOutcome.NoChange, (await actions.InvokeAsync(begin with { IsRepeat = true })).Outcome);
        Assert.Equal(ActionOutcome.Busy, (await actions.InvokeAsync(begin with { InvocationId = Guid.NewGuid() })).Outcome);
        Assert.Equal(ActionOutcome.NoChange, (await actions.InvokeAsync(begin with { Phase = ActionPhase.End, Source = new("other", ActionInputKind.Controller) })).Outcome);
        Assert.True(port.Bypassed);
        actions.CancelSource(new("other", ActionInputKind.Controller)); Assert.True(port.Bypassed);
        actions.CancelSource(Controller); Assert.False(port.Bypassed);
    }
    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task DisposalOrLifecycleCancellationReleasesGesture(bool dispose)
    {
        var port = new FakePort(); var actions = new PlayerActions(port);
        await actions.InvokeAsync(Call(port, PlayerActions.ColorBypass, phase: ActionPhase.Begin));
        if (dispose) actions.Dispose(); else actions.CancelGestures();
        Assert.False(port.Bypassed); Assert.Equal(1, port.RestoreCount); actions.Dispose(); Assert.Equal(1, port.RestoreCount);
    }
    [Fact]
    public async Task ColorWithoutAssignmentIsIneligible()
    {
        var port = new FakePort(); port.Context = port.Context with { ColorActive = false }; using var actions = new PlayerActions(port);
        Assert.Equal(ActionUnavailableReason.ColorInactive, (await actions.InvokeAsync(Call(port, PlayerActions.ColorBypass, phase: ActionPhase.Begin))).Reason);
        Assert.False(port.Bypassed);
    }

    [Fact]
    public async Task RepeatedGestureBeginCannotRearmCancelledHold()
    {
        var port = new FakePort(); using var actions = new PlayerActions(port);
        var begin = Call(port, PlayerActions.ColorBypass, phase: ActionPhase.Begin);
        await actions.InvokeAsync(begin); actions.CancelSource(Controller);
        Assert.Equal(ActionOutcome.NoChange, (await actions.InvokeAsync(begin with { IsRepeat = true })).Outcome);
        Assert.False(port.Bypassed);
    }

    private sealed class FakePort : IPlayerActionPort
    {
        public PlayerActionContext Context { get; set; } = new(new(Guid.NewGuid(), 1, Guid.NewGuid()), true, true, true, true);
        public bool Playing, Bypassed;
        public int ToggleCount, ColorRevision, RestoredRevision, RestoreCount;
        public int CatalogWrites => 0; // Port has no Catalog mutation capability.
        public PlayerActionTarget? LastRestoredTarget;
        public List<int> Steps { get; } = [];
        public Func<Task>? BeforeToggle;
        public async Task TogglePlaybackAsync(PlayerActionTarget target, CancellationToken token)
        { if (BeforeToggle is not null) await BeforeToggle(); Playing = !Playing; ToggleCount++; }
        public Task StepFrameAsync(PlayerActionTarget target, int direction, CancellationToken token)
        { Steps.Add(direction); Playing = false; return Task.CompletedTask; }
        public void SetColorBypass(PlayerActionTarget target, bool bypass)
        { Bypassed = bypass; if (!bypass) { RestoreCount++; RestoredRevision = ColorRevision; LastRestoredTarget = target; } }
    }
}
