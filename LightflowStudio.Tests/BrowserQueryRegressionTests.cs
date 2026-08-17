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
        foreach (var handler in new[] { "BrowserMediaFilterCombo_SelectionChanged", "BrowserSortCombo_SelectionChanged", "BrowserSortDirection_Click" })
        {
            var methodStart = source.IndexOf($"private void {handler}", StringComparison.Ordinal);
            var methodEnd = source.IndexOf("\n    private", methodStart + 1, StringComparison.Ordinal);
            Assert.True(methodStart >= 0, $"{handler} not found");
            var body = source[methodStart..methodEnd];
            Assert.Contains("ApplyBrowserQuery();", body);
        }
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
        var filterAssignment = body.IndexOf("BrowserMediaFilterCombo.SelectedIndex = 0;", StringComparison.Ordinal);
        var sortAssignment = body.IndexOf("BrowserSortCombo.SelectedIndex = 0;", StringComparison.Ordinal);
        var guardOff = body.IndexOf("finally { _synchronizingBrowserQuery = false; }", StringComparison.Ordinal);

        Assert.True(guardOn >= 0 && guardOn < filterAssignment && filterAssignment < guardOff && sortAssignment < guardOff,
            "Every toolbar control reset must happen between the guard being set and cleared.");
    }

    [Fact]
    public void BrowserFilterAndSortCombos_DoNotDeclareASelectedIndexInXamlToAvoidReenteringHandlersDuringInitializeComponent()
    {
        // Regression: WPF can raise SelectionChanged for a XAML-declared SelectedIndex while
        // InitializeComponent is still connecting later-declared named elements in the same file, so the
        // handler (which reads sibling controls) must never be reachable from a declarative default.
        var xaml = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "LightflowStudio", "MainWindow.xaml"));
        var filterStart = xaml.IndexOf("x:Name=\"BrowserMediaFilterCombo\"", StringComparison.Ordinal);
        var filterEnd = xaml.IndexOf('>', filterStart);
        var sortStart = xaml.IndexOf("x:Name=\"BrowserSortCombo\"", StringComparison.Ordinal);
        var sortEnd = xaml.IndexOf('>', sortStart);

        Assert.DoesNotContain("SelectedIndex", xaml[filterStart..filterEnd]);
        Assert.DoesNotContain("SelectedIndex", xaml[sortStart..sortEnd]);
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
