using LightflowStudio;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class PreviewMaintenanceTests : IAsyncLifetime
{
    private readonly string _root = Directory.CreateTempSubdirectory("lightflow-preview-maintenance-").FullName;

    [Fact]
    public async Task UsageReportsDatabaseArtifactsTemporaryAndOrphanFiles()
    {
        await using var fixture = await Fixture.CreateAsync(_root);
        await fixture.AddArtifactAsync(Guid.NewGuid(), PreviewComponentState.Current, 12);
        var orphan = fixture.WriteCacheFile("thumbnails/orphan.jpg", 7, old: true);
        fixture.WriteCacheFile("previews/work.lightflow", 5, old: false);

        var usage = await fixture.Service.GetUsageAsync();

        Assert.True(usage.DatabaseBytes > 0);
        Assert.Equal(19, usage.ThumbnailBytes);
        Assert.Equal(5, usage.TemporaryBytes);
        Assert.Equal(3, usage.ArtifactCount);
        Assert.Equal(2, usage.OrphanCount);
        Assert.True(File.Exists(orphan));
    }

    [Fact]
    public async Task CleanupRemovesOldOrphanAndStaleThenEnforcesQuotaButRetainsOffline()
    {
        await using var fixture = await Fixture.CreateAsync(_root);
        var current = await fixture.AddArtifactAsync(Guid.NewGuid(), PreviewComponentState.Current, 11);
        var stale = await fixture.AddArtifactAsync(Guid.NewGuid(), PreviewComponentState.Stale, 7);
        var offline = await fixture.AddArtifactAsync(Guid.NewGuid(), PreviewComponentState.Stale, 9,
            PreviewSourceAvailability.Unavailable);
        await fixture.Store.SetMetadataAsync(stale.AssetId,
            new(1, PreviewComponentState.Stale, PayloadJson: "{\"stale\":true}"));
        await fixture.Store.SetMetadataAsync(offline.AssetId,
            new(1, PreviewComponentState.Stale, PayloadJson: "{\"offline\":true}"));
        var orphan = fixture.WriteCacheFile("thumbnails/old-orphan.jpg", 5, old: true);

        var result = await fixture.Service.CleanupAsync(new(9, TimeSpan.Zero, TimeSpan.Zero));

        Assert.True(result.Succeeded);
        Assert.False(File.Exists(orphan));
        Assert.False(File.Exists(stale.Path));
        Assert.False(File.Exists(current.Path));
        Assert.True(File.Exists(offline.Path));
        Assert.Equal(PreviewComponentState.Missing, (await fixture.Store.GetAsync(stale.AssetId))!.ThumbnailState);
        Assert.Equal(PreviewComponentState.Missing, (await fixture.Store.GetAsync(stale.AssetId))!.MetadataState);
        Assert.Equal(PreviewComponentState.Stale, (await fixture.Store.GetAsync(offline.AssetId))!.ThumbnailState);
        Assert.Equal(PreviewComponentState.Stale, (await fixture.Store.GetAsync(offline.AssetId))!.MetadataState);
        Assert.Equal(9, result.Usage!.CacheBytes);
    }

    [Fact]
    public async Task CleanupNeverDeletesDatabaseReferencedAsThumbnail()
    {
        await using var fixture = await Fixture.CreateAsync(_root);
        var assetId = Guid.NewGuid();
        await fixture.Store.ObserveSourceAsync(assetId, new(1, DateTime.UtcNow.Ticks, 1, "AA"));
        await fixture.Store.SetArtifactAsync(assetId, PreviewArtifactKind.Thumbnail,
            new(1, PreviewComponentState.Stale, "previews.db"));
        var databasePath = fixture.Locations.PreviewsDatabasePath;
        Assert.True(File.Exists(databasePath));

        await fixture.Service.CleanupAsync(new(long.MaxValue, TimeSpan.Zero, TimeSpan.Zero));

        Assert.True(File.Exists(databasePath));
        Assert.Equal(PreviewComponentState.Missing, (await fixture.Store.GetAsync(assetId))!.ThumbnailState);
    }

    [Fact]
    public async Task CleanupNeverDeletesCrossKindArtifactReference()
    {
        await using var fixture = await Fixture.CreateAsync(_root);
        var assetId = Guid.NewGuid();
        var standardPreview = fixture.WriteCacheFile("previews/protected.jpg", 9, old: true);
        await fixture.Store.ObserveSourceAsync(assetId, new(9, DateTime.UtcNow.Ticks, 1, "AA"));
        await fixture.Store.SetArtifactAsync(assetId, PreviewArtifactKind.Thumbnail,
            new(1, PreviewComponentState.Stale, "previews/protected.jpg"));

        await fixture.Service.CleanupAsync(new(long.MaxValue, TimeSpan.Zero, TimeSpan.Zero));

        Assert.True(File.Exists(standardPreview));
        Assert.Equal(PreviewComponentState.Missing, (await fixture.Store.GetAsync(assetId))!.ThumbnailState);
    }

    [Fact]
    public async Task CleanupDeletesValidThumbnailAndStandardPreviewArtifacts()
    {
        await using var fixture = await Fixture.CreateAsync(_root);
        var thumbnail = await fixture.AddArtifactAsync(Guid.NewGuid(), PreviewArtifactKind.Thumbnail,
            PreviewComponentState.Stale, 7);
        var standardPreview = await fixture.AddArtifactAsync(Guid.NewGuid(), PreviewArtifactKind.StandardPreview,
            PreviewComponentState.Stale, 8);

        await fixture.Service.CleanupAsync(new(long.MaxValue, TimeSpan.Zero, TimeSpan.Zero));

        Assert.False(File.Exists(thumbnail.Path));
        Assert.False(File.Exists(standardPreview.Path));
        Assert.Equal(PreviewComponentState.Missing,
            (await fixture.Store.GetAsync(thumbnail.AssetId))!.ThumbnailState);
        Assert.Equal(PreviewComponentState.Missing,
            (await fixture.Store.GetAsync(standardPreview.AssetId))!.StandardPreviewState);
    }

    [Fact]
    public async Task ClearRemovesAllPreviewRecordsAndArtifacts()
    {
        await using var fixture = await Fixture.CreateAsync(_root);
        await fixture.AddArtifactAsync(Guid.NewGuid(), PreviewComponentState.Current, 10);
        fixture.WriteCacheFile("previews/standard.jpg", 8, old: true);

        var result = await fixture.Service.ClearAsync();

        Assert.True(result.Succeeded);
        Assert.Empty(await fixture.Store.ListAsync());
        Assert.Empty(Directory.EnumerateFiles(fixture.Locations.ThumbnailCacheDirectory, "*", SearchOption.AllDirectories));
        Assert.Empty(Directory.EnumerateFiles(fixture.Locations.StandardPreviewCacheDirectory, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task ClearFailureRestoresMovedArtifactsAndRecords()
    {
        await using var fixture = await Fixture.CreateAsync(_root);
        var artifact = await fixture.AddArtifactAsync(Guid.NewGuid(), PreviewComponentState.Current, 10);
        using var service = fixture.CreateService(new FailingClearStore(fixture.Store));

        await Assert.ThrowsAsync<IOException>(() => service.ClearAsync());

        Assert.True(File.Exists(artifact.Path));
        Assert.NotNull(await fixture.Store.GetAsync(artifact.AssetId));
        Assert.Empty(Directory.EnumerateDirectories(fixture.Locations.PreviewsDirectory, ".lightflow-clearing-*"));
    }

    [Fact]
    public async Task RebuildClearsThenUsesExistingMetadataAndThumbnailServices()
    {
        var assets = new[] { Asset("one.jpg", "image"), Asset("two.mp4", "video") };
        await using var fixture = await Fixture.CreateAsync(_root, assets);
        await fixture.AddArtifactAsync(Guid.NewGuid(), PreviewComponentState.Current, 10);

        var result = await fixture.Service.RebuildAsync();

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Rebuilt);
        Assert.Equal(assets.Select(asset => asset.AssetId), fixture.Metadata.Requests);
        Assert.Equal(assets.Select(asset => asset.AssetId), fixture.Thumbnails.Requests);
        Assert.Empty(await fixture.Store.ListAsync());
    }

    [Fact]
    public async Task RebuildCancellationLeavesStoreValidAndRetryable()
    {
        var assets = new[] { Asset("one.jpg", "image"), Asset("two.jpg", "image") };
        await using var fixture = await Fixture.CreateAsync(_root, assets);
        using var cancellation = new CancellationTokenSource();
        fixture.Metadata.AfterProbe = () => cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            fixture.Service.RebuildAsync(cancellationToken: cancellation.Token));

        Assert.Empty(await fixture.Store.ListAsync());
        await fixture.Store.InitializeAsync();
        fixture.Metadata.AfterProbe = null;
        Assert.True((await fixture.Service.RebuildAsync()).Succeeded);
    }

    [Fact]
    public async Task CleanupWaitsForActiveGenerationAndDoesNotDeleteNewlyPublishedArtifact()
    {
        await using var fixture = await Fixture.CreateAsync(_root);
        using var generation = await fixture.Operations.EnterOperationAsync();
        var cleanup = fixture.Service.CleanupAsync(new(long.MaxValue, TimeSpan.Zero, TimeSpan.Zero));
        await Task.Delay(50);
        Assert.False(cleanup.IsCompleted);
        var artifact = await fixture.AddArtifactAsync(Guid.NewGuid(), PreviewComponentState.Current, 10);
        generation.Dispose();

        await cleanup.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(File.Exists(artifact.Path));
        Assert.Equal(PreviewComponentState.Current, (await fixture.Store.GetAsync(artifact.AssetId))!.ThumbnailState);
    }

    [Fact]
    public async Task CustomPreviewLocationIsUsedForAccountingAndCleanup()
    {
        var custom = Path.Combine(_root, "custom-preview-drive");
        await using var fixture = await Fixture.CreateAsync(_root, customPreviews: custom);
        var orphan = fixture.WriteCacheFile("thumbnails/orphan.jpg", 13, old: true);

        var before = await fixture.Service.GetUsageAsync();
        await fixture.Service.CleanupAsync(new(long.MaxValue, TimeSpan.FromDays(30), TimeSpan.Zero));

        Assert.Equal(Path.GetFullPath(custom), fixture.Locations.PreviewsDirectory);
        Assert.Equal(13, before.ThumbnailBytes);
        Assert.False(File.Exists(orphan));
    }

    [Fact]
    public async Task CoordinatorClearNeverChangesCatalogIdentityRootsAssetsOrSource()
    {
        var appData = Path.Combine(_root, "coordinator");
        await using var coordinator = (await LightflowStorageCoordinator.StartAsync(appData)).Coordinator!;
        var media = Directory.CreateDirectory(Path.Combine(_root, "media")).FullName;
        var source = Path.Combine(media, "photo.jpg");
        await File.WriteAllTextAsync(source, "precious source");
        var root = (await coordinator.MediaRoots.CreateAsync("Originals", media)).Root!;
        var asset = (await coordinator.MediaAssets.CreateAsync(root.RootId, "photo.jpg", "image")).Asset!.Asset;
        var catalogId = coordinator.CatalogSession.Identity.CatalogId;
        await coordinator.Previews!.ObserveSourceAsync(asset.AssetId,
            new(asset.FileSizeBytes, asset.LastWriteUtcTicks, asset.Fingerprint!.Version, asset.Fingerprint.Value));

        var result = await coordinator.ClearPreviewsAsync();

        Assert.True(result.Succeeded);
        Assert.Equal(catalogId, coordinator.CatalogSession.Identity.CatalogId);
        Assert.Equal(root.RootId, (await coordinator.MediaRoots.ListAsync()).Single().RootId);
        Assert.Equal(asset.AssetId, (await coordinator.MediaAssets.ListAsync()).Single().AssetId);
        Assert.Equal("precious source", await File.ReadAllTextAsync(source));
    }

    private static MediaAsset Asset(string relativePath, string mediaType)
    {
        var now = DateTimeOffset.UtcNow;
        return new(Guid.NewGuid(), Guid.NewGuid(), relativePath, relativePath.ToUpperInvariant(), mediaType,
            10, now.UtcTicks, new(1, "AA"), MediaAssetSourceStatus.Available, now, now, now);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly FakeAssets _assets;
        public LightflowStorageLocations Locations { get; }
        public PreviewStoreService Store { get; }
        public PreviewOperationCoordinator Operations { get; } = new();
        public FakeMetadata Metadata { get; } = new();
        public FakeThumbnails Thumbnails { get; } = new();
        public PreviewMaintenanceService Service { get; }

        private Fixture(LightflowStorageLocations locations, IReadOnlyList<MediaAsset> assets)
        {
            Locations = locations;
            Store = new(locations);
            _assets = new(assets);
            Service = new(Store, _assets, Metadata, Thumbnails, Operations, locations);
        }

        public static async Task<Fixture> CreateAsync(string root, IReadOnlyList<MediaAsset>? assets = null,
            string? customPreviews = null)
        {
            var locations = LightflowStorageLocations.Create(Path.Combine(root, Guid.NewGuid().ToString("N")),
                new(PreviewsDirectory: customPreviews));
            var fixture = new Fixture(locations, assets ?? []);
            await fixture.Store.InitializeAsync();
            return fixture;
        }

        public PreviewMaintenanceService CreateService(IPreviewStoreService store) =>
            new(store, _assets, Metadata, Thumbnails, Operations, Locations);

        public async Task<(Guid AssetId, string Path)> AddArtifactAsync(Guid assetId, PreviewComponentState state,
            int bytes, PreviewSourceAvailability availability = PreviewSourceAvailability.Available)
            => await AddArtifactAsync(assetId, PreviewArtifactKind.Thumbnail, state, bytes, availability);

        public async Task<(Guid AssetId, string Path)> AddArtifactAsync(Guid assetId, PreviewArtifactKind kind,
            PreviewComponentState state, int bytes,
            PreviewSourceAvailability availability = PreviewSourceAvailability.Available)
        {
            var source = new PreviewSourceIdentity(bytes, DateTime.UtcNow.Ticks, 1, "AA");
            await Store.ObserveSourceAsync(assetId, source);
            if (availability != PreviewSourceAvailability.Available)
                await Store.SetSourceAvailabilityAsync(assetId, availability);
            var path = Store.GetArtifactPath(assetId, kind, 1, source, "jpg");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllBytesAsync(path, new byte[bytes]);
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-60));
            var relative = Path.GetRelativePath(Locations.PreviewsDirectory, path).Replace('\\', '/');
            await Store.SetArtifactAsync(assetId, kind, new(1, state, relative));
            return (assetId, path);
        }

        public string WriteCacheFile(string relativePath, int bytes, bool old)
        {
            var path = Path.Combine(Locations.PreviewsDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, new byte[bytes]);
            if (old) File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-2));
            return path;
        }

        public async ValueTask DisposeAsync()
        {
            Service.Dispose();
            await Store.DisposeAsync();
            Operations.Dispose();
        }
    }

    private sealed class FakeAssets(IReadOnlyList<MediaAsset> assets) : IMediaAssetService
    {
        public Task<IReadOnlyList<MediaAsset>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(assets);
        public Task<MediaAssetOperationResult> CreateAsync(Guid rootId, string relativePath, string mediaType = "unknown", CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MediaAssetResolution?> GetAsync(Guid assetId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MediaAssetResolution?> FindAsync(Guid rootId, string relativePath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MediaAssetOperationResult> ObserveAsync(Guid assetId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeMetadata : IDerivedMediaMetadataService
    {
        public List<Guid> Requests { get; } = [];
        public Action? AfterProbe { get; set; }
        public Task<DerivedMetadataResult> ProbeAsync(Guid assetId, bool forceRefresh = false,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(assetId);
            AfterProbe?.Invoke();
            return Task.FromResult(new DerivedMetadataResult(DerivedMetadataStatus.Succeeded,
                new(DerivedMediaKind.Image, "jpeg", null, null, 10, null, null, null,
                    new("jpeg", 1, 1, 8, 1, null, null, null, null))));
        }
        public void Dispose() { }
    }

    private sealed class FakeThumbnails : IThumbnailGenerationService
    {
        public List<Guid> Requests { get; } = [];
        public Task<ThumbnailGenerationResult> GenerateAsync(ThumbnailRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request.AssetId);
            return Task.FromResult(new ThumbnailGenerationResult(ThumbnailGenerationStatus.Succeeded));
        }
        public void Dispose() { }
    }

    private sealed class FailingClearStore(IPreviewStoreService inner) : IPreviewStoreService
    {
        public Task InitializeAsync(CancellationToken cancellationToken = default) => inner.InitializeAsync(cancellationToken);
        public Task<PreviewRecord?> GetAsync(Guid assetId, CancellationToken cancellationToken = default) => inner.GetAsync(assetId, cancellationToken);
        public Task<IReadOnlyList<PreviewRecord>> ListAsync(CancellationToken cancellationToken = default) => inner.ListAsync(cancellationToken);
        public Task<PreviewRecord> ObserveSourceAsync(Guid assetId, PreviewSourceIdentity source, CancellationToken cancellationToken = default) => inner.ObserveSourceAsync(assetId, source, cancellationToken);
        public Task<PreviewRecord?> SetSourceAvailabilityAsync(Guid assetId, PreviewSourceAvailability availability, CancellationToken cancellationToken = default) => inner.SetSourceAvailabilityAsync(assetId, availability, cancellationToken);
        public Task<PreviewRecord?> SetMetadataAsync(Guid assetId, PreviewComponentUpdate update, CancellationToken cancellationToken = default) => inner.SetMetadataAsync(assetId, update, cancellationToken);
        public Task<PreviewRecord?> ClearMetadataAsync(Guid assetId, CancellationToken cancellationToken = default) => inner.ClearMetadataAsync(assetId, cancellationToken);
        public Task<PreviewRecord?> SetArtifactAsync(Guid assetId, PreviewArtifactKind kind, PreviewComponentUpdate update, CancellationToken cancellationToken = default) => inner.SetArtifactAsync(assetId, kind, update, cancellationToken);
        public Task<PreviewRecord?> ClearArtifactAsync(Guid assetId, PreviewArtifactKind kind, CancellationToken cancellationToken = default) => inner.ClearArtifactAsync(assetId, kind, cancellationToken);
        public Task ClearAllAsync(CancellationToken cancellationToken = default) => throw new IOException("Injected clear failure.");
        public string GetArtifactPath(Guid assetId, PreviewArtifactKind kind, int generatorVersion, PreviewSourceIdentity source, string extension) => inner.GetArtifactPath(assetId, kind, generatorVersion, source, extension);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync() { try { Directory.Delete(_root, true); } catch { } return Task.CompletedTask; }
}
