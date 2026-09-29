using System.Collections.Concurrent;
using System.Security.Cryptography;
using LightflowStudio;
using Microsoft.Data.Sqlite;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class StartupStateContractTests : IDisposable
{
    private readonly string _root = OwnedRoot();
    // Isolate lifecycle tests from unrelated settings-file replacement reliability. Packaged
    // acceptance and existing StorageManagement tests continue using real persisted settings.
    private readonly MemoryConfiguration _configuration = new();
    private sealed class MemoryConfiguration : IStorageConfigurationStore
    {
        private AppSettings _settings = new();
        public bool TryLoad(out AppSettings settings, out string? diagnostic)
        { settings = _settings; diagnostic = null; return true; }
        public void Save(AppSettings settings) => _settings = settings;
    }
    private LightflowStorageLocations Locations => LightflowStorageLocations.CreateAtRoot(_root) with { IsIsolated = true };
    private static string OwnedRoot()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Directory.Build.props"))) root = root.Parent;
        return Path.Combine(root!.FullName, "artifacts", "startup-state-tests", Guid.NewGuid().ToString("N"));
    }
    private async Task SeedAsync()
    {
        var result = await LightflowStorageCoordinator.StartAsync(profile: Locations, configuration: _configuration);
        Assert.True(result.IsReady, result.Diagnostic);
        await result.Coordinator!.Collections.CreateSetAsync("Authored fixture");
        await result.Coordinator.DisposeAsync();
    }
    private async Task<(StorageStartupResult Result, ConcurrentQueue<string> Lines)> OpenAsync()
    {
        var lines = new ConcurrentQueue<string>();
        using var diagnostics = new StartupDiagnostics(lines.Enqueue);
        return (await LightflowStorageCoordinator.StartAsync(profile: Locations, configuration: _configuration), lines);
    }
    private static void AssertFast(ConcurrentQueue<string> lines)
    {
        Assert.True(lines.Any(s => s.Contains("Catalog startup decision: fast candidate")), string.Join(Environment.NewLine, lines));
        Assert.True(lines.Any(s => s.Contains("Previews startup decision: fast candidate")), string.Join(Environment.NewLine, lines));
        Assert.Contains(lines, s => s.Contains("Catalog readiness: fast structural reads succeeded"));
        Assert.Contains(lines, s => s.Contains("Previews readiness: fast structural reads succeeded"));
        Assert.DoesNotContain(lines, s => s.Contains("full asset enumeration") || s.Contains("full record enumeration") || s.Contains("usage aggregate requested"));
        Assert.DoesNotContain(lines, s => s.Contains("quick check: begin") || s.Contains("full check: begin") || s.Contains("deep validation"));
    }
    private static void AssertDeep(ConcurrentQueue<string> lines)
    {
        Assert.Single(lines, s => s.Contains("Catalog quick check: begin"));
        Assert.Single(lines, s => s.Contains("Preview quick check: begin"));
    }
    [Fact]
    public async Task CleanExitAndImmediateRestartSkipBothScansAndPreserveAuthoredData()
    {
        await SeedAsync();
        for (var i = 0; i < 2; i++)
        {
            var (result, lines) = await OpenAsync();
            await using var storage = result.Coordinator!;
            Assert.True(result.IsReady); AssertFast(lines);
            Assert.Equal("Authored fixture", Assert.Single(await storage.Collections.ListSetsAsync()).Name);
        }
    }
    [Theory]
    [InlineData("missing")]
    [InlineData("partial")]
    [InlineData("dirty")]
    [InlineData("store-partial")]
    public async Task MissingTornOrUnfinishedEvidenceRequiresBothDeepChecks(string damage)
    {
        await SeedAsync();
        var journal = StartupSessionCompletion.PathFor(_root);
        if (damage == "missing") File.Delete(journal);
        else if (damage == "store-partial") File.WriteAllText(StartupStoreEvidence.JournalPath(Locations.CatalogDatabasePath), "{partial");
        else File.WriteAllText(journal, damage == "partial" ? "abcf" : "Dirty");
        var (result, lines) = await OpenAsync();
        await result.Coordinator!.DisposeAsync();
        Assert.DoesNotContain(lines, line => line.Contains("deep reason=CleanShutdown"));
        // A mismatched individual store needs its own deep scan; whole-session damage needs both.
        Assert.Single(lines, s => s.Contains("Catalog quick check: begin"));
        if (damage != "store-partial") AssertDeep(lines);
        var (next, nextLines) = await OpenAsync();
        await next.Coordinator!.DisposeAsync(); AssertFast(nextLines);
    }
    [Fact]
    public async Task PartialCrossStorePublicationCannotAuthorizeFastStartup()
    {
        await SeedAsync();
        using (var session = new StartupSessionCompletion(_root))
        using (var catalog = StartupStoreEvidence.Begin(Locations.CatalogDatabasePath, "Catalog", false, session))
        using (var previews = StartupStoreEvidence.Begin(Locations.PreviewsDatabasePath, "Previews", false, session))
        {
            Assert.True(catalog!.KnownClean); Assert.True(previews!.KnownClean);
            catalog.Complete(Locations.CatalogDatabasePath);
            // Simulate a crash before Preview completion and final session publication.
        }
        var (result, lines) = await OpenAsync();
        await result.Coordinator!.DisposeAsync(); AssertDeep(lines);
    }
    [Fact]
    public async Task ExceptionalShutdownRemainsDirtyEvenWhenDatabaseDisposalSucceeds()
    {
        await SeedAsync();
        var (result, _) = await OpenAsync();
        result.Coordinator!.PreventCleanShutdown();
        await result.Coordinator.DisposeAsync();
        var (next, lines) = await OpenAsync();
        await next.Coordinator!.DisposeAsync(); AssertDeep(lines);
    }
    [Fact]
    public async Task ShutdownWaitsForWholeMutationBeforePublishingClean()
    {
        await SeedAsync();
        var (result, _) = await OpenAsync();
        var storage = result.Coordinator!;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var mutation = storage.Mutations.RunAsync(async () => { entered.SetResult(); await release.Task; await storage.Collections.CreateSetAsync("Drained"); });
        await entered.Task;
        var close = storage.DisposeAsync().AsTask();
        Assert.False(close.IsCompleted);
        release.SetResult(); await mutation; await close;
        var (next, lines) = await OpenAsync();
        AssertFast(lines); Assert.Equal(2, (await next.Coordinator!.Collections.ListSetsAsync()).Count);
        await next.Coordinator.DisposeAsync();
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExitWithOrWithoutBackupCanPublishClean(bool backupEnabled)
    {
        await SeedAsync();
        var (result, _) = await OpenAsync(); var storage = result.Coordinator!;
        if (backupEnabled)
        {
            var backup = await storage.BackupForExitAsync(storage.BackupDirectory, true);
            Assert.True(backup.Succeeded, backup.Diagnostic); storage.CompletePreparedExit();
        }
        await storage.DisposeAsync();
        var (next, lines) = await OpenAsync(); await next.Coordinator!.DisposeAsync(); AssertFast(lines);
    }
    [Fact]
    public async Task FailedOrCancelledBackupDoesNotPublishCleanWhileApplicationContinues()
    {
        await SeedAsync();
        var (result, _) = await OpenAsync(); var storage = result.Coordinator!;
        var file = Path.Combine(_root, "not-directory"); File.WriteAllText(file, "fixture");
        Assert.False((await storage.BackupForExitAsync(file, true)).Succeeded);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => storage.BackupForExitAsync(storage.BackupDirectory, true, cancellationToken: cancelled.Token));
        await storage.Collections.CreateSetAsync("Still running");
        storage.PreventCleanShutdown(); // termination now, before a later permitted graceful exit
        await storage.DisposeAsync();
        var (next, lines) = await OpenAsync(); await next.Coordinator!.DisposeAsync(); AssertDeep(lines);
    }
    [Fact]
    public async Task RestoreUsesDeepGateThenCanEstablishFutureCleanStartup()
    {
        await SeedAsync();
        var (result, _) = await OpenAsync(); var storage = result.Coordinator!;
        var backup = await storage.BackupCatalogAsync(); Assert.True(backup.Succeeded);
        await storage.Collections.CreateSetAsync("After snapshot");
        var lines = new ConcurrentQueue<string>();
        using (var diagnostics = new StartupDiagnostics(lines.Enqueue))
            Assert.True((await storage.RestoreCatalogAsync(backup.Backup!.Path)).Succeeded);
        Assert.Contains(lines, s => s.Contains("reason=Restore"));
        Assert.Contains(lines, s => s.Contains("Catalog quick check: begin"));
        Assert.Single(await storage.Collections.ListSetsAsync());
        await storage.DisposeAsync();
        var (next, nextLines) = await OpenAsync(); await next.Coordinator!.DisposeAsync(); AssertFast(nextLines);
    }
    [Fact]
    public async Task KnownCleanRequestCannotBypassCatalogMigrationOrPostValidation()
    {
        var old = await new CatalogDatabaseService(Locations, null, CatalogMigrations.All.Take(1).ToArray()).CreateNewAsync();
        await old.Session!.DisposeAsync();
        var lines = new ConcurrentQueue<string>();
        using var diagnostics = new StartupDiagnostics(lines.Enqueue);
        var result = await new CatalogDatabaseService(Locations, new SqliteCatalogRecoveryService(Locations)) { CleanStartup = true }.OpenExistingAsync();
        Assert.True(result.IsSuccess); await result.Session!.DisposeAsync();
        Assert.Equal(2, lines.Count(s => s.Contains("Catalog quick check: begin")));
        Assert.Single(lines, s => s.Contains("Catalog full check: begin"));
        Assert.Contains(lines, s => s.Contains("reason=Migration"));
    }
    [Fact]
    public async Task LightweightSchemaAnomaliesDeepCheckThenRefuseUnsafeUse()
    {
        await SeedAsync();
        Execute(Locations.CatalogDatabasePath, "DROP INDEX IX_MediaAssets_RootId_SourceStatus;");
        Execute(Locations.PreviewsDatabasePath, "DROP INDEX IX_PreviewRecords_MetadataState;");
        var lines = new ConcurrentQueue<string>();
        using var diagnostics = new StartupDiagnostics(lines.Enqueue);
        var catalog = await new CatalogDatabaseService(Locations) { CleanStartup = true }.OpenExistingAsync();
        Assert.False(catalog.IsSuccess);
        await using var previews = new PreviewStoreService(Locations) { CleanStartup = true };
        await Assert.ThrowsAsync<InvalidDataException>(() => previews.InitializeAsync());
        Assert.Contains(lines, s => s.Contains("Catalog deep validation reason=DatabaseAnomaly: begin"));
        Assert.Contains(lines, s => s.Contains("Previews deep validation reason=DatabaseAnomaly: begin"));
        AssertDeep(lines);
        using var database = new SqliteConnection($"Data Source={Locations.CatalogDatabasePath};Mode=ReadOnly;Pooling=False");
        database.Open(); using var query = database.CreateCommand(); query.CommandText = "SELECT Name FROM CollectionSets;";
        Assert.Equal("Authored fixture", query.ExecuteScalar());
    }
    [Fact]
    public async Task DamagedPreviewDatabaseIsDisabledWithoutCatalogOrSourceMutation()
    {
        await SeedAsync();
        var source = Path.Combine(_root, "original.bin"); File.WriteAllText(source, "source fixture");
        var catalogHash = SHA256.HashData(File.ReadAllBytes(Locations.CatalogDatabasePath));
        File.WriteAllText(Locations.PreviewsDatabasePath, "not SQLite");
        var (result, lines) = await OpenAsync();
        Assert.True(result.IsReady); Assert.False(result.Coordinator!.PreviewAvailable);
        Assert.Contains(lines, s => s.Contains("recovery transition=disabled"));
        await result.Coordinator.DisposeAsync();
        Assert.Equal("source fixture", File.ReadAllText(source));
        Assert.Equal(catalogHash, SHA256.HashData(File.ReadAllBytes(Locations.CatalogDatabasePath)));
        Assert.Equal("not SQLite", File.ReadAllText(Locations.PreviewsDatabasePath));
    }
    [Fact]
    public async Task StoreLeaseExcludesConcurrentProfileAndChangedFileInvalidatesEvidence()
    {
        await SeedAsync();
        using (var session = new StartupSessionCompletion(_root))
        using (var store = StartupStoreEvidence.Begin(Locations.CatalogDatabasePath, "Catalog", false, session))
            Assert.Throws<IOException>(() => StartupStoreEvidence.Begin(Locations.CatalogDatabasePath, "Catalog", false, session));
        // A copied/restored file never inherits a trusted physical identity.
        var copy = Locations.CatalogDatabasePath + ".replacement";
        File.Copy(Locations.CatalogDatabasePath, copy); File.Move(copy, Locations.CatalogDatabasePath, true);
        var (result, lines) = await OpenAsync(); await result.Coordinator!.DisposeAsync(); AssertDeep(lines);
    }
    [Fact]
    public async Task KnownCleanRequestCannotBypassPreviewMigrationOrPostValidation()
    {
        await SeedAsync();
        Execute(Locations.PreviewsDatabasePath, "ALTER TABLE PreviewRecords DROP COLUMN MetadataRetryAfterUtc; ALTER TABLE PreviewRecords DROP COLUMN ThumbnailRetryAfterUtc; PRAGMA user_version=3;");
        var lines = new ConcurrentQueue<string>();
        using var diagnostics = new StartupDiagnostics(lines.Enqueue);
        await using var previews = new PreviewStoreService(Locations) { CleanStartup = true };
        await previews.InitializeAsync();
        Assert.Equal(2, lines.Count(s => s.Contains("Preview quick check: begin")));
        Assert.Contains(lines, s => s.Contains("reason=Migration"));
    }
    [Fact]
    public async Task CorruptCatalogFailsClosedAndDoesNotDiscardAuthoredDatabase()
    {
        await SeedAsync();
        Execute(Locations.CatalogDatabasePath, "PRAGMA writable_schema=ON; UPDATE sqlite_schema SET rootpage=2147483647 WHERE name='CollectionSets'; PRAGMA writable_schema=OFF;");
        var before = File.ReadAllBytes(Locations.CatalogDatabasePath);
        var (result, _) = await OpenAsync();
        Assert.False(result.IsReady); Assert.False(result.Coordinator!.CatalogAvailable);
        await result.Coordinator.DisposeAsync();
        Assert.Equal(before, File.ReadAllBytes(Locations.CatalogDatabasePath));
    }

    [Fact]
    public async Task FailedCheckpointOrWriteCannotPublishCleanCompletion()
    {
        await SeedAsync();
        using (var session = new StartupSessionCompletion(_root))
        using (var catalog = StartupStoreEvidence.Begin(Locations.CatalogDatabasePath, "Catalog", false, session))
        using (var previews = StartupStoreEvidence.Begin(Locations.PreviewsDatabasePath, "Previews", false, session))
        using (var locked = new FileStream(Locations.CatalogDatabasePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var error = Record.Exception(() => catalog!.Complete(Locations.CatalogDatabasePath));
            Assert.True(error is IOException or SqliteException, error?.ToString());
            Assert.False(catalog!.Completed);
        }
        var (result, lines) = await OpenAsync(); await result.Coordinator!.DisposeAsync(); AssertDeep(lines);
    }

    [Fact]
    public void FirstAdoptionDoesNotClaimAnInterruptedShutdown()
    {
        using (var session = new StartupSessionCompletion(_root))
        {
            Assert.Equal(StartupValidationReason.UncertainState, session.PriorReason);
            Assert.Equal("Verifying Catalog before opening…", StartupValidationProgress.For("Catalog", session.PriorReason).Primary);
        }
        using var interrupted = new StartupSessionCompletion(_root);
        Assert.Equal(StartupValidationReason.UnexpectedShutdown, interrupted.PriorReason);
        Assert.Contains("after an interrupted shutdown", StartupValidationProgress.For("Catalog", interrupted.PriorReason).Primary);
    }

    private static void Execute(string path, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={path};Pooling=False"); connection.Open();
        using var command = connection.CreateCommand(); command.CommandText = sql; command.ExecuteNonQuery();
    }
    public void Dispose() { SqliteConnection.ClearAllPools(); if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
