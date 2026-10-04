using Lightflow.Actions;
using LightflowStudio;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class BrowserSemanticActionTests
{
    private static readonly ActionInputSource Controller = new("test-controller", ActionInputKind.Controller);
    private static BrowserActionInvocation Call(IBrowserActionPort port, string id, ActionArguments arguments, bool repeat = false) =>
        new(id, arguments, Controller, Guid.NewGuid(), port.Context.Target, IsRepeat: repeat);

    [Theory]
    [InlineData(BrowserScopeKind.Folder, false)]
    [InlineData(BrowserScopeKind.Folder, true)]
    [InlineData(BrowserScopeKind.Collection, false)]
    [InlineData(BrowserScopeKind.Collection, true)]
    [InlineData(BrowserScopeKind.SmartCollection, false)]
    [InlineData(BrowserScopeKind.SmartCollection, true)]
    public async Task ControllerNavigation_UsesSharedProjectionSelectionAndStableCurrent(BrowserScopeKind scope, bool details)
    {
        var port = new Port(scope); var actions = new BrowserActions(port);
        port.Grid.SetColumns(details ? 1 : 3);
        port.Grid.SelectSingle(0);
        await actions.InvokeAsync(Call(port, BrowserActions.NavigateSelection, new NavigateSelectionArguments(BrowserMovement.Next)));
        Assert.Equal(port.Grid.Tiles[1].AssetId, port.Current);
        await actions.InvokeAsync(Call(port, BrowserActions.NavigateSelection, new NavigateSelectionArguments(BrowserMovement.Previous)));
        Assert.Equal(port.Grid.Tiles[0].AssetId, port.Current);
        await actions.InvokeAsync(Call(port, BrowserActions.NavigateSelection, new NavigateSelectionArguments(BrowserMovement.Previous)));
        Assert.Equal(port.Grid.Tiles[0].AssetId, port.Current);
        await actions.InvokeAsync(Call(port, BrowserActions.NavigateSelection, new NavigateSelectionArguments(BrowserMovement.Last)));
        await actions.InvokeAsync(Call(port, BrowserActions.NavigateSelection, new NavigateSelectionArguments(BrowserMovement.Next)));
        Assert.Equal(port.Grid.Tiles[^1].AssetId, port.Current);
        port.Grid.SetQuery(new() { SortDescending = true });
        await actions.InvokeAsync(Call(port, BrowserActions.NavigateSelection, new NavigateSelectionArguments(BrowserMovement.First)));
        Assert.Equal(port.Grid.Tiles[0].AssetId, port.Current);
        await actions.InvokeAsync(Call(port, BrowserActions.NavigateSelection, new NavigateSelectionArguments(BrowserMovement.Next, Extend: true)));
        Assert.Equal(2, port.Grid.SelectedKeys.Count);
        Assert.Equal(scope, port.Context.Target!.Scope.Kind);
    }
    [Fact]
    public async Task ReplacedProjectionScopeAndGeneration_CannotInvokeOldTarget()
    {
        var port = new Port(); var actions = new BrowserActions(port);
        var call = Call(port, BrowserActions.NavigateSelection, new NavigateSelectionArguments(BrowserMovement.Next));
        port.Grid.ReapplyQuery();
        Assert.Equal(call.Target, port.Context.Target);
        port.Grid.SetQuery(new() { SortMode = BrowserSortMode.Name, SortDescending = true });
        Assert.Equal(ActionOutcome.Superseded, (await actions.InvokeAsync(call)).Outcome);
        call = Call(port, BrowserActions.NavigateSelection, new NavigateSelectionArguments(BrowserMovement.Next));
        port.Generation++;
        Assert.Equal(ActionOutcome.Superseded, (await actions.InvokeAsync(call)).Outcome);
        call = Call(port, BrowserActions.NavigateSelection, new NavigateSelectionArguments(BrowserMovement.Next));
        port.Scope = new(BrowserScopeKind.Collection, Guid.NewGuid());
        Assert.Equal(ActionOutcome.Superseded, (await actions.InvokeAsync(call)).Outcome);
        Assert.Equal(0, port.NavigateCalls);
    }
    [Fact]
    public async Task DirtyTransitionGuard_PreservesSelectionAndCurrent()
    {
        var port = new Port(); port.Grid.SelectSingle(0); port.Grid.SelectionChanging = () => false;
        Assert.Equal(ActionOutcome.Cancelled, (await new BrowserActions(port).InvokeAsync(Call(port,
            BrowserActions.NavigateSelection, new NavigateSelectionArguments(BrowserMovement.Next)))).Outcome);
        Assert.True(port.Grid.Tiles[0].IsSelected); Assert.Null(port.Current);
    }
    [Fact]
    public async Task CapturesMultiSelectionBeforeAwait_AndBoundsRelativeBacklog()
    {
        var port = new Port(); var actions = new BrowserActions(port); port.Grid.SelectAll();
        var ids = port.Grid.SelectedAssetIdsInBrowserOrder.ToArray();
        port.Pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = actions.InvokeAsync(Call(port, BrowserActions.StepFlag, new StepFlagArguments(TraversalDirection.Next)));
        port.Grid.SelectSingle(0);
        Assert.Equal(ActionOutcome.Busy, (await actions.InvokeAsync(Call(port, BrowserActions.StepFlag,
            new StepFlagArguments(TraversalDirection.Next), repeat: true))).Outcome);
        Assert.Equal(ids, port.Captured);
        port.Pending.SetResult(); await first;
    }
    [Theory]
    [InlineData(BrowserActions.SetRating)]
    [InlineData(BrowserActions.SetFlag)]
    [InlineData(BrowserActions.SetColorLabel)]
    [InlineData(BrowserActions.OpenCurrent)]
    public async Task DirectSetsAndOpen_SuppressRepeat(string id)
    {
        var port = new Port(); port.Grid.SelectAll();
        ActionArguments args = id switch { BrowserActions.SetRating => new SetRatingArguments(3),
            BrowserActions.SetFlag => new SetFlagArguments(ClassificationFlag.Picked),
            BrowserActions.SetColorLabel => new SetColorLabelArguments(null), _ => NoActionArguments.Instance };
        Assert.Equal(ActionOutcome.NoChange, (await new BrowserActions(port).InvokeAsync(Call(port, id, args, true))).Outcome);
        Assert.Empty(port.Captured);
    }
    [Fact]
    public async Task InvalidArgumentsAndEligibility_AreStructuredAndPlatformNeutral()
    {
        var port = new Port(); port.Grid.SelectAll(); var actions = new BrowserActions(port);
        foreach (var (id, args) in new (string, ActionArguments)[] {
            (BrowserActions.SetRating, new SetRatingArguments(6)), (BrowserActions.SetRating, new SetRatingArguments(-1)),
            (BrowserActions.SetFlag, new SetFlagArguments((ClassificationFlag)4)),
            (BrowserActions.StepFlag, new StepFlagArguments((TraversalDirection)0)),
            (BrowserActions.SetColorLabel, new SetColorLabelArguments((ClassificationColorLabel)6)),
            (BrowserActions.NavigateSelection, new NavigateSelectionArguments(BrowserMovement.Next, Distance: 10001)) })
            Assert.Equal(ActionUnavailableReason.InvalidArguments, (await actions.InvokeAsync(Call(port, id, args))).Reason);
        port.Interaction = false;
        Assert.Equal(ActionUnavailableReason.ModalInteraction, actions.Eligibility(BrowserActions.SetRating, port.Context.Target).Reason);
        port.Interaction = true; port.Presented = false;
        Assert.Equal(ActionUnavailableReason.InactivePresentation, actions.Eligibility(BrowserActions.SetRating, port.Context.Target).Reason);
        Assert.Equal(6, BrowserActions.Descriptors.Count);
        Assert.DoesNotContain(typeof(BrowserActions).Assembly.GetReferencedAssemblies(), a => a.Name!.Contains("Windows") || a.Name.Contains("Presentation"));
    }
    [Fact]
    public async Task ObsoleteOpen_DoesNotBlockFreshPresentationOrReleaseItsSingleFlightOwner()
    {
        var port = new Port(); port.Grid.SelectAll(); var actions = new BrowserActions(port);
        var old = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var fresh = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        port.Opens.Enqueue(old); port.Opens.Enqueue(fresh);
        var first = actions.InvokeAsync(Call(port, BrowserActions.OpenCurrent, NoActionArguments.Instance));
        Assert.Equal(ActionOutcome.Busy, (await actions.InvokeAsync(Call(port, BrowserActions.OpenCurrent, NoActionArguments.Instance))).Outcome);
        port.PresentationGeneration++;
        var second = actions.InvokeAsync(Call(port, BrowserActions.OpenCurrent, NoActionArguments.Instance));
        Assert.False(second.IsCompleted);
        old.SetResult(); Assert.Equal(ActionOutcome.Superseded, (await first).Outcome);
        Assert.Equal(ActionOutcome.Busy, (await actions.InvokeAsync(Call(port, BrowserActions.OpenCurrent, NoActionArguments.Instance))).Outcome);
        fresh.SetResult(); Assert.Equal(ActionOutcome.Completed, (await second).Outcome);
    }
    private sealed class Port : IBrowserActionPort
    {
        public Port(BrowserScopeKind kind = BrowserScopeKind.Folder) { Scope = new(kind, Guid.NewGuid()); Grid.Populate(BrowserDetailsTests.Entries(4)); }
        public BrowserGridModel Grid { get; } = new();
        public Guid Session { get; } = Guid.NewGuid();
        public BrowserScopeIdentity Scope;
        public long Generation, PresentationGeneration;
        public Guid? Current;
        public bool Interaction = true, Presented = true;
        public int NavigateCalls;
        public Guid[] Captured = [];
        public TaskCompletionSource? Pending;
        public BrowserActionContext Context => new(new(Session, Generation, Scope, Grid.ProjectionGeneration, PresentationGeneration), Presented,
            Interaction, Grid.SelectedAssetIdsInBrowserOrder, Current, Grid.Tiles.Count > 0);
        public Task<ActionResult> NavigateAsync(BrowserActionTarget target, NavigateSelectionArguments args, CancellationToken token)
        {
            NavigateCalls++;
            var index = BrowserSelectionNavigation.Destination(Grid, Current, args);
            if (!(args.Extend ? Grid.SelectRange(index) : Grid.SelectSingle(index))) return Task.FromResult(new ActionResult(ActionOutcome.Cancelled));
            Current = Grid.Tiles[index].AssetId;
            return Task.FromResult(new ActionResult(ActionOutcome.Completed));
        }
        public Queue<TaskCompletionSource> Opens = new();
        public async Task<ActionResult> OpenAsync(BrowserActionTarget target, OpenBrowserArguments? arguments, CancellationToken token) {
            if (Opens.Count > 0) await Opens.Dequeue().Task.WaitAsync(token);
            return new(Context.Target == target ? ActionOutcome.Completed : ActionOutcome.Superseded);
        }
        public async Task<ActionResult> ClassifyAsync(BrowserActionTarget target, IReadOnlyList<Guid> ids, ActionArguments args, CancellationToken token)
        {
            Captured = ids.ToArray(); if (Pending is not null) await Pending.Task.WaitAsync(token);
            return new(Context.Target == target ? ActionOutcome.Completed : ActionOutcome.Superseded);
        }
    }
}
