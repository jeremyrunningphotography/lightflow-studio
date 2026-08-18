using Xunit;

namespace LightflowStudio.Tests;

/// <summary>
/// Source-text regression coverage for MainWindow's #109 sort/filter/search wiring, mirroring the technique
/// already used elsewhere (see WorkspaceRestorationRegressionTests) for ordering-sensitive behavior that
/// isn't practical to exercise through a live WPF Window in a headless test run.
/// </summary>
public sealed class BrowserQueryRegressionTests
{
    [Fact]
    public void ApplyBrowserState_ResetsTheQueryToolbarOnlyWhenTheScopeActuallyChanges()
    {
        var source = Source();
        var methodStart = source.IndexOf("private void ApplyBrowserState", StringComparison.Ordinal);
        var methodEnd = source.IndexOf("\n    private", methodStart + 1, StringComparison.Ordinal);
        var body = source[methodStart..methodEnd];

        Assert.Contains("if (scope != _browserQueryScope) ResetBrowserQueryToolbar();", body);
        Assert.Contains("_browserQueryScope = scope;", body);
        // The reset call must precede reassigning the tracked scope, or the comparison is always false.
        Assert.True(body.IndexOf("if (scope != _browserQueryScope)", StringComparison.Ordinal) <
            body.IndexOf("_browserQueryScope = scope;", StringComparison.Ordinal));
    }

    [Fact]
    public void BrowserSearchBox_DebouncesRatherThanApplyingOnEveryKeystroke()
    {
        var source = Source();
        var methodStart = source.IndexOf("private void BrowserSearchBox_TextChanged", StringComparison.Ordinal);
        var methodEnd = source.IndexOf("\n    private", methodStart + 1, StringComparison.Ordinal);
        var body = source[methodStart..methodEnd];

        Assert.Contains("_browserSearchDebounceTimer.Stop();", body);
        Assert.Contains("_browserSearchDebounceTimer.Start();", body);
        Assert.DoesNotContain("ApplyBrowserQuery();", body);
    }

    [Fact]
    public void BrowserFilterAndSortControls_ApplyImmediatelyForRapidSwitchingRatherThanDebouncing()
    {
        var source = Source();
        foreach (var handler in new[] { "BrowserSortCombo_SelectionChanged", "BrowserSortDirection_Click", "ToggleBrowserMediaTypeFilter",
            "BrowserFilterChip_Remove_Click" })
        {
            var methodStart = source.IndexOf($"private void {handler}", StringComparison.Ordinal);
            Assert.True(methodStart >= 0, $"{handler} not found");
            var methodEnd = source.IndexOf("\n    private", methodStart + 1, StringComparison.Ordinal);
            var body = source[methodStart..methodEnd];
            Assert.Contains("ApplyBrowserQuery(", body);
            Assert.DoesNotContain("DebounceTimer", body);
        }
    }

    [Fact]
    public void MediaTypeFilterCheckboxHandlers_RouteThroughTheSharedGuardedToggleHelper()
    {
        var source = Source();
        foreach (var (handler, category) in new[]
        {
            ("BrowserFilterImagesCheck_Changed", "MediaTypeCategory.StillImage"),
            ("BrowserFilterRawCheck_Changed", "MediaTypeCategory.RawImage"),
            ("BrowserFilterVideoCheck_Changed", "MediaTypeCategory.Video"),
        })
        {
            var methodStart = source.IndexOf($"private void {handler}", StringComparison.Ordinal);
            Assert.True(methodStart >= 0, $"{handler} not found");
            var methodEnd = source.IndexOf(';', methodStart);
            var body = source[methodStart..methodEnd];
            Assert.Contains("ToggleBrowserMediaTypeFilter(", body);
            Assert.Contains(category, body);
        }

        var toggleStart = source.IndexOf("private void ToggleBrowserMediaTypeFilter", StringComparison.Ordinal);
        var toggleEnd = source.IndexOf("\n    private", toggleStart + 1, StringComparison.Ordinal);
        var toggleBody = source[toggleStart..toggleEnd];
        Assert.Contains("if (_synchronizingBrowserQuery) return;", toggleBody);
        Assert.Contains("WithFilterAdded", toggleBody);
        Assert.Contains("WithFilterRemoved", toggleBody);
    }

