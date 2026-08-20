using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Xunit;

namespace LightflowStudio.Tests;

/// <summary>
/// #124: a live-WPF reproduction of the reported "recursive icon state breaks specifically for the recursive
/// tree containing the startup-restored folder" bug. Drives the actual, real startup path — a real workspace
/// state file seeded exactly as Window_Closing would leave it, then a fresh MainWindow constructed exactly as
/// the real app launches, letting its own Loaded handler drive RestoreBrowserLocationAsync — rather than any
/// simulated/manual navigation, so a defect specific to the restoration code path (as opposed to ordinary
/// interactive navigation) would surface here even if BrowserRecursiveRootTests/BrowserNavigationTests stay
/// green.
/// </summary>
[Collection("STA dispatcher tests")]
public sealed class BrowserRecursiveIconStartupRestorationLiveInteractionTests : IAsyncLifetime
{
    private readonly string _appDataRoot = Path.Combine(Path.GetTempPath(), $"lightflow-restore-icon-app-{Guid.NewGuid():N}");
    private readonly string _mediaRoot = Path.Combine(Path.GetTempPath(), $"lightflow-restore-icon-media-{Guid.NewGuid():N}");
    private string _recursiveRootFolder = "";
    private string _folderA = "";
    private string _folderB = "";
    private string _restoredLeaf = "";

