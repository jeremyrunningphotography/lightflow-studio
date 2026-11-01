using Avalonia;
using Lightflow.UI;
using Xunit;

namespace Lightflow.UI.Tests;

public sealed class ShellGeometryTests
{
    [Fact]
    public void FirstRunUsesApprovedLogicalSizeAndCentersAtRetinaScale()
    {
        var area=new ShellWorkArea(new PixelRect(200,50,3840,2160),2);
        var preferred=ShellGeometry.FirstRun(area);var actual=ShellGeometry.Clamp(preferred,area);
        Assert.Equal(1440,actual.Width);Assert.Equal(900,actual.Height);
        Assert.Equal(680,actual.X);Assert.Equal(230,actual.Y);
        Assert.False(new BrowserLayout().RightOpen);
    }
    [Fact]
    public void SmallerDisplayClampsWithoutDestroyingPreferredRestoredBounds()
    {
        var preferred=new ShellWindowGeometry(1600,1000,2000,100,2);
        var small=new ShellWorkArea(new PixelRect(0,0,2000,1300),2);
        var fitted=ShellGeometry.Clamp(preferred,small);
        Assert.Equal(1000,fitted.Width);Assert.Equal(650,fitted.Height);Assert.Equal(0,fitted.X);Assert.Equal(0,fitted.Y);
        var remembered=ShellGeometry.Remember(preferred,fitted,small,true,true);
        Assert.Equal(preferred.Width,remembered.Width);Assert.Equal(preferred.Height,remembered.Height);
        Assert.Equal(preferred.X,remembered.X);Assert.Equal(preferred.Y,remembered.Y);
        var large=new ShellWorkArea(new PixelRect(0,0,4000,2400),2);
        Assert.Equal(1600,ShellGeometry.Clamp(remembered,large).Width);
    }
    [Fact]
    public void UserResizeAndMoveRestoreOnMatchingMonitorAndRemovedMonitorStaysReachable()
    {
        var areas=new[] {new ShellWorkArea(new PixelRect(0,0,1920,1080)),new ShellWorkArea(new PixelRect(-2560,0,2560,1440))};
        var observed=new ShellWindowGeometry(1300,800,-2000,80);
        Assert.Equal(areas[1],ShellGeometry.Select(observed,areas));
        var preferred=ShellGeometry.Remember(new(),observed,areas[1],true,true);
        Assert.Equal(observed,preferred);
        var restored=ShellGeometry.Clamp(preferred,areas[1]);Assert.Equal(observed,restored);
        var removed=ShellGeometry.Clamp(preferred,areas[0]);Assert.Equal(0,removed.X);Assert.Equal(1300,removed.Width);
    }
    [Fact]
    public void PaneConstraintsRetainPreferenceAndSurplusGoesOnlyToCenter()
    {
        var preferred=new BrowserLayout(510,590,true);
        var small=BrowserPaneWidths.Fit(1092,preferred);var large=BrowserPaneWidths.Fit(1900,preferred);
        Assert.True(small.Navigation+small.Right+small.CenterMinimum+16<=1092);
        Assert.Equal(510,large.Navigation);Assert.Equal(590,large.Right);
        Assert.Equal(510,preferred.NavigationWidth);Assert.Equal(590,preferred.RightWidth);
        var narrow=BrowserPaneWidths.Fit(750,preferred);Assert.True(narrow.Navigation+narrow.Right+narrow.CenterMinimum+16<=750);
        var defaults=new BrowserLayout();Assert.Equal(280,BrowserPaneWidths.Fit(1092,defaults).Navigation);
        Assert.Equal(280,BrowserPaneWidths.Fit(1900,defaults).Navigation);
        Assert.Equal(0,BrowserPaneWidths.Fit(1092,defaults).Right);
    }
    [Fact]
    public void ZoomPreferenceNeverIncludesFullscreenOrDiscardsNormalBounds()
    {
        Assert.True(ShellWindowState.RememberZoom(Avalonia.Controls.WindowState.Maximized,false));
        Assert.False(ShellWindowState.RememberZoom(Avalonia.Controls.WindowState.Normal,true));
        Assert.False(ShellWindowState.RememberZoom(Avalonia.Controls.WindowState.FullScreen,true));
        Assert.True(ShellWindowState.RememberZoom(Avalonia.Controls.WindowState.Minimized,true));
        Assert.False(ShellWindowState.RememberZoom(Avalonia.Controls.WindowState.Minimized,false));
    }
    [Theory]
    [InlineData(double.NaN,double.PositiveInfinity)]
    [InlineData(-100,0)]
    public void InvalidViewGeometryIsSafe(double width,double height)
    {
        var saved=new BrowserLayout(double.NaN,double.PositiveInfinity,Window:new ShellWindowGeometry(width,height));
        var valid=saved.Validated();Assert.Equal(280,valid.NavigationWidth);Assert.Equal(360,valid.RightWidth);Assert.Null(valid.Window);
    }
}
