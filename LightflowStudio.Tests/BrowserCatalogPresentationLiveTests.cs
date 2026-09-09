using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LightflowStudio;
using Xunit;
using Xunit.Abstractions;

namespace LightflowStudio.Tests;

[Collection("STA dispatcher tests")]
public sealed class BrowserCatalogPresentationLiveTests(ITestOutputHelper output)
{
    [Fact]
    public async Task CachedGridRendersThroughRealBindingsBeforeReconciliationAndRejectsQueuedOldPresentation()
    {
        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();
            var directory = Path.Combine(Path.GetTempPath(), "lightflow-known-ui-" + Guid.NewGuid().ToString("N"));
            var media = Directory.CreateDirectory(Path.Combine(directory, "media")).FullName;
            var other = Directory.CreateDirectory(Path.Combine(media, "other")).FullName;
            var startup = await LightflowStorageCoordinator.StartAsync(Path.Combine(directory, "app"));
            var storage = startup.Coordinator!;
            await storage.MediaMonitoring!.DisposeAsync();
            var root = (await storage.MediaRoots.CreateAsync("Test", media)).Root!;
            var bitmap = BitmapSource.Create(16, 16, 96, 96, PixelFormats.Bgr24, null, new byte[16 * 16 * 3], 16 * 3);
            var encoder = new JpegBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var bytes = new MemoryStream();
            encoder.Save(bytes);
            for (var i = 0; i < 200; i++)
            {
                var relative = $"photo{i:D4}.jpg";
                File.WriteAllBytes(Path.Combine(media, relative), bytes.ToArray());
                var asset = (await storage.MediaAssets.CreateAsync(root.RootId, relative, "image")).Asset!.Asset;
                var identity = new PreviewSourceIdentity(asset.FileSizeBytes, asset.LastWriteUtcTicks,
                    asset.Fingerprint!.Version, asset.Fingerprint.Value);
                await storage.Previews!.ObserveSourceAsync(asset.AssetId, identity);
                var path = storage.Previews.GetArtifactPath(asset.AssetId, PreviewArtifactKind.Thumbnail, 1, identity, "jpg");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, bytes.ToArray());
                await storage.Previews.SetArtifactAsync(asset.AssetId, PreviewArtifactKind.Thumbnail,
                    new(1, PreviewComponentState.Current, Path.GetRelativePath(storage.Locations.PreviewsDirectory, path).Replace('\\', '/'),
                        VisualIdentity: PreviewVisualIdentity.Original));
                await storage.Previews.SetMetadataAsync(asset.AssetId, new(1, PreviewComponentState.Current, PayloadJson: "{}"));
            }
            var window = new MainWindow(storage, startup.Status, startup.Diagnostic)
            {
                Left = -32000, Top = -32000, ShowInTaskbar = false, Width = 1440, Height = 900,
                WindowStartupLocation = WindowStartupLocation.Manual
            };
            using var release = new ManualResetEventSlim();
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var navigation = (BrowserNavigationSession)typeof(MainWindow).GetField("_browserNavigation", flags)!.GetValue(window)!;
            var grid = (BrowserGridModel)typeof(MainWindow).GetField("_browserGrid", flags)!.GetValue(window)!;
            BrowserFolderState? known = null;
            var knownNotifications = 0;
            EventHandler<BrowserFolderState> pause = (_, state) =>
            {
                known = state;
                Interlocked.Increment(ref knownNotifications);
                if (!release.Wait(TimeSpan.FromSeconds(20))) throw new TimeoutException("UI did not release reconciliation");
            };
            navigation.KnownContentAvailable += pause;
            try
            {
                window.Show();
                Assert.True(await window.StartupCompletion.WaitAsync(TimeSpan.FromSeconds(30)), "Window startup failed.");
                Assert.NotEmpty(window.BrowserFolderTree.Items);
                var clock = Stopwatch.StartNew();
                var loading = Navigate(media);
                await Until(() => window.BrowserLoadingOverlay.Visibility == Visibility.Collapsed && grid.TotalCount == 200 &&
                    Children(window.BrowserGridRows).OfType<Image>().Any(image => image.Source is BitmapSource));
                var renderMs = clock.Elapsed.TotalMilliseconds;
                Assert.False(loading.IsCompleted);
                Assert.Contains("Checking for changes", window.BrowserStatusText.Text);
                Assert.True(Children(window.BrowserGridRows).OfType<Image>().Count() < 200);
                await Hint();
                Assert.False(loading.IsCompleted);
                Assert.True((bool)typeof(MainWindow).GetField("_browserRefreshPending", flags)!.GetValue(window)!);
                output.WriteLine($"200 cached items: first bound/decoded viewport {renderMs:F1} ms; reconciliation held at deterministic gate.");
                if (Environment.GetEnvironmentVariable("LIGHTFLOW_BROWSER_UI_REPORT") is { Length: > 0 } report)
                    File.WriteAllText(report, $"200 cached items: first bound/decoded viewport {renderMs:F1} ms.\n");

                // Switch scope while the old continuation is blocked, then replay its queued UI callback.
                var obsoleteState = known!;
                var replacement = Navigate(other);
                await replacement.WaitAsync(TimeSpan.FromSeconds(10));
                typeof(MainWindow).GetMethod("BrowserNavigation_KnownContentAvailable", flags)!.Invoke(window, [navigation, obsoleteState]);
                typeof(MainWindow).GetMethod("BrowserNavigation_EffectiveScopeDetermined", flags)!.Invoke(window,
                    [navigation, new BrowserEffectiveScope(obsoleteState.Location!, obsoleteState.Mode,
                        obsoleteState.RecursiveRoots!, obsoleteState.NavigationGeneration)]);
                await Task.Delay(50);
                Assert.Equal(0, grid.TotalCount);
                release.Set();
                await loading.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.Equal(other, window.BrowserCurrentPath.Text);
                Assert.Equal(0, grid.TotalCount);

                release.Reset();
                var beforeReplay = knownNotifications;
                clock.Restart();
                var revisit = Navigate(media);
                await Until(() => window.BrowserLoadingOverlay.Visibility == Visibility.Collapsed && grid.TotalCount == 200 &&
                    Children(window.BrowserGridRows).OfType<Image>().Any(image => image.Source is BitmapSource));
                var revisitMs = clock.Elapsed.TotalMilliseconds;
                Assert.False(revisit.IsCompleted);
                output.WriteLine($"Same-window revisit: first bound/decoded viewport {revisitMs:F1} ms.");
                if (Environment.GetEnvironmentVariable("LIGHTFLOW_BROWSER_UI_REPORT") is { Length: > 0 } secondReport)
                    File.AppendAllText(secondReport, $"Same-window revisit: first bound/decoded viewport {revisitMs:F1} ms.\n");
                await Hint();
                await Hint(); // overlapping hints are coalesced while the same reconciliation is pending
                release.Set();
                await revisit.WaitAsync(TimeSpan.FromSeconds(10));
                await Until(() => knownNotifications == beforeReplay + 2 && !navigation.State.IsRevalidating &&
                    window.BrowserLoadingOverlay.Visibility == Visibility.Collapsed);
                Assert.DoesNotContain("Checking for changes", window.BrowserStatusText.Text);

                // Conservative before-ordering control: suppress only early UI presentation; keep the same
                // final discovery/scheduler/binding path, storage, thumbnail cache and warmed window.
                navigation.KnownContentAvailable -= pause;
                var presentHandler = (EventHandler<BrowserFolderState>)Delegate.CreateDelegate(
                    typeof(EventHandler<BrowserFolderState>), window,
                    typeof(MainWindow).GetMethod("BrowserNavigation_KnownContentAvailable", flags)!);
                navigation.KnownContentAvailable -= presentHandler;
                await Navigate(other);
                clock.Restart();
                var blocking = Navigate(media);
                await Until(() => window.BrowserLoadingOverlay.Visibility == Visibility.Collapsed && grid.TotalCount == 200 &&
                    Children(window.BrowserGridRows).OfType<Image>().Any(image => image.Source is BitmapSource));
                var blockingMs = clock.Elapsed.TotalMilliseconds;
                await blocking;
                output.WriteLine($"Same-window blocking control: first bound/decoded viewport {blockingMs:F1} ms.");
                if (Environment.GetEnvironmentVariable("LIGHTFLOW_BROWSER_UI_REPORT") is { Length: > 0 } controlReport)
                    File.AppendAllText(controlReport, $"Same-window blocking control: first bound/decoded viewport {blockingMs:F1} ms.\n");

                Task Navigate(string path)
                {
                    // Match the real Go action's tree intent before driving its shared navigation method.
                    Keyboard.Focus(window.BrowserCurrentPath);
                    typeof(MainWindow).GetMethod("RequestBrowserTreeSelection", flags, [typeof(string)])!.Invoke(window, [path]);
                    return (Task)typeof(MainWindow).GetMethod("RunBrowserNavigationAsync", flags)!
                        .Invoke(window, [new Func<Task<BrowserFolderState?>>(() => navigation.NavigateToPathAsync(path)), null])!;
                }

                Task Hint() => (Task)typeof(MainWindow).GetMethod("SynchronizeMonitoredFolderAsync", flags)!
                    .Invoke(window, [new MediaFolderEnumerationRequest(root.RootId)])!;
            }
            finally
            {
                release.Set();
                navigation.KnownContentAvailable -= pause;
                window.Close();
                await storage.DisposeAsync();
                // WPF's URI image cache can hold thumbnail handles until finalization, as in other live tests.
                try { Directory.Delete(directory, true); } catch (IOException) { }
            }
        });
    }

    private static async Task Until(Func<bool> condition)
    {
        var timeout = Stopwatch.StartNew();
        while (!condition())
        {
            if (timeout.Elapsed > TimeSpan.FromSeconds(15)) throw new TimeoutException("Cached viewport not presented");
            await Task.Delay(10);
        }
    }

    private static IEnumerable<DependencyObject> Children(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var descendant in Children(child)) yield return descendant;
        }
    }
}
