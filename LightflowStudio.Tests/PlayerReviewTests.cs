using System.Windows;
using System.Windows.Input;
using Xunit;

namespace LightflowStudio.Tests;

public sealed partial class PlayerViewerHostLeaseTests
{
    [Fact]
    public async Task SurfacePanAndCaptureCancellation_SuppressClickAndFullscreen()
    {
        await StaDispatcher.RunAsync(async () =>
        {
            var surface = new System.Windows.Controls.Border();
            var clicks = 0; var fullscreen = 0; var distance = 0d;
            using var input = new PlayerSurfaceInput(surface, () => clicks++, () => fullscreen++,
                (x, y) => distance += x, _ => { }, (_, _) => false, _ => false);
            input.BeginGesture(new(10, 10), 1);
            Assert.False(input.MoveGesture(new(11, 10), true));
            Assert.True(input.MoveGesture(new(60, 10), true));
            input.EndGesture();
            input.BeginGesture(new(10, 10), 2);
            Assert.True(input.MoveGesture(new(60, 10), true));
            input.EndGesture();
            input.BeginGesture(new(10, 10), 1);
            input.EndGesture();
            input.Cancel();
            await Task.Delay(System.Windows.Forms.SystemInformation.DoubleClickTime + 50);
            Assert.Equal(100, distance);
            Assert.Equal(0, clicks);
            Assert.Equal(0, fullscreen);
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task ReviewLoop_UsesFullSourceWorkingRangeOrSelectedSubclip(int context)
    {
        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();
            var backend = new FakeBackend();
            await using var coordinator = new MediaPlaybackCoordinator(() => new MediaPlaybackService(backend));
            var range = context == 0 ? null : new MediaRange(TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(50));
            var store = new FakeRangeStore(range);
            var assetId = Guid.NewGuid();
            var subclips = new FakeSubclipService();
            var now = DateTimeOffset.UtcNow;
            if (context == 2) subclips.Items.Add(new(Guid.NewGuid(), assetId, "Take", 0,
                TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(60), 1, now, now));
            var host = new PlayerViewerHost(coordinator, store, subclips);
            var window = CreateSubclipWindow(host);
            window.ShowActivated = false; window.Left = -32000; window.Show();
            try
            {
                await host.OpenAsync(new(Guid.NewGuid(), "clip.mp4", "clip.mp4", "clip.mp4", MediaPresentationKind.Video, assetId),
                    new(Guid.NewGuid(), "clip.mp4", "clip.mp4", Path.GetFullPath("clip.mp4"), MediaRootAvailability.Online, true));
                if (context == 2)
                {
                    host.SubclipsList.SelectedItem = host.SubclipsList.Items[0];
                    await WaitUntilAsync(() => host.ActiveSubclipId is not null, "selected Subclip");
                }
                foreach (var speedIndex in Enumerable.Range(0, 6))
                {
                    host.SpeedChoice.SelectedIndex = speedIndex;
                    host.LoopChoice.IsChecked = true;
                    host.TryHandleShortcut(System.Windows.Input.Key.Space, host);
                    await WaitUntilAsync(() => host.PlayPauseButton.Content?.ToString() == "Pause", "playing");
                    var plays = backend.PlayCallCount;
                    if (context == 0) backend.End(); else backend.Present(context == 1 ? 50 : 20);
                    await WaitUntilAsync(() => backend.PlayCallCount > plays, "loop restart");
                    Assert.Equal(TimeSpan.FromSeconds(context == 0 ? 0 : context == 1 ? 2 : 10), backend.SeekPositions.Last());
                    Assert.Equal(PlaybackReviewOptions.Speeds[speedIndex], backend.Options.Speed);
                    host.TryHandleShortcut(System.Windows.Input.Key.Space, host);
                    await WaitUntilAsync(() => host.PlayPauseButton.Content?.ToString() == "Play", "pause");
                }
                Assert.Equal(0, store.SaveCount);
                Assert.Equal(range, await store.RestoreAsync(assetId));
            }
            finally { await host.CloseAsync(); window.Close(); }
        });
    }

    [Fact]
    public async Task ReviewControls_ResetAndFullscreenPreservesTheExistingSession()
    {
        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();
            var backend = new FakeBackend();
            await using var coordinator = new MediaPlaybackCoordinator(() => new MediaPlaybackService(backend));
            var host = new PlayerViewerHost(coordinator);
            var window = CreateSubclipWindow(host);
            window.ShowActivated = false; window.Left = -32000; window.Opacity = 0; window.Show();
            try
            {
                var asset = new PlayerViewerAsset(Guid.NewGuid(), "clip.mp4", "clip.mp4", "clip.mp4", MediaPresentationKind.Video);
                var resolution = new MediaPathResolution(asset.RootId, asset.RelativePath, asset.Key, Path.GetFullPath("clip.mp4"), MediaRootAvailability.Online, true);
                await host.OpenAsync(asset, resolution);
                window.UpdateLayout();
                host.SpeedChoice.SelectedIndex = 5;
                host.CadenceChoiceBox.SelectedIndex = 3;
                Assert.Equal(new PlaybackReviewOptions(4, 5), backend.Options);
                host.ZoomChoice.SelectedIndex = 2;
                Assert.True(backend.Viewport.Zoom > 1);
                host.PanViewport(100000, -100000);
                Assert.True(backend.Viewport.PanX > 0);
                Assert.True(backend.Viewport.PanY < 0);
                Assert.InRange(backend.Viewport.PanX, 0, 10);
                host.ZoomChoice.SelectedIndex = 0;
                Assert.Equal(new ViewerViewport(), backend.Viewport);
                host.ZoomChoice.SelectedIndex = 2;
                host.LoopChoice.IsChecked = true;
                var originalContent = window.Content;
                var opens = backend.OpenPresentationOperations.Count(value => value == "open");
                host.ToggleFullscreen();
                Assert.True(host.IsFullscreen);
                Assert.Same(host, window.Content);
                Assert.Same(asset, host.CurrentAsset);
                Assert.True(host.TryHandleShortcut(System.Windows.Input.Key.Escape, host));
                Assert.False(host.IsFullscreen);
                Assert.Same(originalContent, window.Content);
                Assert.Equal(opens, backend.OpenPresentationOperations.Count(value => value == "open"));
                Assert.Equal(new PlaybackReviewOptions(4, 5), backend.Options);
                await host.OpenAsync(asset with { Name = "next" }, resolution);
                Assert.Equal(new PlaybackReviewOptions(), backend.Options);
                Assert.Equal(0, host.ZoomChoice.SelectedIndex);
                Assert.Equal(0, host.CadenceChoiceBox.SelectedIndex);
                Assert.False(host.LoopChoice.IsChecked);
            }
            finally { await host.CloseAsync(); window.Close(); }
        });
    }

    [Fact]
    public async Task SurfaceClick_TogglesOnce_ChromeAndTeardownCannotTriggerIt()
    {
        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();
            var backend = new FakeBackend();
            await using var coordinator = new MediaPlaybackCoordinator(() => new MediaPlaybackService(backend));
            var host = new PlayerViewerHost(coordinator);
            var window = CreateSubclipWindow(host);
            window.ShowActivated = false; window.Left = -32000; window.Show();
            try
            {
                await host.OpenAsync(new(Guid.NewGuid(), "clip.mp4", "clip.mp4", "clip.mp4", MediaPresentationKind.Video),
                    new(Guid.NewGuid(), "clip.mp4", "clip.mp4", Path.GetFullPath("clip.mp4"), MediaRootAvailability.Online, true));
                void Click(UIElement target, int count = 1)
                {
                    var down = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                    { RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent };
                    typeof(MouseButtonEventArgs).GetField("_count", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(down, count);
                    target.RaiseEvent(down);
                    target.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 1, MouseButton.Left)
                    { RoutedEvent = UIElement.PreviewMouseLeftButtonUpEvent });
                }
                Click(host.MediaSurfaceHost);
                await WaitUntilAsync(() => backend.PlayCallCount == 1, "surface play");
                Click(host.MediaSurfaceHost);
                await WaitUntilAsync(() => backend.PauseCallCount == 1, "surface pause");
                var originalContent = window.Content;
                window.Opacity = 0;
                Click(host.MediaSurfaceHost);
                Click(host.MediaSurfaceHost, 2);
                Assert.True(host.IsFullscreen);
                await Task.Delay(System.Windows.Forms.SystemInformation.DoubleClickTime + 50);
                Assert.Equal(1, backend.PlayCallCount);
                Assert.Equal(1, backend.PauseCallCount);
                host.TryHandleShortcut(System.Windows.Input.Key.Escape, host);
                Assert.Same(originalContent, window.Content);
                Click(host.SpeedChoice);
                await Task.Delay(System.Windows.Forms.SystemInformation.DoubleClickTime + 50);
                Assert.Equal(1, backend.PlayCallCount);
                Assert.Equal(1, backend.PauseCallCount);
                Click(host.MediaSurfaceHost);
                await host.CloseAsync();
                await Task.Delay(System.Windows.Forms.SystemInformation.DoubleClickTime + 50);
                Assert.Equal(1, backend.PlayCallCount);
            }
            finally { await host.CloseAsync(); window.Close(); }
        });
    }
}

public sealed class CadenceChoiceTests
{
    [Theory]
    [InlineData(120, "Source|60 fps|30 fps|24 fps")]
    [InlineData(60, "Source|30 fps")]
    [InlineData(50, "Source|25 fps")]
    [InlineData(24, "Source")]
    [InlineData(0, "Source")]
    public void Choices_OnlyAdvertiseRealDivisors(double fps, string labels) =>
        Assert.Equal(labels, string.Join('|', CadenceChoice.ForSource(fps).Select(value => value.Label)));

    [Fact]
    public void FractionalFamilyAndSpeedRemainIndependent()
    {
        var choices = CadenceChoice.ForSource(120000d / 1001);
        Assert.Equal("Source|59.94 fps|29.97 fps|23.976 fps", string.Join('|', choices.Select(value => value.Label)));
        foreach (var speed in PlaybackReviewOptions.Speeds)
        foreach (var choice in choices.Skip(1))
        {
            var options = new PlaybackReviewOptions(speed, choice.Divisor);
            Assert.Equal(choice.Divisor, Math.Ceiling(speed * (120000d / 1001) / (options.OutputThreshold(120000d / 1001) + 1)));
        }
    }
}
