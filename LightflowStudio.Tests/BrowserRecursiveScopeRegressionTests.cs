using Xunit;

namespace LightflowStudio.Tests;

/// <summary>
/// Source-text regression coverage for #124's MainWindow wiring, mirroring the technique already used for
/// #109/#121/#126 (see BrowserQueryRegressionTests, WorkspaceRestorationRegressionTests,
/// BrowserStatusBarRegressionTests) for ordering-sensitive behavior that isn't practical to exercise through
/// a live WPF Window in a headless test run.
/// </summary>
public sealed class BrowserRecursiveScopeRegressionTests
{
    [Fact]
    public void ApplyBrowserState_ClearsSelectionOnAnyScopeIdentityChangeBeforePopulatingTheGrid()
    {
        var body = MethodBody("private void ApplyBrowserState");

        var scopeIdentityAssigned = body.IndexOf("_browserScopeIdentity = scopeIdentity;", StringComparison.Ordinal);
        var clearSelection = body.IndexOf("_browserGrid.ClearSelection();", StringComparison.Ordinal);
        var populate = body.IndexOf("_browserGrid.Populate(", StringComparison.Ordinal);

        Assert.True(clearSelection >= 0, "ApplyBrowserState must clear selection on a scope identity change.");
        Assert.True(clearSelection < scopeIdentityAssigned, "The clear must happen before the new identity is recorded.");
        Assert.True(scopeIdentityAssigned < populate,
            "Selection must be cleared before the grid is populated, so Populate's key-based retention can never resurrect it.");
    }

    [Fact]
    public void ApplyBrowserState_SelectionIdentityIncludesScopeModeButQueryResetIdentityDoesNot()
    {
        var body = MethodBody("private void ApplyBrowserState");

        // #124 requires BrowserQuery to survive toggling Include Subfolders, so the query-reset comparison
        // must stay keyed on folder alone (the pre-existing _browserQueryScope tuple), while selection
        // clearing is a separate, scope-mode-aware identity.
        Assert.Contains("if (scope != _browserQueryScope) ResetBrowserQueryToolbar();", body);
        Assert.Contains("_browserNavigation.ScopeMode)", body);
        Assert.Contains("if (scopeIdentity != _browserScopeIdentity) _browserGrid.ClearSelection();", body);

        var fieldDeclaration = Source().Split('\n')
            .Single(line => line.Contains("private (Guid RootId, string RelativeFolder)? _browserQueryScope;"));
        Assert.DoesNotContain("BrowserScopeMode", fieldDeclaration);
    }

    [Fact]
    public void ApplyBrowserState_PopulatesTheGridFromRecursiveMediaEntriesWhenPresentOtherwiseDirectChildren()
    {
        var body = MethodBody("private void ApplyBrowserState");
        Assert.Contains("_browserGrid.Populate(state.RecursiveMediaEntries ?? directFiles);", body);
    }

    [Fact]
    public void ApplyBrowserState_SyncsTheScopeToggleAndItsEnabledState()
    {
        var body = MethodBody("private void ApplyBrowserState");
        Assert.Contains("BrowserIncludeSubfoldersButton.IsEnabled = state.Location is not null;", body);
        Assert.Contains("SyncBrowserScopeToggle();", body);
    }

    [Fact]
    public void ApplyBrowserState_PersistsScopeModeAlongsideTheRememberedLocation()
    {
        var body = MethodBody("private void ApplyBrowserState");
        Assert.Contains("_workspaceState.SetBrowserLocation(location.RootId, location.RelativeFolder, location.AbsolutePath,", body);
        Assert.Contains("_browserNavigation.ScopeMode == BrowserScopeMode.IncludeSubfolders);", body);
    }

    [Fact]
    public void BrowserIncludeSubfoldersButtonClick_UsesSetScopeModeAsyncAndNeverResetsTheQueryToolbar()
    {
        var body = MethodBody("private async void BrowserIncludeSubfoldersButton_Click");

        Assert.Contains("_browserNavigation.SetScopeModeAsync(mode)", body);
        Assert.DoesNotContain("ResetBrowserQueryToolbar", body);
        Assert.DoesNotContain("_browserQueryScope", body);
        Assert.DoesNotContain("ClearSelection", body);
    }

    [Fact]
    public void BrowserMonitoringFolderRefreshed_UsesBrowserScopeForRecursiveRelevanceAndPreservesExactFolderMatchForDirectMode()
    {
        var body = MethodBody("private void BrowserMonitoring_FolderRefreshed");

        Assert.Contains("BrowserScopeMode.IncludeSubfolders", body);
        Assert.Contains("BrowserScope.IsWithinFolderScope(request.RelativeFolder, location.RelativeFolder)", body);
        Assert.Contains(
            "!string.Equals(location.RelativeFolder ?? \"\", request.RelativeFolder ?? \"\", StringComparison.OrdinalIgnoreCase)",
            body);
    }

    [Fact]
    public void BrowserMonitoringFolderRefreshed_CoalescesRecursiveRefreshesThroughTheDedicatedDebounceTimerRatherThanRefreshingImmediately()
    {
        var body = MethodBody("private void BrowserMonitoring_FolderRefreshed");

        var recursiveBranch = body.IndexOf("BrowserScopeMode.IncludeSubfolders", StringComparison.Ordinal);
        var debounceUsage = body.IndexOf("_browserRecursiveRefreshDebounceTimer.Start();", StringComparison.Ordinal);
        Assert.True(debounceUsage > recursiveBranch, "Recursive relevance must route through the debounce timer, not an immediate refresh.");
        // Direct mode is unchanged: an immediate refresh call still exists for the exact-folder-match branch.
        Assert.Contains("_ = RunBrowserNavigationAsync(() => _browserNavigation.RefreshAsync());", body);
    }

    [Fact]
    public void RestoreBrowserLocationAsync_AppliesTheSavedScopeModeBeforeRestoringThroughTheNormalNavigationPath()
    {
        var body = MethodBody("private async Task RestoreBrowserLocationAsync");

        var scopeModeApplied = body.IndexOf("_browserNavigation.SetScopeModeAsync(", StringComparison.Ordinal);
        var restoreCalled = body.IndexOf("BrowserLocationRestoration.RestoreAsync(", StringComparison.Ordinal);
        Assert.True(scopeModeApplied >= 0 && restoreCalled >= 0);
        Assert.True(scopeModeApplied < restoreCalled,
            "The saved scope mode must be applied before restoration navigates, so restoration uses the normal direct/recursive loading path rather than a separate startup-only recursive path.");
        Assert.Contains("saved.IncludeSubfolders", body);
    }

    [Fact]
    public void Constructor_WiresTheRecursiveDiscoveryServiceIntoTheNavigationSession()
    {
        var source = Source();
        Assert.Contains("storage.RecursiveMediaDiscovery", source);
    }

    [Fact]
    public void Closed_StopsTheRecursiveRefreshDebounceTimer()
    {
        var body = MethodBody("Closed += (_, _) =>");
        Assert.Contains("_browserRecursiveRefreshDebounceTimer.Stop();", body);
    }

    private static string MethodBody(string signaturePrefix)
    {
        var source = Source();
        var methodStart = source.IndexOf(signaturePrefix, StringComparison.Ordinal);
        Assert.True(methodStart >= 0, $"'{signaturePrefix}' not found in MainWindow.xaml.cs");
        var methodEnd = source.IndexOf("\n    private", methodStart + 1, StringComparison.Ordinal);
        if (methodEnd < 0) methodEnd = source.Length;
        return source[methodStart..methodEnd];
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
