using System.IO;
using System.Text.Json;

namespace LightflowStudio;

// Explicit packaged acceptance using the same coordinator operations as the backup dialog.
// This is deliberately run inside LightflowStudio.exe: a test host's manifest can mask MAX_PATH failures.
internal static class CatalogBackupPathVerifier
{
    internal static async Task<bool> VerifyAsync(LightflowStorageLocations profile)
    {
        if (!profile.IsIsolated) return false;
        var report = Path.Combine(profile.ApplicationDataDirectory, "backup-path-verification.jsonl");
        var sync = new object();
        void Log(object value) { lock (sync) File.AppendAllText(report, JsonSerializer.Serialize(value) + Environment.NewLine); }
        using var diagnostics = new StartupDiagnostics(message => Log(new { diagnostic = message }));
        try
        {
            foreach (var length in new[] { 220, 250, 251, 252, 259, 260, 262, 270, 297, 320, 400 })
                await VerifyCaseAsync(profile.ApplicationDataDirectory, length, Log).ConfigureAwait(false);
            Log(new { passed = true });
            return true;
        }
        catch (Exception ex) { Log(new { passed = false, exception = ex.ToString() }); return false; }
    }

    internal static async Task VerifyCaseAsync(string parent, int stagingLength, Action<object> log)
    {
        var stagingPaths = new List<string>();
        using var diagnostics = new StartupDiagnostics(message =>
        {
            // Capture the actual path handed to SQLite, not an inferred temporary suffix.
            var match = System.Text.RegularExpressions.Regex.Match(message, @"; staging \((\d+)\): (.+)$");
            if (match.Success)
            {
                var path = match.Groups[2].Value;
                Require(path.Length == int.Parse(match.Groups[1].Value), "Reported staging path length differs");
                stagingPaths.Add(path);
                log(new { staging = path, length = path.Length });
            }
        });
        var schema = new CatalogDatabaseService(LightflowStorageLocations.CreateAtRoot(parent)).CurrentSchemaVersion;
        var filename = $"LightflowCatalog-User-v{schema}-20260925T120000Z.db";
        var stagingNameLength = filename.Length + 1 + 32 + ".incomplete".Length;
        // Keep the live Catalog within its existing supported range. The 400 case independently
        // deepens the configurable backup destination, without changing ordinary storage policy.
        var rootLength = Math.Min(216, stagingLength - stagingNameLength - "\\Catalog Backups\\".Length);
        var root = PadPath(Path.Combine(parent, "case-" + stagingLength), rootLength);
        if (Directory.Exists(root)) throw new IOException("Packaged verification requires fresh case directories: " + root);
        var locations = LightflowStorageLocations.CreateAtRoot(root) with { IsIsolated = true };
        var destination = PadPath(Path.Combine(root, "Catalog Backups"), stagingLength - stagingNameLength - 1);
        var recovery = new SqliteCatalogRecoveryService(locations);
        CatalogBackup first;
        var startup = await LightflowStorageCoordinator.StartAsync(profile: locations).ConfigureAwait(false);
        await using (var storage = startup.Coordinator!)
        {
            Require(startup.IsReady, startup.Diagnostic);
            await storage.Collections.CreateSetAsync("Before first backup").ConfigureAwait(false);
            await storage.SaveBackupDestinationAsync(destination, CancellationToken.None).ConfigureAwait(false);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var result = await storage.BackupForExitAsync(destination, true).ConfigureAwait(false);
            Require(result.Succeeded, result.Diagnostic);
            first = result.Backup!;
            Require(stagingPaths.Single().Length == stagingLength, "Unexpected generated staging length");
            Require((await recovery.CheckIntegrityAsync(first.Path).ConfigureAwait(false)).IsValid, "First backup integrity");
            log(new { stagingLength, root, catalog = locations.CatalogDatabasePath, catalogLength = locations.CatalogDatabasePath.Length,
                destination, final = first.Path, finalLength = first.Path.Length, firstBackupMs = watch.ElapsedMilliseconds });
            storage.CompletePreparedExit();
        }
        var reopened = await LightflowStorageCoordinator.StartAsync(profile: locations).ConfigureAwait(false);
        await using (var storage = reopened.Coordinator!)
        {
            Require(reopened.IsReady, reopened.Diagnostic);
            Require((await storage.Collections.ListSetsAsync().ConfigureAwait(false)).Count == 1, "Durable state after reopen");
            Require(storage.BackupDirectory == destination, "Configured destination after reopen");
            await storage.Collections.CreateSetAsync("After first backup").ConfigureAwait(false);
            var second = await storage.BackupCatalogAsync().ConfigureAwait(false);
            Require(second.Succeeded && second.Backup!.Path != first.Path, second.Diagnostic);
            Require((await recovery.CheckIntegrityAsync(second.Backup!.Path).ConfigureAwait(false)).IsValid, "Second backup integrity");
            var corrupt = Path.Combine(destination, "corrupt-candidate.db");
            await File.WriteAllTextAsync(corrupt, "not a Catalog").ConfigureAwait(false);
            var rejected = await storage.RestoreCatalogAsync(corrupt).ConfigureAwait(false);
            Require(!rejected.Succeeded, "Corrupt candidate was accepted");
            File.Delete(corrupt);
            Require((await storage.Collections.ListSetsAsync().ConfigureAwait(false)).Count == 2, "Corrupt restore changed live state");
            var restored = await storage.RestoreCatalogAsync(first.Path).ConfigureAwait(false);
            Require(restored.Succeeded, restored.Diagnostic);
            if (stagingLength == 297)
                Require(stagingPaths.Single(path => path.EndsWith(".restoring")).Length == 262, "Restore staging no longer exercises the measured boundary");
            Require((await storage.Collections.ListSetsAsync().ConfigureAwait(false)).Count == 1, "Restored authored state");
            Require((await recovery.CheckIntegrityAsync(locations.CatalogDatabasePath).ConfigureAwait(false)).IsValid, "Live integrity after restore");
            Require(recovery.ListBackups().Any(x => x.Kind == CatalogBackupKind.Recovery), "Missing current-state protection");
            Require(File.Exists(first.Path) && File.Exists(second.Backup.Path), "Prior user backups were lost");
            foreach (var directory in new[] { destination, locations.CatalogDirectory, locations.CatalogBackupsDirectory })
                Require(!Directory.EnumerateFiles(directory).Any(path => path.EndsWith(".incomplete") || path.Contains(".incomplete.") ||
                    path.EndsWith(".restoring") || path.Contains(".before-restore") || path.EndsWith(".tmp") || path.EndsWith("-journal")), "Staging artifacts remain");
            log(new { stagingLength, secondBackup = second.Backup.Path, restore = restored.Succeeded, corruptRejected = true,
                liveHealthy = true, userBackupsPreserved = true, stagingClean = true });
        }
        log(new { stagingLength, shutdownCompleted = true });
    }

    private static string PadPath(string prefix, int length)
    {
        if (prefix.Length > length) throw new ArgumentException("Verification parent path is too long for the selected boundary.");
        while (prefix.Length < length)
        {
            var remaining = length - prefix.Length;
            if (remaining == 1) throw new ArgumentException("Cannot create a one-character path component including separator.");
            var component = Math.Min(60, remaining - 1);
            if (remaining - component - 1 == 1) component--;
            prefix = Path.Combine(prefix, new string('p', component));
        }
        return prefix;
    }

    private static void Require(bool condition, string? diagnostic)
    {
        if (!condition) throw new InvalidOperationException(diagnostic ?? "Packaged Catalog verification failed.");
    }
}
