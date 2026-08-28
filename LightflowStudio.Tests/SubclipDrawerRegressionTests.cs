using System.Xml.Linq;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class SubclipDrawerRegressionTests
{
    [Fact]
    public void DrawerPresentation_HasFocusedHeaderCompactFooterAndExtendedSelection()
    {
        var document = XDocument.Load(Path.Combine(Root(), "LightflowStudio", "PlayerViewerHost.xaml"));
        var shell = XDocument.Load(Path.Combine(Root(), "LightflowStudio", "MainWindow.xaml"));
        var pull = Named(shell, "SubclipsDrawerPullButton");
        var panel = Named(document, "SubclipsPanel");
        var list = Named(document, "SubclipsList");
        var exportButton = Named(document, "ExportSubclipsButton");
        var exportSelected = Named(document, "ExportSelectedSubclipsMenuItem");
        var exportAll = Named(document, "ExportAllSubclipsMenuItem");
        var delete = Named(document, "DeleteSelectedSubclipsButton");
        var actionBar = Named(document, "SubclipsActionBar");
        var title = document.Descendants().Single(element => (string?)element.Attribute("Text") == "SUBCLIPS");
        var names = document.Descendants().Where(element => (string?)element.Attribute("Text") == "{Binding Name}").ToArray();

        Assert.Equal("Collapsed", (string?)pull.Attribute("Visibility"));
        Assert.Equal("Collapsed", (string?)panel.Attribute("Visibility"));
        Assert.Equal("Extended", (string?)list.Attribute("SelectionMode"));
        Assert.Equal("False", (string?)exportSelected.Attribute("IsEnabled"));
        Assert.Equal("Export selected Subclips", (string?)exportSelected.Attribute("AutomationProperties.Name"));
        Assert.Equal("False", (string?)exportAll.Attribute("IsEnabled"));
        Assert.Equal("Export Subclips…", (string?)exportButton.Attribute("Content"));
        Assert.Equal("{StaticResource ExportLaunchButton}", (string?)exportButton.Attribute("Style"));
        Assert.Equal("{StaticResource DangerButton}", (string?)delete.Attribute("Style"));
        Assert.Equal("2", (string?)actionBar.Attribute("Grid.Row"));
        Assert.Equal("0,8,0,0", (string?)actionBar.Attribute("Margin"));
        Assert.Equal("SubclipsPanel_PreviewMouseLeftButtonDown", (string?)panel.Attribute("PreviewMouseLeftButtonDown"));
        Assert.Equal("Saved ranges for this source", (string?)title.Attribute("ToolTip"));
        var header = title.Parent!;
        Assert.Equal(["SUBCLIPS"], header.Elements().Where(element => element.Name.LocalName == "TextBlock")
            .Select(element => (string?)element.Attribute("Text")));
        Assert.Equal(["+ Subclip"], header.Elements().Where(element => element.Name.LocalName == "Button")
            .Select(element => (string?)element.Attribute("Content")));
        Assert.DoesNotContain(document.Descendants(), element =>
            (string?)element.Attribute("Text") == "Saved ranges for this source" && !ReferenceEquals(element, title));
        Assert.Contains(names, element => ((string?)element.Attribute("Foreground"))?.Contains("TextBrush") == true);
        Assert.All(names, element => Assert.NotEqual("Black", (string?)element.Attribute("Foreground")));
        Assert.DoesNotContain(document.Descendants(), element =>
            (string?)element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml")) == "SubclipsStatusText");
    }

    [Fact]
    public void SubclipActions_UseOneOrderedTypedExportPathAndGuardBackgroundClearing()
    {
        var source = File.ReadAllText(Path.Combine(Root(), "LightflowStudio", "PlayerViewerHost.xaml.cs"));
        var request = Body(source, "private void RequestSubclipExport");
        var background = Body(source, "private void SubclipsPanel_PreviewMouseLeftButtonDown");
        var delete = Body(source, "private async void DeleteSelectedSubclips_Click");

        Assert.Contains("!selectedOnly || selectedIds.Contains(item.SubclipId)", request);
        Assert.Contains("_subclipItems.Where", request);
        Assert.Contains("PlayerViewerSubclipsExportRequestedEventArgs(assetId, selected)", request);
        Assert.DoesNotContain("ExportBrowserAssetsAsync", request);
        Assert.Contains("ListBoxItem", background);
        Assert.Contains("ScrollBar", background);
        Assert.Contains("ButtonBase", background);
        Assert.Contains("TextBoxBase", background);
        Assert.Contains("SubclipsList.UnselectAll()", background);
        Assert.Contains("selected.Length > 1", delete);
        Assert.Contains("_subclips.DeleteAsync(assetId", delete);
        Assert.Contains("item.Subclip.Revision", delete);
    }

    [Fact]
    public void PlayerExportLaunchersShareOrangeOutlineStyleWithoutStylingUnrelatedControls()
    {
        var player = XDocument.Load(Path.Combine(Root(), "LightflowStudio", "PlayerViewerHost.xaml"));
        var app = XDocument.Load(Path.Combine(Root(), "LightflowStudio", "App.xaml"));
        var style = app.Descendants().Single(element =>
            (string?)element.Attribute(XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml")) == "ExportLaunchButton");

        Assert.Equal("{StaticResource ExportLaunchButton}", (string?)Named(player, "ExportButton").Attribute("Style"));
        Assert.Equal("{StaticResource ExportLaunchButton}", (string?)Named(player, "ExportSubclipsButton").Attribute("Style"));
        Assert.Contains(style.Elements(), setter => (string?)setter.Attribute("Property") == "BorderBrush" &&
            (string?)setter.Attribute("Value") == "{StaticResource OrangeBrush}");
        Assert.All(player.Descendants().Where(element => element.Name.LocalName == "Button" &&
            (string?)element.Attribute("Style") == "{StaticResource ExportLaunchButton}"), element =>
            Assert.Contains((string?)element.Attribute("Content"), (IEnumerable<string?>)["Export…", "Export Subclips…"]));
    }

    [Fact]
    public void RenamedPanelItemPublishesTheNewDurableNameForTextTooltipAndAutomationBindings()
    {
        var original = new Subclip(Guid.NewGuid(), Guid.NewGuid(), "Original", 7, TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(20), 1, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
        var item = new SubclipPanelItem(original);
        var changed = new List<string?>();
        item.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        item.Replace(original with { Name = "A much longer durable renamed Subclip", Revision = 2 });

        Assert.Equal("A much longer durable renamed Subclip", item.Name);
        Assert.Contains(nameof(SubclipPanelItem.Name), changed);
        Assert.Equal(original.SubclipId, item.SubclipId);
        Assert.Equal((original.In, original.Out), (item.Subclip.In, item.Subclip.Out));
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
    public void BulkDeleteIsDestructiveAndNameRowUsesPersistentVectorRenameAffordance()
    {
        var player = XDocument.Load(Path.Combine(Root(), "LightflowStudio", "PlayerViewerHost.xaml"));
        var delete = Named(player, "DeleteSelectedSubclipsButton");
        var nameRow = Named(player, "SubclipNameRow");
        var name = Named(player, "SubclipNameText");
        var editor = Named(player, "SubclipNameEditor");
        var rename = Named(player, "RenameSubclipButton");

        Assert.Equal("{StaticResource DangerButton}", (string?)delete.Attribute("Style"));
        Assert.Same(nameRow, name.Parent);
        Assert.Same(nameRow, editor.Parent);
        Assert.Same(nameRow, rename.Parent);
        Assert.Equal("*", (string?)nameRow.Descendants().First(element => element.Name.LocalName == "ColumnDefinition").Attribute("Width"));
        Assert.Equal("NoWrap", (string?)name.Attribute("TextWrapping"));
        Assert.Equal("CharacterEllipsis", (string?)name.Attribute("TextTrimming"));
        Assert.Equal("{Binding Name}", (string?)name.Attribute("ToolTip"));
        Assert.Equal("{Binding Name}", (string?)name.Attribute("AutomationProperties.HelpText"));
        Assert.Equal("0", (string?)editor.Attribute("Grid.Column"));
        Assert.Equal("Rename", (string?)rename.Attribute("ToolTip"));
        Assert.Equal("Rename Subclip", (string?)rename.Attribute("AutomationProperties.Name"));
        Assert.Equal("{StaticResource SemanticEditIconGeometry}", (string?)rename.Descendants()
            .Single(element => element.Name.LocalName == "Path").Attribute("Data"));
        Assert.DoesNotContain(player.Descendants(), element =>
            ((string?)element.Attribute("AutomationProperties.Name"))?.StartsWith("Move Subclip", StringComparison.Ordinal) == true);

        var source = File.ReadAllText(Path.Combine(Root(), "LightflowStudio", "PlayerViewerHost.xaml.cs"));
        var begin = Body(source, "private void RenameSubclip_Click");
        var key = Body(source, "private async void SubclipName_KeyDown");
        var commit = Body(source, "private async Task CommitRenameAsync");
        Assert.Contains("editor.Focus(); editor.SelectAll();", begin);
        Assert.Contains("e.Key == Key.Escape", key);
        Assert.Contains("e.Key != Key.Enter", key);
        Assert.Contains("await CommitRenameAsync", key);
        Assert.Contains("await _subclips.RenameAsync(item.SubclipId, item.Subclip.Revision, name)", commit);
        Assert.True(commit.IndexOf("item.Replace(updated)", StringComparison.Ordinal) >
                    commit.IndexOf("await _subclips.RenameAsync", StringComparison.Ordinal));
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
