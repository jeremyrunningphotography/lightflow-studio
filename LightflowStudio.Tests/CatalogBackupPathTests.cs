using Microsoft.Data.Sqlite;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class CatalogBackupPathTests : IDisposable
{
    private readonly string _root = OwnedRoot();

    [Theory]
    [InlineData(220)]
    [InlineData(250)]
    [InlineData(251)]
    [InlineData(252)]
    [InlineData(259)]
    [InlineData(260)]
    [InlineData(262)]
    [InlineData(270)]
    [InlineData(297)] // Restore staging itself is 262 characters.
    [InlineData(320)]
    [InlineData(400)]
    public Task RealCoordinatorBackupRestoreAndShutdownAtMeasuredBoundaries(int length) =>
        CatalogBackupPathVerifier.VerifyCaseAsync(_root, length, _ => { });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedOrCancelledNewCopyPreservesPriorBackupAndLiveCatalog(bool cancel)
    {
        var locations = LightflowStorageLocations.CreateAtRoot(_root) with { IsIsolated = true };
        await using var storage = (await LightflowStorageCoordinator.StartAsync(profile: locations)).Coordinator!;
        await storage.Collections.CreateSetAsync("Durable");
        var destination = LongDirectory("user");
        var first = await storage.BackupForExitAsync(destination, false);
        Assert.True(first.Succeeded, first.Diagnostic);
        var prior = await File.ReadAllBytesAsync(first.Backup!.Path);
        using var cancellation = new CancellationTokenSource();
        var progress = new InlineProgress(message =>
        {
            if (!message.StartsWith("Validating")) return;
            if (cancel) cancellation.Cancel();
            else File.WriteAllText(Directory.GetFiles(destination, "*.incomplete").Single(), "corrupt staged copy");
        });
        if (cancel)
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => storage.BackupForExitAsync(destination, true, progress, cancellation.Token));
        else
        {
            var failed = await storage.BackupForExitAsync(destination, true, progress);
            Assert.False(failed.Succeeded);
            Assert.Contains("integrity", failed.Diagnostic);
        }
        Assert.Equal(prior, await File.ReadAllBytesAsync(first.Backup.Path));
        Assert.Equal(2, Directory.GetFiles(destination).Length); // Prior DB and supplementary metadata only.
        await storage.Collections.CreateSetAsync("Admission reopened");
        Assert.Equal(2, (await storage.Collections.ListSetsAsync()).Count);
        Assert.True((await new SqliteCatalogRecoveryService(locations).CheckIntegrityAsync(locations.CatalogDatabasePath)).IsValid);
    }

    [Fact]
    public async Task LongManagedGenerationsRotateAndCancelledValidatedCopyIsNotPromoted()
    {
        var locations = LightflowStorageLocations.CreateAtRoot(_root) with { CatalogBackupsDirectory = LongDirectory("managed") };
        var created = await new CatalogDatabaseService(locations).CreateNewAsync();
        await created.Session!.DisposeAsync();
        var now = new DateTimeOffset(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);
        var recovery = new SqliteCatalogRecoveryService(locations, () => now);
        for (var day = 0; day < 16; day++)
        {
            var backup = await recovery.CreateBackupAsync(locations.CatalogDatabasePath, CatalogBackupKind.Migration);
            Assert.True(backup.Succeeded, backup.Diagnostic);
            Assert.True(backup.Backup!.Path.Length > 260);
            Assert.True((await recovery.CheckIntegrityAsync(backup.Backup.Path)).IsValid);
            now = now.AddDays(1);
        }
        Assert.InRange(recovery.ListBackups().Count, 10, 13);
        var prior = Directory.GetFiles(locations.CatalogBackupsDirectory).Order().ToArray();
        using var cancellation = new CancellationTokenSource();
        using (var diagnostics = new StartupDiagnostics(message =>
        {
            if (message.Contains("Backup copy full check: end")) cancellation.Cancel();
        }))
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => recovery.CreateBackupAsync(
                locations.CatalogDatabasePath, CatalogBackupKind.Migration, cancellationToken: cancellation.Token));
        Assert.Equal(prior, Directory.GetFiles(locations.CatalogBackupsDirectory).Order().ToArray());
    }

    [Fact]
    public async Task FailedLongRestoreProtectionPreservesLiveBytesAndCleansStaging()
    {
        var locations = LightflowStorageLocations.CreateAtRoot(_root);
        var created = await new CatalogDatabaseService(locations).CreateNewAsync();
        await created.Session!.DisposeAsync();
        var recovery = new SqliteCatalogRecoveryService(locations);
        var candidate = await recovery.CreateBackupAsync(locations.CatalogDatabasePath, CatalogBackupKind.Migration);
        Assert.True(candidate.Succeeded, candidate.Diagnostic);
        var liveBytes = await File.ReadAllBytesAsync(locations.CatalogDatabasePath);
        var blocked = LongDirectory("blocked");
        File.WriteAllText(blocked, "a file prevents protection directory creation");
        var broken = new SqliteCatalogRecoveryService(locations with { CatalogBackupsDirectory = blocked });
        var result = await broken.BeginRestoreAsync(candidate.Backup!.Path, true);
        Assert.False(result.Succeeded);
        Assert.Contains("could not be protected", result.Diagnostic);
        Assert.Equal(liveBytes, await File.ReadAllBytesAsync(locations.CatalogDatabasePath));
        Assert.Empty(Directory.GetFiles(locations.CatalogDirectory, "*.restoring*"));
        Assert.True((await recovery.CheckIntegrityAsync(candidate.Backup.Path)).IsValid);
    }

    [Fact]
    public async Task CancellationDuringInstalledValidationDoesNotStrandDisplacedCatalog()
    {
        var locations = LightflowStorageLocations.CreateAtRoot(_root);
        var created = await new CatalogDatabaseService(locations).CreateNewAsync();
        await created.Session!.DisposeAsync();
        var recovery = new SqliteCatalogRecoveryService(locations);
        var backup = await recovery.CreateBackupAsync(locations.CatalogDatabasePath, CatalogBackupKind.Migration);
        using var cancellation = new CancellationTokenSource();
        var checks = 0;
        CatalogRestoreInstallation installation;
        using (var diagnostics = new StartupDiagnostics(message =>
        {
            if (message.Contains("Recovery full check: begin") && ++checks == 4) cancellation.Cancel();
        }))
            installation = await recovery.BeginRestoreAsync(backup.Backup!.Path, true, cancellation.Token);
        Assert.True(cancellation.IsCancellationRequested);
        Assert.True(installation.Succeeded, installation.Diagnostic);
        Assert.True((await installation.Transaction!.CommitAsync()).Succeeded);
        Assert.True((await recovery.CheckIntegrityAsync(locations.CatalogDatabasePath)).IsValid);
        Assert.Empty(Directory.GetFiles(locations.CatalogDirectory, "*.before-restore*"));
        Assert.Empty(Directory.GetFiles(locations.CatalogDirectory, "*.restoring*"));
    }

    private string LongDirectory(string name)
    {
        var path = Path.Combine(_root, name, new string('a', 70), new string('b', 70), new string('c', 70));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return path;
    }

    private static string OwnedRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "LightflowStudio"))) directory = directory.Parent;
        if (directory is null) throw new InvalidOperationException("Tests require the owning source workspace.");
        return Path.Combine(directory.FullName, "artifacts", "t318", Guid.NewGuid().ToString("N")[..8]);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
    private sealed class InlineProgress(Action<string> action) : IProgress<string>
    { public void Report(string value) => action(value); }
}
