using System.Windows;
using System.Windows.Input;
using Xunit;

namespace LightflowStudio.Tests;

public sealed partial class PlayerViewerHostLeaseTests
{
    [Fact]
    public async Task LiveReviewChange_ReconcilesEndReachedDuringReconfiguration()
    {
        await StaDispatcher.RunAsync(async () =>
        {
            var backend = new FakeBackend();
            await using var service = new MediaPlaybackService(backend);
            await service.OpenAsync(Path.GetFullPath("clip.mp4"));
            await service.PlayAsync();
            backend.ReviewOptionsApplied = () => { backend.HasEnded = true; backend.End(); };
            await service.SetReviewOptionsAsync(new(4));
            Assert.Equal(MediaPlaybackState.Ended, service.Snapshot.State);
        });
    }

    [Fact]
    public async Task SurfaceClick_ExecutesOnRelease_SecondClickOnlyChangesFullscreen()
    {
        await StaDispatcher.RunAsync(() =>
        {
            var surface = new System.Windows.Controls.Border();
            var clicks = 0; var fullscreen = 0;
            using var input = new PlayerSurfaceInput(surface, () => clicks++, () => fullscreen++,
                (_, _) => { }, _ => { }, (_, _) => false, _ => false);
            input.BeginGesture(new(10, 10), 1);
            input.EndGesture();
            Assert.Equal(1, clicks);
            input.BeginGesture(new(10, 10), 2);
            input.EndGesture();
            Assert.Equal(1, clicks);
            Assert.Equal(1, fullscreen);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task FullscreenOverlays_AppearThenRecede_AndFirstEntryHintDoesNotRepeat()
    {
        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();
            var exits = 0;
            var overlay = new PlayerFullscreenOverlay(() => exits++);
            var window = new Window { Content = overlay, Width = 600, Height = 400, Left = -32000, ShowActivated = false };
            window.Show();
            try
            {
                overlay.Enter(true);
                Assert.Equal(Visibility.Visible, overlay.Hint.Visibility);
                Assert.Equal(Visibility.Collapsed, overlay.ExitButton.Visibility);
                overlay.PointerMoved();
                overlay.ShowPlayback(true);
                Assert.Equal(Visibility.Visible, overlay.ExitButton.Visibility);
                Assert.Equal(Visibility.Visible, overlay.Feedback.Visibility);
                var playGeometry = overlay.Feedback.Data.ToString();
                await Task.Delay(600); // The earlier glyph is already fading.
                overlay.ShowPlayback(false);
                Assert.NotEqual(playGeometry, overlay.Feedback.Data.ToString());
                await Task.Delay(300); // Earlier fade completion must not hide this newer glyph.
                Assert.Equal(Visibility.Visible, overlay.Feedback.Visibility);
                Assert.Equal(0.65, overlay.Feedback.Opacity, 2);
                await Task.Delay(800);
                Assert.Equal(Visibility.Collapsed, overlay.Feedback.Visibility);
                await Task.Delay(2000);
                Assert.Equal(Visibility.Collapsed, overlay.ExitButton.Visibility);
                Assert.Equal(Visibility.Collapsed, overlay.Hint.Visibility);
                overlay.Enter(false);
                Assert.Equal(Visibility.Collapsed, overlay.Hint.Visibility);
                overlay.PointerMoved();
                overlay.ExitButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                Assert.Equal(1, exits);
                overlay.Reset();
                Assert.Equal(Visibility.Collapsed, overlay.ExitButton.Visibility);
            }
            finally { window.Close(); }
        });
    }

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
            input.Cancel();
            input.EndGesture();
            await Task.CompletedTask;
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
                host.CadenceChoiceBox.SelectedItem = host.CadenceChoiceBox.Items.Cast<CadenceChoice>().Single(choice => choice.Label == "24");
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
                window.UpdateLayout();
                Assert.True(host.IsFullscreen);
                Assert.Equal(Visibility.Collapsed, host.PlayerHeader.Visibility);
                Assert.Equal(Visibility.Collapsed, host.TransportBar.Visibility);
                Assert.InRange(Math.Abs(host.MediaSurfaceHost.ActualHeight - host.ActualHeight), 0, 1);
                Assert.InRange(Math.Abs(host.MediaSurfaceHost.ActualWidth - host.ActualWidth), 0, 1);
                Assert.Same(originalContent, window.Content);
                Assert.Same(asset, host.CurrentAsset);
                Assert.Equal(Visibility.Visible, host.MediaSurfaceHost.Children.OfType<PlayerFullscreenOverlay>().Single().Hint.Visibility);
                Assert.True(host.TryHandleShortcut(System.Windows.Input.Key.Escape, host));
                Assert.False(host.IsFullscreen);
                Assert.Equal(Visibility.Visible, host.PlayerHeader.Visibility);
                Assert.Equal(Visibility.Visible, host.TransportBar.Visibility);
                Assert.Same(host.RangeReviewRow, host.ReviewControlGroup.Parent);
                Assert.Equal(1, System.Windows.Controls.Grid.GetColumn(host.ReviewControlGroup));
                Assert.Same(originalContent, window.Content);
                host.ToggleFullscreen();
                Assert.Equal(Visibility.Collapsed, host.MediaSurfaceHost.Children.OfType<PlayerFullscreenOverlay>().Single().Hint.Visibility);
                host.ExitFullscreen();
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
                Assert.Equal(1, backend.PlayCallCount);
                Click(host.MediaSurfaceHost);
                Assert.Equal(1, backend.PauseCallCount);
                var seeks = backend.SeekPositions.Count;
                Click(host.PositionSlider);
                Assert.Equal(seeks + 1, backend.SeekPositions.Count);
                Assert.Equal(1, backend.PlayCallCount);
                var originalContent = window.Content;
                window.Opacity = 0;
                Click(host.MediaSurfaceHost);
                Click(host.MediaSurfaceHost, 2);
                Assert.True(host.IsFullscreen);
                Assert.Equal(2, backend.PlayCallCount);
                Assert.Equal(1, backend.PauseCallCount);
                host.TryHandleShortcut(System.Windows.Input.Key.Escape, host);
                Assert.Same(originalContent, window.Content);
                Click(host.SpeedChoice);
                Assert.Equal(2, backend.PlayCallCount);
                Assert.Equal(1, backend.PauseCallCount);
                Click(host.MediaSurfaceHost);
                Assert.Equal(2, backend.PauseCallCount);
                await host.CloseAsync();
                Assert.Equal(2, backend.PlayCallCount);
            }
            finally { await host.CloseAsync(); window.Close(); }
        });
    }
}

