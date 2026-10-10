using Lightflow.Application;
using Lightflow.Domain;
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

internal sealed class AppSettingsStorageConfigurationStore(string path, bool isolated = false) : IStorageConfigurationStore
{
    public bool TryLoad(out AppSettings settings, out string? diagnostic) =>
        AppSettingsStore.TryLoadForStartup(path, out settings, out diagnostic, isolated);
    public void Save(AppSettings settings) => AppSettingsStore.Save(path, settings, isolated);
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
    private StartupSessionCompletion? _startupCompletion;
    private StartupStoreEvidence? _catalogStartup;
    private StartupStoreEvidence? _previewStartup;
    private bool _shutdownAdmissionClosed;
    private bool _cleanShutdownAllowed = true;
    internal void PreventCleanShutdown() => _cleanShutdownAllowed = false;
    private readonly IStorageConfigurationStore _configuration;
    private readonly ICatalogRelocationTransfer _transfer;
    private readonly ICatalogSessionActivator _activator;
    private readonly IStorageLocationAssessor? _assessor;
    private ICatalogRecoveryService _recovery;
    private readonly SemaphoreSlim _mutationGate = new(1, 1);
    private readonly CancellationTokenSource _readShutdown = new();
    private readonly PreviewOperationCoordinator _previewOperations = new();
    private readonly SemaphoreSlim _thumbnailRegenerationGate = new(2, 2);
    private readonly AssetPreviewGenerationGate _assetPreviewGenerationGate = new();
    internal CatalogMutationLifecycle Mutations { get; } = new();
    private CatalogDatabaseSession? _session;
    private CatalogDatabaseSession? _catalogSession
    {
        get => _session;
        set { _session = value; if (value is not null) value.Mutations = Mutations; }
    }

    private LightflowStorageCoordinator(IStorageConfigurationStore configuration, AppSettings settings,
        LightflowStorageLocations locations, CatalogDatabaseSession? session, ICatalogRelocationTransfer transfer,
        ICatalogSessionActivator activator, ICatalogRecoveryService recovery, IPreviewStoreService? previews,
        string? previewDiagnostic, StartupStoreEvidence? catalogStartup = null, StartupStoreEvidence? previewStartup = null, StartupSessionCompletion? startupCompletion = null,
        IStorageLocationAssessor? assessor = null)
    {
        _catalogStartup = catalogStartup; _previewStartup = previewStartup; _startupCompletion = startupCompletion;
        _configuration = configuration;
        Settings = settings;
        Locations = locations;
        _catalogSession = session;
        session?.MarkPublished();
        _transfer = transfer;
        _activator = activator;
        _assessor = assessor;
        _recovery = recovery;
        Previews = previews;
        PreviewAvailable = previews is not null;
        PreviewDiagnostic = previewDiagnostic;
        MediaRoots = new MediaRootService(() => _catalogSession,
            new MachineIdentityProvider(locations.MachineIdentityPath), new MediaRootFileSystem());
        BrowserStorage = new BrowserStorageProvider(MediaRoots, new WindowsBrowserVolumeProvider());
        BrowserLocations = new BrowserLocationResolver(MediaRoots, new BrowserLocationFileSystem());
        MediaAssets = new MediaAssetService(new CatalogMediaAssetRepository(() => _catalogSession, Mutations),
            MediaRoots, new SampledSourceFingerprintService());
        BrowserRecursiveRoots = new BrowserRecursiveRootService(
            new CatalogBrowserRecursiveRootRepository(() => _catalogSession, Mutations));
        MediaTypes = MediaTypeRegistry.CreateDefault();
        MediaFolders = new MediaFolderEnumerator(MediaRoots, MediaTypes, new MediaFolderFileSystem(),
            excludedPath: IsOwnedStoragePath);
        CatalogReconciliation = new CatalogReconciliationService(MediaFolders, MediaAssets);
        MediaRanges = new CatalogMediaRangeStore(() => _catalogSession);
        Markers = new CatalogMarkerService(() => _catalogSession);
        Subclips = new CatalogSubclipService(() => _catalogSession);
        Collections = new CatalogCollectionOrganizationService(() => _catalogSession);
        PreferredPreviewFrames = new CatalogPreferredPreviewFrameStore(() => _catalogSession);
        Luts = new CatalogFolderLutLibrary(() => _catalogSession);
        LutCache = new ApplicationLutLibraryCache(Luts);
        AssetColors = new CatalogAssetColorStore(() => _catalogSession, LutCache);
        BrowserAssetStates = new CatalogBrowserAssetStateStore(() => _catalogSession);
        AssetClassifications = new CatalogAssetClassificationStore(() => _catalogSession);
        AssetDescriptions = new CatalogAssetDescriptionStore(() => _catalogSession);
        _videoRotations = new CatalogAssetVideoRotationStore(() => _catalogSession);
        AssetCopies = new AssetCopyDataService(() => _catalogSession, previews);
        ThumbnailActivity = new ThumbnailGenerationActivity();
        DerivedWork = CreateDerivedWorkScheduler();
        MediaDiscovery = new MediaDiscoveryRefreshService(CatalogReconciliation, () => DerivedWork);
        RecursiveMediaDiscovery = new RecursiveMediaDiscoveryService(MediaFolders, MediaDiscovery);
        MediaMonitoring = new MediaRootMonitoringService(MediaRoots, MediaDiscovery, excludedPath: IsOwnedStoragePath);
    }

