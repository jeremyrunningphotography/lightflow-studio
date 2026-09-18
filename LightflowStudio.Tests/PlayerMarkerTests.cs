using System.Windows;
using System.Windows.Controls.Primitives;
using Xunit;

namespace LightflowStudio.Tests;

public sealed partial class PlayerViewerHostLeaseTests
{
    [Fact]
    public async Task Markers_DiamondCentersAlignWithFullSourceTrackEndpoints()
    {
        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();
            var markers = new FakeMarkers(); var asset = ReviewAsset("alignment.mp4");
            foreach (var seconds in new[] { 0, 30, 60 })
                await markers.CreateAsync(asset.AssetId!.Value, TimeSpan.FromSeconds(seconds));
            await using var coordinator = new MediaPlaybackCoordinator(() => new MediaPlaybackService(new FakeBackend()));
            var host = new PlayerViewerHost(coordinator, markers: markers);
            await host.OpenAsync(asset, ReviewPath(asset));
            host.Measure(new Size(900, 700)); host.Arrange(new Rect(0, 0, 900, 700)); host.UpdateLayout();
            await host.Dispatcher.InvokeAsync(() => { });
            var buttons = host.MarkerTrack.Children.Cast<FrameworkElement>().ToArray();
            Assert.Equal(3, buttons.Length);
            Assert.True(host.MarkerTrack.ActualWidth > 0);
            Assert.Equal(0, System.Windows.Controls.Canvas.GetLeft(buttons[0]) + buttons[0].Width / 2);
            Assert.Equal(host.MarkerTrack.ActualWidth / 2, System.Windows.Controls.Canvas.GetLeft(buttons[1]) + buttons[1].Width / 2);
            Assert.Equal(host.MarkerTrack.ActualWidth, System.Windows.Controls.Canvas.GetLeft(buttons[2]) + buttons[2].Width / 2);
            await host.CloseAsync();
        });
    }

    [Fact]
    public async Task Markers_ControlsUseExactSharedSeekAndDoNotWriteReviewRanges()
    {
        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();
            var backend = new FakeBackend();
            var markers = new FakeMarkers();
            var ranges = new FakeRangeStore(null);
            await using var coordinator = new MediaPlaybackCoordinator(() => new MediaPlaybackService(backend));
            var host = new PlayerViewerHost(coordinator, ranges, markers: markers);
            var asset = ReviewAsset("markers.mp4");
            await host.OpenAsync(asset, ReviewPath(asset));
            var ticks = TimeSpan.FromTicks(123456789);
            var marker = (await markers.CreateAsync(asset.AssetId!.Value, ticks)).Marker;
            await host.OpenAsync(asset, ReviewPath(asset));
            Assert.Single(host.CurrentMarkers);
            await host.SeekMarkerAsync(marker);
            Assert.Equal(ticks, backend.SeekPositions[^1]);
            host.AddMarkerButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            await WaitUntilAsync(() => host.MarkerTransport.IsEnabled, "marker add");
            Assert.Single(markers.Items);
            await host.RenameMarkerAsync(marker, "Named point");
            await WaitUntilAsync(() => markers.Items.Single().Name == "Named point", "marker rename");
            Assert.Equal(marker.MarkerId, markers.Items.Single().MarkerId);
            Assert.Equal(0, ranges.SaveCount);
            Assert.Single(host.MarkerTrack.Children);
            await host.ClearMarkerAsync(markers.Items.Single());
            await WaitUntilAsync(() => host.CurrentMarkers.Count == 0, "marker removal");
            await host.CloseAsync();
            Assert.Empty(host.MarkerTrack.Children);
        });
    }

    [Fact]
    public async Task Markers_DelayedOldLoadAndForeignMarkerCannotAffectNewSource()
    {
        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();
            var backend = new FakeBackend();
            var markers = new FakeMarkers();
            var first = ReviewAsset("first.mp4"); var second = ReviewAsset("second.mp4");
            var oldMarker = (await markers.CreateAsync(first.AssetId!.Value, TimeSpan.FromTicks(123))).Marker;
            var newMarker = (await markers.CreateAsync(second.AssetId!.Value, TimeSpan.FromTicks(456))).Marker;
            markers.DelayAsset = first.AssetId;
            await using var coordinator = new MediaPlaybackCoordinator(() => new MediaPlaybackService(backend));
            var host = new PlayerViewerHost(coordinator, markers: markers);
            var oldOpen = host.OpenAsync(first, ReviewPath(first));
            await markers.Entered.Task;
            await host.OpenAsync(second, ReviewPath(second));
            markers.Delayed.SetResult([oldMarker]);
            await oldOpen;
            Assert.Equal(second, host.CurrentAsset);
            Assert.Equal(newMarker, Assert.Single(host.CurrentMarkers));
            var seeks = backend.SeekPositions.Count;
            await host.SeekMarkerAsync(oldMarker);
            Assert.Equal(seeks, backend.SeekPositions.Count);
            await host.CloseAsync();
            Assert.Empty(host.CurrentMarkers);
        });
    }

    [Fact]
    public async Task Markers_OfflineSourceLoadsAnnotationsWithoutAttemptingPlayback()
    {
        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();
            var backend = new FakeBackend(); var markers = new FakeMarkers();
            var asset = ReviewAsset("offline.mp4");
            var marker = (await markers.CreateAsync(asset.AssetId!.Value, TimeSpan.FromTicks(123))).Marker;
            await using var coordinator = new MediaPlaybackCoordinator(() => new MediaPlaybackService(backend));
            var host = new PlayerViewerHost(coordinator, markers: markers);
            await host.OpenAsync(asset, ReviewPath(asset) with { Exists = false });
            Assert.Single(host.CurrentMarkers);
            Assert.False(host.AddMarkerButton.IsEnabled);
            await host.SeekMarkerAsync(marker);
            Assert.Empty(backend.SeekPositions);
            await host.CloseAsync();
        });
    }


    [Fact]
    public async Task Markers_ShortcutsPreserveFrameSteppingAndTextEntry()
    {
        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();
            var backend = new FakeBackend(); var markers = new FakeMarkers();
            var asset = ReviewAsset("shortcuts.mp4");
            await markers.CreateAsync(asset.AssetId!.Value, TimeSpan.FromSeconds(10));
            await markers.CreateAsync(asset.AssetId.Value, TimeSpan.FromSeconds(20));
            await using var coordinator = new MediaPlaybackCoordinator(() => new MediaPlaybackService(backend));
            var host = new PlayerViewerHost(coordinator, markers: markers);
            await host.OpenAsync(asset, ReviewPath(asset));
            Assert.True(host.TryHandleShortcut(System.Windows.Input.Key.M, host, System.Windows.Input.ModifierKeys.None));
            await WaitUntilAsync(() => markers.Items.Count == 3 && host.MarkerTransport.IsEnabled, "M adds marker");
            foreach (var key in new[] { System.Windows.Input.Key.M, System.Windows.Input.Key.Left, System.Windows.Input.Key.Right })
                foreach (var modifier in new[] { System.Windows.Input.ModifierKeys.None, System.Windows.Input.ModifierKeys.Alt })
                    Assert.False(host.TryHandleShortcut(key, new System.Windows.Controls.TextBox(), modifier));
            Assert.False(host.TryHandleShortcut(System.Windows.Input.Key.M, host, System.Windows.Input.ModifierKeys.Control));
            Assert.True(host.TryHandleShortcut(System.Windows.Input.Key.Right, host, System.Windows.Input.ModifierKeys.Alt));
            await WaitUntilAsync(() => backend.SeekPositions.Last() == TimeSpan.FromSeconds(10), "next marker");
            Assert.True(host.TryHandleShortcut(System.Windows.Input.Key.Left, host, System.Windows.Input.ModifierKeys.Alt));
            await WaitUntilAsync(() => backend.SeekPositions.Last() == TimeSpan.Zero, "previous marker");
            Assert.True(host.TryHandleShortcut(System.Windows.Input.Key.Right, host, System.Windows.Input.ModifierKeys.None));
            await WaitUntilAsync(() => backend.Operations.Contains("forward"), "plain right frame");
            Assert.True(host.TryHandleShortcut(System.Windows.Input.Key.Left, host, System.Windows.Input.ModifierKeys.None));
            await WaitUntilAsync(() => backend.Operations.Contains("backward"), "plain left frame");
            await host.CloseAsync();
        });
    }

    [Fact]
    public async Task Markers_DiamondAndTimelineMenusHaveDistinctActionsAndExactSeek()
    {
        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();
            var backend = new FakeBackend(); var markers = new FakeMarkers(); var asset = ReviewAsset("menus.mp4");
            var unnamed = (await markers.CreateAsync(asset.AssetId!.Value, TimeSpan.FromTicks(123456789))).Marker;
            var named = (await markers.CreateAsync(asset.AssetId.Value, TimeSpan.FromSeconds(20))).Marker;
            await markers.RenameAsync(named.MarkerId, named.Revision, "Good take");
            await using var coordinator = new MediaPlaybackCoordinator(() => new MediaPlaybackService(backend));
            var host = new PlayerViewerHost(coordinator, markers: markers);
            await host.OpenAsync(asset, ReviewPath(asset));
            var diamond = (System.Windows.Controls.Button)host.MarkerTrack.Children[0];
            Assert.Equal(new[] { "Rename Marker", "Clear Marker" }, diamond.ContextMenu.Items.Cast<System.Windows.Controls.MenuItem>().Select(m => m.Header));
            diamond.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            await WaitUntilAsync(() => host.SelectedMarkerId == unnamed.MarkerId && backend.SeekPositions.Last() == unnamed.Position, "diamond seek");
            var menu = host.BuildTimelineMenu();
            Assert.Equal(new[] { "Add Marker", "Go to Marker", "Go to In", "Go to Out" }, menu.Items.Cast<System.Windows.Controls.MenuItem>().Select(m => m.Header));
            var go = (System.Windows.Controls.MenuItem)menu.Items[1];
            Assert.Equal(new[] { unnamed.PositionLabel, "Good take" }, go.Items.Cast<System.Windows.Controls.MenuItem>().Select(m => m.Header));
            ((System.Windows.Controls.MenuItem)go.Items[1]).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent));
            await WaitUntilAsync(() => backend.SeekPositions.Last() == named.Position, "submenu seek");
            ((System.Windows.Controls.MenuItem)diamond.ContextMenu.Items[1]).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent));
            await WaitUntilAsync(() => markers.Items.Count == 1, "clear menu");
            await host.CloseAsync();
        });
    }

    [Fact]
    public async Task Markers_TransportUsesIconsAndFilmstripFollowsVolume()
    {
        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();
            await using var coordinator = new MediaPlaybackCoordinator(() => new MediaPlaybackService(new FakeBackend()));
            var host = new PlayerViewerHost(coordinator, markers: new FakeMarkers());
            Assert.IsType<System.Windows.Shapes.Path>(host.AddMarkerButton.Content);
            Assert.IsType<System.Windows.Shapes.Path>(host.PreviousMarkerButton.Content);
            Assert.IsType<System.Windows.Shapes.Path>(host.NextMarkerButton.Content);
            Assert.Null(host.FindName("MarkerChoice"));
            var right = Assert.IsType<System.Windows.Controls.StackPanel>(host.VolumeSlider.Parent);
            Assert.Same(right, host.FilmstripToggle.Parent);
            Assert.Equal(right.Children.IndexOf(host.VolumeSlider) + 1, right.Children.IndexOf(host.FilmstripToggle));
            host.FilmstripVisible = true;
            host.FilmstripToggle.IsChecked = false;
            host.FilmstripToggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.False(host.FilmstripVisible);
            host.FilmstripToggle.IsChecked = true;
            host.FilmstripToggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.True(host.FilmstripVisible);
            await host.CloseAsync();
        });
    }


    [Fact]
    public async Task Markers_InspectorEditsWhileThumbnailsPendingAndHandlesUnavailable()
    {
        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();
            var markers = new FakeMarkers(); var asset = ReviewAsset("inspector.mp4");
            var marker = (await markers.CreateAsync(asset.AssetId!.Value, TimeSpan.FromTicks(123456789))).Marker;
            var thumbnails = new DelayedMarkerThumbnails();
            using var view = new MediaInspectorView();
            view.InitializeMarkers(markers, thumbnails);
            view.SetContext([new(asset.AssetId.Value, asset.Name, asset.RelativePath, asset.Kind)], true);
            view.PresentMarkers([marker], default);
            var card = Assert.Single(view.MarkerCards);
            TimelineMarker? sought = null;
            view.SeekMarker = value => { sought = value; return Task.CompletedTask; };
            view.MarkerSection.Visibility = Visibility.Visible;
            view.Measure(new Size(400, 900)); view.Arrange(new Rect(0, 0, 400, 900)); view.UpdateLayout();
            var button = MarkerVisuals(view.InspectorMarkers).OfType<System.Windows.Controls.Button>().First();
            button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.Equal(marker.MarkerId, sought?.MarkerId);
            Assert.True(card.CanEdit);
            Assert.Null(card.Thumbnail);
            Assert.Contains("Loading", card.ThumbnailStatus);
            card.DraftName = "Named while loading";
            await view.SaveMarkerNameAsync(card);
            Assert.Equal("Named while loading", markers.Items.Single().Name);
            Assert.Equal(2, card.Marker.Revision);
            thumbnails.Completion.SetResult(null);
            await WaitUntilAsync(() => card.ThumbnailStatus.Contains("unavailable"), "unavailable frame");
            Assert.True(card.CanEdit);
            Assert.Equal(marker.Position, card.Marker.Position);
            card.DraftName = "";
            await view.SaveMarkerNameAsync(card);
            Assert.Empty(markers.Items.Single().Name);
            await view.ClearInspectorMarkerAsync(card);
            Assert.Empty(markers.Items);
        });
    }
    private static IEnumerable<DependencyObject> MarkerVisuals(DependencyObject parent)
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var descendant in MarkerVisuals(child)) yield return descendant;
        }
    }
    private sealed class DelayedMarkerThumbnails : IMarkerThumbnailService
    {
        public TaskCompletionSource<string?> Completion { get; } = new();
        public Task<string?> GetAsync(TimelineMarker marker, CancellationToken token) => Completion.Task;
        public void Dispose() { }
    }

    private sealed class FakeMarkers : IMarkerService
    {
        public List<TimelineMarker> Items { get; } = [];
        public Guid? DelayAsset { get; set; }
        public TaskCompletionSource Entered { get; } = new();
        public TaskCompletionSource<IReadOnlyList<TimelineMarker>> Delayed { get; } = new();
        public Task<IReadOnlyList<TimelineMarker>> ListAsync(Guid assetId, CancellationToken token = default)
        {
            if (DelayAsset == assetId) { Entered.TrySetResult(); return Delayed.Task; }
            return Task.FromResult<IReadOnlyList<TimelineMarker>>(Items.Where(m => m.AssetId == assetId).OrderBy(m => m.Position).ToArray());
        }
        public Task<MarkerCreateResult> CreateAsync(Guid assetId, TimeSpan position, CancellationToken token = default)
        {
            var existing = Items.FirstOrDefault(m => m.AssetId == assetId && m.Position == position);
            if (existing is not null) return Task.FromResult(new MarkerCreateResult(existing, false));
            var marker = new TimelineMarker(Guid.NewGuid(), assetId, position, "", 1, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
            Items.Add(marker); return Task.FromResult(new MarkerCreateResult(marker, true));
        }
        public Task RenameAsync(Guid markerId, long revision, string name, CancellationToken token = default)
        {
            var index = Items.FindIndex(m => m.MarkerId == markerId && m.Revision == revision);
            if (index < 0) throw new MarkerConcurrencyException();
            Items[index] = Items[index] with { Name = name, Revision = revision + 1 }; return Task.CompletedTask;
        }
        public Task DeleteAsync(Guid markerId, long revision, CancellationToken token = default)
        {
            if (Items.RemoveAll(m => m.MarkerId == markerId && m.Revision == revision) != 1) throw new MarkerConcurrencyException();
            return Task.CompletedTask;
        }
        public Task<IReadOnlyDictionary<Guid, MarkerSummary>> SummariesAsync(IReadOnlyCollection<Guid> assets, CancellationToken token = default) =>
            Task.FromResult<IReadOnlyDictionary<Guid, MarkerSummary>>(assets.Distinct().ToDictionary(id => id, id => new MarkerSummary(Items.Count(m => m.AssetId == id))));
    }
}
