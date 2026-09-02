using System.Xml.Linq;
using Xunit;

namespace LightflowStudio.Tests;

/// <summary>
/// Source-text regression coverage for expand-vs-select/navigate separation, the monitoring-debounce-during-
/// load guard, and the filled-icon accent brush — mirroring the technique already used by
/// BrowserRecursiveScopeRegressionTests/WorkspaceRestorationRegressionTests for ordering-sensitive or
/// structural behavior that isn't practical to exercise through a live WPF Window in a headless test run.
/// </summary>
public sealed class BrowserTreeInteractionRegressionTests
{
    [Fact]
    public void BrowserFolderTreeItemExpanded_NeverTriggersNavigationOrSelection()
    {
        // Root cause of three symptoms at once: (1) clicking the disclosure chevron used to also select the
        // row and replace the Browser grid/address bar, (2) BrowserTreeModel.EnsurePathChain expanding every
        // ancestor while revealing a deep restored/direct-path location could fire this handler for an
        // unrelated, shallower ancestor and race the real in-flight navigation, and (3) each such spurious
        // navigation restarted any active recursive scan from a fresh WalkState, resetting its progress to
        // zero. Expansion must only ever materialize real children for lazy-loading.
        var body = MethodBody("private async void BrowserFolderTreeItem_Expanded");

        Assert.DoesNotContain("RunBrowserNavigationAsync", body);
        Assert.DoesNotContain("NavigateToRootAsync", body);
        Assert.DoesNotContain("NavigateToPathAsync", body);
        Assert.DoesNotContain("RequestBrowserTreeSelection", body);
    }

    [Fact]
    public void BrowserFolderTreeItemExpanded_StillMaterializesRealChildrenForLazyLoading()
    {
        var body = MethodBody("private async void BrowserFolderTreeItem_Expanded");

        Assert.Contains("_storage.MediaFolders.EnumerateAsync(", body);
        Assert.Contains("_browserTree.ApplyDirectoryListing(node, rootPath, listing.Entries);", body);
        Assert.Contains("SyncBrowserTreeRecursiveIcons();", body);
        // Guarded by the same reentrancy flag as every other programmatic tree mutation, and only proceeds for
        // a node that still has an unmaterialized (placeholder) child — a no-op for an already-materialized
        // or already-expanded node, exactly like before.
        Assert.Contains("_synchronizingBrowserTree", body);
        Assert.Contains("node.Children.Any(child => child.IsPlaceholder)", body);
    }

    [Fact]
    public void BrowserFolderTreeItemExpanded_NeverLeavesTheLoadingPlaceholderStuckOnAFailedMaterialization()
    {
        // #124: every early-return path in this method (no Catalog anchor yet — a bare, never-clicked Volume
        // row; the anchor no longer resolves to a physical path; an enumeration exception; an unsuccessful
        // listing) used to simply `return`, leaving the node's "Loading…" placeholder child visibly stuck
        // forever with no further feedback — reported as some top-level/source folders' disclosure expansion
        // getting permanently stuck on "Loading…". Every one of those paths must instead collapse the node
        // back to a closed, re-expandable state.
        var body = MethodBody("private async void BrowserFolderTreeItem_Expanded");

        var noAnchor = body.IndexOf("node.RootId is not { } rootId || node.RelativeFolder is not { } relativeFolder)", StringComparison.Ordinal);
        var noPhysicalPath = body.IndexOf("root?.PhysicalPath is not { } rootPath)", StringComparison.Ordinal);
        var enumerationException = body.IndexOf("catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)", StringComparison.Ordinal);
        var unsuccessfulListing = body.IndexOf("if (!listing.Succeeded)", StringComparison.Ordinal);
        Assert.True(noAnchor >= 0 && noPhysicalPath > noAnchor && enumerationException > noPhysicalPath &&
            unsuccessfulListing > enumerationException, "Expected all four early-return sites, in order, in BrowserFolderTreeItem_Expanded.");

        // Every one of the four sites calls CollapseUnmaterializableNode before its own next early-return path
        // begins (or, for the last, before the method body ends) — none of the four falls through to a bare
        // `return;` on its own.
        var afterNoAnchor = body[noAnchor..noPhysicalPath];
        var afterNoPhysicalPath = body[noPhysicalPath..enumerationException];
        var afterEnumerationException = body[enumerationException..unsuccessfulListing];
        var afterUnsuccessfulListing = body[unsuccessfulListing..];
        Assert.Contains("CollapseUnmaterializableNode(node);", afterNoAnchor);
        Assert.Contains("CollapseUnmaterializableNode(node);", afterNoPhysicalPath);
        Assert.Contains("CollapseUnmaterializableNode(node);", afterEnumerationException);
        Assert.Contains("CollapseUnmaterializableNode(node);", afterUnsuccessfulListing);
    }

