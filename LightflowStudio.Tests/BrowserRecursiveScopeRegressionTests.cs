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
        // clearing is a separate, scope-mode-aware identity — including when navigating between two folders
        // that inherit the very same Catalog recursive root (e.g. 2026 -> 2026/August): they are still
        // different RelativeFolder values, so this identity still changes and selection still clears.
        Assert.Contains("if (scope != _browserQueryScope) ResetBrowserQueryToolbar();", body);
        Assert.Contains("(identityLocation.RootId, identityLocation.RelativeFolder, state.Mode)", body);
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
    public void ApplyBrowserState_SyncsTheScopeToggleAndItsEnabledStateAndTheTreesRecursiveIcons()
    {
        var body = MethodBody("private void ApplyBrowserState");
        Assert.Contains("BrowserIncludeSubfoldersButton.IsEnabled = state.Location is not null;", body);
        Assert.Contains("SyncBrowserScopeToggle();", body);
        Assert.Contains("SyncBrowserTreeRecursiveIcons();", body);
        // The full stored Catalog root list arrives on the very state that already determined effective mode
        // — reused here for tree iconography rather than a second Catalog round-trip.
        Assert.Contains("_browserRecursiveRoots = state.RecursiveRoots ?? [];", body);
    }

    [Fact]
    public void ApplyBrowserState_PersistsOnlyThePlainFolderIdentityNeverScopeModeToWorkspaceState()
    {
        // #124 (revised): recursive-root configuration is durable Catalog data now, not workspace state (see
        // BrowserRecursiveRoot) — only the plain folder identity is remembered here, and WorkspaceStateService
        // no longer even accepts a scope-mode argument.
        var body = MethodBody("private void ApplyBrowserState");
        // The call takes exactly three arguments and ends the statement right there — no fourth
        // scope-mode/IncludeSubfolders argument is threaded through to workspace state.
        Assert.Contains("_workspaceState.SetBrowserLocation(location.RootId, location.RelativeFolder, location.AbsolutePath);", body);
    }

    [Fact]
    public void BrowserIncludeSubfoldersButtonClick_UsesSetIncludeSubfoldersAsyncAndNeverResetsTheQueryToolbar()
    {
        var body = MethodBody("private async void BrowserIncludeSubfoldersButton_Click");

        Assert.Contains("_browserNavigation.SetIncludeSubfoldersAsync(enabled)", body);
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
    public void RestoreBrowserLocationAsync_NeedsNoScopeModeStepBecauseEffectiveModeIsAlwaysDerivedDuringNavigation()
    {
        // #124 (revised): there is no longer a settable scope-mode field to prime before restoring — effective
        // mode is derived live from the Catalog as part of ordinary navigation, so restoration just drives the
        // same BrowserLocationRestoration.RestoreAsync path every other Locations interaction uses.
        var body = MethodBody("private async Task RestoreBrowserLocationAsync");

        Assert.Contains("BrowserLocationRestoration.RestoreAsync(_browserNavigation, _storage.MediaRoots, saved)", body);
        Assert.DoesNotContain("SetIncludeSubfoldersAsync", body);
        Assert.DoesNotContain("SetScopeModeAsync", body);
        Assert.DoesNotContain("saved.IncludeSubfolders", body);
    }

    [Fact]
    public void Constructor_WiresTheRecursiveDiscoveryServiceIntoTheNavigationSession()
    {
        var source = Source();
        Assert.Contains("storage.RecursiveMediaDiscovery", source);
    }

    [Fact]
    public void Constructor_WiresTheCatalogBackedRecursiveRootServiceIntoTheNavigationSession()
    {
        // #124 (revised): effective recursive mode is derived from durable Catalog roots, not a settable
        // session field — the navigation session needs the Catalog-backed service to do that derivation.
        var source = Source();
        Assert.Contains("storage.BrowserRecursiveRoots", source);
    }

    [Fact]
    public void Constructor_SubscribesToRecursiveScopeProgressChangedRightAfterConstructingTheSession()
    {
        var source = Source();
        var constructed = source.IndexOf(
            "_browserNavigation = new BrowserNavigationSession(", StringComparison.Ordinal);
        var subscribed = source.IndexOf(
            "_browserNavigation.RecursiveScopeProgressChanged += BrowserNavigation_RecursiveScopeProgressChanged;",
            StringComparison.Ordinal);
        Assert.True(constructed >= 0 && subscribed >= 0);
        Assert.True(subscribed > constructed && subscribed - constructed < 400,
            "The subscription should sit right beside session construction, not somewhere unrelated.");
    }

    [Fact]
    public void Constructor_SubscribesToEffectiveScopeDeterminedRightAfterConstructingTheSession()
    {
        var source = Source();
        var constructed = source.IndexOf(
            "_browserNavigation = new BrowserNavigationSession(", StringComparison.Ordinal);
        var subscribed = source.IndexOf(
            "_browserNavigation.EffectiveScopeDetermined += BrowserNavigation_EffectiveScopeDetermined;",
            StringComparison.Ordinal);
        Assert.True(constructed >= 0 && subscribed >= 0);
        Assert.True(subscribed > constructed && subscribed - constructed < 400,
            "The subscription should sit right beside session construction, not somewhere unrelated.");
    }

    [Fact]
    public void Closed_StopsTheRecursiveRefreshDebounceTimer()
    {
        var body = MethodBody("Closed += (_, _) =>");
        Assert.Contains("_browserRecursiveRefreshDebounceTimer.Stop();", body);
    }

    [Fact]
    public void Closed_UnsubscribesFromRecursiveScopeProgressChangedBeforeDisposingTheSession()
    {
        var body = MethodBody("Closed += (_, _) =>");
        var unsubscribed = body.IndexOf(
            "_browserNavigation.RecursiveScopeProgressChanged -= BrowserNavigation_RecursiveScopeProgressChanged;",
            StringComparison.Ordinal);
        var disposed = body.IndexOf("_browserNavigation.Dispose();", StringComparison.Ordinal);
        Assert.True(unsubscribed >= 0 && disposed >= 0);
        Assert.True(unsubscribed < disposed, "Unsubscribing before Dispose avoids a handler firing against a disposed session.");
    }

    [Fact]
    public void Closed_UnsubscribesFromEffectiveScopeDeterminedBeforeDisposingTheSession()
    {
        var body = MethodBody("Closed += (_, _) =>");
        var unsubscribed = body.IndexOf(
            "_browserNavigation.EffectiveScopeDetermined -= BrowserNavigation_EffectiveScopeDetermined;",
            StringComparison.Ordinal);
        var disposed = body.IndexOf("_browserNavigation.Dispose();", StringComparison.Ordinal);
        Assert.True(unsubscribed >= 0 && disposed >= 0);
        Assert.True(unsubscribed < disposed, "Unsubscribing before Dispose avoids a handler firing against a disposed session.");
    }

    [Fact]
    public void RunBrowserNavigationAsync_EntersLoadingThroughTheSharedShowBrowserLoadingStateEntryPoint()
    {
        // #124 (further revised): the reset-progress-before-overlay ordering, and retiring any stale empty
        // state, now both live centrally in ShowBrowserLoadingState (see WorkspaceRestorationRegressionTests
        // for that ordering) — this only confirms RunBrowserNavigationAsync goes through it rather than
        // duplicating those steps inline, which is what previously let a stale BrowserEmptyState survive
        // into a new navigation's loading presentation.
        var body = MethodBody("private async Task RunBrowserNavigationAsync");
        Assert.Contains("ShowBrowserLoadingState(", body);
    }

    [Fact]
    public void ResetBrowserLoadingProgress_SetsIndeterminateRatherThanLeavingAStaleDeterminateValue()
    {
        var body = MethodBody("private void ResetBrowserLoadingProgress");
        Assert.Contains("BrowserLoadingProgressBar.IsIndeterminate = true;", body);
    }

    [Fact]
    public void BrowserNavigationRecursiveScopeProgressChanged_MarshalsToTheUiThreadBeforeTouchingTheProgressBar()
    {
        var body = MethodBody("private void BrowserNavigation_RecursiveScopeProgressChanged");
        Assert.Contains("Dispatcher.BeginInvoke(() => ApplyRecursiveScopeLoadingProgress(progress));", body);
    }

    [Fact]
    public void BrowserNavigationEffectiveScopeDetermined_MarshalsToTheUiThreadAndUpdatesIconsAndToggleWithoutWaitingForDiscovery()
    {
        var body = MethodBody("private void BrowserNavigation_EffectiveScopeDetermined");
        Assert.Contains("Dispatcher.BeginInvoke(() =>", body);
        Assert.Contains("_browserRecursiveRoots = scope.RecursiveRoots;", body);
        Assert.Contains("SyncBrowserTreeRecursiveIcons();", body);
        Assert.Contains("SyncBrowserScopeToggle(scope.Mode);", body);
    }

    [Fact]
    public void ApplyRecursiveScopeLoadingProgress_NeverFabricatesAPercentageAndStaysIndeterminateBelowThreshold()
    {
        var body = MethodBody("private void ApplyRecursiveScopeLoadingProgress");
        Assert.Contains("if (progress.FoldersDiscovered < 2) { BrowserLoadingProgressBar.IsIndeterminate = true; return; }", body);
        Assert.Contains("BrowserLoadingProgressBar.Maximum = progress.FoldersDiscovered;", body);
        Assert.Contains("BrowserLoadingProgressBar.Value = Math.Min(progress.FoldersVisited, progress.FoldersDiscovered);", body);
    }

    [Fact]
    public void SyncBrowserTreeRecursiveIcon_DerivesTheIconPurelyFromNodeIdentityWithoutMaterializingOrTouchingSelection()
    {
        var body = MethodBody("private void SyncBrowserTreeRecursiveIcon(BrowserTreeNode node)");

        Assert.Contains("if (!node.IsPlaceholder && node.RootId is { } rootId && node.RelativeFolder is { } relativeFolder)", body);
        Assert.Contains(
            "node.IsRecursiveScope = BrowserRecursiveRootLogic.IsEffectivelyRecursive(_browserRecursiveRoots, rootId, relativeFolder);",
            body);
        // Purely identity-derived: no filesystem/Catalog work, no forced expansion/materialization of
        // collapsed nodes, and selection/focus styling stays fully independent of the icon.
        Assert.DoesNotContain("EnumerateAsync", body);
        Assert.DoesNotContain("IsExpanded = true", body);
        Assert.DoesNotContain("IsSelected", body);
    }

    [Fact]
    public void SyncBrowserTreeRecursiveIcons_WalksEveryTreeRootRecursively()
    {
        var body = MethodBody("private void SyncBrowserTreeRecursiveIcons");
        Assert.Contains("foreach (var root in _browserTree.Roots) SyncBrowserTreeRecursiveIcon(root);", body);
    }

    [Theory]
    [InlineData("private void ApplyBrowserState")]
    [InlineData("private async Task RevealBrowserTreeAncestorsAsync")]
    public void SyncBrowserTreeRecursiveIcons_IsCalledFromEveryPlaceTheTreeGainsOrChangesNodes(string signaturePrefix)
    {
        var body = MethodBody(signaturePrefix);
        Assert.Contains("SyncBrowserTreeRecursiveIcons();", body);
    }

    [Fact]
    public void NoOutlineOrLastVisibleDescendantRemnantsSurviveTheHashtag124Revision()
    {
        var source = Source();
        Assert.DoesNotContain("BrowserScopeOutline", source);
        Assert.DoesNotContain("SyncBrowserScopeOutline", source);
        Assert.DoesNotContain("LastVisibleDescendant", source);
        Assert.DoesNotContain("BrowserFolderTreeItem_Collapsed", source);
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
