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
