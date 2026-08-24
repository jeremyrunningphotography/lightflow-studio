using System.Diagnostics;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LightflowStudio;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class ThumbnailGenerationTests : IAsyncLifetime
{
    private readonly string _root = Directory.CreateTempSubdirectory("lightflow-thumbnails-").FullName;

    [Fact]
    public async Task MultiAssetRegeneration_PublishesEachCompletedPreviewBeforeBatchCompletion()
    {
        var first = Guid.NewGuid(); var second = Guid.NewGuid();
        var secondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondRelease = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var published = new List<PreviewRegenerationCompleted>();
        var progress = new SynchronousProgress<PreviewRegenerationCompleted>(published.Add);

        var batch = PreviewRegenerationBatch.RunAsync([first, second], async (assetId, token) =>
        {
            if (assetId == second) { secondStarted.TrySetResult(); await secondRelease.Task.WaitAsync(token); }
            return new(ThumbnailGenerationStatus.Succeeded, $"{assetId:N}.jpg");
        }, progress);
        await secondStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var completed = Assert.Single(published);
        Assert.Equal(first, completed.AssetId);
        Assert.False(batch.IsCompleted);
        secondRelease.TrySetResult();
        await batch;
        Assert.Equal([first, second], published.Select(item => item.AssetId));
    }

    [Fact]
    public async Task OverlappingCameraThenCreativeEnsureCurrent_CoalescesQueuedRendersToLatestIdentity()
    {
        await using var fixture = await ThumbnailFixture.CreateAsync(_root);
        var ids = new List<Guid>();
        for (var index = 0; index < 3; index++)
        {
            var name = $"clip-{index}.mp4";
            await File.WriteAllTextAsync(Path.Combine(fixture.MediaRoot, name), "source");
            ids.Add(await fixture.AddAssetAsync(name, "video"));
        }
        using (var originals = fixture.Service(new FakeRenderer()))
            foreach (var id in ids) await originals.GenerateAsync(new(id));

        var cameraId = Guid.NewGuid(); var creativeId = Guid.NewGuid();
        var colors = new MutableColorStore(new(ids[0],
            new(cameraId, "Camera", "aa", LutResourceAvailability.Available), null, "camera-only"));
        var renderer = new BlockingFirstColorRenderer();
        var cache = new FakeLutCache(new Dictionary<Guid, string>
            { [cameraId] = "camera.cube", [creativeId] = "creative.cube" });
        var activity = new ThumbnailGenerationActivity();
        var activityStarts = 0;
        activity.Changed += (_, change) => { if (change.IsGenerating) Interlocked.Increment(ref activityStarts); };
        using var service = new ThumbnailGenerationService(fixture.Coordinator.MediaAssets,
            fixture.Coordinator.Previews!, fixture.Coordinator.Locations, renderer, maximumConcurrency: 1,
            colors: colors, lutCache: cache, activity: activity);
        Task<ThumbnailGenerationResult> Ensure(Guid id, CancellationToken token) =>
            service.GenerateAsync(new(id, ForceRefresh: false, Priority: ThumbnailPriority.Visible), token);

        var cameraPass = PreviewRegenerationBatch.RunAsync(ids, Ensure);
        await renderer.FirstStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        colors.Intent = new(ids[0],
            new(cameraId, "Camera", "aa", LutResourceAvailability.Available),
            new(creativeId, "Creative", "bb", LutResourceAvailability.Available), "camera-creative");
        var creativePass = PreviewRegenerationBatch.RunAsync(ids, Ensure);
        renderer.ReleaseFirst.TrySetResult();
        await Task.WhenAll(cameraPass, creativePass);

        Assert.Equal(4, renderer.VisualIdentities.Count);
        Assert.True(renderer.VisualIdentities.Count < ids.Count * 2); // six force renders would be redundant.
        Assert.Equal("camera-only", renderer.VisualIdentities[0]);
        Assert.Equal(3, renderer.VisualIdentities.Count(identity => identity == "camera-creative"));
        Assert.Equal(4, activityStarts); // Current/coalesced requests never acquire an activity lease.
        foreach (var id in ids)
            Assert.Equal("camera-creative", (await fixture.Coordinator.Previews!.GetAsync(id))!.ThumbnailVisualIdentity);
    }

    [Fact]
    public async Task ImageGeneration_RespectsExifOrientationAndPublishesAtomically()
    {
        await using var fixture = await ThumbnailFixture.CreateAsync(_root);
        var source = Path.Combine(fixture.MediaRoot, "portrait.jpg");
        WriteOrientedJpeg(source, orientation: 6);
        var assetId = await fixture.AddAssetAsync("portrait.jpg", "image");
        using var service = new ThumbnailGenerationService(fixture.Coordinator.MediaAssets,
            fixture.Coordinator.Previews!, fixture.Coordinator.Locations,
            new CompositeThumbnailRenderer(new WicImageThumbnailRenderer(), new FakeVideoRenderer()));

        var result = await service.GenerateAsync(new(assetId));
        var dimensions = ReadDimensions(result.ThumbnailPath!);
        var record = await fixture.Coordinator.Previews!.GetAsync(assetId);

        Assert.Equal(ThumbnailGenerationStatus.Succeeded, result.Status);
        Assert.Equal((1, 2), dimensions);
        Assert.Equal(PreviewComponentState.Current, record!.ThumbnailState);
        Assert.Equal(ThumbnailGenerationService.CurrentGeneratorVersion, record.ThumbnailGeneratorVersion);
        Assert.DoesNotContain(Directory.EnumerateFiles(Path.GetDirectoryName(result.ThumbnailPath!)!),
            path => path.EndsWith(".lightflow", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task VideoGeneration_UsesPackagedFfmpegAtRepresentativePosition()
    {
        var dependencies = PlaybackDependencyLocator.FindSharedLibraries()
            ?? throw new InvalidOperationException("Run scripts/Get-PlaybackDependencies.ps1 before integration tests.");
        await using var fixture = await ThumbnailFixture.CreateAsync(_root);
        var source = Path.Combine(fixture.MediaRoot, "clip.mkv");
        Run(Path.Combine(dependencies, "ffmpeg.exe"), "-hide_banner", "-loglevel", "error", "-y",
            "-f", "lavfi", "-i", "testsrc2=size=320x180:rate=10:duration=2", "-c:v", "ffv1", source);
        var assetId = await fixture.AddAssetAsync("clip.mkv", "video");
        var metadata = new FakeMetadataService(10);
        using var service = new ThumbnailGenerationService(fixture.Coordinator.MediaAssets,
            fixture.Coordinator.Previews!, fixture.Coordinator.Locations,
            new CompositeThumbnailRenderer(new WicImageThumbnailRenderer(),
                new FfmpegVideoThumbnailRenderer(Path.Combine(dependencies, "ffmpeg.exe"), new ProbeProcessRunner())), metadata);

        var result = await service.GenerateAsync(new(assetId));
        var dimensions = ReadDimensions(result.ThumbnailPath!);

        Assert.Equal(ThumbnailGenerationStatus.Succeeded, result.Status);
        Assert.Equal((512, 288), dimensions);
        Assert.Equal(1, metadata.CallCount);
    }

    [Fact]
    public async Task CurrentThumbnail_PersistsAcrossReopenAndIsReused()
    {
        await using var fixture = await ThumbnailFixture.CreateAsync(_root);
        await File.WriteAllTextAsync(Path.Combine(fixture.MediaRoot, "photo.jpg"), "source");
        var assetId = await fixture.AddAssetAsync("photo.jpg", "image");
        var renderer = new FakeRenderer();
        using (var service = fixture.Service(renderer))
            Assert.Equal(ThumbnailGenerationStatus.Succeeded, (await service.GenerateAsync(new(assetId))).Status);
        await fixture.ReopenAsync();
        using var reopened = fixture.Service(renderer);

        var current = await reopened.GenerateAsync(new(assetId));

        Assert.Equal(ThumbnailGenerationStatus.Current, current.Status);
        Assert.True(File.Exists(current.ThumbnailPath));
        Assert.Equal(1, renderer.CallCount);
    }

    [Fact]
    public async Task SourceAndGeneratorVersionChanges_RegenerateDeterministicPath()
    {
        await using var fixture = await ThumbnailFixture.CreateAsync(_root);
        var source = Path.Combine(fixture.MediaRoot, "photo.jpg");
        await File.WriteAllTextAsync(source, "first");
        var assetId = await fixture.AddAssetAsync("photo.jpg", "image");
        var renderer = new FakeRenderer();
        using var service = fixture.Service(renderer);
        var first = await service.GenerateAsync(new(assetId));
        var record = (await fixture.Coordinator.Previews!.GetAsync(assetId))!;
        await fixture.Coordinator.Previews.SetArtifactAsync(assetId, PreviewArtifactKind.Thumbnail,
            new(99, PreviewComponentState.Current, record.ThumbnailRelativePath));

        var versionRefresh = await service.GenerateAsync(new(assetId));
        await File.AppendAllTextAsync(source, " changed");
        var sourceRefresh = await service.GenerateAsync(new(assetId));

        Assert.Equal(ThumbnailGenerationStatus.Succeeded, versionRefresh.Status);
        Assert.Equal(ThumbnailGenerationStatus.Succeeded, sourceRefresh.Status);
        Assert.Equal(first.ThumbnailPath, versionRefresh.ThumbnailPath);
        Assert.NotEqual(first.ThumbnailPath, sourceRefresh.ThumbnailPath);
        Assert.Equal(3, renderer.CallCount);
        Assert.Contains(Path.Combine("thumbnails", assetId.ToString("N")[..2], assetId.ToString("N").Substring(2, 2)),
            sourceRefresh.ThumbnailPath);
    }

    [Fact]
    public async Task SourceChangedDuringGeneration_DoesNotPublishAndStableRetrySucceeds()
    {
        await using var fixture = await ThumbnailFixture.CreateAsync(_root);
        var source = Path.Combine(fixture.MediaRoot, "photo.jpg");
        await File.WriteAllTextAsync(source, "first");
        var assetId = await fixture.AddAssetAsync("photo.jpg", "image");
        using (var initial = fixture.Service(new FakeRenderer())) await initial.GenerateAsync(new(assetId));
        var before = (await fixture.Coordinator.Previews!.GetAsync(assetId))!;
        var blocking = new ReleasableRenderer();
        using var changing = fixture.Service(blocking);
        var operation = changing.GenerateAsync(new(assetId, ForceRefresh: true));
        await blocking.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await File.AppendAllTextAsync(source, " changed during generation");
        blocking.Release.TrySetResult();

        var changed = await operation;
        var after = (await fixture.Coordinator.Previews.GetAsync(assetId))!;

        Assert.Equal(ThumbnailGenerationStatus.SourceChanged, changed.Status);
        Assert.Equal(PreviewComponentState.Stale, after.ThumbnailState);
        Assert.NotEqual(before.Source, after.Source);
        Assert.Equal(before.ThumbnailRelativePath, after.ThumbnailRelativePath);
        Assert.True(File.Exists(MediaPathSemantics.ResolveContained(fixture.Coordinator.Locations.PreviewsDirectory,
            after.ThumbnailRelativePath!)));
        Assert.Empty(Directory.EnumerateFiles(fixture.Coordinator.Locations.ThumbnailCacheDirectory,
            "*.lightflow", SearchOption.AllDirectories));

        using var stable = fixture.Service(new FakeRenderer());
        var retried = await stable.GenerateAsync(new(assetId));
        Assert.Equal(ThumbnailGenerationStatus.Succeeded, retried.Status);
        Assert.Equal(PreviewComponentState.Current,
            (await fixture.Coordinator.Previews.GetAsync(assetId))!.ThumbnailState);
    }

    [Fact]
    public async Task MaintenanceWaitsForActiveThumbnailPublicationAndRetainsPublishedFile()
    {
        await using var fixture = await ThumbnailFixture.CreateAsync(_root);
        await File.WriteAllTextAsync(Path.Combine(fixture.MediaRoot, "photo.jpg"), "source");
        var assetId = await fixture.AddAssetAsync("photo.jpg", "image");
        using var operations = new PreviewOperationCoordinator();
        var renderer = new ReleasableRenderer();
        using var thumbnails = new ThumbnailGenerationService(fixture.Coordinator.MediaAssets,
            fixture.Coordinator.Previews!, fixture.Coordinator.Locations, renderer, operations: operations);
        using var metadata = new FakeMetadataService(1);
        using var maintenance = new PreviewMaintenanceService(fixture.Coordinator.Previews!,
            fixture.Coordinator.MediaAssets, metadata, thumbnails, operations, fixture.Coordinator.Locations);
        var generation = thumbnails.GenerateAsync(new(assetId));
        await renderer.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var cleanup = maintenance.CleanupAsync(new(long.MaxValue, TimeSpan.Zero, TimeSpan.Zero));
        await Task.Delay(50);
        Assert.False(cleanup.IsCompleted);
        renderer.Release.TrySetResult();

        var generated = await generation.WaitAsync(TimeSpan.FromSeconds(5));
        await cleanup.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(File.Exists(generated.ThumbnailPath));
        Assert.Equal(PreviewComponentState.Current,
            (await fixture.Coordinator.Previews!.GetAsync(assetId))!.ThumbnailState);
    }

    [Fact]
    public async Task CancellationAndInvalidOutput_CleanTemporaryFilesWithoutPublishingCurrent()
    {
        await using var fixture = await ThumbnailFixture.CreateAsync(_root);
        await File.WriteAllTextAsync(Path.Combine(fixture.MediaRoot, "photo.jpg"), "source");
        var assetId = await fixture.AddAssetAsync("photo.jpg", "image");
        var blocking = new CancellationRenderer();
        using var canceledService = fixture.Service(blocking);
        using var cancellation = new CancellationTokenSource();
        var operation = canceledService.GenerateAsync(new(assetId), cancellation.Token);
        await blocking.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
        Assert.Empty(Directory.EnumerateFiles(fixture.Coordinator.Locations.ThumbnailCacheDirectory,
            "*.lightflow", SearchOption.AllDirectories));
        Assert.Equal(PreviewComponentState.Missing,
            (await fixture.Coordinator.Previews!.GetAsync(assetId))!.ThumbnailState);

        using var invalidService = fixture.Service(new InvalidRenderer());
        var invalid = await invalidService.GenerateAsync(new(assetId));
        Assert.Equal(ThumbnailGenerationStatus.InvalidOutput, invalid.Status);
        Assert.Equal(PreviewComponentState.Failed,
            (await fixture.Coordinator.Previews.GetAsync(assetId))!.ThumbnailState);
        Assert.Empty(Directory.EnumerateFiles(fixture.Coordinator.Locations.ThumbnailCacheDirectory,
            "*.lightflow", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task MissingAndOfflineSources_RetainCurrentThumbnailAndCatalogIdentity()
    {
        await using var fixture = await ThumbnailFixture.CreateAsync(_root);
        var source = Path.Combine(fixture.MediaRoot, "photo.jpg");
        await File.WriteAllTextAsync(source, "source");
        var assetId = await fixture.AddAssetAsync("photo.jpg", "image");
        using var service = fixture.Service(new FakeRenderer());
        var generated = await service.GenerateAsync(new(assetId));
        var catalogId = fixture.Coordinator.CatalogSession.Identity.CatalogId;
        File.Delete(source);

        var missing = await service.GenerateAsync(new(assetId, ForceRefresh: true));
        Directory.Delete(fixture.MediaRoot, recursive: true);
        var offline = await service.GenerateAsync(new(assetId, ForceRefresh: true));

        Assert.Equal(ThumbnailGenerationStatus.SourceMissing, missing.Status);
        Assert.Equal(generated.ThumbnailPath, missing.ThumbnailPath);
        Assert.Equal(ThumbnailGenerationStatus.RootUnavailable, offline.Status);
        Assert.Equal(generated.ThumbnailPath, offline.ThumbnailPath);
        Assert.True(File.Exists(generated.ThumbnailPath));
        Assert.Equal(catalogId, fixture.Coordinator.CatalogSession.Identity.CatalogId);
        Assert.Equal(PreviewComponentState.Current,
            (await fixture.Coordinator.Previews!.GetAsync(assetId))!.ThumbnailState);
    }

    [Fact]
    public async Task ExplicitForceRefresh_RebuildsEvenWhenPreviewIdentityIsCurrent()
    {
        await using var fixture = await ThumbnailFixture.CreateAsync(_root);
        await File.WriteAllTextAsync(Path.Combine(fixture.MediaRoot, "photo.jpg"), "source");
        var assetId = await fixture.AddAssetAsync("photo.jpg", "image");
        var renderer = new FakeRenderer();
        using var service = fixture.Service(renderer);
        await service.GenerateAsync(new(assetId));

        var forced = await service.GenerateAsync(new(assetId, ForceRefresh: true));

        Assert.Equal(ThumbnailGenerationStatus.Succeeded, forced.Status);
        Assert.Equal(2, renderer.CallCount);
    }


    [Fact]
    public async Task ColorIdentity_ChangesVisualDerivativeWithoutChangingSourceIdentity_AndOrdersStages()
    {
        await using var fixture = await ThumbnailFixture.CreateAsync(_root);
        var source = Path.Combine(fixture.MediaRoot, "clip.mp4");
        await File.WriteAllTextAsync(source, "unchanged source");
        var assetId = await fixture.AddAssetAsync("clip.mp4", "video");
        var cameraId = Guid.NewGuid(); var creativeId = Guid.NewGuid();
        var colors = new MutableColorStore(new(assetId, null, null, PreviewVisualIdentity.Original));
        var cache = new FakeLutCache(new Dictionary<Guid, string> { [cameraId] = "camera.cube", [creativeId] = "creative.cube" });
        var renderer = new ColorAwareRenderer();
        using var service = new ThumbnailGenerationService(fixture.Coordinator.MediaAssets,
            fixture.Coordinator.Previews!, fixture.Coordinator.Locations, renderer,
            colors: colors, lutCache: cache);

        var original = await service.GenerateAsync(new(assetId));
        var sourceIdentity = (await fixture.Coordinator.Previews!.GetAsync(assetId))!.Source;
        colors.Intent = new(assetId,
            new(cameraId, "Camera", "aa", LutResourceAvailability.Available),
            new(creativeId, "Creative", "bb", LutResourceAvailability.Available), "camera-then-creative");
        var colored = await service.GenerateAsync(new(assetId));
        var record = (await fixture.Coordinator.Previews.GetAsync(assetId))!;

        Assert.NotEqual(original.ThumbnailPath, colored.ThumbnailPath);
        Assert.Equal(sourceIdentity, record.Source);
        Assert.Equal("camera-then-creative", record.ThumbnailVisualIdentity);
        Assert.Equal(["camera.cube", "creative.cube"], renderer.LastColor!.OrderedLutPaths);
        Assert.Equal("unchanged source", await File.ReadAllTextAsync(source));
    }

    [Fact]
    public async Task ColorChangeDuringGeneration_DoesNotOverwritePriorThumbnail_AndActivityAlwaysClears()
    {
        await using var fixture = await ThumbnailFixture.CreateAsync(_root);
        await File.WriteAllTextAsync(Path.Combine(fixture.MediaRoot, "clip.mp4"), "source");
        var assetId = await fixture.AddAssetAsync("clip.mp4", "video");
        var colors = new MutableColorStore(new(assetId, null, null, PreviewVisualIdentity.Original));
        var cache = new FakeLutCache(new Dictionary<Guid, string>());
        using (var initial = new ThumbnailGenerationService(fixture.Coordinator.MediaAssets,
                   fixture.Coordinator.Previews!, fixture.Coordinator.Locations, new ColorAwareRenderer(),
                   colors: colors, lutCache: cache))
            await initial.GenerateAsync(new(assetId));
        var before = (await fixture.Coordinator.Previews!.GetAsync(assetId))!;
        var renderer = new BlockingColorRenderer();
        var activity = new ThumbnailGenerationActivity();
        var changes = new List<bool>();
        activity.Changed += (_, change) => { if (change.AssetId == assetId) changes.Add(change.IsGenerating); };
        using var service = new ThumbnailGenerationService(fixture.Coordinator.MediaAssets,
            fixture.Coordinator.Previews, fixture.Coordinator.Locations, renderer,
            colors: colors, lutCache: cache, activity: activity);
        var generation = service.GenerateAsync(new(assetId, ForceRefresh: true));
        await renderer.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        colors.Intent = new(assetId, new(Guid.NewGuid(), "Camera", "cc", LutResourceAvailability.Available),
            null, "newer-color");
        renderer.Release.TrySetResult();

        var result = await generation;
        var after = (await fixture.Coordinator.Previews.GetAsync(assetId))!;
        Assert.Equal(ThumbnailGenerationStatus.SourceChanged, result.Status);
        Assert.Equal(before.ThumbnailRelativePath, after.ThumbnailRelativePath);
        Assert.Equal([true, false], changes);
    }

    [Fact]
    public async Task MissingAssignedLut_IsFailedRetryableAndRetainsExistingPixels()
    {
        await using var fixture = await ThumbnailFixture.CreateAsync(_root);
        await File.WriteAllTextAsync(Path.Combine(fixture.MediaRoot, "clip.mp4"), "source");
        var assetId = await fixture.AddAssetAsync("clip.mp4", "video");
        var colors = new MutableColorStore(new(assetId, null, null, PreviewVisualIdentity.Original));
        var cache = new FakeLutCache(new Dictionary<Guid, string>());
        using var service = new ThumbnailGenerationService(fixture.Coordinator.MediaAssets,
            fixture.Coordinator.Previews!, fixture.Coordinator.Locations, new ColorAwareRenderer(),
            colors: colors, lutCache: cache);
        var original = await service.GenerateAsync(new(assetId));
        colors.Intent = new(assetId, new(Guid.NewGuid(), "Missing", "dd", LutResourceAvailability.Missing), null, "missing-color");

        var failed = await service.GenerateAsync(new(assetId));
        var record = (await fixture.Coordinator.Previews!.GetAsync(assetId))!;
        Assert.Equal(ThumbnailGenerationStatus.Failed, failed.Status);
        Assert.Equal(original.ThumbnailPath, failed.ThumbnailPath);
        Assert.Equal(PreviewComponentState.Failed, record.ThumbnailState);
        Assert.Equal("missing-color", record.ThumbnailVisualIdentity);
    }

    [Fact]
    public async Task CanceledQueuedWaiter_DoesNotConsumeCapacityAndLaterWorkRuns()
    {
        using var gate = new PriorityAsyncGate(1);
        using var first = await gate.EnterAsync(ThumbnailPriority.Normal, CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        var canceled = gate.EnterAsync(ThumbnailPriority.Normal, cancellation.Token);
        var later = gate.EnterAsync(ThumbnailPriority.Normal, CancellationToken.None);
        cancellation.Cancel();
        first.Dispose();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceled);
        using var laterLease = await later.WaitAsync(TimeSpan.FromSeconds(5));
        laterLease.Dispose();
        using var subsequent = await gate.EnterAsync(ThumbnailPriority.Normal, CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task CancellationRacingWithAssignment_DoesNotLeakSlot()
    {
        using var cancellation = new CancellationTokenSource();
        var assignments = 0;
        using var gate = new PriorityAsyncGate(1, () =>
        {
            if (Interlocked.Increment(ref assignments) == 1) cancellation.Cancel();
        });
        using var first = await gate.EnterAsync(ThumbnailPriority.Normal, CancellationToken.None);
        var raced = gate.EnterAsync(ThumbnailPriority.Normal, cancellation.Token);
        var later = gate.EnterAsync(ThumbnailPriority.Normal, CancellationToken.None);
        first.Dispose();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => raced);
        using var laterLease = await later.WaitAsync(TimeSpan.FromSeconds(5));
        laterLease.Dispose();
        using var subsequent = await gate.EnterAsync(ThumbnailPriority.Normal, CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ActiveLeases_NeverExceedConfiguredMaximum()
    {
        const int maximum = 2;
        using var gate = new PriorityAsyncGate(maximum);
        var active = 0;
        var maximumObserved = 0;
        var tasks = Enumerable.Range(0, 24).Select(async index =>
        {
            using var lease = await gate.EnterAsync((index % 3) switch
            {
                0 => ThumbnailPriority.Visible,
                1 => ThumbnailPriority.Normal,
                _ => ThumbnailPriority.Background
            }, CancellationToken.None);
            var current = Interlocked.Increment(ref active);
            int observed;
            while (current > (observed = Volatile.Read(ref maximumObserved)) &&
                   Interlocked.CompareExchange(ref maximumObserved, current, observed) != observed) { }
            await Task.Delay(5);
            Interlocked.Decrement(ref active);
        });

        await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(maximum, maximumObserved);
        Assert.Equal(0, active);
    }

    [Fact]
    public async Task PriorityOrdering_RemainsIntactAfterCanceledWaiters()
    {
        using var gate = new PriorityAsyncGate(1);
        using var first = await gate.EnterAsync(ThumbnailPriority.Background, CancellationToken.None);
        var background = gate.EnterAsync(ThumbnailPriority.Background, CancellationToken.None);
        var normal = gate.EnterAsync(ThumbnailPriority.Normal, CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        var canceledVisible = gate.EnterAsync(ThumbnailPriority.Visible, cancellation.Token);
        var visible = gate.EnterAsync(ThumbnailPriority.Visible, CancellationToken.None);
        cancellation.Cancel();
        first.Dispose();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceledVisible);
        using var visibleLease = await visible.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(normal.IsCompleted);
        Assert.False(background.IsCompleted);
        visibleLease.Dispose();
        using var normalLease = await normal.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(background.IsCompleted);
        normalLease.Dispose();
        using var backgroundLease = await background.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task CatalogSchema_RemainsFreeOfThumbnailData()
    {
        await using var fixture = await ThumbnailFixture.CreateAsync(_root);
        using var connection = fixture.Coordinator.CatalogSession.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM sqlite_master WHERE name LIKE '%Thumbnail%' OR name LIKE '%Preview%';";
        Assert.Equal(0L, command.ExecuteScalar());
    }

    private static void WriteJpeg(string path, int width = 8, int height = 6)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var stride = width * 3;
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr24, null,
            new byte[stride * height], stride);
        var encoder = new JpegBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static void WriteOrientedJpeg(string path, int orientation)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var bitmap = BitmapSource.Create(2, 1, 96, 96, PixelFormats.Bgr24, null,
            new byte[] { 0, 10, 20, 30, 40, 50 }, 6);
        var metadata = new BitmapMetadata("jpg");
        metadata.SetQuery("/app1/ifd/{ushort=274}", (ushort)orientation);
        var encoder = new JpegBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap, null, metadata, null));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static (int Width, int Height) ReadDimensions(string path)
    {
        using var stream = File.OpenRead(path);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
        return (decoder.Frames[0].PixelWidth, decoder.Frames[0].PixelHeight);
    }

    private static void Run(string executable, params string[] arguments)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException($"Could not start {executable}.");
        var error = process.StandardError.ReadToEnd(); process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException(error);
    }

    private sealed class FakeRenderer : IThumbnailRenderer
    {
        public int CallCount { get; private set; }
        public Task<ThumbnailRenderResult> RenderAsync(string sourcePath, string mediaType, TimeSpan videoPosition,
            string destinationPath, CancellationToken cancellationToken = default)
        { cancellationToken.ThrowIfCancellationRequested(); CallCount++; WriteJpeg(destinationPath); return Task.FromResult(new ThumbnailRenderResult(ThumbnailGenerationStatus.Succeeded)); }
    }

    private sealed class SynchronousProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    private sealed class ColorAwareRenderer : IThumbnailRenderer
    {
        public ThumbnailColorRender? LastColor { get; private set; }
        public Task<ThumbnailRenderResult> RenderAsync(string sourcePath, string mediaType, TimeSpan videoPosition,
            string destinationPath, CancellationToken cancellationToken = default)
            => RenderAsync(sourcePath, mediaType, videoPosition, destinationPath, ThumbnailColorRender.Original, cancellationToken);
        public Task<ThumbnailRenderResult> RenderAsync(string sourcePath, string mediaType, TimeSpan videoPosition,
            string destinationPath, ThumbnailColorRender color, CancellationToken cancellationToken = default)
        { LastColor = color; WriteJpeg(destinationPath); return Task.FromResult(new ThumbnailRenderResult(ThumbnailGenerationStatus.Succeeded)); }
    }

    private sealed class BlockingColorRenderer : IThumbnailRenderer
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<ThumbnailRenderResult> RenderAsync(string sourcePath, string mediaType, TimeSpan videoPosition,
            string destinationPath, CancellationToken cancellationToken = default)
            => RenderAsync(sourcePath, mediaType, videoPosition, destinationPath, ThumbnailColorRender.Original, cancellationToken);
        public async Task<ThumbnailRenderResult> RenderAsync(string sourcePath, string mediaType, TimeSpan videoPosition,
            string destinationPath, ThumbnailColorRender color, CancellationToken cancellationToken = default)
        { Started.TrySetResult(); await Release.Task.WaitAsync(cancellationToken); WriteJpeg(destinationPath); return new(ThumbnailGenerationStatus.Succeeded); }
    }

    private sealed class BlockingFirstColorRenderer : IThumbnailRenderer
    {
        private int _calls;
        public TaskCompletionSource FirstStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseFirst { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<string> VisualIdentities { get; } = [];
        public Task<ThumbnailRenderResult> RenderAsync(string sourcePath, string mediaType, TimeSpan videoPosition,
            string destinationPath, CancellationToken cancellationToken = default) =>
            RenderAsync(sourcePath, mediaType, videoPosition, destinationPath, ThumbnailColorRender.Original, cancellationToken);
        public async Task<ThumbnailRenderResult> RenderAsync(string sourcePath, string mediaType, TimeSpan videoPosition,
            string destinationPath, ThumbnailColorRender color, CancellationToken cancellationToken = default)
        {
            lock (VisualIdentities) VisualIdentities.Add(color.VisualIdentity);
            if (Interlocked.Increment(ref _calls) == 1)
            {
                FirstStarted.TrySetResult();
                await ReleaseFirst.Task.WaitAsync(cancellationToken);
            }
            WriteJpeg(destinationPath);
            return new(ThumbnailGenerationStatus.Succeeded);
        }
    }

    private sealed class MutableColorStore(AssetColorIntent intent) : IAssetColorStore
    {
        public AssetColorIntent Intent { get; set; } = intent;
        public Task<AssetColorIntent> GetAsync(Guid assetId, CancellationToken cancellationToken = default) => Task.FromResult(Intent);
        public Task<IReadOnlyDictionary<Guid, AssetColorIntent>> GetAsync(IReadOnlyCollection<Guid> assetIds, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<Guid, AssetColorIntent>>(assetIds.ToDictionary(id => id, _ => Intent));
        public Task SetStageAsync(IReadOnlyCollection<Guid> assetIds, ColorLutStage stage, Guid? lutId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SetAsync(IReadOnlyCollection<ColorAssignmentChange> changes, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeLutCache(IReadOnlyDictionary<Guid, string> paths) : ILutLibraryCache
    {
        public Task InitializeAsync(string cameraFolder, string creativeFolder, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task InitializeAsync(string cameraFolder, bool cameraIncludeSubfolders, string creativeFolder, bool creativeIncludeSubfolders, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<LutLibrarySnapshot> RefreshAsync(ColorLutStage stage, string folder, CancellationToken cancellationToken = default) => Task.FromResult(Snapshot(stage));
        public Task<LutLibrarySnapshot> RefreshAsync(ColorLutStage stage, string folder, bool includeSubfolders, CancellationToken cancellationToken = default) => Task.FromResult(Snapshot(stage));
        public LutLibrarySnapshot Snapshot(ColorLutStage stage) => new("", [], []);
        public ManagedLutResource? Get(ColorLutStage stage, Guid lutId) => null;
        public string ResolvePath(ColorLutStage stage, Guid lutId) => paths.TryGetValue(lutId, out var path) ? path : throw new FileNotFoundException("Assigned LUT unavailable.");
        public Task<CubeLutData> GetRuntimeAsync(ColorLutStage stage, Guid lutId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeVideoRenderer : IVideoThumbnailRenderer
    {
        public Task<ThumbnailRenderResult> RenderAsync(string sourcePath, TimeSpan position, string destinationPath,
            CancellationToken cancellationToken = default) => Task.FromResult(new ThumbnailRenderResult(ThumbnailGenerationStatus.Unsupported));
    }

    private sealed class ReleasableRenderer : IThumbnailRenderer
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<ThumbnailRenderResult> RenderAsync(string sourcePath, string mediaType, TimeSpan videoPosition,
            string destinationPath, CancellationToken cancellationToken = default)
        { Started.TrySetResult(); await Release.Task.WaitAsync(cancellationToken); WriteJpeg(destinationPath); return new(ThumbnailGenerationStatus.Succeeded); }
    }

    private sealed class CancellationRenderer : IThumbnailRenderer
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<ThumbnailRenderResult> RenderAsync(string sourcePath, string mediaType, TimeSpan videoPosition,
            string destinationPath, CancellationToken cancellationToken = default)
        { await File.WriteAllTextAsync(destinationPath, "partial", cancellationToken); Started.TrySetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); throw new UnreachableException(); }
    }

    private sealed class InvalidRenderer : IThumbnailRenderer
    {
        public async Task<ThumbnailRenderResult> RenderAsync(string sourcePath, string mediaType, TimeSpan videoPosition,
            string destinationPath, CancellationToken cancellationToken = default)
        { await File.WriteAllTextAsync(destinationPath, "not an image", cancellationToken); return new(ThumbnailGenerationStatus.Succeeded); }
    }

    private sealed class FakeMetadataService(double duration) : IDerivedMediaMetadataService
    {
        public int CallCount { get; private set; }
        public Task<DerivedMetadataResult> ProbeAsync(Guid assetId, bool forceRefresh = false, CancellationToken cancellationToken = default)
        {
            CallCount++; return Task.FromResult(new DerivedMetadataResult(DerivedMetadataStatus.Current,
            new(DerivedMediaKind.Video, "matroska", duration, 0, 1, null, null, null, null)));
        }
        public void Dispose() { }
    }

    private sealed class ThumbnailFixture : IAsyncDisposable
    {
        private readonly string _appData;
        private MediaRootInfo _mediaRoot = null!;
        public LightflowStorageCoordinator Coordinator { get; private set; } = null!;
        public string MediaRoot { get; private set; } = null!;
        private ThumbnailFixture(string appData) => _appData = appData;
        public static async Task<ThumbnailFixture> CreateAsync(string root)
        {
            var fixture = new ThumbnailFixture(Path.Combine(root, "appdata"));
            fixture.Coordinator = (await LightflowStorageCoordinator.StartAsync(fixture._appData)).Coordinator!;
            fixture.MediaRoot = Path.Combine(root, "media"); Directory.CreateDirectory(fixture.MediaRoot);
            fixture._mediaRoot = (await fixture.Coordinator.MediaRoots.CreateAsync("Media", fixture.MediaRoot)).Root!;
            return fixture;
        }
        public async Task<Guid> AddAssetAsync(string relativePath, string mediaType) =>
            (await Coordinator.MediaAssets.CreateAsync(_mediaRoot.RootId, relativePath, mediaType)).Asset!.Asset.AssetId;
        public ThumbnailGenerationService Service(IThumbnailRenderer renderer) => new(Coordinator.MediaAssets,
            Coordinator.Previews!, Coordinator.Locations, renderer);
        public async Task ReopenAsync() { await Coordinator.DisposeAsync(); Coordinator = (await LightflowStorageCoordinator.StartAsync(_appData)).Coordinator!; }
        public async ValueTask DisposeAsync() => await Coordinator.DisposeAsync();
    }

    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync() { try { Directory.Delete(_root, true); } catch { } return Task.CompletedTask; }
}
