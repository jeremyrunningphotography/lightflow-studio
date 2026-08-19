using Xunit;

namespace LightflowStudio.Tests;

/// <summary>
/// Source-text regression coverage for MainWindow's workspace-persistence wiring, mirroring the technique
/// already used by UiLayoutTests for ordering-sensitive behavior that isn't practical to exercise through a
/// live WPF Window in a headless test run.
/// </summary>
public sealed class WorkspaceRestorationRegressionTests
{
    [Fact]
    public void Constructor_AppliesRestoredWindowAndLayoutBoundsImmediatelyAfterInitializeComponent()
    {
        var source = Source();
        var initializeIndex = source.IndexOf("InitializeComponent();", StringComparison.Ordinal);
        var applyIndex = source.IndexOf("ApplyRestoredWorkspaceLayout();", StringComparison.Ordinal);

        Assert.True(initializeIndex >= 0 && applyIndex > initializeIndex,
            "Restored bounds must be applied right after InitializeComponent and before the window is shown, " +
            "so the user never sees a visible flash at the wrong size or position.");
    }

    [Fact]
    public void WindowClosing_AlwaysSavesWorkspaceStateEvenWhenAnActiveEncodingJobCancelsTheClose()
    {
        var source = Source();
        var closingStart = source.IndexOf("private void Window_Closing", StringComparison.Ordinal);
        var earlyReturn = source.IndexOf("if (_jobCancellation is null || _forceClose) return;", closingStart, StringComparison.Ordinal);
        var saveCall = source.IndexOf("SaveWorkspaceState();", closingStart, StringComparison.Ordinal);

        Assert.True(closingStart >= 0 && earlyReturn > closingStart && saveCall > closingStart,
            "SaveWorkspaceState must run unconditionally on every Closing invocation.");
        Assert.True(saveCall < earlyReturn,
            "SaveWorkspaceState must run before the encoding-in-progress early return, or a close blocked by " +
            "an active job would never persist the latest window/Browser state.");
    }

    [Fact]
    public void Loaded_RestoresTheBrowserLocationOnlyAfterLocationsTreeStorageEntriesArePopulated()
    {
        var source = Source();
        var refreshStorage = source.IndexOf("await RefreshBrowserStorageAsync();", StringComparison.Ordinal);
        var restore = source.IndexOf("RestoreBrowserLocationAsync(_workspaceState.Current.Browser)", StringComparison.Ordinal);

        Assert.True(refreshStorage >= 0 && restore > refreshStorage,
            "Restoring a Browser location before Locations storage entries are populated would leave an " +
            "offline saved root without a matching tree node to show its honest unavailable state.");
    }

    [Fact]
    public void ApplyRestoredWorkspaceLayout_SeedsLastNonMinimizedStateSoAMaximizedRestoreIsNotReadBackAsNormal()
    {
        // Regression: StateChanged does not fire for the initial WindowState assignment made before the
        // window is shown, so a maximized restore was previously saved back as IsMaximized=false at close
        // (caught via a real packaged-app close/relaunch cycle, not by static analysis alone).
        var source = Source();
        var methodStart = source.IndexOf("private void ApplyRestoredWorkspaceLayout", StringComparison.Ordinal);
        var methodEnd = source.IndexOf("\n    private", methodStart + 1, StringComparison.Ordinal);
        var body = source[methodStart..methodEnd];
        var maximizedAssignment = body.IndexOf("WindowState = WindowState.Maximized;", StringComparison.Ordinal);
        var seedAssignment = body.IndexOf("_lastNonMinimizedWindowState = WindowState;", StringComparison.Ordinal);

        Assert.True(maximizedAssignment >= 0 && seedAssignment > maximizedAssignment,
            "ApplyRestoredWorkspaceLayout must seed _lastNonMinimizedWindowState after applying the restored WindowState.");
    }

    [Fact]
    public void ApplyBrowserState_RecordsTheCurrentFolderInWorkspaceStateButNeverTheGridSelection()
    {
        var source = Source();
        var applyStart = source.IndexOf("private void ApplyBrowserState", StringComparison.Ordinal);
        var applyEnd = source.IndexOf("\n    private", applyStart + 1, StringComparison.Ordinal);
        var applyBody = source[applyStart..applyEnd];

        Assert.Contains("_workspaceState.SetBrowserLocation(", applyBody);
        Assert.DoesNotContain("SelectedKeys", applyBody);
        Assert.DoesNotContain("SelectSingle", applyBody);
    }

