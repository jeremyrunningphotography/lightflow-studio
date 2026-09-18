using LightflowStudio;
using Microsoft.Data.Sqlite;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class TimelineMarkerTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"lightflow-markers-{Guid.NewGuid():N}");
    private LightflowStorageLocations _locations = null!;
    private CatalogDatabaseSession _session = null!;
    private readonly Guid _rootId = Guid.NewGuid(), _asset = Guid.NewGuid(), _other = Guid.NewGuid();
    private CatalogMarkerService Store => new(() => _session);
    public async Task InitializeAsync()
    {
        _locations = LightflowStorageLocations.Create(_root);
        _session = (await new CatalogDatabaseService(_locations).CreateNewAsync()).Session!;
        Execute($"INSERT INTO MediaRoots(RootId,DisplayName,SourceStatus,CreatedUtc,UpdatedUtc) VALUES ('{_rootId:D}','Media','online','{DateTime.UtcNow:O}','{DateTime.UtcNow:O}');");
        foreach (var id in new[] { _asset, _other }) Execute($"""
            INSERT INTO MediaAssets(AssetId,RootId,RelativePath,RelativePathKey,MediaType,FileSizeBytes,LastWriteUtcTicks,SourceStatus,CreatedUtc,UpdatedUtc)
            VALUES ('{id:D}','{_rootId:D}','{id:D}.mov','{id:D}.mov','video',1,1,'available','{DateTime.UtcNow:O}','{DateTime.UtcNow:O}');
            """);
    }
    private void Execute(string sql)
    {
        using var connection = _session.OpenConnection(); using var command = connection.CreateCommand();
        command.CommandText = sql; command.ExecuteNonQuery();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(333667)]
    [InlineData(123456789012345)]
    public async Task ExactTicksIdentityAndNameSurviveRestart(long ticks)
    {
        var created = await Store.CreateAsync(_asset, TimeSpan.FromTicks(ticks));
        await Store.RenameAsync(created.Marker.MarkerId, 1, "  夏 🎬  ");
        await _session.DisposeAsync();
        _session = (await new CatalogDatabaseService(_locations).OpenExistingAsync()).Session!;
        var marker = Assert.Single(await Store.ListAsync(_asset));
        Assert.Equal(created.Marker.MarkerId, marker.MarkerId);
        Assert.Equal(ticks, marker.Position.Ticks);
        Assert.Equal("夏 🎬", marker.Name);
        Assert.Equal(2, marker.Revision);
        Assert.Equal(created.Marker.CreatedUtc, marker.CreatedUtc);
        Assert.True(marker.UpdatedUtc >= marker.CreatedUtc);
    }

    [Fact]
    public async Task ConcurrentDuplicateAddsReturnOneIdentityWithoutChangingRevision()
    {
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Store.CreateAsync(_asset, TimeSpan.FromTicks(123))));
        Assert.Single(results, r => r.Created);
        Assert.Single(results.Select(r => r.Marker.MarkerId).Distinct());
        Assert.Equal(1, Assert.Single(await Store.ListAsync(_asset)).Revision);
        Assert.NotEqual(results[0].Marker.MarkerId, (await Store.CreateAsync(_other, TimeSpan.FromTicks(123))).Marker.MarkerId);
        Assert.True((await Store.CreateAsync(_asset, TimeSpan.FromTicks(124))).Created);
    }

    [Fact]
    public async Task StaleRenameAndDeleteDoNotLosePreciousEdits()
    {
        var marker = (await Store.CreateAsync(_asset, TimeSpan.Zero)).Marker;
        await Store.RenameAsync(marker.MarkerId, 1, "first");
        await Assert.ThrowsAsync<MarkerConcurrencyException>(() => Store.RenameAsync(marker.MarkerId, 1, "stale"));
        await Assert.ThrowsAsync<MarkerConcurrencyException>(() => Store.DeleteAsync(marker.MarkerId, 1));
        Assert.Equal("first", Assert.Single(await Store.ListAsync(_asset)).Name);
        await Store.RenameAsync(marker.MarkerId, 2, "");
        Assert.Equal("", Assert.Single(await Store.ListAsync(_asset)).Name);
        await Store.DeleteAsync(marker.MarkerId, 3);
        Assert.Empty(await Store.ListAsync(_asset));
        await Assert.ThrowsAsync<MarkerConcurrencyException>(() => Store.DeleteAsync(marker.MarkerId, 3));
    }

    [Fact]
    public async Task InvalidPositionsAndUnknownAssetsCannotCreateMarkers()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Store.CreateAsync(_asset, TimeSpan.FromTicks(-1)));
        await Assert.ThrowsAsync<SqliteException>(() => Store.CreateAsync(Guid.NewGuid(), TimeSpan.Zero));
        await Store.CreateAsync(_asset, TimeSpan.Zero);
        Assert.Throws<SqliteException>(() => Execute($"DELETE FROM MediaAssets WHERE AssetId='{_asset:D}'"));
    }

    [Fact]
    public async Task NavigationIsStrictOrderedAndDoesNotWrapOrCrossAssets()
    {
        foreach (var ticks in new long[] { 300, 100, 200 }) await Store.CreateAsync(_asset, TimeSpan.FromTicks(ticks));
        await Store.CreateAsync(_other, TimeSpan.FromTicks(250));
        var markers = await Store.ListAsync(_asset);
        Assert.Equal(new long[] { 100, 200, 300 }, markers.Select(m => m.Position.Ticks));
        Assert.Equal(100, MarkerNavigation.Previous(markers, TimeSpan.FromTicks(200))!.Position.Ticks);
        Assert.Equal(300, MarkerNavigation.Next(markers, TimeSpan.FromTicks(200))!.Position.Ticks);
        Assert.Null(MarkerNavigation.Previous(markers, TimeSpan.FromTicks(100)));
        Assert.Null(MarkerNavigation.Next(markers, TimeSpan.FromTicks(300)));
        Assert.Equal(.5, MarkerNavigation.Fraction(TimeSpan.FromTicks(200), TimeSpan.FromTicks(400)));
        Assert.Equal(0, MarkerNavigation.Fraction(TimeSpan.Zero, TimeSpan.Zero));
    }

    [Fact]
    public async Task OfflineRelocationAndPreviewRebuildPreserveMarkersWithoutSourceWrites()
    {
        var source = Path.Combine(_root, "disposable.mov");
        await File.WriteAllBytesAsync(source, [1, 2, 3, 4]);
        var marker = (await Store.CreateAsync(_asset, TimeSpan.FromTicks(1234567))).Marker;
        var assets = new CatalogMediaAssetRepository(() => _session);
        Assert.Equal(MediaAssetOperationStatus.Succeeded, await assets.RelocateAsync(_asset, _rootId, "moved.mov", DateTimeOffset.UtcNow));
        Execute($"UPDATE MediaAssets SET SourceStatus='missing' WHERE AssetId='{_asset:D}'");
        await using (var previews = new PreviewStoreService(_locations))
            await previews.ObserveSourceAsync(_asset, new(1, 1, 1, "abcd"));
        Directory.Delete(_locations.PreviewsDirectory, true);
        Assert.Equal(marker, Assert.Single(await Store.ListAsync(_asset)));
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, await File.ReadAllBytesAsync(source));
    }

    [Fact]
    public async Task BackupRestoreRecoversIdentityRevisionAndExactPosition()
    {
        var marker = (await Store.CreateAsync(_asset, TimeSpan.FromTicks(10000001))).Marker;
        var recovery = new SqliteCatalogRecoveryService(_locations);
        var backup = await recovery.CreateBackupAsync(_locations.CatalogDatabasePath, CatalogBackupKind.Automatic);
        Assert.True(backup.Succeeded);
        await Store.DeleteAsync(marker.MarkerId, marker.Revision);
        await _session.DisposeAsync();
        var restore = await recovery.BeginRestoreAsync(backup.Backup!.Path);
        Assert.True(restore.Succeeded);
        Assert.True((await restore.Transaction!.CommitAsync()).Succeeded);
        _session = (await new CatalogDatabaseService(_locations, recovery).OpenExistingAsync()).Session!;
        Assert.Equal(marker, Assert.Single(await Store.ListAsync(_asset)));
    }

    [Fact]
    public async Task MigrationFromProduction15ProtectsOriginalAndStartsWithNoMarkers()
    {
        var locations = LightflowStorageLocations.Create(Path.Combine(_root, "old"));
        var old = (await new CatalogDatabaseService(locations, null, CatalogMigrations.All.Take(15).ToArray()).CreateNewAsync()).Session!;
        var identity = old.Identity.CatalogId;
        await old.DisposeAsync();
        var recovery = new SqliteCatalogRecoveryService(locations);
        var opened = await new CatalogDatabaseService(locations, recovery).OpenExistingAsync();
        Assert.True(opened.IsSuccess);
        await using var migrated = opened.Session!;
        Assert.Equal(identity, migrated.Identity.CatalogId);
        Assert.Equal(16, migrated.SchemaVersion);
        Assert.Empty(await new CatalogMarkerService(() => migrated).ListAsync(_asset));
        var backup = Assert.Single(recovery.ListBackups(), b => b.Kind == CatalogBackupKind.Migration);
        using var connection = new SqliteConnection($"Data Source={backup.Path};Mode=ReadOnly;Pooling=False");
        connection.Open(); using var command = connection.CreateCommand(); command.CommandText = "PRAGMA user_version";
        Assert.Equal(15L, command.ExecuteScalar());
    }

    [Fact]
    public async Task InspectorAndBatchSummaryConsumeSameCatalogMarkers()
    {
        var marker = (await Store.CreateAsync(_asset, TimeSpan.FromTicks(7))).Marker;
        var summaries = await Store.SummariesAsync([_asset, _other, _asset]);
        Assert.Equal(2, summaries.Count); Assert.Equal(1, summaries[_asset].Count);
        Assert.False(summaries[_other].HasMarkers);
        var browser = await new CatalogBrowserAssetStateStore(() => _session).GetQueryStatesAsync([_asset, _other]);
        Assert.Equal(1, browser[_asset].MarkerCount);
        Assert.True(browser[_asset].HasMarkers);
        Assert.False(browser[_other].HasMarkers);
        var inspector = new MediaInspectorService(null, new CatalogAssetClassificationStore(() => _session), _locations.PreviewsDirectory, Store);
        var snapshot = await inspector.ReadAsync([new(_asset, "fixture", "fixture.mov", MediaPresentationKind.Video)], default);
        Assert.Equal(marker, Assert.Single(snapshot.Markers));
        Assert.Contains(snapshot.Fields, f => f.Group == "Lightflow" && f.Name == "Markers" && f.Value == "1");
    }

    public async Task DisposeAsync()
    {
        await _session.DisposeAsync();
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