public sealed class CadenceChoiceTests
{
    [Theory]
    [InlineData(120, "Source (120)|60|59.94|50|30|29.97|25|24|23.976")]
    [InlineData(60, "Source (60)|59.94|50|30|29.97|25|24|23.976")]
    [InlineData(59.94, "Source (59.94)|50|30|29.97|25|24|23.976")]
    [InlineData(50, "Source (50)|30|29.97|25|24|23.976")]
    [InlineData(24, "Source (24)|23.976")]
    [InlineData(0, "Source (unknown)")]
    public void Choices_UseCanonicalLowerRates(double fps, string labels) =>
        Assert.Equal(labels, string.Join('|', CadenceChoice.ForSource(fps).Select(value => value.Label)));

    [Fact]
    public void FractionalFamilyAndSpeedRemainIndependent()
    {
        var choices = CadenceChoice.ForSource(120000d / 1001);
        Assert.Equal(new MediaFrameRate(30000, 1001), choices.Single(choice => choice.Label == "29.97").Rate);
        foreach (var speed in PlaybackReviewOptions.Speeds)
        foreach (var choice in choices.Where(choice => choice.Divisor > 1))
        {
            var options = new PlaybackReviewOptions(speed, choice.Divisor);
            Assert.Equal(choice.Divisor, Math.Ceiling(speed * (120000d / 1001) / (options.OutputThreshold(120000d / 1001) + 1)));
        }
    }

    [Theory]
    [InlineData(23.5)] [InlineData(24)] [InlineData(29.97)] [InlineData(59.94)] [InlineData(119.88)]
    public void Choices_NeverExceedSource(double source) => Assert.All(CadenceChoice.ForSource(source).Skip(1),
        choice => Assert.True(choice.Rate!.Value.Value <= source));

    [Theory]
    [InlineData(50, 1)] [InlineData(30, 1)] [InlineData(30000, 1001)] [InlineData(25, 1)] [InlineData(24, 1)] [InlineData(24000, 1001)]
    public void RationalSelection_UsesSourceTimeAndRetainsCanonicalFraction(int numerator, int denominator)
    {
        var rate = new MediaFrameRate(numerator, denominator);
        var selector = new RationalCadenceSelector(rate);
        var selected = Enumerable.Range(0, 600).Select(i => (long)Math.Round(i * TimeSpan.TicksPerSecond * 1001d / 60000))
            .Where(selector.Select).ToArray();
        Assert.InRange(selected.Length, (int)(10.01 * rate.Value) - 1, (int)(10.01 * rate.Value) + 1);
        Assert.Equal(selected.Distinct().Count(), selected.Length);
    }
}