    public AppSettings Settings { get; private set; }
    public LightflowStorageLocations Locations { get; private set; }
    public CatalogDatabaseSession CatalogSession => _catalogSession ?? throw new InvalidOperationException("The Catalog is not open.");
    public bool CatalogAvailable => _catalogSession is not null;
    public bool PreviewAvailable { get; private set; }
    public string? PreviewDiagnostic { get; private set; }
    public string? RecoveryDiagnostic { get; private set; }
    public IMediaRootService MediaRoots { get; }
    public IBrowserStorageProvider BrowserStorage { get; }
    public IBrowserLocationResolver BrowserLocations { get; }
    public IMediaAssetService MediaAssets { get; }
    public IMediaRangeStore MediaRanges { get; }
    public ISubclipService Subclips { get; }
    public IMarkerService Markers { get; }
    public ICollectionOrganizationService Collections { get; }
    public CatalogCollectionOrganizationService SmartCollections => (CatalogCollectionOrganizationService)Collections;
    public IPreferredPreviewFrameStore PreferredPreviewFrames { get; }
    public IBrowserAssetStateStore BrowserAssetStates { get; }
    public IAssetClassificationStore AssetClassifications { get; }
    public IAssetDescriptionStore AssetDescriptions { get; }
    private readonly CatalogAssetVideoRotationStore _videoRotations;
    public IAssetVideoRotationStore VideoRotations => _videoRotations;
    public IAssetCopyDataService AssetCopies { get; }
    public ILutLibrary Luts { get; }
    public ILutLibraryCache LutCache { get; }
    public IAssetColorStore AssetColors { get; }
    public IThumbnailGenerationActivity ThumbnailActivity { get; }
    /// <summary>#124 (revised): durable Catalog storage for Browser "Include Subfolders" recursive roots. See <see cref="BrowserRecursiveRoot"/>.</summary>
    public IBrowserRecursiveRootService BrowserRecursiveRoots { get; }
    public IMediaTypeRegistry MediaTypes { get; }
    public IMediaFolderEnumerator MediaFolders { get; }
    public ICatalogReconciliationService CatalogReconciliation { get; }
    public IDerivedWorkScheduler? DerivedWork { get; private set; }
    public IMediaDiscoveryRefreshService MediaDiscovery { get; }
    public IRecursiveMediaDiscoveryService RecursiveMediaDiscovery { get; }
    public IMediaRootMonitoringService? MediaMonitoring { get; private set; }
    public IPreviewStoreService? Previews { get; private set; }
    public IReadOnlyList<CatalogBackup> CatalogBackups => _recovery.ListBackups()
        .Concat(SqliteCatalogRecoveryService.ListUserBackups(Locations, BackupDirectory))
        .OrderByDescending(backup => backup.CreatedUtc).ToArray();

