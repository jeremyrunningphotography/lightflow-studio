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
    public async Task DailyBackupReuse_DoesNotReadSourceAgain_ButNewBackupsStillValidateIt()
    {
        var locations = LightflowStorageLocations.Create(_root);
        var created = await new CatalogDatabaseService(locations).CreateNewAsync();
        await created.Session!.DisposeAsync();
        var clock = new DateTimeOffset(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);
        var recovery = new SqliteCatalogRecoveryService(locations, () => clock);
        var first = await recovery.CreateBackupAsync(locations.CatalogDatabasePath, CatalogBackupKind.Automatic, true);
        Assert.True(first.Succeeded);
        SqliteConnection.ClearAllPools();
        // Deterministically detect any repeated source scan without a wall-clock performance assertion.
        using (var exclusive = new FileStream(locations.CatalogDatabasePath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var reused = await recovery.CreateBackupAsync(locations.CatalogDatabasePath, CatalogBackupKind.Automatic, true);
            Assert.True(reused.Succeeded);
            Assert.Equal(first.Backup, reused.Backup);
            Assert.False((await recovery.CreateBackupAsync(locations.CatalogDatabasePath, CatalogBackupKind.Automatic)).Succeeded);
            clock = clock.AddDays(1);
            Assert.False((await recovery.CreateBackupAsync(locations.CatalogDatabasePath, CatalogBackupKind.Automatic, true)).Succeeded);
        }
        var nextDay = await recovery.CreateBackupAsync(locations.CatalogDatabasePath, CatalogBackupKind.Automatic, true);
        Assert.True(nextDay.Succeeded);
        Assert.NotEqual(first.Backup!.Path, nextDay.Backup!.Path);
        Assert.True((await recovery.CheckIntegrityAsync(nextDay.Backup.Path)).IsValid);
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

    [Theory]
    [InlineData(1, true)]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(2, false)]
    public async Task SameDayProtection_RetainsAutomaticAndReusesItAcrossLaunches(int protectionKind, bool protectionFirst)
    {
        var locations = LightflowStorageLocations.Create(_root);
        var created = await new CatalogDatabaseService(locations).CreateNewAsync();
        await created.Session!.DisposeAsync();
        var clock = new DateTimeOffset(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);
        var recovery = new SqliteCatalogRecoveryService(locations, () => clock);
        var firstKind = protectionFirst ? (CatalogBackupKind)protectionKind : CatalogBackupKind.Automatic;
        var secondKind = protectionFirst ? CatalogBackupKind.Automatic : (CatalogBackupKind)protectionKind;
        var first = await recovery.CreateBackupAsync(locations.CatalogDatabasePath, firstKind);
        clock = clock.AddSeconds(2);
        var second = await recovery.CreateBackupAsync(locations.CatalogDatabasePath, secondKind);
        Assert.True(first.Succeeded);
        Assert.True(second.Succeeded);
        var automatic = protectionFirst ? second.Backup! : first.Backup!;
        var protection = protectionFirst ? first.Backup! : second.Backup!;
        Assert.Equal(2, recovery.ListBackups().Count);
        Assert.True(File.Exists(automatic.Path));
        Assert.True((await recovery.CheckIntegrityAsync(protection.Path)).IsValid);

        // Reconstructing the service models separate launches; the exclusive source lock proves
        // reuse does not perform source validation or copying (no timing threshold required).
        SqliteConnection.ClearAllPools();
        using (var locked = new FileStream(locations.CatalogDatabasePath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            for (var launch = 0; launch < 3; launch++)
            {
                recovery = new SqliteCatalogRecoveryService(locations, () => clock);
                var reused = await recovery.CreateBackupAsync(locations.CatalogDatabasePath, CatalogBackupKind.Automatic, true);
                Assert.True(reused.Succeeded);
                Assert.Equal(automatic.Path, reused.Backup!.Path);
            }
            clock = clock.AddDays(1);
            Assert.False((await recovery.CreateBackupAsync(locations.CatalogDatabasePath, CatalogBackupKind.Automatic, true)).Succeeded);
        }
        var nextDay = await recovery.CreateBackupAsync(locations.CatalogDatabasePath, CatalogBackupKind.Automatic, true);
        Assert.True(nextDay.Succeeded);
        Assert.NotEqual(automatic.Path, nextDay.Backup!.Path);
        Assert.True(File.Exists(protection.Path));
        Assert.False(File.Exists(automatic.Path));
        Assert.True((await recovery.CheckIntegrityAsync(nextDay.Backup.Path)).IsValid);
    }

    [Fact]
    public async Task SameSecondAtUtcMidnight_DoesNotDateAutomaticBackupInTheFuture()
    {
        var locations = LightflowStorageLocations.Create(_root);
        var created = await new CatalogDatabaseService(locations).CreateNewAsync();
        await created.Session!.DisposeAsync();
        var clock = new DateTimeOffset(2026, 9, 14, 23, 59, 59, TimeSpan.Zero);
        var recovery = new SqliteCatalogRecoveryService(locations, () => clock);
        var protection = await recovery.CreateBackupAsync(locations.CatalogDatabasePath, CatalogBackupKind.Migration);
        var automatic = await recovery.CreateBackupAsync(locations.CatalogDatabasePath, CatalogBackupKind.Automatic, true);
        Assert.True(protection.Succeeded);
        Assert.True(automatic.Succeeded);
        Assert.Equal(clock, automatic.Backup!.CreatedUtc);
        Assert.Equal(automatic.Backup, (await recovery.CreateBackupAsync(locations.CatalogDatabasePath, CatalogBackupKind.Automatic, true)).Backup);
        clock = clock.AddSeconds(1);
        var nextDay = await recovery.CreateBackupAsync(locations.CatalogDatabasePath, CatalogBackupKind.Automatic, true);
        Assert.True(nextDay.Succeeded);
        Assert.NotEqual(automatic.Backup.Path, nextDay.Backup!.Path);
        Assert.True(File.Exists(protection.Backup!.Path));
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
        Assert.False((await recovery.BeginRestoreAsync(invalid)).Succeeded);
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

        var installation = await recovery.BeginRestoreAsync(backup.Backup!.Path);
        var restored = await installation.Transaction!.CommitAsync();
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
    public async Task StartupDoesNotCreateRoutineBackup_AndCorruptRestartStillExposesSafetyCopies()
    {
        var started = await LightflowStorageCoordinator.StartAsync(_root);
        var coordinator = started.Coordinator!;
        Assert.Empty(coordinator.CatalogBackups);
        var safety = await new SqliteCatalogRecoveryService(coordinator.Locations).CreateBackupAsync(
            coordinator.Locations.CatalogDatabasePath, CatalogBackupKind.Migration);
        Assert.True(safety.Succeeded);
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

        var result = await recovery.BeginRestoreAsync(candidate);
        var reopened = await new CatalogDatabaseService(locations).OpenExistingAsync();

        Assert.False(result.Succeeded);
        Assert.Equal(id, reopened.Session!.Identity.CatalogId);
        await reopened.Session.DisposeAsync();
    }

    [Fact]
    public async Task PostInstallActivationFailure_RollsBackAndReactivatesPreviousCatalog()
    {
        var activator = new FailNextActivation();
        var startup = await LightflowStorageCoordinator.StartAsync(_root, activator: activator);
        var coordinator = startup.Coordinator!;
        var catalogId = coordinator.CatalogSession.Identity.CatalogId;
        using (var connection = coordinator.CatalogSession.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "CREATE TABLE RestoreProbe (Value TEXT NOT NULL); INSERT INTO RestoreProbe VALUES ('backup-state');";
            command.ExecuteNonQuery();
        }
        var backup = await coordinator.BackupCatalogAsync();
        using (var connection = coordinator.CatalogSession.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "UPDATE RestoreProbe SET Value='current-state';";
            command.ExecuteNonQuery();
        }
        Directory.CreateDirectory(coordinator.Locations.PreviewsDirectory);
        var preview = Path.Combine(coordinator.Locations.PreviewsDirectory, "untouched.preview");
        await File.WriteAllTextAsync(preview, "preview-data");
        activator.FailNext = true;

        var result = await coordinator.RestoreCatalogAsync(backup.Backup!.Path);

        Assert.False(result.Succeeded);
        Assert.True(coordinator.CatalogAvailable);
        Assert.Equal(catalogId, coordinator.CatalogSession.Identity.CatalogId);
        using (var connection = coordinator.CatalogSession.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT Value FROM RestoreProbe;";
            Assert.Equal("current-state", command.ExecuteScalar());
        }
        Assert.Equal("preview-data", await File.ReadAllTextAsync(preview));
        Assert.Contains(coordinator.CatalogBackups, x => x.Kind == CatalogBackupKind.Recovery);
        Assert.NotEmpty(Directory.EnumerateFiles(coordinator.Locations.CatalogDirectory, "*.failed-restore*"));
        Assert.Empty(Directory.EnumerateFiles(coordinator.Locations.CatalogDirectory, "*.before-restore*"));
        await coordinator.DisposeAsync();
    }

    [Fact]
    public async Task SuccessfulActivation_CommitsAndRemovesDisplacedCatalog()
    {
        var coordinator = (await LightflowStorageCoordinator.StartAsync(_root)).Coordinator!;
        var backup = await coordinator.BackupCatalogAsync();

        var result = await coordinator.RestoreCatalogAsync(backup.Backup!.Path);

        Assert.True(result.Succeeded);
        Assert.True(coordinator.CatalogAvailable);
        Assert.Empty(Directory.EnumerateFiles(coordinator.Locations.CatalogDirectory, "*.before-restore*"));
        Assert.Empty(Directory.EnumerateFiles(coordinator.Locations.CatalogDirectory, "*.restoring"));
        await coordinator.DisposeAsync();
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

    private sealed class FailNextActivation : ICatalogSessionActivator
    {
        public bool FailNext { get; set; }
        public CatalogDatabaseSession Activate(CatalogDatabaseSession session)
        {
            if (!FailNext) return session;
            FailNext = false;
            throw new InvalidOperationException("simulated post-install activation failure");
        }
    }

}