    [Fact]
    public void CollapseUnmaterializableNode_ClosesTheNodeUnderTheSameReentrancyGuardAsEveryOtherProgrammaticTreeMutation()
    {
        var body = MethodBody("private void CollapseUnmaterializableNode");

        Assert.Contains("_synchronizingBrowserTree = true;", body);
        Assert.Contains("node.IsExpanded = false;", body);
        Assert.Contains("_synchronizingBrowserTree = false;", body);
        // Never selects/navigates — only ever touches IsExpanded.
        Assert.DoesNotContain("RequestBrowserTreeSelection", body);
        Assert.DoesNotContain("RunBrowserNavigationAsync", body);
    }

    [Fact]
    public void BrowserFolderTreeSelectedItemChanged_IsTheOnlyHandlerThatNavigates()
    {
        // Selecting a row (mouse click or keyboard) is still the one and only action that changes Browser
        // scope/contents — this pins that down explicitly now that Expanded no longer does.
        var body = MethodBody("private async void BrowserFolderTree_SelectedItemChanged");

        Assert.Contains("RunBrowserNavigationAsync", body);
        Assert.Contains("RequestBrowserTreeSelection(node);", body);
    }

    [Fact]
    public void BrowserFolderTreeSelectedItemChanged_IgnoresADeferredEventForANodeItPassivelyRevealed()
    {
        // TreeView.SelectedItemChanged fires whenever TreeView.SelectedItem changes for any reason, including
        // a purely passive, programmatic IsSelected push — and for a node whose container WPF has not yet
        // realized (routine for a folder never visited before), that event is deferred to a later,
        // unpredictable layout pass rather than firing synchronously, so no fixed dispatcher-priority delay
        // can reliably still be "inside" a synchronization window by the time it lands. Comparing against the
        // specific node the most recent passive reveal targeted closes that gap regardless of how long WPF
        // defers it, since that reveal's own navigation (if any) is already being driven independently and
        // must never be raced by a second, competing one here.
        var body = MethodBody("private async void BrowserFolderTree_SelectedItemChanged");
        Assert.Contains(
            "if (ReferenceEquals(node, _browserTreeRevealedNode)) { _browserTreeRevealedNode = null; return; }",
            body);

        // Set only by the two passive-reveal overloads — never by the interactive-click overload, which is
        // always immediately followed by an actual navigation call from its own caller.
        var locationOverload = MethodBody("private void RequestBrowserTreeSelection(BrowserLocation? location)");
        Assert.Contains("_browserTreeRevealedNode = node;", locationOverload);
        var pathOverload = MethodBody("private void RequestBrowserTreeSelection(string absolutePath)");
        Assert.Contains("_browserTreeRevealedNode = node;", pathOverload);
        var nodeOverload = MethodBody("private void RequestBrowserTreeSelection(BrowserTreeNode node)");
        Assert.DoesNotContain("_browserTreeRevealedNode", nodeOverload);
    }

