using Lightflow.Actions;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class ReviewShellActionTests
{
    private static readonly ActionInputSource Controller = new("review-controller", ActionInputKind.Controller);
    private static ReviewShellInvocation Call(Port port, string id, ActionArguments? arguments = null) =>
        new(id, arguments ?? NoActionArguments.Instance, Controller, Guid.NewGuid(), port.Context.Target);
    [Fact]
    public async Task TypedExportEntriesAreExplicitAndCompletionDoesNotQueueAJob()
    {
        var port = new Port(); var actions = new ReviewShellActions(port);
        foreach (var entry in Enum.GetValues<ExportEntry>()) {
            var result = await actions.InvokeAsync(Call(port, ReviewShellActions.Export, new ExportEntryArguments(entry)));
            Assert.Equal(ActionOutcome.Completed, result.Outcome); Assert.Equal(entry, port.Entries[^1]);
        }
        Assert.Equal(5, port.Entries.Count);
        Assert.Equal(ActionOutcome.NoChange, (await actions.InvokeAsync(Call(port, ReviewShellActions.Export, new ExportEntryArguments(ExportEntry.PlayerVideo)) with { IsRepeat = true })).Outcome);
        Assert.Equal(5, port.Entries.Count);
    }
    [Fact]
    public async Task PendingExportIsSingleFlightAndOwnsItsCapturedContext()
    {
        var release = new TaskCompletionSource<ActionResult>(); var port = new Port { ExportCompletion = release.Task }; var actions = new ReviewShellActions(port);
        var call = Call(port, ReviewShellActions.Export, new ExportEntryArguments(ExportEntry.BrowserVideos));
        var pending = actions.InvokeAsync(call);
        Assert.Equal(ActionOutcome.Busy, (await actions.InvokeAsync(call with { InvocationId = Guid.NewGuid() })).Outcome);
        port.Context = port.Context with { Target = port.Context.Target with { Revision = 2 } };
        Assert.Equal(ActionOutcome.Superseded, (await actions.InvokeAsync(call)).Outcome);
        release.SetResult(new(ActionOutcome.Superseded)); Assert.Equal(ActionOutcome.Superseded, (await pending).Outcome);
        Assert.Single(port.Entries);
    }
    [Theory]
    [InlineData(false, true, ActionUnavailableReason.InactivePresentation)]
    [InlineData(true, false, ActionUnavailableReason.ModalInteraction)]
    public async Task ShellOwnershipPreventsPortAdmission(bool home, bool available, ActionUnavailableReason reason)
    {
        var port = new Port(); port.Context = port.Context with { HomePresented = home, InteractionAvailable = available }; var actions = new ReviewShellActions(port);
        Assert.Equal(reason, (await actions.InvokeAsync(Call(port, ReviewShellActions.Export, new ExportEntryArguments(ExportEntry.PlayerVideo)))).Reason);
        Assert.Empty(port.Entries);
    }
    [Fact]
    public async Task InvalidArgumentsCancellationAndPortFailuresAreStructured()
    {
        var port = new Port(); var actions = new ReviewShellActions(port);
        foreach (var (id, arguments) in new (string, ActionArguments)[] {
            (ReviewShellActions.Export, new ExportEntryArguments((ExportEntry)99)), (ReviewShellActions.Export, NoActionArguments.Instance),
            (ReviewShellActions.ThumbnailSize, new LevelArguments(20)), (ReviewShellActions.ShowPanel, new PanelSurfaceArguments((ReviewPanelSurface)99)),
            (ReviewShellActions.TogglePanel, new LevelArguments(1)) })
            Assert.Equal(ActionUnavailableReason.InvalidArguments, (await actions.InvokeAsync(Call(port, id, arguments))).Reason);
        var call = Call(port, ReviewShellActions.Export, new ExportEntryArguments(ExportEntry.PlayerVideo));
        Assert.Equal(ActionUnavailableReason.InvalidPhase, (await actions.InvokeAsync(call with { Phase = ActionPhase.Begin })).Reason);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.Equal(ActionOutcome.Cancelled, (await actions.InvokeAsync(call, cancelled.Token)).Outcome); Assert.Empty(port.Entries);
        port.ExportCompletion = Task.FromException<ActionResult>(new IOException("handoff failed"));
        var result = await actions.InvokeAsync(call); Assert.Equal(ActionOutcome.Failed, result.Outcome); Assert.Equal("handoff failed", result.Diagnostic);
        port.ExportCompletion = Task.FromCanceled<ActionResult>(cancelled.Token);
        Assert.Equal(ActionOutcome.Cancelled, (await actions.InvokeAsync(call)).Outcome);
    }
    [Fact]
    public void DescriptorInventoryAndContractsRemainPlatformNeutral()
    {
        var descriptors = PlayerActions.Descriptors.Concat(ReviewPresentationActions.Descriptors).Concat(ReviewShellActions.Descriptors).ToArray();
        Assert.Equal(descriptors.Length, descriptors.Select(x => x.Id).Distinct().Count()); Assert.All(descriptors, d => Assert.True(d.Bindable));
        Assert.DoesNotContain(typeof(ReviewShellActions).Assembly.GetReferencedAssemblies(), a => a.Name!.Contains("Windows") || a.Name.Contains("Presentation"));
    }
    private sealed class Port : IReviewShellPort
    {
        public ReviewShellContext Context { get; set; } = new(new(Guid.NewGuid(), 1), true, true);
        public List<ExportEntry> Entries { get; } = [];
        public Task<ActionResult>? ExportCompletion { get; set; }
        public ActionEligibility Eligibility(string id, ActionArguments arguments) => new(true);
        public Task<ActionResult> StepThumbnailsAsync(ReviewShellTarget target, int direction, CancellationToken token) => Task.FromResult(new ActionResult(ActionOutcome.Completed));
        public Task<ActionResult> TogglePanelAsync(ReviewShellTarget target, CancellationToken token) => Task.FromResult(new ActionResult(ActionOutcome.Completed));
        public Task<ActionResult> ShowPanelAsync(ReviewShellTarget target, ReviewPanelSurface surface, CancellationToken token) => Task.FromResult(new ActionResult(ActionOutcome.Completed));
        public Task<ActionResult> OpenExportAsync(ReviewShellTarget target, ExportEntry entry, CancellationToken token) { Entries.Add(entry); return ExportCompletion ?? Task.FromResult(new ActionResult(ActionOutcome.Completed)); }
    }
}
