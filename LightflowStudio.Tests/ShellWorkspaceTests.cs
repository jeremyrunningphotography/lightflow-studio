using LightflowStudio;
using System.Xml.Linq;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class ShellWorkspaceTests
{
    [Fact]
    public void BrowserPlayerHomeIsThePermanentShellDefault()
    {
        Assert.Equal(ShellDestination.Home, ShellDestinationSelection.Default);
        Assert.Equal(0, ShellDestinationSelection.Index(ShellDestination.Home));
        Assert.Equal(2, ShellDestinationSelection.Index(ShellDestination.Jobs));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(99)]
    public void InvalidWorkspaceIndexFallsBackToBrowser(int index)
    {
        Assert.Equal(ShellDestination.Home, ShellDestinationSelection.FromIndex(index));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void DefinedDestinationRoundTrips(int value)
    {
        var destination = (ShellDestination)value;
        Assert.Equal(destination, ShellDestinationSelection.FromIndex(ShellDestinationSelection.Index(destination)));
    }

    [Fact]
    public void HeaderHasNoModuleStripAndUtilityMenuExposesOnlySettingsAndAbout()
    {
        var document = XDocument.Load(Path.Combine(FindRepositoryRoot(), "LightflowStudio", "MainWindow.xaml"));
        var names = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
        Assert.DoesNotContain(document.Descendants(), element => (string?)element.Attribute(names + "Name") == "Navigation");
        var menu = document.Descendants().Single(element => (string?)element.Attribute(names + "Name") == "ApplicationMenu");
        Assert.Equal(["Settings", "About"], menu.Elements().Select(element => (string?)element.Attribute("Header")));
        var button = document.Descendants().Single(element => (string?)element.Attribute(names + "Name") == "ApplicationMenuButton");
        Assert.Equal("Application menu", (string?)button.Attribute("AutomationProperties.Name"));
    }

    [Fact]
    public void RetiredCapabilityTabsAreCollapsedAndHaveNoNavigationAutomationNames()
    {
        var xaml = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "LightflowStudio", "MainWindow.xaml"));
        Assert.DoesNotContain("Browser workspace", xaml);
        Assert.DoesNotContain("Export workspace", xaml);
        Assert.DoesNotContain("Jobs workspace", xaml);
        Assert.DoesNotContain("Media Tools", xaml);
        Assert.DoesNotContain("Premiere Helper", xaml);
        Assert.Contains("Compatibility export review", xaml);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "LightflowStudio"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
