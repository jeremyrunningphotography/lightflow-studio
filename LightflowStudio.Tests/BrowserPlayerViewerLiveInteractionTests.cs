using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Xunit;

namespace LightflowStudio.Tests;

/// <summary>
/// #110: a live-WPF regression seam for the Browser's Player/Viewer presentation state — a real MainWindow, a
/// real temp-directory Catalog, and a real generated image file, driven through the actual visual tree rather
/// than any model-level shortcut, so a defect specific to the WPF container/visibility/focus layer (the
/// concern this whole live-interaction test category exists for — see BrowserToggleOffLiveInteractionTests)
/// would surface here even though MediaPlaybackLeaseSessionTests and FlyleafPlaybackIntegrationTests already prove
/// the playback lease/session and real-Flyleaf paths correct in isolation. Uses a still image, not video: the
/// thing genuinely new in #110 is the Browser↔Player container wiring itself (visibility toggling, scroll/
/// selection/location preservation, lazy PlayerViewerHost construction), not playback internals, and a real
/// WIC image decode exercises that same OpenAsync/CloseAsync/context-preservation path without also pulling in
/// the heavier real-Flyleaf dependency this test does not need.
/// </summary>
[Collection("STA dispatcher tests")]
public sealed class BrowserPlayerViewerLiveInteractionTests : IAsyncLifetime
{
    private readonly string _appDataRoot = Path.Combine(Path.GetTempPath(), $"lightflow-live-app-{Guid.NewGuid():N}");
    private readonly string _mediaRoot = Path.Combine(Path.GetTempPath(), $"lightflow-live-media-{Guid.NewGuid():N}");
    private string _photoPath = "";

