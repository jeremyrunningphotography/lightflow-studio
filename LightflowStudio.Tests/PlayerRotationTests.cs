using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Xunit;

namespace LightflowStudio.Tests;

public sealed partial class PlayerViewerHostLeaseTests
{
    [Fact]
    public async Task RotateCurrent_UsesSamePlaybackAndTemporalState_InFitPixelZoomPanAndFullscreen()
    {
        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();
            var backend = new FakeBackend();
            var rotations = new RotationStore();
            var range = new MediaRange(TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(15));
            var ranges = new FakeRangeStore(range);
            await using var coordinator = new MediaPlaybackCoordinator(() => new MediaPlaybackService(backend));
            var host = new PlayerViewerHost(coordinator, ranges, rotations: rotations);
            var window = CreateSubclipWindow(host);
            window.ShowActivated = false; window.Left = -32000; window.Opacity = 0; window.Show();
            try
            {
                var asset = new PlayerViewerAsset(Guid.NewGuid(), "clip.mp4", "clip.mp4", "clip.mp4", MediaPresentationKind.Video, rotations.Id);
                await host.OpenAsync(asset, new(asset.RootId, asset.RelativePath, asset.Key,
                    Path.GetFullPath("clip.mp4"), MediaRootAvailability.Online, true));
                window.UpdateLayout();
                host.TryHandleShortcut(System.Windows.Input.Key.Space, host);
                var seeks = backend.SeekPositions.ToArray();
                var plays = backend.PlayCallCount; var pauses = backend.PauseCallCount;
                var opens = backend.OpenPresentationOperations.Count(value => value == "open");
                var menu = host.MediaSurfaceHost.ContextMenu!;
                async Task Turn()
                {
                    ((MenuItem)menu.Items[1]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                    await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
                    window.UpdateLayout();
                }
                await Turn();
                Assert.Equal(90, backend.Rotation.Degrees);
                Assert.Equal(new ViewerViewport(), backend.Viewport);
                host.ZoomChoice.SelectedIndex = 2;
                var dpi = VisualTreeHelper.GetDpi(host.MediaSurfaceHost);
                var fit = Math.Min(host.MediaSurfaceHost.ActualWidth * dpi.DpiScaleX / 1080,
                    host.MediaSurfaceHost.ActualHeight * dpi.DpiScaleY / 1920);
                Assert.Equal(1 / fit, backend.Viewport.Zoom, 5);
                host.PanViewport(100000, -100000);
                Assert.True(backend.Viewport.PanY < 0);
                host.ToggleFullscreen();
                await Turn();
                Assert.Equal(180, backend.Rotation.Degrees);
                Assert.True(host.IsFullscreen);
                host.ExitFullscreen();
                Assert.Equal(seeks, backend.SeekPositions);
                Assert.Equal(plays, backend.PlayCallCount); Assert.Equal(pauses, backend.PauseCallCount);
                Assert.Equal(opens, backend.OpenPresentationOperations.Count(value => value == "open"));
                Assert.Equal(0, ranges.SaveCount);
                Assert.Same(asset, host.CurrentAsset);
                Assert.Equal(2, rotations.Value.Revision);
            }
            finally { await host.CloseAsync(); window.Close(); }
        });
    }

    private sealed class RotationStore : IAssetVideoRotationStore
    {
        public Guid Id { get; } = Guid.NewGuid();
        public AssetVideoRotation Value => new(Id, _rotation, _revision);
        private VideoRotation _rotation;
        private long _revision;
        public event EventHandler<IReadOnlyList<AssetVideoRotation>>? Changed;
        public Task<IReadOnlyDictionary<Guid, AssetVideoRotation>> GetAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<Guid, AssetVideoRotation>>(new Dictionary<Guid, AssetVideoRotation> { [Id] = Value });
        public Task RotateAsync(IReadOnlyDictionary<Guid, long> revisions, bool right, CancellationToken cancellationToken = default)
        {
            Assert.Equal(_revision, revisions[Id]);
            _rotation = _rotation.Turn(right); _revision++;
            Changed?.Invoke(this, [Value]);
            return Task.CompletedTask;
        }
    }
}
