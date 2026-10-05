using System.IO;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace LightflowStudio;

internal static class CatalogPackageRuntimeVerifier
{
    internal const string CommandLineSwitch = "--verify-catalog-runtime";
    internal const string MigrationCopyCommandLineSwitch = "--verify-catalog-migration-copy";

    internal static readonly Version MinimumSqliteVersion = new(3, 50, 2);

    internal static bool IsPatchedRuntime(string version) =>
        Version.TryParse(version, out var parsed) && parsed >= MinimumSqliteVersion;

    public static async Task<bool> VerifyAsync(string? reportPath = null)
    {
        var root = Path.Combine(Path.GetTempPath(), $"lightflow-sqlite-package-check-{Guid.NewGuid():N}");
        try
        {
            var locations = LightflowStorageLocations.Create(root);
            var service = new CatalogDatabaseService(locations);
            var created = await service.CreateNewAsync().ConfigureAwait(false);
            if (!created.IsSuccess || created.Session is null) return false;
            var catalogId = created.Session.Identity.CatalogId;
            using var connection = created.Session.OpenConnection();
            using var runtime = connection.CreateCommand();
            runtime.CommandText = "SELECT sqlite_version(), sqlite_source_id();";
            using var reader = runtime.ExecuteReader();
            if (!reader.Read()) return false;
            var version = reader.GetString(0);
            var sourceId = reader.GetString(1);
            reader.Close();
            if (!IsPatchedRuntime(version) || string.IsNullOrWhiteSpace(sourceId)) return false;
            var modules = Process.GetCurrentProcess().Modules.Cast<ProcessModule>()
                .Where(module => new[] { "e_sqlite3.dll", "sqlite3.dll", "winsqlite3.dll" }
                    .Contains(module.ModuleName, StringComparer.OrdinalIgnoreCase))
                .ToArray();
            if (modules.Length != 1 || !string.Equals(modules[0].ModuleName, "e_sqlite3.dll", StringComparison.OrdinalIgnoreCase)) return false;
            var nativePath = modules[0].FileName;
            var nativeHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(nativePath)));
            var compileOptions = new List<string>();
            runtime.CommandText = "PRAGMA compile_options;";
            using (var options = runtime.ExecuteReader())
                while (options.Read()) compileOptions.Add(options.GetString(0));
            connection.Close();
            await created.Session.DisposeAsync().ConfigureAwait(false);

            var reopened = await service.OpenExistingAsync().ConfigureAwait(false);
            if (!reopened.IsSuccess || reopened.Session is null) return false;
            var matches = reopened.Session.Identity.CatalogId == catalogId;
            await reopened.Session.DisposeAsync().ConfigureAwait(false);
            if (!matches) return false;

            var assetId = Guid.NewGuid();
            await using var previews = new PreviewStoreService(locations);
            await previews.ObserveSourceAsync(assetId,
                new PreviewSourceIdentity(1, 1, 1, "0123456789abcdef")).ConfigureAwait(false);
            if ((await previews.GetAsync(assetId).ConfigureAwait(false))?.AssetId != assetId) return false;
            using var previewConnection = new SqliteConnection(new SqliteConnectionStringBuilder
                { DataSource = locations.PreviewsDatabasePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
            previewConnection.Open();
            using var previewRuntime = previewConnection.CreateCommand();
            previewRuntime.CommandText = "SELECT sqlite_version() || '|' || sqlite_source_id();";
            if ((string?)previewRuntime.ExecuteScalar() != version + "|" + sourceId) return false;
            if (reportPath is not null)
                File.WriteAllText(reportPath, JsonSerializer.Serialize(new
                {
                    Version = version, SourceId = sourceId, NativePath = nativePath,
                    NativeSha256 = nativeHash, MinimumVersion = MinimumSqliteVersion.ToString(),
                    CatalogSchema = created.SchemaVersion, PreviewRuntimeMatches = true, CompileOptions = compileOptions
                }, new JsonSerializerOptions { WriteIndented = true }));
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    public static async Task<bool> VerifyMigrationCopyAsync(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath) || !File.Exists(databasePath)) return false;
        var root = Path.Combine(Path.GetTempPath(), $"lightflow-catalog-migration-check-{Guid.NewGuid():N}");
        try
        {
            var catalogDirectory = Path.GetDirectoryName(Path.GetFullPath(databasePath))!;
            var locations = LightflowStorageLocations.Create(root, new(CatalogDirectory: catalogDirectory));
            var recovery = new SqliteCatalogRecoveryService(locations);
            var opened = await new CatalogDatabaseService(locations, recovery).OpenExistingAsync().ConfigureAwait(false);
            if (!opened.IsSuccess || opened.Session is null) return false;
            await opened.Session.DisposeAsync().ConfigureAwait(false);
            return recovery.ListBackups().Any(backup => backup.SchemaVersion == 7 && backup.Kind == CatalogBackupKind.Migration);
        }
        catch
        {
            return false;
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
