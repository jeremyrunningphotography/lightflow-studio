using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using FlyleafLib.Controls.WPF;
using Xunit;

namespace LightflowStudio.Tests;

public sealed partial class FlyleafPlaybackIntegrationTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task StartupRestore_NativeVideoSurvivesTaskbarReveal(int seconds)
    {
        var dependencies = PlaybackDependencyLocator.FindSharedLibraries()!;
        var fixture = Path.Combine(_root, "startup.mkv");
        GenerateCfrFixture(Path.Combine(dependencies, "ffmpeg.exe"), fixture, 3);
        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();
            await using var coordinator = new MediaPlaybackCoordinator(() => new MediaPlaybackService(new FlyleafPlaybackBackend(dependencies)));
            var host = new PlayerViewerHost(coordinator);
            var window = new Window { Content = host, Width = 900, Height = 700, Left = -32000, ShowActivated = false, ShowInTaskbar = false };
            window.SourceInitialized += (_, _) => StartupWindowPresentation.SetCloaked(window, true);
            try
            {
                window.Show();
                var asset = new PlayerViewerAsset(Guid.NewGuid(), "startup.mkv", "startup", "startup.mkv", MediaPresentationKind.Video, Guid.NewGuid());
                await host.OpenAsync(asset, new(asset.RootId, asset.RelativePath, asset.Key, fixture, MediaRootAvailability.Online, true), continuation: new() { Asset = asset, Position = TimeSpan.FromSeconds(seconds), Muted = true });
                await window.Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.Loaded);
                await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
                var native = Assert.IsType<FlyleafHost>(Assert.IsType<MediaPlaybackView>(host.VideoHost.Children[0]).Content);
                var before = new WindowInteropHelper(window).Handle;
                var oldSurface = native.SurfaceHandle;
                var saved = host.CaptureWorkspaceState()!;
                window.ShowInTaskbar = true;
                StartupWindowPresentation.SetCloaked(window, false);
                host.RevealStartupVideoPresentation();
                await window.Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.Loaded);
                await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
                native = Assert.IsType<FlyleafHost>(Assert.IsType<MediaPlaybackView>(host.VideoHost.Children[0]).Content);
                var after = new WindowInteropHelper(window).Handle;
                Assert.True(IsWindowVisible(native.SurfaceHandle), $"native hidden; parent before={before} after={after}, host owner={native.OwnerHandle}, surface={native.SurfaceHandle}");
                Assert.Equal(after, GetParent(native.SurfaceHandle));
                Assert.Equal(after, native.OwnerHandle);
                await Task.Delay(300); // Let compositor handoff settle; a persistent cloak is not a render-timing delay.
                Assert.Equal(0, DwmGetWindowAttribute(native.SurfaceHandle, 14, out var cloaked, sizeof(int)));
                Assert.Equal(0, cloaked);
                Assert.Equal("Play", host.PlayPauseButton.Content);
                Assert.Equal(saved.Position, host.CaptureWorkspaceState()!.Position);
                Assert.NotEqual(oldSurface, native.SurfaceHandle);
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var frame = await ((MediaPlaybackView)host.VideoHost.Children[0]).CaptureFrameAsync(timeout.Token);
                Assert.True(frame.BgraPixels.Where((_, index) => index % 4 != 3).Any(value => value > 20), "Restored native frame must contain fixture video, not black.");
                Assert.Equal(saved.Position, frame.Timestamp.Position);
                host.PlayPauseButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                while (host.CaptureWorkspaceState()!.Position <= saved.Position) await Task.Delay(20, timeout.Token);
                Assert.Equal(0, DwmGetWindowAttribute(native.SurfaceHandle, 14, out cloaked, sizeof(int)));
                Assert.Equal(0, cloaked);
            }
            finally { await host.CloseAsync(); window.Close(); }
        });
    }
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint handle);
    [DllImport("user32.dll")] private static extern nint GetParent(nint handle);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint handle, int attribute, out int value, int size);
}
