using System.Diagnostics;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LightflowStudio;
using Xunit;
using Xunit.Abstractions;

namespace LightflowStudio.Tests;

public sealed class BoundedPreviewWorkTests(ITestOutputHelper output)
{
    [Fact]
    public async Task UnchangedFailureAndOwnedOutputReachQuiescence()
    {
        var watch = Stopwatch.StartNew();
        await using var f = await Fixture.CreateAsync();
        await File.WriteAllTextAsync(Path.Combine(f.Media, "bad.mp4"), "deterministically invalid video");
        var first = await f.RefreshAsync();
        var id = Assert.Single(first.Reconciliation.Items).AssetId;
        Assert.Equal(PreviewComponentState.Failed, (await f.Previews.GetAsync(id))!.MetadataState);
        for (var i = 1; i < 20; i++) await f.RefreshAsync();
        Assert.Equal(1, f.Probe.Calls);
        Assert.Equal(1, f.Renderer.Calls);
        output.WriteLine($"20 completed submissions: probes={f.Probe.Calls}, renders={f.Renderer.Calls}, elapsed={watch.ElapsedMilliseconds}ms. All later submissions settle without executing equivalent work.");

        WriteJpeg(Path.Combine(f.Media, "valid.jpg"));
        await f.RefreshAsync();
        var counts = new List<int>();
        for (var i = 0; i < 4; i++)
        {
            var files = Directory.GetFiles(f.Locations.PreviewsDirectory, "*.jpg", SearchOption.AllDirectories);
            foreach (var file in files) f.Watcher.Publish(new(f.RootId, MediaRootChangeKind.Created, file));
            await f.Monitor.FlushAsync();
            await f.DrainAsync();
            counts.Add(Directory.GetFiles(f.Locations.PreviewsDirectory, "*.jpg", SearchOption.AllDirectories).Length);
        }
        output.WriteLine($"Generated JPEG count across four watcher waves: {string.Join(",", counts)}; elapsed={watch.ElapsedMilliseconds}ms");
        Assert.All(counts, count => Assert.Equal(1, count));
        Assert.Equal(2, (await f.Storage.MediaAssets.ListAsync()).Count);
    }

    [Fact]
    public async Task PersistedDeadlinesSurviveRevisitRestartAndNoiseButAllowMeaningfulRetries()
    {
        await using var f = await Fixture.CreateAsync();
        var path = Path.Combine(f.Media, "bad.mp4");
        await File.WriteAllTextAsync(path, "invalid video");
        var first = await f.RefreshAsync();
        var id = Assert.Single(first.Reconciliation.Items).AssetId;
        var deadline = (await f.Previews.GetAsync(id))!.MetadataRetryAfterUtc;
        for (var i = 0; i < 78; i++)
        {
            f.Clock.Now += TimeSpan.FromSeconds(12);
            f.Watcher.Publish(new(f.RootId, MediaRootChangeKind.Changed, path));
            await f.Monitor.FlushAsync(); await f.DrainAsync(); await f.RefreshAsync();
        }
        Assert.Equal(1, f.Probe.Calls); Assert.Equal(1, f.Renderer.Calls);
        Assert.Equal(deadline, (await f.Previews.GetAsync(id))!.MetadataRetryAfterUtc);
        await f.RestartAsync();
        await f.RefreshAsync();
        Assert.Equal(1, f.Probe.Calls); Assert.Equal(1, f.Renderer.Calls);

        // Advance beyond cooldown without real sleeps; retry happens on demand, not a timer.
        f.Clock.Now += TimeSpan.FromMinutes(31);
        await f.RefreshAsync(); await f.RefreshAsync();
        Assert.Equal(2, f.Probe.Calls); Assert.Equal(2, f.Renderer.Calls);
        await File.AppendAllTextAsync(path, "material change");
        await f.RefreshAsync(); await f.RefreshAsync();
        Assert.Equal(3, f.Probe.Calls); Assert.Equal(3, f.Renderer.Calls);
        await f.Previews.SetMetadataAsync(id, new(99, PreviewComponentState.Failed));
        await f.Previews.SetArtifactAsync(id, PreviewArtifactKind.Thumbnail, new(99, PreviewComponentState.Failed));
        await f.RefreshAsync(); await f.RefreshAsync();
        Assert.Equal(4, f.Probe.Calls); Assert.Equal(4, f.Renderer.Calls);
        await f.Metadata.ProbeAsync(id, forceRefresh: true);
        await f.Thumbnails.GenerateAsync(new(id, ForceRefresh: true));
        Assert.Equal(5, f.Probe.Calls); Assert.Equal(5, f.Renderer.Calls);
        output.WriteLine("78 watcher waves + 78 revisits over 15.6 simulated minutes: one probe/render; restart unchanged; deadline/source/version/force each permit exactly one retry.");
    }

