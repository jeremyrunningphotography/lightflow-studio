using System.Windows;
using System.Windows.Controls;
using Xunit;

namespace LightflowStudio.Tests;

[Collection("STA dispatcher tests")]
public sealed class ContextualRightPanelTests
{
    [Fact]
    public async Task UnavailableSurfaceFallsBackWithoutLosingPreferenceOrRecreatingContent()
    {
        await StaDispatcher.RunAsync(() =>
        {
            TestWpfApplication.EnsureLoaded();
            var panel = new ContextualRightPanel();
            var inspector = new Border();
            var subclips = new ListBox { ItemsSource = new[] { "First", "Second" } };
            panel.AddSurface("inspector", "Inspector", inspector);
            panel.SelectSurface("subclips"); // Restored before the lazy Player registers its surface.
            panel.AddSurface("subclips", "Subclips", subclips, available: false);
            Assert.Equal("inspector", panel.ActiveSurface);
            Assert.Equal("subclips", panel.PreferredSurface);
            panel.SetSurfaceAvailable("subclips", true);
            Assert.Equal("subclips", panel.ActiveSurface);
            subclips.SelectedIndex = 1;
            for (var i = 0; i < 5; i++)
            {
                panel.SelectSurface("inspector");
                panel.SelectSurface("subclips");
                panel.Visibility = Visibility.Collapsed;
                panel.Visibility = Visibility.Visible;
                Assert.Same(subclips, ((TabItem)panel.SurfaceTabs.SelectedItem).Content);
                Assert.Equal(1, subclips.SelectedIndex);
            }
            panel.SetSurfaceAvailable("subclips", false);
            Assert.Equal("inspector", panel.ActiveSurface);
            Assert.Equal("subclips", panel.PreferredSurface);
            Assert.Equal(Visibility.Collapsed, ((TabItem)panel.SurfaceTabs.Items[1]).Visibility);
            panel.SetSurfaceAvailable("subclips", true);
            Assert.Equal("subclips", panel.ActiveSurface);
            // A real user selection replaces the remembered preference.
            panel.SurfaceTabs.SelectedIndex = 0;
            panel.SetSurfaceAvailable("subclips", false);
            panel.SetSurfaceAvailable("subclips", true);
            Assert.Equal("inspector", panel.PreferredSurface);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public void WorkspaceAcceptsSubclipsPreferenceAndRejectsUnknownSurface()
    {
        var state = new WorkspaceState { Layout = new() { RightPanelActiveSurface = "subclips" } };
        Assert.Equal("subclips", WorkspaceState.Normalize(state).Layout!.RightPanelActiveSurface);
        Assert.Null(WorkspaceState.Normalize(state with { Layout = new() { RightPanelActiveSurface = "unknown" } }).Layout!.RightPanelActiveSurface);
    }
}
