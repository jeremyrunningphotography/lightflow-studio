using LightflowStudio;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class WorkspaceWindowPlacementTests
{
    private static readonly ScreenWorkArea PrimaryOnly = new(0, 0, 1920, 1040);

    [Fact]
    public void Clamp_KeepsSavedBoundsUnchangedWhenTheyFitWithinAConnectedMonitor()
    {
        var saved = new WorkspaceWindowState { Width = 1440, Height = 900, Left = 100, Top = 60 };

        var result = WorkspaceWindowPlacement.Clamp(saved, [PrimaryOnly], 1120, 720);

        Assert.Equal(1440, result.Width);
        Assert.Equal(900, result.Height);
        Assert.Equal(100, result.Left);
        Assert.Equal(60, result.Top);
    }

    [Fact]
    public void Clamp_EnforcesTheApplicationsDeclaredMinimumSize()
    {
        var saved = new WorkspaceWindowState { Width = 400, Height = 300, Left = 100, Top = 60 };

        var result = WorkspaceWindowPlacement.Clamp(saved, [PrimaryOnly], 1120, 720);

        Assert.Equal(1120, result.Width);
        Assert.Equal(720, result.Height);
    }

    [Fact]
    public void Clamp_RecoversOntoThePrimaryWorkAreaWhenTheSavedMonitorHasBeenRemoved()
    {
        // Saved position was on a second monitor to the right that is no longer connected.
        var saved = new WorkspaceWindowState { Width = 1440, Height = 900, Left = 2400, Top = 100 };

        var result = WorkspaceWindowPlacement.Clamp(saved, [PrimaryOnly], 1120, 720);

        Assert.InRange(result.Left, PrimaryOnly.Left, PrimaryOnly.Left + PrimaryOnly.Width - result.Width);
        Assert.InRange(result.Top, PrimaryOnly.Top, PrimaryOnly.Top + PrimaryOnly.Height - result.Height);
    }

    [Fact]
    public void Clamp_RecoversWhenSavedBoundsAreAlmostEntirelyOffscreen()
    {
        var saved = new WorkspaceWindowState { Width = 1440, Height = 900, Left = -1400, Top = -880 };

        var result = WorkspaceWindowPlacement.Clamp(saved, [PrimaryOnly], 1120, 720);

        var overlapWidth = Math.Min(result.Left + result.Width, PrimaryOnly.Width) - Math.Max(result.Left, 0);
        var overlapHeight = Math.Min(result.Top + result.Height, PrimaryOnly.Height) - Math.Max(result.Top, 0);
        Assert.True(overlapWidth >= WorkspaceWindowPlacement.MinimumVisibleWidth);
        Assert.True(overlapHeight >= WorkspaceWindowPlacement.MinimumVisibleHeight);
    }

    [Fact]
    public void Clamp_KeepsBoundsThatAreOnlyPartiallyOffscreenButStillMeaningfullyVisible()
    {
        // Enough of the title bar/window remains on-screen to grab and drag back into view.
        var saved = new WorkspaceWindowState { Width = 1440, Height = 900, Left = -1200, Top = 0 };

        var result = WorkspaceWindowPlacement.Clamp(saved, [PrimaryOnly], 1120, 720);

        Assert.Equal(saved.Left, result.Left);
        Assert.Equal(saved.Top, result.Top);
    }

    [Fact]
    public void Clamp_RecoversWhenSavedResolutionNoLongerFitsAnySmallerCurrentMonitor()
    {
        var smaller = new ScreenWorkArea(0, 0, 1280, 720);
        var saved = new WorkspaceWindowState { Width = 2560, Height = 1440, Left = 3000, Top = 200 };

        var result = WorkspaceWindowPlacement.Clamp(saved, [smaller], 1120, 720);

        Assert.True(result.Width <= smaller.Width);
        Assert.True(result.Height <= smaller.Height);
        Assert.InRange(result.Left, 0, smaller.Width - result.Width);
        Assert.InRange(result.Top, 0, smaller.Height - result.Height);
    }

    [Fact]
    public void Clamp_ChoosesAMonitorTheSavedWindowActuallyOverlapsInMultiMonitorTopology()
    {
        var second = new ScreenWorkArea(1920, 0, 1920, 1040);
        var saved = new WorkspaceWindowState { Width = 1440, Height = 900, Left = 2200, Top = 60 };

        var result = WorkspaceWindowPlacement.Clamp(saved, [PrimaryOnly, second], 1120, 720);

        Assert.Equal(saved.Left, result.Left);
        Assert.Equal(saved.Top, result.Top);
    }

    [Fact]
    public void Clamp_NeverRestoresMinimizedState()
    {
        // WorkspaceWindowState carries no minimized concept at all: IsMaximized is the only persisted mode,
        // so a minimized-at-close session cannot round-trip into a minimized-at-launch session.
        var properties = typeof(WorkspaceWindowState).GetProperties().Select(property => property.Name);

        Assert.DoesNotContain(properties, name => name.Contains("Minimiz", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Clamp_PreservesMaximizedFlagThroughRecovery()
    {
        var saved = new WorkspaceWindowState { Width = 1440, Height = 900, Left = 5000, Top = 5000, IsMaximized = true };

        var result = WorkspaceWindowPlacement.Clamp(saved, [PrimaryOnly], 1120, 720);

        Assert.True(result.IsMaximized);
    }

    [Fact]
    public void Clamp_FallsBackToOriginWhenNoMonitorInformationIsAvailable()
    {
        var saved = new WorkspaceWindowState { Width = 1440, Height = 900, Left = 100, Top = 60 };

        var result = WorkspaceWindowPlacement.Clamp(saved, [], 1120, 720);

        Assert.Equal(0, result.Left);
        Assert.Equal(0, result.Top);
        Assert.Equal(1440, result.Width);
        Assert.Equal(900, result.Height);
    }
}