    [Fact]
    public async Task ConcurrentGeneratorInstancesSharePersistedFailureAndDoNotRepeatExecutions()
    {
        await using var f = await Fixture.CreateAsync();
        await File.WriteAllTextAsync(Path.Combine(f.Media, "bad.mp4"), "invalid");
        var reconciliation = await f.Storage.CatalogReconciliation.ReconcileAsync(new(f.RootId));
        var id = Assert.Single(reconciliation.Items).AssetId;
        using var metadata = new DerivedMediaMetadataService(f.Storage.MediaAssets, f.Previews, f.Probe, time: f.Clock);
        using var thumbnails = new ThumbnailGenerationService(f.Storage.MediaAssets, f.Previews, f.Locations, f.Renderer,
            metadata, time: f.Clock);
        await Task.WhenAll(Enumerable.Range(0, 20).Select(i => (i % 2 == 0 ? f.Metadata : metadata).ProbeAsync(id)));
        await Task.WhenAll(Enumerable.Range(0, 20).Select(i => (i % 2 == 0 ? f.Thumbnails : thumbnails).GenerateAsync(new(id))));
        Assert.Equal(1, f.Probe.Calls); Assert.Equal(1, f.Renderer.Calls);
    }

    [Fact]
    public async Task OwnedPathsAreExcludedBeforeQueueingAndExternalRenameSidesStillRefresh()
    {
        await using var f = await Fixture.CreateAsync();
        WriteJpeg(Path.Combine(f.Media, "valid.jpg"));
        await f.RefreshAsync();
        var owned = Directory.GetFiles(f.Locations.PreviewsDirectory, "*.jpg", SearchOption.AllDirectories).Single();
        foreach (var kind in new[] { MediaRootChangeKind.Created, MediaRootChangeKind.Changed, MediaRootChangeKind.Deleted })
            f.Watcher.Publish(new(f.RootId, kind, owned));
        await f.Monitor.FlushAsync();
        Assert.Empty(f.Batches);
        var external = Path.Combine(f.Media, "external.jpg"); WriteJpeg(external);
        f.Watcher.Publish(new(f.RootId, MediaRootChangeKind.Renamed, external, owned));
        await f.Monitor.FlushAsync(); Assert.Single(f.Batches); await f.DrainAsync();
        Assert.Equal(2, f.Renderer.Calls);
        f.Watcher.Publish(new(f.RootId, MediaRootChangeKind.Renamed, owned, external));
        await f.Monitor.FlushAsync(); Assert.Single(f.Batches); await f.DrainAsync();
        var direct = await f.Discovery.RefreshAsync(new(f.RootId, "candidate-cache/thumbnails"));
        await direct.DerivedWork!.Completion;
        Assert.Empty(direct.Reconciliation.Items);
        Directory.CreateDirectory(Path.Combine(f.Media, "candidate-cache-external"));
        WriteJpeg(Path.Combine(f.Media, "candidate-cache-external", "valid.jpg"));
        var neighbor = await f.Discovery.RefreshAsync(new(f.RootId, "candidate-cache-external"));
        await neighbor.DerivedWork!.Completion;
        Assert.Single(neighbor.Reconciliation.Items);
    }

    [Fact]
    public async Task RepeatedCooldownExpiryRemainsBoundedWithoutPermanentlyPoisoningSource()
    {
        await using var f = await Fixture.CreateAsync();
        await File.WriteAllTextAsync(Path.Combine(f.Media, "bad.mp4"), "invalid");
        for (var interval = 0; interval < 48; interval++)
        {
            for (var noise = 0; noise < 5; noise++) await f.RefreshAsync();
            Assert.Equal(interval + 1, f.Probe.Calls);
            Assert.Equal(interval + 1, f.Renderer.Calls);
            Assert.Equal(0, f.Scheduler.Diagnostics.Outstanding);
            f.Clock.Now += PreviewRetryPolicy.Cooldown + TimeSpan.FromSeconds(1);
        }
        output.WriteLine("24 simulated hours: 240 refreshes, 48 attempts per component, queue idle after every wave.");
    }

