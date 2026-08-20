using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Xunit;

namespace LightflowStudio.Tests;

/// <summary>
/// #124: a live-WPF regression seam for the toggle-off interaction, added after hands-on testing reported the
/// Locations tree visibly changing selection when Include Subfolders was turned off from a recursive
/// descendant, with the governing Catalog root left in place — even though BrowserNavigationSession's own
/// integrated session+tree+icon test (SetIncludeSubfoldersAsync_ToggleOffSequence_... in
/// BrowserNavigationTests.cs) already proved that layer correct. These two tests drive the actual, real
/// MainWindow — a real temp-directory Catalog, real filesystem folders, a real generated TreeView/TreeViewItem
/// visual tree, and a real Click routed event on the real BrowserIncludeSubfoldersButton — through two different
/// ways of reaching the descendant (direct-path entry, and real tree-row clicks through the recursive root),
/// rather than any fake/model surface, so a defect specific to the WPF container/focus/event layer would surface
/// here even if every service/model-level test stays green. Both currently pass: extensive hands-on-equivalent
/// testing here could not reproduce the reported symptom — see the PR description for what this does and does
/// not rule out.
/// </summary>
[Collection("STA dispatcher tests")]
public sealed class BrowserToggleOffLiveInteractionTests : IAsyncLifetime
{
    private readonly string _appDataRoot = Path.Combine(Path.GetTempPath(), $"lightflow-live-app-{Guid.NewGuid():N}");
    private readonly string _mediaRoot = Path.Combine(Path.GetTempPath(), $"lightflow-live-media-{Guid.NewGuid():N}");
    private string _folderB = "";

