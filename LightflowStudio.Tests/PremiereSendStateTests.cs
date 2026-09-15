using Xunit;

namespace LightflowStudio.Tests;

public class PremiereSendStateTests
{
    private static PremiereConnection Connected(string path = @"C:\edit.prproj", string bin = "root") => new(
        PremiereConnectionState.Connected, "Connected — Premiere Pro 26.5", new("instance", "1.0.3", 1, "26.5", "9.3",
            new("guid", path, "edit.prproj"), [new(bin, "Project root")]));

    [Theory]
    [InlineData(PremiereConnectionState.PremiereNotInstalled, PremiereSendRoute.Settings)]
    [InlineData(PremiereConnectionState.CompanionNotInstalled, PremiereSendRoute.Settings)]
    [InlineData(PremiereConnectionState.UpdateRequired, PremiereSendRoute.Settings)]
    [InlineData(PremiereConnectionState.Ready, PremiereSendRoute.Disconnected)]
    [InlineData(PremiereConnectionState.ConnectionProblem, PremiereSendRoute.Disconnected)]
    [InlineData(PremiereConnectionState.Connected, PremiereSendRoute.Send)]
    public void RoutesReflectLiveConnection(object state, object route) =>
        Assert.Equal((PremiereSendRoute)route, PremiereSendState.Route(new((PremiereConnectionState)state, "state")));

    [Fact]
    public void InstallationAloneNeverBecomesConnectedAndLiveHealthWinsInventory()
    {
        var ready = new PremiereConnection(PremiereConnectionState.Ready, "installed");
        Assert.Equal(PremiereSendRoute.Disconnected, PremiereSendState.Route(PremiereSendState.WithInstallation(ready, ready)));
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