    [Fact]
    public async Task FailedProbeForChangingSourceDoesNotPoisonNewFingerprint()
    {
        await using var f = await Fixture.CreateAsync();
        var path = Path.Combine(f.Media, "bad.mp4");
        await File.WriteAllTextAsync(path, "invalid");
        var reconciliation = await f.Storage.CatalogReconciliation.ReconcileAsync(new(f.RootId));
        var id = Assert.Single(reconciliation.Items).AssetId;
        f.Probe.BeforeResult = () => File.AppendAllTextAsync(path, "changed during probe");
        var result = await f.Metadata.ProbeAsync(id);
        Assert.Equal(DerivedMetadataStatus.SourceChanged, result.Status);
        Assert.Null((await f.Previews.GetAsync(id))!.MetadataRetryAfterUtc);
        f.Probe.BeforeResult = null;
        await f.RefreshAsync(); await f.RefreshAsync();
        Assert.Equal(2, f.Probe.Calls);
        Assert.Equal(1, f.Renderer.Calls);
    }

    [Fact]
    public async Task TimedOutProbePersistsFailureAndDoesNotRepeatOnAutomaticDemand()
    {
        await using var f = await Fixture.CreateAsync();
        var path = Path.Combine(f.Media, "bad.mp4");
        await File.WriteAllTextAsync(path, "invalid");
        var runner = new TimeoutRunner();
        using var metadata = new DerivedMediaMetadataService(f.Storage.MediaAssets, f.Previews,
            new FfprobeMediaMetadataReader(path, runner), time: f.Clock);
        await using var scheduler = new DerivedWorkScheduler(f.Storage.MediaAssets, f.Previews, metadata, f.Thumbnails, time: f.Clock);
        var discovery = new MediaDiscoveryRefreshService(f.Storage.CatalogReconciliation, () => scheduler);
        for (var i = 0; i < 20; i++)
        {
            var result = await discovery.RefreshAsync(new(f.RootId));
            await result.DerivedWork!.Completion;
            Assert.Equal(0, scheduler.Diagnostics.Outstanding);
        }
        Assert.Equal(1, runner.Calls);
        var failed = (await f.Previews.ListAsync()).Single();
        Assert.Equal(PreviewComponentState.Failed, failed.MetadataState);
        Assert.NotNull(failed.MetadataRetryAfterUtc);
    }

