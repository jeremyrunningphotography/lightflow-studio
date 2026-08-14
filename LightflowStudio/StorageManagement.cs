using System.IO;
using Microsoft.Data.Sqlite;

namespace LightflowStudio;

internal enum StorageStartupStatus { Ready, CatalogUnavailable, CatalogMissing, CatalogUnreadable, CatalogCorrupt, CatalogIdentityMismatch, InvalidConfiguration }
internal sealed record StorageStartupResult(StorageStartupStatus Status, LightflowStorageCoordinator? Coordinator = null, string? Diagnostic = null)
{
    public bool IsReady => Status == StorageStartupStatus.Ready;
}

internal enum StorageChangeStatus { Succeeded, SucceededWithWarning, EquivalentLocation, InvalidDestination, ConflictingCatalog, Failed }
internal sealed record StorageChangeResult(StorageChangeStatus Status, string? Diagnostic = null)
{
    public bool Succeeded => Status is StorageChangeStatus.Succeeded or StorageChangeStatus.SucceededWithWarning;
}

internal enum PreviewRelocationMode { MoveExisting, SwitchAndRebuild }

internal interface IStorageConfigurationStore
{
    bool TryLoad(out AppSettings settings, out string? diagnostic);
    void Save(AppSettings settings);
}

internal sealed class AppSettingsStorageConfigurationStore(string path) : IStorageConfigurationStore
{
    public bool TryLoad(out AppSettings settings, out string? diagnostic) =>
        AppSettingsStore.TryLoadForStartup(path, out settings, out diagnostic);
    public void Save(AppSettings settings) => AppSettingsStore.Save(path, settings);
}

internal interface ICatalogRelocationTransfer
{
    void Backup(string sourceDatabasePath, string destinationDatabasePath);
}

internal sealed class SqliteCatalogRelocationTransfer : ICatalogRelocationTransfer
{
    public void Backup(string sourceDatabasePath, string destinationDatabasePath)
    {
        var sourceString = new SqliteConnectionStringBuilder { DataSource = sourceDatabasePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString();
        var destinationString = new SqliteConnectionStringBuilder { DataSource = destinationDatabasePath, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false }.ToString();
        using var source = new SqliteConnection(sourceString);
        using var destination = new SqliteConnection(destinationString);
        source.Open();
        destination.Open();
        source.BackupDatabase(destination);
    }
}

internal interface ICatalogSessionActivator
{
    CatalogDatabaseSession Activate(CatalogDatabaseSession session);
}

internal sealed class CatalogSessionActivator : ICatalogSessionActivator
{
    public CatalogDatabaseSession Activate(CatalogDatabaseSession session) => session;
}

internal sealed class LightflowStorageCoordinator : IAsyncDisposable
{
    private readonly IStorageConfigurationStore _configuration;
    private readonly ICatalogRelocationTransfer _transfer;
    private readonly ICatalogSessionActivator _activator;
    private ICatalogRecoveryService _recovery;
    private readonly SemaphoreSlim _mutationGate = new(1, 1);
    private CatalogDatabaseSession? _catalogSession;

    private LightflowStorageCoordinator(IStorageConfigurationStore configuration, AppSettings settings,
        LightflowStorageLocations locations, CatalogDatabaseSession? session, ICatalogRelocationTransfer transfer,
        ICatalogSessionActivator activator, ICatalogRecoveryService recovery)
    {
        _configuration = configuration;
        Settings = settings;
        Locations = locations;
        _catalogSession = session;
        _transfer = transfer;
        _activator = activator;
        _recovery = recovery;
        PreviewAvailable = settings.PreviewsDirectory is null || Directory.Exists(locations.PreviewsDirectory);
        PreviewDiagnostic = PreviewAvailable ? null : $"The configured Previews directory is unavailable: {locations.PreviewsDirectory}";
        Previews = PreviewAvailable ? new PreviewStoreService(locations) : null;
        MediaRoots = new MediaRootService(() => _catalogSession,
            new MachineIdentityProvider(locations.MachineIdentityPath), new MediaRootFileSystem());
        MediaAssets = new MediaAssetService(new CatalogMediaAssetRepository(() => _catalogSession),
            MediaRoots, new SampledSourceFingerprintService());
    }

    public AppSettings Settings { get; private set; }
    public LightflowStorageLocations Locations { get; private set; }
    public CatalogDatabaseSession CatalogSession => _catalogSession ?? throw new InvalidOperationException("The Catalog is not open.");
    public bool CatalogAvailable => _catalogSession is not null;
    public bool PreviewAvailable { get; private set; }
    public string? PreviewDiagnostic { get; private set; }
    public string? RecoveryDiagnostic { get; private set; }
    public IMediaRootService MediaRoots { get; }
    public IMediaAssetService MediaAssets { get; }
    public IPreviewStoreService? Previews { get; private set; }
    public IReadOnlyList<CatalogBackup> CatalogBackups => _recovery.ListBackups();

