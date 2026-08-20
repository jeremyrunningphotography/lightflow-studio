using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Xunit;

namespace LightflowStudio.Tests;

/// <summary>
/// #124: a variant of BrowserRecursiveIconStartupRestorationLiveInteractionTests where the recursive root is
/// the Media Root's OWN top level (RelativeFolder == "") rather than a subfolder beneath it — a materially
/// different tree-node creation path (SetStorageEntries/AddCurrentRoot's managed-root branch, populated by
/// RefreshBrowserStorageAsync before any navigation even begins) than a subfolder recursive root (created via
/// BrowserTreeModel.EnsurePathChain during the restored navigation itself). Isolates whether the reported bug
/// is specific to that top-level-root node-identity path.
/// </summary>
[Collection("STA dispatcher tests")]
public sealed class BrowserRecursiveIconStartupRestorationTopLevelLiveInteractionTests : IAsyncLifetime
{
    private readonly string _appDataRoot = Path.Combine(Path.GetTempPath(), $"lightflow-restore-top-app-{Guid.NewGuid():N}");
    private readonly string _mediaRoot = Path.Combine(Path.GetTempPath(), $"lightflow-restore-top-media-{Guid.NewGuid():N}");
    private string _folderA = "";
    private string _folderB = "";
    private string _restoredLeaf = "";

    public Task InitializeAsync()
    {
        _folderA = Path.Combine(_mediaRoot, "A");
        _folderB = Path.Combine(_mediaRoot, "B");
        _restoredLeaf = Path.Combine(_mediaRoot, "Washington County Wander Ride");
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
    public async Task StartupRestoredSubtreeUnderATopLevelRecursiveRoot_StaysFilledAcrossSelectionMoves()
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
                // The Media Root's OWN top level is the recursive root this time.
                await storage.BrowserRecursiveRoots.EnableAsync(rootId, "");
                var seedWorkspace = new WorkspaceStateService(storage.Locations.WorkspaceStatePath);
                seedWorkspace.SetBrowserLocation(rootId, "Washington County Wander Ride", _restoredLeaf);
                seedWorkspace.Save();
                await storage.DisposeAsync();
                trace.Add($"seeded: rootId={rootId} recursive root at Media Root top level, saved location='Washington County Wander Ride'");

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

                    var aContainer = FindContainer(window.BrowserFolderTree,
                        node => string.Equals(node.AbsolutePath, _folderA, StringComparison.OrdinalIgnoreCase));
                    Assert.NotNull(aContainer);
                    aContainer!.IsSelected = true;
                    await WaitUntilAsync(() => window.BrowserLoadingOverlay.Visibility != Visibility.Visible &&
                        string.Equals(window.BrowserCurrentPath.Text, _folderA, StringComparison.OrdinalIgnoreCase),
                        "navigation to A to settle");
                    await SettleAsync(window);

                    trace.Add(DumpRecursiveState(window));
                    AssertAllRecursiveAndFilled(window, "after selecting A (Media Root itself is the recursive root)");

                    var bContainer = FindContainer(window.BrowserFolderTree,
                        node => string.Equals(node.AbsolutePath, _folderB, StringComparison.OrdinalIgnoreCase));
                    Assert.NotNull(bContainer);
                    bContainer!.IsSelected = true;
                    await WaitUntilAsync(() => window.BrowserLoadingOverlay.Visibility != Visibility.Visible &&
                        string.Equals(window.BrowserCurrentPath.Text, _folderB, StringComparison.OrdinalIgnoreCase),
                        "navigation to B to settle");
                    await SettleAsync(window);

                    trace.Add(DumpRecursiveState(window));
                    AssertAllRecursiveAndFilled(window, "after selecting B");
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
            node => string.Equals(node.AbsolutePath, _mediaRoot, StringComparison.OrdinalIgnoreCase));
        var aContainer = FindContainer(window.BrowserFolderTree,
            node => string.Equals(node.AbsolutePath, _folderA, StringComparison.OrdinalIgnoreCase));
        var bContainer = FindContainer(window.BrowserFolderTree,
            node => string.Equals(node.AbsolutePath, _folderB, StringComparison.OrdinalIgnoreCase));
        var leafContainer = FindContainer(window.BrowserFolderTree,
            node => string.Equals(node.AbsolutePath, _restoredLeaf, StringComparison.OrdinalIgnoreCase));

        Assert.True(rootContainer is not null, $"Media Root node not materialized ({when})");
        Assert.True(aContainer is not null, $"A node not materialized ({when})");
        Assert.True(bContainer is not null, $"B node not materialized ({when})");
        Assert.True(leafContainer is not null, $"restored leaf node not materialized ({when})");

        foreach (var (name, container) in new[] { ("Library", rootContainer!), ("A", aContainer!), ("B", bContainer!), ("leaf", leafContainer!) })
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
            ("Library", _mediaRoot), ("A", _folderA), ("B", _folderB), ("leaf", _restoredLeaf)
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