    [Fact]
    public void Constructor_ShowsTheBrowserRestoringStateForASavedLocationBeforeLoadedFires()
    {
        var source = Source();
        var applyLayoutIndex = source.IndexOf("ApplyRestoredWorkspaceLayout();", StringComparison.Ordinal);
        var showRestoringIndex = source.IndexOf("ShowBrowserRestoringState(savedBrowserLocation);", StringComparison.Ordinal);
        var loadedIndex = source.IndexOf("Loaded += async", StringComparison.Ordinal);

        Assert.True(applyLayoutIndex >= 0 && showRestoringIndex > applyLayoutIndex && showRestoringIndex < loadedIndex,
            "The remembered Browser destination must be reflected in the window's first rendered frame, " +
            "before Loaded even runs, not after asynchronous startup work catches up to it.");
    }

    [Fact]
    public void ShowBrowserRestoringState_ReflectsTheDestinationSynchronouslyWithoutAnyAsyncWork()
    {
        var source = Source();
        var methodStart = source.IndexOf("private void ShowBrowserRestoringState", StringComparison.Ordinal);
        var methodEnd = source.IndexOf("\n    private", methodStart + 1, StringComparison.Ordinal);
        var body = source[methodStart..methodEnd];

        Assert.DoesNotContain("await", body);
        // #124 (further revised): the actual text/overlay/empty-state mechanics now live centrally in
        // ShowBrowserLoadingState (see ShowBrowserLoadingState_RetiresStaleEmptyStateBeforeShowingIndeterminateLoading
        // below), shared with ordinary navigation, so both paths retire a stale empty state identically.
        Assert.Contains("ShowBrowserLoadingState(", body);
    }

    [Fact]
    public void ShowBrowserLoadingState_RetiresStaleEmptyStateBeforeShowingIndeterminateLoading()
    {
        // The root cause of a stale empty/failure message overlapping a new navigation's loading presentation
        // was that entering Loading never retired the previous Empty state. This is the single authoritative
        // entry point for that transition — both ShowBrowserRestoringState (startup restoration) and
        // RunBrowserNavigationAsync (every ordinary navigation) go through it, so a completed scope's empty
        // state can never survive into a new navigation generation's loading presentation.
        var source = Source();
        var methodStart = source.IndexOf("private void ShowBrowserLoadingState", StringComparison.Ordinal);
        Assert.True(methodStart >= 0, "ShowBrowserLoadingState not found in MainWindow.xaml.cs");
        var methodEnd = source.IndexOf("\n    private", methodStart + 1, StringComparison.Ordinal);
        var body = source[methodStart..methodEnd];

        Assert.DoesNotContain("await", body);
        var textSet = body.IndexOf("BrowserLoadingText.Text = label;", StringComparison.Ordinal);
        var emptyHidden = body.IndexOf("BrowserEmptyState.Visibility = Visibility.Collapsed;", StringComparison.Ordinal);
        var progressReset = body.IndexOf("ResetBrowserLoadingProgress();", StringComparison.Ordinal);
        var overlayShown = body.IndexOf("BrowserLoadingOverlay.Visibility = Visibility.Visible;", StringComparison.Ordinal);
        Assert.True(textSet >= 0 && emptyHidden > textSet && progressReset > emptyHidden && overlayShown > progressReset,
            "A new loading sequence must retire the stale empty/failure state and begin indeterminate before " +
            "the overlay appears, so empty/failure and loading presentations can never render simultaneously.");
    }