    private sealed class TimeoutRunner : IProbeProcessRunner
    {
        public int Calls;
        public Task<ProbeProcessResult> RunAsync(string executable, IReadOnlyList<string> arguments, CancellationToken cancellationToken = default)
        { Calls++; throw new TimeoutException("Deterministic probe timeout."); }
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    internal static void WriteJpeg(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var bitmap = BitmapSource.Create(8, 6, 96, 96, PixelFormats.Bgr24, null, new byte[144], 24);
        var encoder = new JpegBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public string DirectoryPath = Path.Combine(Path.GetTempPath(), "bounded-preview-" + Guid.NewGuid().ToString("N"));
        public string Media => Path.Combine(DirectoryPath, "media");
        public Guid RootId;
        public LightflowStorageCoordinator Storage = null!;
        public LightflowStorageLocations Locations = null!;
        public PreviewStoreService Previews = null!;
        public DerivedMediaMetadataService Metadata = null!;
        public ThumbnailGenerationService Thumbnails = null!;
        public DerivedWorkScheduler Scheduler = null!;
        public MediaDiscoveryRefreshService Discovery = null!;
        public MediaRootMonitoringService Monitor = null!;
        public WatcherFactory Watcher = new();
        public Clock Clock = new();
        public Probe Probe = new();
        public Renderer Renderer = new();
        public List<IDerivedWorkBatch> Batches = [];
        public static async Task<Fixture> CreateAsync()
        {
            var f = new Fixture();
            Directory.CreateDirectory(f.Media);
            f.Storage = (await LightflowStorageCoordinator.StartAsync(Path.Combine(f.DirectoryPath, "profile"))).Coordinator!;
            await f.Storage.MediaMonitoring!.DisposeAsync();
            f.RootId = (await f.Storage.MediaRoots.CreateAsync("Reproduction", f.Media)).Root!.RootId;
            f.Locations = LightflowStorageLocations.CreateAtRoot(Path.Combine(f.DirectoryPath, "derived"),
                new(PreviewsDirectory: Path.Combine(f.Media, "candidate-cache")));
            f.Previews = new(f.Locations, f.Clock);
            f.Metadata = new(f.Storage.MediaAssets, f.Previews, f.Probe, time: f.Clock);
            f.Thumbnails = new(f.Storage.MediaAssets, f.Previews, f.Locations, f.Renderer, f.Metadata, time: f.Clock);
            f.Scheduler = new(f.Storage.MediaAssets, f.Previews, f.Metadata, f.Thumbnails, maximumConcurrency: 1, time: f.Clock);
            var folders = new MediaFolderEnumerator(f.Storage.MediaRoots, MediaTypeRegistry.CreateDefault(), new MediaFolderFileSystem(),
                excludedPath: new OwnedStoragePaths(() => f.Locations).Contains);
            f.Discovery = new(new CatalogReconciliationService(folders, f.Storage.MediaAssets), () => f.Scheduler);
            f.Monitor = new(f.Storage.MediaRoots, new TrackingRefresh(f), f.Watcher, excludedPath: new OwnedStoragePaths(() => f.Locations).Contains);
            await f.Monitor.SynchronizeAsync();
            return f;
        }
        public async Task<MediaDiscoveryRefreshResult> RefreshAsync()
        {
            var result = await Discovery.RefreshAsync(new(RootId), DerivedWorkPriority.Visible);
            Assert.True(result.Reconciliation.Succeeded, result.Diagnostic);
            await result.DerivedWork!.Completion.WaitAsync(TimeSpan.FromSeconds(20));
            Assert.Equal(0, result.DerivedWork.Progress.Pending + result.DerivedWork.Progress.Running);
            return result;
        }
        public async Task RestartAsync()
        {
            await Scheduler.DisposeAsync(); Thumbnails.Dispose(); Metadata.Dispose(); await Previews.DisposeAsync();
            Previews = new(Locations, Clock);
            Metadata = new(Storage.MediaAssets, Previews, Probe, time: Clock);
            Thumbnails = new(Storage.MediaAssets, Previews, Locations, Renderer, Metadata, time: Clock);
            Scheduler = new(Storage.MediaAssets, Previews, Metadata, Thumbnails, maximumConcurrency: 1, time: Clock);
        }
        public async Task DrainAsync()
        {
            foreach (var batch in Batches.ToArray()) await batch.Completion.WaitAsync(TimeSpan.FromSeconds(20));
            Batches.Clear();
        }
        public async ValueTask DisposeAsync()
        {
            await Monitor.DisposeAsync(); await Scheduler.DisposeAsync(); Thumbnails.Dispose(); Metadata.Dispose();
            await Previews.DisposeAsync(); await Storage.DisposeAsync();
        }
    }
    private sealed class TrackingRefresh(Fixture f) : IMediaDiscoveryRefreshService
    {
        public async Task<MediaDiscoveryRefreshResult> RefreshAsync(MediaFolderEnumerationRequest request,
            DerivedWorkPriority priority = DerivedWorkPriority.Background, CancellationToken cancellationToken = default,
            CancellationToken derivedWorkCancellationToken = default)
        {
            var result = await f.Discovery.RefreshAsync(request, priority, cancellationToken, derivedWorkCancellationToken);
            if (result.DerivedWork is {} batch) f.Batches.Add(batch);
            return result;
        }
    }
    private sealed class WatcherFactory : IMediaRootWatcherFactory, IMediaRootWatcher
    {
        private Action<MediaRootChange> _publish = null!;
        public IMediaRootWatcher Create(MediaRootInfo root, Action<MediaRootChange> publish) { _publish = publish; return this; }
        public void Publish(MediaRootChange change) => _publish(change);
        public void Start() { }
        public void Dispose() { }
    }
    private sealed class Probe : IMediaMetadataProbe
    {
        public int Calls;
        public Func<Task>? BeforeResult;
        public async Task<MediaProbeResult> ProbeAsync(string path, string mediaType, long size, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Calls);
            if (BeforeResult is not null) await BeforeResult();
            return path.EndsWith(".mp4")
                ? new MediaProbeResult(DerivedMetadataStatus.Malformed, Diagnostic: "invalid video")
                : new MediaProbeResult(DerivedMetadataStatus.Succeeded, new(DerivedMediaKind.Image, "jpeg", null, null, size, null, null, null, null));
        }
    }
    private sealed class Renderer : IThumbnailRenderer
    {
        public int Calls;
        public Task<ThumbnailRenderResult> RenderAsync(string sourcePath, string mediaType, TimeSpan position, string destinationPath,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Calls);
            if (sourcePath.EndsWith(".mp4")) return Task.FromResult(new ThumbnailRenderResult(ThumbnailGenerationStatus.Failed, "invalid video"));
            WriteJpeg(destinationPath);
            return Task.FromResult(new ThumbnailRenderResult(ThumbnailGenerationStatus.Succeeded));
        }
    }
}

