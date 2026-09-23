using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Xunit;

namespace LightflowStudio.Tests;

// #281: exercise the actual generated Location tree without OS input or navigation workarounds.
[Collection("STA dispatcher tests")]
public sealed class BrowserTreeExpansionLiveInteractionTests : IAsyncLifetime
{
    private readonly string _appDataRoot = Path.Combine(Path.GetTempPath(), $"lightflow-expand-app-{Guid.NewGuid():N}");
    private readonly string _mediaRoot = Path.Combine(Path.GetTempPath(), $"lightflow-expand-media-{Guid.NewGuid():N}");

    public Task InitializeAsync()
    {
        Directory.CreateDirectory(Path.Combine(_mediaRoot, "Trips", "DayOne"));
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
    public async Task FirstCaretOnNeverSelectedVolume_ResolvesAnchorAndLoadsWithoutNavigation()
    {
        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();

            var startup = await LightflowStorageCoordinator.StartAsync(_appDataRoot);
            Assert.True(startup.IsReady, startup.Diagnostic);
            var storage = startup.Coordinator!;
            var window = NewOffscreenWindow(storage, startup);
            try
            {
                window.Show();
                await window.PresentationReady.WaitAsync(TimeSpan.FromSeconds(30));
                // An unanchored storage row backed by our known-readable fixture avoids depending on
                // host drive ordering, permissions or readiness (which differ on hosted CI).
                var fixture = new BrowserTreeModel();
                fixture.SetStorageEntries([new("fixture-volume", "Fixture volume", _mediaRoot,
                    BrowserStorageKind.Volume, MediaRootAvailability.Online)]);
                var roots = Assert.IsType<System.Collections.ObjectModel.ObservableCollection<BrowserTreeNode>>(
                    window.BrowserFolderTree.ItemsSource);
                roots.Clear();
                roots.Add(Assert.Single(fixture.Roots));
                await SettleAsync(window);

                var volumeContainer = FindContainer(window.BrowserFolderTree,
                    node => node.Storage?.Kind == BrowserStorageKind.Volume && node.RootId is null);
                Assert.NotNull(volumeContainer);
                var volumeNode = (BrowserTreeNode)volumeContainer!.DataContext;

                ToggleCaret(volumeContainer);
                await WaitUntilAsync(() => volumeNode.IsMaterialized);
                await SettleAsync(window);
                Assert.True(volumeNode.IsExpanded);
                Assert.True(volumeContainer.IsExpanded);
                Assert.NotNull(volumeNode.RootId);
                Assert.All(volumeNode.Children, child => Assert.False(child.IsPlaceholder));
                var children = volumeNode.Children.ToArray();
                ToggleCaret(volumeContainer);
                ToggleCaret(volumeContainer);
                await SettleAsync(window);
                Assert.Equal(children, volumeNode.Children);
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
    public async Task NeverSelectedManagedRoot_CaretThenRowThenKeyboard_PreservesChildrenAndDropState()
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
                await window.PresentationReady.WaitAsync(TimeSpan.FromSeconds(30));

                var libraryContainer = FindContainer(window.BrowserFolderTree,
                    node => string.Equals(node.AbsolutePath, _mediaRoot, StringComparison.OrdinalIgnoreCase));
                Assert.NotNull(libraryContainer);
                var libraryNode = (BrowserTreeNode)libraryContainer!.DataContext;
                var pathBeforeExpand = window.BrowserCurrentPath.Text;
                var selectedBeforeExpand = window.BrowserFolderTree.SelectedItem;

                Assert.False(libraryNode.IsSelected);
                Assert.False(libraryNode.IsMaterialized);
                libraryNode.IsFileDropTarget = true;
                ToggleCaret(libraryContainer);
                ToggleCaret(libraryContainer);
                ToggleCaret(libraryContainer);
                await WaitUntilAsync(() => libraryNode.IsMaterialized);
                await SettleAsync(window);
                Assert.True(TreeDropPresentation.GetIsValid(libraryContainer));
                Assert.Null(BrowserFolderDragGesture.HeaderNode(
                    (DependencyObject)libraryContainer.Template.FindName("Expander", libraryContainer)));
                Assert.True(libraryNode.IsExpanded);
                Assert.Equal(2, libraryNode.Children.Count); // "Events" and "Trips" — no duplicates
                Assert.All(libraryNode.Children, child => Assert.False(child.IsPlaceholder));
                Assert.Contains(libraryNode.Children, child => child.DisplayName == "Trips");
                Assert.Contains(libraryNode.Children, child => child.DisplayName == "Events");

                // Expansion never selected/navigated.
                Assert.Same(selectedBeforeExpand, window.BrowserFolderTree.SelectedItem);
                Assert.Equal(pathBeforeExpand, window.BrowserCurrentPath.Text, ignoreCase: true);

                // A never-selected descendant also loads from disclosure intent alone.
                var trips = libraryNode.Children.Single(child => child.DisplayName == "Trips");
                var tripsContainer = FindContainer(libraryContainer, node => ReferenceEquals(node, trips));
                Assert.NotNull(tripsContainer);
                ToggleCaret(tripsContainer!);
                await WaitUntilAsync(() => trips.IsMaterialized);
                Assert.Equal("DayOne", Assert.Single(trips.Children).DisplayName);
                Assert.False(trips.IsSelected);
                Assert.Same(selectedBeforeExpand, window.BrowserFolderTree.SelectedItem);
                Assert.Equal(pathBeforeExpand, window.BrowserCurrentPath.Text);
                // Normal row input still selects/navigates through the existing event handlers.
                var header = (UIElement)libraryContainer.Template.FindName("HeaderChrome", libraryContainer);
                header.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                    { RoutedEvent = Mouse.PreviewMouseDownEvent });
                header.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                    { RoutedEvent = Mouse.MouseDownEvent });
                await WaitUntilAsync(() => !window.BrowserNavigationPending &&
                    string.Equals(window.BrowserCurrentPath.Text, _mediaRoot, StringComparison.OrdinalIgnoreCase));
                await SettleAsync(window);
                Assert.Same(libraryNode, window.BrowserFolderTree.SelectedItem);
                var children = libraryNode.Children.ToArray();
                libraryContainer.IsExpanded = false;
                libraryContainer.Focus();
                var key = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), 0, Key.Right)
                    { RoutedEvent = Keyboard.KeyDownEvent };
                libraryContainer.RaiseEvent(key);
                await SettleAsync(window);
                Assert.True(libraryNode.IsExpanded);
                Assert.Equal(children, libraryNode.Children);
                Assert.True(libraryNode.IsFileDropTarget);
                libraryNode.IsFileDropTarget = false;
                Assert.True(libraryNode.IsSelected);

                // Persisted expansion uses the same RootId/relative-folder identity established by expansion.
                var restored = new BrowserTreeModel();
                await WorkspaceTreeRestoration.RestoreAsync(restored, storage.MediaRoots, storage.MediaFolders,
                    [new() { RootId = libraryNode.RootId!.Value, RelativeFolder = libraryNode.RelativeFolder! }],
                    CancellationToken.None, action => action());
                var restoredRoot = restored.FindByPath(_mediaRoot);
                Assert.NotNull(restoredRoot);
                Assert.True(restoredRoot.IsExpanded);
                Assert.Equal(2, restoredRoot.Children.Count);
                Assert.Null(restored.SelectedNode);
            }
            finally
            {
                window.Close();
                await storage.DisposeAsync();
            }
        });
    }

    private static void ToggleCaret(TreeViewItem item)
    {
        item.ApplyTemplate();
        var caret = (ToggleButton)item.Template.FindName("Expander", item);
        caret.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
            { RoutedEvent = Mouse.PreviewMouseDownEvent });
        // Invoke WPF's real toggle/binding/event path, without sending desktop input.
        var peer = new ToggleButtonAutomationPeer(caret);
        ((IToggleProvider)peer.GetPattern(PatternInterface.Toggle)).Toggle();
    }
    private static MainWindow NewOffscreenWindow(LightflowStorageCoordinator storage, StorageStartupResult startup)
    {
        storage.SaveSettings(storage.Settings with { BackupCatalogOnClose = false });
        return new(storage, startup.Status, startup.Diagnostic)
        {
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -32000,
            Top = -32000,
            ShowInTaskbar = false
        };
    }

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
