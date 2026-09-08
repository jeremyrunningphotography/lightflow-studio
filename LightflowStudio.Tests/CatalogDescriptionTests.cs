using LightflowStudio;
using Microsoft.Data.Sqlite;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class CatalogDescriptionTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"lightflow-descriptions-{Guid.NewGuid():N}");
    private LightflowStorageLocations _locations = null!;
    private CatalogDatabaseSession _session = null!;
    private readonly Guid _rootId = Guid.NewGuid();
    private readonly Guid _a = Guid.NewGuid(), _b = Guid.NewGuid();
    private CatalogAssetDescriptionStore Store => new(() => _session);
    public async Task InitializeAsync()
    {
        _locations = LightflowStorageLocations.Create(_root);
        _session = (await new CatalogDatabaseService(_locations).CreateNewAsync()).Session!;
        Execute($"INSERT INTO MediaRoots(RootId,DisplayName,SourceStatus,CreatedUtc,UpdatedUtc) VALUES ('{_rootId:D}','Media','online','{DateTime.UtcNow:O}','{DateTime.UtcNow:O}');");
        InsertAsset(_a); InsertAsset(_b);
    }
    private void InsertAsset(Guid id) => Execute($"""
        INSERT INTO MediaAssets(AssetId,RootId,RelativePath,RelativePathKey,MediaType,FileSizeBytes,LastWriteUtcTicks,SourceStatus,CreatedUtc,UpdatedUtc)
        VALUES ('{id:D}','{_rootId:D}','{id:D}.jpg','{id:D}.jpg','image',1,1,'available','{DateTime.UtcNow:O}','{DateTime.UtcNow:O}');
        """);
    private void Execute(string sql)
    {
        using var connection = _session.OpenConnection(); using var command = connection.CreateCommand();
        command.CommandText = sql; command.ExecuteNonQuery();
    }
    private static AssetDescriptionPatch Patch(AssetDescriptionField field, string? value) => new(new Dictionary<AssetDescriptionField, string?> { [field] = value });
    private async Task Apply(AssetDescriptionPatch patch, params Guid[] ids)
    {
        var values = await Store.GetAsync(ids);
        await Store.ApplyAsync(values.ToDictionary(p => p.Key, p => p.Value.Revision), patch);
    }

    [Fact]
    public async Task UnicodeMultilineWhitespace_AllFieldsRoundTripAfterRestart_AndClearPreservesOtherFields()
    {
        var values = new Dictionary<AssetDescriptionField, string?>
        {
            [AssetDescriptionField.Title] = "  夏 🎬  ", [AssetDescriptionField.Description] = "caption\r\n第二行\nend  ",
            [AssetDescriptionField.Notes] = "\tprivate\nnotes ", [AssetDescriptionField.CreatorOverride] = "Zoë", [AssetDescriptionField.CreditOverride] = "© 2026"
        };
        await Apply(new(values), _a, _b);
        await _session.DisposeAsync(); _session = (await new CatalogDatabaseService(_locations).OpenExistingAsync()).Session!;
        foreach (var description in (await Store.GetAsync([_a, _b])).Values)
        {
            foreach (var field in values) Assert.Equal(field.Value, description.Get(field.Key));
            Assert.Equal(1, description.Revision);
        }
        await Apply(Patch(AssetDescriptionField.Description, null), _a, _b);
        var cleared = (await Store.GetAsync([_a, _b]))[_a];
        Assert.Null(cleared.Description); Assert.Equal(values[AssetDescriptionField.Notes], cleared.Notes); Assert.Equal(2, cleared.Revision);
        await Apply(Patch(AssetDescriptionField.Title, "   "), _a);
        Assert.Equal("   ", (await Store.GetAsync([_a]))[_a].Title);
    }

    [Fact]
    public async Task StaleOrMissingLastTarget_RollsBackEarlierWrites()
    {
        await Apply(Patch(AssetDescriptionField.Title, "original"), _a, _b);
        var stale = (await Store.GetAsync([_a, _b])).ToDictionary(p => p.Key, p => p.Value.Revision);
        await Apply(Patch(AssetDescriptionField.Notes, "concurrent"), _b);
        await Assert.ThrowsAsync<AssetDescriptionConflictException>(() => Store.ApplyAsync(stale, Patch(AssetDescriptionField.Title, "bulk")));
        Assert.Equal("original", (await Store.GetAsync([_a]))[_a].Title);
        Assert.Equal(1, (await Store.GetAsync([_a]))[_a].Revision);
        var missing = new Dictionary<Guid, long> { [_a] = 1, [Guid.NewGuid()] = 0 };
        await Assert.ThrowsAsync<AssetDescriptionConflictException>(() => Store.ApplyAsync(missing, Patch(AssetDescriptionField.Title, null)));
        Assert.Equal("original", (await Store.GetAsync([_a]))[_a].Title);
    }

    [Fact]
    public async Task SqliteFailureAndCancellation_NeverPartiallyApply()
    {
        Execute($"CREATE TRIGGER FailDescription BEFORE UPDATE ON MediaAssetDescriptions WHEN NEW.AssetId='{_b:D}' BEGIN SELECT RAISE(ABORT,'simulated disk failure'); END;");
        await Assert.ThrowsAsync<SqliteException>(() => Apply(Patch(AssetDescriptionField.Title, "bulk"), _a, _b));
        Assert.All((await Store.GetAsync([_a, _b])).Values, v => { Assert.Null(v.Title); Assert.Equal(0, v.Revision); });
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Store.ApplyAsync(new Dictionary<Guid, long> { [_a] = 0 }, Patch(AssetDescriptionField.Title, "no"), canceled.Token));
        Assert.Null((await Store.GetAsync([_a]))[_a].Title);
    }

    [Fact]
    public async Task BatchedRead_ReturnsOnlyExistingAssets_AndEmptyPatchDoesNotCreateRows()
    {
        var ids = Enumerable.Range(0, 805).Select(_ => Guid.NewGuid()).ToArray();
        foreach (var id in ids) InsertAsset(id);
        await Apply(Patch(AssetDescriptionField.Notes, "bulk"), ids);
        var result = await Store.GetAsync([.. ids, ids[0], Guid.NewGuid()]);
        Assert.Equal(805, result.Count); Assert.All(result.Values, v => Assert.Equal("bulk", v.Notes));
        await Apply(new(new Dictionary<AssetDescriptionField, string?>()), _a);
        Assert.Equal(0, (await Store.GetAsync([_a]))[_a].Revision);
    }

    [Fact]
    public async Task BackupRestore_RecoversDescriptionsAndRevision()
    {
        await Apply(Patch(AssetDescriptionField.Notes, "precious\n筆記"), _a, _b);
        var recovery = new SqliteCatalogRecoveryService(_locations);
        var backup = await recovery.CreateBackupAsync(_locations.CatalogDatabasePath, CatalogBackupKind.Automatic);
        Assert.True(backup.Succeeded);
        await Apply(Patch(AssetDescriptionField.Notes, "later"), _a);
        await _session.DisposeAsync();
        var restore = await recovery.BeginRestoreAsync(backup.Backup!.Path);
        Assert.True(restore.Succeeded); Assert.True((await restore.Transaction!.CommitAsync()).Succeeded);
        _session = (await new CatalogDatabaseService(_locations, recovery).OpenExistingAsync()).Session!;
        var restored = (await Store.GetAsync([_a]))[_a];
        Assert.Equal("precious\n筆記", restored.Notes); Assert.Equal(1, restored.Revision);
    }

    [Fact]
    public async Task SourceMetadataCoexists_AndPreviewRebuildDoesNotChangeDescriptions()
    {
        const string raw = "{\"creator\":\"Source author\",\"title\":\"Source title\"}";
        await using (var previews = new PreviewStoreService(_locations))
        {
            await previews.ObserveSourceAsync(_a, new(1, 1, 1, "abc"));
            await previews.SetMetadataAsync(_a, new(1, PreviewComponentState.Current, PayloadJson: "{\"kind\":\"image\"}", RawPayloadJson: raw));
            Assert.Null((await Store.GetAsync([_a]))[_a].CreatorOverride);
            await Apply(Patch(AssetDescriptionField.CreatorOverride, "Catalog creator"), _a);
            Assert.Equal(raw, (await previews.GetAsync(_a))!.RawMetadataJson);
            await Apply(Patch(AssetDescriptionField.CreatorOverride, null), _a);
            Assert.Equal(raw, (await previews.GetAsync(_a))!.RawMetadataJson);
            await Apply(Patch(AssetDescriptionField.Notes, "durable"), _a);
        }
        Directory.Delete(_locations.PreviewsDirectory, true);
        await using var rebuilt = new PreviewStoreService(_locations);
        await rebuilt.ObserveSourceAsync(_a, new(1, 2, 1, "def"));
        Assert.Equal("durable", (await Store.GetAsync([_a]))[_a].Notes);
    }

    [Fact]
    public async Task RelocationAndIndependentCopy_PreserveAuthoredFields()
    {
        await Apply(Patch(AssetDescriptionField.Title, "Authored"), _a);
        var assets = new CatalogMediaAssetRepository(() => _session);
        Assert.Equal(MediaAssetOperationStatus.Succeeded, await assets.RelocateAsync(_a, _rootId, "moved/photo.jpg", DateTimeOffset.UtcNow));
        Assert.Equal("Authored", (await Store.GetAsync([_a]))[_a].Title);
        await new AssetCopyDataService(() => _session, null).CloneAsync(_a, (await assets.GetAsync(_b))!);
        Assert.Equal("Authored", (await Store.GetAsync([_b]))[_b].Title);
        await Apply(Patch(AssetDescriptionField.Title, "Copy only"), _b);
        Assert.Equal("Authored", (await Store.GetAsync([_a]))[_a].Title);
    }

    [Theory]
    [InlineData(0, "")]
    [InlineData(0, "line\nline")]
    [InlineData(3, "line\rline")]
    [InlineData(4, "line\nline")]
    [InlineData(2, "null\0character")]
    public void InvalidSetCannotBeMistakenForClear(int field, string value) =>
        Assert.Throws<ArgumentException>(() => Patch((AssetDescriptionField)field, value).Validate());

    [Fact]
    public async Task Version12Migration_CreatesNoAuthoredValues_AndProtectsOriginalDatabase()
    {
        var locations = LightflowStorageLocations.Create(Path.Combine(_root, "version12"));
        var oldService = new CatalogDatabaseService(locations, null, CatalogMigrations.All.Take(12).ToArray());
        var old = (await oldService.CreateNewAsync()).Session!;
        var identity = old.Identity.CatalogId;
        await old.DisposeAsync();
        var recovery = new SqliteCatalogRecoveryService(locations);
        var migrated = await new CatalogDatabaseService(locations, recovery).OpenExistingAsync();
        Assert.True(migrated.IsSuccess); Assert.Equal(13, migrated.SchemaVersion);
        await using var session = migrated.Session!;
        Assert.Equal(identity, session.Identity.CatalogId);
        using var connection = session.OpenConnection(); using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM MediaAssetDescriptions;";
        Assert.Equal(0L, command.ExecuteScalar());
        var backup = Assert.Single(recovery.ListBackups(), b => b.Kind == CatalogBackupKind.Migration);
        Assert.True((await recovery.CheckIntegrityAsync(backup.Path)).IsValid);
        using var protectedConnection = new SqliteConnection($"Data Source={backup.Path};Mode=ReadOnly;Pooling=False");
        protectedConnection.Open(); using var protectedCommand = protectedConnection.CreateCommand();
        protectedCommand.CommandText = "PRAGMA user_version;";
        Assert.Equal(12L, protectedCommand.ExecuteScalar());
    }

    public async Task DisposeAsync()
    {
        await _session.DisposeAsync();
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