    public Task InitializeAsync()
    {
        _recursiveRootFolder = Path.Combine(_mediaRoot, "RecursiveRoot");
        _folderA = Path.Combine(_recursiveRootFolder, "A");
        _folderB = Path.Combine(_recursiveRootFolder, "B");
        _restoredLeaf = Path.Combine(_recursiveRootFolder, "Washington County Wander Ride");
        Directory.CreateDirectory(_folderA);
        Directory.CreateDirectory(_folderB);
        Directory.CreateDirectory(_restoredLeaf);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        TryDelete(_appDataRoot);
        TryDelete(_mediaRoot);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task StartupRestoredRecursiveSubtree_StaysFilledAcrossSelectionMovesWithinTheSameSubtree()
    {
        var trace = new List<string>();
        try
        {
            await StaDispatcher.RunAsync(async () =>
            {
                TestWpfApplication.EnsureLoaded();

                // --- Simulate the PREVIOUS session: establish the recursive root and persist the restored
                // location, exactly as a real Window_Closing would leave workspace-state.json. ---
                var startup = await LightflowStorageCoordinator.StartAsync(_appDataRoot);
                Assert.True(startup.IsReady, startup.Diagnostic);
                var storage = startup.Coordinator!;
                var created = await storage.MediaRoots.CreateAsync("Library", _mediaRoot);
                Assert.True(created.Succeeded, created.Diagnostic);
                var rootId = created.Root!.RootId;
                await storage.BrowserRecursiveRoots.EnableAsync(rootId, "RecursiveRoot");
                var seedWorkspace = new WorkspaceStateService(storage.Locations.WorkspaceStatePath);
                seedWorkspace.SetBrowserLocation(rootId, "RecursiveRoot/Washington County Wander Ride", _restoredLeaf);
                seedWorkspace.Save();
                await storage.DisposeAsync();
                trace.Add($"seeded: rootId={rootId} recursive root 'RecursiveRoot', saved location='RecursiveRoot/Washington County Wander Ride'");

                // --- "Relaunch": fresh storage coordinator over the same on-disk Catalog/workspace state,
                // fresh MainWindow, exactly like real app startup. ---
                var relaunch = await LightflowStorageCoordinator.StartAsync(_appDataRoot);
                Assert.True(relaunch.IsReady, relaunch.Diagnostic);
                var relaunchedStorage = relaunch.Coordinator!;

                var window = NewOffscreenWindow(relaunchedStorage, relaunch);
                try
                {
                    window.Show();
                    await WaitUntilAsync(() => window.BrowserLoadingOverlay.Visibility != Visibility.Visible &&
                        string.Equals(window.BrowserCurrentPath.Text, _restoredLeaf, StringComparison.OrdinalIgnoreCase),
                        "startup restoration to settle on the saved leaf");
                    await SettleAsync(window);

                    trace.Add(DumpRecursiveState(window));
                    AssertAllRecursiveAndFilled(window, "immediately after startup restoration");

                    // --- Select another folder WITHIN the same recursive subtree. ---
                    var aContainer = FindContainer(window.BrowserFolderTree,
                        node => string.Equals(node.AbsolutePath, _folderA, StringComparison.OrdinalIgnoreCase));
                    Assert.NotNull(aContainer);
                    aContainer!.IsSelected = true;
                    await WaitUntilAsync(() => window.BrowserLoadingOverlay.Visibility != Visibility.Visible &&
                        string.Equals(window.BrowserCurrentPath.Text, _folderA, StringComparison.OrdinalIgnoreCase),
                        "navigation to A to settle");
                    await SettleAsync(window);

                    trace.Add(DumpRecursiveState(window));
                    AssertAllRecursiveAndFilled(window, "after selecting A within the same subtree");

                    // --- Select unrelated D outside the subtree entirely, then back to B inside it. ---
                    var folderD = Path.Combine(_mediaRoot, "Unrelated");
                    Directory.CreateDirectory(folderD);
                    window.BrowserCurrentPath.Text = folderD;
                    RaiseClick(window.BrowserGoButton);
                    await WaitUntilAsync(() => window.BrowserLoadingOverlay.Visibility != Visibility.Visible &&
                        string.Equals(window.BrowserCurrentPath.Text, folderD, StringComparison.OrdinalIgnoreCase),
                        "navigation to the unrelated folder to settle");
                    await SettleAsync(window);

                    trace.Add(DumpRecursiveState(window));
                    AssertAllRecursiveAndFilled(window, "after selecting an unrelated folder outside the subtree");

                    var bContainer = FindContainer(window.BrowserFolderTree,
                        node => string.Equals(node.AbsolutePath, _folderB, StringComparison.OrdinalIgnoreCase));
                    Assert.NotNull(bContainer);
                    bContainer!.IsSelected = true;
                    await WaitUntilAsync(() => window.BrowserLoadingOverlay.Visibility != Visibility.Visible &&
                        string.Equals(window.BrowserCurrentPath.Text, _folderB, StringComparison.OrdinalIgnoreCase),
                        "navigation to B to settle");
                    await SettleAsync(window);

                    trace.Add(DumpRecursiveState(window));
                    AssertAllRecursiveAndFilled(window, "after returning to B within the same subtree");

                    // --- OFF -> ON on the affected tree from B, then move selection again. ---
                    window.BrowserIncludeSubfoldersButton.Focus();
                    window.BrowserIncludeSubfoldersButton.IsChecked = false;
                    RaiseClick(window.BrowserIncludeSubfoldersButton);
                    await WaitUntilAsync(() => window.BrowserIncludeSubfoldersButton.IsChecked == false &&
                        window.BrowserLoadingOverlay.Visibility != Visibility.Visible, "toggle OFF to settle");
                    await SettleAsync(window);
                    var rootsAfterOff = await relaunchedStorage.BrowserRecursiveRoots.ListAsync();
                    trace.Add($"after toggle OFF from B: recursiveRoots remaining={rootsAfterOff.Count}");
                    Assert.Empty(rootsAfterOff);

                    window.BrowserIncludeSubfoldersButton.Focus();
                    window.BrowserIncludeSubfoldersButton.IsChecked = true;
                    RaiseClick(window.BrowserIncludeSubfoldersButton);
                    await WaitUntilAsync(() => window.BrowserIncludeSubfoldersButton.IsChecked == true &&
                        window.BrowserLoadingOverlay.Visibility != Visibility.Visible, "toggle ON to settle");
                    await SettleAsync(window);
                    var rootsAfterOn = await relaunchedStorage.BrowserRecursiveRoots.ListAsync();
                    trace.Add($"after toggle ON from B: recursiveRoots={string.Join(", ", rootsAfterOn.Select(r => r.RelativeFolder))}");
                    Assert.Single(rootsAfterOn);
                    Assert.Equal("RecursiveRoot/B", rootsAfterOn[0].RelativeFolder);

                    var cContainerAfterReToggle = FindContainer(window.BrowserFolderTree,
                        node => string.Equals(node.AbsolutePath, _restoredLeaf, StringComparison.OrdinalIgnoreCase));
                    Assert.NotNull(cContainerAfterReToggle);
                    cContainerAfterReToggle!.IsSelected = true;
                    await WaitUntilAsync(() => window.BrowserLoadingOverlay.Visibility != Visibility.Visible &&
                        string.Equals(window.BrowserCurrentPath.Text, _restoredLeaf, StringComparison.OrdinalIgnoreCase),
                        "navigation back to the restored leaf to settle");
                    await SettleAsync(window);

                    trace.Add(DumpRecursiveState(window));
                    // Only B (and its descendants) governs recursion now — the restored leaf and A are direct.
                    var bNode = (BrowserTreeNode)bContainer.DataContext;
                    var leafNode = (BrowserTreeNode)cContainerAfterReToggle.DataContext;
                    var aNode = (BrowserTreeNode)aContainer.DataContext;
                    Assert.True(bNode.IsRecursiveScope, "B (the new governing root) must be recursive");
                    Assert.False(leafNode.IsRecursiveScope, "the restored leaf is no longer covered after OFF->ON moved the root to B");
                    Assert.False(aNode.IsRecursiveScope, "A is no longer covered after OFF->ON moved the root to B");
                }
                finally
                {
                    window.Close();
                    await relaunchedStorage.DisposeAsync();
                }
            });
        }
        finally
        {
            foreach (var line in trace) Console.WriteLine(line);
        }
    }

    private void AssertAllRecursiveAndFilled(MainWindow window, string when)
    {
        var rootContainer = FindContainer(window.BrowserFolderTree,
            node => string.Equals(node.AbsolutePath, _recursiveRootFolder, StringComparison.OrdinalIgnoreCase));
        var aContainer = FindContainer(window.BrowserFolderTree,
            node => string.Equals(node.AbsolutePath, _folderA, StringComparison.OrdinalIgnoreCase));
        var bContainer = FindContainer(window.BrowserFolderTree,
            node => string.Equals(node.AbsolutePath, _folderB, StringComparison.OrdinalIgnoreCase));
        var leafContainer = FindContainer(window.BrowserFolderTree,
            node => string.Equals(node.AbsolutePath, _restoredLeaf, StringComparison.OrdinalIgnoreCase));

        Assert.True(rootContainer is not null, $"RecursiveRoot node not materialized ({when})");
        Assert.True(aContainer is not null, $"A node not materialized ({when})");
        Assert.True(bContainer is not null, $"B node not materialized ({when})");
        Assert.True(leafContainer is not null, $"restored leaf node not materialized ({when})");

        foreach (var (name, container) in new[] { ("RecursiveRoot", rootContainer!), ("A", aContainer!), ("B", bContainer!), ("leaf", leafContainer!) })
        {
            var node = (BrowserTreeNode)container.DataContext;
            Assert.True(node.IsRecursiveScope, $"{name}.IsRecursiveScope must be true ({when})");
            Assert.True(node.IsFilledFolderIcon, $"{name}.IsFilledFolderIcon must be true ({when})");
        }
    }

    private string DumpRecursiveState(MainWindow window)
    {
        var nodes = new (string Name, string Path)[]
        {
            ("RecursiveRoot", _recursiveRootFolder), ("A", _folderA), ("B", _folderB), ("leaf", _restoredLeaf)
        };
        var parts = nodes.Select(entry =>
        {
            var container = FindContainer(window.BrowserFolderTree,
                node => string.Equals(node.AbsolutePath, entry.Path, StringComparison.OrdinalIgnoreCase));
            if (container is null) return $"{entry.Name}=<not materialized>";
            var node = (BrowserTreeNode)container.DataContext;
            return $"{entry.Name}(Root={node.RootId}, Rel={node.RelativeFolder}, Sel={node.IsSelected}, Rec={node.IsRecursiveScope}, Filled={node.IsFilledFolderIcon})";
        });
        return $"state: {string.Join(" | ", parts)}; BrowserCurrentPath='{window.BrowserCurrentPath.Text}'";
    }

    private static MainWindow NewOffscreenWindow(LightflowStorageCoordinator storage, StorageStartupResult startup) =>
        new(storage, startup.Status, startup.Diagnostic)
        {
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -32000,
            Top = -32000,
            ShowInTaskbar = false
        };

    private static async Task SettleAsync(MainWindow window)
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