    public static async Task<StorageStartupResult> StartAsync(string? localApplicationData = null,
        CancellationToken cancellationToken = default, ICatalogRelocationTransfer? transfer = null,
        IStorageConfigurationStore? configuration = null, ICatalogSessionActivator? activator = null,
        ICatalogRecoveryService? recovery = null)
    {
        transfer ??= new SqliteCatalogRelocationTransfer();
        activator ??= new CatalogSessionActivator();
        var defaults = localApplicationData is null
            ? LightflowStorageLocations.CreateDefault()
            : LightflowStorageLocations.Create(localApplicationData);
        configuration ??= new AppSettingsStorageConfigurationStore(defaults.SettingsPath);
        if (!configuration.TryLoad(out var settings, out var settingsDiagnostic))
            return new(StorageStartupStatus.InvalidConfiguration, Diagnostic: settingsDiagnostic);
        LightflowStorageLocations locations;
        try
        {
            locations = localApplicationData is null
                ? LightflowStorageLocations.CreateDefault(new(settings.CatalogDirectory, settings.PreviewsDirectory))
                : LightflowStorageLocations.Create(localApplicationData, new(settings.CatalogDirectory, settings.PreviewsDirectory));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return new(StorageStartupStatus.InvalidConfiguration, Diagnostic: exception.Message);
        }

        recovery ??= new SqliteCatalogRecoveryService(locations);
        var database = new CatalogDatabaseService(locations, recovery);
        CatalogOpenResult opened;
        if (!File.Exists(locations.CatalogDatabasePath) && settings.CatalogId is null && settings.CatalogDirectory is null)
        {
            opened = await database.CreateNewAsync(cancellationToken).ConfigureAwait(false);
            if (opened.IsSuccess)
            {
                settings = settings with { CatalogId = opened.Session!.Identity.CatalogId };
                try { configuration.Save(settings); }
                catch
                {
                    await opened.Session.DisposeAsync().ConfigureAwait(false);
                    throw;
                }
            }
        }
        else
        {
            opened = await database.OpenExistingAsync(cancellationToken).ConfigureAwait(false);
        }

        if (!opened.IsSuccess)
            return new(Map(opened.Status),
                new LightflowStorageCoordinator(configuration, settings, locations, null, transfer, activator, recovery), opened.Diagnostic);
        if (settings.CatalogId is Guid expected && opened.Session!.Identity.CatalogId != expected)
        {
            await opened.Session.DisposeAsync().ConfigureAwait(false);
            return new(StorageStartupStatus.CatalogIdentityMismatch,
                new LightflowStorageCoordinator(configuration, settings, locations, null, transfer, activator, recovery),
                "The configured Catalog does not match the Catalog previously associated with this Lightflow installation.");
        }

        if (settings.CatalogId is null)
        {
            settings = settings with { CatalogId = opened.Session!.Identity.CatalogId };
            try { configuration.Save(settings); }
            catch
            {
                await opened.Session.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }
        var coordinator = new LightflowStorageCoordinator(configuration, settings, locations, opened.Session, transfer, activator, recovery);
        var automaticBackup = await recovery.CreateBackupAsync(locations.CatalogDatabasePath, CatalogBackupKind.Automatic,
            onlyIfNeededToday: true, cancellationToken).ConfigureAwait(false);
        if (!automaticBackup.Succeeded)
            coordinator.RecoveryDiagnostic = $"Automatic Catalog backup failed: {automaticBackup.Diagnostic}";
        return new(StorageStartupStatus.Ready, coordinator);
    }

    public async Task<CatalogBackupResult> BackupCatalogAsync(CancellationToken cancellationToken = default)
    {
        await _mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_catalogSession is null) return new(false, Diagnostic: "The Catalog is unavailable.");
            return await _recovery.CreateBackupAsync(Locations.CatalogDatabasePath, CatalogBackupKind.Automatic,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        finally { _mutationGate.Release(); }
    }

    public async Task<CatalogRestoreResult> RestoreCatalogAsync(string backupPath, CancellationToken cancellationToken = default)
    {
        await _mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var expectedId = Settings.CatalogId;
            var candidate = await _recovery.CheckIntegrityAsync(backupPath, cancellationToken).ConfigureAwait(false);
            if (!candidate.IsValid) return new(false, $"The selected backup is invalid. {candidate.Diagnostic}");
            if (expectedId is Guid expectedCandidate && candidate.CatalogId != expectedCandidate)
                return new(false, "The selected backup belongs to a different Lightflow Catalog.");
            var currentSession = _catalogSession;
            var hadActiveCatalog = currentSession is not null;
            if (currentSession is not null)
            {
                await currentSession.DisposeAsync().ConfigureAwait(false);
                _catalogSession = null;
            }
            var installation = await _recovery.BeginRestoreAsync(backupPath, hadActiveCatalog, cancellationToken).ConfigureAwait(false);
            if (!installation.Succeeded)
            {
                var reactivation = await TryReactivateCatalogAsync().ConfigureAwait(false);
                var diagnostic = installation.Diagnostic ?? "The Catalog could not be restored.";
                if (hadActiveCatalog && !reactivation.Succeeded)
                    diagnostic += $" The previous Catalog could not be reactivated: {reactivation.Diagnostic}";
                return new(false, diagnostic);
            }

            CatalogDatabaseSession? replacementSession = null;
            try
            {
                var opened = await new CatalogDatabaseService(Locations, _recovery)
                    .OpenExistingAsync(CancellationToken.None).ConfigureAwait(false);
                if (!opened.IsSuccess)
                    throw new InvalidDataException(opened.Diagnostic ?? "The restored Catalog could not be opened.");
                replacementSession = opened.Session;
                if (expectedId is Guid expected && replacementSession!.Identity.CatalogId != expected)
                    throw new InvalidDataException("The restored backup belongs to a different Lightflow Catalog.");
                replacementSession = _activator.Activate(replacementSession!);
                var committed = await installation.Transaction!.CommitAsync(CancellationToken.None).ConfigureAwait(false);
                if (!committed.Succeeded) throw new IOException(committed.Diagnostic);
                _catalogSession = replacementSession;
                replacementSession = null;
                return committed;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                if (replacementSession is not null) await replacementSession.DisposeAsync().ConfigureAwait(false);
                var rollback = await installation.Transaction!.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                if (!rollback.Succeeded)
                    return new(false, $"The restored Catalog could not be activated: {exception.Message} {rollback.Diagnostic}");
                var reactivation = await TryReactivateCatalogAsync().ConfigureAwait(false);
                if (!reactivation.Succeeded)
                    return new(false, $"The restored Catalog could not be activated: {exception.Message} {rollback.Diagnostic} The previous Catalog could not be reactivated: {reactivation.Diagnostic}");
                return new(false, $"The restored Catalog could not be activated, so the previous Catalog was restored and reactivated. {exception.Message}");
            }
        }
        finally { _mutationGate.Release(); }
    }

