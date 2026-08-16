using LightflowStudio;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class ShellWorkspaceTests
{
    [Fact]
    public void BrowserIsThePermanentShellDefault()
    {
        Assert.Equal(ShellWorkspace.Browser, ShellWorkspaceSelection.Default);
        Assert.Equal(0, ShellWorkspaceSelection.Index(ShellWorkspace.Browser));
        Assert.Equal(1, ShellWorkspaceSelection.Index(ShellWorkspace.Encoding));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(99)]
    public void InvalidWorkspaceIndexFallsBackToBrowser(int index)
    {
        Assert.Equal(ShellWorkspace.Browser, ShellWorkspaceSelection.FromIndex(index));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(6)]
    public void DefinedWorkspaceIndexRoundTrips(int index)
    {
        var workspace = (ShellWorkspace)index;
        Assert.Equal(workspace, ShellWorkspaceSelection.FromIndex(ShellWorkspaceSelection.Index(workspace)));
    }
}
