using Xunit;

namespace LightflowStudio.Tests;

/// <summary>
/// Source-text regression coverage for #126's unified application-wide status bar wiring in MainWindow,
/// mirroring the technique already used for #109 (see BrowserQueryRegressionTests) for ordering-sensitive
/// behavior that isn't practical to exercise through a live WPF Window in a headless test run.
/// </summary>
public sealed class BrowserStatusBarRegressionTests
{
    [Fact]
    public void SyncBrowserStatusBarVisibility_IsNullGuardedRatherThanUsingTheUsualSynchronizationFlag()
    {
        // MainTabs.SelectedIndex="0" is a XAML-declared default, so SelectionChanged can fire while
        // InitializeComponent is still connecting BrowserStatusText/BrowserStatusDivider/
        // BrowserPresentationControls (all declared later in the file) — the same init-order hazard already
        // documented for BrowserSortCombo/the filter checkboxes, just guarded with a null check here instead
        // of _synchronizingBrowserQuery, since this has nothing to do with the Browser query.
        var source = Source();
        var methodStart = source.IndexOf("private void SyncBrowserStatusBarVisibility", StringComparison.Ordinal);
        Assert.True(methodStart >= 0, "SyncBrowserStatusBarVisibility not found");
        var methodEnd = source.IndexOf("\n    private", methodStart + 1, StringComparison.Ordinal);
        var body = source[methodStart..methodEnd];

        Assert.Contains("if (BrowserStatusText is null) return;", body);
        var guardIndex = body.IndexOf("if (BrowserStatusText is null) return;", StringComparison.Ordinal);
        Assert.True(guardIndex < body.IndexOf("BrowserStatusText.Visibility", StringComparison.Ordinal),
            "The null guard must precede every element assignment it protects.");
    }

    [Fact]
    public void SyncBrowserStatusBarVisibility_TogglesAllThreeSegmentsTogetherAndRefreshesTextWhenShown()
    {
        var source = Source();
        var methodStart = source.IndexOf("private void SyncBrowserStatusBarVisibility", StringComparison.Ordinal);
        var methodEnd = source.IndexOf("\n    private", methodStart + 1, StringComparison.Ordinal);
        var body = source[methodStart..methodEnd];

        Assert.Contains("ShellWorkspaceSelection.Index(ShellWorkspace.Browser)", body);
        Assert.Contains("BrowserStatusText.Visibility = visibility;", body);
        Assert.Contains("BrowserStatusDivider.Visibility = visibility;", body);
        // #110: BrowserPresentationControls (the thumbnail-size slider) is Grid-presentation-specific, so it
        // additionally hides while the Player/Viewer is showing, unlike BrowserStatusText/BrowserStatusDivider
        // which stay tied only to whether the Browser tab itself is active. Asserted as one contiguous
        // substring spanning the full conditional (not two independent Assert.Contains calls) so this only
        // passes if both conditions actually join the same expression, not merely both appear anywhere in the
        // method body (e.g. one in an unrelated branch or a comment).
        Assert.Contains(
            "BrowserPresentationControls.Visibility = isBrowserActive && _browserPresentation == BrowserPresentationMode.Grid",
            body);
        Assert.Contains("if (isBrowserActive) UpdateBrowserStatusText();", body);
    }

    [Fact]
    public void MainTabsSelectionChanged_RoutesDirectlyToTheSyncMethod()
    {
        var source = Source();
        var methodStart = source.IndexOf("private void MainTabs_SelectionChanged", StringComparison.Ordinal);
        Assert.True(methodStart >= 0, "MainTabs_SelectionChanged not found");
        var guardEnd = source.IndexOf(';', methodStart);
        var methodEnd = source.IndexOf(';', guardEnd + 1);
        var body = source[methodStart..methodEnd];

        Assert.Contains("if (!ReferenceEquals(e.Source, MainTabs)) return", body);
        Assert.Contains("SyncBrowserStatusBarVisibility()", body);
    }

    [Fact]
    public void Constructor_CallsSyncBrowserStatusBarVisibilityImmediatelyAfterInitializeComponent()
    {
        // This explicit call — not reliance on SelectionChanged firing for the XAML-declared default — is
        // what actually establishes the correct initial visibility, since InitializeComponent has fully
        // returned (and therefore connected every named element) by the time it runs.
        var source = Source();
        var initStart = source.IndexOf("InitializeComponent();", StringComparison.Ordinal);
        Assert.True(initStart >= 0);
        var nextFewLines = source.Substring(initStart, 200);

        Assert.Contains("InitializeBrowserQuickFilterButtons();", nextFewLines);
        Assert.Contains("SyncBrowserStatusBarVisibility();", nextFewLines);
        Assert.True(nextFewLines.IndexOf("SyncBrowserStatusBarVisibility();", StringComparison.Ordinal) <
            nextFewLines.IndexOf("ApplyRestoredWorkspaceLayout();", StringComparison.Ordinal),
            "The sync call must happen before any code that could show the window mid-inconsistent-state.");
    }

    [Fact]
    public void UpdateBrowserStatusText_NeverGatesOnTheActiveTabSoBackgroundProgressStaysFreshWhileHidden()
    {
        // Preview-generation progress keeps calling UpdateBrowserStatusText via AttachBrowserDerivedWork's
        // event handler regardless of which tab is active; if it gated on visibility, switching back to
        // Browser mid-generation would show a stale count until the next progress tick.
        var source = Source();
        var methodStart = source.IndexOf("private void UpdateBrowserStatusText", StringComparison.Ordinal);
        var methodEnd = source.IndexOf("\n    private", methodStart + 1, StringComparison.Ordinal);
        var body = source[methodStart..methodEnd];

        Assert.DoesNotContain("MainTabs.SelectedIndex", body);
        Assert.DoesNotContain("Visibility", body);
    }

    [Fact]
    public void SetBrowserPresentationMode_HidesTheQueryToolbarInPlayerViewerModeAndRestoresItInGridMode()
    {
        // #110: the query toolbar (Subfolders, All/Images/RAW/Video, Search, Filter, Sort) describes/manipulates
        // the Grid's own result set and is irrelevant while reviewing one open asset in the Player/Viewer.
        var source = Source();
        var methodStart = source.IndexOf("private void SetBrowserPresentationMode", StringComparison.Ordinal);
        Assert.True(methodStart >= 0, "SetBrowserPresentationMode not found");
        var methodEnd = source.IndexOf("\n    private", methodStart + 1, StringComparison.Ordinal);
        var body = source[methodStart..methodEnd];

        Assert.Contains(
            "BrowserQueryToolbar.Visibility = mode == BrowserPresentationMode.Grid ? Visibility.Visible : Visibility.Collapsed;",
            body);
        Assert.Contains(
            "BrowserSelectionActionToolbar.Visibility = mode == BrowserPresentationMode.Grid ? Visibility.Visible : Visibility.Collapsed;",
            body);
    }

    [Fact]
    public void PlayerExport_UsesTheCurrentAssetThroughTheSharedBrowserHandoff()
    {
        var source = Source();
        Assert.Contains("_playerViewerHost.ExportRequested += PlayerViewerHost_ExportRequested;", source);
        Assert.Contains("await ExportBrowserAssetsAsync([e.AssetId]);", source);
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
