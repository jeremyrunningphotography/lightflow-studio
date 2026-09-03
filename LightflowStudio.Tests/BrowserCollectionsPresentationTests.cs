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
        Assert.Contains("new System.Windows.Media.Pen(accent, active ? 4 : 2)", code);
        Assert.Contains("DrawRoundedRectangle(null, new System.Windows.Media.Pen(accent, 2)", code);
        Assert.Contains("Math.Min(BrowserCollectionRowHeight, container.ActualHeight)", code);
        Assert.Contains("Math.Min(BrowserCollectionRowHeight, item.ActualHeight)", code);
        Assert.Contains("ResolveInsertionChoices", code);
        Assert.Contains("line.Edge == BrowserCollectionDropKind.InsertBefore ? 2 : -2", code);
        Assert.Contains("choice.Destination == activeDestination", code);
        Assert.Contains("UpdateLines(lines, activeDestination)", code);
        Assert.DoesNotContain("targetFill", code);
        Assert.DoesNotContain("item.BorderThickness =", code);
    }

    [Fact]
    public void TrailingInsertion_ExtendsTheExistingTreeOwnedDragSurfaceAndAdorner()
    {
        var document = XDocument.Load(Path.Combine(Root(), "LightflowStudio", "MainWindow.xaml"));
        var ns = document.Root!.Name.Namespace;
        var x = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
        var tree = document.Descendants(ns + "TreeView")
            .Single(node => (string?)node.Attribute(x + "Name") == "BrowserCollectionTree");
        var code = File.ReadAllText(Path.Combine(Root(), "LightflowStudio", "MainWindow.xaml.cs"));

        Assert.Equal("0,0,0,96", (string?)tree.Attribute("Padding"));
        Assert.Equal("True", (string?)tree.Attribute("AllowDrop"));
        Assert.DoesNotContain("BrowserCollectionsTrailingDropSurface", document.ToString());
        Assert.Contains("new CollectionDropAdorner(BrowserCollectionTree", code);
        Assert.DoesNotContain("new CollectionDropAdorner(BrowserFolderScrollViewer", code);
        Assert.Contains("TryResolveTrailingInsertion", code);
        Assert.Contains("ShowTrailingCollectionDropFeedback", code);
    }

    [Fact]
    public void DragWheelBridge_IsScopedToTheActiveDragAndLeavesNormalWheelRoutingInPlace()
    {
        var code = File.ReadAllText(Path.Combine(Root(), "LightflowStudio", "MainWindow.xaml.cs"));
        var hook = File.ReadAllText(Path.Combine(Root(), "LightflowStudio", "LowLevelMouseWheelHook.cs"));

        Assert.Contains("BrowserScopePane_PreviewMouseWheel", code);
        Assert.Contains("LowLevelMouseWheelHook.TryInstall(RouteCollectionDragWheel)", code);
        Assert.Contains("_collectionDragWheelHook?.Dispose()", code);
        Assert.Contains("CancelCollectionDragHover();", MethodBody(code, "RouteCollectionDragWheel"));
        Assert.Contains("RefreshCollectionDragFeedback", MethodBody(code, "RouteCollectionDragWheel"));
        Assert.Contains("WhMouseLl = 14", hook);
        Assert.Contains("WmMouseWheel = 0x020A", hook);
    }

    [Fact]
    public void DirectDrop_UsesVisibleHeaderHitTestingAndCollapsedDestinationReveal()
    {
        var code = File.ReadAllText(Path.Combine(Root(), "LightflowStudio", "MainWindow.xaml.cs"));

        Assert.Contains("CollectionTreeItemAtHeader(e.GetPosition(BrowserCollectionTree))", code);
        Assert.Contains("MoveIntoCollectionSetAsync(dragged, drop.Target)", code);
        Assert.Contains("target.IsExpanded = true", code);
        Assert.Contains("RevealCollectionNodeAsync(node.Id)", code);
        Assert.Contains("BrowserTreeScroll.RevealVerticalOffset", code);
        Assert.Contains("BrowserFolderScrollViewer.ScrollToVerticalOffset", code);
    }

    [Fact]
    public void DragHover_UsesDeterministicTimerAndDoesNotCommitHierarchyMutation()
    {
        var code = File.ReadAllText(Path.Combine(Root(), "LightflowStudio", "MainWindow.xaml.cs"));
        var hoverBody = MethodBody(code, "ExpandHoveredCollectionSet");

        Assert.Contains("BrowserCollectionDragHover.Dwell", code);
        Assert.Contains("target.IsExpanded = true", hoverBody);
        Assert.Contains("PersistCollectionExpansionState()", hoverBody);
        Assert.DoesNotContain("Reparent", hoverBody);
        Assert.DoesNotContain("Reorder", hoverBody);
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
        Assert.Contains("ReorderHierarchyAsync", code);
        Assert.Contains("createSet: true", code);
    }

    [Fact]
    public void RoutineMembershipSuccess_UsesStatusTextRatherThanBlockingNotice()
    {
        var source = File.ReadAllText(Path.Combine(Root(), "LightflowStudio", "MainWindow.xaml.cs"));
        var add = MethodBody(source, "private async Task AddBrowserSelectionToCollectionsAsync()");
        var drop = MethodBody(source, "private async void BrowserCollectionTree_Drop(");

        Assert.Contains("BrowserStatusText.Text = CollectionMembershipFeedback.ForAdd", add);
        Assert.Contains("BrowserStatusText.Text = CollectionMembershipFeedback.ForAdd", drop);
        Assert.DoesNotContain("NoticeDialog.Show", add);
        Assert.DoesNotContain("NoticeDialog.Show", drop);
        Assert.DoesNotContain("Source files and paths were not changed", source);
    }

    [Fact]
    public void CollectionRemoval_RequiresConfirmationAndDeleteRoutesOnlyFromCollectionScope()
    {
        var source = File.ReadAllText(Path.Combine(Root(), "LightflowStudio", "MainWindow.xaml.cs"));
        var removal = MethodBody(source, "private async Task RemoveBrowserSelectionFromActiveCollectionAsync()");
        var keyboard = MethodBody(source, "private async void BrowserGridRows_KeyDown(");

        Assert.Contains("ConfirmationDialog.Confirm", removal);
        Assert.Contains("RemoveMembershipsAsync", removal);
        Assert.Contains("BrowserStatusText.Text", removal);
        Assert.Contains("e.Key == Key.Delete && _activeCollectionScope is not null", keyboard);
    }

    [Fact]
    public void CollectionPointerIntentCanOverrideAStaleRevealWithoutChangingHierarchyDragPayload()
    {
        var source = File.ReadAllText(Path.Combine(Root(), "LightflowStudio", "MainWindow.xaml.cs"));
        Assert.Contains("_browserCollectionPointerTarget = _collectionDragNode", source);
        Assert.Contains("BrowserCollectionActivation.ShouldIgnoreDelayedReveal", source);
        Assert.Contains("e.Data.GetData(typeof(BrowserCollectionNode))", source);
        Assert.Contains("e.Data.GetData(typeof(BrowserAssetDragPayload))", source);
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

    private static string MethodBody(string source, string name)
    {
        var start = source.LastIndexOf(name, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Method {name} not found.");
        var brace = source.IndexOf('{', start);
        var depth = 0;
        for (var index = brace; index < source.Length; index++)
        {
            if (source[index] == '{') depth++;
            else if (source[index] == '}' && --depth == 0) return source[brace..(index + 1)];
        }
        throw new InvalidOperationException($"Method {name} body was not closed.");
    }
}
