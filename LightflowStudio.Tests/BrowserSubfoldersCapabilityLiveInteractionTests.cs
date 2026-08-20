using System.Windows;
using System.Windows.Threading;
using Xunit;

namespace LightflowStudio.Tests;

/// <summary>
/// #124: a live-WPF regression seam for the "Include Subfolders only makes sense where the selected folder has
/// at least one child folder" requirement — driving the actual, real MainWindow through real navigation to
/// prove BrowserIncludeSubfoldersButton.IsEnabled/ToolTip end up correct for every combination of effective
/// recursive mode and immediate-child-folder existence, including the case that matters most: an inherited
/// recursive LEAF (zero children of its own) must stay enabled, since turning it OFF is how its governing
/// ancestor root gets removed.
/// </summary>
[Collection("STA dispatcher tests")]
public sealed class BrowserSubfoldersCapabilityLiveInteractionTests : IAsyncLifetime
{
    private readonly string _appDataRoot = Path.Combine(Path.GetTempPath(), $"lightflow-caps-app-{Guid.NewGuid():N}");
    private readonly string _mediaRoot = Path.Combine(Path.GetTempPath(), $"lightflow-caps-media-{Guid.NewGuid():N}");
    private string _leaf = "";
    private string _folderWithChildren = "";
    private string _recursiveRootFolder = "";
    private string _inheritedRecursiveLeaf = "";

    public Task InitializeAsync()
    {
        // Direct-mode leaf (no children of its own).
        _leaf = Path.Combine(_mediaRoot, "DirectLeaf");
        Directory.CreateDirectory(_leaf);

        // Direct-mode folder with a child.
        _folderWithChildren = Path.Combine(_mediaRoot, "DirectWithChildren");
        Directory.CreateDirectory(Path.Combine(_folderWithChildren, "Child"));

        // A folder that will become the recursive root, with a descendant leaf that has no children of its own.
        _recursiveRootFolder = Path.Combine(_mediaRoot, "RecursiveRoot");
        _inheritedRecursiveLeaf = Path.Combine(_recursiveRootFolder, "August", "Leaf");
        Directory.CreateDirectory(_inheritedRecursiveLeaf);

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        TryDelete(_appDataRoot);
        TryDelete(_mediaRoot);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task DirectLeafWithNoChildren_DisablesTheToggleWithANoSubfoldersTooltip()
    {
        await RunAsync(async (window, storage) =>
        {
            await NavigateAsync(window, _leaf);

            Assert.False(window.BrowserIncludeSubfoldersButton.IsEnabled);
            Assert.Equal("No subfolders", window.BrowserIncludeSubfoldersButton.ToolTip);
        });
    }

    [Fact]
    public async Task DirectFolderWithChildren_EnablesTheToggle()
    {
        await RunAsync(async (window, storage) =>
        {
            await NavigateAsync(window, _folderWithChildren);

            Assert.True(window.BrowserIncludeSubfoldersButton.IsEnabled);
            Assert.NotEqual("No subfolders", window.BrowserIncludeSubfoldersButton.ToolTip);
        });
    }

    [Fact]
    public async Task RecursiveRoot_EnablesTheToggleRegardlessOfItsOwnChildCount()
    {
        await RunAsync(async (window, storage) =>
        {
            await NavigateAsync(window, _recursiveRootFolder);
            window.BrowserIncludeSubfoldersButton.Focus();
            window.BrowserIncludeSubfoldersButton.IsChecked = true;
            RaiseClick(window.BrowserIncludeSubfoldersButton);
            await WaitUntilAsync(() => window.BrowserIncludeSubfoldersButton.IsChecked == true &&
                window.BrowserLoadingOverlay.Visibility != Visibility.Visible);

            Assert.True(window.BrowserIncludeSubfoldersButton.IsEnabled);
        });
    }

    [Fact]
    public async Task InheritedRecursiveLeafWithNoChildrenOfItsOwn_StaysEnabledSoItCanStillBeTurnedOff()
    {
        // The critical case: disabling Include Subfolders from an inherited descendant is how its governing
        // ancestor root gets removed (see BrowserToggleOffLiveInteractionTests) — that must remain possible
        // even when the descendant itself happens to have zero child folders of its own.
        await RunAsync(async (window, storage) =>
        {
            var roots = await storage.MediaRoots.ListAsync();
            var rootId = roots.Single(root => root.DisplayName == "Library").RootId;
            var relativeToRoot = Path.GetRelativePath(_mediaRoot, _recursiveRootFolder).Replace('\\', '/');
            await storage.BrowserRecursiveRoots.EnableAsync(rootId, relativeToRoot);

            await NavigateAsync(window, _inheritedRecursiveLeaf);

            Assert.True(window.BrowserIncludeSubfoldersButton.IsChecked); // inherited ON
            Assert.True(window.BrowserIncludeSubfoldersButton.IsEnabled); // must still be clickable to turn OFF
        });
    }

    [Fact]
    public async Task ChangingSelectedFolder_UpdatesTheEnabledStateForTheNewlySelectedFolder()
    {
        await RunAsync(async (window, storage) =>
        {
            await NavigateAsync(window, _folderWithChildren);
            Assert.True(window.BrowserIncludeSubfoldersButton.IsEnabled);

            await NavigateAsync(window, _leaf);

            Assert.False(window.BrowserIncludeSubfoldersButton.IsEnabled);
        });
    }

    [Fact]
    public async Task DisabledLeafState_ClickIsUnroutedAndNeverTouchesBrowserLocationOrQuery()
    {
        await RunAsync(async (window, storage) =>
        {
            await NavigateAsync(window, _leaf);
            Assert.False(window.BrowserIncludeSubfoldersButton.IsEnabled);
            var pathBefore = window.BrowserCurrentPath.Text;
            var searchBefore = window.BrowserSearchBox.Text;

            // WPF never routes Click for a disabled control via real input; raising the event directly is
            // still meaningful here since the handler itself starts with `if (_synchronizingBrowserScopeMode)
            // return;` and nothing else — IsEnabled=False is what actually prevents a real click from ever
            // reaching it. This confirms state stays untouched either way.
            RaiseClick(window.BrowserIncludeSubfoldersButton);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

            Assert.Equal(pathBefore, window.BrowserCurrentPath.Text);
            Assert.Equal(searchBefore, window.BrowserSearchBox.Text);
        });
    }

    private async Task NavigateAsync(MainWindow window, string folder)
    {
        window.BrowserCurrentPath.Text = folder;
        RaiseClick(window.BrowserGoButton);
        await WaitUntilAsync(() => window.BrowserLoadingOverlay.Visibility != Visibility.Visible &&
            string.Equals(window.BrowserCurrentPath.Text, folder, StringComparison.OrdinalIgnoreCase));
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
    }

    private async Task RunAsync(Func<MainWindow, LightflowStorageCoordinator, Task> body)
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
                await body(window, storage);
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
