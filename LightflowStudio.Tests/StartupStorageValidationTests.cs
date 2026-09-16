using System.Collections.Concurrent;
using LightflowStudio;
using Microsoft.Data.Sqlite;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class StartupStorageValidationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"lightflow-startup-{Guid.NewGuid():N}");

    [Fact]
    public async Task CurrentCatalog_EachOpenScansOnce_AndLaterCorruptionIsStillRejected()
    {
        var locations = LightflowStorageLocations.Create(_root);
        var database = new CatalogDatabaseService(locations);
        var created = await database.CreateNewAsync();
        var identity = created.Session!.Identity;
        await created.Session.DisposeAsync();
        for (var launch = 0; launch < 3; launch++)
        {
            var lines = new ConcurrentQueue<string>();
            using var diagnostics = new StartupDiagnostics(lines.Enqueue);
            var opened = await database.OpenExistingAsync();
            Assert.True(opened.IsSuccess, opened.Diagnostic);
            Assert.Equal(identity, opened.Session!.Identity);
            Assert.True(opened.Session.RuntimePolicy.ForeignKeysEnabled);
            Assert.Equal(CatalogSqliteConnectionFactory.FullSynchronousLevel, opened.Session.RuntimePolicy.SynchronousLevel);
            Assert.Single(lines, x => x.Contains("Catalog quick check: begin"));
            await opened.Session.DisposeAsync();
        }
        Execute(locations.CatalogDatabasePath, "PRAGMA writable_schema=ON; UPDATE sqlite_schema SET rootpage=2147483647 WHERE name='MediaRoots'; PRAGMA writable_schema=OFF;");
        Assert.Equal(CatalogOpenStatus.Corrupt, (await database.OpenExistingAsync()).Status);
    }

    [Fact]
    public async Task CatalogMigration_RetainsPreAndPostChecks_AndRestorableOldSchemaBackup()
    {
        var locations = LightflowStorageLocations.Create(_root);
        var created = await new CatalogDatabaseService(locations, null, CatalogMigrations.All.Take(1).ToArray()).CreateNewAsync();
        var catalogId = created.Session!.Identity.CatalogId;
        await created.Session.DisposeAsync();
        var recovery = new SqliteCatalogRecoveryService(locations);
        var lines = new ConcurrentQueue<string>();
        using var diagnostics = new StartupDiagnostics(lines.Enqueue);
        var migrated = await new CatalogDatabaseService(locations, recovery).OpenExistingAsync();
        Assert.True(migrated.IsSuccess, migrated.Diagnostic);
        Assert.Equal(catalogId, migrated.Session!.Identity.CatalogId);
        Assert.Equal(2, lines.Count(x => x.Contains("Catalog quick check: begin")));
        Assert.Single(lines, x => x.Contains("Catalog full check: begin"));
        await migrated.Session.DisposeAsync();
        var backup = Assert.Single(recovery.ListBackups());
        Assert.Equal(CatalogBackupKind.Migration, backup.Kind);
        var checkedBackup = await recovery.CheckIntegrityAsync(backup.Path);
        Assert.True(checkedBackup.IsValid);
        Assert.Equal(1, checkedBackup.SchemaVersion);
        Assert.Equal(catalogId, checkedBackup.CatalogId);
        var restored = await recovery.BeginRestoreAsync(backup.Path);
        Assert.True(restored.Succeeded, restored.Diagnostic);
        Assert.True((await restored.Transaction!.CommitAsync()).Succeeded);
        var reopened = await new CatalogDatabaseService(locations, recovery).OpenExistingAsync();
        Assert.True(reopened.IsSuccess, reopened.Diagnostic);
        Assert.Equal(catalogId, reopened.Session!.Identity.CatalogId);
        await reopened.Session.DisposeAsync();
    }

    [Fact]
    public async Task Previews_CurrentReopenScansOnce_MigrationScansBeforeAndAfter_AndCorruptionIsRejected()
    {
        var locations = LightflowStorageLocations.Create(_root);
        var asset = Guid.NewGuid();
        await using (var store = new PreviewStoreService(locations))
            await store.ObserveSourceAsync(asset, new(100, 200, 1, "abcdef0123456789"));
        for (var launch = 0; launch < 2; launch++)
        {
            var lines = new ConcurrentQueue<string>();
            using var diagnostics = new StartupDiagnostics(lines.Enqueue);
            await using var store = new PreviewStoreService(locations);
            Assert.NotNull(await store.GetAsync(asset));
            Assert.Single(lines, x => x.Contains("Preview quick check: begin"));
        }
        Execute(locations.PreviewsDatabasePath, "ALTER TABLE PreviewRecords DROP COLUMN ThumbnailVisualIdentity; ALTER TABLE PreviewRecords DROP COLUMN StandardPreviewVisualIdentity; PRAGMA user_version=1;");
        var migrationLines = new ConcurrentQueue<string>();
        using (var diagnostics = new StartupDiagnostics(migrationLines.Enqueue))
        await using (var store = new PreviewStoreService(locations))
        {
            Assert.NotNull(await store.GetAsync(asset));
            Assert.Equal(2, migrationLines.Count(x => x.Contains("Preview quick check: begin")));
        }
        Execute(locations.PreviewsDatabasePath, "PRAGMA writable_schema=ON; UPDATE sqlite_schema SET rootpage=2147483647 WHERE name='PreviewRecords'; PRAGMA writable_schema=OFF;");
        await using var corrupt = new PreviewStoreService(locations);
        var error = await Record.ExceptionAsync(() => corrupt.InitializeAsync());
        Assert.True(error is InvalidDataException or SqliteException, error?.ToString());
    }

    private static void Execute(string path, string sql)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    public void Dispose() { SqliteConnection.ClearAllPools(); if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
