using LightflowStudio;
using Microsoft.Data.Sqlite;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class CatalogRecoveryTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"lightflow-recovery-{Guid.NewGuid():N}");

    [Fact]
    public async Task AutomaticBackup_IsValidated_AndLimitedToOnePerUtcDay()
    {
        var locations = LightflowStorageLocations.Create(_root);
        var created = await new CatalogDatabaseService(locations).CreateNewAsync();
        await created.Session!.DisposeAsync();
        var clock = new DateTimeOffset(2026, 8, 14, 12, 0, 0, TimeSpan.Zero);
        var recovery = new SqliteCatalogRecoveryService(locations, () => clock);

        var first = await recovery.CreateBackupAsync(locations.CatalogDatabasePath, CatalogBackupKind.Automatic, true);
        var second = await recovery.CreateBackupAsync(locations.CatalogDatabasePath, CatalogBackupKind.Automatic, true);

        Assert.True(first.Succeeded);
        Assert.Equal(first.Backup!.Path, second.Backup!.Path);
        Assert.Single(recovery.ListBackups());
        Assert.True((await recovery.CheckIntegrityAsync(first.Backup.Path)).IsValid);
    }

    [Fact]
    public async Task MigrationBackup_UsesRealValidatedSqliteBackup()
    {
        var locations = LightflowStorageLocations.Create(_root);
        var created = await new CatalogDatabaseService(locations).CreateNewAsync();
        await created.Session!.DisposeAsync();
        var recovery = new SqliteCatalogRecoveryService(locations);

        var result = await recovery.PrepareForMigrationAsync(locations.CatalogDatabasePath, 1, 2, default);

        Assert.True(result.Succeeded);
        Assert.Contains(recovery.ListBackups(), x => x.Kind == CatalogBackupKind.Migration);
    }

    [Fact]
    public async Task CorruptionAndInvalidBackup_AreRejectedWithoutReplacingCatalog()
    {
        var locations = LightflowStorageLocations.Create(_root);
        Directory.CreateDirectory(locations.CatalogDirectory);
        await File.WriteAllTextAsync(locations.CatalogDatabasePath, "not sqlite");
        var invalid = Path.Combine(_root, "invalid.db");
        await File.WriteAllTextAsync(invalid, "not a backup");
        var recovery = new SqliteCatalogRecoveryService(locations);

        Assert.False((await recovery.CheckIntegrityAsync(locations.CatalogDatabasePath)).IsValid);
        Assert.False((await recovery.RestoreAsync(invalid)).Succeeded);
        Assert.Equal("not sqlite", await File.ReadAllTextAsync(locations.CatalogDatabasePath));
    }

    [Fact]
    public async Task Restore_ReplacesCatalogSafely_AndLeavesPreviewsUntouched()
    {
        var locations = LightflowStorageLocations.Create(_root, new(null, Path.Combine(_root, "custom-previews")));
        var created = await new CatalogDatabaseService(locations).CreateNewAsync();
        var id = created.Session!.Identity.CatalogId;
        await created.Session.DisposeAsync();
        var recovery = new SqliteCatalogRecoveryService(locations);
        var backup = await recovery.CreateBackupAsync(locations.CatalogDatabasePath, CatalogBackupKind.Automatic);
        Directory.CreateDirectory(locations.PreviewsDirectory);
        var preview = Path.Combine(locations.PreviewsDirectory, "keep.preview");
        await File.WriteAllTextAsync(preview, "rebuildable but untouched");
        File.Delete(locations.CatalogDatabasePath);

        var restored = await recovery.RestoreAsync(backup.Backup!.Path);
        var opened = await new CatalogDatabaseService(locations, recovery).OpenExistingAsync();

        Assert.True(restored.Succeeded);
        Assert.Equal(id, opened.Session!.Identity.CatalogId);
        Assert.Equal("rebuildable but untouched", await File.ReadAllTextAsync(preview));
        await opened.Session.DisposeAsync();
    }

    [Fact]
    public async Task CustomCatalogLocation_OwnsItsBackups()
    {
        var custom = Path.Combine(_root, "catalog-on-another-drive");
        var locations = LightflowStorageLocations.Create(_root, new(custom, null));
        var created = await new CatalogDatabaseService(locations).CreateNewAsync();
        await created.Session!.DisposeAsync();
        var result = await new SqliteCatalogRecoveryService(locations)
            .CreateBackupAsync(locations.CatalogDatabasePath, CatalogBackupKind.Automatic);

        Assert.True(result.Succeeded);
        Assert.Equal(Path.GetFullPath(locations.CatalogBackupsDirectory), Path.GetDirectoryName(result.Backup!.Path));
    }

    [Fact]
    public async Task Startup_CreatesAutomaticBackup_AndCorruptRestartStillExposesIt()
    {
        var started = await LightflowStorageCoordinator.StartAsync(_root);
        var coordinator = started.Coordinator!;
        var backups = coordinator.CatalogBackups;
        var catalogPath = coordinator.Locations.CatalogDatabasePath;
        await coordinator.DisposeAsync();

        Assert.Single(backups);
        try { File.Delete(catalogPath + "-wal"); } catch { }
        try { File.Delete(catalogPath + "-shm"); } catch { }
        await File.WriteAllBytesAsync(catalogPath, Enumerable.Repeat((byte)0xA5, 4096).ToArray());
        var restarted = await LightflowStorageCoordinator.StartAsync(_root);

        Assert.Contains(restarted.Status, new[] { StorageStartupStatus.CatalogUnreadable, StorageStartupStatus.CatalogCorrupt });
        Assert.False(restarted.Coordinator!.CatalogAvailable);
        Assert.Single(restarted.Coordinator.CatalogBackups);
        await restarted.Coordinator.DisposeAsync();
    }

    [Fact]
    public async Task Restore_AbortsWithoutMutation_WhenCurrentCatalogCannotBeProtected()
    {
        var locations = LightflowStorageLocations.Create(_root);
        var created = await new CatalogDatabaseService(locations).CreateNewAsync();
        var id = created.Session!.Identity.CatalogId;
        await created.Session.DisposeAsync();
        var recovery = new SqliteCatalogRecoveryService(locations);
        var originalBackup = await recovery.CreateBackupAsync(locations.CatalogDatabasePath, CatalogBackupKind.Automatic);
        var candidate = Path.Combine(_root, "candidate.db");
        File.Copy(originalBackup.Backup!.Path, candidate);
        Directory.Delete(locations.CatalogBackupsDirectory, true);
        await File.WriteAllTextAsync(locations.CatalogBackupsDirectory, "blocks backup directory creation");

        var result = await recovery.RestoreAsync(candidate);
        var reopened = await new CatalogDatabaseService(locations).OpenExistingAsync();

        Assert.False(result.Succeeded);
        Assert.Equal(id, reopened.Session!.Identity.CatalogId);
        await reopened.Session.DisposeAsync();
    }

    [Fact]
    public async Task Retention_DeletesOnlyOwnedBackupsAndKeepsDailyAndMonthlyBounds()
    {
        var locations = LightflowStorageLocations.Create(_root);
        var created = await new CatalogDatabaseService(locations).CreateNewAsync();
        await created.Session!.DisposeAsync();
        var now = new DateTimeOffset(2026, 8, 14, 12, 0, 0, TimeSpan.Zero);
        var recovery = new SqliteCatalogRecoveryService(locations, () => now);
        Directory.CreateDirectory(locations.CatalogBackupsDirectory);
        var unrelated = Path.Combine(locations.CatalogBackupsDirectory, "family-photos.db");
        await File.WriteAllTextAsync(unrelated, "never delete");
        for (var day = 0; day < 100; day += 4)
        {
            now = now.AddDays(-4);
            Assert.True((await recovery.CreateBackupAsync(locations.CatalogDatabasePath, CatalogBackupKind.Automatic)).Succeeded);
        }

        Assert.True(File.Exists(unrelated));
        Assert.True(recovery.ListBackups().Count <= SqliteCatalogRecoveryService.DailyRetention + SqliteCatalogRecoveryService.MonthlyRetention);
    }

    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync() { try { Directory.Delete(_root, true); } catch { } return Task.CompletedTask; }
}
