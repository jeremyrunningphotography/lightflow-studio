using System.Text.Json;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class VideoRotationTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "lightflow-rotation-" + Guid.NewGuid().ToString("N"));
    private LightflowStorageLocations _locations = null!;
    private CatalogDatabaseSession _session = null!;
    private readonly Guid _rootId = Guid.NewGuid(), _a = Guid.NewGuid(), _b = Guid.NewGuid();
    private CatalogAssetVideoRotationStore Store => new(() => _session);
    public async Task InitializeAsync()
    {
        _locations = LightflowStorageLocations.Create(_root);
        _session = (await new CatalogDatabaseService(_locations).CreateNewAsync()).Session!;
        Execute($"INSERT INTO MediaRoots(RootId,DisplayName,SourceStatus,CreatedUtc,UpdatedUtc) VALUES ('{_rootId:D}','Media','online','{DateTime.UtcNow:O}','{DateTime.UtcNow:O}');");
        foreach (var id in new[] { _a, _b }) Execute($"""
            INSERT INTO MediaAssets(AssetId,RootId,RelativePath,RelativePathKey,MediaType,FileSizeBytes,LastWriteUtcTicks,SourceStatus,CreatedUtc,UpdatedUtc)
            VALUES ('{id:D}','{_rootId:D}','{id:D}.mp4','{id:D}.MP4','video',1,1,'missing','{DateTime.UtcNow:O}','{DateTime.UtcNow:O}');
            """);
    }
    private void Execute(string sql)
    {
        using var connection = _session.OpenConnection(); using var command = connection.CreateCommand();
        command.CommandText = sql; command.ExecuteNonQuery();
    }
    private async Task Turn(bool right, params Guid[] ids)
    {
        var values = await Store.GetAsync(ids);
        await Store.RotateAsync(values.ToDictionary(p => p.Key, p => p.Value.Revision), right);
    }

    [Theory]
    [InlineData(0, 0, 0, 640, 360)]
    [InlineData(0, 90, 90, 360, 640)]
    [InlineData(90, 90, 180, 640, 360)]
    [InlineData(270, 90, 0, 640, 360)]
    [InlineData(180, 90, 270, 360, 640)]
    public void SourceOrientation_ComposesOnce_AndSwapsGeometry(int source, int adjustment, int degrees, int width, int height)
    {
        var effective = new VideoRotation(source).Compose(new(adjustment));
        Assert.Equal(degrees, effective.Degrees);
        Assert.Equal((width, height), effective.Dimensions(640, 360));
        Assert.Equal(new VideoRotation(adjustment), JsonSerializer.Deserialize<VideoRotation>(JsonSerializer.Serialize(new VideoRotation(adjustment))));
    }

    [Fact]
    public void QuarterTurnsNormalize_AndRejectArbitraryAngles()
    {
        var rotation = new VideoRotation();
        for (var i = 0; i < 100; i++) rotation = rotation.Turn(false);
        Assert.Equal(0, rotation.Degrees);
        Assert.Equal(270, rotation.Turn(false).Degrees);
        Assert.Equal(0, rotation.Turn(false).Turn(true).Degrees);
        Assert.Throws<ArgumentOutOfRangeException>(() => new VideoRotation(45));
    }

    [Fact]
    public void ExportSnapshots_RoundTripRotationAndKeepLegacyZeroSerialization()
    {
        var options = new EncodingJobOptions(".", ".", OutputResolution.Source, RecoveryStrategy.Normal,
            new EncodingOptions(), null, "_export", false, false, false);
        var source = new EncodingSource("clip.mp4", 1, TimeSpan.FromSeconds(1), Rotation: new(90));
        var captured = ExportSettingsMaterializer.Materialize(options, source);
        var saved = JsonSerializer.Serialize(captured);
        Assert.Equal(90, JsonSerializer.Deserialize<MaterializedExportSettings>(saved)!.Rotation.Degrees);
        var rerun = ExportSettingsMaterializer.Materialize(options, source with { Rotation = new(270), RestoredExport = captured });
        Assert.Equal(90, rerun.Rotation.Degrees);
        var legacy = JsonSerializer.Serialize(captured with { Rotation = default });
        Assert.DoesNotContain("Rotation", legacy);
        Assert.Equal(0, JsonSerializer.Deserialize<MaterializedExportSettings>(legacy)!.Rotation.Degrees);
    }

    [Fact]
    public async Task OfflineIntent_SurvivesRestartRelocationPreviewCleanupAndRecovery()
    {
        await Turn(true, _a);
        var recovery = new SqliteCatalogRecoveryService(_locations);
        var backup = await recovery.CreateBackupAsync(_locations.CatalogDatabasePath, CatalogBackupKind.Automatic);
        Assert.True(backup.Succeeded, backup.Diagnostic);
        var assets = new CatalogMediaAssetRepository(() => _session);
        Assert.Equal(MediaAssetOperationStatus.Succeeded, await assets.RelocateAsync(_a, _rootId, "moved/video.mp4", DateTimeOffset.UtcNow));
        await using (var previews = new PreviewStoreService(_locations))
            await previews.ObserveSourceAsync(_a, new(1, 1, 1, "abc"));
        Directory.Delete(_locations.PreviewsDirectory, true);
        await _session.DisposeAsync();
        _session = (await new CatalogDatabaseService(_locations).OpenExistingAsync()).Session!;
        Assert.Equal(90, (await Store.GetAsync([_a]))[_a].Rotation.Degrees);
        await Turn(true, _a);
        await _session.DisposeAsync();
        var restored = await recovery.BeginRestoreAsync(backup.Backup!.Path);
        Assert.True(restored.Succeeded);
        Assert.True((await restored.Transaction!.CommitAsync()).Succeeded);
        _session = (await new CatalogDatabaseService(_locations, recovery).OpenExistingAsync()).Session!;
        var value = (await Store.GetAsync([_a]))[_a];
        Assert.Equal(90, value.Rotation.Degrees); Assert.Equal(1, value.Revision);
    }

    [Fact]
    public async Task BatchConflict_RollsBackAllTargets_AndOnlyPublishesCommittedState()
    {
        var store = Store;
        var notifications = new List<IReadOnlyList<AssetVideoRotation>>();
        store.Changed += (_, values) => notifications.Add(values);
        var initial = (await store.GetAsync([_a, _b])).ToDictionary(p => p.Key, p => p.Value.Revision);
        await store.RotateAsync(new Dictionary<Guid, long> { [_b] = 0 }, true);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.RotateAsync(initial, true));
        Assert.Single(notifications);
        var result = await store.GetAsync([_a, _b]);
        Assert.Equal(0, result[_a].Rotation.Degrees); Assert.Equal(90, result[_b].Rotation.Degrees);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.RotateAsync(initial, false, cancelled.Token));
        Assert.Single(notifications);
    }

    [Fact]
    public async Task TimingAndSourceObservationRemainUnchanged_AndCopiesAreIndependent()
    {
        Execute($"INSERT INTO MediaAssetPreferredFrames(AssetId,PositionTicks,Revision,CreatedUtc,UpdatedUtc) VALUES('{_a:D}',12345,7,'{DateTime.UtcNow:O}','{DateTime.UtcNow:O}');");
        await Turn(true, _a);
        await Turn(true, _a);
        var assets = new CatalogMediaAssetRepository(() => _session);
        var source = (await assets.GetAsync(_a))!;
        Assert.Equal(1, source.FileSizeBytes); Assert.Equal(1, source.LastWriteUtcTicks);
        using (var connection = _session.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT PositionTicks,Revision FROM MediaAssetPreferredFrames";
            using var reader = command.ExecuteReader(); Assert.True(reader.Read());
            Assert.Equal(12345, reader.GetInt64(0)); Assert.Equal(7, reader.GetInt64(1));
        }
        await new AssetCopyDataService(() => _session, null).CloneAsync(_a, (await assets.GetAsync(_b))!);
        await Turn(false, _b);
        var values = await Store.GetAsync([_a, _b]);
        Assert.Equal(180, values[_a].Rotation.Degrees); Assert.Equal(90, values[_b].Rotation.Degrees);
    }

    [Fact]
    public async Task Migration17_ProtectsPriorCatalogAndDefaultsToSourceOrientation()
    {
        var locations = LightflowStorageLocations.Create(Path.Combine(_root, "old"));
        var old = (await new CatalogDatabaseService(locations, null, CatalogMigrations.All.Take(17).ToArray()).CreateNewAsync()).Session!;
        var identity = old.Identity.CatalogId;
        await old.DisposeAsync();
        var recovery = new SqliteCatalogRecoveryService(locations);
        var result = await new CatalogDatabaseService(locations, recovery).OpenExistingAsync();
        Assert.True(result.IsSuccess);
        await using var migrated = result.Session!;
        Assert.Equal(identity, migrated.Identity.CatalogId);
        Assert.Single(recovery.ListBackups(), value => value.Kind == CatalogBackupKind.Migration);
        using var connection = migrated.OpenConnection(); using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM MediaAssetVideoRotation";
        Assert.Equal(0L, command.ExecuteScalar());
    }

    [Theory]
    [InlineData(0, "")]
    [InlineData(90, "transpose=clock")]
    [InlineData(180, "hflip,vflip")]
    [InlineData(270, "transpose=cclock")]
    public void ExportRotation_PrecedesScalingAndAlwaysEncodes(int degrees, string filter)
    {
        var args = FfmpegCommandBuilder.Encode("in.mp4", "out.mp4", null, RecoveryStrategy.Normal,
            OutputResolution.Hd720, rotation: new(degrees));
        var filters = args[args.IndexOf("-vf") + 1];
        Assert.Equal(filter.Length == 0 ? "scale=-2:720" : filter + ",scale=-2:720", filters);
        Assert.Equal("h264_nvenc", args[args.IndexOf("-c:v") + 1]);
        Assert.DoesNotContain("-noautorotate", args);
    }

    public async Task DisposeAsync()
    {
        await _session.DisposeAsync();
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
