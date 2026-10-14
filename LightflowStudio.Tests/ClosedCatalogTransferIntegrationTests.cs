using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Lightflow.Application;
using Microsoft.Data.Sqlite;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class ClosedCatalogTransferIntegrationTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(TestWorkspace(), "artifacts", "transfer-392", Guid.NewGuid().ToString("N"));
    private LightflowStorageLocations Locations => LightflowStorageLocations.CreateAtRoot(_root) with { IsIsolated = true };
    private static string TestWorkspace()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (Directory.Exists(Path.Combine(d.FullName, "LightflowStudio"))) return d.FullName;
        throw new InvalidOperationException("Task-owned workspace required.");
    }
    public Task InitializeAsync() { Directory.CreateDirectory(_root); return Task.CompletedTask; }
    public Task DisposeAsync() { if (Directory.Exists(_root)) Directory.Delete(_root, true); return Task.CompletedTask; }

    [Fact]
    public async Task MoveIntoAbsentChildOfEmptyParentRetainsAtomicPublicationAndReopensAuthoredCatalog()
    {
        var parent = Directory.CreateDirectory(Path.Combine(_root, "Selected empty parent")).FullName;
        var destination = Path.Combine(parent, "Lightflow Catalog");
        Guid id; byte[] backupHash; string backupName;
        await using (var storage = (await LightflowStorageCoordinator.StartAsync(profile: Locations)).Coordinator!)
        {
            storage.SaveSettings(storage.Settings with { BackupCatalogOnClose = false });
            id = storage.CatalogSession.Identity.CatalogId;
            await storage.Collections.CreateCollectionAsync("Parent selection authored collection");
            var backup = await new SqliteCatalogRecoveryService(Locations).CreateBackupAsync(storage.CatalogSession.DatabasePath, CatalogBackupKind.Recovery);
            Assert.True(backup.Succeeded, backup.Diagnostic);
            backupName = Path.GetFileName(backup.Backup!.Path);
            backupHash = SHA256.HashData(await File.ReadAllBytesAsync(backup.Backup.Path));
            Assert.Empty(Directory.EnumerateFileSystemEntries(parent));
            var move = await storage.RelocateCatalogAsync(destination);
            Assert.True(move.Succeeded, move.Diagnostic);
            Assert.Equal(destination, storage.Locations.CatalogDirectory);
            Assert.Equal(destination, Assert.Single(Directory.EnumerateDirectories(parent)));
            Assert.False(Directory.Exists(Path.Combine(destination, "Catalog")));
            Assert.False(Directory.Exists(Path.Combine(destination, "Lightflow Catalog")));
            var manifest = ClosedCatalogManifestFormat.Deserialize(await File.ReadAllBytesAsync(Path.Combine(destination, "manifest.json")));
            Assert.Equal(ClosedCatalogPublicationMode.AtomicDirectory, manifest.PublicationMode);
            Assert.Equal(id, manifest.CatalogId);
            Assert.Throws<IOException>(() => StartupStoreEvidence.Begin(WindowsCatalogEvidenceEffects.Instance, storage.CatalogSession.DatabasePath, "Catalog", false));
            Assert.Equal(backupHash, SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(destination, "Backups", backupName))));
            Assert.True(File.Exists(Locations.CatalogDatabasePath));
        }
        var reopened = await LightflowStorageCoordinator.StartAsync(profile: Locations);
        Assert.True(reopened.IsReady, reopened.Diagnostic);
        await using var ready = reopened.Coordinator!;
        Assert.Equal(destination, ready.Locations.CatalogDirectory);
        Assert.Equal(id, ready.CatalogSession.Identity.CatalogId);
        Assert.Equal("Parent selection authored collection", Assert.Single(await ready.Collections.ListCollectionsAsync()).Name);
        Assert.Equal(backupHash, SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(destination, "Backups", backupName))));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExistingFinalChildRefusesWithoutChangingSourceOrDestination(bool foreignFile)
    {
        await using var storage = (await LightflowStorageCoordinator.StartAsync(profile: Locations)).Coordinator!;
        var source = storage.Locations.CatalogDirectory;
        var id = storage.CatalogSession.Identity.CatalogId;
        await storage.Collections.CreateCollectionAsync("Retained on collision");
        var destination = Directory.CreateDirectory(Path.Combine(_root, "Parent", "Lightflow Catalog")).FullName;
        var foreign = Path.Combine(destination, "foreign.txt");
        if (foreignFile) await File.WriteAllTextAsync(foreign, "preserve these bytes");
        var result = await storage.RelocateCatalogAsync(destination);
        Assert.False(result.Succeeded); Assert.True(storage.VerifyWorkspaceReadiness());
        Assert.Equal(source, storage.Locations.CatalogDirectory);
        Assert.Equal(id, storage.CatalogSession.Identity.CatalogId);
        Assert.Equal("Retained on collision", Assert.Single(await storage.Collections.ListCollectionsAsync()).Name);
        Assert.False(File.Exists(Path.Combine(destination, LightflowStorageLocations.CatalogFileName)));
        if (foreignFile) Assert.Equal("preserve these bytes", await File.ReadAllTextAsync(foreign));
        else Assert.Empty(Directory.EnumerateFileSystemEntries(destination));
    }

    [Fact]
    public async Task NativeMovePreservesDatabaseManagedBackupsAndExternalLutReferences()
    {
        await using var storage = (await LightflowStorageCoordinator.StartAsync(profile: Locations)).Coordinator!;
        await storage.Collections.CreateSetAsync("Authored set");
        var id = storage.CatalogSession.Identity.CatalogId;
        var resource = Guid.NewGuid();
        using (var sql = storage.CatalogSession.OpenConnection())
        {
            using var insert = sql.CreateCommand();
            insert.CommandText = "INSERT INTO LutResources (LutId,DisplayName,OriginalFileName,ContentSha256,LutKind,LutSize,CreatedUtc,UpdatedUtc) VALUES ($id,'Missing external LUT','missing.cube',$hash,'3d',2,$now,$now);";
            insert.Parameters.AddWithValue("$id", resource.ToString("D")); insert.Parameters.AddWithValue("$hash", new string('a', 64));
            insert.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.UtcDateTime.ToString("O")); insert.ExecuteNonQuery();
        }
        var externalLutFolder = Path.Combine(_root, "externally-managed-luts");
        storage.SaveSettings(storage.Settings with { CameraLutFolder = externalLutFolder, CreativeLutFolder = externalLutFolder });
        var media = Directory.CreateDirectory(Path.Combine(_root, "media")).FullName;
        await File.WriteAllTextAsync(Path.Combine(media, "clip.mp4"), "synthetic media bytes");
        var root = (await storage.MediaRoots.CreateAsync("Media", media)).Root!;
        var assetId = (await storage.MediaAssets.CreateAsync(root.RootId, "clip.mp4", "video")).Asset!.Asset.AssetId;
        await storage.AssetColors.SetStageAsync([assetId], ColorLutStage.Camera, resource);
        var authoredColor = (await storage.AssetColors.GetAsync([assetId]))[assetId];
        var recovery = new SqliteCatalogRecoveryService(Locations);
        var backup = await recovery.CreateBackupAsync(storage.CatalogSession.DatabasePath, CatalogBackupKind.Recovery);
        Assert.True(backup.Succeeded, backup.Diagnostic);
        var hash = SHA256.HashData(await File.ReadAllBytesAsync(backup.Backup!.Path));
        var destination = Path.Combine(_root, "moved");
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var result = await storage.RelocateCatalogAsync(destination);
        Assert.True(result.Succeeded, result.Diagnostic);
        Assert.Equal(id, storage.CatalogSession.Identity.CatalogId);
        Assert.Single(await storage.Collections.ListSetsAsync());
        Assert.Equal(authoredColor, (await storage.AssetColors.GetAsync([assetId]))[assetId]);
        Assert.Equal(externalLutFolder, storage.Settings.CameraLutFolder);
        Assert.Equal(externalLutFolder, storage.Settings.CreativeLutFolder);
        Assert.Equal(hash, SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(destination, "Backups", Path.GetFileName(backup.Backup.Path)))));
        var manifest = ClosedCatalogManifestFormat.Deserialize(await File.ReadAllBytesAsync(Path.Combine(destination, "manifest.json")));
        Assert.Equal(resource, Assert.Single(manifest.ExternalDependencies).ResourceId);
        Assert.DoesNotContain(manifest.Artifacts, a => a.RelativeName.EndsWith(".cube", StringComparison.Ordinal));
        Assert.True(File.Exists(Locations.CatalogDatabasePath));
        System.Diagnostics.Debug.WriteLine($"LF011 real native complete-unit move: {watch.Elapsed.TotalMilliseconds:F3} ms");
    }

    [Fact]
    public async Task MoveWaitsForCheckedOutReadAndRejectsStaleSessionAfterSwitch()
    {
        await using var storage = (await LightflowStorageCoordinator.StartAsync(profile: Locations)).Coordinator!;
        var prior = storage.CatalogSession;
        using var read = prior.OpenConnection();
        var move = storage.RelocateCatalogAsync(Path.Combine(_root, "read-drain"));
        await Task.Delay(100);
        Assert.False(move.IsCompleted);
        read.Close();
        var result = await move.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.True(result.Succeeded, result.Diagnostic);
        Assert.Throws<ObjectDisposedException>(() => prior.OpenConnection());
        Assert.Throws<ObjectDisposedException>(() => read.Open());
        Assert.Equal(System.Data.ConnectionState.Closed, read.State);
        read.Dispose();
        await storage.Collections.CreateSetAsync("New session");
    }
    [Fact]
    public async Task ForeignWriterPreventsClosedMoveWithoutLosingCommittedWal()
    {
        await using var storage = (await LightflowStorageCoordinator.StartAsync(profile: Locations)).Coordinator!;
        var source = storage.CatalogSession.DatabasePath;
        using var external = new SqliteConnection($"Data Source={source};Pooling=False"); external.Open();
        using (var sql = external.CreateCommand()) { sql.CommandText = "PRAGMA journal_mode=WAL; PRAGMA wal_autocheckpoint=0; CREATE TABLE ForeignWriterProbe (Value TEXT); INSERT INTO ForeignWriterProbe VALUES ('committed');"; sql.ExecuteNonQuery(); }
        var result = await storage.RelocateCatalogAsync(Path.Combine(_root, "foreign-writer"));
        Assert.False(result.Succeeded);
        Assert.True(storage.CatalogAvailable, result.Diagnostic);
        using var check = external.CreateCommand(); check.CommandText = "SELECT Value FROM ForeignWriterProbe;";
        Assert.Equal("committed", check.ExecuteScalar());
    }
    [Fact]
    public async Task UnknownCatalogFileBlocksMoveAndIsRetained()
    {
        await using var storage = (await LightflowStorageCoordinator.StartAsync(profile: Locations)).Coordinator!;
        var unknown = Path.Combine(Locations.CatalogDirectory, "user-authored.cube"); await File.WriteAllTextAsync(unknown, "precious unowned evidence");
        var moved = await storage.RelocateCatalogAsync(Path.Combine(_root, "unknown"));
        Assert.False(moved.Succeeded); Assert.True(storage.CatalogAvailable, moved.Diagnostic);
        Assert.Equal("precious unowned evidence", await File.ReadAllTextAsync(unknown));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StartupChecksPendingAndTruncatedJournalBeforeAnyCatalogWriter(bool truncated)
    {
        Guid id;
        await using (var storage = (await LightflowStorageCoordinator.StartAsync(profile: Locations)).Coordinator!) id = storage.CatalogSession.Identity.CatalogId;
        var operation = Guid.NewGuid();
        var request = new ClosedCatalogTransferRequest(operation, 0, ClosedCatalogTransferMode.MoveAndActivate, id, 12, CatalogDatabaseService.SqliteApplicationId);
        var journal = new ClosedCatalogTransferJournal(Locations, operation);
        await journal.AppendAsync(new(1, request, 0, ClosedCatalogTransferState.Preflight, true, "", null, null), CancellationToken.None);
        await journal.DisposeAsync();
        if (truncated)
        {
            var path = Path.Combine(ClosedCatalogTransferJournal.DirectoryPath(Locations), operation.ToString("N") + ".journal");
            using var file = new FileStream(path, FileMode.Open, FileAccess.Write); file.SetLength(file.Length - 3);
        }
        var reopened = await LightflowStorageCoordinator.StartAsync(profile: Locations);
        await using var unavailable = reopened.Coordinator!;
        Assert.Equal(StorageStartupStatus.CatalogUnavailable, reopened.Status); Assert.False(unavailable.CatalogAvailable);
        Assert.True(File.Exists(Locations.CatalogDatabasePath)); Assert.Equal(id, unavailable.Settings.CatalogId);
    }
    [Fact]
    public async Task LiveJournalWriterRefusesUnverifiedRecovery_ReleasedJournalStillRefusesInterruptedTransfer()
    {
        Guid id;
        await using (var storage = (await LightflowStorageCoordinator.StartAsync(profile: Locations)).Coordinator!)
            id = storage.CatalogSession.Identity.CatalogId;
        var request = new ClosedCatalogTransferRequest(Guid.NewGuid(), 0,
            ClosedCatalogTransferMode.MoveAndActivate, id, 12, CatalogDatabaseService.SqliteApplicationId);
        var path = Path.Combine(ClosedCatalogTransferJournal.DirectoryPath(Locations), $"{request.OperationId:N}.journal");
        byte[] prior;
        await using (var journal = new ClosedCatalogTransferJournal(Locations, request.OperationId))
        {
            await journal.AppendAsync(new(1, request, 0, ClosedCatalogTransferState.Preflight, true, "", null, null), CancellationToken.None);
            // Read sharing permits retaining bytes while the actual closed-evidence reader
            // must refuse coexistence with this deliberately live writer.
            using (var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var bytes = new MemoryStream()) { reader.CopyTo(bytes); prior = bytes.ToArray(); }
            var locked = await LightflowStorageCoordinator.StartAsync(profile: Locations, allowFirstCreation: false);
            await using var unavailable = locked.Coordinator!;
            Assert.Equal(StorageStartupStatus.CatalogUnavailable, locked.Status);
            Assert.Equal(CatalogRecoveryReason.PendingRecovery, locked.RecoveryReason);
            Assert.Contains("could not be verified", locked.Diagnostic);
            Assert.False(unavailable.CatalogAvailable);
            await Assert.ThrowsAsync<InvalidOperationException>(() => unavailable.Collections.CreateCollectionAsync("Blocked"));
            Assert.False((await unavailable.RestoreCatalogAsync("unselected-backup.db")).Succeeded);
        }
        Assert.Equal(prior, await File.ReadAllBytesAsync(path));
        var released = await LightflowStorageCoordinator.StartAsync(profile: Locations, allowFirstCreation: false);
        await using var stillUnavailable = released.Coordinator!;
        Assert.Equal(StorageStartupStatus.CatalogUnavailable, released.Status);
        Assert.Equal(CatalogRecoveryReason.PendingRecovery, released.RecoveryReason);
        Assert.Contains("interrupted Catalog transfer", released.Diagnostic);
        Assert.False(stillUnavailable.CatalogAvailable);
        Assert.Equal(id, stillUnavailable.Settings.CatalogId);
        Assert.Equal(prior, await File.ReadAllBytesAsync(path));
    }
    [Fact]
    public async Task CorruptOrUnreadyBackupIsUnlistedAndPriorBackupSurvives()
    {
        await using var storage = (await LightflowStorageCoordinator.StartAsync(profile: Locations)).Coordinator!;
        var first = await storage.BackupCatalogAsync(); var second = await storage.BackupCatalogAsync();
        Assert.True(first.Succeeded, first.Diagnostic); Assert.True(second.Succeeded, second.Diagnostic);
        var before = SHA256.HashData(await File.ReadAllBytesAsync(first.Backup!.Path));
        var bad = await File.ReadAllBytesAsync(second.Backup!.Path); bad[20] ^= 1; await File.WriteAllBytesAsync(second.Backup.Path, bad);
        Assert.Single(storage.CatalogBackups);
        Assert.Equal(before, SHA256.HashData(await File.ReadAllBytesAsync(first.Backup.Path)));
        var restore = await storage.RestoreCatalogAsync(second.Backup.Path); Assert.False(restore.Succeeded);
        Assert.True(storage.CatalogAvailable);
    }
    [Fact]
    public async Task BackupCopybackRestoresThroughExistingRecoveryOwner()
    {
        await using var storage = (await LightflowStorageCoordinator.StartAsync(profile: Locations)).Coordinator!;
        await storage.Collections.CreateSetAsync("Before backup"); var id = storage.CatalogSession.Identity.CatalogId;
        var backup = await storage.BackupCatalogAsync(); Assert.True(backup.Succeeded, backup.Diagnostic);
        await storage.Collections.CreateSetAsync("After backup");
        var restore = await storage.RestoreCatalogAsync(backup.Backup!.Path); Assert.True(restore.Succeeded, restore.Diagnostic);
        Assert.Single(await storage.Collections.ListSetsAsync()); Assert.Equal(id, storage.CatalogSession.Identity.CatalogId);
        Assert.True(File.Exists(backup.Backup.Path));
    }

    [Fact]
    public void NativeDirectoryPublicationRetainsExactObjectAndRefusesCollision()
    {
        var stage = Path.Combine(_root, "native-stage"); var destination = Path.Combine(_root, "native-published");
        WindowsClosedCatalogTransferOperation.CreateExclusiveDirectory(stage);
        File.WriteAllText(Path.Combine(stage, "sentinel"), "owned bytes");
        using (var lease = new WindowsStorageLocationAssessor.TransferDirectoryLease(stage))
        {
            lease.PublishNoReplace(destination);
            Assert.True(Directory.Exists(destination));
            lease.Revalidate(destination);
        }
        Assert.Equal("owned bytes", File.ReadAllText(Path.Combine(destination, "sentinel")));
        WindowsClosedCatalogTransferOperation.CreateExclusiveDirectory(stage);
        using var collision = new WindowsStorageLocationAssessor.TransferDirectoryLease(stage);
        Assert.Throws<System.ComponentModel.Win32Exception>(() => collision.PublishNoReplace(destination));
        Assert.True(Directory.Exists(stage));
        Assert.Equal("owned bytes", File.ReadAllText(Path.Combine(destination, "sentinel")));
    }

    [Fact]
    public async Task RepresentativeLocalCostsAndNativeLeaseReleaseAreRecorded()
    {
        var storage = (await LightflowStorageCoordinator.StartAsync(profile: Locations)).Coordinator!;
        var disposed = false;
        try
        {
        using (var sql = storage.CatalogSession.OpenConnection())
        using (var command = sql.CreateCommand())
        {
            command.CommandText = "CREATE TABLE TransferPerformanceProbe(Bytes BLOB); WITH RECURSIVE c(n) AS (SELECT 1 UNION ALL SELECT n+1 FROM c WHERE n<256) INSERT INTO TransferPerformanceProbe SELECT zeroblob(65536) FROM c;";
            command.ExecuteNonQuery();
        }
        var local = Path.Combine(_root, "measured-snapshot"); Directory.CreateDirectory(local);
        var request = new ClosedCatalogTransferRequest(Guid.NewGuid(), 0, ClosedCatalogTransferMode.BackupTransport,
            storage.CatalogSession.Identity.CatalogId, storage.CatalogSession.SchemaVersion, CatalogDatabaseService.SqliteApplicationId);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var manifest = await new SqliteCatalogRecoveryService(Locations).FinalizeTransferSnapshotAsync(request, local, CancellationToken.None);
        var snapshotMs = watch.Elapsed.TotalMilliseconds;
        var artifact = Assert.Single(manifest.Artifacts); var source = Path.Combine(local, artifact.RelativeName);
        var copied = Path.Combine(_root, "measured-copy.snapshot"); watch.Restart();
        await using (var input = WindowsClosedCatalogTransferOperation.OpenClosedRead(source))
        await using (var output = new FileStream(copied, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, FileOptions.Asynchronous))
        { await input.CopyToAsync(output); output.Flush(true); }
        var copyMs = watch.Elapsed.TotalMilliseconds; watch.Restart();
        using (var readback = WindowsClosedCatalogTransferOperation.OpenClosedRead(copied))
        { Assert.Equal(artifact.ByteLength, readback.Length); Assert.Equal(artifact.Sha256, Convert.ToHexString(SHA256.HashData(readback))); }
        var verifyMs = watch.Elapsed.TotalMilliseconds;
        watch.Restart();
        await using (var journal = new ClosedCatalogTransferJournal(Locations, request.OperationId))
        {
            var first = new ClosedCatalogJournalRecord(1, request, 0, ClosedCatalogTransferState.Preflight, true, "", null, null);
            await journal.AppendAsync(first, CancellationToken.None);
            var failed = new ClosedCatalogJournalRecord(1, request, 1, ClosedCatalogTransferState.Failed, true, ClosedCatalogJournalFormat.Digest(first), null, "Measured journal only; no lifecycle effect");
            await journal.AppendAsync(failed, CancellationToken.None);
            await journal.AppendAsync(failed with { Sequence = 2, IsIntent = false, PreviousRecordDigest = ClosedCatalogJournalFormat.Digest(failed) }, CancellationToken.None);
        }
        var journalMs = watch.Elapsed.TotalMilliseconds; watch.Restart();
        Assert.False(ClosedCatalogJournalFormat.RequiresRecovery(ClosedCatalogTransferJournal.Read(Path.Combine(ClosedCatalogTransferJournal.DirectoryPath(Locations), request.OperationId.ToString("N") + ".journal"))));
        var recoveryScanMs = watch.Elapsed.TotalMilliseconds;
        using var cancellation = new CancellationTokenSource(); var cancellationWatch = new System.Diagnostics.Stopwatch();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => storage.BackupForExitAsync(storage.BackupDirectory, false,
            new MeasurementProgress(message => { if (message.StartsWith("Copying", StringComparison.Ordinal)) { cancellationWatch.Start(); cancellation.Cancel(); } }), cancellation.Token));
        cancellationWatch.Stop();
        watch.Restart(); await storage.DisposeAsync(); disposed = true; var shutdownMs = watch.Elapsed.TotalMilliseconds;
        var journalDirectory = ClosedCatalogTransferJournal.DirectoryPath(Locations);
        Directory.Move(journalDirectory, journalDirectory + ".release-probe");
        Directory.Move(journalDirectory + ".release-probe", journalDirectory);
        var report = Path.Combine(TestWorkspace(), "artifacts", "transfer-392-performance-" + Guid.NewGuid().ToString("N") + ".json");
        await File.WriteAllTextAsync(report, JsonSerializer.Serialize(new { fixtureBytes = artifact.ByteLength, snapshotMs, copyMs,
            copyMiBPerSecond = artifact.ByteLength / 1048576d / (copyMs / 1000d), verifyMs, journalMs, recoveryScanMs,
            cancellationBeforeCopyMs = cancellationWatch.Elapsed.TotalMilliseconds, shutdownWithoutBackupMs = shutdownMs,
            nativeJournalLeaseReleased = true, host = Environment.MachineName }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally { if (!disposed) await storage.DisposeAsync(); }
    }
    private sealed class MeasurementProgress(Action<string> action) : IProgress<string>
    { public void Report(string value) => action(value); }
}