    public Task InitializeAsync()
    {
        Directory.CreateDirectory(Path.Combine(_mediaRoot, "A", "B"));
        _folderB = Path.Combine(_mediaRoot, "A", "B");
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        TryDelete(_appDataRoot);
        TryDelete(_mediaRoot);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task ClickingSubfoldersOff_KeepsTheSameDescendantSelectedAndActuallyRemovesTheGoverningRoot()
    {
        var trace = new List<string>();
        try
        {
            await StaDispatcher.RunAsync(async () =>
            {
                TestWpfApplication.EnsureLoaded();

                var startup = await LightflowStorageCoordinator.StartAsync(_appDataRoot);
                Assert.True(startup.IsReady, startup.Diagnostic);
                var storage = startup.Coordinator!;
                var created = await storage.MediaRoots.CreateAsync("Library", _mediaRoot);
                Assert.True(created.Succeeded, created.Diagnostic);
                var rootId = created.Root!.RootId;
                await storage.BrowserRecursiveRoots.EnableAsync(rootId, "A");
                trace.Add($"setup: rootId={rootId} recursive root established at 'A'");

                var window = NewOffscreenWindow(storage, startup);
                try
                {
                    window.Show();
                    await WaitUntilAsync(() => window.BrowserFolderTree.Items.Count > 0, "storage entries to populate");
                    trace.Add("window shown, Locations tree populated");

                    window.BrowserCurrentPath.Text = _folderB;
                    RaiseClick(window.BrowserGoButton);
                    await WaitUntilAsync(() => window.BrowserLoadingOverlay.Visibility != Visibility.Visible &&
                        string.Equals(window.BrowserCurrentPath.Text, _folderB, StringComparison.OrdinalIgnoreCase),
                        "navigation to folder B to settle");
                    await WaitUntilAsync(() => window.BrowserIncludeSubfoldersButton.IsChecked == true,
                        "toggle to reflect B's inherited recursive mode");
                    trace.Add($"navigated to B; toggle IsChecked={window.BrowserIncludeSubfoldersButton.IsChecked}; " +
                        $"tree SelectedItem={Describe(window.BrowserFolderTree.SelectedItem)}");

                    var selectedBefore = window.BrowserFolderTree.SelectedItem;
                    Assert.NotNull(selectedBefore);
                    var pathBefore = window.BrowserCurrentPath.Text;

                    ClickSubfoldersToggleOff(window, trace);

                    await WaitUntilAsync(() => window.BrowserIncludeSubfoldersButton.IsChecked == false,
                        "toggle to reflect the OFF click");
                    await WaitUntilAsync(() => window.BrowserLoadingOverlay.Visibility != Visibility.Visible,
                        "the resulting reload to finish");
                    // Let any further deferred (DispatcherPriority.Loaded/Background) reveal/focus callbacks drain.
                    await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

                    var recursiveRootsAfter = await storage.BrowserRecursiveRoots.ListAsync();
                    trace.Add($"after toggle-off: tree SelectedItem={Describe(window.BrowserFolderTree.SelectedItem)}; " +
                        $"BrowserCurrentPath='{window.BrowserCurrentPath.Text}'; recursiveRoots remaining={recursiveRootsAfter.Count}");

                    Assert.Same(selectedBefore, window.BrowserFolderTree.SelectedItem);
                    Assert.Equal(pathBefore, window.BrowserCurrentPath.Text, ignoreCase: true);
                    Assert.Empty(recursiveRootsAfter);
                }
                finally
                {
                    window.Close();
                    await storage.DisposeAsync();
                }
            });
        }
        finally
        {
            foreach (var line in trace) Console.WriteLine(line);
        }
    }

    [Fact]
    public async Task ClickingSubfoldersOff_AfterReachingTheDescendantThroughRealTreeRowClicksStillKeepsItSelected()
    {
        // The reported repro specifically describes reaching B by navigating *within* the recursive subtree
        // (real tree-row clicks through A, the recursive root, down to B) rather than direct-path entry — a
        // materially different code path (BrowserFolderTree_SelectedItemChanged's interactive-click
        // RequestBrowserTreeSelection(BrowserTreeNode) overload, and BrowserFolderTreeItem_Expanded's real
        // chevron-expand materialization) from the direct-path overload the first test above exercises. This
        // reproduces that exact shape: real click to open A (the stored recursive root), real chevron-expand to
        // materialize A's children, then a real click on B's own generated TreeViewItem container.
        var trace = new List<string>();
        try
        {
            await StaDispatcher.RunAsync(async () =>
            {
                TestWpfApplication.EnsureLoaded();

                var startup = await LightflowStorageCoordinator.StartAsync(_appDataRoot);
                Assert.True(startup.IsReady, startup.Diagnostic);
                var storage = startup.Coordinator!;
                var created = await storage.MediaRoots.CreateAsync("Library", _mediaRoot);
                Assert.True(created.Succeeded, created.Diagnostic);
                var rootId = created.Root!.RootId;
                var folderA = Path.Combine(_mediaRoot, "A");
                await storage.BrowserRecursiveRoots.EnableAsync(rootId, "A");
                trace.Add($"setup: rootId={rootId} recursive root established at 'A'");

                var window = NewOffscreenWindow(storage, startup);
                try
                {
                    window.Show();
                    await WaitUntilAsync(() => window.BrowserFolderTree.Items.Count > 0, "storage entries to populate");

                    // Real click on the Media Root itself, then a real chevron-expand click (selecting a row
                    // does not itself expand it — EnsurePathChain only expands ancestors while walking to a
                    // *deeper* target), before A's own TreeViewItem container even materializes: a
                    // Visibility=Collapsed ItemsHost (this style's own IsExpanded=False trigger) is skipped
                    // during layout entirely, so WPF never generates containers for a collapsed row's children.
                    window.BrowserCurrentPath.Text = _mediaRoot;
                    RaiseClick(window.BrowserGoButton);
                    await WaitUntilAsync(() => window.BrowserLoadingOverlay.Visibility != Visibility.Visible &&
                        string.Equals(window.BrowserCurrentPath.Text, _mediaRoot, StringComparison.OrdinalIgnoreCase),
                        "navigation to the media root to settle");
                    await SettleLayoutAsync(window);

                    var libraryContainer = FindContainer(window.BrowserFolderTree,
                        node => string.Equals(node.AbsolutePath, _mediaRoot, StringComparison.OrdinalIgnoreCase));
                    Assert.NotNull(libraryContainer);
                    libraryContainer!.IsExpanded = true;
                    await SettleLayoutAsync(window);

                    var aContainer = FindContainer(window.BrowserFolderTree,
                        node => string.Equals(node.AbsolutePath, folderA, StringComparison.OrdinalIgnoreCase));
                    Assert.NotNull(aContainer);
                    aContainer!.IsSelected = true; // a real TreeViewItem selection, exactly what a mouse click sets
                    await WaitUntilAsync(() => window.BrowserLoadingOverlay.Visibility != Visibility.Visible &&
                        string.Equals(window.BrowserCurrentPath.Text, folderA, StringComparison.OrdinalIgnoreCase),
                        "navigation to A to settle");
                    await WaitUntilAsync(() => window.BrowserIncludeSubfoldersButton.IsChecked == true,
                        "toggle to reflect A's own recursive root");
                    trace.Add($"clicked into A (the stored recursive root); toggle IsChecked={window.BrowserIncludeSubfoldersButton.IsChecked}");

                    // Real chevron-expand: setting IsExpanded on the real container raises the Expanded routed
                    // event exactly like clicking the disclosure arrow, running BrowserFolderTreeItem_Expanded's
                    // real materialization.
                    aContainer.IsExpanded = true;
                    await WaitUntilAsync(() => FindContainer(window.BrowserFolderTree,
                        node => string.Equals(node.AbsolutePath, _folderB, StringComparison.OrdinalIgnoreCase)) is not null,
                        "B's real TreeViewItem container to materialize after expanding A");
                    await SettleLayoutAsync(window);

                    var bContainer = FindContainer(window.BrowserFolderTree,
                        node => string.Equals(node.AbsolutePath, _folderB, StringComparison.OrdinalIgnoreCase));
                    Assert.NotNull(bContainer);
                    bContainer!.IsSelected = true; // real click on B
                    await WaitUntilAsync(() => window.BrowserLoadingOverlay.Visibility != Visibility.Visible &&
                        string.Equals(window.BrowserCurrentPath.Text, _folderB, StringComparison.OrdinalIgnoreCase),
                        "navigation to B to settle");
                    await WaitUntilAsync(() => window.BrowserIncludeSubfoldersButton.IsChecked == true,
                        "toggle to reflect B's inherited recursive mode");
                    trace.Add($"clicked into B via its real TreeViewItem; toggle IsChecked={window.BrowserIncludeSubfoldersButton.IsChecked}; " +
                        $"tree SelectedItem={Describe(window.BrowserFolderTree.SelectedItem)}");

                    var selectedBefore = window.BrowserFolderTree.SelectedItem;
                    Assert.NotNull(selectedBefore);
                    Assert.Same(bContainer.DataContext, selectedBefore);
                    var pathBefore = window.BrowserCurrentPath.Text;

                    ClickSubfoldersToggleOff(window, trace);

                    await WaitUntilAsync(() => window.BrowserIncludeSubfoldersButton.IsChecked == false,
                        "toggle to reflect the OFF click");
                    await WaitUntilAsync(() => window.BrowserLoadingOverlay.Visibility != Visibility.Visible,
                        "the resulting reload to finish");
                    await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

                    var recursiveRootsAfter = await storage.BrowserRecursiveRoots.ListAsync();
                    trace.Add($"after toggle-off: tree SelectedItem={Describe(window.BrowserFolderTree.SelectedItem)}; " +
                        $"BrowserCurrentPath='{window.BrowserCurrentPath.Text}'; recursiveRoots remaining={recursiveRootsAfter.Count}");

                    Assert.Same(selectedBefore, window.BrowserFolderTree.SelectedItem);
                    Assert.Equal(pathBefore, window.BrowserCurrentPath.Text, ignoreCase: true);
                    Assert.Empty(recursiveRootsAfter);
                }
                finally
                {
                    window.Close();
                    await storage.DisposeAsync();
                }
            });
        }
        finally
        {
            foreach (var line in trace) Console.WriteLine(line);
        }
    }

    private static MainWindow NewOffscreenWindow(LightflowStorageCoordinator storage, StorageStartupResult startup) =>
        new(storage, startup.Status, startup.Diagnostic)
        {
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -32000,
            Top = -32000,
            ShowInTaskbar = false
        };

    /// <summary>
    /// Real click, not a model/property shortcut: focuses the button first (matching real mouse-click focus
    /// transfer), then reproduces the exact IsChecked-then-Click order a real mouse click produces. NOTE:
    /// ToggleButtonAutomationPeer.Toggle() (IToggleProvider) was tried first and rejected — it calls
    /// ToggleButton.OnToggle() directly, which flips IsChecked but never raises the Click routed event at all,
    /// so BrowserIncludeSubfoldersButton_Click (wired to Click) never ran; that produced a false failure that
    /// looked exactly like the reported bug, which is itself a useful cautionary finding about how NOT to
    /// simulate this specific control.
    /// </summary>
    private static void ClickSubfoldersToggleOff(MainWindow window, List<string> trace)
    {
        window.BrowserIncludeSubfoldersButton.Focus();
        window.BrowserIncludeSubfoldersButton.IsChecked = false;
        RaiseClick(window.BrowserIncludeSubfoldersButton);
        trace.Add("clicked Subfolders toggle OFF (IsChecked=false, then Click raised)");
    }

    private static async Task SettleLayoutAsync(MainWindow window)
    {
        window.BrowserFolderTree.UpdateLayout();
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        window.BrowserFolderTree.UpdateLayout();
    }

    private static TreeViewItem? FindContainer(ItemsControl parent, Func<BrowserTreeNode, bool> predicate)
    {
        foreach (var item in parent.Items)
        {
            if (parent.ItemContainerGenerator.ContainerFromItem(item) is not TreeViewItem container) continue;
            if (item is BrowserTreeNode node && predicate(node)) return container;
            var found = FindContainer(container, predicate);
            if (found is not null) return found;
        }
        return null;
    }

    private static void RaiseClick(System.Windows.Controls.Primitives.ButtonBase button) =>
        button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

    private static string Describe(object? item) => item switch
    {
        null => "<null>",
        BrowserTreeNode node => $"BrowserTreeNode(AbsolutePath={node.AbsolutePath}, RelativeFolder={node.RelativeFolder})",
        DependencyObject dependencyObject => dependencyObject.GetType().Name,
        _ => item.ToString() ?? item.GetType().Name
    };

    private static async Task WaitUntilAsync(Func<bool> condition, string waitingFor, int timeoutMs = 20000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException($"Timed out waiting for {waitingFor}.");
            await Task.Delay(25);
        }
    }

    private static void TryDelete(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); } catch { /* best-effort cleanup */ }
    }
}