    [Fact]
    public void Loaded_KicksOffBrowserRestorationBeforeAnyEncodingHistoryOrSettingsOnlyRefresh()
    {
        // The core of the fix: on a real machine, RefreshDependencyHealthAsync alone (it spawns ffmpeg/
        // ffprobe subprocesses) measured ~640-780ms, and together with the other calls below previously
        // delayed the start of Browser restoration by over a second — none of that work is visible by
        // default, since Browser (not Encoding/History/Settings) is the startup workspace.
        var source = Source();
        var kickoff = source.IndexOf("_ = RestoreBrowserLocationAsync(_workspaceState.Current.Browser);", StringComparison.Ordinal);
        Assert.True(kickoff >= 0);

        var loadedStart = source.IndexOf("Loaded += async", StringComparison.Ordinal);
        var loadedEnd = source.IndexOf("\n        };", loadedStart, StringComparison.Ordinal);
        Assert.True(loadedStart >= 0 && loadedEnd > loadedStart && kickoff > loadedStart && kickoff < loadedEnd);

        foreach (var laterCall in new[]
        {
            "RefreshCatalogBackups();", "RefreshHistory();", "LocateTools();", "await RefreshDependencyHealthAsync();",
            "RefreshBatchFiles();", "RefreshLuts();", "await RefreshMediaRootsAsync();", "await RefreshPreviewUsageAsync();"
        })
        {
            // Search only within the Loaded handler: the same method names are also referenced elsewhere
            // (e.g. RefreshBatchFiles from a debounce-timer Tick handler in the constructor).
            var index = source.IndexOf(laterCall, kickoff, StringComparison.Ordinal);
            Assert.True(index > kickoff && index < loadedEnd,
                $"'{laterCall}' must run after Browser restoration is kicked off, not before it.");
        }
    }

    [Fact]
    public void RestoreBrowserLocationAsync_LeavesAnHonestDefaultStateForEveryNonSuccessOutcome()
    {
        // Offline/missing/failed/canceled restoration must never leave the canvas stuck on the restoring
        // overlay's text or on a collapsed BrowserEmptyState with nothing else shown.
        var source = Source();
        var methodStart = source.IndexOf("private async Task RestoreBrowserLocationAsync", StringComparison.Ordinal);
        var methodEnd = source.IndexOf("\n    private", methodStart + 1, StringComparison.Ordinal);
        var body = source[methodStart..methodEnd];

        Assert.Contains("else ShowDefaultBrowserEmptyState();", body);
        var failureBranch = body.IndexOf("if (result.State is { } failure)", StringComparison.Ordinal);
        var failureBranchEnd = body.IndexOf("else ShowDefaultBrowserEmptyState();", failureBranch, StringComparison.Ordinal);
        Assert.Contains("ApplyBrowserNavigationFailure(failure);", body[failureBranch..failureBranchEnd]);
        Assert.Contains("BrowserCurrentPath.Text = \"\";", body[failureBranch..failureBranchEnd]);
        var canceledCatch = body.IndexOf("catch (OperationCanceledException)", StringComparison.Ordinal);
        var canceledCatchEnd = body.IndexOf("catch (Exception exception)", canceledCatch, StringComparison.Ordinal);
        Assert.Contains("ShowDefaultBrowserEmptyState();", body[canceledCatch..canceledCatchEnd]);
        var exceptionCatchEnd = body.IndexOf("finally", canceledCatchEnd, StringComparison.Ordinal);
        Assert.Contains("ShowDefaultBrowserEmptyState();", body[canceledCatchEnd..exceptionCatchEnd]);
    }

    [Fact]
    public void RunBrowserNavigationAsync_ResetsTheLoadingLabelSoRestorationsCustomTextNeverLeaksIntoOrdinaryNavigation()
    {
        var source = Source();
        var methodStart = source.IndexOf("private async Task RunBrowserNavigationAsync", StringComparison.Ordinal);
        var methodEnd = source.IndexOf("\n    private", methodStart + 1, StringComparison.Ordinal);
        var body = source[methodStart..methodEnd];
        // #124 (further revised): the label is scope-mode-aware (a ternary covering recursive scanning too)
        // and passed straight into ShowBrowserLoadingState, which now owns clearing any stale presentation —
        // this only asserts the direct-mode text still exists and is routed through that shared entry point.
        Assert.Contains("ShowBrowserLoadingState(", body);
        Assert.Contains("\"Loading folder…\"", body);
    }

    [Fact]
    public void ApplyBrowserSuccessState_RevealsTreeAncestorsWithoutBlockingPresentation()
    {
        // Ancestor/sibling materialization must not delay the grid/media the user already sees becoming
        // interactive: it is fire-and-forget, applied identically for ordinary navigation and restoration.
        var source = Source();
        var methodStart = source.IndexOf("private bool ApplyBrowserSuccessState", StringComparison.Ordinal);
        var methodEnd = source.IndexOf("\n    private", methodStart + 1, StringComparison.Ordinal);
        var body = source[methodStart..methodEnd];

        Assert.Contains("_ = RevealBrowserTreeAncestorsAsync(location, generation);", body);
        Assert.DoesNotContain("await RevealBrowserTreeAncestorsAsync", body);
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
