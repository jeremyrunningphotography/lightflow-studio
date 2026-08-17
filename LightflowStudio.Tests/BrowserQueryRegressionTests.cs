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
    public void SyncBrowserQueryToolbarVisuals_ShowsTheChipRowOnlyWhenAtLeastOnePredicateIsActive()
    {
        var source = Source();
        var methodStart = source.IndexOf("private void SyncBrowserQueryToolbarVisuals", StringComparison.Ordinal);
        var methodEnd = source.IndexOf("\n    private", methodStart + 1, StringComparison.Ordinal);
        var body = source[methodStart..methodEnd];

        Assert.Contains("BrowserFilterChips.ItemsSource = filters;", body);
        Assert.Contains("filters.Count > 0 ? Visibility.Visible : Visibility.Collapsed", body);
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
            "BrowserSortCombo", "BrowserFilterImagesCheck", "BrowserFilterRawCheck", "BrowserFilterVideoCheck"
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
