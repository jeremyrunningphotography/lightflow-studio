using Xunit;

namespace LightflowStudio.Tests;

/// <summary>
/// Source-text regression coverage for #125's thumbnail-size wiring in MainWindow, mirroring the technique
/// already used for #109/#126 (see BrowserQueryRegressionTests/BrowserStatusBarRegressionTests) for
/// ordering-sensitive behavior that isn't practical to exercise through a live WPF Window in a headless
/// test run.
/// </summary>
public sealed class BrowserThumbnailSizeRegressionTests
{
    [Fact]
    public void ApplyBrowserThumbnailSize_IsTheSingleAuthoritativeApplyPointForTrackingResourcesAndReflow()
    {
        var source = Source();
        var methodStart = source.IndexOf("private void ApplyBrowserThumbnailSize", StringComparison.Ordinal);
        Assert.True(methodStart >= 0, "ApplyBrowserThumbnailSize not found");
        var methodEnd = source.IndexOf("\n    private", methodStart + 1, StringComparison.Ordinal);
        var body = source[methodStart..methodEnd];

        Assert.Contains("_browserThumbnailSize = size;", body);
        Assert.Contains("BrowserThumbnailSizeSlider.Value = (int)size;", body);
        Assert.Contains("Resources[\"BrowserTileWidth\"] = BrowserGridLayout.TileWidthFor(size);", body);
        Assert.Contains("Resources[\"BrowserTileThumbnailHeight\"] = BrowserGridLayout.ThumbnailAreaHeightFor(size);", body);
        Assert.Contains("UpdateBrowserGridColumns();", body);

        // The step buttons' enabled state is derived from BrowserGridLayout.ThumbnailSizes.Count here — not
        // a hardcoded "5" or a second copy of the level list — so it can never drift if levels are added or
        // removed again.
        Assert.Contains("BrowserThumbnailSizeDecreaseButton.IsEnabled = (int)size > 0;", body);
        Assert.Contains("BrowserThumbnailSizeIncreaseButton.IsEnabled = (int)size < BrowserGridLayout.ThumbnailSizes.Count - 1;", body);

        // The Slider.Value assignment must be guarded, or setting it here would re-enter
        // BrowserThumbnailSizeSlider_ValueChanged.
        var guardOn = body.IndexOf("_synchronizingBrowserThumbnailSize = true;", StringComparison.Ordinal);
        var sliderAssignment = body.IndexOf("BrowserThumbnailSizeSlider.Value = (int)size;", StringComparison.Ordinal);
        var guardOff = body.IndexOf("finally { _synchronizingBrowserThumbnailSize = false; }", StringComparison.Ordinal);
        Assert.True(guardOn >= 0 && guardOn < sliderAssignment && sliderAssignment < guardOff,
            "The Slider.Value assignment must happen between the guard being set and cleared.");
    }

    [Fact]
    public void BrowserThumbnailSizeSliderValueChanged_IsGuardedAndRoutesThroughTheSharedApplyMethod()
    {
        var source = Source();
        var methodStart = source.IndexOf("private void BrowserThumbnailSizeSlider_ValueChanged", StringComparison.Ordinal);
        Assert.True(methodStart >= 0, "BrowserThumbnailSizeSlider_ValueChanged not found");
        var methodEnd = source.IndexOf("\n    private", methodStart + 1, StringComparison.Ordinal);
        var body = source[methodStart..methodEnd];

        Assert.Contains("if (_synchronizingBrowserThumbnailSize) return;", body);
        Assert.Contains("ApplyBrowserThumbnailSize(BrowserGridLayout.ThumbnailSizeFromLevel(", body);
    }

    [Fact]
    public void UpdateBrowserGridColumns_ConsumesTheCurrentThumbnailSizeRatherThanAHardcodedTileWidth()
    {
        var source = Source();
        var methodStart = source.IndexOf("private void UpdateBrowserGridColumns", StringComparison.Ordinal);
        Assert.True(methodStart >= 0, "UpdateBrowserGridColumns not found");
        var methodEnd = source.IndexOf("\n    private", methodStart + 1, StringComparison.Ordinal);
        var body = source[methodStart..methodEnd];

        Assert.Contains("BrowserGridLayout.ComputeColumns(width, BrowserGridLayout.TileWidthFor(_browserThumbnailSize))", body);
    }

    [Fact]
    public void ApplyRestoredWorkspaceLayout_AppliesTheSavedOrDefaultThumbnailSizeUnconditionallyBeforeTheWindowIsShown()
    {
        // Unconditional (not nested in the `if` that guards BrowserLocationsPaneWidth): this call is also
        // what seeds Resources["BrowserTileWidth"]/["BrowserTileThumbnailHeight"] for the very first frame,
        // whether or not a size was ever saved — a missing/legacy document must still get valid tile
        // dimensions, not a 0/unset DynamicResource lookup on first paint.
        var source = Source();
        var methodStart = source.IndexOf("private void ApplyRestoredWorkspaceLayout", StringComparison.Ordinal);
        Assert.True(methodStart >= 0, "ApplyRestoredWorkspaceLayout not found");
        var methodEnd = source.IndexOf("\n    private", methodStart + 1, StringComparison.Ordinal);
        var body = source[methodStart..methodEnd];

        Assert.Contains("BrowserGridLayout.ThumbnailSizeFromLevel(level)", body);
        Assert.Contains("BrowserGridLayout.DefaultThumbnailSize", body);
        Assert.Contains("ApplyBrowserThumbnailSize(savedThumbnailSize);", body);

        // The call must be reachable unconditionally (it is its own top-level statement, not nested inside
        // the single-statement `if (...) BrowserNavigationColumn.Width = ...;` above it) and must run after
        // that pane-width assignment, matching source order.
        var paneWidthAssignment = body.IndexOf("BrowserNavigationColumn.Width = new GridLength(paneWidth);", StringComparison.Ordinal);
        var applyCall = body.IndexOf("ApplyBrowserThumbnailSize(savedThumbnailSize);", StringComparison.Ordinal);
        Assert.True(paneWidthAssignment >= 0 && applyCall > paneWidthAssignment,
            "ApplyBrowserThumbnailSize must run after the BrowserLocationsPaneWidth assignment.");
    }