    [Fact]
    public void ApplyBrowserQuery_AlwaysResyncsToolbarVisualsSoCheckboxesChipsAndTheDirectionGlyphNeverDrift()
    {
        var source = Source();
        var methodStart = source.IndexOf("private void ApplyBrowserQuery(Func<BrowserQuery, BrowserQuery> transform)", StringComparison.Ordinal);
        Assert.True(methodStart >= 0);
        var methodEnd = source.IndexOf("\n    private", methodStart + 1, StringComparison.Ordinal);
        var body = source[methodStart..methodEnd];

        Assert.Contains("_browserGrid.SetQuery(transform(_browserGrid.Query));", body);
        Assert.Contains("SyncBrowserQueryToolbarVisuals();", body);
    }

    [Fact]
    public void SyncBrowserQueryToolbarVisuals_ExcludesMediaTypeFromTheChipRowSinceTheQuickButtonsAlreadyShowIt()
    {
        // The permanent media-type toggles already communicate that facet's complete state; a "Video ×"
        // chip beneath a highlighted Video button would be redundant. The chip row (and the space it would
        // occupy) exists only for predicates — future fields — with no permanent toolbar representation.
        var source = Source();
        var methodStart = source.IndexOf("private void SyncBrowserQueryToolbarVisuals", StringComparison.Ordinal);
        var methodEnd = source.IndexOf("\n    private", methodStart + 1, StringComparison.Ordinal);
        var body = source[methodStart..methodEnd];

        Assert.Contains("filters.Where(f => f.Field != BrowserFilterField.MediaType)", body);
        Assert.Contains("BrowserFilterChips.ItemsSource = advancedFilters;", body);
        Assert.Contains("advancedFilters.Length > 0 ? Visibility.Visible : Visibility.Collapsed", body);
    }

    [Fact]
    public void SyncBrowserQueryToolbarVisuals_GivesEachMediaTypeButtonAnIndependentToggleStateAndDerivesAllFromZeroPredicates()
    {
        // Every media-type button reflects only its own category (multiple may be checked at once); "All"
        // must come from there being zero active predicates, never from every individual button happening
        // to be checked, so "no filter" and "every type explicitly selected" stay visually distinct even
        // though they show the same tiles.
        var source = Source();
        var methodStart = source.IndexOf("private void SyncBrowserQueryToolbarVisuals", StringComparison.Ordinal);
        var methodEnd = source.IndexOf("\n    private", methodStart + 1, StringComparison.Ordinal);
        var body = source[methodStart..methodEnd];

        Assert.Contains("BrowserQuickFilterAllButton.IsChecked = activeMediaTypes.Count == 0;", body);
        Assert.Contains("foreach (var (category, button) in _browserQuickFilterButtons) button.IsChecked = activeMediaTypes.Contains(category);", body);
        Assert.DoesNotContain("mediaTypeValues.Length == 1", body);
        Assert.DoesNotContain(" is [", body);
    }

    [Fact]
    public void BrowserQuickFilterCategoryButtonClick_IsSharedAcrossEveryCategoryAndRoutesThroughTheGuardedToggleHelper()
    {
        var source = Source();
        var methodStart = source.IndexOf("private void BrowserQuickFilterCategoryButton_Click", StringComparison.Ordinal);
        Assert.True(methodStart >= 0, "BrowserQuickFilterCategoryButton_Click not found");
        var methodEnd = source.IndexOf("\n    private", methodStart + 1, StringComparison.Ordinal);
        var body = source[methodStart..methodEnd];

        Assert.Contains("if (_synchronizingBrowserQuery) return;", body);
        Assert.Contains("ToggleBrowserMediaTypeFilter((MediaTypeCategory)button.Tag, button.IsChecked == true);", body);
        // No hardcoded per-category handler should remain — one shared handler for every dynamically
        // created segment, unlike the removed BrowserQuickFilterImagesButton_Click/RawButton_Click/VideoButton_Click.
        Assert.DoesNotContain("BrowserQuickFilterImagesButton_Click", source);
        Assert.DoesNotContain("BrowserQuickFilterRawButton_Click", source);
        Assert.DoesNotContain("BrowserQuickFilterVideoButton_Click", source);
        Assert.DoesNotContain("SetSoleBrowserMediaTypeFilter", source);
        Assert.DoesNotContain("WithOnlyFilter", source);

        var allStart = source.IndexOf("private void BrowserQuickFilterAllButton_Click", StringComparison.Ordinal);
        var allEnd = source.IndexOf("\n    private", allStart + 1, StringComparison.Ordinal);
        var allBody = source[allStart..allEnd];
        Assert.Contains("if (_synchronizingBrowserQuery) return;", allBody);
        Assert.Contains("query.WithoutField(BrowserFilterField.MediaType)", allBody);
    }