    [Fact]
    public void RevealBrowserTreeAncestorsAsync_SyncsRecursiveIconsImmediatelyAfterEachAncestorRatherThanOnlyAtTheEndOfTheLoop()
    {
        // #124: this loop can exit early (the two generation checks below) when a newer navigation supersedes
        // it mid-materialization — a real, multi-await path with no synchronous alternative, and specifically
        // the common case for the very FIRST visit to a recursive subtree in a session (every ancestor
        // genuinely needs a real enumeration; an already-visited subtree's ancestors are already materialized
        // and this whole loop is a same-generation no-op for it) — exactly startup restoration into a
        // recursive descendant. When SyncBrowserTreeRecursiveIcons was only called once, after the whole loop,
        // any ancestor this call HAD already materialized (ApplyDirectoryListing sets real RootId/RelativeFolder
        // identity) before an abort never received an IsRecursiveScope value at all — defaulting to false — and
        // nothing downstream was guaranteed to revisit a node that only became known moments before the abort.
        // Reported specifically as the startup-restored recursive root (and its siblings) never showing the
        // filled icon, persisting even after disabling and re-enabling Include Subfolders. The fix: sync
        // immediately after each individual ApplyDirectoryListing call, inside the loop, so every ancestor this
        // method successfully materializes gets a correct icon state before any later generation check can
        // ever skip it.
        var body = MethodBody("private async Task RevealBrowserTreeAncestorsAsync");

        var applyDirectoryListing = body.IndexOf("_browserTree.ApplyDirectoryListing(ancestor, location.RootPath, listing.Entries);", StringComparison.Ordinal);
        Assert.True(applyDirectoryListing >= 0, "ApplyDirectoryListing call not found in RevealBrowserTreeAncestorsAsync.");
        var syncAfterApply = body.IndexOf("SyncBrowserTreeRecursiveIcons();", applyDirectoryListing, StringComparison.Ordinal);
        Assert.True(syncAfterApply >= 0, "SyncBrowserTreeRecursiveIcons() must be called after ApplyDirectoryListing inside the loop.");

        // The sync call must be reachable regardless of whether the loop's own foreach body continues to a
        // next iteration or the method returns right after — i.e. it must not be gated behind the loop's own
        // closing brace/the trailing post-loop generation check.
        var loopEnd = body.IndexOf("if (generation == _browserUiGeneration && _browserTree.SelectedNode is { } selected)", StringComparison.Ordinal);
        Assert.True(loopEnd < 0 || syncAfterApply < loopEnd,
            "SyncBrowserTreeRecursiveIcons() must run inside the loop, not deferred to a post-loop check that a generation change can skip.");
    }

    [Fact]
    public void RecursiveRefreshDebounceTick_SkipsRestartingANavigationThatIsAlreadyLoading()
    {
        // #124: a relevant monitoring event (most commonly the recursive scan's own folder reads, which some
        // drives/watchers — particularly removable/network media — report back as spurious "changed" events)
        // arriving while a load is already in flight must never restart it: the in-flight load already
        // performs a full, current pass over the same scope, and restarting would cancel it mid-walk and
        // silently reset FoldersVisited to zero, making one continuous recursive scan look like it keeps
        // restarting every time a descendant folder is touched.
        var source = Source();
        var tickStart = source.IndexOf("_browserRecursiveRefreshDebounceTimer.Tick += (_, _) =>", StringComparison.Ordinal);
        Assert.True(tickStart >= 0, "_browserRecursiveRefreshDebounceTimer.Tick handler not found in MainWindow.xaml.cs");
        var tickEnd = source.IndexOf("};", tickStart, StringComparison.Ordinal);
        var body = source[tickStart..tickEnd];

        var loadingGuard = body.IndexOf("if (BrowserLoadingOverlay.Visibility == Visibility.Visible) return;", StringComparison.Ordinal);
        var refreshCall = body.IndexOf("_ = RunBrowserNavigationAsync(() => _browserNavigation.RefreshAsync());", StringComparison.Ordinal);
        Assert.True(loadingGuard >= 0 && refreshCall > loadingGuard,
            "The already-loading guard must run before the refresh it would otherwise restart.");
    }

    [Fact]
    public void FilledFolderIcons_UseTheSharedLightflowAccentBrushNotAHardcodedExplorerLikeColor()
    {
        var document = XDocument.Load(Path.Combine(FindRepositoryRoot(), "LightflowStudio", "MainWindow.xaml"));
        var ns = document.Root!.Name.Namespace;
        var xNamespace = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");

        var folderIconFilled = document.Descendants(ns + "Path")
            .Single(path => (string?)path.Attribute(xNamespace + "Name") == "FolderIconFilled");
        Assert.Equal("{StaticResource ShellFocusBrush}", (string?)folderIconFilled.Attribute("Fill"));

        // The outline glyph keeps its own pre-existing folder color, unaffected — only the filled state adopts
        // the shared accent.
        var folderIcon = document.Descendants(ns + "TextBlock")
            .Single(block => (string?)block.Attribute(xNamespace + "Name") == "FolderIcon");
        Assert.Equal("#E4B85A", (string?)folderIcon.Attribute("Foreground"));

        var scopeStyle = document.Descendants(ns + "Style")
            .Single(style => (string?)style.Attribute(xNamespace + "Key") == "BrowserScopeToggleButtonStyle");
        var checkedTrigger = scopeStyle.Descendants(ns + "Trigger")
            .Single(trigger => (string?)trigger.Attribute("Property") == "IsChecked" && (string?)trigger.Attribute("Value") == "True");
        Assert.Contains(checkedTrigger.Elements(ns + "Setter"), setter =>
            (string?)setter.Attribute("TargetName") == "ScopeGlyphFilled" && (string?)setter.Attribute("Property") == "Fill" &&
            (string?)setter.Attribute("Value") == "{StaticResource ShellFocusBrush}");
        // Same brush the checked-state Chrome border already uses — the toolbar's own established
        // "active" accent, reused rather than a second, independent accent value.
        Assert.Contains(checkedTrigger.Elements(ns + "Setter"), setter =>
            (string?)setter.Attribute("TargetName") == "Chrome" && (string?)setter.Attribute("Property") == "BorderBrush" &&
            (string?)setter.Attribute("Value") == "{StaticResource ShellFocusBrush}");
    }