    public Task InitializeAsync()
    {
        Directory.CreateDirectory(_mediaRoot);
        _photoPath = Path.Combine(_mediaRoot, "photo.jpg");
        CreateTestJpeg(_photoPath);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        TryDelete(_appDataRoot);
        TryDelete(_mediaRoot);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task DoubleClickThenEscape_OpensTheViewerAndReturnsToTheSameBrowserContext()
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
            window.Width = 1800;
            window.Height = 720;
            try
            {
                window.Show();
                await WaitUntilAsync(() => window.BrowserFolderTree.Items.Count > 0, "storage entries to populate");

                window.BrowserCurrentPath.Text = _mediaRoot;
                RaiseClick(window.BrowserGoButton);
                await WaitUntilAsync(() => window.BrowserLoadingOverlay.Visibility != Visibility.Visible &&
                    string.Equals(window.BrowserCurrentPath.Text, _mediaRoot, StringComparison.OrdinalIgnoreCase),
                    "navigation to the media root to settle");

                var tile = await WaitForTileAsync(window);
                Assert.Equal(MediaTypeCategory.StillImage, tile!.Category);

                // A real MouseLeftButtonDown on the tile's own realized visual, exactly the routed event
                // BrowserGridTile_MouseLeftButtonDown handles — not a model-level SelectSingle shortcut.
                var tileElement = FindElementByDataContext(window.BrowserGridRows, tile);
                Assert.NotNull(tileElement);
                RaiseMouseLeftButtonDown(tileElement!, clickCount: 2);

                // OpenBrowserPlayerViewerAsync is fire-and-forget from the click handler and awaits a real
                // Catalog round-trip before switching presentation, so this must poll rather than assert
                // immediately after RaiseEvent returns.
                await WaitUntilAsync(() => window.BrowserPlayerHost.Visibility == Visibility.Visible &&
                    window.BrowserGridHost.Visibility == Visibility.Collapsed,
                    "the Player/Viewer presentation to become visible");

                // #110: the query toolbar (Subfolders/All-Images-RAW-Video/Search/Filter/Sort) describes the
                // Grid's own result set and must be hidden while reviewing one open asset — presentation only,
                // BrowserQuery/filter/sort/search state itself is untouched (checked via BrowserCurrentPath
                // below once back in Grid mode).
                Assert.Equal(Visibility.Collapsed, window.BrowserQueryToolbar.Visibility);

                var host = Assert.IsType<PlayerViewerHost>(window.BrowserPlayerHost.Content);
                await WaitUntilAsync(() => host.CurrentAsset?.Name == "photo.jpg", "the Viewer to finish opening the photo");
                Assert.Equal(ExpectedSelectionActionRow(window), Grid.GetRow(window.BrowserSelectionActionToolbar));

                RaiseClick(window.JobsDrawerPullButton);
                window.UpdateLayout();
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                window.UpdateLayout();
                Assert.Equal(Visibility.Visible, window.JobsDrawer.Visibility);
                Assert.Equal("photo.jpg", host.CurrentAsset?.Name);
                Assert.Equal(4, Grid.GetRow(window.BrowserSelectionActionToolbar));
                AssertContained(window.BrowserPlayerHost, window.BrowserCenter);
                window.JobsDrawerColumn.Width = new GridLength(610);
                window.UpdateLayout();
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                window.UpdateLayout();
                Assert.Equal("photo.jpg", host.CurrentAsset?.Name);
                AssertContained(window.BrowserPlayerHost, window.BrowserCenter);
                RaiseClick(window.JobsDrawerPullButton);
                window.UpdateLayout();
                Assert.Equal("photo.jpg", host.CurrentAsset?.Name);
                Assert.Equal(ExpectedSelectionActionRow(window), Grid.GetRow(window.BrowserSelectionActionToolbar));
                AssertContained(window.BrowserPlayerHost, window.BrowserCenter);

                // Esc, handled by PlayerViewerHost's own PreviewKeyDown — raised directly on that control
                // rather than relying on real keyboard focus routing, which an off-screen test window cannot
                // reliably establish.
                host.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), 0, Key.Escape)
                    { RoutedEvent = UIElement.PreviewKeyDownEvent });

                await WaitUntilAsync(() => window.BrowserGridHost.Visibility == Visibility.Visible &&
                    window.BrowserPlayerHost.Visibility == Visibility.Collapsed,
                    "the Browser Grid presentation to return");
                Assert.Equal(BrowserPresentationMode.Grid, GetPresentationMode(window));
                Assert.Equal(Visibility.Visible, window.BrowserQueryToolbar.Visibility);

                // Browser context — location and the selection the tile click made — survived the round trip.
                Assert.Equal(_mediaRoot, window.BrowserCurrentPath.Text, ignoreCase: true);
                Assert.True(tile.IsSelected);
            }
            finally
            {
                window.Close();
                await storage.DisposeAsync();
            }
        });
    }

    [Fact]
    public async Task BrowserLutCombo_RendersOptionLabelsForSelectedValueAndDropdownItems()
    {
        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();
            var startup = await LightflowStorageCoordinator.StartAsync(_appDataRoot);
            Assert.True(startup.IsReady, startup.Diagnostic);
            var storage = startup.Coordinator!;
            var window = NewOffscreenWindow(storage, startup);
            Window? comboWindow = null;
            try
            {
                window.Show();
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                await WaitUntilAsync(() =>
                        window.BrowserCameraLutCombo.SelectedItem is BrowserLutActionOption { Label: "No LUT" } &&
                        window.BrowserCreativeLutCombo.SelectedItem is BrowserLutActionOption { Label: "No LUT" },
                    "empty-selection LUT presentation");
                Assert.False(window.BrowserCameraLutCombo.IsEnabled);
                Assert.False(window.BrowserCreativeLutCombo.IsEnabled);
                var combo = new ComboBox
                {
                    Style = Assert.IsType<Style>(window.FindResource("BrowserSelectionLutComboStyle"))
                };
                comboWindow = new Window
                {
                    Content = combo, Width = 240, Height = 100,
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Left = -32000, Top = -32000, ShowInTaskbar = false
                };
                comboWindow.Show();
                var expected = "Persisted Camera LUT";
                var presentation = new BrowserLutPickerPresentation(
                    [new BrowserLutActionOption(Guid.NewGuid(), expected)], 0);
                var apply = typeof(MainWindow).GetMethod("ApplyBrowserLutPresentation",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                    ?? throw new MissingMethodException(nameof(MainWindow), "ApplyBrowserLutPresentation");

                apply.Invoke(null, [combo, presentation]);
                combo.ApplyTemplate();
                combo.UpdateLayout();
                Assert.Contains(VisualText(combo), text => text == expected);
                Assert.DoesNotContain(VisualText(combo), text =>
                    text.Contains(nameof(BrowserLutActionOption), StringComparison.Ordinal));

                combo.IsDropDownOpen = true;
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                var item = Assert.IsType<ComboBoxItem>(combo.ItemContainerGenerator.ContainerFromIndex(0));
                item.ApplyTemplate();
                item.UpdateLayout();
                Assert.Contains(VisualText(item), text => text == expected);
                Assert.DoesNotContain(VisualText(item), text =>
                    text.Contains(nameof(BrowserLutActionOption), StringComparison.Ordinal));
            }
            finally
            {
                comboWindow?.Close();
                window.Close();
                await storage.DisposeAsync();
            }
        });
    }

    private static IEnumerable<string> VisualText(DependencyObject root)
    {
        if (root is TextBlock text && !string.IsNullOrEmpty(text.Text)) yield return text.Text;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var childText in VisualText(VisualTreeHelper.GetChild(root, index)))
                yield return childText;
    }

    private static async Task<BrowserGridTile?> WaitForTileAsync(MainWindow window)
    {
        await WaitUntilAsync(() => window.BrowserGridRows.Items.Count > 0, "the grid to populate a row");
        window.BrowserGridRows.UpdateLayout();
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        window.BrowserGridRows.UpdateLayout();
        return ((BrowserGridRow)window.BrowserGridRows.Items[0]).Tiles.FirstOrDefault();
    }

    private static BrowserPresentationMode GetPresentationMode(MainWindow window) =>
        window.BrowserPlayerHost.Visibility == Visibility.Visible ? BrowserPresentationMode.PlayerViewer : BrowserPresentationMode.Grid;

    /// <summary>
    /// Finds the tile's own <c>Border</c> specifically — not merely the first visual-tree element whose
    /// (inherited) DataContext matches, which would be the tile's <c>ContentPresenter</c> ancestor, an element
    /// higher in the tree than the Border that actually carries <c>MouseLeftButtonDown="BrowserGridTile_MouseLeftButtonDown"</c>
    /// (see MainWindow.xaml). Raising the event on that ancestor could never reach a descendant's own
    /// directly-attached handler, so this must locate the real leaf, not just any matching DataContext.
    /// </summary>
    private static Border? FindElementByDataContext(DependencyObject root, object dataContext)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is Border { } border && ReferenceEquals(border.DataContext, dataContext)) return border;
            if (FindElementByDataContext(child, dataContext) is { } found) return found;
        }
        return null;
    }

    private static void RaiseMouseLeftButtonDown(UIElement element, int clickCount)
    {
        var args = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
        {
            RoutedEvent = UIElement.MouseLeftButtonDownEvent,
            Source = element
        };
        SetClickCount(args, clickCount);
        element.RaiseEvent(args);
    }

    /// <summary>
    /// MouseButtonEventArgs.ClickCount has no public setter — it is normally derived from MouseDevice's own
    /// internal click-tracking, which an offscreen test window cannot drive through real timed clicks. Setting
    /// the backing field directly is the only way to produce a genuine double-click gesture for
    /// BrowserGridTile_MouseLeftButtonDown's e.ClickCount &gt;= 2 branch.
    /// </summary>
    private static void SetClickCount(MouseButtonEventArgs args, int clickCount)
    {
        var field = typeof(MouseButtonEventArgs).GetField("_count",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?? throw new InvalidOperationException("MouseButtonEventArgs._count was not found by reflection.");
        field.SetValue(args, clickCount);
    }

    private static void CreateTestJpeg(string path)
    {
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
            context.DrawRectangle(System.Windows.Media.Brushes.SteelBlue, null, new Rect(0, 0, 32, 24));
        var bitmap = new RenderTargetBitmap(32, 24, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new JpegBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        encoder.Save(stream);
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

    private static void AssertContained(FrameworkElement child, FrameworkElement ancestor)
    {
        var bounds = child.TransformToAncestor(ancestor).TransformBounds(new Rect(child.RenderSize));
        Assert.True(bounds.Left >= -1 && bounds.Right <= ancestor.ActualWidth + 1,
            $"{child.Name} is outside {ancestor.Name}: {bounds} vs {ancestor.ActualWidth:0.##}");
    }

    private static int ExpectedSelectionActionRow(MainWindow window) =>
        window.BrowserCenter.ActualWidth >= 1120 ? 2 : 4;

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
