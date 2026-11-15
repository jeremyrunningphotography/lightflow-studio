using System.Reflection;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using Xunit;

namespace LightflowStudio.Tests;

public sealed partial class FlyleafPlaybackIntegrationTests
{
    [Fact]
    public async Task SpeedFocus_NativePausedStepsAndPreviewInputRemainCorrectAtEveryRate()
    {
        var dependencies = PlaybackDependencyLocator.FindSharedLibraries()!;
        var fixture = Path.Combine(_root, "speed-focus.mkv");
        GenerateCfrFixture(Path.Combine(dependencies, "ffmpeg.exe"), fixture, 3);
        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();
            await using var backend = new FlyleafPlaybackBackend(dependencies);
            MediaPlaybackService? service = null;
            await using var coordinator = new MediaPlaybackCoordinator(() => service = new(backend));
            var host = new PlayerViewerHost(coordinator);
            var window = new Window { Content = host, Width = 1100, Height = 800, Left = -32000, ShowInTaskbar = false };
            window.Show();
            try
            {
                var asset = new PlayerViewerAsset(Guid.NewGuid(), "focus.mkv", "focus.mkv", "focus.mkv", MediaPresentationKind.Video);
                await host.OpenAsync(asset, new(asset.RootId, asset.RelativePath, asset.Key, fixture, MediaRootAvailability.Online, true));
                var queue = (FrameStepQueue)typeof(PlayerViewerHost).GetField("_frameStepQueue",
                    BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(host)!;
                for (var index = 0; index < PlaybackReviewOptions.Speeds.Length; index++)
                {
                    await service!.SeekAsync(TimeSpan.FromMilliseconds(400));
                    window.Activate(); host.SpeedChoice.Focus();
                    host.SpeedChoice.IsDropDownOpen = true;
                    await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.Loaded);
                    host.SpeedChoice.SelectedIndex = index;
                    host.SpeedChoice.IsDropDownOpen = false;
                    var popup = (System.Windows.Controls.Primitives.Popup)host.SpeedChoice.Template.FindName("PART_Popup", host.SpeedChoice);
                    var deadline = DateTime.UtcNow.AddSeconds(5);
                    while (System.Windows.PresentationSource.FromVisual((System.Windows.Media.Visual)popup.Child) is not null)
                    {
                        Assert.True(DateTime.UtcNow < deadline, "Speed popup did not finish closing.");
                        await Task.Delay(10);
                    }
                    await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
                    Assert.Same(host, Keyboard.FocusedElement);
                    Assert.True(host.TryHandleShortcut(Key.Right, Keyboard.FocusedElement as DependencyObject, ModifierKeys.None));
                    await queue.WaitUntilIdleAsync();
                    AssertTimestamp(TimeSpan.FromMilliseconds(500), service.Snapshot.DisplayedTimestamp!);
                    host.PreviousFrameButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                    await queue.WaitUntilIdleAsync();
                    AssertTimestamp(TimeSpan.FromMilliseconds(400), service.Snapshot.DisplayedTimestamp!);
                    host.NextFrameButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                    await queue.WaitUntilIdleAsync();
                    AssertTimestamp(TimeSpan.FromMilliseconds(500), service.Snapshot.DisplayedTimestamp!);
                    Assert.True(host.TryHandleShortcut(Key.Left, Keyboard.FocusedElement as DependencyObject, ModifierKeys.None));
                    await queue.WaitUntilIdleAsync();
                    AssertTimestamp(TimeSpan.FromMilliseconds(400), service.Snapshot.DisplayedTimestamp!);
                    Assert.Equal(MediaPlaybackState.Paused, service.Snapshot.State);
                }
                // Exercise the actual child-HWND input seam, not merely the host's WPF background.
                host.SpeedChoice.SelectedIndex = 3; // return from retained backstep bitmap to the live preview
                await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
                var view = Assert.IsType<MediaPlaybackView>(host.VideoHost.Children[0]);
                host.SpeedChoice.Focus();
                view.InputSurface.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
                    { RoutedEvent = Mouse.PreviewMouseDownEvent });
                Assert.False(host.SpeedChoice.IsKeyboardFocusWithin);
                Assert.True(host.TryHandleShortcut(Key.Right, Keyboard.FocusedElement as DependencyObject, ModifierKeys.None));
                await queue.WaitUntilIdleAsync();
                AssertTimestamp(TimeSpan.FromMilliseconds(500), service!.Snapshot.DisplayedTimestamp!);
            }
            finally { await host.CloseAsync(); window.Close(); }
        });
    }
}