    [Fact]
    public void InitializeBrowserQuickFilterButtons_GeneratesOneSegmentPerPresentableCategoryRatherThanAHardcodedList()
    {
        var source = Source();
        var methodStart = source.IndexOf("private void InitializeBrowserQuickFilterButtons", StringComparison.Ordinal);
        Assert.True(methodStart >= 0, "InitializeBrowserQuickFilterButtons not found");
        var methodEnd = source.IndexOf("\n    private", methodStart + 1, StringComparison.Ordinal);
        var body = source[methodStart..methodEnd];

        Assert.Contains("BrowserGridModel.PresentableCategories", body);
        Assert.Contains("button.Click += BrowserQuickFilterCategoryButton_Click;", body);
        Assert.Contains("BrowserQuickFilterSegments.Children.Add(button);", body);
        Assert.Contains("_browserQuickFilterButtons[category] = button;", body);
        // The Click handler is wired after construction and IsChecked is never set in the object
        // initializer, so no Checked event can fire before the button is fully set up.
        var initializerEnd = body.IndexOf("};", body.IndexOf("new ToggleButton", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.DoesNotContain("IsChecked", body[..initializerEnd]);

        var constructorStart = source.IndexOf("InitializeComponent();", StringComparison.Ordinal);
        var constructorEnd = source.IndexOf('\n', constructorStart);
        Assert.Contains("InitializeBrowserQuickFilterButtons();", source[constructorStart..(constructorEnd + 60)]);
    }

    [Fact]
    public void MainWindowPreviewKeyDown_ScopesCtrlFToAnOpenBrowserWorkspace()
    {
        var source = Source();
        var methodStart = source.IndexOf("private void MainWindow_PreviewKeyDown", StringComparison.Ordinal);
        var methodEnd = source.IndexOf("\n    private", methodStart + 1, StringComparison.Ordinal);
        var body = source[methodStart..methodEnd];

        Assert.Contains("e.Key != Key.F", body);
        Assert.Contains("ModifierKeys.Control", body);
        Assert.Contains("ShellWorkspaceSelection.Index(ShellWorkspace.Browser)", body);
        Assert.Contains("BrowserQueryToolbar.IsEnabled", body);
        Assert.Contains("BrowserSearchBox.Focus();", body);
        Assert.Contains("e.Handled = true;", body);
    }

    [Fact]
    public void AttachBrowserDerivedWork_ClearsTheActiveBatchEvenWhenNothingIsScheduled()
    {
        // Regression: assigning _activeBrowserDerivedWorkBatch only inside the "batch is not null" branch
        // would leave a *previous* folder's batch reference live for a folder with nothing scheduled, making
        // the status line show a stale "Generating previews…" for a folder that has no activity at all.
        var source = Source();
        var methodStart = source.IndexOf("private void AttachBrowserDerivedWork", StringComparison.Ordinal);
        var methodEnd = source.IndexOf("\n    private", methodStart + 1, StringComparison.Ordinal);
        var body = source[methodStart..methodEnd];
        var assignment = body.IndexOf("_activeBrowserDerivedWorkBatch = batch;", StringComparison.Ordinal);
        var earlyReturn = body.IndexOf("if (batch is null) return;", StringComparison.Ordinal);

        Assert.True(assignment >= 0 && earlyReturn > assignment,
            "_activeBrowserDerivedWorkBatch must be assigned unconditionally, before the null-batch early return.");
    }

    [Fact]
    public void ApplyBrowserDerivedWorkResultsAsync_OnlyCoalescesAResortWhenTheActiveSortDependsOnMetadata()
    {
        var source = Source();
        var methodStart = source.IndexOf("private async Task ApplyBrowserDerivedWorkResultsAsync", StringComparison.Ordinal);
        var methodEnd = source.IndexOf("\n    private", methodStart + 1, StringComparison.Ordinal);
        var body = source[methodStart..methodEnd];

        Assert.Contains("sortRelevantMetadataChanged", body);
        Assert.Contains("BrowserSortMode.CaptureDate or BrowserSortMode.Duration", body);
        Assert.Contains("_browserMetadataResortTimer.Stop();", body);
        Assert.Contains("_browserMetadataResortTimer.Start();", body);
        // Never a per-item resort: the timer restart must be reachable only once, outside the per-asset loop.
        Assert.Equal(1, CountOccurrences(body, "_browserMetadataResortTimer.Start();"));
    }

    [Fact]
    public void ResetBrowserQueryToolbar_GuardsEveryControlAssignmentSoWpfCannotReenterHandlersMidReset()
    {
        var source = Source();
        var methodStart = source.IndexOf("private void ResetBrowserQueryToolbar", StringComparison.Ordinal);
        var methodEnd = source.IndexOf("\n    private", methodStart + 1, StringComparison.Ordinal);
        var body = source[methodStart..methodEnd];
        var guardOn = body.IndexOf("_synchronizingBrowserQuery = true;", StringComparison.Ordinal);
        var sortAssignment = body.IndexOf("BrowserSortCombo.SelectedIndex = 0;", StringComparison.Ordinal);
        var filterButtonAssignment = body.IndexOf("BrowserFilterButton.IsChecked = false;", StringComparison.Ordinal);
        var guardOff = body.IndexOf("finally { _synchronizingBrowserQuery = false; }", StringComparison.Ordinal);

        Assert.True(guardOn >= 0 && guardOn < sortAssignment && sortAssignment < guardOff &&
            guardOn < filterButtonAssignment && filterButtonAssignment < guardOff,
            "Every toolbar control reset must happen between the guard being set and cleared.");
        // The filter checkboxes/chips are reflected by SyncBrowserQueryToolbarVisuals (also guarded, called
        // separately below) rather than being reset by hand here — one code path for "reflect the model".
        Assert.Contains("SyncBrowserQueryToolbarVisuals();", body);
    }

    [Fact]
    public void BrowserFilterAndSortControls_DoNotDeclareADefaultSelectionInXamlToAvoidReenteringHandlersDuringInitializeComponent()
    {
        // Regression: WPF can raise SelectionChanged/Checked for a XAML-declared default while
        // InitializeComponent is still connecting later-declared named elements in the same file, so a
        // handler that reads sibling controls must never be reachable from a declarative default.
        var xaml = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "LightflowStudio", "MainWindow.xaml"));
        foreach (var name in new[]
        {
            "BrowserSortCombo", "BrowserFilterImagesCheck", "BrowserFilterRawCheck", "BrowserFilterVideoCheck",
            "BrowserQuickFilterAllButton"
        })
        {
            var start = xaml.IndexOf($"x:Name=\"{name}\"", StringComparison.Ordinal);
            Assert.True(start >= 0, $"{name} not found in XAML");
            var end = xaml.IndexOf('>', start);
            var tag = xaml[start..end];
            Assert.DoesNotContain("SelectedIndex", tag);
            Assert.DoesNotContain("IsChecked", tag);
        }
    }

    [Fact]
    public void MetadataResortTimer_ReapplyQueryRatherThanSetQuerySinceTheQueryItselfDidNotChange()
    {
        var source = Source();
        var tickStart = source.IndexOf("_browserMetadataResortTimer.Tick += (_, _) =>", StringComparison.Ordinal);
        var tickEnd = source.IndexOf("};", tickStart, StringComparison.Ordinal);
        var body = source[tickStart..tickEnd];

        Assert.Contains("_browserGrid.ReapplyQuery();", body);
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0) { count++; index += needle.Length; }
        return count;
    }

    private static string Source() =>
        File.ReadAllText(Path.Combine(FindRepositoryRoot(), "LightflowStudio", "MainWindow.xaml.cs"));

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
