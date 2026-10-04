using Lightflow.Actions;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Xunit;
using InputKey = System.Windows.Input.Key;

namespace LightflowStudio.Tests;

public sealed partial class PlayerViewerHostLeaseTests
{
    [Fact]
    public async Task ConfiguredShortcuts_PlayerAndNativeSurfaceUseRemappedCommandsAndLocalOwnership()
    {
        await StaDispatcher.RunAsync(async () => {
            TestWpfApplication.EnsureLoaded(); var backend = new FakeBackend();
            var ranges = new FakeRangeStore(null); var clips = new FakeSubclipService();
            await using var coordinator = new MediaPlaybackCoordinator(() => new MediaPlaybackService(backend));
            var host = new PlayerViewerHost(coordinator, ranges, clips);
            var profile = new ShortcutProfile();
            void Assign(string id, string? key) => profile.Set(KeyboardCommandCatalog.Commands.Single(c => c.Id == id), key is null ? null : new(key), ShortcutPlatform.Windows);
            Assign("player.play-pause", "P"); Assign("player.next-frame", "N"); Assign("player.set-in", "B"); Assign("player.set-out", null);
            host.Shortcuts = new(profile, ShortcutPlatform.Windows);
            var window = CreateSubclipWindow(host); window.ShowActivated = false; window.Left = -32000; window.Show();
            try {
                var asset = ReviewAsset("configured.mp4"); await host.OpenAsync(asset, ReviewPath(asset));
                Assert.False(host.TryHandleShortcut(InputKey.Space, host, ModifierKeys.None));
                Assert.False(host.TryHandleShortcut(InputKey.Right, host, ModifierKeys.None));
                Assert.False(host.TryHandleShortcut(InputKey.O, host, ModifierKeys.None));
                Assert.True(host.TryHandleShortcut(InputKey.P, host, ModifierKeys.None));
                await WaitUntilAsync(() => backend.PlayCallCount == 1, "custom play");
                Assert.True(host.TryHandleShortcut(InputKey.P, host, ModifierKeys.None, true)); Assert.Equal(1, backend.PlayCallCount);
                using var input = new PlayerSurfaceInput(new Border(), () => { }, () => { }, (_, _) => { }, _ => { },
                    (_, _) => false, host.TryHandleShortcutKeyUp, repeatAwareKey: (key, owner, repeat) => host.TryHandleShortcut(key, owner, ModifierKeys.None, repeat));
                Assert.True(input.HandleKeyDown(InputKey.N, host, true));
                await WaitUntilAsync(() => backend.Operations.Contains("forward"), "custom native step");
                Assert.True(host.TryHandleShortcut(InputKey.B, host, ModifierKeys.None));
                await WaitUntilAsync(() => ranges.SaveCount == 1, "custom In");
                foreach (var local in new DependencyObject[] { new TextBox(), new ComboBox { IsEditable = true }, new ComboBox { IsDropDownOpen = true }, new ListBox(), new MenuItem() })
                    Assert.False(host.TryHandleShortcut(InputKey.P, local, ModifierKeys.None));
                Assert.False(host.TryHandleShortcut(InputKey.Left, new Slider(), ModifierKeys.None));
                Assert.False(host.TryHandleShortcut(InputKey.Enter, new Button(), ModifierKeys.None));
                host.IsEnabled = false; Assert.False(host.TryHandleShortcut(InputKey.P, host, ModifierKeys.None)); host.IsEnabled = true;
            }
            finally { await host.CloseAsync(); window.Close(); }
        });
    }
    [Theory]
    [InlineData("release")][InlineData("alt-release")][InlineData("focus")][InlineData("deactivate")][InlineData("replace")][InlineData("modal")]
    public async Task ConfiguredShortcuts_ColorHoldRetainsReleaseAndCancellation(string ending)
    {
        await StaDispatcher.RunAsync(async () => {
            TestWpfApplication.EnsureLoaded(); var backend = new FakeBackend();
            var folder = Directory.CreateTempSubdirectory("lightflow-remapped-color-");
            var colors = new FakeColorStore(); var camera = Guid.NewGuid();
            var cache = new FakeLutLibrary(new Dictionary<Guid,string> { [camera] = WriteIdentityCube(folder.FullName, "camera.cube") });
            await using var coordinator = new MediaPlaybackCoordinator(() => new MediaPlaybackService(backend));
            var host = new PlayerViewerHost(coordinator, lutCache: cache, assetColors: colors, cameraLutFolder: () => folder.FullName, creativeLutFolder: () => folder.FullName);
            var modifiers = ending == "alt-release" ? ModifierKeys.Alt | ModifierKeys.Shift : ModifierKeys.Shift;
            var profile = new ShortcutProfile(); profile.Set(KeyboardCommandCatalog.Commands.Single(c => c.Id == "player.color-bypass"), new("B", ending == "alt-release" ? ShortcutModifiers.Alt | ShortcutModifiers.Shift : ShortcutModifiers.Shift), ShortcutPlatform.Windows);
            host.Shortcuts = new(profile, ShortcutPlatform.Windows);
            var window = CreateSubclipWindow(host); window.ShowActivated = false; window.Left = -32000; window.Show();
            try {
                var asset = ReviewAsset("custom-color.mp4"); await host.OpenAsync(asset, ReviewPath(asset));
                await WaitUntilAsync(() => host.CameraLutCombo.Items.Count >= 2, "LUT"); host.CameraLutCombo.SelectedIndex = 1;
                await WaitUntilAsync(() => colors.SetCount == 1 && !backend.ColorCalls[^1].Bypass, "Color");
                Assert.False(host.TryHandleShortcut(InputKey.C, host, ModifierKeys.None));
                Assert.True(host.TryHandleShortcut(InputKey.B, host, modifiers)); Assert.True(backend.ColorCalls[^1].Bypass);
                Assert.False(host.TryHandleShortcutKeyUp(InputKey.C)); Assert.True(backend.ColorCalls[^1].Bypass);
                switch (ending) {
                    case "release": Assert.True(host.TryHandleShortcutKeyUp(InputKey.B)); break;
                    case "alt-release":
                        var surface = new Border();
                        using (var input = new PlayerSurfaceInput(surface, () => { }, () => { }, (_, _) => { }, _ => { }, (_, _) => false, host.TryHandleShortcutKeyUp)) {
                            var up = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), 0, InputKey.B) { RoutedEvent = Keyboard.PreviewKeyUpEvent };
                            typeof(KeyEventArgs).GetMethod("MarkSystem", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(up, []);
                            Assert.Equal(InputKey.System, up.Key); Assert.Equal(InputKey.B, up.SystemKey);
                            surface.RaiseEvent(up); Assert.True(up.Handled);
                        }
                        break;
                    case "focus": host.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, 0, host, null) { RoutedEvent = UIElement.LostKeyboardFocusEvent }); break;
                    case "deactivate": typeof(PlayerViewerHost).GetMethod("ActionWindowDeactivated", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(host, [window, EventArgs.Empty]); break;
                    case "replace": var next = ReviewAsset("next-color.mp4"); await host.OpenAsync(next, ReviewPath(next)); break;
                    case "modal": window.IsEnabled = false; window.IsEnabled = true; break;
                }
                Assert.False((bool)typeof(PlayerViewerHost).GetField("_momentaryColorBypass", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)!);
                if (ending != "replace") Assert.False(backend.ColorCalls[^1].Bypass);
                var calls = backend.ColorCalls.Count;
                Assert.True(host.TryHandleShortcut(InputKey.B, host, modifiers, true)); Assert.Equal(calls, backend.ColorCalls.Count);
                Assert.Equal(ending is not ("release" or "alt-release"), host.TryHandleShortcut(InputKey.B, host, ModifierKeys.None, true)); Assert.Equal(calls, backend.ColorCalls.Count);
                if (ending is "focus" or "deactivate" or "modal") {
                    // Release outside the window may be missed; a fresh press can begin again.
                    Assert.True(host.TryHandleShortcut(InputKey.B, host, ModifierKeys.Shift));
                    Assert.True(backend.ColorCalls[^1].Bypass);
                }
                host.TryHandleShortcutKeyUp(InputKey.B);
                Assert.Equal(1, colors.SetCount);
            }
            finally { window.IsEnabled = true; await host.CloseAsync(); window.Close(); folder.Delete(true); }
        });
    }
}

