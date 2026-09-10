using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class BrowserReviewSetTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Capture_UsesOrderedResultsOrSelectedSubset_AndRemainsStable(bool multiple)
    {
        var tiles = new[] { Tile("z.mp4"), Tile("a.jpg", MediaTypeCategory.StillImage), Tile("m.mp4") };
        tiles[0].IsSelected = true;
        tiles[2].IsSelected = multiple;
        var current = Asset(tiles[0]);
        var review = BrowserPlayerReviewSet.Capture(tiles, current);
        Assert.Equal(multiple, review.IsSelectionSubset);
        Assert.Equal(multiple ? new[] { "z.mp4", "m.mp4" } : new[] { "z.mp4", "a.jpg", "m.mp4" }, review.Items.Select(item => item.Asset.Name));
        Assert.Same(tiles[0], review.Items[0].Preview);
        tiles[0].ThumbnailPath = "updated-preview.jpg";
        tiles[0].IsSelected = false;
        Array.Reverse(tiles);
        Assert.Equal("z.mp4", review.Items[0].Asset.Name);
        Assert.False(review.CanPrevious);
        Assert.True(review.CanNext);
        review.Select(review.Items[^1].Asset.AssetId!.Value);
        Assert.False(review.CanNext);
    }

    [Fact]
    public void Capture_UsesExistingSearchFilterAndManualCollectionOrder()
    {
        var tiles = new[] { Tile("keep-z.mp4"), Tile("other.mp4"), Tile("keep-a.jpg", MediaTypeCategory.StillImage), Tile("keep-b.mp4") };
        var ordered = BrowserQueryEngine.Apply(tiles, BrowserQuery.Default with { SearchText = "keep", SortMode = BrowserSortMode.Manual,
            Filters = [BrowserFilterPredicate.ForMediaType(MediaTypeCategory.Video)] });
        var review = BrowserPlayerReviewSet.Capture(ordered, Asset(ordered[0]));
        Assert.Equal(new[] { "keep-z.mp4", "keep-b.mp4" }, review.Items.Select(item => item.Asset.Name));
        Assert.DoesNotContain(review.Items, item => item.Asset.Name == "other.mp4");
    }

    [Fact]
    public void Capture_MixedSelectionDoesNotExpandWhenOnlyOneSelectedItemIsCompatible()
    {
        var tiles = new[] { Tile("selected.mp4"), Tile("selected.wav", MediaTypeCategory.Audio),
            Tile("unselected.jpg", MediaTypeCategory.StillImage), Tile("unselected.raw", MediaTypeCategory.RawImage) };
        tiles[0].IsSelected = tiles[1].IsSelected = true;
        var review = BrowserPlayerReviewSet.Capture(tiles, Asset(tiles[0]));
        Assert.True(review.IsSelectionSubset);
        Assert.Equal("selected.mp4", Assert.Single(review.Items).Asset.Name);
        tiles[1].IsSelected = false;
        review = BrowserPlayerReviewSet.Capture(tiles, Asset(tiles[0]));
        Assert.Equal(new[] { "selected.mp4", "unselected.jpg", "unselected.raw" }, review.Items.Select(item => item.Asset.Name));
    }

    [Fact]
    public void Visibility_RoundTripsInExistingLayoutWithoutDroppingOtherState()
    {
        var state = new WorkspaceStateService("unused.json", new() { Layout = new() { RightPanelOpen = true } });
        state.SetPlayerFilmstripVisible(false);
        state.SetBrowserLocationsPaneWidth(300);
        var restored = WorkspaceState.Normalize(System.Text.Json.JsonSerializer.Deserialize<WorkspaceState>(System.Text.Json.JsonSerializer.Serialize(state.Current)));
        Assert.False(restored.Layout!.PlayerFilmstripVisible);
        Assert.True(restored.Layout.RightPanelOpen);
        Assert.True(new WorkspaceLayoutState().PlayerFilmstripVisible);
    }

    private static BrowserGridTile Tile(string name, MediaTypeCategory category = MediaTypeCategory.Video) =>
        new(new(Guid.NewGuid(), name, name, name, false, new(category), 10, DateTimeOffset.UnixEpoch, AssetId: Guid.NewGuid()), 0);
    private static PlayerViewerAsset Asset(BrowserGridTile tile) => new(tile.RootId, tile.RelativePath, tile.Key, tile.Name,
        MediaPresentationClassification.KindFor(tile.Category), tile.AssetId);
}

