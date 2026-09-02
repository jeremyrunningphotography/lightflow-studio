using System.Xml.Linq;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class BrowserCollectionsPresentationTests
{
    [Fact]
    public void Sidebar_UsesOneSharedVerticalScrollViewerForFolderAndCollectionSections()
    {
        var document = XDocument.Load(Path.Combine(Root(), "LightflowStudio", "MainWindow.xaml"));
        var ns = document.Root!.Name.Namespace;
        var x = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
        var folders = document.Descendants(ns + "TreeView").Single(node => (string?)node.Attribute(x + "Name") == "BrowserFolderTree");
        var collections = document.Descendants(ns + "TreeView").Single(node => (string?)node.Attribute(x + "Name") == "BrowserCollectionTree");
        var folderScroll = folders.Ancestors(ns + "ScrollViewer").Single();
        var collectionScroll = collections.Ancestors(ns + "ScrollViewer").Single();

        Assert.Same(folderScroll, collectionScroll);
        Assert.Equal("BrowserFolderScrollViewer", (string?)folderScroll.Attribute(x + "Name"));
        Assert.Equal("BrowserScopePane_PreviewMouseWheel", (string?)folderScroll.Attribute("PreviewMouseWheel"));
        Assert.Equal("Disabled", (string?)collections.Attribute("ScrollViewer.VerticalScrollBarVisibility"));
    }

    [Fact]
    public void DragFeedback_UsesExplicitHighContrastAdornerInsteadOfTreeItemBorderProperties()
    {
        var code = File.ReadAllText(Path.Combine(Root(), "LightflowStudio", "MainWindow.xaml.cs"));
        Assert.Contains("class CollectionDropAdorner", code);
        Assert.Contains("new System.Windows.Media.Pen(accent, 4)", code);
        Assert.Contains("DrawRoundedRectangle", code);
        Assert.DoesNotContain("item.BorderThickness =", code);
    }

    [Fact]
    public void CollectionHierarchy_UsesDistinctVectorPathsAndNoFilesystemGlyphBinding()
    {
        var document = XDocument.Load(Path.Combine(Root(), "LightflowStudio", "MainWindow.xaml"));
        var ns = document.Root!.Name.Namespace;
        var template = document.Descendants(ns + "HierarchicalDataTemplate")
            .Single(node => (string?)node.Attribute("DataType") == "{x:Type local:BrowserCollectionNode}");
        var paths = template.Descendants(ns + "Path").Select(path => (string?)path.Attribute("Data")).ToArray();

        Assert.Equal(2, paths.Length);
        Assert.Equal(2, paths.Distinct().Count());
        Assert.All(template.Descendants(ns + "Path"), path => Assert.Equal("{StaticResource MutedTextBrush}", (string?)path.Attribute("Fill")));
        Assert.All(template.Descendants(ns + "Viewbox"), icon => Assert.Equal("18", (string?)icon.Attribute("Width")));
        var selected = template.Descendants(ns + "DataTrigger")
            .Single(trigger => (string?)trigger.Attribute("Binding") == "{Binding IsSelected}");
        Assert.Equal(2, selected.Descendants(ns + "Setter").Count(setter =>
            (string?)setter.Attribute("Value") == "{StaticResource ShellFocusBrush}"));
        Assert.DoesNotContain(template.Descendants(), node => ((string?)node.Attribute("Text"))?.Contains("Glyph") == true);
    }

    [Fact]
    public void SidebarSections_CollapseIndependentlyInsideSharedScroller()
    {
        var document = XDocument.Load(Path.Combine(Root(), "LightflowStudio", "MainWindow.xaml"));
        var ns = document.Root!.Name.Namespace;
        var x = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
        var locations = document.Descendants().Single(node => (string?)node.Attribute(x + "Name") == "BrowserLocationsSectionToggle");
        var collections = document.Descendants().Single(node => (string?)node.Attribute(x + "Name") == "BrowserCollectionsSectionToggle");
        Assert.Equal("BrowserScopeSectionToggle_Changed", (string?)locations.Attribute("Checked"));
        Assert.Equal("BrowserScopeSectionToggle_Changed", (string?)collections.Attribute("Unchecked"));
        Assert.Same(locations.Ancestors(ns + "ScrollViewer").Single(), collections.Ancestors(ns + "ScrollViewer").Single());
    }

    [Fact]
    public void HierarchyMenus_ExposeDurableNameSortAndBothCreationCommandsUsePlacementDialog()
    {
        var xaml = File.ReadAllText(Path.Combine(Root(), "LightflowStudio", "MainWindow.xaml"));
        var code = File.ReadAllText(Path.Combine(Root(), "LightflowStudio", "MainWindow.xaml.cs"));
        Assert.Contains("Sort by Name — Ascending", xaml);
        Assert.Contains("Sort by Name — Descending", xaml);
        Assert.Contains("ReorderSetsAsync", code);
        Assert.Contains("ReorderCollectionsAsync", code);
        Assert.Contains("createSet: true", code);
    }

    [Fact]
    public void CollectionModals_SizeToContentWithoutFixedMinimumHeight()
    {
        var ns = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml/presentation");
        foreach (var name in new[] { "ConfirmationDialog.xaml", "NoticeDialog.xaml", "NewCollectionDialog.xaml" })
        {
            var window = XDocument.Load(Path.Combine(Root(), "LightflowStudio", name)).Root!;
            Assert.Equal(ns + "Window", window.Name);
            Assert.Equal("Height", (string?)window.Attribute("SizeToContent"));
            Assert.Null(window.Attribute("MinHeight"));
        }
    }

    private static string Root()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "AGENTS.md"))) current = current.Parent;
        return current?.FullName ?? throw new DirectoryNotFoundException();
    }
}
