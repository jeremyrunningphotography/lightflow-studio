using System.Xml.Linq;
using Xunit;

namespace LightflowStudio.Tests;

public class UiLayoutTests
{
    [Fact]
    public void BrowserWorkspace_ExposesFilesystemFirstNavigationAndMediaGrid()
    {
        var document = XDocument.Load(Path.Combine(FindRepositoryRoot(), "LightflowStudio", "MainWindow.xaml"));

        Assert.NotNull(Named(document, "BrowserFolderTree"));
        Assert.NotNull(Named(document, "BrowserGridRows"));
        Assert.Equal("BrowserCurrentPath_KeyDown", (string?)Named(document, "BrowserCurrentPath").Attribute("KeyDown"));
        Assert.Equal("BrowserGo_Click", (string?)Named(document, "BrowserGoButton").Attribute("Click"));
        var ns = document.Root!.Name.Namespace;
        Assert.Equal("BrowserFolderTree_SelectedItemChanged",
            (string?)Named(document, "BrowserFolderTree").Attribute("SelectedItemChanged"));
        Assert.DoesNotContain(document.Descendants(ns + "Button"), element =>
            (string?)element.Attribute("Click") == "BrowserFolder_Click");
        var itemTemplates = Named(document, "BrowserGridRows").Descendants(ns + "DataTemplate").ToList();
        Assert.Equal(2, itemTemplates.Count);
        Assert.DoesNotContain(itemTemplates.SelectMany(template => template.Descendants()), element =>
            ((string?)element.Attribute("Text"))?.Contains("Folder", StringComparison.Ordinal) == true);
        Assert.Equal("BrowserBack_Click", (string?)Named(document, "BrowserBackButton").Attribute("Click"));
        Assert.Equal("BrowserForward_Click", (string?)Named(document, "BrowserForwardButton").Attribute("Click"));
        Assert.Equal("BrowserUp_Click", (string?)Named(document, "BrowserUpButton").Attribute("Click"));
        Assert.Equal("BrowserRefresh_Click", (string?)Named(document, "BrowserRefreshButton").Attribute("Click"));
        Assert.DoesNotContain(document.Descendants(), element =>
            ((string?)element.Attribute("Text"))?.Contains("FOUNDATION PREVIEW", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void BrowserLoadingOverlay_IsARestrainedInCanvasIndicatorRatherThanAModalOrSplashSurface()
    {
        var document = XDocument.Load(Path.Combine(FindRepositoryRoot(), "LightflowStudio", "MainWindow.xaml"));
        var ns = document.Root!.Name.Namespace;
        var overlay = Named(document, "BrowserLoadingOverlay");
        var loadingText = Named(document, "BrowserLoadingText");

        Assert.Equal("Border", overlay.Name.LocalName);
        Assert.Equal(Named(document, "BrowserGridHost"), overlay.Ancestors(ns + "Border").First());
        Assert.DoesNotContain(document.Descendants(ns + "Window"), element => !ReferenceEquals(element, document.Root));
        Assert.DoesNotContain(document.Descendants(ns + "Popup"), element => overlay.Ancestors().Contains(element) || element.Descendants().Contains(overlay));
        Assert.Contains(overlay.Descendants(ns + "ProgressBar"), bar => (string?)bar.Attribute("IsIndeterminate") == "True");
        Assert.Equal(overlay, loadingText.Ancestors(ns + "Border").First());
    }

    [Fact]
    public void BrowserWorkspace_HasNoRedundantTitleBandOrStatusLabelAndStartsCloseToNavigation()
    {
        var root = FindRepositoryRoot();
        var document = XDocument.Load(Path.Combine(root, "LightflowStudio", "MainWindow.xaml"));
        var shell = XDocument.Load(Path.Combine(root, "LightflowStudio", "Themes", "LightflowShell.xaml"));
        var ns = document.Root!.Name.Namespace;
        var xNamespace = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");

        Assert.DoesNotContain(document.Descendants(), element =>
            (string?)element.Attribute("Text") == "MEDIA WORKSPACE");
        Assert.DoesNotContain(document.Descendants(), element =>
            ((string?)element.Attribute("Style"))?.Contains("ShellWorkspaceTitle", StringComparison.Ordinal) == true);
        Assert.DoesNotContain(document.Descendants(), element =>
            ((string?)element.Attribute("Style"))?.Contains("ShellEyebrow", StringComparison.Ordinal) == true);
        Assert.DoesNotContain(shell.Descendants(shell.Root!.Name.Namespace + "Style"), style =>
            (string?)style.Attribute(xNamespace + "Key") is "ShellWorkspaceTitle" or "ShellEyebrow");

        var browserTab = Named(document, "BrowserNavigationColumn").Ancestors(ns + "TabItem").Single();
        var browserRootGrid = browserTab.Element(ns + "Grid")!;
        Assert.DoesNotContain(browserRootGrid.Descendants(ns + "Border"), border =>
            (string?)border.Attribute("BorderThickness") == "0,0,0,1");
        Assert.Null(browserRootGrid.Element(ns + "Grid.RowDefinitions"));

        Assert.DoesNotContain(document.Descendants(), element =>
            (string?)element.Attribute(xNamespace + "Name") == "BrowserWorkspaceStatus");
        Assert.DoesNotContain(document.Descendants(), element =>
            (string?)element.Attribute("Text") == "Choose a Media Root");
        var toolbarGrid = Named(document, "BrowserRefreshButton").Parent!;
        Assert.Contains(toolbarGrid.Elements(ns + "TextBox"), element =>
            (string?)element.Attribute(xNamespace + "Name") == "BrowserCurrentPath");
        Assert.Equal(4, toolbarGrid.Element(ns + "Grid.ColumnDefinitions")!.Elements(ns + "ColumnDefinition").Count());

        Assert.Equal("28,16,28,24", (string?)shell.Root.Elements(shell.Root.Name.Namespace + "Thickness")
            .Single(thickness => (string?)thickness.Attribute(xNamespace + "Key") == "ShellWorkspacePadding"));
    }

    [Fact]
    public void BrowserFolderNavigation_IsBoundedResizableAndScrollsInBothDirections()
    {
        var root = FindRepositoryRoot();
        var document = XDocument.Load(Path.Combine(root, "LightflowStudio", "MainWindow.xaml"));
        var app = XDocument.Load(Path.Combine(root, "LightflowStudio", "App.xaml"));
        var ns = document.Root!.Name.Namespace;
        var column = Named(document, "BrowserNavigationColumn");
        var splitter = Named(document, "BrowserNavigationSplitter");
        var scroller = Named(document, "BrowserFolderScrollViewer");
        var tree = Named(document, "BrowserFolderTree");

        Assert.Equal("280", (string?)column.Attribute("Width"));
        Assert.Equal("220", (string?)column.Attribute("MinWidth"));
        Assert.Equal("520", (string?)column.Attribute("MaxWidth"));
        Assert.Equal("Columns", (string?)splitter.Attribute("ResizeDirection"));
        Assert.Equal("PreviousAndNext", (string?)splitter.Attribute("ResizeBehavior"));
        Assert.Equal("SizeWE", (string?)splitter.Attribute("Cursor"));
        Assert.Equal("8", (string?)splitter.Attribute("Width"));
        Assert.Equal("Transparent", (string?)splitter.Attribute("Background"));
        var splitterTemplate = splitter.Descendants(ns + "ControlTemplate").Single();
        Assert.Empty(splitterTemplate.Descendants(ns + "Border"));
        Assert.All(splitterTemplate.Descendants(ns + "Grid"), grid =>
            Assert.Equal("Transparent", (string?)grid.Attribute("Background")));
        Assert.Equal("Auto", (string?)scroller.Attribute("HorizontalScrollBarVisibility"));
        Assert.Equal("Auto", (string?)scroller.Attribute("VerticalScrollBarVisibility"));
        Assert.Equal("{StaticResource BrowserFolderScrollViewerStyle}", (string?)scroller.Attribute("Style"));
        Assert.Equal("BrowserFolderTree_PreviewMouseWheel", (string?)tree.Attribute("PreviewMouseWheel"));
        Assert.Equal("{x:Null}", (string?)tree.Attribute("FocusVisualStyle"));
        Assert.Equal("Disabled", tree.Attributes().Single(attribute =>
            attribute.Name.LocalName == "ScrollViewer.HorizontalScrollBarVisibility").Value);
        Assert.Equal("Disabled", tree.Attributes().Single(attribute =>
            attribute.Name.LocalName == "ScrollViewer.VerticalScrollBarVisibility").Value);
        Assert.Equal(scroller, tree.Parent);

        var appNs = app.Root!.Name.Namespace;
        var horizontalTrigger = app.Descendants(appNs + "Trigger").Single(trigger =>
            (string?)trigger.Attribute("Property") == "Orientation" &&
            (string?)trigger.Attribute("Value") == "Horizontal");
        Assert.Contains(horizontalTrigger.Elements(appNs + "Setter"), setter =>
            setter.Attribute("TargetName") is null && (string?)setter.Attribute("Property") == "Width" &&
            (string?)setter.Attribute("Value") == "Auto");
        Assert.Contains(horizontalTrigger.Elements(appNs + "Setter"), setter =>
            setter.Attribute("TargetName") is null && (string?)setter.Attribute("Property") == "Height" &&
            (string?)setter.Attribute("Value") == "12");

        var xNamespace = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
        var scrollViewerStyle = app.Descendants(appNs + "Style").Single(style =>
            (string?)style.Attribute(xNamespace + "Key") == "BrowserFolderScrollViewerStyle");
        Assert.Contains(scrollViewerStyle.Descendants(appNs + "ScrollBar"), bar =>
            (string?)bar.Attribute(xNamespace + "Name") == "PART_VerticalScrollBar");
        Assert.Contains(scrollViewerStyle.Descendants(appNs + "ScrollBar"), bar =>
            (string?)bar.Attribute(xNamespace + "Name") == "PART_HorizontalScrollBar");
        Assert.Contains(scrollViewerStyle.Descendants(appNs + "Border"), border =>
            (string?)border.Attribute(xNamespace + "Name") == "ScrollBarCorner" &&
            (string?)border.Attribute("Background") == "#111319");

        var treeItemStyle = app.Descendants(appNs + "Style").Single(style =>
            (string?)style.Attribute(xNamespace + "Key") == "BrowserTreeItemStyle");
        Assert.Contains(treeItemStyle.Elements(appNs + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "FocusVisualStyle" &&
            (string?)setter.Attribute("Value") == "{x:Null}");
        Assert.Contains(treeItemStyle.Descendants(appNs + "DataTrigger"), trigger =>
            ((string?)trigger.Attribute("Binding"))?.Contains("IsSelected", StringComparison.Ordinal) == true);
        Assert.Contains(treeItemStyle.Descendants(appNs + "DataTrigger"), trigger =>
            ((string?)trigger.Attribute("Binding"))?.Contains("IsKeyboardFocused", StringComparison.Ordinal) == true);
        Assert.DoesNotContain(treeItemStyle.Descendants(appNs + "DataTrigger"), trigger =>
            ((string?)trigger.Attribute("Binding"))?.Contains("IsKeyboardFocusWithin", StringComparison.Ordinal) == true);
        var treeTemplate = treeItemStyle.Descendants(appNs + "ControlTemplate").First();
        var expander = treeTemplate.Descendants(appNs + "ToggleButton").Single(button =>
            (string?)button.Attribute(xNamespace + "Name") == "Expander");
        var header = treeTemplate.Descendants(appNs + "Border").Single(border =>
            (string?)border.Attribute(xNamespace + "Name") == "HeaderChrome");
        var itemsHost = treeTemplate.Descendants(appNs + "ItemsPresenter").Single();
        Assert.Equal("28", (string?)expander.Attribute("Height"));
        Assert.Equal("16", (string?)expander.Attribute("Width"));
        Assert.Equal("28", (string?)header.Attribute("Height"));
        Assert.Equal("14,0,0,0", (string?)itemsHost.Attribute("Margin"));

        var hierarchyTemplate = tree.Descendants(ns + "HierarchicalDataTemplate").Single();
        var row = hierarchyTemplate.Elements(ns + "Grid").Single();
        var columns = row.Element(ns + "Grid.ColumnDefinitions")!.Elements(ns + "ColumnDefinition").ToList();
        Assert.Equal("26", (string?)row.Attribute("Height"));
        Assert.Equal(["18", "Auto", "Auto"], columns.Select(column => (string?)column.Attribute("Width")));
        Assert.DoesNotContain(hierarchyTemplate.Descendants(ns + "StackPanel"), _ => true);
        var folderLabel = hierarchyTemplate.Descendants(ns + "TextBlock").Single(text =>
            (string?)text.Attribute(xNamespace + "Name") == "FolderLabel");
        Assert.Equal("8,0,0,0", (string?)folderLabel.Attribute("Margin"));
        Assert.Null(folderLabel.Attribute("Foreground"));

        var shell = XDocument.Load(Path.Combine(root, "LightflowStudio", "Themes", "LightflowShell.xaml"));
        var shellNs = shell.Root!.Name.Namespace;
        Assert.Contains(shell.Root.Elements(shellNs + "Color"), color =>
            (string?)color.Attribute(xNamespace + "Key") == "ShellNavigationTextColor" && color.Value == "#D8D8DC");
        Assert.Contains(treeItemStyle.Elements(appNs + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "Foreground" &&
            (string?)setter.Attribute("Value") == "{StaticResource ShellNavigationTextBrush}");

        var source = File.ReadAllText(Path.Combine(root, "LightflowStudio", "MainWindow.xaml.cs"));
        Assert.Contains("BrowserFolderScrollViewer.ScrollToVerticalOffset", source);
        Assert.Contains("BrowserFolderScrollViewer.ScrollToHorizontalOffset", source);
        Assert.Contains("Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)", source);
        Assert.Contains("BrowserTreeScroll.RevealVerticalOffset", source);
        Assert.Contains("BrowserTreeScroll.RevealHorizontalOffset", source);
        Assert.Contains("BrowserFolderScrollViewer.ScrollToVerticalOffset", source);
        Assert.Contains("BrowserFolderScrollViewer.ScrollToHorizontalOffset", source);
        Assert.DoesNotContain("BringIntoView()", source);

        // Regression: programmatic selection (direct-path entry, Back/Forward, etc.) only sets IsSelected,
        // which drives the tree item's background fill. The focus-ring outline is a separate
        // IsKeyboardFocused-driven style trigger, so revealing a node must also give its container real
        // keyboard focus, or it visually differs from a manually clicked selection.
        var revealStart = source.IndexOf("private void BringBrowserTreeNodeIntoView", StringComparison.Ordinal);
        var revealEnd = source.IndexOf("\n    private", revealStart + 1, StringComparison.Ordinal);
        Assert.True(revealStart >= 0 && revealEnd > revealStart);
        var revealBody = source[revealStart..revealEnd];
        Assert.Contains("container.Focus()", revealBody);
        Assert.True(revealBody.IndexOf("container.Focus()", StringComparison.Ordinal) <
            revealBody.IndexOf("ScrollToVerticalOffset", StringComparison.Ordinal));
        Assert.True(source.IndexOf("RequestBrowserTreeSelection(_browserNavigation.UpTarget);", StringComparison.Ordinal) <
            source.IndexOf("_browserNavigation.UpAsync()", StringComparison.Ordinal));
        Assert.True(source.IndexOf("RequestBrowserTreeSelection(_browserNavigation.BackTarget);", StringComparison.Ordinal) <
            source.IndexOf("_browserNavigation.BackAsync()", StringComparison.Ordinal));
        Assert.True(source.IndexOf("RequestBrowserTreeSelection(_browserNavigation.ForwardTarget);", StringComparison.Ordinal) <
            source.IndexOf("_browserNavigation.ForwardAsync()", StringComparison.Ordinal));
        var selectionIndex = source.IndexOf("RequestBrowserTreeSelection(node);", StringComparison.Ordinal);
        var navigationIndex = source.IndexOf("await RunBrowserNavigationAsync", selectionIndex, StringComparison.Ordinal);
        Assert.True(selectionIndex >= 0 && navigationIndex > selectionIndex,
            "Tree intent selection must occur before asynchronous navigation begins.");
        var runStart = source.IndexOf("private async Task RunBrowserNavigationAsync", StringComparison.Ordinal);
        var applyStart = source.IndexOf("private void ApplyBrowserState", runStart, StringComparison.Ordinal);
        var runBody = source[runStart..applyStart];
        Assert.DoesNotContain("_browserGrid.Populate", runBody);
    }

    [Fact]
    public void ActivityLog_IsCollapsedByDefault()
    {
        var document = XDocument.Load(Path.Combine(FindRepositoryRoot(), "LightflowStudio", "MainWindow.xaml"));
        var ns = document.Root!.Name.Namespace;
        var expander = document.Descendants(ns + "Expander")
            .Single(element => element.Attributes().Any(attribute =>
                attribute.Name.LocalName == "Name" && attribute.Value == "ActivityLogExpander"));

        Assert.Equal("False", (string?)expander.Attribute("IsExpanded"));
    }
    [Fact]
    public void BatchSetup_DirectChildrenOnlyUseDefinedRows()
    {
        var document = XDocument.Load(Path.Combine(FindRepositoryRoot(), "LightflowStudio", "MainWindow.xaml"));
        var ns = document.Root!.Name.Namespace;
        var heading = document.Descendants(ns + "TextBlock")
            .Single(element => (string?)element.Attribute("Text") == "Batch Setup");
        var grid = heading.Parent!;
        var rowCount = grid.Element(ns + "Grid.RowDefinitions")!.Elements(ns + "RowDefinition").Count();
        var assignedRows = grid.Elements()
            .Select(element => (string?)element.Attribute("Grid.Row"))
            .Where(value => int.TryParse(value, out _))
            .Select(value => int.Parse(value!))
            .ToList();

        Assert.NotEmpty(assignedRows);
        Assert.True(assignedRows.Max() < rowCount,
            $"Batch Setup assigns a child to row {assignedRows.Max()}, but only {rowCount} rows are defined.");
    }

    [Fact]
    public void BatchOptions_ArePlacedBesideTheInputsTheyAffect()
    {
        var document = XDocument.Load(Path.Combine(FindRepositoryRoot(), "LightflowStudio", "MainWindow.xaml"));
        var ns = document.Root!.Name.Namespace;
        var recursive = Named(document, "Recursive");
        var inputFolder = Named(document, "InputFolder");
        var overwrite = Named(document, "OverwriteExisting");
        var outputMode = Named(document, "OutputMode");

        Assert.Equal(inputFolder.Parent!.Parent, recursive.Parent);
        Assert.Equal(outputMode.Parent!.Parent, overwrite.Parent!.Parent);
        Assert.Equal(overwrite.Parent, Named(document, "PreserveFolderStructure").Parent);
        Assert.DoesNotContain(document.Descendants(ns + "CheckBox"),
            element => (string?)element.Attribute("Content") == "Skip completed files");
    }

    [Fact]
    public void StartEncoding_IsDisabledUntilBatchRequirementsAreMet()
    {
        var document = XDocument.Load(Path.Combine(FindRepositoryRoot(), "LightflowStudio", "MainWindow.xaml"));

        Assert.Equal("False", (string?)Named(document, "StartButton").Attribute("IsEnabled"));
    }

    [Fact]
    public void BatchFileTable_ConstrainsRowsToTheVisibleColumnWidth()
    {
        var document = XDocument.Load(Path.Combine(FindRepositoryRoot(), "LightflowStudio", "MainWindow.xaml"));
        var list = Named(document, "BatchFileList");

        Assert.Equal("Disabled", list.Attributes()
            .Single(attribute => attribute.Name.LocalName == "ScrollViewer.HorizontalScrollBarVisibility").Value);
        Assert.Equal("Stretch", (string?)list.Attribute("HorizontalContentAlignment"));
    }

    [Fact]
    public void MainWindow_ProvidesEnoughWidthForTheBatchFileColumns()
    {
        var document = XDocument.Load(Path.Combine(FindRepositoryRoot(), "LightflowStudio", "MainWindow.xaml"));

        Assert.True(double.Parse((string?)document.Root!.Attribute("MinWidth") ?? "0") >= 1120);
    }
    [Fact]
    public void EncodingRequirements_UseAnchoredActionableHelp()
    {
        var document = XDocument.Load(Path.Combine(FindRepositoryRoot(), "LightflowStudio", "MainWindow.xaml"));
        var ns = document.Root!.Name.Namespace;

        Assert.Contains(document.Descendants(ns + "TextBlock"), element =>
            (string?)element.Attribute("Text") == "Encoding Requirements");
        Assert.DoesNotContain(document.Descendants(ns + "TextBlock"), element =>
            (string?)element.Attribute("Text") == "Encoding Readiness");
        var button = document.Descendants(ns + "ToggleButton").Single(element =>
            (string?)element.Attribute(XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml") + "Name") == "RequirementHelpButton");
        Assert.Equal("RequirementHelp_MouseLeave", (string?)button.Attribute("MouseLeave"));
        Assert.Contains(document.Descendants(ns + "Popup"), popup =>
            ((string?)popup.Attribute("IsOpen"))?.Contains("RequirementHelpButton") == true);
    }

    [Fact]
    public void BatchConfiguration_KeepsFileExpanderEnabledWhileLockingEditableControls()
    {
        var document = XDocument.Load(Path.Combine(FindRepositoryRoot(), "LightflowStudio", "MainWindow.xaml"));
        var configuration = Named(document, "BatchConfiguration");
        var source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "LightflowStudio", "MainWindow.xaml.cs"));

        Assert.Equal("1", (string?)configuration.Attribute("Grid.Row"));
        Assert.Equal("StackPanel", Named(document, "BatchSourceConfiguration").Name.LocalName);
        Assert.Equal("Border", Named(document, "BatchFileContent").Name.LocalName);
        Assert.Equal("Border", Named(document, "BatchOutputConfiguration").Name.LocalName);
        Assert.Equal("StackPanel", Named(document, "BatchLutConfiguration").Name.LocalName);
        Assert.Equal("StackPanel", Named(document, "BatchFormatConfiguration").Name.LocalName);
        Assert.DoesNotContain("BatchConfiguration.IsEnabled = !running;", source);
        Assert.Contains("BatchSourceConfiguration.IsEnabled = !running;", source);
        Assert.Contains("BatchOutputConfiguration.IsEnabled = !running;", source);
        Assert.Contains("BatchLutConfiguration.IsEnabled = !running;", source);
        Assert.Contains("BatchFormatConfiguration.IsEnabled = !running;", source);
        Assert.Contains("BatchFileContent.IsHitTestVisible = !running;", source);
    }
    [Fact]
    public void PhotographyBranding_IsNotRepeatedInTheStatusBar()
    {
        var document = XDocument.Load(Path.Combine(FindRepositoryRoot(), "LightflowStudio", "MainWindow.xaml"));
        var statusText = Named(document, "StatusText");
        var statusBar = statusText.Parent!;

        Assert.DoesNotContain(statusBar.Descendants(), element =>
            (string?)element.Attribute("Text") == "JEREMY RUNNING PHOTOGRAPHY");
        Assert.Contains(document.Descendants(), element =>
            (string?)element.Attribute("Text") == "JEREMY RUNNING PHOTOGRAPHY");
    }
    [Fact]
    public void NavigationIconsAndLabels_AreVerticallyCenteredInAStableGrid()
    {
        var document = XDocument.Load(Path.Combine(FindRepositoryRoot(), "LightflowStudio", "MainWindow.xaml"));
        var ns = document.Root!.Name.Namespace;
        var navigation = Named(document, "Navigation");
        var items = navigation.Elements(ns + "ListBoxItem").ToList();

        Assert.Equal(7, items.Count);
        foreach (var item in items)
        {
            var grid = item.Element(ns + "Grid")!;
            var text = grid.Elements(ns + "TextBlock").ToList();
            Assert.Equal("22", (string?)grid.Attribute("Height"));
            Assert.All(text, element => Assert.Equal("Center", (string?)element.Attribute("VerticalAlignment")));
            Assert.Equal("Center", (string?)text[0].Attribute("TextAlignment"));
            Assert.Equal("Segoe Fluent Icons, Segoe MDL2 Assets", (string?)text[0].Attribute("FontFamily"));
        }
    }

    [Fact]
    public void PermanentShell_StartsInBrowserAndKeepsExistingWorkspacesReachable()
    {
        var document = XDocument.Load(Path.Combine(FindRepositoryRoot(), "LightflowStudio", "MainWindow.xaml"));
        var ns = document.Root!.Name.Namespace;
        var navigation = Named(document, "Navigation");
        var labels = navigation.Elements(ns + "ListBoxItem")
            .Select(item => item.Descendants(ns + "TextBlock").Last())
            .Select(label => (string?)label.Attribute("Text"))
            .ToList();
        var tabs = Named(document, "MainTabs").Elements(ns + "TabItem").ToList();

        Assert.Equal("0", (string?)Named(document, "MainTabs").Attribute("SelectedIndex"));
        Assert.Equal(["Browser", "Encoding", "Media Tools", "History", "Premiere Helper", "Settings", "About"], labels);
        Assert.Equal(labels.Count, tabs.Count);
        Assert.Contains(tabs[0].Descendants(), element =>
            (string?)element.Attribute(XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml") + "Name") == "BrowserFolderTree");
        Assert.Contains(tabs[1].Descendants(ns + "TextBlock"), text => (string?)text.Attribute("Text") == "Batch Encode");
    }

    [Fact]
    public void Shell_UsesSharedDarkOnlyResourcesAndKeyboardFocusNavigation()
    {
        var root = FindRepositoryRoot();
        var shell = XDocument.Load(Path.Combine(root, "LightflowStudio", "Themes", "LightflowShell.xaml"));
        var window = XDocument.Load(Path.Combine(root, "LightflowStudio", "MainWindow.xaml"));
        var app = File.ReadAllText(Path.Combine(root, "LightflowStudio", "App.xaml"));

        Assert.Contains(shell.Descendants(), element =>
            element.Attributes().Any(attribute => attribute.Name.LocalName == "Key" && attribute.Value == "ShellPanel"));
        Assert.Contains("Themes/LightflowShell.xaml", app);
        Assert.DoesNotContain("LightTheme", app, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Once", (string?)Named(window, "Navigation").Attribute("KeyboardNavigation.TabNavigation"));
        Assert.True(double.Parse((string?)window.Root!.Attribute("MinWidth") ?? "0") >= 1120);
        Assert.True(double.Parse((string?)window.Root.Attribute("MinHeight") ?? "0") >= 720);
    }

    [Fact]
    public void WorkspaceNavigation_IsCompactAndLeavesBrowserLeftEdgeForFolderContext()
    {
        var document = XDocument.Load(Path.Combine(FindRepositoryRoot(), "LightflowStudio", "MainWindow.xaml"));
        var ns = document.Root!.Name.Namespace;
        var navigation = Named(document, "Navigation");
        var itemsPanel = navigation.Element(ns + "ListBox.ItemsPanel")!
            .Descendants(ns + "StackPanel").Single();
        var shellGrid = document.Root.Element(ns + "Grid")!;

        Assert.Equal("Horizontal", (string?)itemsPanel.Attribute("Orientation"));
        Assert.Equal("1", (string?)Named(document, "MainTabs").Attribute("Grid.Row"));
        Assert.Null(shellGrid.Element(ns + "Grid.ColumnDefinitions"));
        Assert.DoesNotContain(navigation.Parent!.Parent!.Descendants(), element =>
            (string?)element.Attribute("Background") == "{StaticResource BrandGradient}");
        Assert.Equal("60", (string?)shellGrid.Element(ns + "Grid.RowDefinitions")!
            .Elements(ns + "RowDefinition").First().Attribute("Height"));
    }

    [Fact]
    public void TrimEditor_UsesLightflowBrandingAndKeepsSeekSeparateFromRangeIndicator()
    {
        var document = XDocument.Load(Path.Combine(FindRepositoryRoot(), "LightflowStudio", "TrimEditorWindow.xaml"));
        var ns = document.Root!.Name.Namespace;
        var slider = Named(document, "PositionSlider");
        var range = Named(document, "EditorRangeIndicator");

        Assert.Equal("Lightflow Studio — Trim Video", (string?)document.Root.Attribute("Title"));
        Assert.Equal("Assets/Branding/LightflowStudio.ico", (string?)document.Root.Attribute("Icon"));
        Assert.Equal("PositionSlider_PreviewMouseLeftButtonDown", (string?)slider.Attribute("PreviewMouseLeftButtonDown"));
        Assert.Equal("PlaybackTimelineSlider", ((string?)slider.Attribute("Style"))?.Split(' ').Last().TrimEnd('}'));
        Assert.Equal("TrimRangeIndicator", range.Name.LocalName);
        Assert.Equal("SeekIn_Click", (string?)Named(document, "InTimeLink").Attribute("Click"));
        Assert.Equal("SeekOut_Click", (string?)Named(document, "OutTimeLink").Attribute("Click"));
        var hitAreaStyle = document.Descendants(ns + "Style").Single(element =>
            element.Attributes().Any(attribute => attribute.Name.LocalName == "Key" && attribute.Value == "PlaybackTimelineHitArea"));
        Assert.Contains(hitAreaStyle.Descendants(ns + "Border"), element =>
            (string?)element.Attribute("Background") == "Transparent");
        Assert.Contains(document.Descendants(ns + "Border"), element =>
            (string?)element.Attribute("Background") == "{StaticResource BrandGradient}");
    }
    private static XElement Named(XDocument document, string name) =>
        document.Descendants().Single(element => element.Attributes().Any(attribute =>
            attribute.Name.LocalName == "Name" && attribute.Value == name));
    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Directory.Build.props"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not find the Lightflow Studio repository root.");
    }
}
