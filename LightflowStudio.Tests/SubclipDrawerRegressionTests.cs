using System.Xml.Linq;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class SubclipDrawerRegressionTests
{
    [Fact]
    public void DrawerPresentation_HasPlayerPullReadableNamesExtendedSelectionAndNoSubtitleRow()
    {
        var document = XDocument.Load(Path.Combine(Root(), "LightflowStudio", "PlayerViewerHost.xaml"));
        var pull = Named(document, "SubclipsDrawerPullButton");
        var panel = Named(document, "SubclipsPanel");
        var list = Named(document, "SubclipsList");
        var title = document.Descendants().Single(element => (string?)element.Attribute("Text") == "SUBCLIPS");
        var names = document.Descendants().Where(element => (string?)element.Attribute("Text") == "{Binding Name}").ToArray();

        Assert.Equal("Collapsed", (string?)pull.Attribute("Visibility"));
        Assert.Equal("Collapsed", (string?)panel.Attribute("Visibility"));
        Assert.Equal("Extended", (string?)list.Attribute("SelectionMode"));
        Assert.Equal("Saved ranges for this source", (string?)title.Attribute("ToolTip"));
        Assert.DoesNotContain(document.Descendants(), element =>
            (string?)element.Attribute("Text") == "Saved ranges for this source" && !ReferenceEquals(element, title));
        Assert.Contains(names, element => ((string?)element.Attribute("Foreground"))?.Contains("TextBrush") == true);
        Assert.All(names, element => Assert.NotEqual("Black", (string?)element.Attribute("Foreground")));
    }

    [Fact]
    public void ShellCoordinator_IsSingleAuthorityForJobsAndSubclipsMutualExclusion()
    {
        var source = File.ReadAllText(Path.Combine(Root(), "LightflowStudio", "MainWindow.xaml.cs"));
        var coordinator = Body(source, "private void SetRightDrawer");
        var jobsOpen = Body(source, "private void OpenJobsDrawer");

        Assert.Contains("RightDrawerKind", coordinator);
        Assert.Contains("SetSubclipsDrawerOpen(drawer == RightDrawerKind.Subclips)", coordinator);
        Assert.Contains("drawer != RightDrawerKind.Jobs", coordinator);
        Assert.Contains("SetRightDrawer(RightDrawerKind.Jobs)", jobsOpen);
        Assert.Contains("SetRightDrawer(request.Open ? RightDrawerKind.Subclips : RightDrawerKind.None)", source);
    }

    private static XElement Named(XDocument document, string name) => document.Descendants().Single(element =>
        (string?)element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml")) == name);

    private static string Body(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0);
        var next = source.IndexOf("\n    private ", start + signature.Length, StringComparison.Ordinal);
        return source[start..(next < 0 ? source.Length : next)];
    }

    private static string Root()
    {
        var directory = AppContext.BaseDirectory;
        while (directory is not null && !Directory.Exists(Path.Combine(directory, "LightflowStudio")))
            directory = Directory.GetParent(directory)?.FullName;
        return directory ?? throw new DirectoryNotFoundException();
    }
}