public sealed partial class PlayerViewerHostLeaseTests
{
    private static PlayerViewerAsset ReviewAsset(string name) => new(Guid.NewGuid(), name, name, name, MediaPresentationKind.Video, Guid.NewGuid());
    private static MediaPathResolution ReviewPath(PlayerViewerAsset asset) => new(asset.RootId, asset.RelativePath, asset.Key,
        Path.GetFullPath(asset.Name), MediaRootAvailability.Online, true);

    [Fact]
    public async Task Filmstrip_AllNavigationSharesPausedInLifecycle_AndHiddenKeyboardKeepsSelection()
    {
        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();
            var backend = new FakeBackend();
            var ranges = new FakeRangeStore(new(TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(7), TimeSpan.FromSeconds(40)));
            await using var coordinator = new MediaPlaybackCoordinator(() => new MediaPlaybackService(backend));
            var host = new PlayerViewerHost(coordinator, ranges);
            var assets = new[] { ReviewAsset("a.mp4"), ReviewAsset("b.mp4"), ReviewAsset("c.mp4") };
            var review = new PlayerReviewSet(assets.Select(a => new PlayerReviewItem(a, null)).ToArray(), assets[0].AssetId);
            host.SetReviewSet(review, (a, _) => Task.FromResult(ReviewPath(a)));
            await host.OpenAsync(assets[0], ReviewPath(assets[0]), continuation: new() { Asset = assets[0], Position = TimeSpan.FromSeconds(25) });
            host.TryHandleShortcut(System.Windows.Input.Key.Space, host);
            await WaitUntilAsync(() => backend.PlayCallCount == 1, "playing source");
            host.FilmstripVisible = false;
            Assert.True(host.TryHandleShortcut(System.Windows.Input.Key.Right, host, ModifierKeys.Control));
            await WaitUntilAsync(() => host.CurrentAsset == assets[1] && host.PositionSlider.IsEnabled, "next at In");
            Assert.Equal(TimeSpan.FromSeconds(7), backend.SeekPositions[^1]);
            Assert.True(backend.PauseCallCount > 0);
            Assert.Equal(1, backend.PlayCallCount);
            Assert.Equal(0, ranges.SaveCount);
            Assert.False(host.TryHandleShortcut(System.Windows.Input.Key.Left, new TextBox(), ModifierKeys.Control));
            Assert.Equal(1, review.CurrentIndex);
            host.FilmstripVisible = true;
            Assert.Same(review, host.ReviewSet);
            host.Filmstrip.SelectedIndex = 2;
            await WaitUntilAsync(() => host.CurrentAsset == assets[2] && host.PositionSlider.IsEnabled, "thumbnail source");
            Assert.False(host.NextAssetButton.IsEnabled);
            await host.TraverseReviewAsync(1);
            Assert.Equal(assets[2], host.CurrentAsset);
            host.PreviousAssetButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            await WaitUntilAsync(() => host.CurrentAsset == assets[1] && host.PositionSlider.IsEnabled, "previous button");
            Assert.True(host.TryHandleShortcut(System.Windows.Input.Key.Left, host.Filmstrip, ModifierKeys.None));
            await WaitUntilAsync(() => backend.Operations.Contains("backward"), "frame step");
            Assert.Equal(1, review.CurrentIndex);
            Assert.True(host.TryHandleShortcut(System.Windows.Input.Key.Left, host.Filmstrip, ModifierKeys.Control));
            await WaitUntilAsync(() => host.CurrentAsset == assets[0] && host.PositionSlider.IsEnabled, "previous keyboard");
            Assert.False(host.PreviousAssetButton.IsEnabled);
            await host.TraverseReviewAsync(-1);
            Assert.Equal(assets[0], host.CurrentAsset);
            await host.CloseAsync();
        });
    }

    [Fact]
    public async Task Filmstrip_RapidTraversalObsoletesDelayedResolution_AndMissingMemberCanBeLeft()
    {
        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();
            var backend = new FakeBackend();
            await using var coordinator = new MediaPlaybackCoordinator(() => new MediaPlaybackService(backend));
            var host = new PlayerViewerHost(coordinator);
            var assets = new[] { ReviewAsset("a.mp4"), ReviewAsset("b.mp4"), ReviewAsset("missing.mp4"), ReviewAsset("d.mp4") };
            var delayed = new TaskCompletionSource<MediaPathResolution>();
            host.SetReviewSet(new(assets.Select(a => new PlayerReviewItem(a, null)).ToArray(), assets[0].AssetId), (a, _) =>
                a == assets[1] ? delayed.Task : Task.FromResult(a == assets[2] ? ReviewPath(a) with { Exists = false } : ReviewPath(a)));
            await host.OpenAsync(assets[0], ReviewPath(assets[0]));
            var obsolete = host.TraverseReviewAsync(1);
            await host.TraverseReviewAsync(1);
            Assert.Equal(assets[2], host.CurrentAsset);
            Assert.False(host.PositionSlider.IsEnabled);
            await host.TraverseReviewAsync(1);
            delayed.SetResult(ReviewPath(assets[1]));
            await obsolete;
            Assert.Equal(assets[3], host.CurrentAsset);
            Assert.True(host.PositionSlider.IsEnabled);
            Assert.Equal(TimeSpan.Zero, host.CaptureWorkspaceState()!.Position);
            Assert.Equal(0, backend.PlayCallCount);
            await host.CloseAsync();
        });
    }

    [Fact]
    public async Task Filmstrip_StaleSourceRangeLoadCannotReplaceLatestOrSeekItsPlayback()
    {
        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();
            var backend = new FakeBackend();
            await using var coordinator = new MediaPlaybackCoordinator(() => new MediaPlaybackService(backend));
            var assets = new[] { ReviewAsset("a.mp4"), ReviewAsset("b.mp4"), ReviewAsset("d.mp4") };
            var rangeStore = new DelayedReviewRangeStore(assets[1].AssetId!.Value);
            var host = new PlayerViewerHost(coordinator, rangeStore);
            host.SetReviewSet(new(assets.Select(a => new PlayerReviewItem(a, null)).ToArray(), assets[0].AssetId),
                (a, _) => Task.FromResult(ReviewPath(a)));
            await host.OpenAsync(assets[0], ReviewPath(assets[0]));
            var obsolete = host.TraverseReviewAsync(1);
            await rangeStore.Entered.Task;
            await host.TraverseReviewAsync(1);
            rangeStore.Completion.SetResult(new(TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(39), null));
            await obsolete;
            Assert.Equal(assets[2], host.CurrentAsset);
            Assert.Equal(TimeSpan.FromSeconds(4), host.CaptureWorkspaceState()!.Position);
            Assert.DoesNotContain(TimeSpan.FromSeconds(39), backend.SeekPositions);
            Assert.True(host.PositionSlider.IsEnabled);
            Assert.Equal(0, backend.PlayCallCount);
            await host.CloseAsync();
        });
    }

    [Fact]
    public async Task Filmstrip_RepeatedSwitchesRestoreOwnRangesAndReleasePresentation()
    {
        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();
            var backend = new FakeBackend();
            await using var service = new MediaPlaybackService(backend);
            await using var coordinator = new MediaPlaybackCoordinator(() => service);
            var assets = new[] { ReviewAsset("a.mp4"), ReviewAsset("b.mp4"), ReviewAsset("default.mp4") };
            var ranges = new ReviewRanges(new Dictionary<Guid, MediaRange?>
            {
                [assets[0].AssetId!.Value] = new(TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(20)),
                [assets[1].AssetId!.Value] = new(TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(13), TimeSpan.FromSeconds(45)),
                [assets[2].AssetId!.Value] = null
            });
            var host = new PlayerViewerHost(coordinator, ranges);
            host.SetReviewSet(new(assets.Select(a => new PlayerReviewItem(a, null)).ToArray(), assets[0].AssetId),
                (a, _) => Task.FromResult(ReviewPath(a)));
            await host.OpenAsync(assets[0], ReviewPath(assets[0]));
            for (var cycle = 0; cycle < 3; cycle++)
            {
                await service.SeekAsync(TimeSpan.FromSeconds(32));
                await service.PlayAsync();
                foreach (var index in new[] { 1, 2, 0 })
                {
                    await host.SelectReviewAssetAsync(assets[index].AssetId!.Value);
                    Assert.Equal(MediaPlaybackState.Paused, service.Snapshot.State);
                    Assert.Equal(TimeSpan.FromSeconds(index == 0 ? 5 : index == 1 ? 13 : 0), host.CaptureWorkspaceState()!.Position);
                    Assert.Single(host.VideoHost.Children);
                    var range = (MediaRange?)typeof(PlayerViewerHost).GetField("_reviewRange",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(host);
                    Assert.Equal(ranges.Values[assets[index].AssetId!.Value], range);
                }
            }
            Assert.Equal(0, ranges.Saves);
            await host.CloseAsync();
            Assert.Empty(host.VideoHost.Children);
            Assert.Null(host.ReviewSet);
            var position = host.PositionSlider.Value;
            backend.Present(59);
            await host.Dispatcher.InvokeAsync(() => { });
            Assert.Equal(position, host.PositionSlider.Value);
            Assert.Null(host.CurrentAsset);
        });
    }

    private sealed class ReviewRanges(Dictionary<Guid, MediaRange?> values) : IMediaRangeStore
    {
        public Dictionary<Guid, MediaRange?> Values => values;
        public int Saves { get; private set; }
        public Task<MediaRange?> RestoreAsync(Guid assetId, CancellationToken cancellationToken = default) => Task.FromResult(values[assetId]);
        public Task SaveAsync(Guid assetId, MediaRange? range, CancellationToken cancellationToken = default) { Saves++; return Task.CompletedTask; }
    }

    private sealed class DelayedReviewRangeStore(Guid delayedId) : IMediaRangeStore
    {
        internal TaskCompletionSource Entered { get; } = new();
        internal TaskCompletionSource<MediaRange?> Completion { get; } = new();
        public Task<MediaRange?> RestoreAsync(Guid assetId, CancellationToken cancellationToken = default)
        {
            if (assetId == delayedId) { Entered.TrySetResult(); return Completion.Task; }
            return Task.FromResult<MediaRange?>(new(TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(4), null));
        }
        public Task SaveAsync(Guid assetId, MediaRange? range, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Traversal must not save ranges.");
    }

    [Fact]
    public async Task Filmstrip_LargeSetVirtualizesAndRevealsCurrentAfterShowing()
    {
        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();
            await using var coordinator = new MediaPlaybackCoordinator(() => new MediaPlaybackService(new FakeBackend()));
            var host = new PlayerViewerHost(coordinator);
            var assets = Enumerable.Range(0, 10000).Select(i => ReviewAsset($"{i}.mp4")).ToArray();
            host.SetReviewSet(new(assets.Select(a => new PlayerReviewItem(a, null)).ToArray(), assets[0].AssetId),
                (a, _) => Task.FromResult(ReviewPath(a) with { Exists = false }));
            var window = new Window { Content = host, Width = 900, Height = 700, ShowInTaskbar = false };
            try
            {
                window.Show();
                host.FilmstripVisible = false;
                await host.SelectReviewAssetAsync(assets[^1].AssetId!.Value);
                Assert.Equal(Visibility.Collapsed, host.Filmstrip.Visibility);
                Assert.Equal(Visibility.Collapsed, host.FilmstripChrome.Visibility);
                Assert.True(host.StillFilmstripToggle.IsVisible);
                Assert.False(host.StillFilmstripToggle.IsChecked);
                host.FilmstripVisible = true;
                await host.Dispatcher.InvokeAsync(() => host.UpdateLayout(), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                Assert.True(host.PreviousAssetButton.TransformToAncestor(host).Transform(new System.Windows.Point()).X <
                    host.Filmstrip.TransformToAncestor(host).Transform(new System.Windows.Point()).X);
                Assert.True(host.NextAssetButton.TransformToAncestor(host).Transform(new System.Windows.Point()).X >=
                    host.Filmstrip.TransformToAncestor(host).Transform(new System.Windows.Point(host.Filmstrip.ActualWidth, 0)).X);
                Assert.NotNull(host.Filmstrip.ItemContainerGenerator.ContainerFromIndex(9999));
                Assert.Null(host.Filmstrip.ItemContainerGenerator.ContainerFromIndex(0));
                Assert.True(Enumerable.Range(0, assets.Length).Count(i => host.Filmstrip.ItemContainerGenerator.ContainerFromIndex(i) is not null) < 100);
            }
            finally { await host.CloseAsync(); window.Close(); }
        });
    }
}