    public static async Task<StorageStartupResult> StartAsync(string? localApplicationData = null,
        CancellationToken cancellationToken = default, ICatalogRelocationTransfer? transfer = null,
        IStorageConfigurationStore? configuration = null, ICatalogSessionActivator? activator = null,
        ICatalogRecoveryService? recovery = null, LightflowStorageLocations? profile = null,
        InitializedDataProfile? initializedProfile = null, IStorageLocationAssessor? assessor = null)
    {
        using var timing = StartupDiagnostics.Stage("Storage initialization", "Opening storage…");
        transfer ??= new SqliteCatalogRelocationTransfer();
        activator ??= new CatalogSessionActivator();
        var defaults = profile ?? (localApplicationData is null
            ? LightflowStorageLocations.Current
            : LightflowStorageLocations.Create(localApplicationData));
        initializedProfile ??= ApplicationDataProfile.Initialize(defaults);
        initializedProfile.RequireProfile(defaults);
        configuration ??= new AppSettingsStorageConfigurationStore(defaults.SettingsPath, defaults.IsIsolated);
        if (!configuration.TryLoad(out var settings, out var settingsDiagnostic))
            return new(StorageStartupStatus.InvalidConfiguration, Diagnostic: settingsDiagnostic);
        if (defaults.IsIsolated && !File.Exists(defaults.SettingsPath))
            settings = settings with { LutFolder = null, CameraLutFolder = "", CreativeLutFolder = "",
                ScreengrabDirectory = Path.Combine(defaults.ApplicationDataDirectory, "Screengrabs") };
        LightflowStorageLocations locations;
        try
        {
            locations = defaults.WithOverrides(new(settings.CatalogDirectory, settings.PreviewsDirectory));
            // Configured database leaves/SQLite sidecars are distinct from the default boundaries
            // validated before reading settings; no cache descendants or second initialization.
            if (defaults.IsIsolated)
                foreach (var databasePath in new[] { locations.CatalogDatabasePath, locations.PreviewsDatabasePath }
                             .Except(new[] { defaults.CatalogDatabasePath, defaults.PreviewsDatabasePath }, StringComparer.OrdinalIgnoreCase))
                    foreach (var suffix in new[] { "", "-wal", "-shm", "-journal", ".startup-state" })
                        ApplicationDataProfile.GuardAccess(databasePath + suffix);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or IOException or UnauthorizedAccessException)
        {
            return new(StorageStartupStatus.InvalidConfiguration, Diagnostic: exception.Message);
        }

        recovery ??= new SqliteCatalogRecoveryService(locations);
        var needsBackupConfiguration = settings.CatalogBackupDirectory is null;
        if (settings.CatalogBackupDirectory is null)
        {
            // Deterministic new/existing-profile migration. Preserve all managed recovery copies.
            settings = settings with { CatalogBackupDirectory = CatalogBackupDestination.Default(locations) };
        }
        StartupStoreEvidence? catalogStartup = null, previewStartup = null;
        StartupSessionCompletion? startupCompletion = null;
        CatalogDatabaseSession? pendingSession = null;
        IPreviewStoreService? pendingPreviews = null;
        LightflowStorageCoordinator? pendingCoordinator = null;
        var create = !File.Exists(locations.CatalogDatabasePath) && settings.CatalogId is null && settings.CatalogDirectory is null;
        using var admission = new CatalogLocationAdmission(locations, create ? StorageOperation.Create : StorageOperation.Open, assessor);
        try
        {
            try
            {
                await admission.ValidateAsync(cancellationToken).ConfigureAwait(false);
                await admission.ValidateAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (CatalogLocationAdmissionException exception)
            {
                var (availablePreviews, diagnostic) = await OpenPreviewsAsync(settings, locations, cancellationToken).ConfigureAwait(false);
                return new(StorageStartupStatus.CatalogUnavailable,
                    new LightflowStorageCoordinator(configuration, settings, locations, null, transfer, activator,
                        recovery, availablePreviews, diagnostic, assessor: assessor), exception.Message);
            }
            var boundLocations = admission.LocationsForUse();
            startupCompletion = new StartupSessionCompletion(locations.ApplicationDataDirectory);
            catalogStartup = StartupStoreEvidence.Begin(locations.CatalogDatabasePath, "Catalog", settings.CatalogDirectory is null, startupCompletion,
                boundLocations.CatalogDatabasePath);
            previewStartup = StartupStoreEvidence.Begin(locations.PreviewsDatabasePath, "Previews", settings.PreviewsDirectory is null, startupCompletion);
            var cleanPair = catalogStartup?.KnownClean == true && previewStartup?.KnownClean == true;
            catalogStartup?.RequireCompletePeer(cleanPair); previewStartup?.RequireCompletePeer(cleanPair);
            StartupDiagnostics.Note($"Catalog startup decision: {(catalogStartup?.KnownClean == true ? "fast candidate" : "deep")} reason={catalogStartup?.Reason ?? StartupValidationReason.UnexpectedShutdown}");
            StartupDiagnostics.Note($"Previews startup decision: {(previewStartup?.KnownClean == true ? "fast candidate" : "deep")} reason={previewStartup?.Reason ?? StartupValidationReason.UnexpectedShutdown}");
            var boundRecovery = recovery is SqliteCatalogRecoveryService nativeRecovery ? nativeRecovery.ForLocations(boundLocations) : recovery;
            var database = new CatalogDatabaseService(locations, boundRecovery)
            { CleanStartup = catalogStartup?.KnownClean == true, ValidationReason = catalogStartup?.Reason ?? StartupValidationReason.UnexpectedShutdown,
                ValidateStorageAccess = (token, phase) => admission.ValidateClosedDatabaseBoundary(token, phase),
                ResolvedDatabasePath = boundLocations.CatalogDatabasePath };
            CatalogOpenResult opened;
            await admission.ValidateAsync(cancellationToken).ConfigureAwait(false);
            if (create)
            {
                opened = await database.CreateNewAsync(cancellationToken).ConfigureAwait(false);
            }
            else
            {
                opened = await database.OpenExistingAsync(cancellationToken).ConfigureAwait(false);
            }

            pendingSession = opened.Session;
            if (opened.IsSuccess)
            {
                opened.Session!.CloseBeforeActivation();
                await admission.ValidateAsync(cancellationToken, "AfterCatalogOpen_AfterInitialSQLiteUse").ConfigureAwait(false);
            }
            if (!opened.IsSuccess)
            {
                StartupDiagnostics.Note($"Catalog readiness: {opened.Status}; transition=existing recovery surface");
                var (unavailableCatalogPreviews, unavailableCatalogPreviewDiagnostic) =
                    await OpenPreviewsAsync(settings, locations, cancellationToken, previewStartup).ConfigureAwait(false);
                return new(Map(opened.Status),
                    new LightflowStorageCoordinator(configuration, settings, locations, null, transfer, activator, recovery,
                        unavailableCatalogPreviews, unavailableCatalogPreviewDiagnostic, catalogStartup, previewStartup, startupCompletion, assessor), opened.Diagnostic);
            }
            if (settings.CatalogId is Guid expected && !PortablePathIdentityValidator.ValidateCatalogIdentity(expected, opened.Session!.Identity.CatalogId).IsSafe)
            {
                await opened.Session.DisposeAsync().ConfigureAwait(false);
                var (mismatchedCatalogPreviews, mismatchedCatalogPreviewDiagnostic) =
                    await OpenPreviewsAsync(settings, locations, cancellationToken, previewStartup).ConfigureAwait(false);
                return new(StorageStartupStatus.CatalogIdentityMismatch,
                    new LightflowStorageCoordinator(configuration, settings, locations, null, transfer, activator, recovery,
                        mismatchedCatalogPreviews, mismatchedCatalogPreviewDiagnostic, catalogStartup, previewStartup, startupCompletion, assessor),
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
            if (needsBackupConfiguration)
            {
                try { configuration.Save(settings); }
                catch { await opened.Session!.DisposeAsync().ConfigureAwait(false); throw; }
            }
            var (previews, previewDiagnostic) = await OpenPreviewsAsync(settings, locations, cancellationToken, previewStartup).ConfigureAwait(false);
            pendingPreviews = previews;
            var coordinator = new LightflowStorageCoordinator(configuration, settings, locations, opened.Session, transfer,
                activator, recovery, previews, previewDiagnostic, catalogStartup, previewStartup, startupCompletion, assessor);
            pendingCoordinator = coordinator;
            using (StartupDiagnostics.Stage("Media-root monitoring", "Checking media locations…"))
                await coordinator.MediaMonitoring!.StartAsync(cancellationToken).ConfigureAwait(false);
            StartupDiagnostics.Note("Catalog readiness: succeeded; Previews readiness: " + (previews is null ? "unavailable" : "succeeded"));
            return new(StorageStartupStatus.Ready, coordinator);
        }
        catch (CatalogLocationAdmissionException exception)
        {
            if (pendingSession is not null) await pendingSession.DisposeAsync().ConfigureAwait(false);
            catalogStartup?.Dispose(); previewStartup?.Dispose(); startupCompletion?.Dispose();
            var (availablePreviews, diagnostic) = await OpenPreviewsAsync(settings, locations, cancellationToken).ConfigureAwait(false);
            return new(StorageStartupStatus.CatalogUnavailable,
                new LightflowStorageCoordinator(configuration, settings, locations, null, transfer, activator,
                    recovery, availablePreviews, diagnostic, assessor: assessor), exception.Message);
        }
        catch
        {
            try
            {
                if (pendingCoordinator is not null)
                { pendingCoordinator.PreventCleanShutdown(); await pendingCoordinator.DisposeAsync().ConfigureAwait(false); }
                else
                {
                    if (pendingSession is not null) await pendingSession.DisposeAsync().ConfigureAwait(false);
                    if (pendingPreviews is not null) await pendingPreviews.DisposeAsync().ConfigureAwait(false);
                }
            }
            finally { catalogStartup?.Dispose(); previewStartup?.Dispose(); startupCompletion?.Dispose(); }
            throw;
        }
    }

    private static async Task<(IPreviewStoreService? Service, string? Diagnostic)> OpenPreviewsAsync(
        AppSettings settings, LightflowStorageLocations locations, CancellationToken cancellationToken, StartupStoreEvidence? startup = null)
    {
        if (settings.PreviewsDirectory is not null && !Directory.Exists(locations.PreviewsDirectory))
            return (null, $"The configured Previews directory is unavailable: {locations.PreviewsDirectory}");

        var previews = new PreviewStoreService(locations)
        { CleanStartup = startup?.KnownClean == true, ValidationReason = startup?.Reason ?? StartupValidationReason.UnexpectedShutdown };
        try
        {
            await previews.InitializeAsync(cancellationToken).ConfigureAwait(false);
            return (previews, null);
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException
            or NotSupportedException or SqliteException)
        {
            StartupDiagnostics.Note($"Previews readiness: unavailable; recovery transition=disabled; {exception.GetType().Name}");
            await previews.DisposeAsync().ConfigureAwait(false);
            return (null, $"The Preview store is unavailable: {exception.Message}");
        }
    }

    public async Task<CatalogBackupResult> BackupCatalogAsync(CancellationToken cancellationToken = default)
    {
        return await BackupForExitAsync(BackupDirectory, false, null, cancellationToken).ConfigureAwait(false);
    }

    internal string BackupDirectory => Settings.CatalogBackupDirectory ?? CatalogBackupDestination.Default(Locations);
    private CatalogMutationLifecycle.Quiescence? _exitQuiescence;

    internal async Task<CatalogBackupResult> BackupForExitAsync(string destination, bool keepQuiescent,
        IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        progress?.Report("Finishing accepted Catalog changes…");
        var operationId = Guid.NewGuid();
        StartupDiagnostics.Note($"Catalog backup: stage=WriterDrain; operationId={operationId:N}; begin");
        CatalogMutationLifecycle.Quiescence quiet;
        try { quiet = await Mutations.QuiesceAsync(cancellationToken).ConfigureAwait(false); }
        catch (Exception error)
        {
            // Preserve exception type/cancellation and drain semantics; add diagnostic context only.
            error.Data["CatalogBackupStage"] = "WriterDrain";
            error.Data["CatalogBackupOperationId"] = operationId;
            StartupDiagnostics.Note($"Catalog backup: stage=WriterDrain; operationId={operationId:N}; {error.GetType().Name}");
            throw;
        }
        var keep = false;
        try
        {
            // Catalog relocation/restore participate in admission, so the session is stable here.
            // Do not take the Preview maintenance lock: its workers can legitimately be waiting
            // to publish a new Catalog observation after this snapshot boundary reopens.
            if (_catalogSession is null) return new(false, Diagnostic:
                $"Catalog backup: stage=CatalogStartupAdmission; operationId={operationId:N}; The Catalog is unavailable.");
            var result = await new SqliteCatalogRecoveryService(Locations).CreateUserBackupAsync(
                _catalogSession.ResolvedDatabasePath, destination, _catalogSession.Identity.CatalogId,
                _catalogSession.SchemaVersion, progress, cancellationToken, diagnosticOperationId: operationId).ConfigureAwait(false);
            if (result.Succeeded && keepQuiescent) { _exitQuiescence = quiet; keep = true; }
            return result;
        }
        finally { if (!keep) quiet.Dispose(); }
    }

    internal void CancelPreparedExit() { _exitQuiescence?.Dispose(); _exitQuiescence = null; }
    internal void CompletePreparedExit()
    {
        if (_exitQuiescence is not null) { _exitQuiescence.CompleteShutdown(); _shutdownAdmissionClosed = true; }
        _exitQuiescence = null;
    }

    internal async Task SaveBackupDestinationAsync(string destination, CancellationToken token)
    {
        await _mutationGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var settings = Settings with { CatalogBackupDirectory = destination };
            await Task.Run(() => _configuration.Save(settings), token).ConfigureAwait(false);
            Settings = settings;
        }
        finally { _mutationGate.Release(); }
    }

    public async Task<CatalogRestoreResult> RestoreCatalogAsync(string backupPath, CancellationToken cancellationToken = default)
    {
        return await Mutations.RunAsync<CatalogRestoreResult>(async () => {
        await _mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await DisposeMediaMonitoringAsync().ConfigureAwait(false);
            await DisposeDerivedWorkSchedulerAsync().ConfigureAwait(false);
            using var admission = new CatalogLocationAdmission(Locations, StorageOperation.RestoreActivation, _assessor);
            try
            {
                await admission.ValidateAsync(cancellationToken).ConfigureAwait(false);
                admission.RequireActiveSessionBinding(_catalogSession);
            }
            catch (CatalogLocationAdmissionException exception) { return new(false, exception.Message); }
            var boundLocations = admission.LocationsForUse();
            var expectedId = Settings.CatalogId;
            var candidate = await _recovery.CheckIntegrityAsync(backupPath, cancellationToken).ConfigureAwait(false);
            if (!candidate.IsValid) return new(false, $"The selected backup is invalid. {candidate.Diagnostic}");
            if (expectedId is Guid expectedCandidate && !PortablePathIdentityValidator.ValidateCatalogIdentity(expectedCandidate, candidate.CatalogId ?? Guid.Empty).IsSafe)
                return new(false, "The selected backup belongs to a different Lightflow Catalog.");
            try { await admission.ValidateAsync(cancellationToken).ConfigureAwait(false); }
            catch (CatalogLocationAdmissionException exception) { return new(false, exception.Message); }
            var currentSession = _catalogSession;
            var hadActiveCatalog = currentSession is not null;
            if (currentSession is not null)
            {
                await currentSession.DisposeAsync().ConfigureAwait(false);
                _catalogSession = null;
            }
            CatalogRestoreInstallation installation;
            try
            {
                installation = _recovery is SqliteCatalogRecoveryService nativeRecovery
                    ? await nativeRecovery.ForLocations(boundLocations).BeginRestoreValidatedAsync(backupPath, hadActiveCatalog,
                        token => admission.ValidateAsync(token).GetAwaiter().GetResult(), cancellationToken).ConfigureAwait(false)
                    : await _recovery.BeginRestoreAsync(backupPath, hadActiveCatalog, cancellationToken).ConfigureAwait(false);
            }
            catch (CatalogLocationAdmissionException exception) { return new(false, exception.Message); }
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
                await admission.ValidateAsync(CancellationToken.None).ConfigureAwait(false);
                var migrationRecovery = _recovery is SqliteCatalogRecoveryService migrationNative
                    ? migrationNative.ForLocations(boundLocations) : _recovery;
                var opened = await new CatalogDatabaseService(Locations, migrationRecovery) { ValidationReason = StartupValidationReason.Restore,
                    ValidateStorageAccess = (token, phase) => admission.ValidateClosedDatabaseBoundary(token, phase),
                    ResolvedDatabasePath = boundLocations.CatalogDatabasePath }
                    .OpenExistingAsync(CancellationToken.None).ConfigureAwait(false);
                if (!opened.IsSuccess)
                    throw new InvalidDataException(opened.Diagnostic ?? "The restored Catalog could not be opened.");
                replacementSession = opened.Session;
                replacementSession!.CloseBeforeActivation();
                await admission.ValidateAsync(CancellationToken.None, "RestoreActivation_AfterInitialSQLiteUse").ConfigureAwait(false);
                if (expectedId is Guid expected && !PortablePathIdentityValidator.ValidateCatalogIdentity(expected, replacementSession!.Identity.CatalogId).IsSafe)
                    throw new InvalidDataException("The restored backup belongs to a different Lightflow Catalog.");
                replacementSession = _activator.Activate(replacementSession!);
                var committed = await installation.Transaction!.CommitAsync(CancellationToken.None).ConfigureAwait(false);
                if (!committed.Succeeded) throw new IOException(committed.Diagnostic);
                _catalogSession = replacementSession;
                replacementSession!.MarkPublished();
                replacementSession = null;
                _videoRotations.NotifyCatalogRestored();
                return committed;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                if (replacementSession is not null) await replacementSession.DisposeAsync().ConfigureAwait(false);
                try { await admission.ReleaseForClosedReplacementAsync(CancellationToken.None).ConfigureAwait(false); }
                catch (CatalogLocationAdmissionException refusal)
                {
                    return new(false, $"Restore activation was refused. Preserved replacement and displaced Catalog artifacts; no rollback through revoked storage. {refusal.Message}");
                }
                var rollback = await installation.Transaction!.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                if (!rollback.Succeeded)
                    return new(false, $"The restored Catalog could not be activated: {exception.Message} {rollback.Diagnostic}");
                var reactivation = await TryReactivateCatalogAsync().ConfigureAwait(false);
                if (!reactivation.Succeeded)
                    return new(false, $"The restored Catalog could not be activated: {exception.Message} {rollback.Diagnostic} The previous Catalog could not be reactivated: {reactivation.Diagnostic}");
                return new(false, $"The restored Catalog could not be activated, so the previous Catalog was restored and reactivated. {exception.Message}");
            }
        }
        finally
        {
            try
            {
                DerivedWork = CreateDerivedWorkScheduler();
                await RecreateMediaMonitoringAsync().ConfigureAwait(false);
            }
            finally { _mutationGate.Release(); }
        }
    }, cancellationToken);
    }

