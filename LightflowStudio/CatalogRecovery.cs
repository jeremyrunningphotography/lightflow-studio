using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace LightflowStudio;

internal enum CatalogBackupKind { Automatic, Migration, Recovery }
internal sealed record CatalogBackup(string Path, int SchemaVersion, DateTimeOffset CreatedUtc, CatalogBackupKind Kind);
internal sealed record CatalogIntegrityResult(bool IsValid, string? Diagnostic = null, int? SchemaVersion = null, Guid? CatalogId = null);
internal sealed record CatalogBackupResult(bool Succeeded, CatalogBackup? Backup = null, string? Diagnostic = null);
internal sealed record CatalogRestoreResult(bool Succeeded, string? Diagnostic = null);

internal interface ICatalogRecoveryService : ICatalogMigrationBackup
{
    Task<CatalogIntegrityResult> CheckIntegrityAsync(string databasePath, CancellationToken cancellationToken = default);
    Task<CatalogBackupResult> CreateBackupAsync(string databasePath, CatalogBackupKind kind,
        bool onlyIfNeededToday = false, CancellationToken cancellationToken = default);
    IReadOnlyList<CatalogBackup> ListBackups();
    Task<CatalogRestoreResult> RestoreAsync(string backupPath, CancellationToken cancellationToken = default);
}

internal sealed partial class SqliteCatalogRecoveryService : ICatalogRecoveryService
{
    private readonly ILightflowStorageLocations _locations;
    private readonly Func<DateTimeOffset> _utcNow;
    internal const int DailyRetention = 10;
    internal const int MonthlyRetention = 3;

    public SqliteCatalogRecoveryService(ILightflowStorageLocations locations, Func<DateTimeOffset>? utcNow = null)
    {
        _locations = locations;
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    public Task<CatalogIntegrityResult> CheckIntegrityAsync(string databasePath, CancellationToken cancellationToken = default) =>
        Task.Run(() => Inspect(databasePath, full: true, cancellationToken), cancellationToken);

    public async Task<CatalogMigrationBackupResult> PrepareForMigrationAsync(string catalogDatabasePath,
        int currentSchemaVersion, int targetSchemaVersion, CancellationToken cancellationToken)
    {
        var result = await CreateBackupAsync(catalogDatabasePath, CatalogBackupKind.Migration,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return result.Succeeded ? CatalogMigrationBackupResult.Success() :
            CatalogMigrationBackupResult.Failure(result.Diagnostic ?? "The pre-migration backup could not be validated.");
    }

    public Task<CatalogBackupResult> CreateBackupAsync(string databasePath, CatalogBackupKind kind,
        bool onlyIfNeededToday = false, CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        var source = Inspect(databasePath, full: true, cancellationToken);
        if (!source.IsValid) return new CatalogBackupResult(false, Diagnostic: source.Diagnostic);
        var now = _utcNow().ToUniversalTime();
        if (onlyIfNeededToday)
        {
            var existing = ListBackups().FirstOrDefault(x => x.Kind == CatalogBackupKind.Automatic && x.CreatedUtc.UtcDateTime.Date == now.UtcDateTime.Date);
            if (existing is not null) return new CatalogBackupResult(true, existing);
        }
        var final = UniqueBackupPath(source.SchemaVersion!.Value, now, kind);
        var staging = final + $".{Guid.NewGuid():N}.tmp";
        try
        {
            Directory.CreateDirectory(_locations.CatalogBackupsDirectory);
            BackupDatabase(databasePath, staging);
            var validation = Inspect(staging, full: true, cancellationToken);
            if (!validation.IsValid || validation.CatalogId != source.CatalogId || validation.SchemaVersion != source.SchemaVersion)
                return new CatalogBackupResult(false, Diagnostic: validation.Diagnostic ?? "The backup did not preserve Catalog identity and schema.");
            File.Move(staging, final);
            File.WriteAllText(final + ".metadata.json", JsonSerializer.Serialize(new BackupMetadata(kind)));
            var backup = ParseBackup(final)!;
            ApplyRetention();
            return new CatalogBackupResult(true, backup);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SqliteException)
        {
            return new CatalogBackupResult(false, Diagnostic: $"Catalog backup failed: {ex.Message}");
        }
        finally { try { File.Delete(staging); } catch { } }
    }, cancellationToken);

    public IReadOnlyList<CatalogBackup> ListBackups()
    {
        if (!Directory.Exists(_locations.CatalogBackupsDirectory)) return [];
        return Directory.EnumerateFiles(_locations.CatalogBackupsDirectory, "LightflowCatalog-v*-*.db")
            .Select(ParseBackup).Where(x => x is not null).Cast<CatalogBackup>()
            .OrderByDescending(x => x.CreatedUtc).ToArray();
    }

    public Task<CatalogRestoreResult> RestoreAsync(string backupPath, CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        var candidate = Inspect(backupPath, full: true, cancellationToken);
        if (!candidate.IsValid) return new CatalogRestoreResult(false, $"The selected backup is not valid. {candidate.Diagnostic}");
        Directory.CreateDirectory(_locations.CatalogDirectory);
        var live = _locations.CatalogDatabasePath;
        var staged = live + $".{Guid.NewGuid():N}.restoring";
        var displaced = live + $".{_utcNow():yyyyMMddTHHmmssZ}.before-restore";
        var movedCurrent = false;
        var currentProtected = false;
        var replacementInstalled = false;
        try
        {
            if (File.Exists(live))
            {
                var current = Inspect(live, full: true, cancellationToken);
                if (current.IsValid)
                {
                    var protection = CreateBackupAsync(live, CatalogBackupKind.Recovery,
                        cancellationToken: cancellationToken).GetAwaiter().GetResult();
                    if (!protection.Succeeded)
                        return new(false, $"The current Catalog could not be protected. {protection.Diagnostic}");
                    currentProtected = true;
                }
            }
            BackupDatabase(backupPath, staged);
            var stagedCheck = Inspect(staged, full: true, cancellationToken);
            if (!stagedCheck.IsValid) throw new InvalidDataException(stagedCheck.Diagnostic);
            if (File.Exists(live)) { File.Move(live, displaced); movedCurrent = true; }
            MoveCompanion(live + "-wal", displaced + "-wal");
            MoveCompanion(live + "-shm", displaced + "-shm");
            File.Move(staged, live);
            replacementInstalled = true;
            var restored = Inspect(live, full: true, cancellationToken);
            if (!restored.IsValid) throw new InvalidDataException(restored.Diagnostic);
            if (currentProtected)
            {
                DeleteCatalogFiles(displaced);
                movedCurrent = false;
            }
            return new CatalogRestoreResult(true, movedCurrent
                ? $"Catalog restored. The previous Catalog was preserved at {displaced}."
                : "Catalog restored successfully.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SqliteException or InvalidDataException)
        {
            try
            {
                if (replacementInstalled) DeleteCatalogFiles(live);
                if (movedCurrent) File.Move(displaced, live);
                MoveCompanion(displaced + "-wal", live + "-wal");
                MoveCompanion(displaced + "-shm", live + "-shm");
            }
            catch (Exception rollback) { return new(false, $"Restore failed: {ex.Message} The previous Catalog is preserved at {displaced}, but automatic rollback also failed: {rollback.Message}"); }
            return new(false, $"Restore failed and the previous Catalog was restored: {ex.Message}");
        }
        finally { try { File.Delete(staged); } catch { } }
    }, cancellationToken);

    private CatalogIntegrityResult Inspect(string path, bool full, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(path)) return new(false, $"Catalog file does not exist: {path}");
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
            connection.Open();
            using var integrity = connection.CreateCommand(); integrity.CommandText = full ? "PRAGMA integrity_check;" : "PRAGMA quick_check;";
            if (!string.Equals(Convert.ToString(integrity.ExecuteScalar()), "ok", StringComparison.OrdinalIgnoreCase)) return new(false, "SQLite integrity checking reported corruption.");
            using var app = connection.CreateCommand(); app.CommandText = "PRAGMA application_id;";
            if (Convert.ToInt32(app.ExecuteScalar()) != CatalogDatabaseService.SqliteApplicationId) return new(false, "The file is not a Lightflow Catalog.");
            using var version = connection.CreateCommand(); version.CommandText = "PRAGMA user_version;";
            var schema = Convert.ToInt32(version.ExecuteScalar());
            using var identity = connection.CreateCommand(); identity.CommandText = "SELECT CatalogId FROM CatalogInfo WHERE SingletonId=1;";
            return Guid.TryParse(Convert.ToString(identity.ExecuteScalar()), out var id) ? new(true, SchemaVersion: schema, CatalogId: id) : new(false, "Catalog identity metadata is invalid.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SqliteException or InvalidOperationException)
        { return new(false, $"Catalog integrity check failed: {ex.Message}"); }
    }

    private static void BackupDatabase(string sourcePath, string destinationPath)
    {
        using var source = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = sourcePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        using var destination = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = destinationPath, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false }.ToString());
        source.Open(); destination.Open(); source.BackupDatabase(destination);
    }

