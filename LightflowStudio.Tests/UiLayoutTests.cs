using System.Xml.Linq;
using Xunit;

namespace LightflowStudio.Tests;

public class UiLayoutTests
{
    [Fact]
    public void BrowserTiles_ExposeIndependentDurableIconsAndTransientThumbnailWorkingIndicator()
    {
        var document = XDocument.Load(Path.Combine(FindRepositoryRoot(), "LightflowStudio", "MainWindow.xaml"));
        var hybrid = Named(document, "BrowserHybridStateOverlay");
        var working = Named(document, "BrowserThumbnailWorkingIndicator");
        Assert.Contains(hybrid.Descendants(), element => ((string?)element.Attribute("Visibility"))?.Contains("HasReviewRange") == true);
        Assert.Contains(hybrid.Descendants(), element => ((string?)element.Attribute("Visibility"))?.Contains("HasSubclips") == true);
        Assert.Contains(hybrid.Descendants(), element => ((string?)element.Attribute("Visibility"))?.Contains("HasColorState") == true);
        Assert.Contains("IsThumbnailGenerating", (string?)working.Attribute("Visibility"));
        Assert.Equal("True", (string?)working.Attribute("IsIndeterminate"));
        Assert.NotNull(working.Ancestors().FirstOrDefault(element => element.Name.LocalName == "Grid")?
            .Elements().FirstOrDefault(element => element.Name.LocalName == "Image"));
    }

