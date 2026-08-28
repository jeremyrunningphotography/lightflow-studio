using System.IO;

namespace LightflowStudio;

internal static class CatalogPackageRuntimeVerifier
{
    internal const string CommandLineSwitch = "--verify-catalog-runtime";
    internal const string MigrationCopyCommandLineSwitch = "--verify-catalog-migration-copy";

    public static async Task<bool> VerifyAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), $"lightflow-sqlite-package-check-{Guid.NewGuid():N}");
        try
        {
            var locations = LightflowStorageLocations.Create(root);
            var service = new CatalogDatabaseService(locations);
            var created = await service.CreateNewAsync().ConfigureAwait(false);
            if (!created.IsSuccess || created.Session is null) return false;
            var catalogId = created.Session.Identity.CatalogId;
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
            return (await previews.GetAsync(assetId).ConfigureAwait(false))?.AssetId == assetId;
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