    private string UniqueBackupPath(int schema, DateTimeOffset now, CatalogBackupKind kind)
    {
        while (true)
        {
            var path = Path.Combine(_locations.CatalogBackupsDirectory, $"LightflowCatalog-v{schema}-{now:yyyyMMddTHHmmssZ}.db");
            if (!File.Exists(path)) return path;
            now = now.AddSeconds(1);
        }
    }

    private void ApplyRetention()
    {
        var all = ListBackups();
        var daily = all.GroupBy(x => x.CreatedUtc.UtcDateTime.Date).Select(g => g.OrderByDescending(x => x.CreatedUtc).First()).Take(DailyRetention).ToHashSet();
        var monthly = all.GroupBy(x => (x.CreatedUtc.Year, x.CreatedUtc.Month)).Select(g => g.OrderByDescending(x => x.CreatedUtc).First()).Take(MonthlyRetention).ToHashSet();
        foreach (var backup in all.Where(x => !daily.Contains(x) && !monthly.Contains(x)))
        {
            try { File.Delete(backup.Path); } catch { }
            try { File.Delete(backup.Path + ".metadata.json"); } catch { }
        }
    }

    private static CatalogBackup? ParseBackup(string path)
    {
        var match = BackupName().Match(Path.GetFileName(path));
        if (!match.Success || !int.TryParse(match.Groups[1].Value, out var schema) ||
            !DateTimeOffset.TryParseExact(match.Groups[2].Value, "yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var created)) return null;
        var kind = CatalogBackupKind.Automatic;
        try
        {
            var metadata = JsonSerializer.Deserialize<BackupMetadata>(File.ReadAllText(path + ".metadata.json"));
            if (metadata is not null) kind = metadata.Kind;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
        return new(path, schema, created.ToUniversalTime(), kind);
    }
    private static void MoveCompanion(string source, string destination) { if (File.Exists(source)) File.Move(source, destination); }
    private static void DeleteCatalogFiles(string path) { foreach (var file in new[] { path, path + "-wal", path + "-shm" }) try { File.Delete(file); } catch { } }

    private sealed record BackupMetadata(CatalogBackupKind Kind);

    [GeneratedRegex(@"^LightflowCatalog-v(\d+)-(\d{8}T\d{6}Z)\.db$", RegexOptions.IgnoreCase)]
    private static partial Regex BackupName();
}
