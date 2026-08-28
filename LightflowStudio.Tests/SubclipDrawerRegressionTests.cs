using System.Xml.Linq;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class SubclipDrawerRegressionTests
{
    [Fact]
    public void DrawerPresentation_HasShellPullReadableNamesExtendedSelectionAndNoFooterOrSubtitleRow()
    {
        var document = XDocument.Load(Path.Combine(Root(), "LightflowStudio", "PlayerViewerHost.xaml"));
        var shell = XDocument.Load(Path.Combine(Root(), "LightflowStudio", "MainWindow.xaml"));
        var pull = Named(shell, "SubclipsDrawerPullButton");
        var panel = Named(document, "SubclipsPanel");
        var list = Named(document, "SubclipsList");
        var exportSelected = Named(document, "ExportSelectedSubclipsButton");
        var title = document.Descendants().Single(element => (string?)element.Attribute("Text") == "SUBCLIPS");
        var names = document.Descendants().Where(element => (string?)element.Attribute("Text") == "{Binding Name}").ToArray();

        Assert.Equal("Collapsed", (string?)pull.Attribute("Visibility"));
        Assert.Equal("Collapsed", (string?)panel.Attribute("Visibility"));
        Assert.Equal("Extended", (string?)list.Attribute("SelectionMode"));
        Assert.Equal("False", (string?)exportSelected.Attribute("IsEnabled"));
        Assert.Equal("Export selected Subclips", (string?)exportSelected.Attribute("AutomationProperties.Name"));
        Assert.Equal("Saved ranges for this source", (string?)title.Attribute("ToolTip"));
        Assert.DoesNotContain(document.Descendants(), element =>
            (string?)element.Attribute("Text") == "Saved ranges for this source" && !ReferenceEquals(element, title));
        Assert.Contains(names, element => ((string?)element.Attribute("Foreground"))?.Contains("TextBrush") == true);
        Assert.All(names, element => Assert.NotEqual("Black", (string?)element.Attribute("Foreground")));
        Assert.DoesNotContain(document.Descendants(), element =>
            (string?)element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml")) == "SubclipsStatusText");
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
        Assert.Contains("SetSubclipsContextAvailable", source);
        Assert.Contains("_subclipsContextAvailable && homeActive", source);
        Assert.Contains("SubclipsDrawerPull_Click", source);
    }

    [Fact]
    public void DrawerPullsShareOneDpiSafeSwitcherAndDrawerVocabulary()
    {
        var shell = XDocument.Load(Path.Combine(Root(), "LightflowStudio", "MainWindow.xaml"));
        var player = XDocument.Load(Path.Combine(Root(), "LightflowStudio", "PlayerViewerHost.xaml"));
        var app = XDocument.Load(Path.Combine(Root(), "LightflowStudio", "App.xaml"));
        var switcher = Named(shell, "RightDrawerPullSwitcher");
        var jobsPull = Named(shell, "JobsDrawerPullButton");
        var subclipsPull = Named(shell, "SubclipsDrawerPullButton");

        Assert.Equal(switcher, jobsPull.Parent);
        Assert.Equal(switcher, subclipsPull.Parent);
        Assert.Equal("{StaticResource DrawerPullButton}", (string?)jobsPull.Attribute("Style"));
        Assert.Equal("{StaticResource DrawerPullButton}", (string?)subclipsPull.Attribute("Style"));
        Assert.Equal("0,8,0,0", (string?)subclipsPull.Attribute("Margin"));
        Assert.Null(jobsPull.Attribute("VerticalAlignment"));
        Assert.Null(subclipsPull.Attribute("VerticalAlignment"));
        Assert.Equal("{StaticResource DrawerBody}", (string?)Named(shell, "JobsDrawer").Attribute("Style"));
        Assert.Equal("{StaticResource DrawerBody}", (string?)Named(player, "SubclipsPanel").Attribute("Style"));
        Assert.Contains(app.Descendants(), element =>
            (string?)element.Attribute(XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml")) == "DrawerCard");
    }

    [Fact]
    public void BulkDeleteIsDestructiveAndReorderUsesDistinctVectorGeometry()
    {
        var player = XDocument.Load(Path.Combine(Root(), "LightflowStudio", "PlayerViewerHost.xaml"));
        var delete = Named(player, "DeleteSelectedSubclipsButton");
        var reorder = player.Descendants().Where(element => element.Name.LocalName == "Button" &&
            ((string?)element.Attribute("AutomationProperties.Name"))?.StartsWith("Move Subclip", StringComparison.Ordinal) == true).ToArray();

        Assert.Equal("{StaticResource DangerButton}", (string?)delete.Attribute("Style"));
        Assert.Equal(2, reorder.Length);
        var paths = reorder.Select(button => button.Elements().Single(element => element.Name.LocalName == "Path")).ToArray();
        Assert.Equal("{StaticResource SemanticMoveUpIconGeometry}", (string?)paths[0].Attribute("Data"));
        Assert.Equal("{StaticResource SemanticMoveDownIconGeometry}", (string?)paths[1].Attribute("Data"));
        Assert.NotEqual((string?)paths[0].Attribute("Data"), (string?)paths[1].Attribute("Data"));
        Assert.Equal("{Binding CanMoveUp}", (string?)reorder[0].Attribute("IsEnabled"));
        Assert.Equal("{Binding CanMoveDown}", (string?)reorder[1].Attribute("IsEnabled"));
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
