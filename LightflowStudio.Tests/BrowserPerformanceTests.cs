using System.Collections.Concurrent;
using System.Diagnostics;
using System.Windows.Media.Imaging;
using LightflowStudio;
using Xunit;
using Xunit.Abstractions;

namespace LightflowStudio.Tests;

public sealed class BrowserPerformanceTests(ITestOutputHelper output)
{
    // Opt-in evidence harness: real isolated Catalog, filesystem, Preview store and Browser models.
    // No timing threshold in CI; never opens or modifies photography libraries.
    [Fact]
    public async Task MeasureNavigationStages()
    {
        var report = Environment.GetEnvironmentVariable("LIGHTFLOW_BROWSER_BENCHMARK");
        if (string.IsNullOrEmpty(report)) return;
        var directory = Path.Combine(Path.GetTempPath(), "lightflow-browser-bench-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var stages = new ConcurrentDictionary<string, double>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "LightflowStudio.Browser",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity => stages.AddOrUpdate(activity.OperationName, activity.Duration.TotalMilliseconds,
                (_, value) => value + activity.Duration.TotalMilliseconds)
        };
        ActivitySource.AddActivityListener(listener);
        var lines = new List<string>();
        LightflowStorageCoordinator? storage = null;
        try
        {
            var media = Directory.CreateDirectory(Path.Combine(directory, "media")).FullName;
            foreach (var (name, count, folders) in new[] { ("normal", 200, 1), ("large", 1000, 1), ("recursive", 1000, 10) })
            {
                for (var index = 0; index < count; index++)
                {
                    var folder = Directory.CreateDirectory(Path.Combine(media, name, folders == 1 ? "" : $"sub{index % folders}"));
                    File.WriteAllBytes(Path.Combine(folder.FullName, $"image{index:D5}.jpg"), new byte[128 * 1024]);
                }
            }
            storage = (await LightflowStorageCoordinator.StartAsync(Path.Combine(directory, "app"))).Coordinator!;
            await storage.MediaMonitoring!.DisposeAsync();
            var root = (await storage.MediaRoots.CreateAsync("Benchmark", media)).Root!;
            await storage.BrowserRecursiveRoots.EnableAsync(root.RootId, "recursive");
            foreach (var scenario in new[] { "normal", "large", "recursive" })
            {
                await Measure(scenario, "cold");
                await Measure(scenario, "same-session");
                await storage.DisposeAsync();
                storage = (await LightflowStorageCoordinator.StartAsync(Path.Combine(directory, "app"))).Coordinator!;
                await storage.MediaMonitoring!.DisposeAsync();
                await Measure(scenario, "restart");
            }
            await Measure("normal", "simulated-slow");

            async Task Measure(string scenario, string visit)
            {
                stages.Clear();
                // No generators: seeded records exercise cached Preview reads independently of FFmpeg.
                var baseline = Environment.GetEnvironmentVariable("LIGHTFLOW_BROWSER_BASELINE") == "1";
                IMediaFolderEnumerator folders = visit == "simulated-slow"
                    ? new SlowFolders(storage!.MediaFolders) : storage!.MediaFolders;
                var reconciliation = new CatalogReconciliationService(folders, storage.MediaAssets);
                var discovery = new MediaDiscoveryRefreshService(reconciliation, () => null);
                using var navigation = new BrowserNavigationSession(storage.MediaRoots, storage.BrowserLocations,
                    discovery, folders, storage.BrowserRecursiveRoots, assets: baseline ? null : storage.MediaAssets,
                    mediaTypes: storage.MediaTypes);
                var clock = Stopwatch.StartNew();
                var firstPresentationMs = 0d;
                Task? earlyHydration = null;
                double hydrationMs = 0, decodeMs = 0;
                navigation.KnownContentAvailable += (_, known) =>
                {
                    firstPresentationMs = clock.Elapsed.TotalMilliseconds;
                    earlyHydration = Hydrate(known);
                };
                var state = await navigation.NavigateToPathAsync(Path.Combine(media, scenario));
                var navigationMs = clock.Elapsed.TotalMilliseconds;
                Assert.NotNull(state);
                Assert.Equal(BrowserFolderStatus.Ready, state.Status);
                if (earlyHydration is not null) await earlyHydration;
                else { firstPresentationMs = navigationMs; await Hydrate(state); }
                var grid = new BrowserGridModel();
                clock.Restart();
                grid.Populate(state.RecursiveMediaEntries ?? state.Entries);
                var modelMs = clock.Elapsed.TotalMilliseconds;
                clock.Restart();
                grid.SetQuery(BrowserQuery.Default);
                var queryMs = clock.Elapsed.TotalMilliseconds;
                var ids = (await storage.MediaAssets.ListAsync()).Where(asset => asset.RelativePath.StartsWith(scenario + "/")).Select(asset => asset.AssetId).ToArray();
                if (visit == "cold")
                {
                    foreach (var asset in (await storage.MediaAssets.ListAsync()).Where(asset => ids.Contains(asset.AssetId)))
                    {
                        var source = new PreviewSourceIdentity(asset.FileSizeBytes, asset.LastWriteUtcTicks,
                            asset.Fingerprint!.Version, asset.Fingerprint.Value);
                        await storage.Previews!.ObserveSourceAsync(asset.AssetId, source);
                        var path = storage.Previews.GetArtifactPath(asset.AssetId, PreviewArtifactKind.Thumbnail, 1, source, "png");
                        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                        File.WriteAllBytes(path, Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a7l8AAAAASUVORK5CYII="));
                        await storage.Previews.SetArtifactAsync(asset.AssetId, PreviewArtifactKind.Thumbnail,
                            new(1, PreviewComponentState.Current, Path.GetRelativePath(storage.Locations.PreviewsDirectory, path).Replace('\\', '/'), VisualIdentity: PreviewVisualIdentity.Original));
                        await storage.Previews.SetMetadataAsync(asset.AssetId, new(1, PreviewComponentState.Current, PayloadJson: "{}"));
                    }
                }
                clock.Restart();
                await storage.Previews!.GetManyAsync(ids);
                var previewMs = clock.Elapsed.TotalMilliseconds;
                clock.Restart();
                await using (var monitoring = new MediaRootMonitoringService(storage.MediaRoots, discovery))
                {
                    await monitoring.StartAsync();
                    var watcherMs = clock.Elapsed.TotalMilliseconds;
                    // Setup is application-global, not paid on real folder revisits; report separately.
                    stages["watcher.setup.separate"] = watcherMs;
                }
                var line = $"{scenario},{visit},presentation={firstPresentationMs:F1},navigation={navigationMs:F1},models={modelMs:F1},query={queryMs:F1},previewRead={previewMs:F1},hydration={hydrationMs:F1},decode32={decodeMs:F1}," +
                    string.Join(",", stages.OrderBy(pair => pair.Key).Select(pair => $"{pair.Key}={pair.Value:F1}"));
                lines.Add(line);
                output.WriteLine(line);

                async Task Hydrate(BrowserFolderState presented)
                {
                    var watch = Stopwatch.StartNew();
                    var model = new BrowserGridModel();
                    model.Populate(presented.RecursiveMediaEntries ?? presented.Entries);
                    var items = presented.Reconciliation?.Items;
                    if (items is not null) model.ApplyAssetIdentities(items);
                    var records = await storage.Previews!.GetManyAsync(model.Tiles.Where(tile => tile.AssetId.HasValue)
                        .Select(tile => tile.AssetId!.Value).ToArray());
                    foreach (var record in records.Values)
                    {
                        if (record.ThumbnailRelativePath is not null)
                        {
                            var path = MediaPathSemantics.ResolveContained(storage.Locations.PreviewsDirectory, record.ThumbnailRelativePath);
                            if (File.Exists(path)) model.ApplyThumbnail(record.AssetId, path);
                        }
                        model.ApplyMetadata(record.AssetId, BrowserQueryEngine.ExtractMetadata(record.MetadataJson));
                    }
                    hydrationMs = watch.Elapsed.TotalMilliseconds;
                    watch.Restart();
                    foreach (var tile in model.Tiles.Where(tile => tile.HasThumbnail).Take(32))
                    {
                        using var stream = File.OpenRead(tile.ThumbnailPath!);
                        var bitmap = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                        Assert.True(bitmap.Frames[0].PixelWidth > 0);
                    }
                    decodeMs = watch.Elapsed.TotalMilliseconds;
                }
            }
        }
        finally
        {
            if (storage is not null) await storage.DisposeAsync();
            await File.WriteAllLinesAsync(report, lines);
            Directory.Delete(directory, true);
        }
    }

    private sealed class SlowFolders(IMediaFolderEnumerator inner) : IMediaFolderEnumerator
    {
        public async Task<MediaFolderEnumerationResult> EnumerateAsync(MediaFolderEnumerationRequest request,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(150, cancellationToken);
            return await inner.EnumerateAsync(request, cancellationToken);
        }
    }
}
