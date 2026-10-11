using Lightflow.Application;
using Lightflow.Domain;
using System.IO;
using Microsoft.Data.Sqlite;

namespace LightflowStudio;

internal enum StorageStartupStatus { Ready, CatalogUnavailable, CatalogMissing, CatalogUnreadable, CatalogCorrupt, CatalogIdentityMismatch, InvalidConfiguration, FirstRunChoiceRequired }
internal sealed record StorageStartupResult(StorageStartupStatus Status, LightflowStorageCoordinator? Coordinator = null, string? Diagnostic = null,
    CatalogRecoveryReason RecoveryReason = CatalogRecoveryReason.None)
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
        SqliteCatalogRecoveryService.FinalizeRelocationCompatibility(sourceDatabasePath, destinationDatabasePath);
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

internal sealed partial class LightflowStorageCoordinator : IAsyncDisposable
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
        set
        {
            if (value is not null)
            {
                RequireCatalogOwnership(value.ResolvedDatabasePath);
                value.Mutations = Mutations;
                value.RequireOwnership = RequireCatalogOwnership;
                value.ReadinessFailed = LoseWorkspaceReadiness;
            }
            _session = value;
        }
    }
    private void RequireCatalogOwnership(string database)
    {
        if (_workspaceReadinessLost) throw new IOException("Catalog readiness is unavailable. Authoring is blocked.");
        try
        {
            if (_catalogStartup is null) throw new IOException("Catalog ownership is unavailable. Authoring is blocked.");
            _catalogStartup.RequireOwnership(database);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ObjectDisposedException or System.ComponentModel.Win32Exception)
        { LoseWorkspaceReadiness(error.Message); throw; }
    }

    private volatile bool _workspaceReadinessLost;
    internal event Action? WorkspaceReadinessLost;
    internal bool CatalogTransitionInProgress => _mutationGate.CurrentCount == 0;
    internal bool VerifyWorkspaceReadiness()
    {
        if (!CatalogAvailable) return false;
        try
        {
            RequireCatalogOwnership(_catalogSession!.ResolvedDatabasePath);
            if (!File.Exists(_catalogSession.ResolvedDatabasePath))
                throw new IOException("The active Catalog is unavailable. Its configured location has been retained.");
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ObjectDisposedException or System.ComponentModel.Win32Exception)
        { LoseWorkspaceReadiness(error.Message); return false; }
    }
    private void LoseWorkspaceReadiness(string diagnostic)
    {
        if (_workspaceReadinessLost) return;
        _workspaceReadinessLost = true;
        PreventCleanShutdown(); RecoveryDiagnostic = diagnostic;
        Mutations.Dispose();
        _shutdownAdmissionClosed = true;
        WorkspaceReadinessLost?.Invoke();
    }

    private LightflowStorageCoordinator(IStorageConfigurationStore configuration, AppSettings settings,
        LightflowStorageLocations locations, CatalogDatabaseSession? session, ICatalogRelocationTransfer transfer,
        ICatalogSessionActivator activator, ICatalogRecoveryService recovery, IPreviewStoreService? previews,
        string? previewDiagnostic, StartupStoreEvidence? catalogStartup = null, StartupStoreEvidence? previewStartup = null, StartupSessionCompletion? startupCompletion = null,
        IStorageLocationAssessor? assessor = null)
    {
        _catalogStartup = catalogStartup; _previewStartup = previewStartup; _startupCompletion = startupCompletion;
        if (session is not null) _catalogStartup!.MarkValidated();
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
    public bool CatalogAvailable => _catalogSession is not null && !_workspaceReadinessLost;
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

    private static async Task<StorageStartupResult> StartCoreAsync(string? localApplicationData = null,
        CancellationToken cancellationToken = default, ICatalogRelocationTransfer? transfer = null,
        IStorageConfigurationStore? configuration = null, ICatalogSessionActivator? activator = null,
        ICatalogRecoveryService? recovery = null, LightflowStorageLocations? profile = null,
        InitializedDataProfile? initializedProfile = null, IStorageLocationAssessor? assessor = null,
        bool allowFirstCreation = true, AppSettings? selectedSettings = null, bool explicitCreation = false, string? reservedCatalogDirectory = null)
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
        settings = selectedSettings ?? settings;
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
        var transferRecovery = ClosedCatalogTransferJournal.InspectPending(locations);
        if (transferRecovery is not null)
        {
            var (availablePreviews, diagnostic) = await OpenPreviewsAsync(settings, locations, cancellationToken).ConfigureAwait(false);
            return new(StorageStartupStatus.CatalogUnavailable,
                new LightflowStorageCoordinator(configuration, settings, locations, null, transfer, activator,
                    recovery, availablePreviews, diagnostic, assessor: assessor), transferRecovery, CatalogRecoveryReason.PendingRecovery);
        }
        if (!allowFirstCreation && settings.CatalogId is null && settings.CatalogDirectory is null && !File.Exists(locations.CatalogDatabasePath))
        {
            var (availablePreviews, diagnostic) = await OpenPreviewsAsync(settings, locations, cancellationToken).ConfigureAwait(false);
            return new(StorageStartupStatus.FirstRunChoiceRequired,
                new LightflowStorageCoordinator(configuration, settings, locations, null, transfer, activator,
                    recovery, availablePreviews, diagnostic, assessor: assessor),
                "Create a new Catalog or open an existing Catalog. No Catalog has been created.");
        }
        var priorCatalogReservation = settings.CatalogBackupDirectory is not null;
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
        var create = explicitCreation || !File.Exists(locations.CatalogDatabasePath) && settings.CatalogId is null && settings.CatalogDirectory is null;
        if (create && !explicitCreation && (priorCatalogReservation || !CanCreateAt(locations.CatalogDirectory, defaults, settings)))
        {
            var (availablePreviews, diagnostic) = await OpenPreviewsAsync(settings, locations, cancellationToken).ConfigureAwait(false);
            return new(StorageStartupStatus.CatalogUnavailable,
                new LightflowStorageCoordinator(configuration, settings, locations, null, transfer, activator,
                    recovery, availablePreviews, diagnostic, assessor: assessor),
                "The default Catalog location contains existing or reserved files. Choose a safely empty or new Catalog directory.", CatalogRecoveryReason.LocationRefused);
        }
        var presentationReason = CatalogRecoveryReason.LocationUnavailable;
        using var admission = new CatalogLocationAdmission(locations, create ? StorageOperation.Create : StorageOperation.Open, assessor, reservedCatalogDirectory);
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
                        recovery, availablePreviews, diagnostic, assessor: assessor), exception.Message,
                    exception.Reason == StorageLocationReason.LocationUnavailable ? (create ? CatalogRecoveryReason.InitialLocationUnavailable : CatalogRecoveryReason.LocationUnavailable)
                        : exception.Reason is StorageLocationReason.NetworkActiveCatalog or StorageLocationReason.UnsupportedCatalogFileSystem or StorageLocationReason.UnsupportedCatalogCapabilities
                            ? CatalogRecoveryReason.UnsupportedStorage : CatalogRecoveryReason.LocationRefused);
            }
            var boundLocations = admission.LocationsForUse();
            startupCompletion = new StartupSessionCompletion(locations.ApplicationDataDirectory);
            try
            {
                catalogStartup = StartupStoreEvidence.Begin(locations.CatalogDatabasePath, "Catalog", create, startupCompletion,
                    boundLocations.CatalogDatabasePath) ?? throw new DirectoryNotFoundException("The Catalog directory is unavailable.");
            }
            catch (IOException exception)
            {
                presentationReason = exception is DirectoryNotFoundException
                    ? (create ? CatalogRecoveryReason.InitialLocationUnavailable : CatalogRecoveryReason.LocationUnavailable)
                    : exception.InnerException is System.ComponentModel.Win32Exception { NativeErrorCode: 32 }
                        ? CatalogRecoveryReason.CompetingOwner : CatalogRecoveryReason.OwnershipUnavailable;
                throw new CatalogLocationAdmissionException((presentationReason == CatalogRecoveryReason.CompetingOwner
                    ? "Another Lightflow Studio session is using this Catalog. "
                    : presentationReason is CatalogRecoveryReason.LocationUnavailable or CatalogRecoveryReason.InitialLocationUnavailable
                        ? "" : "The Catalog could not be opened exclusively. ") + exception.Message,
                    StorageLocationReason.AssessmentFailed);
            }
            Guid? transferredIdentity;
            try { transferredIdentity = create ? null : VerifySelectedTransfer(boundLocations.CatalogDirectory, catalogStartup.HadValidatedCatalog); }
            catch (Exception refusal) when (refusal is IOException or InvalidDataException or System.Text.Json.JsonException)
            {
                presentationReason = CatalogRecoveryReason.ValidationFailed;
                throw new CatalogLocationAdmissionException("Catalog transfer verification refused opening. Existing data and evidence are retained. " + refusal.Message,
                    StorageLocationReason.AssessmentFailed);
            }
            previewStartup = StartupStoreEvidence.Begin(locations.PreviewsDatabasePath, "Previews", settings.PreviewsDirectory is null, startupCompletion);
            var cleanPair = catalogStartup?.KnownClean == true && previewStartup?.KnownClean == true;
            catalogStartup?.RequireCompletePeer(cleanPair); previewStartup?.RequireCompletePeer(cleanPair);
            StartupDiagnostics.Note($"Catalog startup decision: {(catalogStartup?.KnownClean == true ? "fast candidate" : "deep")} reason={catalogStartup?.Reason ?? StartupValidationReason.UnexpectedShutdown}");
            StartupDiagnostics.Note($"Previews startup decision: {(previewStartup?.KnownClean == true ? "fast candidate" : "deep")} reason={previewStartup?.Reason ?? StartupValidationReason.UnexpectedShutdown}");
            var boundRecovery = recovery is SqliteCatalogRecoveryService nativeRecovery ? nativeRecovery.ForLocations(boundLocations) : recovery;
            var database = new CatalogDatabaseService(locations, boundRecovery)
            { CleanStartup = catalogStartup?.KnownClean == true, ValidationReason = catalogStartup?.Reason ?? StartupValidationReason.UnexpectedShutdown,
                ExpectedCatalogId = selectedSettings?.CatalogId ?? transferredIdentity,
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

            if (selectedSettings is not null)
            {
                if (explicitCreation) settings = settings with { CatalogId = opened.Session!.Identity.CatalogId };
                catalogStartup!.RequireOwnership(opened.Session!.ResolvedDatabasePath);
                var originalSession = opened.Session;
                var activated = activator.Activate(originalSession);
                if (!ReferenceEquals(activated, originalSession))
                {
                    if (activated is not null) await activated.DisposeAsync().ConfigureAwait(false);
                    throw new IOException("Catalog activation did not retain the selected session owner.");
                }
                var ownedRecovery = new SqliteCatalogRecoveryService(boundLocations);
                var full = await Task.Run(() => ownedRecovery.InspectOwnedCatalog(boundLocations.CatalogDatabasePath, cancellationToken), cancellationToken).ConfigureAwait(false);
                if (!full.IsValid) throw new InvalidDataException(full.Diagnostic);
                SqliteCatalogRecoveryService.ReadExternalLutDependencies(boundLocations.CatalogDatabasePath);
                configuration.Save(settings);
                if (!configuration.TryLoad(out var persisted, out var diagnostic) || persisted.CatalogId != settings.CatalogId || persisted.CatalogDirectory != settings.CatalogDirectory)
                    throw new IOException(diagnostic ?? "Selected Catalog configuration commit could not be verified.");
            }
            else if (settings.CatalogId is null)
            {
                settings = settings with { CatalogId = opened.Session!.Identity.CatalogId };
                try { configuration.Save(settings); }
                catch
                {
                    await opened.Session.DisposeAsync().ConfigureAwait(false);
                    throw;
                }
            }
            if (needsBackupConfiguration && selectedSettings is null)
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
                    recovery, availablePreviews, diagnostic, assessor: assessor), exception.Message, presentationReason);
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
            // Keep an owned SQLite scope checked out through native source revalidation.
            // Pool retirement must not close the last connection and delete WAL sidecars
            // between the assessor's attributes and native identity observations.
            using var sourceScope = _catalogSession.OpenConnection();
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
        var transferRecovery = ClosedCatalogTransferJournal.InspectPending(Locations);
        if (transferRecovery is not null) return new(false, transferRecovery);
        if (_recovery is SqliteCatalogRecoveryService transportRecovery)
        {
            try { backupPath = await transportRecovery.PrepareRecoveryInputAsync(backupPath, cancellationToken).ConfigureAwait(false); }
            catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException)
            { return new(false, $"The backup could not be verified and copied back locally. {e.Message}"); }
        }
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
            try { EnsureCatalogRecoveryOwnership(boundLocations.CatalogDatabasePath); }
            catch (Exception refusal) when (refusal is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
            { return new(false, "Catalog recovery ownership could not be acquired. Close another writer, reconnect the configured storage, and retry. " + refusal.Message); }
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
            catch (CatalogLocationAdmissionException exception)
            { return await BlockCatalogRecoveryAsync(exception.Message).ConfigureAwait(false); }
            if (!installation.Succeeded)
            {
                var reactivation = await TryReactivateCatalogAsync().ConfigureAwait(false);
                var diagnostic = installation.Diagnostic ?? "The Catalog could not be restored.";
                if (!reactivation.Succeeded)
                    return await BlockCatalogRecoveryAsync(diagnostic + $" The previous Catalog could not be reactivated: {reactivation.Diagnostic}").ConfigureAwait(false);
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
                var activated = _activator.Activate(replacementSession!);
                if (!ReferenceEquals(activated, replacementSession))
                {
                    if (activated is not null) await activated.DisposeAsync().ConfigureAwait(false);
                    throw new IOException("Restore activation did not retain its owned Catalog session.");
                }
                var committed = await installation.Transaction!.CommitAsync(CancellationToken.None).ConfigureAwait(false);
                if (!committed.Succeeded) throw new IOException(committed.Diagnostic);
                _catalogStartup!.MarkValidated();
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
                    return await BlockCatalogRecoveryAsync($"Restore activation was refused. Preserved replacement and displaced Catalog artifacts; no rollback through revoked storage. {refusal.Message}").ConfigureAwait(false);
                }
                var rollback = await installation.Transaction!.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                if (!rollback.Succeeded)
                    return await BlockCatalogRecoveryAsync($"The restored Catalog could not be activated: {exception.Message} {rollback.Diagnostic}").ConfigureAwait(false);
                var reactivation = await TryReactivateCatalogAsync().ConfigureAwait(false);
                if (!reactivation.Succeeded)
                    return await BlockCatalogRecoveryAsync($"The restored Catalog could not be activated: {exception.Message} {rollback.Diagnostic} The previous Catalog could not be reactivated: {reactivation.Diagnostic}").ConfigureAwait(false);
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
        RequireCatalogOwnership(boundLocations.CatalogDatabasePath);
        var migrationRecovery = _recovery is SqliteCatalogRecoveryService migrationNative
            ? migrationNative.ForLocations(boundLocations) : _recovery;
        var opened = await new CatalogDatabaseService(Locations, migrationRecovery)
            { ExpectedCatalogId = Settings.CatalogId, ValidateStorageAccess = (token, phase) => admission.ValidateClosedDatabaseBoundary(token, phase),
                ResolvedDatabasePath = boundLocations.CatalogDatabasePath }
            .OpenExistingAsync(CancellationToken.None).ConfigureAwait(false);
        if (!opened.IsSuccess) return new(false, opened.Diagnostic ?? "The Catalog could not be reopened.");
        try
        {
            if (Settings.CatalogId is Guid expected && opened.Session!.Identity.CatalogId != expected)
                throw new InvalidDataException("The previous Catalog identity changed. Ownership rollback cannot be proven.");
            opened.Session!.CloseBeforeActivation();
            await admission.ValidateAsync(CancellationToken.None, "Reactivation_AfterInitialSQLiteUse").ConfigureAwait(false);
            var activated = activate ? _activator.Activate(opened.Session!) : opened.Session!;
            if (!ReferenceEquals(activated, opened.Session))
            {
                if (activated is not null) await activated.DisposeAsync().ConfigureAwait(false);
                throw new IOException("Catalog reactivation did not retain its owned source session.");
            }
            _catalogStartup!.MarkValidated();
            _catalogSession = opened.Session;
            _catalogSession.MarkPublished();
            return new(true);
        }
        catch (Exception exception)
        {
            await opened.Session!.DisposeAsync().ConfigureAwait(false);
            return new(false, exception.Message);
        }
    }

    private void EnsureCatalogRecoveryOwnership(string resolvedDatabase)
    {
        if (_catalogStartup is not null) { RequireCatalogOwnership(resolvedDatabase); return; }
        StartupSessionCompletion? completion = null;
        StartupStoreEvidence? lease = null;
        try
        {
            completion = _startupCompletion is null ? new StartupSessionCompletion(Locations.ApplicationDataDirectory) : null;
            lease = StartupStoreEvidence.Begin(Locations.CatalogDatabasePath, "Catalog", false,
                _startupCompletion ?? completion, resolvedDatabase) ?? throw new IOException("The configured Catalog directory is unavailable.");
            lease.RequireOwnership(resolvedDatabase);
            _catalogStartup = lease; lease = null;
            _startupCompletion ??= completion; completion = null;
        }
        finally { lease?.Dispose(); completion?.Dispose(); }
    }

    private async Task<CatalogRestoreResult> BlockCatalogRecoveryAsync(string diagnostic)
    {
        PreventCleanShutdown();
        RecoveryDiagnostic = "Recovery required. All Catalog authoring is blocked; artifacts are retained. " + diagnostic;
        if (_catalogSession is not null) { await _catalogSession.DisposeAsync().ConfigureAwait(false); _catalogSession = null; }
        _catalogStartup?.Dispose(); _catalogStartup = null;
        Mutations.Dispose(); _shutdownAdmissionClosed = true;
        return new(false, RecoveryDiagnostic);
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