    private async Task<CatalogRestoreResult> TryReactivateCatalogAsync(bool activate = true)
    {
        using var admission = new CatalogLocationAdmission(Locations, StorageOperation.Open, _assessor);
        try
        {
            await admission.ValidateAsync(CancellationToken.None).ConfigureAwait(false);
            await admission.ValidateAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (CatalogLocationAdmissionException exception) { return new(false, exception.Message); }
        var boundLocations = admission.LocationsForUse();
        var migrationRecovery = _recovery is SqliteCatalogRecoveryService migrationNative
            ? migrationNative.ForLocations(boundLocations) : _recovery;
        var opened = await new CatalogDatabaseService(Locations, migrationRecovery)
            { ValidateStorageAccess = (token, phase) => admission.ValidateClosedDatabaseBoundary(token, phase),
                ResolvedDatabasePath = boundLocations.CatalogDatabasePath }
            .OpenExistingAsync(CancellationToken.None).ConfigureAwait(false);
        if (!opened.IsSuccess) return new(false, opened.Diagnostic ?? "The Catalog could not be reopened.");
        try
        {
            opened.Session!.CloseBeforeActivation();
            await admission.ValidateAsync(CancellationToken.None, "Reactivation_AfterInitialSQLiteUse").ConfigureAwait(false);
            _catalogSession = activate ? _activator.Activate(opened.Session!) : opened.Session!;
            _catalogSession.MarkPublished();
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
        return await Mutations.RunAsync<StorageChangeResult>(async () => {
        await _mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await DisposeMediaMonitoringAsync().ConfigureAwait(false);
            await DisposeDerivedWorkSchedulerAsync().ConfigureAwait(false);
            return await RelocateCatalogCoreAsync(destinationDirectory, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            try
            {
                DerivedWork = CreateDerivedWorkScheduler();
                await RecreateMediaMonitoringAsync().ConfigureAwait(false);
            }
            finally { _mutationGate.Release(); }
        }
    }, cancellationToken);
    }

    private async Task<StorageChangeResult> RelocateCatalogCoreAsync(string destinationDirectory,
        CancellationToken cancellationToken)
    {
        using var sourceAdmission = new CatalogLocationAdmission(Locations, StorageOperation.Open, _assessor);
        LightflowStorageLocations destination;
        LightflowStorageLocations boundDestination;
        CatalogLocationAdmission? destinationAdmission = null;
        try
        {
            destination = ValidateDestination(destinationDirectory, catalog: true);
            if (SamePath(Locations.CatalogDirectory, destination.CatalogDirectory))
                return new(StorageChangeStatus.EquivalentLocation, "That is already the active Catalog location.");
            destinationAdmission = new CatalogLocationAdmission(destination, StorageOperation.Relocate, _assessor);
            await destinationAdmission.ValidateAsync(cancellationToken).ConfigureAwait(false);
            await destinationAdmission.ValidateAsync(cancellationToken).ConfigureAwait(false);
            boundDestination = destinationAdmission.LocationsForUse();
            if (File.Exists(boundDestination.CatalogDatabasePath) || Directory.Exists(boundDestination.CatalogDatabasePath))
            {
                destinationAdmission.Dispose();
                return new(StorageChangeStatus.ConflictingCatalog, "A Catalog already exists at the selected location.");
            }
            ProbeWritable(boundDestination.CatalogDirectory);
            await destinationAdmission.ValidateAsync(cancellationToken).ConfigureAwait(false);
            if (Directory.EnumerateFileSystemEntries(boundDestination.CatalogDirectory).Any())
            {
                destinationAdmission.Dispose();
                return new(StorageChangeStatus.ConflictingCatalog,
                    "The selected Catalog folder must be empty so Lightflow cannot overwrite or mix with existing data.");
            }
            await sourceAdmission.ValidateAsync(cancellationToken).ConfigureAwait(false);
            await sourceAdmission.ValidateAsync(cancellationToken).ConfigureAwait(false);
            sourceAdmission.RequireActiveSessionBinding(_catalogSession);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException or OperationCanceledException)
        {
            destinationAdmission?.Dispose();
            return new(StorageChangeStatus.InvalidDestination, exception.Message);
        }

        using var admission = destinationAdmission;

        var sourceLocations = Locations;
        var sourceSettings = Settings;
        var expected = CatalogSession.Identity;
        var expectedSchema = CatalogSession.SchemaVersion;
        _catalogStartup?.Dispose(); _catalogStartup = null;
        await _catalogSession!.DisposeAsync().ConfigureAwait(false);
        _catalogSession = null;
        var staged = boundDestination.CatalogDatabasePath + $".{Guid.NewGuid():N}.moving";
        var destinationOwned = false;
        var configurationSwitched = false;
        CatalogDatabaseSession? destinationSession = null;
        try
        {
            Directory.CreateDirectory(boundDestination.CatalogDirectory);
            await admission!.ValidateAsync(cancellationToken).ConfigureAwait(false);
            await sourceAdmission.ValidateAsync(cancellationToken).ConfigureAwait(false);
            _transfer.Backup(sourceAdmission.LocationsForUse().CatalogDatabasePath, staged);
            await admission.ValidateAsync(cancellationToken).ConfigureAwait(false);
            File.Move(staged, boundDestination.CatalogDatabasePath);
            destinationOwned = true;
            await admission.ValidateAsync(cancellationToken).ConfigureAwait(false);
            var opened = await new CatalogDatabaseService(destination)
                { ValidateStorageAccess = (token, phase) => admission.ValidateClosedDatabaseBoundary(token, phase),
                    ResolvedDatabasePath = boundDestination.CatalogDatabasePath }
                .OpenExistingAsync(cancellationToken).ConfigureAwait(false);
            destinationSession = opened.Session;
            if (!opened.IsSuccess || !PortablePathIdentityValidator.ValidateCatalogIdentity(expected.CatalogId, opened.Session!.Identity.CatalogId).IsSafe ||
                opened.Session.SchemaVersion != expectedSchema)
            {
                throw new InvalidDataException(opened.Diagnostic ?? "The relocated Catalog failed identity or schema validation.");
            }

            var changed = sourceSettings with { CatalogDirectory = destination.CatalogDirectory, CatalogId = expected.CatalogId };
            destinationSession!.CloseBeforeActivation();
            await admission.ValidateAsync(cancellationToken, "RelocationActivation_AfterInitialSQLiteUse").ConfigureAwait(false);
            _configuration.Save(changed);
            configurationSwitched = true;
            destinationSession = _activator.Activate(destinationSession!);
            Settings = changed;
            Locations = destination;
            _recovery = new SqliteCatalogRecoveryService(destination);
            _catalogSession = destinationSession;
            destinationSession!.MarkPublished();
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
                    if (exception is CatalogLocationAdmissionException)
                    {
                        if (destinationSession is not null) await destinationSession.DisposeAsync().ConfigureAwait(false);
                        Locations = sourceLocations;
                        Settings = sourceSettings;
                        return new(StorageChangeStatus.Failed,
                            $"Storage admission failed and the prior configuration could not be restored. No Catalog writer was activated. The source Catalog remains at {sourceLocations.CatalogDirectory}. {exception.Message} {rollbackException.Message}");
                    }
                    _catalogSession = destinationSession;
                    destinationSession?.MarkPublished();
                    destinationSession = null;
                    Locations = destination;
                    Settings = sourceSettings with { CatalogDirectory = destination.CatalogDirectory, CatalogId = expected.CatalogId };
                    return new(StorageChangeStatus.SucceededWithWarning,
                        $"The Catalog was moved, but Lightflow could not restore the prior configuration after activation failed. The validated destination remains active. {rollbackException.Message}");
                }
            }
            if (destinationSession is not null) await destinationSession.DisposeAsync().ConfigureAwait(false);
            // A failed identity probe revokes authority to clean through that spelling. Leave
            // operation-owned artifacts for explicit recovery rather than deleting on a new mount.
            var cleanupSafe = false;
            try { await admission!.ReleaseForClosedReplacementAsync(CancellationToken.None).ConfigureAwait(false); cleanupSafe = true; }
            catch (CatalogLocationAdmissionException) { }
            if (cleanupSafe)
            {
                try { File.Delete(staged); } catch { }
                if (destinationOwned) DeleteCatalogFiles(boundDestination.CatalogDatabasePath);
            }
            Locations = sourceLocations;
            Settings = sourceSettings;
            var reactivation = await TryReactivateCatalogAsync(activate: false).ConfigureAwait(false);
            return new(StorageChangeStatus.Failed, $"The Catalog was not moved. {exception.Message}" +
                (reactivation.Succeeded ? "" : $" Source reopen failed: {reactivation.Diagnostic}"));
        }
    }