    [Fact]
    public void NoElementInMainWindowUsesTheOldExplorerLikeAmberForAFilledRecursiveOrSelectedIcon()
    {
        var source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "LightflowStudio", "MainWindow.xaml"));
        Assert.DoesNotContain("Fill=\"#E4B85A\"", source);
    }

    [Fact]
    public void TreeRowIconIsDrivenByExactlyOneAuthoritativeDataTriggerNotTwoCompetingOnes()
    {
        // #124 (further revised): a stored recursive root's filled icon must never depend on which folder is
        // currently selected. Two independently-firing DataTriggers (IsSelected, IsRecursiveScope) each
        // targeting the same FolderIcon/FolderIconFilled Visibility could let one state's Setter application
        // order mask the other, letting the icon incorrectly fall back to outline when selection moved away
        // even though the row's own recursive-scope state never changed. Pinning this to exactly one trigger,
        // bound to the single derived BrowserTreeNode.IsFilledFolderIcon, removes any dependency on WPF
        // trigger-precedence ordering.
        var document = XDocument.Load(Path.Combine(FindRepositoryRoot(), "LightflowStudio", "MainWindow.xaml"));
        var ns = document.Root!.Name.Namespace;

        var template = document.Descendants(ns + "HierarchicalDataTemplate")
            .Single(element => (string?)element.Attribute("DataType") == "{x:Type local:BrowserTreeNode}");
        var iconTriggers = template.Descendants(ns + "DataTrigger")
            .Where(trigger => (string?)trigger.Attribute("Binding") is { } binding &&
                (binding.Contains("IsSelected") || binding.Contains("IsRecursiveScope") || binding.Contains("IsFilledFolderIcon")))
            .ToList();

        var onlyTrigger = Assert.Single(iconTriggers);
        Assert.Equal("{Binding IsFilledFolderIcon}", (string?)onlyTrigger.Attribute("Binding"));
        Assert.DoesNotContain(template.Descendants(ns + "DataTrigger"),
            trigger => (string?)trigger.Attribute("Binding") == "{Binding IsSelected}");
        Assert.DoesNotContain(template.Descendants(ns + "DataTrigger"),
            trigger => (string?)trigger.Attribute("Binding") == "{Binding IsRecursiveScope}");
    }

    [Fact]
    public void ShowBrowserLoadingState_HidesTheStalePreviousGridInsteadOfLettingItShowThroughTheOverlay()
    {
        // #124: BrowserLoadingOverlay's own background is deliberately semi-transparent (so the progress bar
        // it hosts stays legible against the shell) — that previously let the previous folder's media tiles
        // remain faintly visible underneath it while a new scope loaded. Hiding the grid outright here, not
        // merely painting over it, is what "the prior scope stops being presented" actually requires.
        var body = MethodBody("private void ShowBrowserLoadingState");

        var emptyHidden = body.IndexOf("BrowserEmptyState.Visibility = Visibility.Collapsed;", StringComparison.Ordinal);
        var gridHidden = body.IndexOf("BrowserGridRows.Visibility = Visibility.Collapsed;", StringComparison.Ordinal);
        var overlayShown = body.IndexOf("BrowserLoadingOverlay.Visibility = Visibility.Visible;", StringComparison.Ordinal);
        Assert.True(emptyHidden >= 0, "ShowBrowserLoadingState must hide the stale empty state.");
        Assert.True(gridHidden >= 0, "ShowBrowserLoadingState must hide the stale grid content.");
        Assert.True(overlayShown > gridHidden,
            "The stale grid must be hidden before (or alongside) showing the loading overlay, never left visible under it.");
    }

    [Fact]
    public void ApplyBrowserState_RestoresGridVisibilityThatLoadingHidForTheNewlyAcceptedScope()
    {
        var body = MethodBody("private void ApplyBrowserState");
        Assert.Contains("BrowserGridRows.Visibility = Visibility.Visible;", body);
    }

    [Fact]
    public void ApplyBrowserSuccessState_RejectsAStaleGenerationBeforeEverTouchingGridOrEmptyStateVisibility()
    {
        // #124: a superseded navigation's late completion (A accepted, request B, request C before B finishes)
        // must never reach ApplyBrowserState/its grid-visibility restore — the generation check happens first,
        // as an early return, so a stale B can't flicker C's already-current presentation back to B's content.
        var body = MethodBody("private bool ApplyBrowserSuccessState");
        var generationCheck = body.IndexOf("generation != _browserUiGeneration", StringComparison.Ordinal);
        var applyCall = body.IndexOf("ApplyBrowserState(state);", StringComparison.Ordinal);
        Assert.True(generationCheck >= 0 && applyCall > generationCheck,
            "The generation check must guard ApplyBrowserState, not run after it.");
    }

    [Fact]
    public void ApplyBrowserNavigationFailure_RestoresGridVisibilityForThePreviousFolderThatRemainsLoaded()
    {
        // A failed navigation never replaced the still-current BrowserGridModel data — only ShowBrowserLoadingState's
        // presentation-level hide needs undoing here, consistent with this method's own diagnostic text ("The
        // previous folder remains loaded").
        var body = MethodBody("private void ApplyBrowserNavigationFailure");
        Assert.Contains("BrowserGridRows.Visibility = Visibility.Visible;", body);
        Assert.Contains("previous folder remains loaded", body);
    }

    [Fact]
    public void BrowserIncludeSubfoldersButtonClick_NeverTouchesTreeSelectionDirectlyOnlyReloadsTheCurrentLocation()
    {
        // #124: toggling Include Subfolders must change scope MODE for whichever folder is already open, never
        // BROWSER LOCATION — this handler must never call RequestBrowserTreeSelection itself (which would imply
        // picking a *different* node); the existing selection is left exactly where it already is, and
        // BrowserNavigationSession.SetIncludeSubfoldersAsync (proven via
        // SetIncludeSubfoldersAsync_ToggleOffSequence_PreservesTreeSelectionAndRevertsIconsAcrossSessionAndTreeLayersTogether
        // in BrowserNavigationTests.cs) reloads that exact same location.
        var body = MethodBody("private async void BrowserIncludeSubfoldersButton_Click");

        Assert.Contains("_browserNavigation.SetIncludeSubfoldersAsync(enabled)", body);
        Assert.DoesNotContain("RequestBrowserTreeSelection", body);
        Assert.DoesNotContain("NavigateToRootAsync", body);
        Assert.DoesNotContain("NavigateToPathAsync", body);
    }

    [Fact]
    public void SyncBrowserSubfoldersCapability_NeverProbesTheFilesystemAndAlwaysLetsEffectiveRecursiveModeOverrideNoSubfolders()
    {
        // #124: the toggle must disable itself when the selected folder definitively has no child folders and
        // isn't already effectively recursive — but effective recursive mode must always win regardless, since
        // an inherited recursive LEAF must remain able to turn itself OFF (that's how its governing ancestor
        // root gets removed). Reuses BrowserTreeNode.HasSubfolders (already-known data from the same folder
        // listing every navigation already fetches) rather than any synchronous filesystem/IO call of its own.
        var body = MethodBody("private void SyncBrowserSubfoldersCapability");

        Assert.Contains("_browserTree.SelectedNode?.HasSubfolders", body);
        Assert.Contains("!effectiveRecursive && definitelyNoSubfolders", body);
        Assert.DoesNotContain("Directory.", body);
        Assert.DoesNotContain("EnumerateAsync", body);
        Assert.DoesNotContain("File.", body);

        var callSite = MethodBody("private void ApplyBrowserState");
        Assert.Contains("SyncBrowserSubfoldersCapability(state);", callSite);
        // Runs after Synchronize populates the newly-selected node's real children, not before.
        var synchronizeIndex = callSite.IndexOf("_browserTree.Synchronize(state)", StringComparison.Ordinal);
        var capabilityIndex = callSite.IndexOf("SyncBrowserSubfoldersCapability(state);", StringComparison.Ordinal);
        Assert.True(synchronizeIndex >= 0 && capabilityIndex > synchronizeIndex);
    }

    [Fact]
    public void SyncBrowserSubfoldersCapability_ShowsAConciseTooltipOnlyWhenActuallyDisabledForLackOfSubfolders()
    {
        var body = MethodBody("private void SyncBrowserSubfoldersCapability");
        Assert.Contains("\"No subfolders\"", body);
        Assert.Contains("BrowserIncludeSubfoldersDefaultToolTip", body);
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
