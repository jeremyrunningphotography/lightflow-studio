using System.IO;
using System.Text.Json;
using Lightflow.Application;
using Lightflow.Domain;
namespace LightflowStudio;
public sealed partial class CatalogLifecycle
{
    private ICatalogLifecycleEffects _effects = null!;
    private ICatalogConfigurationEffects _configuration => _effects.Configuration;
    private ICatalogRelocationTransfer? _transfer => _effects.RelocationTransfer;
    private CatalogTransferFiles _files => new(_effects.TransferNative);
    private ICatalogRecoveryService _recovery = null!;
    private CatalogDatabaseSession? _catalogSession { get => Session; set => Session = value; }
    public ICatalogConfigurationSnapshot Settings { get; set; } = null!;
    public ILightflowStorageLocations Locations { get; set; } = null!;
    public ICatalogRecoveryService Recovery { get => _recovery; set => _recovery = value; }
    public CatalogDatabaseSession CatalogSession => Session ?? throw new InvalidOperationException("The Catalog is not open.");
    public CatalogLifecycle(ICatalogLifecycleEffects effects) : this(effects.Consumers) => _effects = effects;
    private bool SamePath(string left, string right) => _effects.SamePath(left, right);
    private Task<(ICatalogCompletionPeerStore? Store, string? Diagnostic)> OpenPreviewsAsync(
        ICatalogConfigurationSnapshot settings, ILightflowStorageLocations locations, CancellationToken token, StartupStoreEvidence? evidence = null)
        => _effects.SupportsCompletionPeer ? _effects.OpenCompletionPeerAsync(settings, locations, token, evidence)
        : Task.FromResult<(ICatalogCompletionPeerStore?, string?)>((null, null));
    private CatalogLifecycle CreateRoot(ICatalogConfigurationSnapshot settings, ILightflowStorageLocations locations,
        CatalogDatabaseSession? session, ICatalogRecoveryService recovery, ICatalogCompletionPeerStore? peer, string? diagnostic,
        StartupStoreEvidence? evidence = null, StartupStoreEvidence? peerEvidence = null, StartupSessionCompletion? completion = null)
    {
        Settings = settings; Locations = locations; _recovery = recovery;
        CatalogEvidence = evidence; ProfileCompletion = completion;
        if (session is not null) evidence!.MarkValidated();
        Session = session; session?.MarkPublished();
        _effects.ComposeConsumers(this, peer, diagnostic, peerEvidence);
        return this;
    }
    private ILightflowStorageLocations ValidateCatalogDestination(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        if (!Path.IsPathFullyQualified(directory)) throw new ArgumentException("Choose an absolute folder path.");
        return _effects.WithOverrides(Locations, Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)), Locations.PreviewsDirectory);
    }
    private Task<CatalogStartupResult> StartCoreAsync(CancellationToken cancellationToken = default,
        bool allowFirstCreation = true, ICatalogConfigurationSnapshot? selectedSettings = null,
        bool explicitCreation = false, string? reservedCatalogDirectory = null)
        => new CatalogLifecycle(_effects).StartAttemptAsync(cancellationToken, allowFirstCreation, selectedSettings,
            explicitCreation, reservedCatalogDirectory);
    private async Task<CatalogStartupResult> StartAttemptAsync(CancellationToken cancellationToken = default,
        bool allowFirstCreation = true, ICatalogConfigurationSnapshot? selectedSettings = null,
        bool explicitCreation = false, string? reservedCatalogDirectory = null)
    {
        using var timing = StartupDiagnostics.Stage("Storage initialization", "Opening storage…");
        var defaults = _effects.Profile;
        _effects.RequireInitializedProfile();
        var configuration = _configuration;
        if (!configuration.TryLoad(out var settings, out var settingsDiagnostic))
            return new(StorageStartupStatus.InvalidConfiguration, Diagnostic: settingsDiagnostic);
        settings = selectedSettings ?? settings;
        if (defaults.IsIsolated && !File.Exists(defaults.SettingsPath))
            settings = configuration.InitializeIsolatedDefaults(settings, defaults.ApplicationDataDirectory);
        ILightflowStorageLocations locations;
        try
        {
            locations = _effects.WithOverrides(defaults, settings.CatalogDirectory, settings.PreviewsDirectory);
            _effects.GuardConfiguredLeaves(defaults, locations);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or IOException or UnauthorizedAccessException)
        {
            return new(StorageStartupStatus.InvalidConfiguration, Diagnostic: exception.Message, RecoveryReason: CatalogRecoveryReason.LocationRefused);
        }

        var recovery = _effects.RecoveryOverride ?? new CatalogRecoveryService(_effects.RecoveryNative, locations);
        var transferRecovery = CatalogTransferJournal.InspectPending(_effects.TransferNative, locations);
        if (transferRecovery is not null)
        {
            var (availablePreviews, diagnostic) = await OpenPreviewsAsync(settings, locations, cancellationToken).ConfigureAwait(false);
            return new(StorageStartupStatus.CatalogUnavailable,
                CreateRoot(settings, locations, null, recovery, availablePreviews, diagnostic), transferRecovery, CatalogRecoveryReason.PendingRecovery);
        }
        if (!allowFirstCreation && settings.CatalogId is null && settings.CatalogDirectory is null && !File.Exists(locations.CatalogDatabasePath))
        {
            var (availablePreviews, diagnostic) = await OpenPreviewsAsync(settings, locations, cancellationToken).ConfigureAwait(false);
            return new(StorageStartupStatus.FirstRunChoiceRequired,
                CreateRoot(settings, locations, null, recovery, availablePreviews, diagnostic),
                "Create a new Catalog or open an existing Catalog. No Catalog has been created.");
        }
        var priorCatalogReservation = settings.CatalogBackupDirectory is not null;
        var needsBackupConfiguration = settings.CatalogBackupDirectory is null;
        if (settings.CatalogBackupDirectory is null)
        {
            // Deterministic new/existing-profile migration. Preserve all managed recovery copies.
            settings = configuration.Stage(settings, new(SetBackupDirectory: true, CatalogBackupDirectory: CatalogBackupDestinationPolicy.Default(locations)));
        }
        StartupStoreEvidence? catalogStartup = null, previewStartup = null;
        StartupSessionCompletion? startupCompletion = null;
        CatalogDatabaseSession? pendingSession = null;
        ICatalogCompletionPeerStore? pendingPreviews = null;
        CatalogLifecycle? pendingCoordinator = null;
        var create = explicitCreation || !File.Exists(locations.CatalogDatabasePath) && settings.CatalogId is null && settings.CatalogDirectory is null;
        if (create && !explicitCreation && (priorCatalogReservation || !CanCreateAt(locations.CatalogDirectory, defaults, settings)))
        {
            var (availablePreviews, diagnostic) = await OpenPreviewsAsync(settings, locations, cancellationToken).ConfigureAwait(false);
            return new(StorageStartupStatus.CatalogUnavailable,
                CreateRoot(settings, locations, null, recovery, availablePreviews, diagnostic),
                "The default Catalog location contains existing or reserved files. Choose a safely empty or new Catalog directory.", CatalogRecoveryReason.LocationRefused);
        }
        var presentationReason = CatalogRecoveryReason.LocationUnavailable;
        using var admission = _effects.CreateAdmission(locations, create ? StorageOperation.Create : StorageOperation.Open, reservedCatalogDirectory);
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
                    CreateRoot(settings, locations, null, recovery, availablePreviews, diagnostic), exception.Message,
                    exception.Reason == StorageLocationReason.LocationUnavailable ? (create ? CatalogRecoveryReason.InitialLocationUnavailable : CatalogRecoveryReason.LocationUnavailable)
                        : exception.Reason is StorageLocationReason.NetworkActiveCatalog or StorageLocationReason.UnsupportedCatalogFileSystem or StorageLocationReason.UnsupportedCatalogCapabilities
                            ? CatalogRecoveryReason.UnsupportedStorage : CatalogRecoveryReason.LocationRefused);
            }
            var boundLocations = admission.LocationsForUse();
            startupCompletion = new StartupSessionCompletion(_effects.Evidence, locations.ApplicationDataDirectory);
            try
            {
                catalogStartup = StartupStoreEvidence.Begin(_effects.Evidence, locations.CatalogDatabasePath, "Catalog", create, startupCompletion,
                    boundLocations.CatalogDatabasePath) ?? throw new DirectoryNotFoundException("The Catalog folder is unavailable.");
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
            previewStartup = _effects.SupportsCompletionPeer ? _effects.BeginCompletionPeer(locations, settings, startupCompletion) : null;
            CatalogLifecycle.RequireCompletePeers(catalogStartup, previewStartup);
            StartupDiagnostics.Note($"Catalog startup decision: {(catalogStartup?.KnownClean == true ? "fast candidate" : "deep")} reason={catalogStartup?.Reason ?? StartupValidationReason.UnexpectedShutdown}");
            StartupDiagnostics.Note($"Previews startup decision: {(previewStartup?.KnownClean == true ? "fast candidate" : "deep")} reason={previewStartup?.Reason ?? StartupValidationReason.UnexpectedShutdown}");
            var boundRecovery = recovery is CatalogRecoveryService nativeRecovery ? nativeRecovery.ForLocations(boundLocations) : recovery;
            var opened = await CatalogLifecycle.OpenAdmittedAsync(locations, admission, catalogStartup!, previewStartup,
                create, selectedSettings?.CatalogId ?? transferredIdentity, boundRecovery, cancellationToken).ConfigureAwait(false);
            pendingSession = opened.Session;
            if (!opened.IsSuccess)
            {
                StartupDiagnostics.Note($"Catalog readiness: {opened.Status}; transition=existing recovery surface");
                var (unavailableCatalogPreviews, unavailableCatalogPreviewDiagnostic) =
                    await OpenPreviewsAsync(settings, locations, cancellationToken, previewStartup).ConfigureAwait(false);
                return new(Map(opened.Status),
                    CreateRoot(settings, locations, null, recovery, unavailableCatalogPreviews, unavailableCatalogPreviewDiagnostic, catalogStartup, previewStartup, startupCompletion), opened.Diagnostic);
            }
            if (settings.CatalogId is Guid expected && !PortablePathIdentityValidator.ValidateCatalogIdentity(expected, opened.Session!.Identity.CatalogId).IsSafe)
            {
                await opened.Session.DisposeAsync().ConfigureAwait(false);
                var (mismatchedCatalogPreviews, mismatchedCatalogPreviewDiagnostic) =
                    await OpenPreviewsAsync(settings, locations, cancellationToken, previewStartup).ConfigureAwait(false);
                return new(StorageStartupStatus.CatalogIdentityMismatch,
                    CreateRoot(settings, locations, null, recovery, mismatchedCatalogPreviews, mismatchedCatalogPreviewDiagnostic, catalogStartup, previewStartup, startupCompletion),
                    "The configured Catalog does not match the Catalog previously associated with this Lightflow installation.");
            }

            if (selectedSettings is not null)
            {
                if (explicitCreation) settings = configuration.Stage(settings, new(SetCatalogId: true, CatalogId: opened.Session!.Identity.CatalogId));
                catalogStartup!.RequireOwnership(opened.Session!.ResolvedDatabasePath);
                var originalSession = opened.Session;
                var activated = _effects.Activate(originalSession);
                if (!ReferenceEquals(activated, originalSession))
                {
                    if (activated is not null) await activated.DisposeAsync().ConfigureAwait(false);
                    throw new IOException("Catalog activation did not retain the selected session owner.");
                }
                var ownedRecovery = new CatalogRecoveryService(_effects.RecoveryNative, boundLocations);
                var full = await Task.Run(() => ownedRecovery.InspectOwnedCatalog(boundLocations.CatalogDatabasePath, cancellationToken), cancellationToken).ConfigureAwait(false);
                if (!full.IsValid) throw new InvalidDataException(full.Diagnostic);
                CatalogRecoveryService.ReadExternalLutDependencies(_effects.RecoveryNative, boundLocations.CatalogDatabasePath);
                configuration.Save(settings);
                if (!configuration.TryLoad(out var persisted, out var diagnostic) || persisted.CatalogId != settings.CatalogId || persisted.CatalogDirectory != settings.CatalogDirectory)
                    throw new IOException(diagnostic ?? "Selected Catalog configuration commit could not be verified.");
            }
            else if (settings.CatalogId is null)
            {
                settings = configuration.Stage(settings, new(SetCatalogId: true, CatalogId: opened.Session!.Identity.CatalogId));
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
            var coordinator = CreateRoot(settings, locations, opened.Session, recovery, previews, previewDiagnostic, catalogStartup, previewStartup, startupCompletion);
            pendingCoordinator = coordinator;
            using (StartupDiagnostics.Stage("Media-root monitoring", "Checking media locations…"))
                await _effects.StartMonitoringAsync(cancellationToken).ConfigureAwait(false);
            StartupDiagnostics.Note("Catalog readiness: succeeded; Previews readiness: " + (previews is null ? "unavailable" : "succeeded"));
            return new(StorageStartupStatus.Ready, coordinator);
        }
        catch (CatalogLocationAdmissionException exception)
        {
            if (pendingSession is not null) await pendingSession.DisposeAsync().ConfigureAwait(false);
            catalogStartup?.Dispose(); previewStartup?.Dispose(); startupCompletion?.Dispose();
            var (availablePreviews, diagnostic) = await OpenPreviewsAsync(settings, locations, cancellationToken).ConfigureAwait(false);
            return new(StorageStartupStatus.CatalogUnavailable,
                CreateRoot(settings, locations, null, recovery, availablePreviews, diagnostic), exception.Message, presentationReason);
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

    public async Task<CatalogStartupResult> StartAsync(CancellationToken cancellationToken = default,
        bool allowFirstCreation = true, string? existingCatalogDirectory = null, Guid? selectedCatalogId = null,
        bool resumePreviousCatalogOnFailure = true)
    {
        if (existingCatalogDirectory is null)
            return await StartCoreAsync(cancellationToken, allowFirstCreation).ConfigureAwait(false);
        var defaults = _effects.Profile;
        var configuration = _configuration;
        if (!configuration.TryLoad(out var previous, out var diagnostic)) return new(StorageStartupStatus.InvalidConfiguration, Diagnostic: diagnostic);
        if (selectedCatalogId is null || selectedCatalogId == Guid.Empty)
            return new(StorageStartupStatus.InvalidConfiguration, Diagnostic: "Explicit Catalog selection requires the preflight Catalog identity.");
        var intended = configuration.Stage(previous, new(SetCatalogDirectory: true, CatalogDirectory: Path.GetFullPath(existingCatalogDirectory), SetCatalogId: true, CatalogId: selectedCatalogId));
        CatalogStartupResult? selected = null;
        try
        {
            selected = await StartCoreAsync(cancellationToken, false, intended).ConfigureAwait(false);
            if (selected.IsReady) return selected;
            if (selected.Coordinator is not null) await selected.Coordinator.DisposeAsync().ConfigureAwait(false);
            diagnostic = selected.Diagnostic ?? "The selected Catalog could not be activated.";
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or JsonException or Microsoft.Data.Sqlite.SqliteException)
        { diagnostic = error.Message; }
        // The old shell has retired. Never resume a candidate after an unverified save.
        // Prove old configuration before reopening its existing owner; creation is disabled.
        try
        {
            if (!configuration.TryLoad(out var persisted, out _) || persisted.CatalogId != previous.CatalogId || persisted.CatalogDirectory != previous.CatalogDirectory)
                configuration.Save(previous);
            if (!configuration.TryLoad(out persisted, out _) || persisted.CatalogId != previous.CatalogId || persisted.CatalogDirectory != previous.CatalogDirectory)
                throw new IOException("Previous Catalog configuration could not be verified.");
        }
        catch (Exception rollback) when (rollback is IOException or UnauthorizedAccessException)
        { return new(StorageStartupStatus.InvalidConfiguration, Diagnostic: "Recovery required: Catalog selection and configuration rollback could not be proven. All Catalog artifacts are retained. " + diagnostic + " " + rollback.Message,
            RecoveryReason: CatalogRecoveryReason.PendingRecovery); }
        if (!resumePreviousCatalogOnFailure)
            return new(selected?.Status ?? StorageStartupStatus.CatalogUnreadable, Diagnostic: diagnostic,
                RecoveryReason: selected?.RecoveryReason ?? CatalogRecoveryReason.ValidationFailed);
        var prior = await StartCoreAsync(cancellationToken, false).ConfigureAwait(false);
        if (prior.Coordinator is not null) prior.Coordinator.RecoveryDiagnostic = "The selected Catalog was not opened. Previous configuration was retained. " + diagnostic;
        return prior with { Diagnostic = "The selected Catalog was not opened. Previous configuration was retained. " + diagnostic };
    }

    public async Task<CatalogStartupResult> CreateCatalogAsync(string directory, CancellationToken token = default)
    {
        var profile = _effects.Profile;
        var configuration = _configuration;
        if (!configuration.TryLoad(out var previous, out var diagnostic))
            return new(StorageStartupStatus.InvalidConfiguration, Diagnostic: diagnostic, RecoveryReason: CatalogRecoveryReason.ConfigurationInvalid);
        var pending = CatalogTransferJournal.InspectPending(_effects.TransferNative, profile);
        if (pending is not null) return new(StorageStartupStatus.CatalogUnavailable, Diagnostic: pending, RecoveryReason: CatalogRecoveryReason.PendingRecovery);
        directory = Path.GetFullPath(directory);
        if (profile.IsIsolated && !_effects.Contains(profile.ApplicationDataDirectory, directory))
            return new(StorageStartupStatus.CatalogUnavailable,
                Diagnostic: $"Allowed workspace: {profile.ApplicationDataDirectory}\nProfile containment refused the selected folder.",
                RecoveryReason: CatalogRecoveryReason.OutsideWorkspace);
        try
        {
            if (profile.IsIsolated) _effects.RequireContained(profile.ApplicationDataDirectory, directory);
        }
        catch (Exception refusal) when (refusal is ArgumentException or IOException or UnauthorizedAccessException)
        { return new(StorageStartupStatus.CatalogUnavailable, Diagnostic: refusal.Message, RecoveryReason: CatalogRecoveryReason.LocationRefused); }
        if (!CanCreateAt(directory, profile, previous))
            return new(StorageStartupStatus.CatalogUnavailable, Diagnostic: "Choose a new, unused Catalog folder outside the previous Catalog and reserved storage locations.", RecoveryReason: CatalogRecoveryReason.LocationRefused);
        var intended = configuration.Stage(previous, new(SetCatalogDirectory: true, CatalogDirectory: directory, SetCatalogId: true, SetBackupDirectory: true));
        CatalogStartupResult? candidate = null;
        try
        {
            candidate = await StartCoreAsync(allowFirstCreation: false,
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
            RecoveryReason: ResolveRecoveryReason(candidate?.Status ?? StorageStartupStatus.CatalogUnreadable,
                candidate?.RecoveryReason ?? CatalogRecoveryReason.ValidationFailed));
    }

    public string ProposeNewCatalogDirectory(ILightflowStorageLocations profile, ICatalogConfigurationSnapshot previous)
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

    private bool CanCreateAt(string directory, ILightflowStorageLocations profile, ICatalogConfigurationSnapshot previous)
    {
        if (File.Exists(directory)) return false;
        if (!IsUnoccupied(directory, profile)) return false;
        if (previous.CatalogDirectory is not null || previous.CatalogId is not null ||
            !IsUnoccupied(profile.CatalogDirectory, profile))
        {
            var reserved = previous.CatalogDirectory ?? profile.CatalogDirectory;
            if (_effects.PathsOverlap(directory, reserved)) return false;
        }
        return !new[] { profile.PreviewsDirectory, profile.TemporaryDirectory, CatalogTransferJournal.DirectoryPath(_effects.TransferNative, profile) }
            .Any(path => _effects.PathsOverlap(directory, path));
    }

    private bool IsUnoccupied(string directory, ILightflowStorageLocations profile)
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

    public async Task<CatalogSelectionResult> InspectCatalogSelectionAsync(string directory, CancellationToken token = default)
    {
        try
        {
            var selected = _effects.WithOverrides(Locations, directory, Settings.PreviewsDirectory);
            if (SamePath(selected.CatalogDirectory, Locations.CatalogDirectory) && CatalogAvailable)
                return new(true, CatalogSession.Identity.CatalogId, selected.CatalogDirectory, "This Catalog is already open.");
            if (!Directory.Exists(selected.CatalogDirectory) || !File.Exists(selected.CatalogDatabasePath))
                return new(false, Diagnostic: "Select the existing Catalog folder itself, rather than its parent. The selection has not been created or replaced.", RecoveryReason: CatalogRecoveryReason.CatalogDirectoryRequired);
            using var admission = _effects.CreateAdmission(selected, StorageOperation.Open);
            await admission.ValidateAsync(token).ConfigureAwait(false);
            var bound = admission.LocationsForUse();
            using var preflightCompletion = _startupCompletion is null ? new StartupSessionCompletion(_effects.Evidence, Locations.ApplicationDataDirectory) : null;
            using var ownership = StartupStoreEvidence.Begin(_effects.Evidence, selected.CatalogDatabasePath, "Catalog", false,
                _startupCompletion ?? preflightCompletion, bound.CatalogDatabasePath) ?? throw new IOException("Catalog ownership could not be established.");
            ownership.RequireOwnership(bound.CatalogDatabasePath);
            var transferredIdentity = VerifySelectedTransfer(bound.CatalogDirectory, ownership.HadValidatedCatalog);
            var recovery = new CatalogRecoveryService(_effects.RecoveryNative, bound);
            var opened = await new CatalogDatabaseService(selected, recovery)
            {
                ExpectedCatalogId = transferredIdentity,
                ResolvedDatabasePath = bound.CatalogDatabasePath,
                ValidateStorageAccess = (t, phase) => admission.ValidateClosedDatabaseBoundary(t, phase)
            }.OpenExistingAsync(token).ConfigureAwait(false);
            if (!opened.IsSuccess) return new(false, Diagnostic: opened.Diagnostic, RecoveryReason: CatalogRecoveryReason.ValidationFailed);
            await using var session = opened.Session!;
            if (transferredIdentity is Guid expected && session.Identity.CatalogId != expected)
                return new(false, Diagnostic: "Catalog identity does not match its retained transfer receipt. No identity has been rewritten.");
            session.CloseBeforeActivation();
            await admission.ValidateAsync(token, "SelectionPreflight_SQLiteClosed").ConfigureAwait(false);
            var full = await Task.Run(() => recovery.InspectOwnedCatalog(bound.CatalogDatabasePath, token), token).ConfigureAwait(false);
            if (!full.IsValid) return new(false, Diagnostic: full.Diagnostic);
            CatalogRecoveryService.ReadExternalLutDependencies(_effects.RecoveryNative, bound.CatalogDatabasePath);
            ownership.MarkValidated();
            return new(true, session.Identity.CatalogId, selected.CatalogDirectory);
        }
        catch (OperationCanceledException) { throw; }
        catch (CatalogLocationAdmissionException error)
        { return new(false, Diagnostic: error.Message, RecoveryReason: error.Reason is StorageLocationReason.UnsupportedCatalogFileSystem or StorageLocationReason.UnsupportedCatalogCapabilities or StorageLocationReason.NetworkActiveCatalog
            ? CatalogRecoveryReason.UnsupportedStorage : CatalogRecoveryReason.LocationRefused); }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or NotSupportedException or JsonException or Microsoft.Data.Sqlite.SqliteException)
        { return new(false, Diagnostic: error.Message, RecoveryReason: error.InnerException is System.ComponentModel.Win32Exception { NativeErrorCode: 32 }
            ? CatalogRecoveryReason.CompetingOwner : CatalogRecoveryReason.ValidationFailed); }
    }

    public Guid? VerifySelectedTransfer(string directory, bool previouslyValidated)
    {
        var pending = Path.Combine(directory, "pending.json");
        var manifestPath = Path.Combine(directory, "manifest.json");
        var readyPath = Path.Combine(directory, "ready.json");
        if (!File.Exists(pending) && !File.Exists(manifestPath) && !File.Exists(readyPath)) return null;
        if (!File.Exists(pending) || !File.Exists(manifestPath) || !File.Exists(readyPath))
            throw new InvalidDataException("Incomplete Catalog transfer. Retain the folder and its recovery evidence.");
        // A never-opened closed unit must verify byte-for-byte before first SQLite use.
        // Once the existing lifecycle records successful validation, authored writes invalidate the
        // historical snapshot hashes. Its metadata remains retained evidence; native
        // admission, full SQLite/identity/path validation govern this mutable Catalog.
        // Dirty or failed first-open evidence never grants this distinction.
        if (!previouslyValidated)
        {
            var manifest = CatalogRecoveryService.VerifyTransport(_effects.RecoveryNative, directory, allowCatalogOwnershipEvidence: true);
            if (manifest.PublicationMode != ClosedCatalogPublicationMode.AtomicDirectory)
                throw new InvalidDataException("Select a standard Catalog, not a backup transport container.");
            return manifest.CatalogId;
        }
        else
        {
            var manifest = ClosedCatalogManifestFormat.Deserialize(_files.ReadBounded(manifestPath));
            if (manifest.PublicationMode != ClosedCatalogPublicationMode.AtomicDirectory)
                throw new InvalidDataException("A backup container is not an active Catalog.");
            var ready = JsonSerializer.Deserialize<ClosedCatalogReadiness>(_files.ReadBounded(readyPath))
                ?? throw new InvalidDataException("Missing readiness receipt.");
            ClosedCatalogManifestFormat.ValidateReadiness(manifest, ready);
            _files.VerifyMetadata(directory, manifest, ready);
            return manifest.CatalogId;
        }
    }

    public async Task<StorageChangeResult> RelocateCatalogAsync(string destinationDirectory, CancellationToken cancellationToken = default,
        IProgress<ClosedCatalogTransferState>? progress = null)
    {
        ILightflowStorageLocations destination;
        try
        {
            destination = ValidateCatalogDestination(destinationDirectory);
            if (SamePath(Locations.CatalogDirectory, destination.CatalogDirectory))
                return new(StorageChangeStatus.EquivalentLocation, "That is already the active Catalog location.");
            if (Directory.Exists(destination.CatalogDirectory) || File.Exists(destination.CatalogDirectory))
                return new(StorageChangeStatus.ConflictingCatalog, "The resulting Catalog folder already exists. Choose another name or parent folder.");
        }
        catch (Exception e) when (e is IOException or ArgumentException or UnauthorizedAccessException or NotSupportedException)
        { return new(StorageChangeStatus.InvalidDestination, e.Message); }
        await _mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        CatalogMutationLifecycle.Quiescence? quiet = null;
        CatalogDatabaseSession? provisional = null;
        StartupStoreEvidence? destinationOwnership = null;
        var sourceLocations = Locations;
        var sourceSettings = Settings;
        var sourceClosed = false;
        var configurationAttempted = false;
        var resumed = false;
        var consumersStopped = false;
        try
        {
            if (_catalogSession is null) return new(StorageChangeStatus.Failed, "The current Catalog is unavailable.");
            RequireCatalogOwnership(_catalogSession.ResolvedDatabasePath);
            var identity = _catalogSession.Identity.CatalogId;
            var schema = _catalogSession.SchemaVersion;
            var changed = _configuration.Stage(sourceSettings, new(SetCatalogDirectory: true, CatalogDirectory: destination.CatalogDirectory, SetCatalogId: true, CatalogId: identity));
            var request = new ClosedCatalogTransferRequest(Guid.NewGuid(), 0, ClosedCatalogTransferMode.MoveAndActivate,
                identity, schema, CatalogDatabaseService.SqliteApplicationId);
            using var sourceAdmission = _effects.CreateAdmission(sourceLocations, StorageOperation.Open);
            using var destinationAdmission = _effects.CreateAdmission(destination, StorageOperation.Relocate);
            await using var journal = new CatalogTransferJournal(_effects.TransferNative, sourceLocations, request.OperationId);
            var operation = new ClosedCatalogTransferOperation(_effects.TransferNative, sourceLocations, destination.CatalogDirectory,
                sourceSettings.Serialize(), changed.Serialize(),
                async token =>
                {
                    await sourceAdmission.ValidateAsync(token).ConfigureAwait(false);
                    sourceAdmission.RequireActiveSessionBinding(_catalogSession);
                    quiet = await Mutations.QuiesceAsync(token).ConfigureAwait(false);
                    consumersStopped = true;
                    await StopConsumersAsync().ConfigureAwait(false);
                    var current = _catalogSession!;
                    _catalogSession = null;
                    sourceClosed = true;
                    await current.DisposeAsync().ConfigureAwait(false);
                },
                (r, local, token) => new CatalogRecoveryService(_effects.RecoveryNative, sourceLocations).FinalizeTransferSnapshotAsync(r, local, token, _transfer),
                async token =>
                {
                    await destinationAdmission.ValidateAsync(token).ConfigureAwait(false);
                    var bound = destinationAdmission.LocationsForUse();
                    destinationOwnership = StartupStoreEvidence.Begin(_effects.Evidence, destination.CatalogDatabasePath, "Catalog", false,
                        _startupCompletion, bound.CatalogDatabasePath)
                        ?? throw new IOException("The destination Catalog ownership lease could not be established.");
                    destinationOwnership.RequireOwnership(bound.CatalogDatabasePath);
                    var opened = await new CatalogDatabaseService(destination)
                    {
                        ValidateStorageAccess = (t, phase) => destinationAdmission.ValidateClosedDatabaseBoundary(t, phase),
                        ResolvedDatabasePath = bound.CatalogDatabasePath
                    }.OpenExistingAsync(token).ConfigureAwait(false);
                    provisional = opened.Session;
                    if (!opened.IsSuccess || provisional!.Identity.CatalogId != identity || provisional.SchemaVersion != schema)
                        throw new InvalidDataException(opened.Diagnostic ?? "The transferred Catalog did not preserve identity/schema.");
                    provisional.CloseBeforeActivation();
                    await destinationAdmission.ValidateAsync(token, "TransferActivation_SQLiteClosed").ConfigureAwait(false);
                    destinationOwnership.MarkValidated();
                    var activated = _effects.Activate(provisional);
                    if (!ReferenceEquals(activated, provisional))
                    {
                        if (activated is not null) await activated.DisposeAsync().ConfigureAwait(false);
                        throw new InvalidDataException("Existing Catalog activation did not retain its owned session.");
                    }
                    if (provisional.Identity.CatalogId != identity || provisional.SchemaVersion != schema)
                        throw new InvalidDataException("Existing Catalog activation did not return the intended session.");
                    return new ClosedCatalogTransferReceipt($"Existing lifecycle activated Catalog {identity:D}, schema {schema}; authoring remains closed");
                },
                token =>
                {
                    configurationAttempted = true;
                    _configuration.Save(changed);
                    if (!_configuration.TryLoad(out var persisted, out var diagnostic) || persisted.CatalogId != identity ||
                        !string.Equals(persisted.CatalogDirectory, changed.CatalogDirectory, StringComparison.Ordinal))
                        throw new IOException(diagnostic ?? "The activated Catalog configuration did not verify on readback.");
                    return Task.FromResult(new ClosedCatalogTransferReceipt($"Configuration readback verified: Catalog {identity:D}, directory {changed.CatalogDirectory}"));
                },
                _ =>
                {
                    Settings = changed; Locations = destination;
                    _recovery = new CatalogRecoveryService(_effects.RecoveryNative, destination);
                    var sourceOwnership = _catalogStartup;
                    _catalogStartup = destinationOwnership;
                    destinationOwnership = null;
                    _catalogSession = provisional;
                    _catalogSession!.MarkPublished(); provisional = null;
                    sourceOwnership!.Dispose();
                    quiet!.Dispose(); quiet = null;
                    resumed = true;
                    return Task.CompletedTask;
                },
                async rollbackToken =>
                {
                    if (provisional is not null) { await provisional.DisposeAsync().ConfigureAwait(false); provisional = null; }
                    destinationOwnership?.Dispose(); destinationOwnership = null;
                    if (configurationAttempted)
                    {
                        if (!_configuration.TryLoad(out var persisted, out _) || persisted.CatalogId != sourceSettings.CatalogId ||
                            persisted.CatalogDirectory != sourceSettings.CatalogDirectory)
                            _configuration.Save(sourceSettings);
                        if (!_configuration.TryLoad(out persisted, out _) || persisted.CatalogId != sourceSettings.CatalogId ||
                            persisted.CatalogDirectory != sourceSettings.CatalogDirectory) return false;
                    }
                    Settings = sourceSettings; Locations = sourceLocations;
                    if (sourceClosed)
                    {
                        var reopened = await TryReactivateCatalogAsync(activate: false).ConfigureAwait(false);
                        if (!reopened.Succeeded) return false;
                    }
                    quiet?.Dispose(); quiet = null; resumed = true;
                    return true;
                });
            var result = await new ClosedCatalogTransferOrchestrator().ExecuteAsync(request, operation,
                journal, progress, cancellationToken).ConfigureAwait(false);
            if (result.State == ClosedCatalogTransferState.RecoveryRequired)
            {
                PreventCleanShutdown();
                resumed = false;
                RecoveryDiagnostic = result.Diagnostic ?? "Catalog ownership recovery is required. Authoring is blocked.";
                if (_catalogSession is not null) { await _catalogSession.DisposeAsync().ConfigureAwait(false); _catalogSession = null; }
                _catalogStartup?.Dispose(); _catalogStartup = null;
                quiet?.CompleteShutdown(); quiet = null;
                _shutdownAdmissionClosed = true;
            }
            return result.Succeeded ? new(StorageChangeStatus.Succeeded,
                $"Catalog moved to {destination.CatalogDirectory}. The source at {sourceLocations.CatalogDirectory} is retained as a pre-move recovery copy.")
                : new(StorageChangeStatus.Failed, $"The Catalog was not moved successfully. {result.Diagnostic} Source and transfer artifacts are retained. Operation {request.OperationId:N}; state {result.State}.");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        { return new(StorageChangeStatus.Failed, $"Catalog transfer could not establish durable operation evidence: {e.Message}"); }
        finally
        {
            try
            {
            if (provisional is not null) await provisional.DisposeAsync().ConfigureAwait(false);
            destinationOwnership?.Dispose();
            if (resumed || !sourceClosed)
            {
                quiet?.Dispose();
                if (consumersStopped)
                {
                    await RestartConsumersAsync().ConfigureAwait(false);
                }
            }
            }
            finally { _mutationGate.Release(); }
        }
    }

    public async Task<CatalogRestoreResult> RestoreCatalogAsync(string backupPath, CancellationToken cancellationToken = default)
    {
        var transferRecovery = CatalogTransferJournal.InspectPending(_effects.TransferNative, Locations);
        if (transferRecovery is not null) return new(false, transferRecovery);
        if (_recovery is CatalogRecoveryService transportRecovery)
        {
            try { backupPath = await transportRecovery.PrepareRecoveryInputAsync(backupPath, cancellationToken).ConfigureAwait(false); }
            catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException)
            { return new(false, $"The backup could not be verified and copied back locally. {e.Message}"); }
        }
        return await Mutations.RunAsync<CatalogRestoreResult>(async () => {
        await _mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await StopConsumersAsync().ConfigureAwait(false);
            using var admission = _effects.CreateAdmission(Locations, StorageOperation.RestoreActivation);
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
                installation = _recovery is CatalogRecoveryService nativeRecovery
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
                var migrationRecovery = _recovery is CatalogRecoveryService migrationNative
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
                var activated = _effects.Activate(replacementSession!);
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
                _effects.NotifyCatalogRestored();
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
                await RestartConsumersAsync().ConfigureAwait(false);
            }
            finally { _mutationGate.Release(); }
        }
    }, cancellationToken);
    }

    private async Task<CatalogRestoreResult> TryReactivateCatalogAsync(bool activate = true)
    {
        using var admission = _effects.CreateAdmission(Locations, StorageOperation.Open);
        try
        {
            await admission.ValidateAsync(CancellationToken.None).ConfigureAwait(false);
            await admission.ValidateAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (CatalogLocationAdmissionException exception) { return new(false, exception.Message); }
        var boundLocations = admission.LocationsForUse();
        RequireCatalogOwnership(boundLocations.CatalogDatabasePath);
        var migrationRecovery = _recovery is CatalogRecoveryService migrationNative
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
            var activated = activate ? _effects.Activate(opened.Session!) : opened.Session!;
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
            completion = _startupCompletion is null ? new StartupSessionCompletion(_effects.Evidence, Locations.ApplicationDataDirectory) : null;
            lease = StartupStoreEvidence.Begin(_effects.Evidence, Locations.CatalogDatabasePath, "Catalog", false,
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

    public async Task<CatalogBackupResult> BackupForExitAsync(string destination, bool keepQuiescent,
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
            var result = await new CatalogRecoveryService(_effects.RecoveryNative, Locations).CreateUserBackupAsync(
                _catalogSession.ResolvedDatabasePath, destination, _catalogSession.Identity.CatalogId,
                _catalogSession.SchemaVersion, progress, cancellationToken, diagnosticOperationId: operationId).ConfigureAwait(false);
            if (result.Succeeded && keepQuiescent) { _exitQuiescence = quiet; keep = true; }
            return result;
        }
        finally { if (!keep) quiet.Dispose(); }
    }

    public async Task SaveBackupDestinationAsync(string destination, CancellationToken token)
    {
        await _mutationGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var settings = _configuration.Stage(Settings, new(SetBackupDirectory: true, CatalogBackupDirectory: destination));
            await Task.Run(() => _configuration.Save(settings), token).ConfigureAwait(false);
            Settings = settings;
        }
        finally { _mutationGate.Release(); }
    }

    private static StorageStartupStatus Map(CatalogOpenStatus status) => status switch
    {
        CatalogOpenStatus.MissingExpectedCatalog => StorageStartupStatus.CatalogMissing,
        CatalogOpenStatus.StorageUnavailable => StorageStartupStatus.CatalogUnavailable,
        CatalogOpenStatus.Corrupt => StorageStartupStatus.CatalogCorrupt,
        _ => StorageStartupStatus.CatalogUnreadable
    };

    public static CatalogRecoveryReason ResolveRecoveryReason(StorageStartupStatus status, CatalogRecoveryReason reason) =>
        reason != CatalogRecoveryReason.None || status is StorageStartupStatus.Ready or StorageStartupStatus.FirstRunChoiceRequired ? reason
        : status == StorageStartupStatus.InvalidConfiguration ? CatalogRecoveryReason.ConfigurationInvalid
        : status == StorageStartupStatus.CatalogMissing ? CatalogRecoveryReason.LocationUnavailable
        : status == StorageStartupStatus.CatalogUnavailable ? CatalogRecoveryReason.OperationFailed
        : CatalogRecoveryReason.ValidationFailed;
}