    public async Task<StorageChangeResult> RelocatePreviewsAsync(string destinationDirectory,
        PreviewRelocationMode mode, CancellationToken cancellationToken = default)
    {
        await _mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await DisposeDerivedWorkSchedulerAsync().ConfigureAwait(false);
            using var previewLease = await _previewOperations.EnterMaintenanceAsync(cancellationToken).ConfigureAwait(false);
            return await RelocatePreviewsCoreAsync(destinationDirectory, mode, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            try { DerivedWork = CreateDerivedWorkScheduler(); }
            finally { _mutationGate.Release(); }
        }
    }

    public async Task<PreviewUsage?> GetPreviewUsageAsync(CancellationToken cancellationToken = default)
    {
        // Usage is read-only accounting, not a write/flush that shutdown must finish.
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _readShutdown.Token);
        cancellationToken = lifetime.Token;
        await _mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var maintenance = CreatePreviewMaintenance();
            return maintenance is null ? null : await maintenance.GetUsageAsync(cancellationToken).ConfigureAwait(false);
        }
        finally { _mutationGate.Release(); }
    }

    public async Task<PreviewMaintenanceResult> CleanupPreviewsAsync(CancellationToken cancellationToken = default)
    {
        await _mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var maintenance = CreatePreviewMaintenance();
            return maintenance is null
                ? new(false, Diagnostic: PreviewDiagnostic ?? "The Preview store is unavailable.")
                : await maintenance.CleanupAsync(PreviewMaintenancePolicy.FromSettings(Settings), cancellationToken)
                    .ConfigureAwait(false);
        }
        finally { _mutationGate.Release(); }
    }

    public async Task<PreviewMaintenanceResult> ClearPreviewsAsync(CancellationToken cancellationToken = default)
    {
        await _mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var maintenance = CreatePreviewMaintenance();
            return maintenance is null
                ? new(false, Diagnostic: PreviewDiagnostic ?? "The Preview store is unavailable.")
                : await maintenance.ClearAsync(cancellationToken).ConfigureAwait(false);
        }
        finally { _mutationGate.Release(); }
    }

    public async Task<PreviewRebuildResult> RebuildPreviewsAsync(IProgress<PreviewRebuildProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        await _mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!CatalogAvailable) return new(false, 0, 0, 0, "The Catalog is unavailable.");
            using var maintenance = CreatePreviewMaintenance();
            return maintenance is null
                ? new(false, 0, 0, 0, PreviewDiagnostic ?? "The Preview store is unavailable.")
                : await maintenance.RebuildAsync(progress, cancellationToken).ConfigureAwait(false);
        }
        finally { _mutationGate.Release(); }
    }

    public async Task<IReadOnlyList<ThumbnailGenerationResult>> RegenerateThumbnailsAsync(
        IReadOnlyList<Guid> assetIds, CancellationToken cancellationToken = default,
        IProgress<PreviewRegenerationCompleted>? progress = null,
        PreviewRegenerationMode mode = PreviewRegenerationMode.Force)
    {
        if (!CatalogAvailable || Previews is null)
            return assetIds.Select(_ => new ThumbnailGenerationResult(ThumbnailGenerationStatus.Failed,
                Diagnostic: PreviewDiagnostic ?? "Preview storage is unavailable.")).ToArray();
        using var thumbnails = ThumbnailGenerationFactory.Create(MediaAssets, Previews, Locations, Settings,
            null, 2, _previewOperations, AssetColors, LutCache, ThumbnailActivity, PreferredPreviewFrames);
        return await PreviewRegenerationBatch.RunAsync(assetIds, async (assetId, token) =>
        {
            using var assetLease = await _assetPreviewGenerationGate.EnterAsync(assetId, token).ConfigureAwait(false);
            await _thumbnailRegenerationGate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                return await thumbnails.GenerateAsync(new(assetId, ForceRefresh: mode == PreviewRegenerationMode.Force,
                    Priority: ThumbnailPriority.Visible), token).ConfigureAwait(false);
            }
            finally { _thumbnailRegenerationGate.Release(); }
        }, progress, cancellationToken).ConfigureAwait(false);
    }

    internal IPositionFrameService CreatePositionFrameService() =>
        new PositionFrameService(MediaAssets, () => Locations, SubclipPosterFactory.CreateRenderer(Settings), _previewOperations, AssetColors, LutCache);

    internal IMarkerThumbnailService CreateMarkerThumbnailService() =>
        new MarkerThumbnailService(MediaAssets, () => Locations, SubclipPosterFactory.CreateRenderer(Settings), _previewOperations);

    internal ISubclipPosterService CreateSubclipPosterService() =>
        SubclipPosterFactory.Create(MediaAssets, () => Locations, Settings, operations: _previewOperations);

    private IPreviewMaintenanceService? CreatePreviewMaintenance()
    {
        if (Previews is null) return null;
        var metadata = DerivedMediaMetadataFactory.Create(MediaAssets, Previews, Settings,
            operations: _previewOperations);
        var thumbnails = ThumbnailGenerationFactory.Create(MediaAssets, Previews, Locations, Settings,
            null, 2, _previewOperations, AssetColors, LutCache, ThumbnailActivity, PreferredPreviewFrames);
        return new PreviewMaintenanceService(Previews, MediaAssets, metadata, thumbnails,
            _previewOperations, Locations, ownsGenerators: true);
    }

    private bool IsOwnedStoragePath(string path) => new OwnedStoragePaths(() => Locations).Contains(path);

    private IDerivedWorkScheduler? CreateDerivedWorkScheduler()
    {
        if (!CatalogAvailable || Previews is null) return null;
        var metadata = DerivedMediaMetadataFactory.Create(MediaAssets, Previews, Settings,
            operations: _previewOperations);
        var thumbnails = ThumbnailGenerationFactory.Create(MediaAssets, Previews, Locations, Settings,
            null, 2, _previewOperations, AssetColors, LutCache, ThumbnailActivity, PreferredPreviewFrames);
        return new DerivedWorkScheduler(MediaAssets, Previews, metadata, thumbnails,
            ownsGenerators: true, operations: _previewOperations, colors: AssetColors,
            artifactExists: relative => PreviewArtifactFiles.Exists(Locations.PreviewsDirectory, relative),
            preferredFrames: PreferredPreviewFrames, excludedPath: IsOwnedStoragePath);
    }

    private async Task DisposeDerivedWorkSchedulerAsync()
    {
        var scheduler = DerivedWork;
        DerivedWork = null;
        if (scheduler is not null) await scheduler.DisposeAsync().ConfigureAwait(false);
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
            _previewStartup?.Dispose(); _previewStartup = null;
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
            await Previews.InitializeAsync(cancellationToken).ConfigureAwait(false);
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
        return Locations.WithOverrides(catalog
                ? new(normalized, Locations.PreviewsDirectory)
                : new(Locations.CatalogDirectory, normalized));
    }

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

    public ValueTask DisposeAsync() => DisposeAsync(null);

    internal async ValueTask DisposeAsync(Action<string>? diagnostic)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        void Report(string stage) => diagnostic?.Invoke($"[Storage shutdown pid={Environment.ProcessId} elapsed={timer.Elapsed.TotalMilliseconds:F1}ms] {stage}");
        try
        {
            Report("Canceling read-only usage measurement");
            _readShutdown.Cancel();
            Report("Disposing LUT cache");
            if (LutCache is IDisposable disposableLutCache) disposableLutCache.Dispose();
            Report("Stopping media monitoring");
            await DisposeMediaMonitoringAsync().ConfigureAwait(false);
            Report("Stopping derived work");
            await DisposeDerivedWorkSchedulerAsync().ConfigureAwait(false);
            Report("Draining Catalog mutations");
            if (!_shutdownAdmissionClosed)
            {
                if (_exitQuiescence is not null) CompletePreparedExit();
                else
                {
                    var quiet = await Mutations.QuiesceAsync().ConfigureAwait(false);
                    quiet.CompleteShutdown(); _shutdownAdmissionClosed = true;
                }
            }
            Report("Waiting for storage operations");
            await _mutationGate.WaitAsync().ConfigureAwait(false);
            try
            {
                Report("Waiting for Preview operations");
                using var previewLease = await _previewOperations.EnterMaintenanceAsync().ConfigureAwait(false);
                var healthy = _cleanShutdownAllowed && _catalogSession is not null && Previews is not null;
                var resolvedCatalogPath = _catalogSession?.ResolvedDatabasePath;
                Report("Closing Catalog");
                if (_catalogSession is not null) await _catalogSession.DisposeAsync().ConfigureAwait(false);
                _catalogSession = null;
                Report("Closing Previews");
                if (Previews is not null) await Previews.DisposeAsync().ConfigureAwait(false);
                Previews = null;
                if (healthy)
                {
                    // Failure to attest is conservative: shutdown may finish, next launch validates.
                    try
                    {
                        _catalogStartup?.Complete(resolvedCatalogPath!);
                        _previewStartup?.Complete(Locations.PreviewsDatabasePath);
                        if (_catalogStartup?.Completed == true && _previewStartup?.Completed == true)
                        {
                            _startupCompletion?.Complete();
                            Report("Durable clean storage completion recorded");
                        }
                    }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException or SqliteException)
                    { Report($"Clean completion unavailable; next startup validates: {error.GetType().Name}: {error.Message}"); }
                }
                Report("Completed");
            }
            finally
            {
                _mutationGate.Release();
                _mutationGate.Dispose();
                _readShutdown.Dispose();
                Mutations.Dispose();
            }
        }
        finally
        {
            _catalogStartup?.Dispose(); _catalogStartup = null;
            _previewStartup?.Dispose(); _previewStartup = null;
            _startupCompletion?.Dispose(); _startupCompletion = null;
        }
    }

    private async Task DisposeMediaMonitoringAsync()
    {
        var monitoring = MediaMonitoring;
        MediaMonitoring = null;
        if (monitoring is not null) await monitoring.DisposeAsync().ConfigureAwait(false);
    }

    private async Task RecreateMediaMonitoringAsync()
    {
        var monitoring = new MediaRootMonitoringService(MediaRoots, MediaDiscovery, excludedPath: IsOwnedStoragePath);
        MediaMonitoring = monitoring;
        if (CatalogAvailable) await monitoring.StartAsync().ConfigureAwait(false);
    }
}
