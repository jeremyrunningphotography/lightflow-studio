using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Xunit;

namespace LightflowStudio.Tests;

/// <summary>
/// #124: a live-WPF regression seam for disclosure expansion, added after hands-on testing found that
/// expanding some top-level/source folders' chevrons could leave their "Loading…" placeholder stuck forever.
/// Root cause: BrowserFolderTreeItem_Expanded's early-return paths (no Catalog anchor yet for a bare,
/// never-clicked Volume row; a root that no longer resolves to a physical path; an enumeration exception; an
/// unsuccessful listing) simply returned, leaving the placeholder visible with no further feedback. These
/// tests drive the actual, real MainWindow — real Catalog, real filesystem, a real generated TreeView — to
/// prove the fix (CollapseUnmaterializableNode) closes the node back to an honest, re-expandable state instead.
/// </summary>
[Collection("STA dispatcher tests")]
public sealed class BrowserTreeExpansionLiveInteractionTests : IAsyncLifetime
{
    private readonly string _appDataRoot = Path.Combine(Path.GetTempPath(), $"lightflow-expand-app-{Guid.NewGuid():N}");
    private readonly string _mediaRoot = Path.Combine(Path.GetTempPath(), $"lightflow-expand-media-{Guid.NewGuid():N}");

    public Task InitializeAsync()
    {
        Directory.CreateDirectory(Path.Combine(_mediaRoot, "Trips"));
        Directory.CreateDirectory(Path.Combine(_mediaRoot, "Events"));
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        TryDelete(_appDataRoot);
        TryDelete(_mediaRoot);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task ExpandingABareUnanchoredVolumeRow_ClosesBackInsteadOfStayingStuckOnLoading()
    {
        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();

            var startup = await LightflowStorageCoordinator.StartAsync(_appDataRoot);
            Assert.True(startup.IsReady, startup.Diagnostic);
            var storage = startup.Coordinator!;
            // Deliberately not registering "C:\" as a Media Root — a fresh Catalog has zero MediaRoots besides
            // whichever this test creates, so every OTHER detected local volume (see RefreshBrowserStorageAsync)
            // is exactly the "bare, not-yet-anchored Volume row" case: BrowserTreeNode.RootId is null until the
            // row is clicked once.
            var window = NewOffscreenWindow(storage, startup);
            try
            {
                window.Show();
                await WaitUntilAsync(() => window.BrowserFolderTree.Items.Count > 0);

                var volumeContainer = FindContainer(window.BrowserFolderTree,
                    node => node.Storage?.Kind == BrowserStorageKind.Volume && node.RootId is null);
                Assert.NotNull(volumeContainer);
                var volumeNode = (BrowserTreeNode)volumeContainer!.DataContext;

                // Real chevron-expand: setting IsExpanded raises the Expanded routed event exactly like a
                // click on the disclosure arrow.
                volumeContainer.IsExpanded = true;
                await SettleAsync(window);

                // No Catalog anchor exists yet, so BrowserFolderTreeItem_Expanded cannot materialize real
                // children — it must close the node back rather than leaving IsExpanded true with a
                // permanently-stuck "Loading…" placeholder child.
                Assert.False(volumeNode.IsExpanded);
                Assert.False(volumeContainer.IsExpanded);
                var placeholder = Assert.Single(volumeNode.Children);
                Assert.True(placeholder.IsPlaceholder);

                // The row itself was never selected/navigated by any of this.
                Assert.Null(window.BrowserFolderTree.SelectedItem);
                Assert.Equal("", window.BrowserCurrentPath.Text);
            }
            finally
            {
                window.Close();
                await storage.DisposeAsync();
            }
        });
    }

    [Fact]
    public async Task ExpandingAnAnchoredFolderWithRealChildren_MaterializesThemAndNeverGetsStuck()
    {
        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();

            var startup = await LightflowStorageCoordinator.StartAsync(_appDataRoot);
            Assert.True(startup.IsReady, startup.Diagnostic);
            var storage = startup.Coordinator!;
            var created = await storage.MediaRoots.CreateAsync("Library", _mediaRoot);
            Assert.True(created.Succeeded, created.Diagnostic);

            var window = NewOffscreenWindow(storage, startup);
            try
            {
                window.Show();
                await WaitUntilAsync(() => window.BrowserFolderTree.Items.Count > 0);

                window.BrowserCurrentPath.Text = _mediaRoot;
                RaiseClick(window.BrowserGoButton);
                await WaitUntilAsync(() => window.BrowserLoadingOverlay.Visibility != Visibility.Visible &&
                    string.Equals(window.BrowserCurrentPath.Text, _mediaRoot, StringComparison.OrdinalIgnoreCase));
                await SettleAsync(window);

                var libraryContainer = FindContainer(window.BrowserFolderTree,
                    node => string.Equals(node.AbsolutePath, _mediaRoot, StringComparison.OrdinalIgnoreCase));
                Assert.NotNull(libraryContainer);
                var libraryNode = (BrowserTreeNode)libraryContainer!.DataContext;
                var pathBeforeExpand = window.BrowserCurrentPath.Text;
                var selectedBeforeExpand = window.BrowserFolderTree.SelectedItem;

                // Real chevron-expand, repeated rapidly (collapse, then re-expand) to prove no duplication.
                libraryContainer.IsExpanded = true;
                await SettleAsync(window);
                libraryContainer.IsExpanded = false;
                libraryContainer.IsExpanded = true;
                await SettleAsync(window);

                Assert.True(libraryNode.IsExpanded);
                Assert.Equal(2, libraryNode.Children.Count); // "Events" and "Trips" — no duplicates
                Assert.All(libraryNode.Children, child => Assert.False(child.IsPlaceholder));
                Assert.Contains(libraryNode.Children, child => child.DisplayName == "Trips");
                Assert.Contains(libraryNode.Children, child => child.DisplayName == "Events");

                // Expansion never selected/navigated.
                Assert.Same(selectedBeforeExpand, window.BrowserFolderTree.SelectedItem);
                Assert.Equal(pathBeforeExpand, window.BrowserCurrentPath.Text, ignoreCase: true);
            }
            finally
            {
                window.Close();
                await storage.DisposeAsync();
            }
        });
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

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 20000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("Timed out waiting for a condition.");
            await Task.Delay(25);
        }
    }

    private static void TryDelete(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); } catch { /* best-effort cleanup */ }
    }
}