    [Fact]
    public void SaveWorkspaceState_PersistsTheCurrentThumbnailSizeAlongsideThePaneWidth()
    {
        var source = Source();
        var methodStart = source.IndexOf("private void SaveWorkspaceState", StringComparison.Ordinal);
        Assert.True(methodStart >= 0, "SaveWorkspaceState not found");
        var methodEnd = source.IndexOf("\n    private", methodStart + 1, StringComparison.Ordinal);
        var body = source[methodStart..methodEnd];

        Assert.Contains("_workspaceState.SetBrowserThumbnailSizeLevel((int)_browserThumbnailSize);", body);
        // Must run before Save(), or the freshly-set level would never reach disk.
        var setCall = body.IndexOf("_workspaceState.SetBrowserThumbnailSizeLevel", StringComparison.Ordinal);
        var saveCall = body.IndexOf("_workspaceState.Save();", StringComparison.Ordinal);
        Assert.True(setCall >= 0 && setCall < saveCall);
    }

    [Fact]
    public void ThumbnailSizeWiring_NeverTouchesBrowserQueryScopeCatalogOrPreviewApis()
    {
        // #125 is presentation state — architecturally separate from BrowserQuery/scope (#109/#124) and from
        // Catalog/Preview generation policy. None of the methods that actually change or apply the size may
        // reference those systems — including the two step-button handlers added alongside the slider.
        var source = Source();
        foreach (var method in new[]
        {
            "ApplyBrowserThumbnailSize", "BrowserThumbnailSizeSlider_ValueChanged", "UpdateBrowserGridColumns",
            "BrowserThumbnailSizeDecreaseButton_Click", "BrowserThumbnailSizeIncreaseButton_Click"
        })
        {
            var methodStart = source.IndexOf($"private void {method}", StringComparison.Ordinal);
            Assert.True(methodStart >= 0, $"{method} not found");
            var methodEnd = method.EndsWith("_Click", StringComparison.Ordinal)
                ? source.IndexOf(';', methodStart) // expression-bodied: private void X(...) => ...;
                : source.IndexOf("\n    private", methodStart + 1, StringComparison.Ordinal);
            var body = source[methodStart..methodEnd];

            Assert.DoesNotContain("SetQuery", body);
            Assert.DoesNotContain("BrowserQuery", body);
            Assert.DoesNotContain(".Populate(", body);
            Assert.DoesNotContain("Previews.", body);
            Assert.DoesNotContain("_storage.Catalog", body);
        }
    }

    [Fact]
    public void StepButtonClicks_RouteThroughTheSameApplyMethodAsTheSliderUsingTheSharedStepOperation()
    {
        // Both buttons are just two more callers of ApplyBrowserThumbnailSize — the same single sizing path
        // the slider uses — via BrowserGridLayout.StepLevel rather than a second increment/decrement
        // implementation or a hardcoded +1/-1 index computed inline.
        var source = Source();

        var decreaseStart = source.IndexOf("private void BrowserThumbnailSizeDecreaseButton_Click", StringComparison.Ordinal);
        Assert.True(decreaseStart >= 0, "BrowserThumbnailSizeDecreaseButton_Click not found");
        var decreaseEnd = source.IndexOf(';', decreaseStart);
        var decreaseBody = source[decreaseStart..decreaseEnd];
        Assert.Contains("ApplyBrowserThumbnailSize(BrowserGridLayout.StepLevel(_browserThumbnailSize, -1))", decreaseBody);

        var increaseStart = source.IndexOf("private void BrowserThumbnailSizeIncreaseButton_Click", StringComparison.Ordinal);
        Assert.True(increaseStart >= 0, "BrowserThumbnailSizeIncreaseButton_Click not found");
        var increaseEnd = source.IndexOf(';', increaseStart);
        var increaseBody = source[increaseStart..increaseEnd];
        Assert.Contains("ApplyBrowserThumbnailSize(BrowserGridLayout.StepLevel(_browserThumbnailSize, 1))", increaseBody);
    }

    [Fact]
    public void StepButtonClicks_NeverCallWorkspaceStateDirectlySoPersistenceStaysOnTheExistingLifecycleOnly()
    {
        // A click must not save workspace state itself — persistence remains #125's existing
        // shutdown/debounce-timer lifecycle (SaveWorkspaceState), read from _browserThumbnailSize whenever
        // that already-scheduled save happens to run, exactly like slider changes.
        var source = Source();
        foreach (var method in new[] { "BrowserThumbnailSizeDecreaseButton_Click", "BrowserThumbnailSizeIncreaseButton_Click" })
        {
            var methodStart = source.IndexOf($"private void {method}", StringComparison.Ordinal);
            Assert.True(methodStart >= 0, $"{method} not found");
            var methodEnd = source.IndexOf(';', methodStart);
            var body = source[methodStart..methodEnd];

            Assert.DoesNotContain("_workspaceState", body);
        }
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
