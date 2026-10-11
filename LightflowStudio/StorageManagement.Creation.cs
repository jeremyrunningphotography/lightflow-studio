using System.IO;
using Lightflow.Application;

namespace LightflowStudio;

internal sealed partial class LightflowStorageCoordinator
{
    internal static string ProposeNewCatalogDirectory(LightflowStorageLocations profile, AppSettings previous)
    {
        var normal = profile.CatalogDirectory;
        if (CanCreateAt(normal, profile, previous)) return normal;
        for (var index = 1; index <= 100; index++)
        {
            var candidate = Path.Combine(profile.ApplicationDataDirectory, index == 1 ? "Lightflow Catalog" : "Lightflow Catalog " + index);
            if (CanCreateAt(candidate, profile, previous)) return candidate;
        }
        return "";
    }

    private static bool CanCreateAt(string directory, LightflowStorageLocations profile, AppSettings previous)
    {
        if (File.Exists(directory)) return false;
        if (!IsUnoccupied(directory, profile)) return false;
        if (previous.CatalogDirectory is not null || previous.CatalogId is not null ||
            !IsUnoccupied(profile.CatalogDirectory, profile))
        {
            var reserved = previous.CatalogDirectory ?? profile.CatalogDirectory;
            if (LightflowStorageLocations.PathsOverlap(directory, reserved)) return false;
        }
        return !new[] { profile.PreviewsDirectory, profile.TemporaryDirectory, ClosedCatalogTransferJournal.DirectoryPath(profile) }
            .Any(path => LightflowStorageLocations.PathsOverlap(directory, path));
    }

    private static bool IsUnoccupied(string directory, LightflowStorageLocations profile)
    {
        if (!Directory.Exists(directory)) return !File.Exists(directory);
        var entries = Directory.EnumerateFileSystemEntries(directory).ToArray();
        if (entries.Length == 0) return true;
        // Profile initialization creates this empty default backup boundary before first choice.
        // Any file, retained evidence, other child, or backup makes the destination occupied.
        return SamePath(directory, profile.CatalogDirectory) && entries.Length == 1 &&
            SamePath(entries[0], profile.CatalogBackupsDirectory) && Directory.Exists(entries[0]) &&
            !Directory.EnumerateFileSystemEntries(entries[0]).Any();
    }

    internal static async Task<StorageStartupResult> CreateCatalogAsync(string directory, LightflowStorageLocations profile,
        InitializedDataProfile? initializedProfile = null, IStorageConfigurationStore? configuration = null,
        ICatalogSessionActivator? activator = null, IStorageLocationAssessor? assessor = null, CancellationToken token = default)
    {
        configuration ??= new AppSettingsStorageConfigurationStore(profile.SettingsPath, profile.IsIsolated);
        if (!configuration.TryLoad(out var previous, out var diagnostic))
            return new(StorageStartupStatus.InvalidConfiguration, Diagnostic: diagnostic);
        var pending = ClosedCatalogTransferJournal.InspectPending(profile);
        if (pending is not null) return new(StorageStartupStatus.CatalogUnavailable, Diagnostic: pending, RecoveryReason: CatalogRecoveryReason.PendingRecovery);
        directory = Path.GetFullPath(directory);
        if (!CanCreateAt(directory, profile, previous))
            return new(StorageStartupStatus.CatalogUnavailable, Diagnostic: "Choose a new, unused Catalog folder outside the previous Catalog and reserved storage locations.", RecoveryReason: CatalogRecoveryReason.LocationRefused);
        var intended = previous with { CatalogDirectory = directory, CatalogId = null, CatalogBackupDirectory = null };
        StorageStartupResult? candidate = null;
        try
        {
            candidate = await StartCoreAsync(configuration: configuration, activator: activator, profile: profile,
                initializedProfile: initializedProfile, assessor: assessor, allowFirstCreation: false,
                selectedSettings: intended, explicitCreation: true, cancellationToken: token,
                reservedCatalogDirectory: previous.CatalogDirectory ?? (previous.CatalogId is not null ||
                    !IsUnoccupied(profile.CatalogDirectory, profile) ? profile.CatalogDirectory : null)).ConfigureAwait(false);
            if (candidate.IsReady) return candidate;
            if (candidate.Coordinator is not null) await candidate.Coordinator.DisposeAsync().ConfigureAwait(false);
            diagnostic = candidate.Diagnostic;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or OperationCanceledException or Microsoft.Data.Sqlite.SqliteException)
        { diagnostic = error.Message; }
        // The new artifacts are retained. Only configuration is rolled back; do not activate
        // a previous Catalog implicitly after a failed explicit creation.
        try
        {
            if (!configuration.TryLoad(out var persisted, out _) || persisted.CatalogDirectory != previous.CatalogDirectory || persisted.CatalogId != previous.CatalogId)
                configuration.Save(previous);
            if (!configuration.TryLoad(out persisted, out _) || persisted.CatalogDirectory != previous.CatalogDirectory || persisted.CatalogId != previous.CatalogId)
                throw new IOException("Previous Catalog configuration could not be verified.");
        }
        catch (Exception rollback) when (rollback is IOException or UnauthorizedAccessException)
        { return new(StorageStartupStatus.InvalidConfiguration, Diagnostic: diagnostic + " " + rollback.Message, RecoveryReason: CatalogRecoveryReason.PendingRecovery); }
        return new(candidate?.Status ?? StorageStartupStatus.CatalogUnreadable, Diagnostic: diagnostic,
            RecoveryReason: candidate?.RecoveryReason ?? CatalogRecoveryReason.ValidationFailed);
    }
}