public sealed partial class BrowserActionIntegrationTests
{
    private sealed class ShortcutInputSource : PresentationSource
    {
        public override Visual RootVisual { get; set; } = new DrawingVisual();
        protected override CompositionTarget GetCompositionTargetCore() => null!;
        public override bool IsDisposed => false;
    }
    private static object? ShortcutMethod(MainWindow w, string name, params object[] args) => typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(w, args);
    [Fact]
    public Task ConfiguredShortcuts_SettingsCaptureStagingConflictResetAndPersistence() => WithWindow(async (window, storage, directory) => {
        ShortcutMethod(window, "InitializeShortcuts");
        var path = Path.Combine(Path.GetDirectoryName(storage.Locations.SettingsPath)!, "keyboard-shortcuts.json");
        Assert.False(File.Exists(path));
        window.MainTabs.SelectedIndex = ShellDestinationSelection.Index(ShellDestination.Settings);
        window.SettingsCategoryList.SelectedItem = window.SettingsCategoryList.Items.Cast<ListBoxItem>().Single(i => (string)i.Tag == "Shortcuts");
        var button = new Button { Tag = "player.play-pause" };
        void Edit() => ShortcutMethod(window, "ShortcutEdit_Click", button, new RoutedEventArgs());
        bool Capture(InputKey key) => (bool)ShortcutMethod(window, "TryCaptureShortcut", new KeyEventArgs(Keyboard.PrimaryDevice, new ShortcutInputSource(), 0, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent })!;
        Edit(); Assert.True(Capture(InputKey.LeftShift)); Assert.False(window.ShortcutApply.IsEnabled);
        Assert.True(Capture(InputKey.Right)); Assert.Contains("Conflicts", window.ShortcutCaptureText.Text); Assert.False(window.ShortcutApply.IsEnabled);
        Assert.True(Capture(InputKey.Delete)); Assert.Contains("contextual", window.ShortcutCaptureText.Text);
        Assert.True(Capture(InputKey.Escape)); Assert.Equal(Visibility.Collapsed, window.ShortcutCapture.Visibility);
        Assert.Null(Field<InputKey?>(window, "_captureKeyRelease"));
        Edit(); Assert.True(Capture(InputKey.P)); Assert.True(window.ShortcutApply.IsEnabled);
        Assert.False(Capture(InputKey.Tab)); // Candidate confirmation remains keyboard accessible.
        ShortcutMethod(window, "ShortcutApply_Click", window.ShortcutApply, new RoutedEventArgs());
        Assert.False(File.Exists(path));
        Assert.NotNull(Field<KeyboardShortcutResolver>(window, "_shortcutResolver").Resolve(new("Space"), ShortcutContext.Player));
        Assert.True((bool)ShortcutMethod(window, "SaveShortcuts")!);
        Assert.Null(Field<KeyboardShortcutResolver>(window, "_shortcutResolver").Resolve(new("Space"), ShortcutContext.Player));
        Assert.NotNull(KeyboardShortcutStore.Load(path).Profile.Overrides.Single(o => o.CommandId == "player.play-pause"));
        ShortcutMethod(window, "InitializeShortcuts"); // Re-read durable profile, same authority as next startup.
        Assert.NotNull(Field<KeyboardShortcutResolver>(window, "_shortcutResolver").Resolve(new("P"), ShortcutContext.Player));
        window.ShortcutSearch.Text = "Play / Pause"; Assert.Single(window.ShortcutRows.Items);
        ShortcutMethod(window, "ShortcutClear_Click", button, new RoutedEventArgs());
        Assert.True((bool)ShortcutMethod(window, "SaveShortcuts")!);
        Assert.Null(Field<KeyboardShortcutResolver>(window, "_shortcutResolver").Resolve(new("P"), ShortcutContext.Player));
        ShortcutMethod(window, "ShortcutReset_Click", button, new RoutedEventArgs());
        Assert.True((bool)ShortcutMethod(window, "SaveShortcuts")!);
        Assert.NotNull(Field<KeyboardShortcutResolver>(window, "_shortcutResolver").Resolve(new("Space"), ShortcutContext.Player));
        ShortcutMethod(window, "ResetAllShortcuts"); Assert.True((bool)ShortcutMethod(window, "SaveShortcuts")!);
        Assert.Empty(KeyboardShortcutStore.Load(path).Profile.Overrides);
        await Task.CompletedTask; return null;
    });
    [Fact]
    public Task ConfiguredShortcuts_BrowserRatingFlagNavigationAndShellUseSavedOverrides() => WithWindow(async (window, storage, directory) => {
        var (root, ids, collection) = await Seed(storage, directory); await Load(window, storage, directory, root, collection, BrowserScopeKind.Folder);
        var profile = Field<ShortcutProfile>(window, "_shortcutDraft");
        foreach (var (id, key) in new[] { ("asset.rating-5", "R"), ("asset.flag-next", "G"), ("browser.navigate-right", "N"), ("review.toggle-right-panel", "T") })
            profile.Set(KeyboardCommandCatalog.Commands.Single(c => c.Id == id), new(key), ShortcutPlatform.Windows);
        Set(window, "_shortcutsChanged", true); Assert.True((bool)ShortcutMethod(window, "SaveShortcuts")!);
        var grid = Field<BrowserGridModel>(window, "_browserGrid"); grid.SelectSingle(0);
        Assert.False(window.TryHandleConfiguredBrowserShortcut(InputKey.D5, ModifierKeys.None, window.BrowserGridRows));
        Assert.True(window.TryHandleConfiguredBrowserShortcut(InputKey.R, ModifierKeys.None, window.BrowserGridRows));
        await WaitFor(async () => (await storage.AssetClassifications.GetAsync([ids[0]]))[ids[0]].Rating == 5);
        Assert.True(window.TryHandleConfiguredBrowserShortcut(InputKey.G, ModifierKeys.None, window.BrowserGridRows, true));
        await WaitFor(async () => (await storage.AssetClassifications.GetAsync([ids[0]]))[ids[0]].Flag == AssetFlag.Picked);
        Assert.False(window.TryHandleConfiguredBrowserShortcut(InputKey.N, ModifierKeys.None, new TextBox()));
        Assert.False(window.TryHandleConfiguredBrowserShortcut(InputKey.N, ModifierKeys.None, window.BrowserFolderTree));
        Assert.True(window.TryHandleConfiguredBrowserShortcut(InputKey.N, ModifierKeys.None, window.BrowserGridRows, true));
        Assert.True(grid.Tiles[1].IsSelected);
        var panel = Field<bool>(window, "_rightPanelOpen");
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, new ShortcutInputSource(), 0, InputKey.T);
        Assert.True((bool)ShortcutMethod(window, "TryHandleShellShortcut", args)!);
        Assert.NotEqual(panel, Field<bool>(window, "_rightPanelOpen"));
        return null;
    });
    [Theory]
    [InlineData(InputKey.Enter, "Enter")][InlineData(InputKey.PageUp, "PageUp")][InlineData(InputKey.PageDown, "PageDown")]
    public void ConfiguredShortcuts_WindowsAliasesAreCanonical(InputKey key, string logical) => Assert.Equal(logical, WindowsKeyboardShortcuts.Translate(key, ModifierKeys.None)!.Key);
    [Theory]
    [InlineData(InputKey.ImeProcessed)][InlineData(InputKey.DeadCharProcessed)][InlineData(InputKey.NumPad0)][InlineData(InputKey.LeftCtrl)]
    public void ConfiguredShortcuts_CompositionNumpadAndModifiersAreNotGestures(InputKey key) => Assert.Null(WindowsKeyboardShortcuts.Translate(key, ModifierKeys.None));
    [Fact]
    public Task ConfiguredShortcuts_NewDigitBindingsPreserveClosedPickerTypeAhead() => StaDispatcher.RunAsync(() => {
        var player = new Border(); var filmstrip = new Border();
        Assert.True(PlayerKeyboardOwnership.Owns(InputKey.D6, ModifierKeys.None, new ComboBox { IsTextSearchEnabled = true }, player, filmstrip));
        Assert.True(PlayerKeyboardOwnership.Owns(InputKey.D9, ModifierKeys.None, new ListBox { IsTextSearchEnabled = true }, player, filmstrip));
        Assert.False(PlayerKeyboardOwnership.Owns(InputKey.D6, ModifierKeys.None, new ComboBox { IsTextSearchEnabled = false }, player, filmstrip));
        return Task.CompletedTask;
    });
}



