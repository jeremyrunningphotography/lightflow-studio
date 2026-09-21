using Xunit;

namespace LightflowStudio.Tests;

public class PremiereSendStateTests
{
    [Fact]
    public async Task InstalledReadyWaitsForDetectionThenOpensSendWithoutRequiringPriorHeartbeat()
    {
        var inventory = new TaskCompletionSource<PremiereConnection>();
        var live = new PremiereConnection(PremiereConnectionState.Ready, "Waiting for companion");
        var route = PremiereSendState.ResolveRouteAsync(() => live, () => false, () => inventory.Task);
        Assert.False(route.IsCompleted);
        inventory.SetResult(new(PremiereConnectionState.Ready, "Companion installed"));
        Assert.Equal(PremiereSendRoute.Send, await route);
        Assert.False(new PremiereSendState().CanSend(live));
        var shown = new List<string>();
        await PremiereSendState.NavigateAsync(true, await route, () => shown.Add("settings"),
            () => { shown.Add("send"); return Task.CompletedTask; });
        Assert.Equal(new[] { "send" }, shown);
    }

    [Theory]
    [InlineData(PremiereConnectionState.CompanionNotInstalled)]
    [InlineData(PremiereConnectionState.PremiereNotInstalled)]
    [InlineData(PremiereConnectionState.UpdateRequired)]
    [InlineData(PremiereConnectionState.ConnectionProblem)]
    public async Task MissingOrUnknownInstallationStillOpensSettings(object state)
    {
        Assert.Equal(PremiereSendRoute.Settings, await PremiereSendState.ResolveRouteAsync(
            () => new(PremiereConnectionState.Ready, "waiting"), () => false,
            () => Task.FromResult(new PremiereConnection((PremiereConnectionState)state, "setup needed"))));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PreviouslyPairedOrLiveConnectionSkipsInstallationProbe(bool paired)
    {
        Assert.Equal(PremiereSendRoute.Send, await PremiereSendState.ResolveRouteAsync(
            () => new(paired ? PremiereConnectionState.Ready : PremiereConnectionState.Connected, "state"), () => paired,
            () => throw new InvalidOperationException("Should not inspect")));
    }

    [Fact]
    public async Task ConnectionEstablishedDuringInspectionWinsStaleInventory()
    {
        var live = new PremiereConnection(PremiereConnectionState.Ready, "waiting");
        Assert.Equal(PremiereSendRoute.Send, await PremiereSendState.ResolveRouteAsync(() => live, () => false, () =>
        {
            live = Connected();
            return Task.FromResult(new PremiereConnection(PremiereConnectionState.ConnectionProblem, "unavailable"));
        }));
    }
    private static PremiereConnection Connected(string path = @"C:\edit.prproj", string bin = "root") => new(
        PremiereConnectionState.Connected, "Connected — Premiere Pro 26.5", new("instance", "1.0.3", 1, "26.5", "9.3",
            new("guid", path, "edit.prproj"), [new(bin, "Project root")]));

    [Theory]
    [InlineData(PremiereConnectionState.PremiereNotInstalled, PremiereSendRoute.Settings)]
    [InlineData(PremiereConnectionState.CompanionNotInstalled, PremiereSendRoute.Settings)]
    [InlineData(PremiereConnectionState.UpdateRequired, PremiereSendRoute.Settings)]
    [InlineData(PremiereConnectionState.Ready, PremiereSendRoute.Settings)]
    [InlineData(PremiereConnectionState.ConnectionProblem, PremiereSendRoute.Settings)]
    [InlineData(PremiereConnectionState.Connected, PremiereSendRoute.Send)]
    public void RoutesReflectLiveConnection(object state, object route) =>
        Assert.Equal((PremiereSendRoute)route, PremiereSendState.Route(new((PremiereConnectionState)state, "state")));

    [Fact]
    public void ConfiguredProfilesOpenSendRegardlessOfTransientReadiness()
    {
        foreach (var state in Enum.GetValues<PremiereConnectionState>())
            Assert.Equal(PremiereSendRoute.Send, PremiereSendState.Route(new(state, "state"), setupComplete: true));
        var disconnected = new PremiereConnection(PremiereConnectionState.Ready, "waiting for companion");
        Assert.False(new PremiereSendState().CanSend(disconnected));
        Assert.False(PremiereSendState.Present(disconnected).IsActionable);
    }

    [Theory]
    [InlineData(true, false, "settings,send")]
    [InlineData(true, true, "send")]
    [InlineData(false, false, "settings")]
    [InlineData(false, true, "settings")]
    public async Task SettingsDismissalPreservesOnlyTheOriginalSendIntent(bool send, bool configured, string expected)
    {
        var shown = new List<string>();
        await PremiereSendState.NavigateAsync(send,
            PremiereSendState.Route(new(PremiereConnectionState.Ready, "waiting"), configured),
            () => shown.Add("settings"), // ShowDialog returns for either Done or the window close button.
            () => { shown.Add("send"); return Task.CompletedTask; });
        Assert.Equal(expected, string.Join(",", shown));
    }

    [Fact]
    public void InstallationAloneNeverBecomesConnectedAndLiveHealthWinsInventory()
    {
        var ready = new PremiereConnection(PremiereConnectionState.Ready, "installed");
        Assert.Equal(PremiereSendRoute.Settings, PremiereSendState.Route(PremiereSendState.WithInstallation(ready, ready)));
        var live = Connected();
        Assert.Same(live, PremiereSendState.WithInstallation(live, ready));
    }
    [Fact]
    public void RequiresExplicitBinSelectionAndRejectsDeletedBinBeforeNextRefresh()
    {
        var state = new PremiereSendState(); var live = Connected();
        state.Refresh(live);
        Assert.Single(state.Bins); Assert.False(state.CanSend(live));
        state.SelectedBinId = "root"; Assert.True(state.CanSend(live));
        Assert.False(state.CanSend(Connected(bin: "replacement")));
        state.Refresh(Connected(bin: "replacement")); Assert.Null(state.SelectedBinId);
    }
    [Fact]
    public void ProjectSwitchAndSaveAsRejectStaleClickAndClearSelection()
    {
        var state = new PremiereSendState(); var live = Connected(); state.Refresh(live); state.SelectedBinId = "root";
        var switched = Connected(@"C:\copy.prproj");
        Assert.False(state.CanSend(switched)); Assert.True(state.Refresh(switched));
        Assert.Null(state.SelectedBinId); Assert.False(state.CanSend(switched));
        state.SelectedBinId = "root";
        var other = switched with { Companion = switched.Companion! with { Project = switched.Companion.Project! with { Guid = "other" } } };
        Assert.False(state.CanSend(other)); state.Refresh(other); Assert.Null(state.SelectedBinId);
    }
    [Fact]
    public void DisconnectClearsStaleCompanionAndReconnectRequiresNewChoice()
    {
        var state = new PremiereSendState(); var live = Connected(); state.Refresh(live); state.SelectedBinId = "root";
        var stale = live with { State = PremiereConnectionState.ConnectionProblem };
        Assert.False(state.CanSend(stale)); state.Refresh(stale);
        Assert.Empty(state.Bins); Assert.Null(state.DestinationId);
        state.Refresh(live); Assert.False(state.CanSend(live));
    }
    [Fact]
    public void NoProjectAndUnavailableBinsHaveActionableExplanations()
    {
        var state = new PremiereSendState(); var live = Connected();
        var noProject = live with { Companion = live.Companion! with { Project = null } };
        state.Refresh(noProject); Assert.Empty(state.Bins); Assert.Equal("", state.Message); Assert.False(state.CanSend(noProject));
        Assert.Equal("Open or create a Premiere project, then click Refresh.", PremiereSendState.Present(noProject).Guidance);
        var noBins = live with { Companion = live.Companion! with { Bins = [] } };
        state.Refresh(noBins); Assert.Contains("Waiting for bins", state.Message); Assert.False(state.CanSend(noBins));
    }
    [Fact]
    public void EditorRenamePreservesSelectionByNativeId()
    {
        var state = new PremiereSendState(); var live = Connected(); state.Refresh(live); state.SelectedBinId = "root";
        var renamed = live with { Companion = live.Companion! with { Bins = [new("root", "Archive / renamed")] } };
        Assert.False(state.Refresh(renamed)); Assert.True(state.CanSend(renamed)); Assert.Equal("root", state.SelectedBinId);
    }
}
