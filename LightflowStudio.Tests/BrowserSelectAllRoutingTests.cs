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
    [InlineData("Other selector")]
    public Task IndependentOwnersNeverSelectBrowser(string context) => RunAsync(async (window, model) =>
    {
        Call(window, "EnsurePlayerViewerHost");
        var player = Field<PlayerViewerHost>(window, "_playerViewerHost");
        UIElement source;
        if (context is "Player" or "Filmstrip")
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

    private static KeyEventArgs Press(Window window, UIElement source, ModifierKeys modifiers = ModifierKeys.Control)
    {
        var old = new byte[256]; Assert.True(GetKeyboardState(old));
        var state = new byte[256];
        if (modifiers.HasFlag(ModifierKeys.Control)) state[0x11] = state[0xA2] = 0x80;
        if (modifiers.HasFlag(ModifierKeys.Shift)) state[0x10] = state[0xA0] = 0x80;
        if (modifiers.HasFlag(ModifierKeys.Alt)) state[0x12] = state[0xA4] = 0x80;
        try
        {
            Assert.True(SetKeyboardState(state)); Assert.Equal(modifiers, Keyboard.Modifiers);
            var args = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window)!, 0, Key.A)
                { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            source.RaiseEvent(args);
            if (!args.Handled) { args.RoutedEvent = Keyboard.KeyDownEvent; source.RaiseEvent(args); }
            return args;
        }
        finally { Assert.True(SetKeyboardState(old)); }
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
