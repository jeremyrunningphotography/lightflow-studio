using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Xunit;

namespace LightflowStudio.Tests;

[Collection("STA dispatcher tests")]
public sealed class BrowserSelectAllRoutingTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    [DllImport("user32.dll")] private static extern bool GetKeyboardState(byte[] state);
    [DllImport("user32.dll")] private static extern bool SetKeyboardState(byte[] state);
    private static object? Call(object owner, string name, params object[] args) => owner.GetType().GetMethod(name, Hidden)!.Invoke(owner, args);
    private static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, Hidden)!.GetValue(owner)!;

    [Theory]
    [InlineData("Grid")]
    [InlineData("Details")]
    [InlineData("Folder tree")]
    [InlineData("Collections tree")]
    [InlineData("Toolbar")]
    [InlineData("Neutral startup")]
    public Task BrowserContextsSelectVisibleMixedMediaWithoutMovingFocus(string context) => RunAsync(async (window, model) =>
    {
        if (context == "Details") window.ApplyBrowserLayout(BrowserLayoutMode.Details, true);
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        var source = context switch
        {
            "Folder tree" => window.BrowserFolderTree,
            "Collections tree" => window.BrowserCollectionTree,
            "Toolbar" => window.BrowserGoButton,
            "Neutral startup" => (UIElement)window,
            _ => window.BrowserGridRows
        };
        Focus(window, source);
        var focus = Keyboard.FocusedElement;
        model.SelectSingle(0);
        var anchor = model.SelectionAnchorAssetId;
        Assert.True(Press(window, source).Handled);
        Assert.Equal(3, model.SelectedKeys.Count);
        Assert.Contains(model.SelectedTilesInBrowserOrder, tile => tile.Category == MediaTypeCategory.StillImage);
        Assert.Contains(model.SelectedTilesInBrowserOrder, tile => tile.Category == MediaTypeCategory.Video);
        Assert.Equal(anchor, model.SelectionAnchorAssetId);
        Assert.Same(focus, Keyboard.FocusedElement);
        var selected = model.SelectedKeys.ToHashSet();
        Assert.True(Press(window, source).Handled);
        Assert.True(selected.SetEquals(model.SelectedKeys));

        model.SetQuery(BrowserQuery.Default with { SearchText = "visible" });
        Assert.True(Press(window, source).Handled);
        Assert.Equal(2, model.SelectedKeys.Count);
        Assert.All(model.SelectedTilesInBrowserOrder, tile => Assert.Contains("visible", tile.Name));
        Assert.Same(focus, Keyboard.FocusedElement);
    });

    [Theory]
    [InlineData("Search")]
    [InlineData("Path")]
    [InlineData("Rename")]
    [InlineData("Rich text")]
    [InlineData("Editable combo")]
    public Task NativeTextEditingKeepsSelectAll(string context) => RunAsync((window, model) =>
    {
        UIElement source;
        TextBox? text = null;
        if (context == "Rich text")
        {
            var rich = new RichTextBox(); rich.AppendText("rename text");
            window.BrowserCenter.Children.Add(rich); source = rich;
        }
        else if (context == "Editable combo")
        {
            var combo = new System.Windows.Controls.ComboBox { IsEditable = true, Text = "rename text", Style = new Style(typeof(System.Windows.Controls.ComboBox)) };
            window.BrowserCenter.Children.Add(combo); window.UpdateLayout(); combo.ApplyTemplate();
            source = text = (TextBox)combo.Template.FindName("PART_EditableTextBox", combo);
        }
        else
        {
            text = context switch { "Search" => window.BrowserSearchBox, "Path" => window.BrowserCurrentPath, _ => new TextBox() };
            if (context == "Rename") window.BrowserCenter.Children.Add(text);
            text.Text = "rename text"; source = text;
        }
        model.SelectSingle(0);
        var selected = model.SelectedKeys.ToHashSet();
        Focus(window, source);
        Press(window, source);
        Assert.True(selected.SetEquals(model.SelectedKeys));
        if (text is not null) Assert.Equal(text.Text.Length, text.SelectionLength);
        else Assert.Contains("rename text", ((RichTextBox)source).Selection.Text);
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData("Subclips")]
    [InlineData("Jobs")]
    [InlineData("Compact Jobs")]
    [InlineData("Filmstrip")]
    [InlineData("Inspector")]
    [InlineData("Player")]
    [InlineData("Neutral Inspector in Player")]
    [InlineData("Other selector")]
    public Task IndependentOwnersNeverSelectBrowser(string context) => RunAsync(async (window, model) =>
    {
        Call(window, "EnsurePlayerViewerHost");
        var player = Field<PlayerViewerHost>(window, "_playerViewerHost");
        UIElement source;
        if (context == "Neutral Inspector in Player")
        {
            Call(window, "SetBrowserPresentationMode", BrowserPresentationMode.PlayerViewer);
            window.HomeRightPanel.SelectSurface("inspector"); Call(window, "SetRightPanelOpen", true);
            source = Field<MediaInspectorView>(window, "_inspector");
        }
        else if (context is "Player" or "Filmstrip")
        {
            Call(window, "SetBrowserPresentationMode", BrowserPresentationMode.PlayerViewer);
            source = context == "Player" ? player : player.Filmstrip;
        }
        else if (context == "Subclips")
        {
            window.HomeRightPanel.SetSurfaceAvailable("subclips", true);
            window.HomeRightPanel.SelectSurface("subclips");
            Call(window, "SetRightPanelOpen", true);
            typeof(PlayerViewerHost).GetField("_applyingWorkspaceReview", Hidden)!.SetValue(player, true);
            player.SubclipsList.ItemsSource = Enumerable.Range(0, 2).Select(i => new SubclipPanelItem(new(
                Guid.NewGuid(), Guid.NewGuid(), $"Subclip {i}", i, TimeSpan.Zero, TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(2), 1, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch))).ToArray();
            source = player.SubclipsList;
        }
        else if (context == "Jobs")
        {
            window.MainTabs.SelectedIndex = ShellDestinationSelection.Index(ShellDestination.Jobs);
            window.HistoryList.ItemsSource = Enumerable.Range(0, 2).Select(i => new JobsWorkspaceItem(Guid.NewGuid(), null, null,
                false, false, $"Job {i}", "test", JobState.Completed, null, "", "", "", "", "", DateTimeOffset.UnixEpoch, i)).ToArray();
            source = window.HistoryList;
        }
        else if (context == "Compact Jobs")
        {
            window.HomeRightPanel.SelectSurface("jobs"); Call(window, "SetRightPanelOpen", true);
            source = Field<CompactJobsView>(window, "_compactJobsView").CompactJobsList;
        }
        else if (context == "Inspector")
        {
            Call(window, "SetRightPanelOpen", true);
            window.HomeRightPanel.Focusable = true; source = window.HomeRightPanel;
        }
        else
        {
            var list = new ListBox { SelectionMode = SelectionMode.Extended };
            list.Items.Add("one"); list.Items.Add("two"); window.BrowserCenter.Children.Add(list); source = list;
        }
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        model.SelectSingle(0); var selected = model.SelectedKeys.ToHashSet();
        Focus(window, source);
        Press(window, source);
        Assert.True(selected.SetEquals(model.SelectedKeys));
        if (context is "Subclips" or "Jobs" or "Other selector") Assert.Equal(2, ((ListBox)source).SelectedItems.Count);
    });

    [Theory]
    [InlineData("Context menu")]
    [InlineData("Filter popup")]
    [InlineData("Dropdown")]
    [InlineData("Dialog")]
    public Task OpenLocalSurfacesBlockBrowserFallback(string context) => RunAsync((window, model) =>
    {
        model.SelectSingle(0); var selected = model.SelectedKeys.ToHashSet();
        Focus(window, window.BrowserGoButton);
        Window? dialog = null;
        try
        {
            switch (context)
            {
                case "Context menu": window.BrowserGridRows.ContextMenu.IsOpen = true; break;
                case "Filter popup": window.BrowserFilterPopup.IsOpen = true; break;
                case "Dropdown": window.BrowserSortCombo.IsDropDownOpen = true; break;
                case "Dialog":
                    dialog = new Window { Owner = window, Left = -32000, Top = -32000, ShowInTaskbar = false, Content = new TextBox { Text = "dialog text" } };
                    dialog.Show(); Press(dialog, (TextBox)dialog.Content); break;
            }
            if (context != "Dialog")
            {
                Press(window, window.BrowserGoButton);
                Assert.True(selected.SetEquals(model.SelectedKeys));
            }
            else
            {
                // A modal owner is disabled; no fallback even if invoked through a stale route.
                model.SelectSingle(0); window.IsEnabled = false;
                Press(window, window.BrowserGoButton); Assert.Single(model.SelectedKeys);
            }
        }
        finally
        {
            window.IsEnabled = true; window.BrowserGridRows.ContextMenu.IsOpen = false;
            window.BrowserFilterPopup.IsOpen = false; window.BrowserSortCombo.IsDropDownOpen = false; dialog?.Close();
        }
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(ModifierKeys.None, false)]
    [InlineData(ModifierKeys.Control, true)]
    [InlineData(ModifierKeys.Control | ModifierKeys.Shift, false)]
    [InlineData(ModifierKeys.Control | ModifierKeys.Alt, false)]
    public Task FallbackRequiresExactControlAndGridRetainsExtraModifierBehavior(ModifierKeys modifiers, bool fallback) => RunAsync((window, model) =>
    {
        Focus(window, window.BrowserGoButton);
        Press(window, window.BrowserGoButton, modifiers);
        Assert.Equal(fallback ? 3 : 0, model.SelectedKeys.Count);
        model.ClearSelection();
        Press(window, window.BrowserGridRows, modifiers);
        Assert.Equal(modifiers.HasFlag(ModifierKeys.Control) ? 3 : 0, model.SelectedKeys.Count);
        return Task.CompletedTask;
    });

    [Fact]
    public Task NotReadyAndEmptyViewsDoNotSelectStaleMediaAndSelectionVetoIsPreserved() => RunAsync((window, model) =>
    {
        Call(window, "ShowBrowserLoadingState");
        Press(window, window); Assert.Empty(model.SelectedKeys);
        window.BrowserGridRows.Visibility = Visibility.Visible;
        window.BrowserQueryToolbar.IsEnabled = false;
        Press(window, window); Assert.Empty(model.SelectedKeys);
        window.BrowserQueryToolbar.IsEnabled = true;
        model.SelectionChanging = () => false;
        Press(window, window); Assert.Empty(model.SelectedKeys);
        model.SelectionChanging = null;
        model.SetQuery(BrowserQuery.Default with { SearchText = "no matches" });
        Assert.True(Press(window, window).Handled); Assert.Empty(model.SelectedKeys);
        return Task.CompletedTask;
    });

    private static void Focus(MainWindow window, UIElement source)
    {
        window.Activate(); window.UpdateLayout(); Keyboard.Focus(source);
    }

    private static KeyEventArgs Press(Window window, UIElement source, ModifierKeys modifiers = ModifierKeys.Control, Key key = Key.A)
    {
        var old = new byte[256]; Assert.True(GetKeyboardState(old));
        var state = new byte[256];
        if (modifiers.HasFlag(ModifierKeys.Control)) state[0x11] = state[0xA2] = 0x80;
        if (modifiers.HasFlag(ModifierKeys.Shift)) state[0x10] = state[0xA0] = 0x80;
        if (modifiers.HasFlag(ModifierKeys.Alt)) state[0x12] = state[0xA4] = 0x80;
        try
        {
            Assert.True(SetKeyboardState(state)); Assert.Equal(modifiers, Keyboard.Modifiers);
            var args = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window)!, 0, key)
                { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            source.RaiseEvent(args);
            if (!args.Handled) { args.RoutedEvent = Keyboard.KeyDownEvent; source.RaiseEvent(args); }
            return args;
        }
        finally { Assert.True(SetKeyboardState(old)); }
    }

    [Theory]
    [InlineData("Grid")]
    [InlineData("Details")]
    public Task SearchEnterFinishesLiveQueryWithoutLosingFilterOrEditingOnReturn(string layout) => RunAsync(async (window, model) =>
    {
        window.ApplyBrowserLayout(Enum.Parse<BrowserLayoutMode>(layout), true);
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Focus(window, window.BrowserSearchBox);
        window.BrowserSearchBox.Text = "visible";
        Assert.True(Press(window, window.BrowserSearchBox).Handled);
        Assert.Equal("visible", window.BrowserSearchBox.SelectedText);
        Assert.Empty(model.SelectedKeys);
        // Enter must flush a pending debounce immediately, not select the previous result set.
        Assert.True(Press(window, window.BrowserSearchBox, ModifierKeys.None, Key.Enter).Handled);
        Assert.Same(window.BrowserGridRows, Keyboard.FocusedElement);
        Assert.Equal("visible", model.Query.SearchText);
        Assert.Equal("visible", window.BrowserSearchBox.Text);
        Assert.Equal(2, model.VisibleCount);
        Press(window, window.BrowserGridRows);
        Assert.Equal(2, model.SelectedKeys.Count);
        Assert.All(model.SelectedTilesInBrowserOrder, tile => Assert.Contains("visible", tile.Name));
        for (var i = 0; i < 3; i++)
        {
            Focus(window, window.BrowserSearchBox);
            Press(window, window.BrowserSearchBox);
            Assert.Equal("visible", window.BrowserSearchBox.SelectedText);
            Press(window, window.BrowserSearchBox, ModifierKeys.None, Key.Enter);
            Assert.Same(window.BrowserGridRows, Keyboard.FocusedElement);
        }
        // No delayed callback may steal focus back from a new edit.
        Focus(window, window.BrowserSearchBox);
        await Task.Delay(350);
        Assert.Same(window.BrowserSearchBox, Keyboard.FocusedElement);
        Assert.Equal("visible", model.Query.SearchText);
        Assert.Equal(2, model.SelectedKeys.Count);
    });

    [Theory]
    [InlineData("Filter popup")]
    [InlineData("Search menu")]
    [InlineData("Shift Enter")]
    [InlineData("Loading")]
    public Task SearchEnterDoesNotTakeLocalPopupOrModifiedKeyOwnership(string context) => RunAsync((window, model) =>
    {
        Focus(window, window.BrowserSearchBox);
        window.BrowserSearchBox.Text = "visible";
        var menu = new ContextMenu(); menu.Items.Add(new MenuItem { Header = "Local action" });
        try
        {
            if (context == "Filter popup") window.BrowserFilterPopup.IsOpen = true;
            if (context == "Search menu") { window.BrowserSearchBox.ContextMenu = menu; menu.IsOpen = true; }
            if (context == "Loading") Call(window, "ShowBrowserLoadingState");
            Press(window, window.BrowserSearchBox, context == "Shift Enter" ? ModifierKeys.Shift : ModifierKeys.None, Key.Enter);
            Assert.False(window.BrowserGridRows.IsKeyboardFocused);
            Assert.Empty(model.SelectedKeys);
            Assert.Equal("visible", window.BrowserSearchBox.Text);
        }
        finally { menu.IsOpen = false; window.BrowserFilterPopup.IsOpen = false; }
        return Task.CompletedTask;
    });

    [Fact]
    public Task NeutralInspectorClickReleasesOnlyEditingFocusAndKeepsDraftAndExplicitSave() => RunAsync(async (window, model) =>
    {
        // Finish the initial navigation's existing Loaded-priority tree reveal before editing.
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        var focusTrace = "";
        window.AddHandler(Keyboard.GotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler((_, e) =>
        {
            if (e.NewFocus is TreeViewItem) focusTrace = Environment.StackTrace;
        }), true);
        var storage = Field<LightflowStorageCoordinator>(window, "_storage");
        var inspector = Field<MediaInspectorView>(window, "_inspector");
        var store = new CountingDescriptions(storage.AssetDescriptions);
        inspector.Initialize(() => new MediaInspectorService(storage.Previews, storage.AssetClassifications,
            storage.Locations.PreviewsDirectory, storage.Markers), store);
        model.SelectSingle(0);
        window.HomeRightPanel.SelectSurface("inspector"); Call(window, "SetRightPanelOpen", true);
        inspector.ConfirmTransition = _ => false;
        try
        {
            await WaitForAsync(() => Descendants<TextBox>(inspector.DescriptionSection).Count() == 5);
            var fields = Descendants<TextBox>(inspector.DescriptionSection).ToArray();
            var editor = Assert.IsType<InspectorDescriptionEditor>(inspector.DescriptionSection.DataContext);
            var id = Assert.Single(model.SelectedAssetIdsInBrowserOrder);
            Focus(window, fields[0]); fields[0].Text = "I02 title";
            Press(window, fields[0]); Assert.Equal("I02 title", fields[0].SelectedText);
            Assert.Single(model.SelectedKeys);
            // Another editor is interactive: the preview handler must not release its owner.
            NeutralPress(fields[1]); Assert.Same(fields[0], Keyboard.FocusedElement);
            Focus(window, fields[1]); fields[1].Text = "I02 caption";
            Press(window, fields[1]); Assert.Equal("I02 caption", fields[1].SelectedText);
            var neutral = Assert.IsAssignableFrom<UIElement>(inspector.InputHitTest(new Point(2, 2)));
            var selected = model.SelectedKeys.ToHashSet();
            var title = inspector.TitleText.Text;
            for (var i = 0; i < 3; i++)
            {
                Focus(window, fields[i % 2]);
                var pointer = NeutralPress(neutral);
                Assert.False(pointer.Handled);
                Assert.Same(inspector, Keyboard.FocusedElement);
                Assert.True(selected.SetEquals(model.SelectedKeys));
                Assert.Equal(title, inspector.TitleText.Text);
                Assert.True(editor.HasDraft); Assert.Equal(0, store.Saves);
            }
            Press(window, inspector); // Normal selection transition veto preserves the unapplied draft.
            Assert.Single(model.SelectedKeys); Assert.True(editor.HasDraft); Assert.Equal(0, store.Saves);
            Assert.True(await editor.ApplyAsync());
            Assert.Equal(1, store.Saves);
            var saved = (await storage.AssetDescriptions.GetAsync([id]))[id];
            Assert.Equal("I02 title", saved.Title); Assert.Equal("I02 caption", saved.Description);
            Assert.Equal(1, saved.Revision);
            NeutralPress(neutral); Press(window, inspector);
            Assert.Equal(3, model.SelectedKeys.Count); Assert.Equal(1, store.Saves);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.True(ReferenceEquals(inspector, Keyboard.FocusedElement), focusTrace);
        }
        finally { inspector.ConfirmTransition = _ => true; }
    });

    [Theory]
    [InlineData("Button")]
    [InlineData("Dropdown")]
    [InlineData("Scrollbar")]
    [InlineData("Marker card")]
    public Task InspectorInteractivePointerAndKeyboardOwnersAreNotNeutral(string context) => RunAsync((window, model) =>
    {
        var inspector = Field<MediaInspectorView>(window, "_inspector");
        window.HomeRightPanel.SelectSurface("inspector"); Call(window, "SetRightPanelOpen", true);
        var host = Assert.IsType<DockPanel>(inspector.Content);
        var edit = new TextBox { Text = "metadata text" }; DockPanel.SetDock(edit, Dock.Top); host.Children.Insert(0, edit);
        UIElement control = context switch
        {
            "Button" => new Button { Content = "Action" },
            "Dropdown" => new System.Windows.Controls.ComboBox { ItemsSource = new[] { "one", "two" } },
            "Scrollbar" => new System.Windows.Controls.Primitives.ScrollBar(),
            _ => new Border { Background = System.Windows.Media.Brushes.Transparent,
                DataContext = new InspectorMarkerCard(new TimelineMarker(Guid.NewGuid(), Guid.NewGuid(), TimeSpan.Zero, "Marker", 1, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch)) }
        };
        ((FrameworkElement)control).Height = 34;
        DockPanel.SetDock(control, Dock.Top); host.Children.Insert(1, control); Focus(window, edit);
        model.SelectSingle(0); var selection = model.SelectedKeys.ToHashSet();
        NeutralPress(control);
        Assert.Same(edit, Keyboard.FocusedElement);
        if (control.Focusable) Focus(window, control);
        Press(window, control); Assert.True(selection.SetEquals(model.SelectedKeys));
        var scroll = Descendants<ScrollViewer>(inspector).First();
        var offset = scroll.VerticalOffset;
        var wheel = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120) { RoutedEvent = Mouse.PreviewMouseWheelEvent };
        edit.RaiseEvent(wheel); Assert.False(wheel.Handled);
        Assert.NotSame(inspector, Keyboard.FocusedElement);
        Assert.Equal(offset, scroll.VerticalOffset);
        return Task.CompletedTask;
    });

    private static MouseButtonEventArgs NeutralPress(UIElement source)
    {
        var args = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
            { RoutedEvent = Mouse.PreviewMouseDownEvent };
        source.RaiseEvent(args); return args;
    }

    [Fact]
    public Task NeutralInspectorFocusCommitsExistingMarkerRenameOnce() => RunAsync(async (window, model) =>
    {
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        var storage = Field<LightflowStorageCoordinator>(window, "_storage");
        var tile = model.Tiles.First(t => t.Category == MediaTypeCategory.Video);
        model.SelectSingle(tile.Index);
        var marker = (await storage.Markers.CreateAsync(tile.AssetId!.Value, TimeSpan.Zero)).Marker;
        using var inspector = new MediaInspectorView();
        inspector.InitializeMarkers(storage.Markers, storage.CreateMarkerThumbnailService());
        var markerWindow = new Window { Content = inspector, Width = 400, Height = 700, Left = -32000, Top = -32000, ShowInTaskbar = false };
        try
        {
            markerWindow.Show();
            inspector.SetContext([new(tile.AssetId, tile.Name, tile.RelativePath, MediaPresentationKind.Video)], true);
            inspector.PresentMarkers([marker], default); inspector.MarkerSection.Visibility = Visibility.Visible;
            markerWindow.UpdateLayout();
            var card = Assert.Single(inspector.MarkerCards);
            var changes = 0; inspector.MarkersChanged += (_, _) => changes++;
            inspector.BeginMarkerRename(card, inspector.InspectorMarkers);
            await WaitForAsync(() => Descendants<TextBox>(inspector.InspectorMarkers).Any(t => t.IsKeyboardFocused));
            var name = Assert.Single(Descendants<TextBox>(inspector.InspectorMarkers));
            name.Text = "I02 saved on neutral focus";
            NeutralPress(inspector.TitleText);
            Assert.Same(inspector, Keyboard.FocusedElement);
            await WaitForAsync(() => changes == 1);
            var saved = Assert.Single(await storage.Markers.ListAsync(tile.AssetId.Value));
            Assert.Equal("I02 saved on neutral focus", saved.Name);
            Assert.Equal(marker.Revision + 1, saved.Revision);
            Press(markerWindow, inspector); Assert.Single(model.SelectedKeys);
            NeutralPress(inspector.TitleText);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.Equal(1, changes);
            }
        finally { markerWindow.Close(); }
    });

    [Fact]
    public Task NeutralInspectorFocusDoesNotOverrideItsOpenMenuOrPointerCapture() => RunAsync(async (window, model) =>
    {
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        var inspector = Field<MediaInspectorView>(window, "_inspector");
        model.SelectSingle(0);
        window.HomeRightPanel.SelectSurface("inspector"); Call(window, "SetRightPanelOpen", true);
        await WaitForAsync(() => Descendants<TextBox>(inspector.DescriptionSection).Count() == 5);
        window.UpdateLayout(); NeutralPress(inspector.TitleText);
        Assert.Same(inspector, Keyboard.FocusedElement);
        var menu = new ContextMenu(); menu.Items.Add(new MenuItem { Header = "Local action" });
        inspector.ContextMenu = menu;
        try
        {
            menu.IsOpen = true;
            Press(window, inspector); Assert.Single(model.SelectedKeys);
            menu.IsOpen = false;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var edit = Descendants<TextBox>(inspector.DescriptionSection).First();
            Focus(window, edit);
            // Native SetCapture is unavailable on the non-input desktop. Keep the captured-owner
            // policy assertion explicit at the local classifier boundary; other pointer cases route live.
            Assert.False(inspector.IsNeutralPointerSource(inspector.TitleText, edit));
            Assert.Same(edit, Keyboard.FocusedElement);
        }
        finally { menu.IsOpen = false; }
    });

    private static IEnumerable<T> Descendants<T>(DependencyObject owner) where T : DependencyObject
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(owner); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(owner, i);
            if (child is T found) yield return found;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }

    private static async Task WaitForAsync(Func<bool> ready)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!ready()) await Task.Delay(20, timeout.Token);
    }

    private sealed class CountingDescriptions(IAssetDescriptionStore inner) : IAssetDescriptionStore
    {
        internal int Saves;
        public Task<IReadOnlyDictionary<Guid, AssetDescription>> GetAsync(IReadOnlyCollection<Guid> ids, CancellationToken token = default) => inner.GetAsync(ids, token);
        public Task ApplyAsync(IReadOnlyDictionary<Guid, long> expected, AssetDescriptionPatch patch, CancellationToken token = default)
        { Saves++; return inner.ApplyAsync(expected, patch, token); }
    }

    private static Task RunAsync(Func<MainWindow, BrowserGridModel, Task> test) => StaDispatcher.RunAsync(async () =>
    {
        TestWpfApplication.EnsureLoaded();
        var directory = Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "browser-select-all-tests", Guid.NewGuid().ToString("N"))).FullName;
        var media = Directory.CreateDirectory(Path.Combine(directory, "media")).FullName;
        var startup = await LightflowStorageCoordinator.StartAsync(Path.Combine(directory, "data"));
        var storage = startup.Coordinator!;
        await storage.MediaMonitoring!.DisposeAsync();
        storage.SaveSettings(storage.Settings with { BackupCatalogOnClose = false });
        var root = (await storage.MediaRoots.CreateAsync("Fixture", media)).Root!;
        foreach (var name in new[] { "visible-video.mov", "visible-image.jpg", "hidden-video.mov" })
        {
            File.WriteAllText(Path.Combine(media, name), "disposable media identity fixture");
            await storage.MediaAssets.CreateAsync(root.RootId, name, name.EndsWith("jpg") ? "image" : "video");
        }
        var window = new MainWindow(storage, startup.Status, startup.Diagnostic)
            { Left = -32000, Top = -32000, ShowInTaskbar = false, Width = 1440, Height = 900 };
        try
        {
            window.Show(); Assert.True(await window.StartupCompletion.WaitAsync(TimeSpan.FromSeconds(30)));
            window.BrowserCurrentPath.Text = media;
            await (Task)Call(window, "NavigateToEnteredBrowserPathAsync")!;
            var model = Field<BrowserGridModel>(window, "_browserGrid"); Assert.Equal(3, model.Tiles.Count);
            await test(window, model);
        }
        finally { window.Close(); await storage.DisposeAsync(); }
    });
}
