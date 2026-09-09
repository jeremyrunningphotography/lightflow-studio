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
    public async Task Inspector_PreservesHomeAndPlayerContext_ResizesAndRestores_WithGlobalJobs()
    {
        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();
            var startup = await LightflowStorageCoordinator.StartAsync(_appDataRoot);
            var storage = startup.Coordinator!;
            await storage.MediaRoots.CreateAsync("Library", _mediaRoot);
            var window = NewOffscreenWindow(storage, startup);
            window.Width = 1440;
            try
            {
                window.Show();
                await WaitUntilAsync(() => window.BrowserFolderTree.Items.Count > 0, "storage");
                window.BrowserCurrentPath.Text = _mediaRoot;
                RaiseClick(window.BrowserGoButton);
                await WaitUntilAsync(() => window.BrowserLoadingOverlay.Visibility != Visibility.Visible && window.BrowserGridRows.Items.Count > 0, "media");
                var tile = await WaitForTileAsync(window);
                window.RightPanelToggle.IsChecked = true;
                RaiseClick(window.RightPanelToggle);
                window.UpdateLayout();
                var inspector = Assert.IsType<MediaInspectorView>(((TabItem)window.HomeRightPanel.SurfaceTabs.SelectedItem).Content);
                Assert.Contains("Select media", inspector.TitleText.Text);
                var element = FindElementByDataContext(window.BrowserGridRows, tile!);
                RaiseMouseLeftButtonDown(element!, 1);
                await WaitUntilAsync(() => inspector.FieldGroups.ItemsSource is not null && inspector.TitleText.Text == tile!.Name, "Inspector");
                Assert.True(window.RightPanelColumn.ActualWidth >= 280);
                var rows = window.BrowserGridRows.ItemsSource;
                window.RightPanelColumn.Width = new GridLength(410);
                window.RightPanelSplitter.RaiseEvent(new System.Windows.Controls.Primitives.DragCompletedEventArgs(50, 0, false)
                { RoutedEvent = System.Windows.Controls.Primitives.Thumb.DragCompletedEvent });
                window.RightPanelToggle.IsChecked = false; RaiseClick(window.RightPanelToggle);
                window.UpdateLayout();
                Assert.Equal(0, window.RightPanelColumn.ActualWidth);
                window.RightPanelToggle.IsChecked = true; RaiseClick(window.RightPanelToggle);
                window.UpdateLayout();
                Assert.InRange(window.RightPanelColumn.ActualWidth, 409, 411);
                Assert.Same(rows, window.BrowserGridRows.ItemsSource);
                Assert.True(tile!.IsSelected);
                element = FindElementByDataContext(window.BrowserGridRows, tile);
                RaiseMouseLeftButtonDown(element!, 2);
                await WaitUntilAsync(() => inspector.IsPlayerContext && inspector.TitleText.Text == tile.Name, "Player Inspector context");
                var player = Assert.IsType<PlayerViewerHost>(window.BrowserPlayerHost.Content);
                var subclipsTab = window.HomeRightPanel.SurfaceTabs.Items.Cast<TabItem>().Single(tab => Equals(tab.Tag, "subclips"));
                Assert.Equal(Visibility.Collapsed, subclipsTab.Visibility); // Still image context.
                var stillAsset = player.CurrentAsset!;
                var missingVideo = stillAsset with { Kind = MediaPresentationKind.Video };
                await player.OpenAsync(missingVideo, new(stillAsset.RootId, stillAsset.RelativePath,
                    stillAsset.RelativePath, null, MediaRootAvailability.Unavailable, false));
                Assert.Equal(Visibility.Visible, subclipsTab.Visibility);
                Assert.Same(player.SubclipsContent, subclipsTab.Content);
                window.HomeRightPanel.SelectSurface("subclips");
                Assert.Equal("subclips", window.HomeRightPanel.ActiveSurface);
                // Even unavailable media has its truthful Catalog context; a non-Catalog video does not.
                await player.OpenAsync(missingVideo with { AssetId = null }, new(stillAsset.RootId, stillAsset.RelativePath,
                    stillAsset.RelativePath, null, MediaRootAvailability.Unavailable, false));
                Assert.Equal(Visibility.Collapsed, subclipsTab.Visibility);
                Assert.Equal("inspector", window.HomeRightPanel.ActiveSurface);
                Assert.Equal("subclips", window.HomeRightPanel.PreferredSurface);
                window.HomeRightPanel.SelectSurface("inspector");
                window.OpenJobsPanel();
                window.Width = 1120; window.UpdateLayout();
                Assert.True(window.HomeRightPanel.IsVisible);
                Assert.Equal("jobs", window.HomeRightPanel.ActiveSurface);
                Assert.Same(player, window.BrowserPlayerHost.Content);
                AssertContained(window.HomeRightPanel, window.BrowserWorkspaceRoot);
                Assert.True(window.BrowserCenter.ActualWidth >= 200);
                window.HomeRightPanel.SelectSurface("inspector");
                RaiseClick(player.BackButton);
                await WaitUntilAsync(() => !inspector.IsPlayerContext, "return context");
                Assert.True(tile.IsSelected);
                Assert.Same(rows, window.BrowserGridRows.ItemsSource);
                window.Close();
                var saved = WorkspaceStateStore.Load(storage.Locations.WorkspaceStatePath);
                Assert.True(saved.Layout!.RightPanelOpen);
                Assert.Equal("inspector", saved.Layout.RightPanelActiveSurface);
                Assert.InRange(saved.Layout.RightPanelWidth!.Value, 409, 411);
            }
            finally { window.Close(); await storage.DisposeAsync(); }
        });
    }

    [Theory]
    [InlineData("browser", 0, false)]
    [InlineData("browser", 1, false)]
    [InlineData("browser", 2, false)]
    [InlineData("browser", 1, true)]
    [InlineData("close", 0, false)]
    [InlineData("close", 1, false)]
    [InlineData("close", 2, false)]
    [InlineData("close", 1, true)]
    [InlineData("asset", 0, false)]
    [InlineData("asset", 1, false)]
    [InlineData("asset", 2, false)]
    [InlineData("asset", 1, true)]
    public Task DirtyInspectorTransition_RealDialogResumesOrAbortsOriginalAction(string transition, int choice, bool fail) =>
        StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();
            CreateTestJpeg(Path.Combine(_mediaRoot, "second.jpg"));
            var startup = await LightflowStorageCoordinator.StartAsync(_appDataRoot);
            var storage = startup.Coordinator!;
            await storage.MediaRoots.CreateAsync("Library", _mediaRoot);
            var window = NewOffscreenWindow(storage, startup);
            window.Width = 1440;
            MediaInspectorView? inspector = null;
            try
            {
                window.Show();
                await WaitUntilAsync(() => window.BrowserFolderTree.Items.Count > 0, "storage");
                window.BrowserCurrentPath.Text = _mediaRoot; RaiseClick(window.BrowserGoButton);
                await WaitUntilAsync(() => window.BrowserLoadingOverlay.Visibility != Visibility.Visible && window.BrowserGridRows.Items.Count > 0, "media");
                var first = (await WaitForTileAsync(window))!;
                var second = window.BrowserGridRows.Items.Cast<BrowserGridRow>().SelectMany(row => row.Tiles)
                    .First(tile => tile.Key != first.Key);
                window.RightPanelToggle.IsChecked = true; RaiseClick(window.RightPanelToggle);
                inspector = Assert.IsType<MediaInspectorView>(((TabItem)window.HomeRightPanel.SurfaceTabs.SelectedItem).Content);
                RaiseMouseLeftButtonDown(FindElementByDataContext(window.BrowserGridRows, first)!, transition == "browser" ? 1 : 2);
                await WaitUntilAsync(() => inspector.IsPlayerContext == (transition != "browser") &&
                    inspector.DescriptionSection.DataContext is InspectorDescriptionEditor { CanEdit: true }, "editable context");
                var store = new TestDescriptionStore { Failure = fail ? new IOException("test disk full") : null };
                // Replace only persistence to inject a deterministic failed write; navigation and modal are real WPF.
                inspector.Initialize(() => new MediaInspectorService(storage.Previews, storage.AssetClassifications,
                    storage.Locations.PreviewsDirectory), store);
                var editor = (InspectorDescriptionEditor)inspector.DescriptionSection.DataContext;
                await editor.SetContextAsync([first.AssetId], transition != "browser");
                editor.Fields[0].Text = "draft";
                var dialogs = 0;
                inspector.ConfirmDescriptions = _ => throw new InvalidOperationException("Navigation must not open a second Apply confirmation.");
                inspector.ConfirmTransition = pending =>
                {
                    dialogs++;
                    window.Dispatcher.BeginInvoke(() =>
                    {
                        Assert.False(inspector.TryLeaveContext()); // Background refresh cannot nest another warning.
                        var dialog = Application.Current.Windows.OfType<ConfirmationDialog>().Single();
                        RaiseClick(choice == 0 ? dialog.CancelButton : choice == 1 ? dialog.ConfirmButton : dialog.DiscardButton);
                    });
                    return ConfirmationDialog.ConfirmTransition(window, pending);
                };
                var player = window.BrowserPlayerHost.Content as PlayerViewerHost;
                var original = player?.CurrentAsset;
                if (transition == "browser")
                    RaiseMouseLeftButtonDown(FindElementByDataContext(window.BrowserGridRows, second)!, 1);
                else if (transition == "close") RaiseClick(player!.BackButton);
                else await player!.OpenAsync(original! with { AssetId = second.AssetId, Name = second.Name, RelativePath = second.RelativePath },
                    new(second.RootId, second.RelativePath, second.Key, null, MediaRootAvailability.Unavailable, false));
                var continued = choice != 0 && !fail;
                await WaitUntilAsync(() => dialogs == 1 && (continued ? !editor.HasDraft : editor.HasDraft), "transition result");
                Assert.Equal(1, dialogs);
                if (transition == "browser") { Assert.Equal(continued, second.IsSelected); Assert.Equal(!continued, first.IsSelected); }
                else if (transition == "close") Assert.Equal(!continued, inspector.IsPlayerContext);
                else Assert.Equal(continued ? second.AssetId : first.AssetId, player!.CurrentAsset!.AssetId);
                if (!continued) { Assert.Same(editor, inspector.DescriptionSection.DataContext); Assert.Equal("draft", editor.Fields[0].Text); }
                if (choice == 1) Assert.Equal(first.AssetId, Assert.Single(store.AppliedTargets!).Key);
                else Assert.Null(store.AppliedPatch);
                if (fail) Assert.Contains("test disk full", editor.Status);
            }
            finally
            {
                if (inspector?.DescriptionSection.DataContext is InspectorDescriptionEditor editor)
                    await editor.ResolveTransitionAsync(DescriptionTransitionChoice.Discard);
                window.Close(); await storage.DisposeAsync();
            }
        });

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

                window.OpenJobsPanel();
                window.UpdateLayout();
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                window.UpdateLayout();
                Assert.Equal(Visibility.Visible, window.HomeRightPanel.Visibility);
                Assert.Equal("photo.jpg", host.CurrentAsset?.Name);
                Assert.Equal(4, Grid.GetRow(window.BrowserSelectionActionToolbar));
                AssertContained(window.BrowserPlayerHost, window.BrowserCenter);
                window.RightPanelColumn.Width = new GridLength(600);
                window.UpdateLayout();
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                window.UpdateLayout();
                Assert.Equal("photo.jpg", host.CurrentAsset?.Name);
                AssertContained(window.BrowserPlayerHost, window.BrowserCenter);
                window.RightPanelToggle.IsChecked = false;
                RaiseClick(window.RightPanelToggle);
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

    [Fact]
    public Task AssetDragBoundary_FirstPlayerPointerRoutesAndRepeatedEntriesNeverNeedPriming() =>
        WithDragBoundaryWindowAsync(async (window, tile) =>
        {
            var controls = new[] { "ExportButton", "PreviousFrameButton", "PlayPauseButton", "NextFrameButton",
                "ScreengrabButton", "SetPreviewFrameButton", "SetInButton", "SetOutButton" };
            var rows = window.BrowserGridRows.ItemsSource;
            var gridMoves = 0;
            window.BrowserGridRows.AddHandler(Mouse.PreviewMouseMoveEvent,
                new MouseEventHandler((_, _) => gridMoves++), true);
            foreach (var name in controls)
            {
                var border = FindElementByDataContext(window.BrowserGridRows, tile)!;
                RaiseMouseLeftButtonDown(border, 1);
                Assert.Same(tile, window.TakeBrowserAssetDrag(FarFromDragOrigin(window), MouseButtonState.Pressed));
                // Double-click arms another origin and hides the tile before its mouse-up arrives.
                RaiseMouseLeftButtonDown(border, 2);
                await WaitUntilAsync(() => GetPresentationMode(window) == BrowserPresentationMode.PlayerViewer, "Player");
                Assert.Null(DragField<BrowserGridTile?>(window, "_browserAssetDragTile"));
                Assert.Equal(default, DragField<Point>(window, "_browserAssetDragStart"));
                Assert.Null(DragField<BrowserGridTile?>(window, "_browserAssetPendingSingleSelection"));
                var player = Assert.IsType<PlayerViewerHost>(window.BrowserPlayerHost.Content);
                var control = Assert.IsAssignableFrom<UIElement>(player.FindName(name));
                // Routed input, not OS mouse injection. Actual video action effects are covered by the
                // Player lease/command tests; this still-image fixture exercises their shared input route.
                var down = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                    { RoutedEvent = Mouse.PreviewMouseDownEvent };
                control.RaiseEvent(down);
                Assert.False(down.Handled);
                for (var click = 0; click < 2; click++)
                {
                    var move = new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.PreviewMouseMoveEvent };
                    control.RaiseEvent(move);
                    Assert.False(move.Handled);
                    Assert.Null(window.TakeBrowserAssetDrag(new Point(10000, 10000), MouseButtonState.Pressed));
                }
                Assert.Equal(0, gridMoves);
                Assert.Null(DragField<object?>(window, "_fileDragAdorner"));
                RaiseClick(player.BackButton);
                await WaitUntilAsync(() => GetPresentationMode(window) == BrowserPresentationMode.Grid, "Browser");
                Assert.True(tile.IsSelected);
                Assert.Same(rows, window.BrowserGridRows.ItemsSource);
                Assert.False(window.BrowserGridRows.IsMouseCaptureWithin);
                RaiseMouseLeftButtonDown(border, 1);
                Assert.Same(tile, window.TakeBrowserAssetDrag(FarFromDragOrigin(window), MouseButtonState.Pressed));
            }
        });

    [Fact]
    public Task AssetDragBoundary_ThresholdReleaseHandledChromeAndAsyncGeneration() =>
        WithDragBoundaryWindowAsync((window, tile) =>
        {
            var border = FindElementByDataContext(window.BrowserGridRows, tile)!;
            RaiseMouseLeftButtonDown(border, 1);
            var origin = DragField<Point>(window, "_browserAssetDragStart");
            Assert.Null(window.TakeBrowserAssetDrag(origin, MouseButtonState.Pressed));
            Assert.Same(tile, window.TakeBrowserAssetDrag(FarFromDragOrigin(window), MouseButtonState.Pressed));
            var generation = DragField<long>(window, "_browserAssetGestureGeneration");
            Assert.True(window.IsBrowserAssetDragCurrent(generation, MouseButtonState.Pressed));
            Assert.False(window.IsBrowserAssetDragCurrent(generation, MouseButtonState.Released));
            Assert.Null(window.TakeBrowserAssetDrag(new Point(10000, 10000), MouseButtonState.Pressed));

            // A release handled outside the tile still invalidates a pending asynchronous drag.
            window.BrowserGoButton.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                { RoutedEvent = Mouse.MouseUpEvent, Handled = true });
            Assert.False(window.IsBrowserAssetDragCurrent(generation, MouseButtonState.Pressed));
            RaiseMouseLeftButtonDown(border, 1);
            Assert.Null(window.TakeBrowserAssetDrag(FarFromDragOrigin(window), MouseButtonState.Released));
            Assert.Null(window.TakeBrowserAssetDrag(new Point(10000, 10000), MouseButtonState.Pressed));

            // Even a fast round trip cannot revalidate source resolution started in the old presentation.
            RaiseMouseLeftButtonDown(border, 1);
            Assert.Same(tile, window.TakeBrowserAssetDrag(FarFromDragOrigin(window), MouseButtonState.Pressed));
            generation = DragField<long>(window, "_browserAssetGestureGeneration");
            var setMode = typeof(MainWindow).GetMethod("SetBrowserPresentationMode",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            for (var i = 0; i < 20; i++)
            {
                setMode.Invoke(window, [BrowserPresentationMode.PlayerViewer]);
                Assert.Null(window.TakeBrowserAssetDrag(new Point(10000, 10000), MouseButtonState.Pressed));
                setMode.Invoke(window, [BrowserPresentationMode.Grid]);
            }
            Assert.False(window.IsBrowserAssetDragCurrent(generation, MouseButtonState.Pressed));
            RaiseMouseLeftButtonDown(border, 1);
            window.BrowserGoButton.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                { RoutedEvent = Mouse.PreviewMouseDownEvent, Handled = true });
            Assert.Null(window.TakeBrowserAssetDrag(new Point(10000, 10000), MouseButtonState.Pressed));
            RaiseMouseLeftButtonDown(border, 1);
            Assert.Same(tile, window.TakeBrowserAssetDrag(FarFromDragOrigin(window), MouseButtonState.Pressed));
            Assert.True(tile.IsSelected);
            Assert.False(window.BrowserGridRows.IsMouseCaptureWithin);
            return Task.CompletedTask;
        });

    [Fact]
    public Task AssetDragBoundary_DeferredSelectionAndCaptureLossRetireOnlyGestureState() =>
        WithDragBoundaryWindowAsync((window, tile) =>
        {
            var grid = DragField<BrowserGridModel>(window, "_browserGrid");
            var other = grid.Tiles.First(candidate => candidate.Key != tile.Key);
            var border = FindElementByDataContext(window.BrowserGridRows, tile)!;
            RaiseMouseLeftButtonDown(border, 1);
            grid.ToggleCtrl(other.Index);
            RaiseMouseLeftButtonDown(border, 1);
            Assert.Same(tile, DragField<BrowserGridTile?>(window, "_browserAssetPendingSingleSelection"));
            Assert.Equal(2, grid.SelectedKeys.Count);
            // Real WPF MouseUp routing must commit the deferred selection before window cleanup.
            border.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                { RoutedEvent = Mouse.MouseUpEvent });
            Assert.Single(grid.SelectedKeys);
            Assert.True(tile.IsSelected);
            Assert.Null(DragField<BrowserGridTile?>(window, "_browserAssetPendingSingleSelection"));

            grid.ToggleCtrl(other.Index);
            RaiseMouseLeftButtonDown(border, 1);
            window.BrowserGridRows.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0)
                { RoutedEvent = Mouse.LostMouseCaptureEvent });
            Assert.Null(window.TakeBrowserAssetDrag(new Point(10000, 10000), MouseButtonState.Pressed));
            Assert.Null(DragField<BrowserGridTile?>(window, "_browserAssetPendingSingleSelection"));
            Assert.Equal(2, grid.SelectedKeys.Count);
            RaiseMouseLeftButtonDown(border, 1);
            Assert.Same(tile, window.TakeBrowserAssetDrag(FarFromDragOrigin(window), MouseButtonState.Pressed));
            Assert.Equal(2, BrowserAssetDragSelection.AssetIdsForDrag(tile.IsSelected, tile.AssetId,
                grid.SelectedAssetIdsInBrowserOrder).Count);
            return Task.CompletedTask;
        });

    private Task WithDragBoundaryWindowAsync(Func<MainWindow, BrowserGridTile, Task> test) =>
        StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();
            CreateTestJpeg(Path.Combine(_mediaRoot, "other.jpg"));
            var startup = await LightflowStorageCoordinator.StartAsync(_appDataRoot);
            var storage = startup.Coordinator!;
            await storage.MediaRoots.CreateAsync("Library", _mediaRoot);
            var window = NewOffscreenWindow(storage, startup);
            try
            {
                window.Show();
                await WaitUntilAsync(() => window.BrowserFolderTree.Items.Count > 0, "storage");
                window.BrowserCurrentPath.Text = _mediaRoot;
                RaiseClick(window.BrowserGoButton);
                await WaitUntilAsync(() => window.BrowserLoadingOverlay.Visibility != Visibility.Visible &&
                    window.BrowserGridRows.Items.Count > 0, "media");
                var tile = (await WaitForTileAsync(window))!;
                await test(window, tile);
            }
            finally { window.Close(); await storage.DisposeAsync(); }
        });

    private static T DragField<T>(MainWindow window, string name) => (T)typeof(MainWindow)
        .GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(window)!;

    private static Point FarFromDragOrigin(MainWindow window) =>
        DragField<Point>(window, "_browserAssetDragStart") + new Vector(
            SystemParameters.MinimumHorizontalDragDistance + 1, SystemParameters.MinimumVerticalDragDistance + 1);

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
