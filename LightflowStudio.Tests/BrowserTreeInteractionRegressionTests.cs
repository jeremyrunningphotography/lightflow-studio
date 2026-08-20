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
    public void BrowserFolderTreeSelectedItemChanged_IsTheOnlyHandlerThatNavigates()
    {
        // Selecting a row (mouse click or keyboard) is still the one and only action that changes Browser
        // scope/contents — this pins that down explicitly now that Expanded no longer does.
        var body = MethodBody("private async void BrowserFolderTree_SelectedItemChanged");

        Assert.Contains("RunBrowserNavigationAsync", body);
        Assert.Contains("RequestBrowserTreeSelection(node);", body);
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
