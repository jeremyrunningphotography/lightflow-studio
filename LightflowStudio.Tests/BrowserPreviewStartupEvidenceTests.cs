using System.Collections.Concurrent;
using System.Diagnostics;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LightflowStudio;
using Xunit;
using Xunit.Abstractions;

namespace LightflowStudio.Tests;

public sealed class BrowserPreviewStartupEvidenceTests(ITestOutputHelper output)
{
    [Fact]
    public async Task MeasureRealFirstVisitCachedRevisitAndExplicitRegeneration()
    {
        var report = Environment.GetEnvironmentVariable("LIGHTFLOW_BROWSER_STARTUP_REPORT");
        if (string.IsNullOrEmpty(report)) return;
        var directory = Path.Combine(Path.GetTempPath(), "browser-startup-" + Guid.NewGuid().ToString("N"));
        var media = Directory.CreateDirectory(Path.Combine(directory, "media")).FullName;
        var clock = new Stopwatch();
        var events = new ConcurrentQueue<(string Stage, double Ms)>();
        void Mark(string stage) => events.Enqueue((stage, clock.Elapsed.TotalMilliseconds));
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "LightflowStudio.Browser",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity => Mark(activity.OperationName + ".complete")
        };
        ActivitySource.AddActivityListener(listener);
        var lines = new List<string>();
        await using var storage = (await LightflowStorageCoordinator.StartAsync(Path.Combine(directory, "app"))).Coordinator!;
        await storage.MediaMonitoring!.DisposeAsync();
        var root = (await storage.MediaRoots.CreateAsync("Evidence", media)).Root!;
        var bitmap = BitmapSource.Create(32, 32, 96, 96, PixelFormats.Bgr24, null, new byte[32 * 32 * 3], 32 * 3);
        var encoder = new JpegBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var bytes = new MemoryStream();
        encoder.Save(bytes);
        foreach (var scope in new[] { "direct", "recursive" })
        {
            for (var index = 0; index < 30; index++)
            {
                var folder = Directory.CreateDirectory(Path.Combine(media, scope,
                    scope == "recursive" ? $"sub{index % 3}" : "")).FullName;
                File.WriteAllBytes(Path.Combine(folder, $"image{index:D2}.jpg"), bytes.ToArray());
            }
        }
        await storage.BrowserRecursiveRoots.EnableAsync(root.RootId, "recursive");
        using var metadata = new TimedMetadata(DerivedMediaMetadataFactory.Create(storage.MediaAssets,
            storage.Previews!, storage.Settings), Mark);
        using var thumbnails = new ThumbnailGenerationService(storage.MediaAssets, storage.Previews!,
            storage.Locations, new TimedRenderer(Mark), metadata);
        var timedThumbnails = new TimedThumbnails(thumbnails, Mark);
        await using var scheduler = new DerivedWorkScheduler(storage.MediaAssets, storage.Previews!, metadata,
            timedThumbnails, artifactExists: relative => File.Exists(Path.Combine(storage.Locations.PreviewsDirectory, relative)));
        var timedScheduler = new TimedScheduler(scheduler, Mark);
        var discovery = new MediaDiscoveryRefreshService(storage.CatalogReconciliation, () => timedScheduler);
        foreach (var scope in new[] { "direct", "recursive" })
        foreach (var visit in new[] { "cold", "cached" })
        {
            events.Clear(); clock.Restart();
            using var navigation = new BrowserNavigationSession(storage.MediaRoots, storage.BrowserLocations,
                discovery, storage.MediaFolders, storage.BrowserRecursiveRoots, assets: storage.MediaAssets,
                mediaTypes: storage.MediaTypes);
            var presented = false;
            navigation.KnownContentAvailable += (_, _) => { presented = true; Mark("presentable"); };
            var state = (await navigation.NavigateToPathAsync(Path.Combine(media, scope)))!;
            if (!presented) Mark("presentable");
            Assert.Equal(0, navigation.WorkingGeneration);
            var complete = await state.DerivedWork!.Completion.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.Equal(0, complete.Failed);
            var snapshot = events.ToArray();
            if (visit == "cached") Assert.DoesNotContain(snapshot, item => item.Stage == "render.start");
            else Assert.Equal(30, snapshot.Count(item => item.Stage == "render.start"));
            lines.Add($"{scope}/{visit}: " + string.Join("; ", snapshot.GroupBy(item => item.Stage)
                .Select(group => $"{group.Key} first={group.Min(item => item.Ms):F1}ms count={group.Count()}")));
            if (visit == "cached")
            {
                events.Clear(); clock.Restart();
                var id = state.Reconciliation!.Items.First().AssetId;
                var regenerated = await timedThumbnails.GenerateAsync(new(id, ForceRefresh: true, Priority: ThumbnailPriority.Visible));
                Assert.True(regenerated.Succeeded);
                lines.Add($"{scope}/explicit: " + string.Join("; ", events.Select(item => $"{item.Stage}={item.Ms:F1}ms")));
            }
        }
        await File.WriteAllLinesAsync(report, lines);
        foreach (var line in lines) output.WriteLine(line);
        // Storage and WIC handles are disposed by the owning test; fixture remains under task-owned TEMP.
    }

    private sealed class TimedScheduler(IDerivedWorkScheduler inner, Action<string> mark) : IDerivedWorkScheduler
    {
        public DerivedWorkSchedulingResult TrySchedule(CatalogReconciliationResult reconciliation,
            DerivedWorkPriority priority = DerivedWorkPriority.Background, CancellationToken cancellationToken = default)
        {
            if (reconciliation.Items.Count > 0) mark("eligible+admission");
            return inner.TrySchedule(reconciliation, priority, cancellationToken);
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class TimedMetadata(IDerivedMediaMetadataService inner, Action<string> mark) : IDerivedMediaMetadataService
    {
        public async Task<DerivedMetadataResult> ProbeAsync(Guid assetId, bool forceRefresh = false,
            CancellationToken cancellationToken = default)
        {
            mark("worker.metadata.start");
            var result = await inner.ProbeAsync(assetId, forceRefresh, cancellationToken);
            mark("metadata.complete");
            return result;
        }
        public void Dispose() => inner.Dispose();
    }

    private sealed class TimedThumbnails(IThumbnailGenerationService inner, Action<string> mark) : IThumbnailGenerationService
    {
        public async Task<ThumbnailGenerationResult> GenerateAsync(ThumbnailRequest request, CancellationToken cancellationToken = default)
        {
            mark("thumbnail.start");
            var result = await inner.GenerateAsync(request, cancellationToken);
            if (result.Succeeded) mark("artifact.committed");
            return result;
        }
        public void Dispose() { }
    }

    private sealed class TimedRenderer(Action<string> mark) : IThumbnailRenderer
    {
        public async Task<ThumbnailRenderResult> RenderAsync(string sourcePath, string mediaType, TimeSpan videoPosition,
            string destinationPath, CancellationToken cancellationToken = default)
        {
            mark("render.start");
            var result = await new WicImageThumbnailRenderer().RenderAsync(sourcePath, destinationPath, cancellationToken);
            mark("render.complete");
            return result;
        }
    }
}
