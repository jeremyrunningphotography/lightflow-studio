using Lightflow.Domain;
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
        var row = Field<Dictionary<string, MainWindow.ShortcutRow>>(window, "_shortcutRowModels")["player.play-pause"];
        void Edit() => ShortcutMethod(window, "ShortcutEdit_Click", button, new RoutedEventArgs());
        bool Capture(InputKey key) => (bool)ShortcutMethod(window, "TryCaptureShortcut", new KeyEventArgs(Keyboard.PrimaryDevice, new ShortcutInputSource(), 0, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent })!;
        Edit(); Assert.True(Capture(InputKey.LeftShift)); Assert.False(row.CanApply);
        Assert.True(row.IsCapturing);
        Assert.True(Capture(InputKey.Right)); Assert.Contains("Conflicts", row.CaptureMessage); Assert.False(row.CanApply);
        Assert.Equal("Right", row.Candidate);
        Assert.True(Capture(InputKey.Delete)); Assert.Contains("contextual", row.CaptureMessage);
        Assert.True(Capture(InputKey.Escape)); Assert.False(row.IsCapturing);
        Assert.Null(Field<InputKey?>(window, "_captureKeyRelease"));
        Edit(); Assert.True(Capture(InputKey.P)); Assert.True(row.CanApply);
        Assert.Equal("P", row.Candidate);
        Assert.False(Capture(InputKey.Tab)); // Candidate confirmation remains keyboard accessible.
        ShortcutMethod(window, "ShortcutApply_Click", button, new RoutedEventArgs());
        Assert.Equal("P", row.Current); Assert.True(row.Unsaved); Assert.False(row.IsCapturing);
        Assert.False(File.Exists(path));
        Assert.NotNull(Field<KeyboardShortcutResolver>(window, "_shortcutResolver").Resolve(new("Space"), ShortcutContext.Player));
        Assert.True((bool)ShortcutMethod(window, "SaveShortcuts")!);
        Assert.False(row.Unsaved);
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
    public Task ConfiguredShortcuts_SettingsSectionsRememberExpansionAndSearchBindings() => WithWindow(async (window, storage, directory) => {
        ShortcutMethod(window, "InitializeShortcuts");
        MainWindow.ShortcutSection[] Sections() => window.ShortcutRows.Items.Cast<MainWindow.ShortcutSection>().ToArray();
        var sections = Sections();
        Assert.Equal(new[] { "Browser", "Player", "Presentation", "Workflow" }, sections.Select(s => s.Title));
        Assert.All(sections, s => Assert.False(s.IsExpanded));
        var allRows = sections.SelectMany(s => s.Groups).SelectMany(g => g.Rows).ToArray();
        Assert.Equal(77, allRows.Length);
        Assert.Equal(77, allRows.Select(r => r.Id).Distinct().Count());
        Assert.DoesNotContain(allRows, r => r.Group == "Other");
        Assert.All(allRows.Where(r => r.Assigned && !r.Customized), r => Assert.Equal("", r.Detail));
        sections.Single(s => s.Title == "Player").IsExpanded = true;
        window.ShortcutSearch.Text = "Color Labels";
        Assert.Equal("Browser", Assert.Single(Sections()).Title);
        Assert.True(Sections()[0].IsExpanded);
        window.ShortcutSearch.Text = "Ctrl+Right";
        Assert.Contains(Sections().SelectMany(s => s.Groups).SelectMany(g => g.Rows), r => r.Id == "player.next-media");
        window.ShortcutSearch.Text = "player.next-frame";
        Assert.Single(Sections()[0].Groups[0].Rows);
        window.ShortcutSearch.Text = "";
        Assert.True(Sections().Single(s => s.Title == "Player").IsExpanded);
        Assert.False(Sections().Single(s => s.Title == "Browser").IsExpanded);
        var next = allRows.Single(r => r.Id == "player.next-frame");
        var more = new Button { DataContext = next };
        ShortcutMethod(window, "ShortcutMore_Click", more, new RoutedEventArgs());
        var menu = more.ContextMenu!;
        var items = menu.Items.Cast<MenuItem>().ToArray();
        Assert.All(items, item => Assert.Same(window.FindResource("LightflowMenuItemStyle"), item.Style));
        Assert.True(items[0].IsEnabled); Assert.False(items[1].IsEnabled);
        items[0].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        menu.IsOpen = false;
        next = Sections().SelectMany(s => s.Groups).SelectMany(g => g.Rows).Single(r => r.Id == "player.next-frame");
        Assert.Equal("Unassigned", next.State);
        Assert.Contains("Default: Right", next.Detail);
        window.ShortcutSearch.Text = "Right"; // Default still searchable after unassigning current.
        Assert.Contains(Sections().SelectMany(s => s.Groups).SelectMany(g => g.Rows), r => r.Id == next.Id);
        more.DataContext = next;
        ShortcutMethod(window, "ShortcutMore_Click", more, new RoutedEventArgs());
        items = more.ContextMenu!.Items.Cast<MenuItem>().ToArray();
        Assert.False(items[0].IsEnabled); Assert.True(items[1].IsEnabled);
        items[1].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        more.ContextMenu.IsOpen = false;
        next = Sections().SelectMany(s => s.Groups).SelectMany(g => g.Rows).Single(r => r.Id == "player.next-frame");
        Assert.Equal("Right", next.Current); Assert.Equal("Default", next.State);
        Assert.DoesNotContain("Default:", next.Detail);
        window.ShortcutSearch.Text = "no-such-shortcut";
        Assert.Empty(Sections()); Assert.Equal(Visibility.Visible, window.ShortcutEmpty.Visibility);
        await Task.CompletedTask;
        return null;
    });

    [Fact]
    public Task ConfiguredShortcuts_RowLocalCaptureKeepsLowRowFocusScrollAndStaging() => WithWindow(async (window, storage, directory) => {
        window.ShowInTaskbar = false;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -32000; window.Top = -32000;
        window.Width = 1120; window.Height = 720;
        window.Show();
        Assert.True(await window.StartupCompletion.WaitAsync(TimeSpan.FromSeconds(30)));
        window.Activate();
        window.MainTabs.SelectedIndex = ShellDestinationSelection.Index(ShellDestination.Settings);
        window.SettingsCategoryList.SelectedItem = window.SettingsCategoryList.Items.Cast<ListBoxItem>().Single(i => (string)i.Tag == "Shortcuts");
        window.ShortcutRows.Items.Cast<MainWindow.ShortcutSection>().Single(s => s.Title == "Browser").IsExpanded = true;
        async Task Settle() { await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle); window.UpdateLayout(); }
        await Settle();
        const string id = "browser.thumbnails-next";
        var row = Field<Dictionary<string, MainWindow.ShortcutRow>>(window, "_shortcutRowModels")[id];
        Button Control(string name) => Assert.IsType<Button>(window.FindShortcutControl(id, name));
        var edit = Control("ShortcutEdit");
        edit.BringIntoView(); edit.Focus(); await Settle();
        Assert.True(window.IsActive);
        var initialOffset = window.SettingsShortcutsPage.VerticalOffset;
        Assert.True(initialOffset > 400);
        var source = window.ShortcutRows.ItemsSource;
        void AssertRowFocus(string name) {
            Assert.Same(Control(name), Keyboard.FocusedElement);
            Assert.False(window.ShortcutSearch.IsKeyboardFocused);
            Assert.Same(source, window.ShortcutRows.ItemsSource);
            Assert.True(window.SettingsShortcutsPage.VerticalOffset > initialOffset - 200);
            var container = window.FindShortcutControl(id, "ShortcutRowContainer")!;
            var top = container.TranslatePoint(new Point(), window.SettingsShortcutsPage).Y;
            Assert.True(top >= -1 && top + container.ActualHeight <= window.SettingsShortcutsPage.ViewportHeight + 1, $"Row bounds {top}, {container.ActualHeight}");
        }
        bool Capture(InputKey key) => (bool)ShortcutMethod(window, "TryCaptureShortcut", new KeyEventArgs(Keyboard.PrimaryDevice, new ShortcutInputSource(), 0, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent })!;
        void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Click(edit); await Settle();
        AssertRowFocus("ShortcutCancel"); Assert.True(row.IsCapturing); Assert.Equal("Recording shortcut…", row.CaptureTitle);
        Assert.True(Capture(InputKey.LeftShift)); Assert.Equal("", row.Candidate);
        Assert.True(Capture(InputKey.Right)); await Settle();
        Assert.Contains("Conflicts", row.CaptureMessage); Assert.False(row.CanApply);
        Assert.Contains("Conflicts", Assert.IsType<TextBlock>(window.FindShortcutControl(id, "ShortcutCaptureText")).Text);
        Assert.True(Capture(InputKey.Delete)); await Settle(); Assert.Contains("contextual", row.CaptureMessage);
        AssertRowFocus("ShortcutCancel");
        Assert.True(Capture(InputKey.B)); await Settle();
        AssertRowFocus("ShortcutApply"); Assert.Equal("B", row.Candidate);
        Assert.Equal("B", Assert.IsType<TextBlock>(window.FindShortcutControl(id, "ShortcutCandidate")).Text);
        Assert.False(Capture(InputKey.Tab)); // Candidate confirmation accepts normal keyboard navigation.
        var apply = Control("ShortcutApply");
        apply.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, new ShortcutInputSource(), 0, InputKey.Enter) { RoutedEvent = Keyboard.KeyDownEvent });
        await Settle();
        Assert.False(row.IsCapturing); Assert.Equal("B", row.Current); Assert.True(row.Unsaved);
        AssertRowFocus("ShortcutEdit");
        Assert.Null(Field<KeyboardShortcutResolver>(window, "_shortcutResolver").Resolve(new("B"), ShortcutContext.Browser));
        Assert.True((bool)ShortcutMethod(window, "SaveShortcuts")!); await Settle();
        Assert.False(row.Unsaved);
        Assert.Equal(id, Field<KeyboardShortcutResolver>(window, "_shortcutResolver").Resolve(new("B"), ShortcutContext.Browser)!.Id);
        Click(edit); await Settle(); Assert.True(Capture(InputKey.Escape)); await Settle(); AssertRowFocus("ShortcutEdit");
        Click(edit); await Settle(); Click(Control("ShortcutCancel")); await Settle(); AssertRowFocus("ShortcutEdit");
        Click(edit); await Settle(); Assert.False(Capture(InputKey.Tab)); await Settle(); Assert.False(row.IsCapturing);
        edit.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)); await Settle(); Assert.False(window.ShortcutSearch.IsKeyboardFocused);
        // Menu-origin actions retain the existing row even when current search no longer matches.
        var action = new MenuItem { Tag = id };
        ShortcutMethod(window, "ShortcutClear_Click", action, new RoutedEventArgs()); await Settle();
        AssertRowFocus("ShortcutEdit"); Assert.Equal("Unassigned", row.Current); Assert.True(row.Unsaved);
        ShortcutMethod(window, "ShortcutReset_Click", action, new RoutedEventArgs()); await Settle(); AssertRowFocus("ShortcutEdit");
        Assert.False(row.Customized);
        window.ShortcutSearch.Text = "B"; await Settle();
        Click(Control("ShortcutEdit")); await Settle();
        window.ShortcutSearch.Text = "nothing matches"; await Settle();
        Assert.Contains(window.ShortcutRows.Items.Cast<MainWindow.ShortcutSection>().SelectMany(s => s.Groups).SelectMany(g => g.Rows), r => r.Id == id);
        Assert.True(row.IsCapturing);
        Capture(InputKey.Escape); await Settle();
        Click(Control("ShortcutEdit")); await Settle();
        window.ShortcutRows.Items.Cast<MainWindow.ShortcutSection>().Single(s => s.Title == "Browser").IsExpanded = false;
        await Settle(); Assert.False(row.IsCapturing); Assert.Null(Field<BindableCommand?>(window, "_captureCommand"));
        return null;
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