    private async Task<CatalogRestoreResult> TryReactivateCatalogAsync()
    {
        var opened = await new CatalogDatabaseService(Locations, _recovery)
            .OpenExistingAsync(CancellationToken.None).ConfigureAwait(false);
        if (!opened.IsSuccess) return new(false, opened.Diagnostic ?? "The Catalog could not be reopened.");
        try
        {
            _catalogSession = _activator.Activate(opened.Session!);
            return new(true);
        }
        catch (Exception exception)
        {
            await opened.Session!.DisposeAsync().ConfigureAwait(false);
            return new(false, exception.Message);
        }
    }

    public async Task<StorageChangeResult> RelocateCatalogAsync(string destinationDirectory,
        CancellationToken cancellationToken = default)
    {
        await _mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return await RelocateCatalogCoreAsync(destinationDirectory, cancellationToken).ConfigureAwait(false); }
        finally { _mutationGate.Release(); }
    }

    private async Task<StorageChangeResult> RelocateCatalogCoreAsync(string destinationDirectory,
        CancellationToken cancellationToken)
    {
        LightflowStorageLocations destination;
        try
        {
            destination = ValidateDestination(destinationDirectory, catalog: true);
            if (SamePath(Locations.CatalogDirectory, destination.CatalogDirectory))
                return new(StorageChangeStatus.EquivalentLocation, "That is already the active Catalog location.");
            if (File.Exists(destination.CatalogDatabasePath) || Directory.Exists(destination.CatalogDatabasePath))
                return new(StorageChangeStatus.ConflictingCatalog, "A Catalog already exists at the selected location.");
            ProbeWritable(destination.CatalogDirectory);
            if (Directory.EnumerateFileSystemEntries(destination.CatalogDirectory).Any())
                return new(StorageChangeStatus.ConflictingCatalog,
                    "The selected Catalog folder must be empty so Lightflow cannot overwrite or mix with existing data.");
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException or OperationCanceledException)
        {
            return new(StorageChangeStatus.InvalidDestination, exception.Message);
        }

        var sourceLocations = Locations;
        var sourceSettings = Settings;
        var expected = CatalogSession.Identity;
        var expectedSchema = CatalogSession.SchemaVersion;
        await _catalogSession!.DisposeAsync().ConfigureAwait(false);
        _catalogSession = null;
        var staged = destination.CatalogDatabasePath + $".{Guid.NewGuid():N}.moving";
        var destinationOwned = false;
        var configurationSwitched = false;
        CatalogDatabaseSession? destinationSession = null;
        try
        {
            Directory.CreateDirectory(destination.CatalogDirectory);
            _transfer.Backup(sourceLocations.CatalogDatabasePath, staged);
            File.Move(staged, destination.CatalogDatabasePath);
            destinationOwned = true;
            var opened = await new CatalogDatabaseService(destination).OpenExistingAsync(cancellationToken).ConfigureAwait(false);
            destinationSession = opened.Session;
            if (!opened.IsSuccess || opened.Session!.Identity.CatalogId != expected.CatalogId ||
                opened.Session.SchemaVersion != expectedSchema)
            {
                throw new InvalidDataException(opened.Diagnostic ?? "The relocated Catalog failed identity or schema validation.");
            }

            var changed = sourceSettings with { CatalogDirectory = destination.CatalogDirectory, CatalogId = expected.CatalogId };
            _configuration.Save(changed);
            configurationSwitched = true;
            destinationSession = _activator.Activate(destinationSession!);
            Settings = changed;
            Locations = destination;
            _recovery = new SqliteCatalogRecoveryService(destination);
            _catalogSession = destinationSession;
            destinationSession = null;
            return new(StorageChangeStatus.Succeeded);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SqliteException or InvalidDataException or OperationCanceledException)
        {
            if (configurationSwitched)
            {
                try { _configuration.Save(sourceSettings); }
                catch (Exception rollbackException) when (rollbackException is IOException or UnauthorizedAccessException)
                {
                    _catalogSession = destinationSession;
                    destinationSession = null;
                    Locations = destination;
                    Settings = sourceSettings with { CatalogDirectory = destination.CatalogDirectory, CatalogId = expected.CatalogId };
                    return new(StorageChangeStatus.SucceededWithWarning,
                        $"The Catalog was moved, but Lightflow could not restore the prior configuration after activation failed. The validated destination remains active. {rollbackException.Message}");
                }
            }
            if (destinationSession is not null) await destinationSession.DisposeAsync().ConfigureAwait(false);
            try { File.Delete(staged); } catch { }
            if (destinationOwned) DeleteCatalogFiles(destination.CatalogDatabasePath);
            var reopened = await new CatalogDatabaseService(sourceLocations).OpenExistingAsync(CancellationToken.None).ConfigureAwait(false);
            if (reopened.IsSuccess) _catalogSession = reopened.Session;
            Locations = sourceLocations;
            Settings = sourceSettings;
            return new(StorageChangeStatus.Failed, $"The Catalog was not moved. {exception.Message}");
        }
    }

