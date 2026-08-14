using LightflowStudio;
using Microsoft.Data.Sqlite;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class PreviewPersistenceTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"lightflow-previews-{Guid.NewGuid():N}");
    private static PreviewSourceIdentity Source(string fingerprint = "abcdef0123456789", long size = 100, long write = 200) =>
        new(size, write, 1, fingerprint);

    [Fact]
    public async Task Record_PersistsAcrossServiceReopen_ByStableAssetId()
    {
        var locations = LightflowStorageLocations.Create(_root);
        var assetId = Guid.NewGuid();
        await using (var first = new PreviewStoreService(locations))
            await first.ObserveSourceAsync(assetId, Source());

        await using var reopened = new PreviewStoreService(locations);
        var record = await reopened.GetAsync(assetId);

        Assert.Equal(assetId, record!.AssetId);
        Assert.Equal(Source(), record.Source);
        Assert.True(File.Exists(locations.PreviewsDatabasePath));
    }

    [Fact]
    public async Task CustomPreviewLocation_OwnsDatabaseAndCachePaths()
    {
        var custom = Path.Combine(_root, "preview-cache-on-other-drive");
        var locations = LightflowStorageLocations.Create(_root, new(null, custom));
        await using var store = new PreviewStoreService(locations);
        await store.ObserveSourceAsync(Guid.NewGuid(), Source());

        Assert.Equal(Path.Combine(custom, LightflowStorageLocations.PreviewsFileName), locations.PreviewsDatabasePath);
        Assert.True(File.Exists(locations.PreviewsDatabasePath));
        Assert.StartsWith(Path.GetFullPath(custom), store.GetArtifactPath(Guid.NewGuid(), PreviewArtifactKind.Thumbnail, 1, Source(), "jpg"));
        Assert.False(File.Exists(LightflowStorageLocations.Create(_root).PreviewsDatabasePath));
    }

    [Fact]
    public async Task ComponentVersionsAndSourceChanges_DriveStaleness()
    {
        var store = new PreviewStoreService(LightflowStorageLocations.Create(_root));
        var assetId = Guid.NewGuid();
        await store.ObserveSourceAsync(assetId, Source());
        await store.SetMetadataAsync(assetId, new(3, PreviewComponentState.Current, PayloadJson: "{\"codec\":\"h264\"}"));
        await store.SetArtifactAsync(assetId, PreviewArtifactKind.Thumbnail,
            new(7, PreviewComponentState.Current, "thumbnails/a.jpg"));

        var unchanged = await store.ObserveSourceAsync(assetId, Source());
        var changed = await store.ObserveSourceAsync(assetId, Source("deadbeef01234567", 101, 201));

        Assert.Equal(3, unchanged.MetadataProbeVersion);
        Assert.Equal(7, unchanged.ThumbnailGeneratorVersion);
        Assert.Equal(PreviewComponentState.Current, unchanged.MetadataState);
        Assert.Equal(PreviewComponentState.Current, unchanged.ThumbnailState);
        Assert.Equal(PreviewComponentState.Stale, changed.MetadataState);
        Assert.Equal(PreviewComponentState.Stale, changed.ThumbnailState);
        Assert.Equal("{\"codec\":\"h264\"}", changed.MetadataJson);
        await store.DisposeAsync();
    }

    [Fact]
    public async Task UnavailableSource_RetainsPreviewRecordAndGeneratedState()
    {
        await using var store = new PreviewStoreService(LightflowStorageLocations.Create(_root));
        var assetId = Guid.NewGuid();
        await store.ObserveSourceAsync(assetId, Source());
        await store.SetArtifactAsync(assetId, PreviewArtifactKind.Thumbnail,
            new(1, PreviewComponentState.Current, "thumbnails/retained.jpg"));

        var offline = await store.SetSourceAvailabilityAsync(assetId, PreviewSourceAvailability.Unavailable);

        Assert.Equal(PreviewSourceAvailability.Unavailable, offline!.SourceAvailability);
        Assert.Equal(PreviewComponentState.Current, offline.ThumbnailState);
        Assert.Equal("thumbnails/retained.jpg", offline.ThumbnailRelativePath);
        Assert.NotNull(await store.GetAsync(assetId));
    }

    [Fact]
    public async Task CachePaths_AreDeterministicVersionedAndTwoLevelPartitioned()
    {
        await using var store = new PreviewStoreService(LightflowStorageLocations.Create(_root));
        var assetId = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");

        var first = store.GetArtifactPath(assetId, PreviewArtifactKind.Thumbnail, 4, Source(), ".JPG");
        var repeated = store.GetArtifactPath(assetId, PreviewArtifactKind.Thumbnail, 4, Source(), "jpg");
        var changed = store.GetArtifactPath(assetId, PreviewArtifactKind.Thumbnail, 5, Source(), "jpg");
        var changedSource = store.GetArtifactPath(assetId, PreviewArtifactKind.Thumbnail, 4, Source(write: 201), "jpg");

        Assert.Equal(first, repeated);
        Assert.NotEqual(first, changed);
        Assert.NotEqual(first, changedSource);
        Assert.Contains(Path.Combine("thumbnails", "00", "11"), first);
        Assert.Contains("-g4-f1-", first);
        Assert.EndsWith(".jpg", first);
    }

    [Fact]
    public async Task CatalogAssetId_AssociatesWithoutWritingPreviewDataToCatalog()
    {
        var coordinator = (await LightflowStorageCoordinator.StartAsync(_root)).Coordinator!;
        var rootPath = Path.Combine(_root, "media");
        Directory.CreateDirectory(rootPath);
        await File.WriteAllBytesAsync(Path.Combine(rootPath, "clip.mp4"), [1, 2, 3, 4]);
        var root = await coordinator.MediaRoots.CreateAsync("Media", rootPath);
        var asset = await coordinator.MediaAssets.CreateAsync(root.Root!.RootId, "clip.mp4", "video");

        var preview = await coordinator.Previews!.ObserveSourceAsync(asset.Asset!.Asset.AssetId, Source());
        Directory.Delete(rootPath, true);
        var offline = await coordinator.MediaAssets.ObserveAsync(asset.Asset.Asset.AssetId);
        var retained = await coordinator.Previews.GetAsync(asset.Asset.Asset.AssetId);

        Assert.Equal(asset.Asset.Asset.AssetId, preview.AssetId);
        Assert.Equal(MediaAssetOperationStatus.RootUnavailable, offline.Status);
        Assert.NotNull(retained);
        using var connection = coordinator.CatalogSession.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM sqlite_master WHERE name='PreviewRecords';";
        Assert.Equal(0L, command.ExecuteScalar());
        await coordinator.DisposeAsync();
    }

    [Fact]
    public async Task DeletingEntirePreviewStore_DoesNotAffectCatalogData()
    {
        var coordinator = (await LightflowStorageCoordinator.StartAsync(_root)).Coordinator!;
        var catalogId = coordinator.CatalogSession.Identity.CatalogId;
        await using (var previewStore = new PreviewStoreService(coordinator.Locations))
            await previewStore.ObserveSourceAsync(Guid.NewGuid(), Source());

        Directory.Delete(coordinator.Locations.PreviewsDirectory, true);

        Assert.True(File.Exists(coordinator.Locations.CatalogDatabasePath));
        Assert.Equal(catalogId, coordinator.CatalogSession.Identity.CatalogId);
        using var connection = coordinator.CatalogSession.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT CatalogId FROM CatalogInfo WHERE SingletonId=1;";
        Assert.Equal(catalogId.ToString("D"), command.ExecuteScalar());
        await coordinator.DisposeAsync();
    }

    [Fact]
    public async Task PreviewRelocation_QuiescesAndReopensPersistentIndexAtDestination()
    {
        var coordinator = (await LightflowStorageCoordinator.StartAsync(_root)).Coordinator!;
        var assetId = Guid.NewGuid();
        await coordinator.Previews!.ObserveSourceAsync(assetId, Source());
        var catalogId = coordinator.CatalogSession.Identity.CatalogId;
        var destination = Path.Combine(_root, "relocated-previews");

        var result = await coordinator.RelocatePreviewsAsync(destination, PreviewRelocationMode.MoveExisting);

        Assert.True(result.Succeeded);
        Assert.Equal(Path.Combine(destination, LightflowStorageLocations.PreviewsFileName), coordinator.Locations.PreviewsDatabasePath);
        Assert.Equal(assetId, (await coordinator.Previews!.GetAsync(assetId))!.AssetId);
        Assert.Equal(catalogId, coordinator.CatalogSession.Identity.CatalogId);
        await coordinator.DisposeAsync();
    }

    [Fact]
    public async Task ForeignDatabase_IsRejectedRatherThanAdopted()
    {
        var locations = LightflowStorageLocations.Create(_root);
        Directory.CreateDirectory(locations.PreviewsDirectory);
        using (var connection = new SqliteConnection($"Data Source={locations.PreviewsDatabasePath}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE ForeignData(Value TEXT);";
            command.ExecuteNonQuery();
        }
        await using var store = new PreviewStoreService(locations);

        await Assert.ThrowsAsync<InvalidDataException>(() => store.GetAsync(Guid.NewGuid()));

        using var verify = new SqliteConnection($"Data Source={locations.PreviewsDatabasePath};Mode=ReadOnly");
        verify.Open();
        using var check = verify.CreateCommand();
        check.CommandText = "SELECT count(*) FROM sqlite_master WHERE name='ForeignData';";
        Assert.Equal(1L, check.ExecuteScalar());
    }

    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync() { try { Directory.Delete(_root, true); } catch { } return Task.CompletedTask; }
}