    [Fact]
    public void BrowserColorIcon_IsSharedMulticolorVectorVocabulary()
    {
        var document = XDocument.Load(Path.Combine(FindRepositoryRoot(), "LightflowStudio", "MainWindow.xaml"));
        var marker = document.Descendants().Single(element =>
            (string?)element.Attribute(XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml")) == "BrowserColorIcon");
        var fills = marker.Descendants().Where(element => element.Name.LocalName == "Path")
            .Select(element => (string?)element.Attribute("Fill")).Where(value => value is not null).Distinct().ToArray();
        Assert.True(fills.Length >= 6);
        Assert.Contains(marker.Descendants(), element => element.Name.LocalName == "EllipseGeometry");
        Assert.Contains(marker.Descendants(), element => (string?)element.Attribute("AutomationProperties.Name") == "Color Applied");
    }
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
    public void BrowserQueryToolbar_LivesInTheMediaAreaAndNeverInTheLocationsSidebar()
    {
        // #109's UX boundary: the left sidebar chooses scope only; search/filter/sort/view controls belong
        // exclusively to the media-area toolbar. No permanent Filters section is ever added to the sidebar.
        var document = XDocument.Load(Path.Combine(FindRepositoryRoot(), "LightflowStudio", "MainWindow.xaml"));
        var ns = document.Root!.Name.Namespace;
        var locationsPanel = document.Descendants(ns + "TextBlock").Single(tb => (string?)tb.Attribute("Text") == "Locations")
            .Ancestors(ns + "Border").First();
        var toolbar = Named(document, "BrowserQueryToolbar");
        var searchBox = Named(document, "BrowserSearchBox");
        var filterButton = Named(document, "BrowserFilterButton");
        var filterChips = Named(document, "BrowserFilterChips");
        var sortCombo = Named(document, "BrowserSortCombo");
        var directionButton = Named(document, "BrowserSortDirectionButton");
        // Status now lives in its own bar beneath the grid (mockup-driven revision), not inside the toolbar,
        // so it stays with the media it describes instead of competing with the toolbar's controls.
        var statusText = Named(document, "BrowserStatusText");

        Assert.DoesNotContain(locationsPanel.Descendants(), element => element == toolbar);
        Assert.DoesNotContain(locationsPanel.Descendants(), element =>
            element == searchBox || element == filterButton || element == sortCombo || element == directionButton || element == statusText);
        Assert.DoesNotContain(locationsPanel.Descendants(ns + "TextBlock"), tb =>
            ((string?)tb.Attribute("Text"))?.Contains("Filter", StringComparison.OrdinalIgnoreCase) == true);

        Assert.Contains(toolbar.Descendants(), element => element == searchBox);
        Assert.Contains(toolbar.Descendants(), element => element == filterButton);
        Assert.Contains(toolbar.Descendants(), element => element == filterChips);
        Assert.Contains(toolbar.Descendants(), element => element == sortCombo);
        Assert.Contains(toolbar.Descendants(), element => element == directionButton);
        Assert.DoesNotContain(toolbar.Descendants(), element => element == statusText);

        Assert.Equal("BrowserSearchBox_TextChanged", (string?)searchBox.Attribute("TextChanged"));
        Assert.Equal("BrowserSortCombo_SelectionChanged", (string?)sortCombo.Attribute("SelectionChanged"));
        Assert.Equal("BrowserSortDirection_Click", (string?)directionButton.Attribute("Click"));
        Assert.Equal("False", (string?)toolbar.Attribute("IsEnabled"));
    }

    [Fact]
    public void BrowserFilterButton_OpensAdvancedPredicatesWithoutDuplicatingTheToolbarMediaTypeFacet()
    {
        // Progressive disclosure per #109's revised interaction model: Filter ▾ opens a compact predicate
        // editor; it must not be a permanent one-off ComboBox sitting in the everyday toolbar.
        var document = XDocument.Load(Path.Combine(FindRepositoryRoot(), "LightflowStudio", "MainWindow.xaml"));
        var ns = document.Root!.Name.Namespace;
        var filterButton = Named(document, "BrowserFilterButton");
        var popup = Named(document, "BrowserFilterPopup");

        Assert.Equal("ToggleButton", filterButton.Name.LocalName);
        Assert.True(double.Parse((string)filterButton.Attribute("MinWidth")!, System.Globalization.CultureInfo.InvariantCulture) >= 80);
        Assert.Equal("Popup", popup.Name.LocalName);
        Assert.Equal("BrowserFilterButton", ((string?)popup.Attribute("PlacementTarget"))?.Replace("{Binding ElementName=", "").TrimEnd('}'));
        Assert.DoesNotContain(popup.Descendants(ns + "CheckBox"), check =>
            (string?)check.Attribute("Content") is "Images" or "RAW" or "Video");
    }

    [Fact]
    public void BrowserFilterPopup_GroupsIndexedMetadataAndDurableStateInTheExistingProgressiveSurface()
    {
        var document = XDocument.Load(Path.Combine(FindRepositoryRoot(), "LightflowStudio", "MainWindow.xaml"));
        var ns = document.Root!.Name.Namespace;
        var popup = Named(document, "BrowserFilterPopup");
        Assert.Equal("BrowserFilterPopup_Opened", (string?)popup.Attribute("Opened"));
        foreach (var name in new[]
        {
            "BrowserCameraFilterOptions", "BrowserLensFilterOptions", "BrowserCaptureDateFrom", "BrowserCaptureDateTo",
            "BrowserDurationFilterCombo", "BrowserResolutionFilterOptions", "BrowserFrameRateFilterOptions",
            "BrowserStateFilterOptions", "BrowserCameraFilterInformation", "BrowserLensFilterInformation",
            "BrowserResolutionFilterInformation", "BrowserFrameRateFilterInformation"
        })
            Assert.Contains(popup.Descendants(), element => element == Named(document, name));
        Assert.Contains(popup.Descendants(), element => element == Named(document, "BrowserClearAdvancedFiltersButton"));
        Assert.Contains(popup.Descendants(), element => element == Named(document, "BrowserResolutionFilterGroup"));
        Assert.Contains(popup.Descendants(), element => element == Named(document, "BrowserFrameRateFilterGroup"));
        var stateChoice = Named(document, "BrowserStateFilterOptions").Descendants(ns + "CheckBox").Single();
        Assert.Equal("{Binding DisplayLabel}", (string?)stateChoice.Attribute("Content"));
        Assert.Equal("{Binding IsEnabled}", (string?)stateChoice.Attribute("IsEnabled"));
        Assert.DoesNotContain(popup.Descendants(ns + "TextBlock"), text =>
            ((string?)text.Attribute("Text"))?.Contains("hydrated Catalog state", StringComparison.OrdinalIgnoreCase) == true);
    }

    [Fact]
    public void BrowserFilterChips_TemplateBindsToRemovablePredicatesWithAKeyboardAccessibleRemoveControl()
    {
        var document = XDocument.Load(Path.Combine(FindRepositoryRoot(), "LightflowStudio", "MainWindow.xaml"));
        var ns = document.Root!.Name.Namespace;
        var chips = Named(document, "BrowserFilterChips");
        var template = chips.Descendants(ns + "DataTemplate").Single();

        Assert.Equal("{x:Type local:BrowserFilterPredicate}", (string?)template.Attribute("DataType"));
        Assert.Contains(template.Descendants(ns + "TextBlock"), tb => (string?)tb.Attribute("Text") == "{Binding Label}");
        // A Button (not a bare clickable TextBlock) so the remove control is reachable and activatable by keyboard.
        var removeButton = template.Descendants(ns + "Button").Single();
        Assert.Equal("BrowserFilterChip_Remove_Click", (string?)removeButton.Attribute("Click"));
        Assert.Equal("{Binding RemoveAutomationLabel}", (string?)removeButton.Attribute("AutomationProperties.Name"));
    }

    [Fact]
    public void BrowserQueryToolbar_SortComboItemCountMatchesItsEnumSoASelectedIndexCastCannotSilentlyMismatch()
    {
        var document = XDocument.Load(Path.Combine(FindRepositoryRoot(), "LightflowStudio", "MainWindow.xaml"));
        var ns = document.Root!.Name.Namespace;

        var sortItems = Named(document, "BrowserSortCombo").Elements(ns + "ComboBoxItem").ToList();
        Assert.Equal(Enum.GetValues<LightflowStudio.BrowserSortMode>().Length, sortItems.Count);
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
    public void BrowserGridHost_IsTheOneAuthoritativeMediaCanvasBackgroundWithEveryDescendantSurfaceLayerExplicitlyTransparent()
    {
        // #124: hands-on testing found visible vertical bands/seams across the media canvas — present with
        // populated thumbnails, not just the empty state, ruling out an empty-state-specific cause. Two
        // contributing structural issues, fixed together: (1) several layers between BrowserGridHost's own
        // opaque background and an individual tile's own card chrome (the ScrollViewer, the row-virtualizing
        // panel, and each row's own tile-StackPanel) had no Background at all — implicitly transparent in
        // practice, but never guaranteed so by anything other than the absence of a Setter, leaving room for a
        // future implicit style to introduce one unnoticed; (2) UseLayoutRounding was never set anywhere in
        // this subtree, so adjacent same-color panels/rows positioned at fractional device-pixel offsets (this
        // Grid.Row is "*"-sized alongside several "Auto" siblings) could render a faint anti-aliased seam
        // between them even when their fill colors matched exactly. BrowserGridHost's own Background is now
        // the single authoritative source every other layer is explicitly Transparent against, and
        // UseLayoutRounding scopes pixel-snapping to exactly this subtree (deliberately not the whole Window,
        // to keep this change scoped to the reported area).
        var document = XDocument.Load(Path.Combine(FindRepositoryRoot(), "LightflowStudio", "MainWindow.xaml"));
        var ns = document.Root!.Name.Namespace;
        var gridHost = Named(document, "BrowserGridHost");

        Assert.Equal("#0D0F13", (string?)gridHost.Attribute("Background"));
        Assert.Equal("True", (string?)gridHost.Attribute("UseLayoutRounding"));

        var outerGrid = gridHost.Element(ns + "Grid")!;
        Assert.Equal("Transparent", (string?)outerGrid.Attribute("Background"));

        var gridRows = Named(document, "BrowserGridRows");
        Assert.Equal("Transparent", (string?)gridRows.Attribute("Background"));

        var scrollViewer = gridRows.Descendants(ns + "ScrollViewer").Single();
        Assert.Equal("Transparent", (string?)scrollViewer.Attribute("Background"));

        var rowVirtualizingPanel = gridRows.Descendants(ns + "VirtualizingStackPanel").Single();
        Assert.Equal("Transparent", (string?)rowVirtualizingPanel.Attribute("Background"));

        // The per-row tile ItemsControl and its own horizontal StackPanel — the layer immediately hosting each
        // BrowserGridTile card.
        var rowTemplate = gridRows.Descendants(ns + "DataTemplate")
            .Single(template => (string?)template.Attribute("DataType") == "{x:Type local:BrowserGridRow}");
        var tileItemsControl = rowTemplate.Element(ns + "ItemsControl")!;
        Assert.Equal("Transparent", (string?)tileItemsControl.Attribute("Background"));
        var tileStackPanel = tileItemsControl.Element(ns + "ItemsControl.ItemsPanel")!
            .Descendants(ns + "StackPanel").Single();
        Assert.Equal("Transparent", (string?)tileStackPanel.Attribute("Background"));
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
        Assert.Equal(6, toolbarGrid.Element(ns + "Grid.ColumnDefinitions")!.Elements(ns + "ColumnDefinition").Count());
        Assert.Contains(toolbarGrid.Elements(ns + "ToggleButton"), element =>
            (string?)element.Attribute(xNamespace + "Name") == "BrowserIncludeSubfoldersButton");

        Assert.Equal("14,8,14,12", (string?)shell.Root.Elements(shell.Root.Name.Namespace + "Thickness")
            .Single(thickness => (string?)thickness.Attribute(xNamespace + "Key") == "ShellWorkspacePadding"));
    }

    [Fact]
    public void BrowserControls_KeepLocationPermanentAndDeclareWideLowerGroupOrder()
    {
        var document = XDocument.Load(Path.Combine(FindRepositoryRoot(), "LightflowStudio", "MainWindow.xaml"));
        var ns = document.Root!.Name.Namespace;
        var xNamespace = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
        var browseToolbar = Named(document, "BrowserBrowseToolbar");
        var navigationToolbar = Named(document, "BrowserNavigationToolbar");
        var toggle = Named(document, "BrowserIncludeSubfoldersButton");
        var queryToolbar = Named(document, "BrowserQueryToolbar");
        var mediaTypeSegments = Named(document, "BrowserQuickFilterSegments");
        var searchBox = Named(document, "BrowserSearchBox");
        var filterButton = Named(document, "BrowserFilterButton");
        var sortCombo = Named(document, "BrowserSortCombo");

        Assert.Equal(ns + "ToggleButton", toggle.Name);
        var browseColumns = browseToolbar.Element(ns + "Grid.ColumnDefinitions")!.Elements(ns + "ColumnDefinition").ToList();
        Assert.Equal(["*", "Auto"], browseColumns.Select(column => (string?)column.Attribute("Width")));
        Assert.Equal("0", (string?)navigationToolbar.Attribute("Grid.Row"));
        Assert.Equal("2", (string?)navigationToolbar.Attribute("Grid.ColumnSpan"));
        Assert.Equal("2", (string?)queryToolbar.Attribute("Grid.Row"));
        Assert.Contains(toggle.Ancestors(), ancestor => ancestor == navigationToolbar);
        Assert.DoesNotContain(toggle.Ancestors(), ancestor => ancestor == queryToolbar);
        var locationRow = toggle.Parent!;
        var locationSiblings = locationRow.Elements().ToList();
        Assert.True(locationSiblings.IndexOf(Named(document, "BrowserRefreshButton")) < locationSiblings.IndexOf(toggle));

        var toolbarRow = Named(document, "BrowserQueryLayout");
        var mediaTypeChip = mediaTypeSegments.Parent!;
        var searchChip = searchBox.Ancestors().First(ancestor => ancestor.Parent == toolbarRow);
        var sortChip = sortCombo.Ancestors().First(ancestor => ancestor.Parent == toolbarRow);
        var siblings = toolbarRow.Elements().ToList();
        int IndexOf(XElement element) => siblings.IndexOf(element);
        Assert.True(IndexOf(mediaTypeChip) < IndexOf(searchChip));
        Assert.True(IndexOf(searchChip) < IndexOf(filterButton));
        Assert.True(IndexOf(filterButton) < IndexOf(sortChip));

        // Visually distinct from, not merged into, the segmented All/Images/RAW/Video group.
        Assert.DoesNotContain(mediaTypeSegments.Elements(ns + "ToggleButton"), element =>
            (string?)element.Attribute(xNamespace + "Name") == "BrowserIncludeSubfoldersButton");

        Assert.Equal("BrowserIncludeSubfoldersButton_Click", (string?)toggle.Attribute("Click"));
        Assert.Equal("Include Subfolders", (string?)toggle.Attribute("AutomationProperties.Name"));
        Assert.False((bool?)toggle.Attribute("IsEnabled") ?? true, "The toggle must start disabled until a location is open, like Refresh.");

        // Its own style — not #109's Filter ▾ chip style, and not the segmented group's style — so it never
        // reads as a filter facet or as one of the media-type segments.
        Assert.Equal("{StaticResource BrowserScopeToggleButtonStyle}", (string?)toggle.Attribute("Style"));

        // Shares the Locations tree's outline/filled folder vocabulary: an outline folder glyph (U+E8B7, the
        // same one the Locations tree renders while unchecked) that swaps to a small hand-authored Path
        // silhouette (a genuinely solid folder, not a font glyph \u2014 every available Segoe Fluent Icons/MDL2
        // Assets folder codepoint read as "open" rather than filled in hands-on testing) once checked, using
        // only straight-line path segments so its rendering never depends on font/curve fidelity.
        var scopeStyle = document.Descendants(ns + "Style")
            .Single(style => (string?)style.Attribute(xNamespace + "Key") == "BrowserScopeToggleButtonStyle");
        var outlineGlyph = scopeStyle.Descendants(ns + "TextBlock")
            .Single(block => (string?)block.Attribute(xNamespace + "Name") == "ScopeGlyphOutline");
        Assert.Equal("\uE8B7", (string?)outlineGlyph.Attribute("Text"));
        var filledGlyph = scopeStyle.Descendants(ns + "Path")
            .Single(path => (string?)path.Attribute(xNamespace + "Name") == "ScopeGlyphFilled");
        Assert.Equal("Collapsed", (string?)filledGlyph.Attribute("Visibility"));
        Assert.False(string.IsNullOrWhiteSpace((string?)filledGlyph.Attribute("Data")));
        var checkedTrigger = scopeStyle.Descendants(ns + "Trigger")
            .Single(trigger => (string?)trigger.Attribute("Property") == "IsChecked" && (string?)trigger.Attribute("Value") == "True");
        Assert.Contains(checkedTrigger.Elements(ns + "Setter"), setter =>
            (string?)setter.Attribute("TargetName") == "ScopeGlyphOutline" && (string?)setter.Attribute("Property") == "Visibility" &&
            (string?)setter.Attribute("Value") == "Collapsed");
        Assert.Contains(checkedTrigger.Elements(ns + "Setter"), setter =>
            (string?)setter.Attribute("TargetName") == "ScopeGlyphFilled" && (string?)setter.Attribute("Property") == "Visibility" &&
            (string?)setter.Attribute("Value") == "Visible");
        Assert.Contains(scopeStyle.Descendants(ns + "TextBlock"), block => (string?)block.Attribute("Text") == "Subfolders");
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
        // #124 (revised): the recursive-scope outline is gone — folder icons communicate recursive-mode
        // inheritance instead — so the TreeView is once again the ScrollViewer's direct content child.
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
    public void BrowserFolderTreeRow_UsesOutlineFilledFolderIconSemanticsForSelectionAndRecursiveScope()
    {
        // #124 (further revised): the FolderIcon font glyph still read as an "open" folder rather than a
        // genuinely filled one for the active state (every available Segoe Fluent Icons/MDL2 Assets folder
        // codepoint did, in hands-on testing), so the filled state is now a separate, overlaid FolderIconFilled
        // Path silhouette (straight-edged, no arcs, so its rendering cannot depend on font/curve fidelity)
        // toggled via Visibility rather than swapping the outline glyphs own Text. A single DataTrigger, bound
        // to the model's own derived BrowserTreeNode.IsFilledFolderIcon (IsSelected OR IsRecursiveScope,
        // computed once in the model rather than as two independently firing triggers on the same properties
        // \u2014 see BrowserTreeInteractionRegressionTests.TreeRowIconIsDrivenByExactlyOneAuthoritativeDataTriggerNotTwoCompetingOnes
        // for why two competing triggers here was itself a bug), hides the outline and reveals the filled Path.
        var document = XDocument.Load(Path.Combine(FindRepositoryRoot(), "LightflowStudio", "MainWindow.xaml"));
        var ns = document.Root!.Name.Namespace;
        var xNamespace = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
        var tree = Named(document, "BrowserFolderTree");
        var folderIcon = Named(document, "FolderIcon");
        var folderIconFilled = Named(document, "FolderIconFilled");

        Assert.Equal(ns + "TextBlock", folderIcon.Name);
        Assert.Equal("\uE8B7", (string?)folderIcon.Attribute("Text"));
        Assert.False(string.IsNullOrWhiteSpace((string?)folderIcon.Attribute(xNamespace + "Name")));

        Assert.Equal(ns + "Path", folderIconFilled.Name);
        Assert.Equal("Collapsed", (string?)folderIconFilled.Attribute("Visibility"));
        Assert.False(string.IsNullOrWhiteSpace((string?)folderIconFilled.Attribute("Data")));

        var template = tree.Descendants(ns + "HierarchicalDataTemplate").Single();
        var triggers = template.Element(ns + "HierarchicalDataTemplate.Triggers")!;

        var filledTrigger = triggers.Elements(ns + "DataTrigger").Single(trigger =>
            ((string?)trigger.Attribute("Binding"))?.Contains("IsFilledFolderIcon") == true);
        Assert.Equal("True", (string?)filledTrigger.Attribute("Value"));
        Assert.Contains(filledTrigger.Elements(ns + "Setter"), setter =>
            (string?)setter.Attribute("TargetName") == "FolderIcon" && (string?)setter.Attribute("Property") == "Visibility" &&
            (string?)setter.Attribute("Value") == "Collapsed");
        Assert.Contains(filledTrigger.Elements(ns + "Setter"), setter =>
            (string?)setter.Attribute("TargetName") == "FolderIconFilled" && (string?)setter.Attribute("Property") == "Visibility" &&
            (string?)setter.Attribute("Value") == "Visible");

        // Neither IsSelected nor IsRecursiveScope drives the icon directly anymore \u2014 both feed only the
        // derived IsFilledFolderIcon, so trigger-precedence ordering can never let one mask the other.
        Assert.DoesNotContain(triggers.Elements(ns + "DataTrigger"),
            trigger => (string?)trigger.Attribute("Binding") == "{Binding IsSelected}");
        Assert.DoesNotContain(triggers.Elements(ns + "DataTrigger"),
            trigger => (string?)trigger.Attribute("Binding") == "{Binding IsRecursiveScope}");

        // Only the pre-existing Expanded handler remains — no Collapsed EventSetter (unneeded now that
        // icon state is derived per-row from Catalog data rather than the outline's live selection bounds).
        var itemStyle = tree.Descendants(ns + "Style")
            .Single(style => (string?)style.Attribute("TargetType") == "TreeViewItem");
        Assert.Contains(itemStyle.Elements(ns + "EventSetter"), setter =>
            (string?)setter.Attribute("Event") == "Expanded" &&
            (string?)setter.Attribute("Handler") == "BrowserFolderTreeItem_Expanded");
        Assert.DoesNotContain(itemStyle.Elements(ns + "EventSetter"), setter =>
            (string?)setter.Attribute("Event") == "Collapsed");
    }

    [Fact]
    public void BrowserTreeItemStyle_SelectedRowsKeepTheirAccentBorderRegardlessOfKeyboardFocusLocation()
    {
        // #124: hands-on testing found the current Browser location's row appeared to lose its "selected" look
        // entirely the moment keyboard focus moved away from the tree (to the media grid, toolbar, or search
        // box) — the accent HeaderChrome BorderBrush was keyed only to IsKeyboardFocused (true only while this
        // exact TreeViewItem literally holds keyboard focus), while ShellSelectionBrush's own row fill
        // (#282129) is deliberately subtle and, alone, reads as barely distinguishable from an unselected row
        // against this dark theme. The border must persist for as long as the row IS the selected/current
        // location, independent of where keyboard focus currently sits.
        var app = XDocument.Load(Path.Combine(FindRepositoryRoot(), "LightflowStudio", "App.xaml"));
        var ns = app.Root!.Name.Namespace;
        var itemStyle = app.Descendants(ns + "Style").Single(style => style.Attributes()
            .Any(attribute => attribute.Name.LocalName == "Key" && attribute.Value == "BrowserTreeItemStyle"));
        var chromeStyleTriggers = itemStyle.Descendants(ns + "Style")
            .Single(style => (string?)style.Attribute("TargetType") == "Border").Element(ns + "Style.Triggers")!;

        var selectedBorderTrigger = chromeStyleTriggers.Elements(ns + "DataTrigger").Where(trigger =>
            ((string?)trigger.Attribute("Binding"))?.Contains("IsSelected") == true &&
            trigger.Elements(ns + "Setter").Any(setter => (string?)setter.Attribute("Property") == "BorderBrush"));
        Assert.NotEmpty(selectedBorderTrigger);
        Assert.All(selectedBorderTrigger, trigger => Assert.Equal("{StaticResource ShellFocusBrush}",
            (string?)trigger.Elements(ns + "Setter").Single(setter => (string?)setter.Attribute("Property") == "BorderBrush")
                .Attribute("Value")));

        // The background fill still persists for a selected row regardless of focus too.
        var selectedBackgroundTrigger = chromeStyleTriggers.Elements(ns + "DataTrigger").Single(trigger =>
            ((string?)trigger.Attribute("Binding"))?.Contains("IsSelected") == true &&
            trigger.Elements(ns + "Setter").Any(setter => (string?)setter.Attribute("Property") == "Background"));
        Assert.Equal("{StaticResource ShellSelectionBrush}", (string?)selectedBackgroundTrigger
            .Elements(ns + "Setter").Single(setter => (string?)setter.Attribute("Property") == "Background").Attribute("Value"));

        // Keyboard-focus feedback is preserved (not removed), it just no longer gates the selected treatment.
        Assert.Contains(chromeStyleTriggers.Elements(ns + "DataTrigger"), trigger =>
            ((string?)trigger.Attribute("Binding"))?.Contains("IsKeyboardFocused") == true &&
            trigger.Elements(ns + "Setter").Any(setter =>
                (string?)setter.Attribute("Property") == "BorderBrush" &&
                (string?)setter.Attribute("Value") == "{StaticResource ShellFocusBrush}"));
    }

    [Fact]
    public void RecursiveFilledIconGlyphs_AreVectorPathsNotFontGlyphSwapsAndMatchTheOutlineIconsLayoutFootprint()
    {
        // The "filled" state used to swap a font glyph's Text property (first to a two-folder stack, then to
        // U+E838) - both read as an "open" folder rather than a genuinely filled one in hands-on testing. The
        // fix is a small hand-authored Path silhouette shown/hidden via Visibility instead, so this confirms
        // that swap pattern is fully gone (in both the tree row and the toolbar toggle) and that the two icon
        // states occupy the same on-screen footprint (no Margin difference that would shift anything beside them).
        var document = XDocument.Load(Path.Combine(FindRepositoryRoot(), "LightflowStudio", "MainWindow.xaml"));
        var ns = document.Root!.Name.Namespace;

        var textSwapSetters = document.Descendants(ns + "Setter")
            .Where(setter => (string?)setter.Attribute("Property") == "Text" &&
                new[] { "FolderIcon", "ScopeGlyph", "ScopeGlyphOutline" }.Contains((string?)setter.Attribute("TargetName")))
            .ToArray();
        Assert.Empty(textSwapSetters);

        var folderIconFilled = Named(document, "FolderIconFilled");
        Assert.Null(folderIconFilled.Attribute("Margin"));
        Assert.Equal("15", (string?)folderIconFilled.Attribute("Width"));
        Assert.Equal("13", (string?)folderIconFilled.Attribute("Height"));

        var scopeGlyphFilled = Named(document, "ScopeGlyphFilled");
        Assert.Null(scopeGlyphFilled.Attribute("Margin"));
        Assert.Equal("15", (string?)scopeGlyphFilled.Attribute("Width"));
        Assert.Equal("13", (string?)scopeGlyphFilled.Attribute("Height"));
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
    public void ExportRequirements_UseAnchoredActionableHelp()
    {
        var document = XDocument.Load(Path.Combine(FindRepositoryRoot(), "LightflowStudio", "MainWindow.xaml"));
        var ns = document.Root!.Name.Namespace;

        Assert.Contains(document.Descendants(ns + "TextBlock"), element =>
            (string?)element.Attribute("Text") == "Export Requirements");
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
    public void ApplicationUtilityControl_IsCompactAccessibleAndRightAligned()
    {
        var document = XDocument.Load(Path.Combine(FindRepositoryRoot(), "LightflowStudio", "MainWindow.xaml"));
        var button = Named(document, "ApplicationMenuButton");
        Assert.Equal("2", (string?)button.Attribute("Grid.Column"));
        Assert.Equal("36", (string?)button.Attribute("Width"));
        Assert.Equal("Center", (string?)button.Attribute("VerticalAlignment"));
        Assert.Equal("Segoe Fluent Icons, Segoe MDL2 Assets", (string?)button.Attribute("FontFamily"));
        Assert.Equal("Application menu", (string?)button.Attribute("AutomationProperties.Name"));
    }

    [Fact]
    public void PermanentShell_StartsAtBrowserPlayerHomeWithoutAPeerModuleStrip()
    {
        var document = XDocument.Load(Path.Combine(FindRepositoryRoot(), "LightflowStudio", "MainWindow.xaml"));
        var ns = document.Root!.Name.Namespace;
        var tabs = Named(document, "MainTabs").Elements(ns + "TabItem").ToList();

        Assert.Equal("0", (string?)Named(document, "MainTabs").Attribute("SelectedIndex"));
        Assert.DoesNotContain(document.Descendants(), element => element.Attributes().Any(attribute =>
            attribute.Name.LocalName == "Name" && attribute.Value == "Navigation"));
        Assert.Contains(tabs[0].Descendants(), element =>
            (string?)element.Attribute(XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml") + "Name") == "BrowserFolderTree");
        Assert.Equal("Collapsed", (string?)tabs[1].Attribute("Visibility"));
        Assert.Contains(tabs[2].Descendants(), element =>
            (string?)element.Attribute(XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml") + "Name") == "HistoryList");
        Assert.Equal(5, tabs.Count);
    }

    [Fact]
    public void BrowserActions_KeepSelectionActionsSeparateFromStatusPreviewMaintenance()
    {
        var root = FindRepositoryRoot();
        var document = XDocument.Load(Path.Combine(root, "LightflowStudio", "MainWindow.xaml"));
        var app = XDocument.Load(Path.Combine(root, "LightflowStudio", "App.xaml"));
        var ns = document.Root!.Name.Namespace;
        var x = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
        var browse = Named(document, "BrowserBrowseToolbar");
        var actions = Named(document, "BrowserSelectionActionToolbar");
        var contextMenu = Named(document, "BrowserGridRows").Descendants(ns + "ContextMenu").Single();
        var navigation = Named(document, "BrowserNavigationToolbar");
        var query = Named(document, "BrowserQueryToolbar");

        Assert.Equal("0", (string?)browse.Attribute("Grid.Row"));
        Assert.Equal(["*", "Auto"], browse.Element(ns + "Grid.ColumnDefinitions")!.Elements(ns + "ColumnDefinition")
            .Select(column => (string?)column.Attribute("Width")));
        Assert.Equal("0", (string?)navigation.Attribute("Grid.Row"));
        Assert.Equal("2", (string?)query.Attribute("Grid.Row"));
        Assert.Equal("0", (string?)query.Attribute("Grid.Column"));
        Assert.Equal("1", (string?)actions.Attribute("Grid.Column"));
        Assert.Contains(actions.Ancestors(), ancestor => ancestor == browse);
        Assert.DoesNotContain(document.Descendants(ns + "ColumnDefinition"), column =>
            (string?)column.Attribute("Width") == "520" && column.Ancestors().Contains(browse));
        Assert.Equal("2", (string?)actions.Attribute("Grid.Row"));
        Assert.Null(actions.Attribute("Visibility"));
        Assert.DoesNotContain(actions.Descendants(ns + "TextBlock"), text =>
            (string?)text.Attribute("Text") == "SELECTION");
        Assert.DoesNotContain(actions.Descendants(), element =>
            (string?)element.Attribute(x + "Name") == "BrowserSelectionActionSummary");
        Assert.Equal("BrowserExportActionButtonStyle", Named(document, "BrowserExportButton").Attribute("Style")!.Value.Split(' ').Last().TrimEnd('}'));
        var exportStyle = document.Descendants(ns + "Style").Single(style =>
            (string?)style.Attribute(x + "Key") == "BrowserExportActionButtonStyle");
        Assert.Equal("{StaticResource BrowserSelectionActionButtonStyle}", (string?)exportStyle.Attribute("BasedOn"));
        Assert.Equal("{StaticResource ShellFocusBrush}", (string?)exportStyle.Elements(ns + "Setter")
            .Single(setter => (string?)setter.Attribute("Property") == "BorderBrush").Attribute("Value"));
        var baseActionStyle = document.Descendants(ns + "Style").Single(style =>
            (string?)style.Attribute(x + "Key") == "BrowserSelectionActionButtonStyle");
        Assert.Contains(baseActionStyle.Descendants(ns + "Trigger"), trigger =>
            (string?)trigger.Attribute("Property") == "IsEnabled" && (string?)trigger.Attribute("Value") == "False" &&
            trigger.Elements(ns + "Setter").Any(setter => (string?)setter.Attribute("Property") == "Opacity"));
        Assert.Equal("BrowserThumbnailSizeStepButtonStyle", Named(document, "BrowserRegenerateThumbnailsButton").Attribute("Style")!.Value.Split(' ').Last().TrimEnd('}'));
        Assert.Equal("BrowserSelectionLutComboStyle", Named(document, "BrowserCameraLutCombo").Attribute("Style")!.Value.Split(' ').Last().TrimEnd('}'));
        Assert.Equal("BrowserSelectionLutComboStyle", Named(document, "BrowserCreativeLutCombo").Attribute("Style")!.Value.Split(' ').Last().TrimEnd('}'));
        Assert.Equal("150", (string?)document.Descendants(ns + "Style").Single(style =>
            (string?)style.Attribute(x + "Key") == "BrowserSelectionLutComboStyle")
            .Elements(ns + "Setter").Single(setter => (string?)setter.Attribute("Property") == "Width").Attribute("Value"));
        Assert.DoesNotContain(document.Descendants(ns + "Style").Single(style =>
            (string?)style.Attribute(x + "Key") == "BrowserSelectionLutComboStyle").Elements(ns + "Setter"),
            setter => (string?)setter.Attribute("Property") == "MinWidth");
        Assert.Contains(document.Descendants(ns + "Style").Single(style =>
                (string?)style.Attribute(x + "Key") == "BrowserSelectionLutComboStyle").Descendants(ns + "TextBlock"),
            text => (string?)text.Attribute("Text") == "{Binding Label}" &&
                    (string?)text.Attribute("TextTrimming") == "CharacterEllipsis");
        var actionNames = actions.Descendants().Select(element => (string?)element.Attribute(x + "Name"))
            .Where(name => name is not null).ToList();
        Assert.True(actionNames.IndexOf("BrowserCameraLutCombo") < actionNames.IndexOf("BrowserCreativeLutCombo"));
        Assert.DoesNotContain("BrowserRegenerateThumbnailsButton", actionNames);
        var presentationNames = Named(document, "BrowserPresentationControls").Elements()
            .Select(element => (string?)element.Attribute(x + "Name")).Where(name => name is not null).ToList();
        Assert.True(presentationNames.IndexOf("BrowserRegenerateThumbnailsButton") <
                    presentationNames.IndexOf("BrowserThumbnailSizeDecreaseButton"));
        Assert.Equal("1", (string?)Named(document, "BrowserExportButton").Attribute("Grid.Column"));
        Assert.Null(Named(document, "BrowserCameraLutCombo").Attribute("Click"));
        Assert.Null(Named(document, "BrowserCreativeLutCombo").Attribute("Click"));
        Assert.DoesNotContain(document.Descendants(), element =>
            (string?)element.Attribute(x + "Name") == "BrowserEncodeButton");
        Assert.Equal("{StaticResource LightflowContextMenuStyle}", (string?)contextMenu.Attribute("Style"));
        Assert.All(contextMenu.Elements(ns + "MenuItem"), item =>
            Assert.Equal("{StaticResource LightflowMenuItemStyle}", (string?)item.Attribute("Style")));
        Assert.Contains(app.Descendants(ns + "Style"), style => (string?)style.Attribute(x + "Key") == "LightflowContextMenuStyle");
        Assert.Contains(app.Descendants(ns + "Style"), style => (string?)style.Attribute(x + "Key") == "LightflowMenuItemStyle");
        Assert.Equal(
            ["Export", "Regenerate Previews", "Rename", "Camera LUT", "Creative LUT"],
            contextMenu.Elements(ns + "MenuItem").Select(item => (string?)item.Attribute("Header")).ToList());
        Assert.Equal(["Export…", "Export Subclips…"], contextMenu.Elements(ns + "MenuItem").First()
            .Elements(ns + "MenuItem").Select(item => (string?)item.Attribute("Header")).ToList());
        Assert.All(contextMenu.Elements(ns + "MenuItem").TakeLast(2), submenu => Assert.True(submenu.HasElements));
    }

    [Fact]
    public void BrowserRow2_UsesOneAuthoritativeControlHeightAcrossRefineColorAndExport()
    {
        var document = XDocument.Load(Path.Combine(FindRepositoryRoot(), "LightflowStudio", "MainWindow.xaml"));
        var ns = document.Root!.Name.Namespace;
        var x = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
        var heightResource = document.Descendants().Single(element =>
            (string?)element.Attribute(x + "Key") == "BrowserRow2ControlHeight");
        Assert.Equal("34", heightResource.Value);

        foreach (var styleName in new[] { "BrowserToolbarChipStyle", "BrowserToolbarToggleButtonStyle",
                     "BrowserQuickFilterSegmentStyle", "BrowserSelectionActionButtonStyle", "BrowserSelectionLutComboStyle" })
        {
            var style = document.Descendants(ns + "Style").Single(element =>
                (string?)element.Attribute(x + "Key") == styleName);
            Assert.Contains(style.Elements(ns + "Setter"), setter =>
                (string?)setter.Attribute("Property") == "Height" &&
                (string?)setter.Attribute("Value") == "{StaticResource BrowserRow2ControlHeight}");
        }

        Assert.Equal("{StaticResource BrowserRow2ControlHeight}",
            (string?)Named(document, "BrowserSortCombo").Attribute("Height"));
        Assert.Equal("{StaticResource BrowserRow2ControlHeight}",
            (string?)Named(document, "BrowserSortDirectionButton").Attribute("Height"));
        Assert.Equal("Center", (string?)Named(document, "BrowserColorActions").Descendants(ns + "TextBlock")
            .Single(text => (string?)text.Attribute("Text") == "COLOR").Attribute("VerticalAlignment"));
        Assert.Equal("{StaticResource BrowserExportActionButtonStyle}",
            (string?)Named(document, "BrowserExportButton").Attribute("Style"));
        Assert.Equal((string?)Named(document, "BrowserQueryToolbar").Attribute("Padding"),
            (string?)Named(document, "BrowserSelectionActionToolbar").Attribute("Padding"));
    }

    [Fact]
    public void PlayerColorRow_ContainsRightAlignedCurrentAssetExport()
    {
        var root = FindRepositoryRoot();
        var document = XDocument.Load(Path.Combine(root, "LightflowStudio", "PlayerViewerHost.xaml"));
        var ns = document.Root!.Name.Namespace;
        var color = Named(document, "ColorSurface");
        var export = Named(document, "ExportButton");

        Assert.Equal("Stretch", (string?)color.Attribute("HorizontalAlignment"));
        Assert.Equal("6", (string?)export.Attribute("Grid.Column"));
        Assert.Equal("Export…", (string?)export.Attribute("Content"));
        Assert.Equal("ExportButton_Click", (string?)export.Attribute("Click"));
        Assert.Equal("*", (string?)color.Element(ns + "Grid.ColumnDefinitions")!
            .Elements(ns + "ColumnDefinition").ElementAt(5).Attribute("Width"));
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
        Assert.Equal("Application menu", (string?)Named(window, "ApplicationMenuButton").Attribute("AutomationProperties.Name"));
        Assert.True(double.Parse((string?)window.Root!.Attribute("MinWidth") ?? "0") >= 1120);
        Assert.True(double.Parse((string?)window.Root.Attribute("MinHeight") ?? "0") >= 720);
    }

    [Fact]
    public void HeaderUtilityMenu_IsCompactAndLeavesBrowserLeftEdgeForFolderContext()
    {
        var document = XDocument.Load(Path.Combine(FindRepositoryRoot(), "LightflowStudio", "MainWindow.xaml"));
        var ns = document.Root!.Name.Namespace;
        var menuButton = Named(document, "ApplicationMenuButton");
        var shellGrid = document.Root.Element(ns + "Grid")!;

        Assert.Equal("2", (string?)menuButton.Attribute("Grid.Column"));
        Assert.Equal("0", (string?)Named(document, "MainTabs").Attribute("Grid.Column"));
        Assert.Equal("1", (string?)Named(document, "MainTabs").Parent!.Attribute("Grid.Row"));
        Assert.Null(shellGrid.Element(ns + "Grid.ColumnDefinitions"));
        Assert.DoesNotContain(menuButton.Parent!.Descendants(), element =>
            (string?)element.Attribute("Background") == "{StaticResource BrandGradient}");
        Assert.Equal("60", (string?)shellGrid.Element(ns + "Grid.RowDefinitions")!
            .Elements(ns + "RowDefinition").First().Attribute("Height"));
    }

    [Fact]
    public void SharedDensity_UsesCompactWorkspacePanelCardAndControlSpacing()
    {
        var root = FindRepositoryRoot();
        var shell = XDocument.Load(Path.Combine(root, "LightflowStudio", "Themes", "LightflowShell.xaml"));
        var app = XDocument.Load(Path.Combine(root, "LightflowStudio", "App.xaml"));
        var ns = app.Root!.Name.Namespace;
        var x = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");

        Assert.Equal("14,8,14,12", shell.Descendants().Single(element =>
            (string?)element.Attribute(x + "Key") == "ShellWorkspacePadding").Value);
        Assert.Equal("9", shell.Descendants().Single(element =>
            (string?)element.Attribute(x + "Key") == "ShellPanelPadding").Value);
        Assert.Equal("7,4", app.Descendants().Single(element =>
            (string?)element.Attribute(x + "Key") == "CompactControlPadding").Value);
        var card = app.Descendants(ns + "Style").Single(element => (string?)element.Attribute(x + "Key") == "Card");
        Assert.Contains(card.Elements(ns + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "Padding" &&
            (string?)setter.Attribute("Value") == "{StaticResource CompactCardPadding}");
    }

    [Fact]
    public void JobsHeader_PlacesTitleAndActionsOnFirstRowWithSubtitleBelow()
    {
        var document = XDocument.Load(Path.Combine(FindRepositoryRoot(), "LightflowStudio", "MainWindow.xaml"));
        var ns = document.Root!.Name.Namespace;
        var header = Named(document, "JobsHeader");
        var title = header.Elements(ns + "TextBlock").Single(element => (string?)element.Attribute("Text") == "Jobs");
        var subtitle = header.Elements(ns + "TextBlock").Single(element =>
            ((string?)element.Attribute("Text"))?.StartsWith("Manage current") == true);
        var actions = header.Elements(ns + "StackPanel").Single();

        Assert.Null(title.Attribute("Grid.Row"));
        Assert.Equal("1", (string?)subtitle.Attribute("Grid.Row"));
        Assert.Equal("1", (string?)actions.Attribute("Grid.Column"));
        Assert.Equal(new[] { "JobsBackToBrowserButton", "RefreshHistoryButton", "JobsClearHistoryButton" },
            actions.Elements(ns + "Button").Select(button => (string?)button.Attribute(XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml") + "Name")));
    }

    [Fact]
    public void JobsToolbar_UsesOneAuthoritativeHeightForSearchStatusConcurrencyAndQueueGate()
    {
        var document = XDocument.Load(Path.Combine(FindRepositoryRoot(), "LightflowStudio", "MainWindow.xaml"));
        var ns = document.Root!.Name.Namespace;
        var x = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
        var height = document.Descendants().Single(element =>
            (string?)element.Attribute(x + "Key") == "JobsToolbarControlHeight");
        Assert.Equal("34", height.Value);

        foreach (var styleName in new[] { "JobsToolbarTextBoxStyle", "JobsToolbarComboBoxStyle", "JobsToolbarButtonStyle" })
        {
            var style = document.Descendants(ns + "Style").Single(element =>
                (string?)element.Attribute(x + "Key") == styleName);
            Assert.Contains(style.Elements(ns + "Setter"), setter =>
                (string?)setter.Attribute("Property") == "Height" &&
                (string?)setter.Attribute("Value") == "{StaticResource JobsToolbarControlHeight}");
        }

        Assert.Equal("{StaticResource JobsToolbarTextBoxStyle}", (string?)Named(document, "JobsSearchText").Attribute("Style"));
        Assert.Equal("{StaticResource JobsToolbarComboBoxStyle}", (string?)Named(document, "JobsFilter").Attribute("Style"));
        Assert.Equal("{StaticResource JobsToolbarComboBoxStyle}", (string?)Named(document, "FullJobsMaximumExports").Attribute("Style"));
        Assert.Equal("{StaticResource JobsToolbarButtonStyle}", (string?)Named(document, "FullJobsQueueGateButton").Attribute("Style"));
    }

    [Fact]
    public void JobsHeaderButtons_ShareToolbarHeightWhileDangerActionRetainsSemanticStyle()
    {
        var document = XDocument.Load(Path.Combine(FindRepositoryRoot(), "LightflowStudio", "MainWindow.xaml"));
        var ns = document.Root!.Name.Namespace;
        var x = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");

        foreach (var styleName in new[] { "JobsHeaderButtonStyle", "JobsHeaderDangerButtonStyle" })
        {
            var style = document.Descendants(ns + "Style").Single(element =>
                (string?)element.Attribute(x + "Key") == styleName);
            Assert.Contains(style.Elements(ns + "Setter"), setter =>
                (string?)setter.Attribute("Property") == "Height" &&
                (string?)setter.Attribute("Value") == "{StaticResource JobsToolbarControlHeight}");
        }

        var danger = document.Descendants(ns + "Style").Single(element =>
            (string?)element.Attribute(x + "Key") == "JobsHeaderDangerButtonStyle");
        Assert.Equal("{StaticResource DangerButton}", (string?)danger.Attribute("BasedOn"));
        Assert.Equal("{StaticResource JobsHeaderButtonStyle}", (string?)Named(document, "JobsBackToBrowserButton").Attribute("Style"));
        Assert.Equal("{StaticResource JobsHeaderButtonStyle}", (string?)Named(document, "RefreshHistoryButton").Attribute("Style"));
        Assert.Equal("{StaticResource JobsHeaderDangerButtonStyle}", (string?)Named(document, "JobsClearHistoryButton").Attribute("Style"));
    }

    [Fact]
    public void BrowserAndJobsPanels_UseCompactExplicitContainerSpacing()
    {
        var document = XDocument.Load(Path.Combine(FindRepositoryRoot(), "LightflowStudio", "MainWindow.xaml"));

        Assert.Equal("6,5", (string?)Named(document, "BrowserNavigationToolbar").Attribute("Padding"));
        Assert.Equal("5,4", (string?)Named(document, "BrowserQueryToolbar").Attribute("Padding"));
        Assert.Equal("5,4", (string?)Named(document, "BrowserSelectionActionToolbar").Attribute("Padding"));
        Assert.Equal("7", (string?)Named(document, "BrowserGridHost").Attribute("Padding"));
        Assert.Equal("4", (string?)Named(document, "BrowserNavigationGap").Attribute("Height"));
        Assert.Equal("7,6,7,7", (string?)Named(document, "JobsWorkspace").Attribute("Margin"));
        Assert.Equal("1,0,0,5", (string?)Named(document, "JobsHeader").Attribute("Margin"));
        Assert.Equal("0,0,0,5", (string?)Named(document, "JobsToolbar").Attribute("Margin"));
    }

    [Fact]
    public void ExportAndPlayerGroupedSurfaces_DoNotRetainOldDensityOverrides()
    {
        var root = FindRepositoryRoot();
        var export = XDocument.Load(Path.Combine(root, "LightflowStudio", "ExportDialog.xaml"));
        var player = XDocument.Load(Path.Combine(root, "LightflowStudio", "PlayerViewerHost.xaml"));
        var ns = export.Root!.Name.Namespace;

        Assert.Equal("11", (string?)export.Root.Descendants(ns + "Grid").First(grid =>
            grid.Parent == export.Root).Attribute("Margin"));
        var explicitExportPanels = export.Descendants(ns + "Border").Where(border =>
            (string?)border.Attribute("Style") == "{StaticResource ShellPanel}" && border.Attribute("Padding") is not null);
        Assert.All(explicitExportPanels, panel => Assert.Equal("7", (string?)panel.Attribute("Padding")));
        Assert.Equal("7,5,7,5", (string?)Named(player, "TransportBar").Attribute("Padding"));
        Assert.Equal("6,0,6,6", (string?)Named(player, "TransportBar").Attribute("Margin"));
    }

    [Fact]
    public void BrowserCenterHasNoMinimumThatCanOverflowTheDrawerAllocatedColumn()
    {
        var document = XDocument.Load(Path.Combine(FindRepositoryRoot(), "LightflowStudio", "MainWindow.xaml"));
        var center = Named(document, "BrowserCenter");
        var root = Named(document, "BrowserWorkspaceRoot");
        Assert.Null(center.Attribute("MinWidth"));
        Assert.Equal("BrowserWorkspaceRoot_SizeChanged", (string?)root.Attribute("SizeChanged"));
        Assert.Equal("BrowserNavigationSplitter_DragCompleted", (string?)Named(document, "BrowserNavigationSplitter").Attribute("DragCompleted"));
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

    [Fact]
    public void BrowserQuickFilterSegments_OnlyDeclaresTheAllButtonStaticallyLeavingEachCategoryToCodeBehind()
    {
        // "All" isn't a media type, so it alone is hand-declared; one toggle per
        // BrowserGridModel.PresentableCategories is appended in code (InitializeBrowserQuickFilterButtons)
        // so the row can never drift from what the grid actually presents. A hardcoded per-category button
        // here (Images/RAW/Video) would be exactly the drift risk this design avoids.
        var document = XDocument.Load(Path.Combine(FindRepositoryRoot(), "LightflowStudio", "MainWindow.xaml"));
        var ns = document.Root!.Name.Namespace;
        var segments = Named(document, "BrowserQuickFilterSegments");
        var all = Named(document, "BrowserQuickFilterAllButton");
        var toolbar = Named(document, "BrowserQueryToolbar");

        Assert.Equal("ToggleButton", all.Name.LocalName);
        Assert.Contains(toolbar.Descendants(), element => element == all);
        Assert.DoesNotContain("IsChecked", all.Attributes().Select(attribute => attribute.Name.LocalName));
        Assert.Equal("BrowserQuickFilterAllButton_Click", (string?)all.Attribute("Click"));

        Assert.Equal([all], segments.Elements(ns + "ToggleButton"));
        Assert.DoesNotContain(document.Descendants(), element =>
            element.Attributes().Any(attribute => attribute.Name.LocalName == "Name" &&
                attribute.Value is "BrowserQuickFilterImagesButton" or "BrowserQuickFilterRawButton" or "BrowserQuickFilterVideoButton"));
    }

    [Fact]
    public void BrowserSearchBox_HasAPlaceholderOverlayDrivenByItsOwnEmptyText()
    {
        var document = XDocument.Load(Path.Combine(FindRepositoryRoot(), "LightflowStudio", "MainWindow.xaml"));
        var ns = document.Root!.Name.Namespace;
        var searchBox = Named(document, "BrowserSearchBox");
        var placeholder = document.Descendants(ns + "TextBlock")
            .Single(tb => (string?)tb.Attribute("Text") == "Search assets…");

        Assert.Equal("False", (string?)placeholder.Attribute("IsHitTestVisible"));
        Assert.Equal("{Binding Text, ElementName=BrowserSearchBox, Converter={StaticResource StringEmptyToVisibility}}",
            (string?)placeholder.Attribute("Visibility"));
        Assert.Contains("Ctrl+F", (string?)searchBox.Attribute("ToolTip"));
        Assert.Contains(document.Descendants(), element => element.Name.LocalName == "StringEmptyToVisibilityConverter" &&
            element.Attributes().Any(attribute => attribute.Name.LocalName == "Key" && attribute.Value == "StringEmptyToVisibility"));
    }

    [Fact]
    public void BrowserStatusText_LivesInTheSharedApplicationStatusBarRatherThanItsOwnBrowserOnlyStrip()
    {
        // #126: one intentional application-wide status strip, not a Browser-specific bar stacked above an
        // unrelated global one. BrowserStatusText is part of the same Border as the global StatusText.
        var document = XDocument.Load(Path.Combine(FindRepositoryRoot(), "LightflowStudio", "MainWindow.xaml"));
        var ns = document.Root!.Name.Namespace;
        var statusText = Named(document, "BrowserStatusText");
        var appStatusText = Named(document, "StatusText");
        var statusBar = statusText.Ancestors(ns + "Border").First();
        var gridHost = Named(document, "BrowserGridHost");
        var mainTabs = Named(document, "MainTabs");

        Assert.Equal("2", (string?)statusBar.Attribute("Grid.Row"));
        Assert.Equal(statusBar, appStatusText.Ancestors(ns + "Border").First());
        // The shared bar is a sibling of MainTabs (outside the TabControl entirely), not nested inside the
        // Browser tab's own content, so it's unaffected by which tab is active.
        Assert.Equal(mainTabs.Parent!.Parent, statusBar.Parent);
        Assert.DoesNotContain(gridHost.Descendants(), element => element == statusText);
        Assert.DoesNotContain(mainTabs.Descendants(), element => element == statusText);
    }

    [Fact]
    public void BrowserStatusBarSegment_DefaultsToCollapsedInXamlAndTrimsLongText()
    {
        var document = XDocument.Load(Path.Combine(FindRepositoryRoot(), "LightflowStudio", "MainWindow.xaml"));
        var statusText = Named(document, "BrowserStatusText");
        var divider = Named(document, "BrowserStatusDivider");
        var presentationControls = Named(document, "BrowserPresentationControls");

        // Visibility is driven from code (SyncBrowserStatusBarVisibility) based on the active tab, not a
        // XAML default that would show Browser status while some other workspace is active on first paint.
        Assert.Equal("Collapsed", (string?)statusText.Attribute("Visibility"));
        Assert.Equal("Collapsed", (string?)divider.Attribute("Visibility"));
        Assert.Equal("Collapsed", (string?)presentationControls.Attribute("Visibility"));

        // Long/transient status text (e.g. "Generating previews… (N left)") must not crowd out the reserved
        // #125 presentation-control slot at the trailing edge.
        Assert.Equal("CharacterEllipsis", (string?)statusText.Attribute("TextTrimming"));
        Assert.NotNull(statusText.Attribute("MaxWidth"));
    }

    [Fact]
    public void BrowserPresentationControls_HostsTheThumbnailSizeSliderAtTheTrailingEdge()
    {
        // #125 occupies the integration point #126 reserved: the container docked to the right of the
        // Browser status text (i.e., the outermost trailing element in the shared status bar).
        var document = XDocument.Load(Path.Combine(FindRepositoryRoot(), "LightflowStudio", "MainWindow.xaml"));
        var ns = document.Root!.Name.Namespace;
        var statusText = Named(document, "BrowserStatusText");
        var presentationControls = Named(document, "BrowserPresentationControls");
        var slider = Named(document, "BrowserThumbnailSizeSlider");
        var statusBar = statusText.Ancestors(ns + "Border").First();
        var dockPanel = statusBar.Descendants(ns + "DockPanel").Single();

        Assert.Contains(presentationControls.Descendants(), element => element == slider);
        Assert.Equal("Right", (string?)presentationControls.Attribute("DockPanel.Dock"));
        Assert.Equal("Right", (string?)statusText.Attribute("DockPanel.Dock"));
        var dockedRightInOrder = dockPanel.Elements()
            .Where(element => (string?)element.Attribute("DockPanel.Dock") == "Right").ToList();
        // Declaration order for same-side DockPanel children maps to right-to-left visual order, so
        // BrowserPresentationControls must be declared first among them to land at the true trailing edge.
        Assert.Equal(presentationControls, dockedRightInOrder.First());
    }

    [Fact]
    public void BrowserPresentationControls_OrderRegenerateImmediatelyBeforePreviewSizeControls()
    {
        // The small icon (decrease) reads left-to-right before the slider before the large icon (increase),
        // matching the visual "small ... large" convention the mockup called for.
        var document = XDocument.Load(Path.Combine(FindRepositoryRoot(), "LightflowStudio", "MainWindow.xaml"));
        var presentationControls = Named(document, "BrowserPresentationControls");
        var regenerate = Named(document, "BrowserRegenerateThumbnailsButton");
        var decreaseButton = Named(document, "BrowserThumbnailSizeDecreaseButton");
        var slider = Named(document, "BrowserThumbnailSizeSlider");
        var increaseButton = Named(document, "BrowserThumbnailSizeIncreaseButton");

        var elements = presentationControls.Elements().ToList();
        var regenerateIndex = elements.IndexOf(regenerate);
        Assert.True(regenerateIndex >= 0);
        Assert.Equal([regenerate, decreaseButton, slider, increaseButton], elements.Skip(regenerateIndex).Take(4));
    }

    [Fact]
    public void BrowserThumbnailSizeStepButtons_AreRealButtonsNotDecorativeElementsWithRawMouseHandling()
    {
        var document = XDocument.Load(Path.Combine(FindRepositoryRoot(), "LightflowStudio", "MainWindow.xaml"));
        var decreaseButton = Named(document, "BrowserThumbnailSizeDecreaseButton");
        var increaseButton = Named(document, "BrowserThumbnailSizeIncreaseButton");

        Assert.Equal("Button", decreaseButton.Name.LocalName);
        Assert.Equal("Button", increaseButton.Name.LocalName);
        Assert.Equal("{StaticResource BrowserThumbnailSizeStepButtonStyle}", (string?)decreaseButton.Attribute("Style"));
        Assert.Equal("{StaticResource BrowserThumbnailSizeStepButtonStyle}", (string?)increaseButton.Attribute("Style"));
        Assert.Equal("BrowserThumbnailSizeDecreaseButton_Click", (string?)decreaseButton.Attribute("Click"));
        Assert.Equal("BrowserThumbnailSizeIncreaseButton_Click", (string?)increaseButton.Attribute("Click"));
        // Not a raw MouseDown/MouseLeftButtonDown handler on a TextBlock/Border — Button's own Click already
        // gives keyboard (Enter/Space) activation and focus for free.
        Assert.Null(decreaseButton.Attribute("MouseLeftButtonDown"));
        Assert.Null(increaseButton.Attribute("MouseLeftButtonDown"));
    }

    [Fact]
    public void BrowserPreviewControls_HaveProductTerminologyAndAccessibleNames()
    {
        var document = XDocument.Load(Path.Combine(FindRepositoryRoot(), "LightflowStudio", "MainWindow.xaml"));
        var decreaseButton = Named(document, "BrowserThumbnailSizeDecreaseButton");
        var increaseButton = Named(document, "BrowserThumbnailSizeIncreaseButton");

        Assert.Equal("Decrease Preview size", (string?)decreaseButton.Attribute("ToolTip"));
        Assert.Equal("Decrease Preview size", (string?)decreaseButton.Attribute("AutomationProperties.Name"));
        Assert.Equal("Increase Preview size", (string?)increaseButton.Attribute("ToolTip"));
        Assert.Equal("Increase Preview size", (string?)increaseButton.Attribute("AutomationProperties.Name"));
        var regenerate = Named(document, "BrowserRegenerateThumbnailsButton");
        Assert.Equal("Regenerate Previews", (string?)regenerate.Attribute("ToolTip"));
        Assert.Equal("Regenerate Previews", (string?)regenerate.Attribute("AutomationProperties.Name"));
        Assert.Equal("BrowserPresentationControls", (string?)regenerate.Parent?.Attribute(
            XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml") + "Name"));
    }

    [Fact]
    public void BrowserThumbnailSizeStepButtons_DoNotDeclareAnIsEnabledDefaultInXaml()
    {
        // Same InitializeComponent-timing hazard already documented for the slider/BrowserSortCombo/the
        // filter checkboxes (see #126): IsEnabled is set purely from code, in
        // MainWindow.ApplyBrowserThumbnailSize, so it can never be stale or fire mid-InitializeComponent.
        var document = XDocument.Load(Path.Combine(FindRepositoryRoot(), "LightflowStudio", "MainWindow.xaml"));
        var decreaseButton = Named(document, "BrowserThumbnailSizeDecreaseButton");
        var increaseButton = Named(document, "BrowserThumbnailSizeIncreaseButton");

        Assert.DoesNotContain("IsEnabled", decreaseButton.Attributes().Select(attribute => attribute.Name.LocalName));
        Assert.DoesNotContain("IsEnabled", increaseButton.Attributes().Select(attribute => attribute.Name.LocalName));
    }

    [Fact]
    public void BrowserThumbnailSizeStepButtonStyle_HasDisabledFocusAndHoverTreatmentConsistentWithTheDarkTheme()
    {
        var document = XDocument.Load(Path.Combine(FindRepositoryRoot(), "LightflowStudio", "MainWindow.xaml"));
        var ns = document.Root!.Name.Namespace;
        var style = document.Descendants(ns + "Style")
            .Single(element => element.Attributes().Any(attribute => attribute.Name.LocalName == "Key" && attribute.Value == "BrowserThumbnailSizeStepButtonStyle"));
        var triggers = style.Descendants(ns + "Trigger").ToList();

        Assert.Contains(triggers, trigger => (string?)trigger.Attribute("Property") == "IsEnabled" && (string?)trigger.Attribute("Value") == "False");
        Assert.Contains(triggers, trigger => (string?)trigger.Attribute("Property") == "IsMouseOver" && (string?)trigger.Attribute("Value") == "True");
        Assert.Contains(triggers, trigger => (string?)trigger.Attribute("Property") == "IsPressed" && (string?)trigger.Attribute("Value") == "True");
        Assert.Contains(triggers, trigger => (string?)trigger.Attribute("Property") == "IsKeyboardFocused" && (string?)trigger.Attribute("Value") == "True");
    }

    [Fact]
    public void BrowserThumbnailSizeSlider_DoesNotDeclareAValueDefaultInXaml()
    {
        // Same InitializeComponent-timing hazard already documented for BrowserSortCombo/the filter
        // checkboxes/MainTabs (see #126): a literal Value would let WPF raise ValueChanged before
        // BrowserGridHost and the tile DynamicResources are connected. The restored/default size is applied
        // from code only, in ApplyRestoredWorkspaceLayout, after InitializeComponent returns.
        var document = XDocument.Load(Path.Combine(FindRepositoryRoot(), "LightflowStudio", "MainWindow.xaml"));
        var slider = Named(document, "BrowserThumbnailSizeSlider");

        Assert.DoesNotContain("Value", slider.Attributes().Select(attribute => attribute.Name.LocalName));
        Assert.Equal("BrowserThumbnailSizeSlider_ValueChanged", (string?)slider.Attribute("ValueChanged"));
    }

    [Fact]
    public void BrowserThumbnailSizeSlider_RangeMatchesTheRealThumbnailSizeLevelCountAndSnapsToDiscreteNotches()
    {
        // Cross-checked against the actual enum/list, not a hardcoded duplicate: if a future change adds or
        // removes a BrowserThumbnailSize level without updating the slider's Minimum/Maximum, this fails.
        var document = XDocument.Load(Path.Combine(FindRepositoryRoot(), "LightflowStudio", "MainWindow.xaml"));
        var slider = Named(document, "BrowserThumbnailSizeSlider");

        Assert.Equal("0", (string?)slider.Attribute("Minimum"));
        Assert.Equal((BrowserGridLayout.ThumbnailSizes.Count - 1).ToString(), (string?)slider.Attribute("Maximum"));
        Assert.Equal("True", (string?)slider.Attribute("IsSnapToTickEnabled"));
        Assert.Equal("1", (string?)slider.Attribute("TickFrequency"));
    }

    [Fact]
    public void BrowserThumbnailSizeSlider_IsAccessibleThroughToolTipAndAutomationNameRatherThanAPermanentLabel()
    {
        var document = XDocument.Load(Path.Combine(FindRepositoryRoot(), "LightflowStudio", "MainWindow.xaml"));
        var slider = Named(document, "BrowserThumbnailSizeSlider");

        Assert.False(string.IsNullOrWhiteSpace((string?)slider.Attribute("ToolTip")));
        Assert.False(string.IsNullOrWhiteSpace((string?)slider.Attribute("AutomationProperties.Name")));
    }

    [Fact]
    public void BrowserGridTile_WidthAndThumbnailAreaHeightComeFromTheSharedDynamicResourcesRatherThanHardcodedLiterals()
    {
        // #125: these must be DynamicResource (not StaticResource or a literal) so every realized/recycled
        // tile picks up MainWindow.ApplyBrowserThumbnailSize's updates live, including tiles the slider
        // changes while virtualized out of view.
        var document = XDocument.Load(Path.Combine(FindRepositoryRoot(), "LightflowStudio", "MainWindow.xaml"));
        var ns = document.Root!.Name.Namespace;
        var tileTemplate = document.Descendants(ns + "DataTemplate")
            .Single(template => (string?)template.Attribute("DataType") == "{x:Type local:BrowserGridTile}");
        var tileBorder = tileTemplate.Elements(ns + "Border").Single();
        var thumbnailBorder = Named(document, "BrowserPreviewSurface");

        Assert.Equal("{DynamicResource BrowserTileWidth}", (string?)tileBorder.Attribute("Width"));
        Assert.Contains(thumbnailBorder.Descendants(ns + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "Height" &&
            (string?)setter.Attribute("Value") == "{DynamicResource BrowserTileThumbnailHeight}");
    }

    [Fact]
    public void BrowserViewModes_UseInfoFramesHybridOverlayAndSharedDistinctVectorIcons()
    {
        var document = XDocument.Load(Path.Combine(FindRepositoryRoot(), "LightflowStudio", "MainWindow.xaml"));
        var ns = document.Root!.Name.Namespace;
        Assert.NotNull(Named(document, "BrowserInfoUpperFrame"));
        Assert.NotNull(Named(document, "BrowserInfoLowerFrame"));
        Assert.NotNull(Named(document, "BrowserHybridStateOverlay"));
        foreach (var frameName in new[] { "BrowserInfoUpperFrame", "BrowserInfoLowerFrame" })
        {
            var frameStyle = Named(document, frameName).Descendants(ns + "Style").Single();
            Assert.Contains(frameStyle.Elements(ns + "Setter"), setter =>
                (string?)setter.Attribute("Property") == "Visibility" && (string?)setter.Attribute("Value") == "Collapsed");
            Assert.All(frameStyle.Descendants(ns + "DataTrigger"), trigger =>
                Assert.Equal("{x:Static local:BrowserViewMode.Info}", (string?)trigger.Attribute("Value")));
        }
        var hybridStyle = Named(document, "BrowserHybridStateOverlay").Descendants(ns + "Style").Single();
        Assert.Contains(hybridStyle.Elements(ns + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "Visibility" && (string?)setter.Attribute("Value") == "Collapsed");
        Assert.Contains(hybridStyle.Descendants(ns + "Condition"), condition =>
            (string?)condition.Attribute("Value") == "{x:Static local:BrowserViewMode.Hybrid}");
        Assert.Equal(3, new[] { "BrowserPreviewViewButton", "BrowserInfoViewButton", "BrowserHybridViewButton" }
            .Count(name => Named(document, name).Name == ns + "ToggleButton"));
        foreach (var key in new[] { "BrowserReviewRangeIcon", "BrowserSubclipsIcon", "BrowserColorIcon" })
            Assert.Single(document.Descendants(ns + "DataTemplate"), template =>
                (string?)template.Attribute(XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml")) == key);
        var spacingStyle = document.Descendants(ns + "Style").Single(style =>
            (string?)style.Attribute(XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml")) == "BrowserStateIconItemStyle");
        Assert.Contains(spacingStyle.Elements(ns + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "Margin" &&
            (string?)setter.Attribute("Value") == "{DynamicResource BrowserStateIconSpacing}");
        Assert.DoesNotContain(Named(document, "BrowserHybridStateOverlay").Descendants(ns + "ContentPresenter"),
            presenter => presenter.Attribute("Margin") is not null);
        Assert.DoesNotContain(Named(document, "BrowserInfoLowerFrame").Descendants(ns + "ContentPresenter"),
            presenter => presenter.Attribute("Margin") is not null);

        var filenameRow = Named(document, "BrowserFilenameRow");
        var filenameStyle = filenameRow.Descendants(ns + "Style").Single();
        Assert.Contains(filenameStyle.Elements(ns + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "Visibility" && (string?)setter.Attribute("Value") == "Visible");
        var previewCollapse = filenameStyle.Descendants(ns + "DataTrigger").Single();
        Assert.Equal("{Binding ViewMode}", (string?)previewCollapse.Attribute("Binding"));
        Assert.Equal("{x:Static local:BrowserViewMode.Preview}", (string?)previewCollapse.Attribute("Value"));
        Assert.Contains(previewCollapse.Elements(ns + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "Visibility" && (string?)setter.Attribute("Value") == "Collapsed");

        var tileTemplate = document.Descendants(ns + "DataTemplate")
            .Single(template => (string?)template.Attribute("DataType") == "{x:Type local:BrowserGridTile}");
        var tileBorder = tileTemplate.Elements(ns + "Border").Single();
        Assert.Equal("{Binding Name}", (string?)tileBorder.Attribute("ToolTip"));
        Assert.Equal("{Binding AutomationLabel}", (string?)tileBorder.Attribute("AutomationProperties.Name"));
        Assert.DoesNotContain(filenameStyle.Descendants(ns + "DataTrigger"), trigger =>
            (string?)trigger.Attribute("Value") is "{x:Static local:BrowserViewMode.Info}" or "{x:Static local:BrowserViewMode.Hybrid}");
    }

    [Fact]
    public void BrowserLiveDurableStateHandlers_PublishCommittedFlagThenRefreshAuthoritativeQueryProjection()
    {
        var behavior = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "LightflowStudio", "MainWindow.xaml.cs"));
        var handlersStart = behavior.IndexOf("private void EnsurePlayerViewerHost()", StringComparison.Ordinal);
        var handlersEnd = behavior.IndexOf("private void BrowserViewMode_Click", handlersStart, StringComparison.Ordinal);
        Assert.True(handlersStart >= 0 && handlersEnd > handlersStart);
        var handlers = behavior[handlersStart..handlersEnd];

        Assert.Contains("ApplyCommittedBrowserAssetStateFlag(change.AssetId, BrowserAssetState.ReviewRange", handlers,
            StringComparison.Ordinal);
        Assert.Contains("ApplyCommittedBrowserAssetStateFlag(change.AssetId, BrowserAssetState.Color", handlers,
            StringComparison.Ordinal);
        Assert.Contains("ApplyCommittedBrowserAssetStateFlag(change.AssetId, BrowserAssetState.Subclips", handlers,
            StringComparison.Ordinal);
        Assert.Contains("_browserGrid.ApplyAssetStateFlag(assetId, flag, enabled);", handlers, StringComparison.Ordinal);
        Assert.Contains("RefreshCommittedBrowserAssetQueryStateAsync(assetId, revision)", handlers, StringComparison.Ordinal);
        Assert.Contains("BrowserAssetStates.GetQueryStatesAsync([assetId])", handlers, StringComparison.Ordinal);
        Assert.Contains("_browserGrid.ApplyAssetState(assetId, state);", handlers, StringComparison.Ordinal);
        Assert.Contains("ApplyCommittedBrowserAssetStateFlag(id, BrowserAssetState.Color, committed[id].HasColor);",
            behavior, StringComparison.Ordinal);
    }

    [Fact]
    public void GlobalStatusText_HasNoXamlVisibilityToggleAndAlwaysOccupiesTheFillArea()
    {
        // App health must never disappear just because the Browser tab isn't active — only the Browser
        // segment is conditionally shown; the global segment is the DockPanel's fill (undocked, last child).
        var document = XDocument.Load(Path.Combine(FindRepositoryRoot(), "LightflowStudio", "MainWindow.xaml"));
        var ns = document.Root!.Name.Namespace;
        var appStatusText = Named(document, "StatusText");
        var statusBar = appStatusText.Ancestors(ns + "Border").First();
        var dockPanel = statusBar.Descendants(ns + "DockPanel").Single();

        Assert.Null(appStatusText.Attribute("Visibility"));
        Assert.Null(appStatusText.Attribute("DockPanel.Dock"));
        Assert.Equal(appStatusText, dockPanel.Elements().Last());
    }

    [Fact]
    public void MainTabs_WiresSelectionChangedToKeepTheStatusBarSegmentInSync()
    {
        var document = XDocument.Load(Path.Combine(FindRepositoryRoot(), "LightflowStudio", "MainWindow.xaml"));
        var mainTabs = Named(document, "MainTabs");
        Assert.Equal("MainTabs_SelectionChanged", (string?)mainTabs.Attribute("SelectionChanged"));
    }

    [Fact]
    public void BrowserWorkspaceGrid_NoLongerReservesRowsForItsOwnStatusBar()
    {
        // The combined responsive toolbar occupies one Auto row above the media; no separate selection-action
        // row remains in the Browser center grid.
        var document = XDocument.Load(Path.Combine(FindRepositoryRoot(), "LightflowStudio", "MainWindow.xaml"));
        var ns = document.Root!.Name.Namespace;
        var gridHost = Named(document, "BrowserGridHost");
        var browserWorkspaceGrid = gridHost.Parent!;
        var rowDefinitions = browserWorkspaceGrid.Element(ns + "Grid.RowDefinitions")!.Elements(ns + "RowDefinition").ToList();

        Assert.Equal(3, rowDefinitions.Count);
        Assert.Equal("2", (string?)gridHost.Attribute("Grid.Row"));
        Assert.Equal("*", (string?)rowDefinitions[2].Attribute("Height"));
    }

    [Fact]
    public void Window_WiresCtrlFToTheBrowserSearchBoxScopedHandler()
    {
        var document = XDocument.Load(Path.Combine(FindRepositoryRoot(), "LightflowStudio", "MainWindow.xaml"));
        Assert.Equal("MainWindow_PreviewKeyDown", (string?)document.Root!.Attribute("PreviewKeyDown"));
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