    public async Task<StorageChangeResult> RelocatePreviewsAsync(string destinationDirectory,
        PreviewRelocationMode mode, CancellationToken cancellationToken = default)
    {
        await _mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return await RelocatePreviewsCoreAsync(destinationDirectory, mode, cancellationToken).ConfigureAwait(false); }
        finally { _mutationGate.Release(); }
    }

    private async Task<StorageChangeResult> RelocatePreviewsCoreAsync(string destinationDirectory,
        PreviewRelocationMode mode, CancellationToken cancellationToken)
    {
        LightflowStorageLocations destination;
        var sourceDirectory = Locations.PreviewsDirectory;
        string? stagingDirectory = null;
        string? ownedDestinationDirectory = null;
        var destinationOwned = false;
        var configurationSwitched = false;
        var previewStoreQuiesced = false;
        try
        {
            destination = ValidateDestination(destinationDirectory, catalog: false);
            if (SamePath(Locations.PreviewsDirectory, destination.PreviewsDirectory))
                return new(StorageChangeStatus.EquivalentLocation, "That is already the active Previews location.");
            ProbeWritable(destination.PreviewsDirectory);
            if (Directory.EnumerateFileSystemEntries(destination.PreviewsDirectory).Any())
                throw new IOException("The selected Previews destination must be empty.");
            if (Previews is not null)
            {
                await Previews.DisposeAsync().ConfigureAwait(false);
                Previews = null;
                previewStoreQuiesced = true;
            }
            if (mode == PreviewRelocationMode.MoveExisting)
            {
                stagingDirectory = destination.PreviewsDirectory + $".lightflow-moving-{Guid.NewGuid():N}";
                await CopyDirectoryAsync(sourceDirectory, stagingDirectory, cancellationToken).ConfigureAwait(false);
                Directory.Delete(destination.PreviewsDirectory);
                Directory.Move(stagingDirectory, destination.PreviewsDirectory);
                stagingDirectory = null;
                destinationOwned = true;
                ownedDestinationDirectory = destination.PreviewsDirectory;
            }
            var changed = Settings with { PreviewsDirectory = destination.PreviewsDirectory };
            _configuration.Save(changed);
            configurationSwitched = true;
            Settings = changed;
            Locations = destination;
            PreviewAvailable = true;
            PreviewDiagnostic = null;
            Previews = new PreviewStoreService(destination);
            if (mode == PreviewRelocationMode.MoveExisting)
            {
                try { Directory.Delete(sourceDirectory, recursive: true); } catch { }
            }
            return new(StorageChangeStatus.Succeeded);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException or OperationCanceledException)
        {
            if (stagingDirectory is not null)
            {
                try { Directory.Delete(stagingDirectory, recursive: true); } catch { }
            }
            if (mode == PreviewRelocationMode.MoveExisting && destinationOwned && !configurationSwitched)
            {
                try { Directory.Delete(ownedDestinationDirectory!, recursive: true); } catch { }
            }
            if (previewStoreQuiesced && !configurationSwitched)
                Previews = new PreviewStoreService(Locations);
            return new(StorageChangeStatus.Failed, $"The Previews location was not changed. {exception.Message}");
        }
    }

    public void SaveSettings(AppSettings settings)
    {
        _mutationGate.Wait();
        try
        {
            var preserved = settings with
            {
                CatalogDirectory = Settings.CatalogDirectory,
                PreviewsDirectory = Settings.PreviewsDirectory,
                CatalogId = Settings.CatalogId
            };
            _configuration.Save(preserved);
            Settings = preserved;
        }
        finally { _mutationGate.Release(); }
    }

    private LightflowStorageLocations ValidateDestination(string directory, bool catalog)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        if (!Path.IsPathFullyQualified(directory)) throw new ArgumentException("Choose an absolute folder path.");
        var normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        if (catalog && IsUnc(normalized))
            throw new NotSupportedException("A live Catalog cannot be stored on a network or UNC path.");
        return Locations.WithOverrides(catalog
                ? new(normalized, Locations.PreviewsDirectory)
                : new(Locations.CatalogDirectory, normalized));
    }

    private static bool IsUnc(string path) => path.StartsWith(@"\\", StringComparison.Ordinal) ||
        path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase);

    private static bool SamePath(string left, string right) =>
        string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)), StringComparison.OrdinalIgnoreCase);

    private static void ProbeWritable(string directory)
    {
        if (File.Exists(directory)) throw new IOException("The selected path is a file, not a folder.");
        Directory.CreateDirectory(directory);
        var probe = Path.Combine(directory, $".lightflow-write-{Guid.NewGuid():N}.tmp");
        try { File.WriteAllText(probe, "storage validation"); }
        finally { try { File.Delete(probe); } catch { } }
    }

    private static void DeleteCatalogFiles(string databasePath)
    {
        foreach (var path in new[] { databasePath, databasePath + "-wal", databasePath + "-shm" })
        {
            try { File.Delete(path); } catch { }
        }
    }

    private static async Task CopyDirectoryAsync(string source, string destination, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(source)) { Directory.CreateDirectory(destination); return; }
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await using var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
            await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
            await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        }
    }

    private static StorageStartupStatus Map(CatalogOpenStatus status) => status switch
    {
        CatalogOpenStatus.MissingExpectedCatalog => StorageStartupStatus.CatalogMissing,
        CatalogOpenStatus.StorageUnavailable => StorageStartupStatus.CatalogUnavailable,
        CatalogOpenStatus.Corrupt => StorageStartupStatus.CatalogCorrupt,
        _ => StorageStartupStatus.CatalogUnreadable
    };

    public async ValueTask DisposeAsync()
    {
        await _mutationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_catalogSession is not null) await _catalogSession.DisposeAsync().ConfigureAwait(false);
            _catalogSession = null;
            if (Previews is not null) await Previews.DisposeAsync().ConfigureAwait(false);
            Previews = null;
        }
        finally
        {
            _mutationGate.Release();
            _mutationGate.Dispose();
        }
    }
}
