using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Xunit;
using InputKey = System.Windows.Input.Key;

namespace LightflowStudio.Tests;

public sealed partial class PlayerViewerHostLeaseTests
{
    [Fact]
    public async Task ArrowInput_RapidRoutedRequestsRemainBoundedAndCoalesceAlternatingIntent()
    {
        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var backend = new FakeBackend { BeforeStep = () => { started.TrySetResult(); return release.Task; } };
            await using var coordinator = new MediaPlaybackCoordinator(() => new MediaPlaybackService(backend));
            var host = new PlayerViewerHost(coordinator);
            var window = CreateSubclipWindow(host); window.ShowActivated = false; window.Left = -32000; window.Show();
            try
            {
                var asset = ReviewAsset("rapid.mp4"); await host.OpenAsync(asset, ReviewPath(asset));
                Assert.True(RaisePlayerKey(host, window, InputKey.Right).Handled);
                await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
                var queue = (FrameStepQueue)typeof(PlayerViewerHost).GetField("_frameStepQueue",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(host)!;
                for (var index = 0; index < 100; index++) Assert.True(RaisePlayerKey(host, window, InputKey.Right).Handled);
                Assert.Equal(20, queue.PendingDelta);
                for (var index = 0; index < 5; index++) Assert.True(RaisePlayerKey(host, window, InputKey.Left).Handled);
                Assert.Equal(15, queue.PendingDelta);
                Assert.Empty(backend.Operations);
                release.TrySetResult();
                await queue.WaitUntilIdleAsync().WaitAsync(TimeSpan.FromSeconds(5));
                Assert.Equal(16, backend.Operations.Count(x => x == "forward"));
                Assert.DoesNotContain("backward", backend.Operations);
                Assert.Equal(0, backend.PlayCallCount);
            }
            finally { release.TrySetResult(); await host.CloseAsync(); window.Close(); }
        });
    }

    [Fact]
    public async Task ArrowInput_FolderTreeOriginStepsOnlyWhilePlayerIsActive()
    {
        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();
            var root = Path.GetFullPath(Path.Combine("artifacts", "344", "routed-input-" + Guid.NewGuid().ToString("N")));
            var startup = await LightflowStorageCoordinator.StartAsync(root);
            var storage = startup.Coordinator!;
            storage.SaveSettings(storage.Settings with { BackupCatalogOnClose = false });
            var backend = new FakeBackend();
            await using var coordinator = new MediaPlaybackCoordinator(() => new MediaPlaybackService(backend));
            var markers = new FakeMarkers();
            var ranges = new FakeRangeStore(null); var clips = new FakeSubclipService();
            var host = new PlayerViewerHost(coordinator, ranges, clips, markers: markers);
            var window = new MainWindow(storage, startup.Status, startup.Diagnostic)
                { Left = -32000, Top = -32000, ShowActivated = false, ShowInTaskbar = false };
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            void Mode(BrowserPresentationMode mode) => typeof(MainWindow).GetMethod("SetBrowserPresentationMode", flags)!.Invoke(window, [mode]);
            try
            {
                window.Show();
                await WaitUntilAsync(() => window.BrowserFolderTree.Items.Count > 0, "folder tree");
                typeof(MainWindow).GetField("_playerViewerHost", flags)!.SetValue(window, host);
                window.BrowserPlayerHost.Content = host;
                Mode(BrowserPresentationMode.PlayerViewer);
                var assets = new[] { ReviewAsset("a.mp4"), ReviewAsset("b.mp4") };
                await markers.CreateAsync(assets[0].AssetId!.Value, TimeSpan.Zero);
                await markers.CreateAsync(assets[0].AssetId!.Value, TimeSpan.FromSeconds(10));
                host.SetReviewSet(new(assets.Select(a => new PlayerReviewItem(a, null)).ToArray(), assets[0].AssetId),
                    (a, _) => Task.FromResult(ReviewPath(a)));
                await host.OpenAsync(assets[0], ReviewPath(assets[0]));
                window.UpdateLayout();
                var ancestors = new List<DependencyObject>();
                for (DependencyObject? owner = window.BrowserFolderTree; owner is not null;
                    owner = VisualTreeHelper.GetParent(owner)) ancestors.Add(owner);
                Assert.Same(window.MainTabs, ancestors.First(x => x is System.Windows.Controls.Primitives.Selector));
                Keyboard.Focus(window.BrowserFolderTree);
                Assert.True(window.BrowserFolderTree.IsKeyboardFocusWithin);
                foreach (var key in new[] { InputKey.Right, InputKey.Left, InputKey.Right, InputKey.Left })
                {
                    var direction = key == InputKey.Right ? "forward" : "backward";
                    var count = backend.Operations.Count(x => x == direction);
                    Assert.True(RaisePlayerKey(window.BrowserFolderTree, window, key).Handled);
                    await WaitUntilAsync(() => backend.Operations.Count(x => x == direction) == count + 1, direction);
                    await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                    Assert.Equal(count + 1, backend.Operations.Count(x => x == direction));
                }
                Assert.True(window.BrowserPlayerHost.IsVisible);
                Assert.Equal(0, backend.PlayCallCount);
                var directionalSteps = backend.Operations.Count(x => x is "forward" or "backward");
                Assert.True(RaisePlayerKey(window.BrowserFolderTree, window, InputKey.Right, ModifierKeys.Alt).Handled);
                await WaitUntilAsync(() => backend.SeekPositions.Last() == TimeSpan.FromSeconds(10), "next marker");
                await WaitUntilAsync(() => host.PositionSlider.Value == 10000, "marker presentation");
                Assert.True(RaisePlayerKey(window.BrowserFolderTree, window, InputKey.Left, ModifierKeys.Alt).Handled);
                await WaitUntilAsync(() => backend.SeekPositions.Last() == TimeSpan.Zero, "previous marker");
                Assert.Equal(directionalSteps, backend.Operations.Count(x => x is "forward" or "backward"));
                Assert.True(RaisePlayerKey(window.BrowserFolderTree, window, InputKey.Right, ModifierKeys.Control).Handled);
                await WaitUntilAsync(() => host.CurrentAsset == assets[1] && host.PositionSlider.IsEnabled, "next review asset");
                Assert.True(RaisePlayerKey(window.BrowserFolderTree, window, InputKey.Left, ModifierKeys.Control).Handled);
                await WaitUntilAsync(() => host.CurrentAsset == assets[0] && host.PositionSlider.IsEnabled, "previous review asset");

                Keyboard.Focus(window.BrowserFolderTree);
                await VerifyReviewRangeKeys(host, window, backend, ranges, clips);
                Mode(BrowserPresentationMode.Grid);
                var stepCount = backend.Operations.Count;
                foreach (var key in new[] { InputKey.Left, InputKey.Right })
                    Assert.False(RaisePlayerKey(window.BrowserFolderTree, window, key).Handled);
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Assert.Equal(stepCount, backend.Operations.Count);
                // Exercise the tree's own bubble handler after the unchanged Browser preview route.
                var item = new TreeViewItem { Header = "folder" };
                item.Items.Add(new TreeViewItem { Header = "child" });
                window.BrowserFolderTree.ItemsSource = new[] { item };
                window.UpdateLayout();
                item.IsExpanded = false;
                var right = RaisePlayerKey(item, window, InputKey.Right);
                Assert.False(right.Handled);
                right.RoutedEvent = Keyboard.KeyDownEvent;
                WithKeyboardModifiers(ModifierKeys.None, () => item.RaiseEvent(right));
                Assert.True(item.IsExpanded);
                Assert.True(right.Handled);
                Keyboard.Focus(item);
                Assert.True(item.IsKeyboardFocused);
                var left = RaisePlayerKey(item, window, InputKey.Left);
                left.RoutedEvent = Keyboard.KeyDownEvent;
                WithKeyboardModifiers(ModifierKeys.None, () => item.RaiseEvent(left));
                Assert.False(item.IsExpanded);
            }
            finally
            {
                await host.CloseAsync(); window.Close(); await storage.DisposeAsync();
                Directory.Delete(root, true);
            }
        });
    }

    [Theory]
    [InlineData("host", false)] [InlineData("transport", false)]
    [InlineData("media", false)] [InlineData("filmstrip", false)]
    [InlineData("host", true)] [InlineData("media", true)]
    public async Task ArrowInput_RoutedPressUsesPresentedStepPath(string owner, bool fullscreen)
    {
        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();
            var backend = new FakeBackend(hasAudio: true);
            await using var coordinator = new MediaPlaybackCoordinator(() => new MediaPlaybackService(backend));
            var host = new PlayerViewerHost(coordinator);
            var window = CreateSubclipWindow(host);
            window.ShowActivated = false; window.Left = -32000; window.Opacity = 0; window.Show();
            try
            {
                var asset = ReviewAsset("clip.mp4");
                await host.OpenAsync(asset, ReviewPath(asset));
                if (fullscreen) host.ToggleFullscreen();
                window.UpdateLayout();
                UIElement source = owner switch
                {
                    "transport" => host.PlayPauseButton,
                    "media" => host.MediaSurfaceHost,
                    "filmstrip" => host.Filmstrip,
                    _ => host
                };
                foreach (var key in new[] { InputKey.Right, InputKey.Left, InputKey.Right, InputKey.Left })
                {
                    var direction = key == InputKey.Right ? "forward" : "backward";
                    var count = backend.Operations.Count(x => x == direction);
                    Assert.True(RaisePlayerKey(source, window, key).Handled);
                    await WaitUntilAsync(() => backend.Operations.Count(x => x == direction) == count + 1, direction);
                    await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                    Assert.Equal(count + 1, backend.Operations.Count(x => x == direction));
                }
                Assert.Equal(0, backend.PlayCallCount);
                Assert.Equal(2, backend.Operations.Count(x => x == "forward"));
                Assert.Equal(2, backend.Operations.Count(x => x == "backward"));
                Assert.Equal(Visibility.Visible, host.SteppedFrameSurface.Visibility);
                Assert.NotNull(host.SteppedFrameSurface.Source);
                Assert.True(backend.PauseCallCount >= 4);
                // A playing source also ends paused without any additional Play request.
                Assert.True(RaisePlayerKey(host, window, InputKey.Space).Handled);
                await WaitUntilAsync(() => backend.PlayCallCount == 1, "playing");
                Assert.True(RaisePlayerKey(source, window, InputKey.Right).Handled);
                await WaitUntilAsync(() => host.PlayPauseButton.Content?.ToString() == "Play", "paused after stepping");
                Assert.Equal(1, backend.PlayCallCount);
                if (fullscreen)
                {
                    Assert.True(host.IsFullscreen);
                    Assert.True(RaisePlayerKey(host, window, InputKey.Escape).Handled);
                    Assert.False(host.IsFullscreen);
                }
            }
            finally { await host.CloseAsync(); window.Close(); }
        });
    }

    [Theory]
    [InlineData("text")] [InlineData("editableComboText")] [InlineData("slider")]
    [InlineData("thumb")] [InlineData("list")] [InlineData("combo")]
    [InlineData("tabHeader")] [InlineData("menu")]
    public async Task ArrowInput_TabContentBoundaryPreservesActualLocalControlOwnership(string kind)
    {
        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();
            var backend = new FakeBackend();
            await using var coordinator = new MediaPlaybackCoordinator(() => new MediaPlaybackService(backend));
            var host = new PlayerViewerHost(coordinator);
            FrameworkElement control = kind switch
            {
                "text" => new TextBox { Text = "editing" },
                "editableComboText" => new ComboBox { IsEditable = true, Text = "editing", Style = new Style(typeof(ComboBox)) },
                "slider" => new Slider(),
                "thumb" => new System.Windows.Controls.Primitives.Thumb(),
                "list" => new ListBox { Items = { "one", "two" } },
                "combo" => new ComboBox { Items = { "one", "two" } },
                "menu" => new Menu { Items = { new MenuItem { Header = "one" } } },
                _ => new Border()
            };
            var content = new StackPanel(); content.Children.Add(host); content.Children.Add(control);
            var header = new TextBlock { Text = "tab" };
            var tabs = new TabControl { Items = { new TabItem { Header = header, Content = content } } };
            var window = new Window { Content = tabs, Left = -32000, ShowActivated = false };
            window.PreviewKeyDown += (_, args) => args.Handled = host.TryHandleShortcut(args.Key, args.OriginalSource as DependencyObject);
            window.Show();
            try
            {
                var asset = ReviewAsset("clip.mp4"); await host.OpenAsync(asset, ReviewPath(asset));
                window.UpdateLayout();
                UIElement source = kind == "tabHeader" ? header : control;
                if (kind == "editableComboText")
                    source = (TextBox)((ComboBox)control).Template.FindName("PART_EditableTextBox", control);
                if (kind == "list") source = (ListBoxItem)((ListBox)control).ItemContainerGenerator.ContainerFromIndex(0);
                foreach (var key in new[] { InputKey.Left, InputKey.Right })
                {
                    var inputRemainedLocal = false;
                    KeyEventHandler observer = (_, args) => { inputRemainedLocal = !args.Handled; };
                    source.AddHandler(Keyboard.PreviewKeyDownEvent, observer, true);
                    try { Assert.False(RaisePlayerKey(source, window, key).Handled); }
                    finally { source.RemoveHandler(Keyboard.PreviewKeyDownEvent, observer); }
                    Assert.True(inputRemainedLocal);
                    Assert.False(host.TryHandleShortcut(key, source, ModifierKeys.None));
                }
                Assert.Empty(backend.Operations);
                // Ordinary content underneath this same Selector is permitted.
                Assert.True(host.TryHandleShortcut(InputKey.Right, content, ModifierKeys.None));
                await WaitUntilAsync(() => backend.Operations.Contains("forward"), "content step");
            }
            finally { await host.CloseAsync(); window.Close(); }
        });
    }

    internal static KeyEventArgs RaisePlayerKey(UIElement source, Window window, InputKey key, ModifierKeys modifiers = ModifierKeys.None)
    {
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), 0, key)
            { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        WithKeyboardModifiers(modifiers, () => source.RaiseEvent(args));
        return args;
    }

    // Set only the test STA thread's synchronous input state, restore it before pumping/awaiting.
    // No SendInput, physical key presses, desktop cursor/capture, or global keyboard changes.
    private static void WithKeyboardModifiers(ModifierKeys modifiers, Action action)
    {
        var saved = new byte[256];
        Assert.True(GetKeyboardState(saved));
        var state = (byte[])saved.Clone();
        foreach (var key in new[] { 0x10, 0x11, 0x12, 0x5B, 0x5C, 0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5 }) state[key] = 0;
        if (modifiers.HasFlag(ModifierKeys.Control)) state[0x11] = state[0xA2] = 0x80;
        if (modifiers.HasFlag(ModifierKeys.Alt)) state[0x12] = state[0xA4] = 0x80;
        Assert.True(SetKeyboardState(state));
        try { action(); }
        finally { Assert.True(SetKeyboardState(saved)); }
    }

    [DllImport("user32.dll")] private static extern bool GetKeyboardState(byte[] state);
    [DllImport("user32.dll")] private static extern bool SetKeyboardState(byte[] state);
}
