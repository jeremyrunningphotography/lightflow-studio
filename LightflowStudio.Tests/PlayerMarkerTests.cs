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
            Assert.Single(host.MarkerChoice.Items);
            await host.SeekMarkerAsync(marker);
            Assert.Equal(ticks, backend.SeekPositions[^1]);
            host.AddMarkerButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            await WaitUntilAsync(() => host.MarkerControls.IsEnabled, "marker add");
            Assert.Single(markers.Items);
            host.MarkerName.Text = "Named point";
            host.RenameMarkerButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            await WaitUntilAsync(() => markers.Items.Single().Name == "Named point", "marker rename");
            Assert.Equal(marker.MarkerId, markers.Items.Single().MarkerId);
            Assert.Equal(0, ranges.SaveCount);
            Assert.Single(host.MarkerTrack.Children);
            host.RemoveMarkerButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            await WaitUntilAsync(() => host.MarkerChoice.Items.Count == 0, "marker removal");
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
            Assert.Equal(newMarker, Assert.Single(host.MarkerChoice.Items.Cast<TimelineMarker>()));
            var seeks = backend.SeekPositions.Count;
            await host.SeekMarkerAsync(oldMarker);
            Assert.Equal(seeks, backend.SeekPositions.Count);
            await host.CloseAsync();
            Assert.Empty(host.MarkerChoice.Items);
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
            Assert.Single(host.MarkerChoice.Items);
            Assert.False(host.AddMarkerButton.IsEnabled);
            await host.SeekMarkerAsync(marker);
            Assert.Empty(backend.SeekPositions);
            await host.CloseAsync();
        });
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
