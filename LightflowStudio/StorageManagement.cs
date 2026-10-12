using Lightflow.Application;
using Lightflow.Domain;
using System.IO;
using Microsoft.Data.Sqlite;

namespace LightflowStudio;

internal sealed record StorageStartupResult(StorageStartupStatus Status, LightflowStorageCoordinator? Coordinator = null, string? Diagnostic = null,
    CatalogRecoveryReason RecoveryReason = CatalogRecoveryReason.None)
{
    public bool IsReady => Status == StorageStartupStatus.Ready;
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
    private readonly CatalogLifecycle _lifecycle;
    internal CatalogLifecycle Lifecycle => _lifecycle;
    private StartupSessionCompletion? _startupCompletion { get => _lifecycle.ProfileCompletion; set => _lifecycle.ProfileCompletion = value; }
    private StartupStoreEvidence? _catalogStartup { get => _lifecycle.CatalogEvidence; set => _lifecycle.CatalogEvidence = value; }
    private StartupStoreEvidence? _previewStartup;
    private bool _shutdownAdmissionClosed { get => _lifecycle.AdmissionClosed; set => _lifecycle.AdmissionClosed = value; }
    internal void PreventCleanShutdown() => _lifecycle.PreventCleanShutdown();
    private readonly IStorageConfigurationStore _configuration;
    private readonly ICatalogRelocationTransfer _transfer;
    private readonly ICatalogSessionActivator _activator;
    private readonly IStorageLocationAssessor? _assessor;
    private ICatalogRecoveryService _recovery { get => _lifecycle.Recovery; set => _lifecycle.Recovery = value; }
    private SemaphoreSlim _mutationGate => _lifecycle.TransitionGate;
    private readonly CancellationTokenSource _readShutdown = new();
    private readonly PreviewOperationCoordinator _previewOperations = new();
    private readonly SemaphoreSlim _thumbnailRegenerationGate = new(2, 2);
    private readonly AssetPreviewGenerationGate _assetPreviewGenerationGate = new();
    internal CatalogMutationLifecycle Mutations => _lifecycle.Mutations;
    private CatalogDatabaseSession? _catalogSession { get => _lifecycle.Session; set => _lifecycle.Session = value; }
    private void RequireCatalogOwnership(string database) => _lifecycle.RequireCatalogOwnership(database);
    internal event Action? WorkspaceReadinessLost { add => _lifecycle.WorkspaceReadinessLost += value; remove => _lifecycle.WorkspaceReadinessLost -= value; }
    internal bool CatalogTransitionInProgress => _lifecycle.CatalogTransitionInProgress;
    internal bool VerifyWorkspaceReadiness() => _lifecycle.VerifyWorkspaceReadiness();
    private void LoseWorkspaceReadiness(string diagnostic) => _lifecycle.LoseWorkspaceReadiness(diagnostic);

    private LightflowStorageCoordinator(IStorageConfigurationStore configuration, AppSettings settings,
        LightflowStorageLocations locations, CatalogDatabaseSession? session, ICatalogRelocationTransfer transfer,
        ICatalogSessionActivator activator, ICatalogRecoveryService recovery, IPreviewStoreService? previews,
        string? previewDiagnostic, StartupStoreEvidence? catalogStartup = null, StartupStoreEvidence? previewStartup = null, StartupSessionCompletion? startupCompletion = null,
        IStorageLocationAssessor? assessor = null, CatalogLifecycle? lifecycle = null)
    {
        _lifecycle = lifecycle ?? throw new InvalidOperationException("A shared Catalog lifecycle is required.");
        _previewStartup = previewStartup;
        _configuration = configuration;
        Settings = settings;
        Locations = locations;
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

    public AppSettings Settings { get => ((WindowsCatalogConfigurationSnapshot)_lifecycle.Settings).Value; private set => _lifecycle.Settings = new WindowsCatalogConfigurationSnapshot(value); }
    public LightflowStorageLocations Locations { get => (LightflowStorageLocations)_lifecycle.Locations; private set => _lifecycle.Locations = value; }
    public CatalogDatabaseSession CatalogSession => _catalogSession ?? throw new InvalidOperationException("The Catalog is not open.");
    public bool CatalogAvailable => _lifecycle.CatalogAvailable;
    public bool PreviewAvailable { get; private set; }
    public string? PreviewDiagnostic { get; private set; }
    public string? RecoveryDiagnostic { get => _lifecycle.RecoveryDiagnostic; private set => _lifecycle.SetRecoveryDiagnostic(value); }
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
    private CatalogMutationLifecycle.Quiescence? _exitQuiescence { get => _lifecycle.ExitQuiescence; set => _lifecycle.ExitQuiescence = value; }

    internal Task<CatalogBackupResult> BackupForExitAsync(string destination, bool keepQuiescent,
        IProgress<string>? progress = null, CancellationToken cancellationToken = default) => _lifecycle.BackupForExitAsync(destination, keepQuiescent, progress, cancellationToken);

    internal void CancelPreparedExit() => _lifecycle.CancelPreparedExit();
    internal void CompletePreparedExit() => _lifecycle.CompletePreparedExit();

    internal Task SaveBackupDestinationAsync(string destination, CancellationToken token) => _lifecycle.SaveBackupDestinationAsync(destination, token);

    public Task<CatalogRestoreResult> RestoreCatalogAsync(string backupPath, CancellationToken cancellationToken = default) => _lifecycle.RestoreCatalogAsync(backupPath, cancellationToken);




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

    public ValueTask DisposeAsync() => _lifecycle.DisposeAsync();
    internal ValueTask DisposeAsync(Action<string>? diagnostic) => _lifecycle.DisposeAsync(diagnostic);

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
