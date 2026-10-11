using System.IO;
using System.Text.Json;
using Lightflow.Application;
using Lightflow.Domain;

namespace LightflowStudio;

internal sealed record CatalogSelectionResult(bool Succeeded, Guid? CatalogId = null, string? Directory = null, string? Diagnostic = null,
    CatalogRecoveryReason RecoveryReason = CatalogRecoveryReason.None);

internal sealed partial class LightflowStorageCoordinator
{
    public static async Task<StorageStartupResult> StartAsync(string? localApplicationData = null,
        CancellationToken cancellationToken = default, ICatalogRelocationTransfer? transfer = null,
        IStorageConfigurationStore? configuration = null, ICatalogSessionActivator? activator = null,
        ICatalogRecoveryService? recovery = null, LightflowStorageLocations? profile = null,
        InitializedDataProfile? initializedProfile = null, IStorageLocationAssessor? assessor = null,
        bool allowFirstCreation = true, string? existingCatalogDirectory = null, Guid? selectedCatalogId = null, bool resumePreviousCatalogOnFailure = true)
    {
        if (existingCatalogDirectory is null)
            return await StartCoreAsync(localApplicationData, cancellationToken, transfer, configuration, activator,
                recovery, profile, initializedProfile, assessor, allowFirstCreation).ConfigureAwait(false);
        var defaults = profile ?? (localApplicationData is null ? LightflowStorageLocations.Current : LightflowStorageLocations.Create(localApplicationData));
        configuration ??= new AppSettingsStorageConfigurationStore(defaults.SettingsPath, defaults.IsIsolated);
        if (!configuration.TryLoad(out var previous, out var diagnostic)) return new(StorageStartupStatus.InvalidConfiguration, Diagnostic: diagnostic);
        if (selectedCatalogId is null || selectedCatalogId == Guid.Empty)
            return new(StorageStartupStatus.InvalidConfiguration, Diagnostic: "Explicit Catalog selection requires the preflight Catalog identity.");
        var intended = previous with { CatalogDirectory = Path.GetFullPath(existingCatalogDirectory), CatalogId = selectedCatalogId };
        StorageStartupResult? selected = null;
        try
        {
            selected = await StartCoreAsync(localApplicationData, cancellationToken, transfer, configuration, activator,
                recovery, defaults, initializedProfile, assessor, false, intended).ConfigureAwait(false);
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
        { return new(StorageStartupStatus.InvalidConfiguration, Diagnostic: "Recovery required: Catalog selection and configuration rollback could not be proven. All Catalog artifacts are retained. " + diagnostic + " " + rollback.Message); }
        if (!resumePreviousCatalogOnFailure)
            return new(selected?.Status ?? StorageStartupStatus.CatalogUnreadable, Diagnostic: diagnostic,
                RecoveryReason: selected?.RecoveryReason ?? CatalogRecoveryReason.ValidationFailed);
        var prior = await StartCoreAsync(localApplicationData, cancellationToken, transfer, configuration, activator,
            recovery, defaults, initializedProfile, assessor, false).ConfigureAwait(false);
        if (prior.Coordinator is not null) prior.Coordinator.RecoveryDiagnostic = "The selected Catalog was not opened. Previous configuration was retained. " + diagnostic;
        return prior with { Diagnostic = "The selected Catalog was not opened. Previous configuration was retained. " + diagnostic };
    }

    // Preflight only. The shell retires through shutdown; the new process calls StartAsync
    // with this explicit identity and reacquires the same lease before activation/commit.
    public async Task<CatalogSelectionResult> InspectCatalogSelectionAsync(string directory, CancellationToken token = default)
    {
        try
        {
            var selected = Locations.WithOverrides(new(directory, Settings.PreviewsDirectory));
            if (SamePath(selected.CatalogDirectory, Locations.CatalogDirectory) && CatalogAvailable)
                return new(true, CatalogSession.Identity.CatalogId, selected.CatalogDirectory, "This Catalog is already open.");
            if (!Directory.Exists(selected.CatalogDirectory) || !File.Exists(selected.CatalogDatabasePath))
                return new(false, Diagnostic: "Choose an existing Catalog folder containing LightflowCatalog.db. The selection has not been created or replaced.", RecoveryReason: CatalogRecoveryReason.ValidationFailed);
            using var admission = new CatalogLocationAdmission(selected, StorageOperation.Open, _assessor);
            await admission.ValidateAsync(token).ConfigureAwait(false);
            var bound = admission.LocationsForUse();
            using var preflightCompletion = _startupCompletion is null ? new StartupSessionCompletion(Locations.ApplicationDataDirectory) : null;
            using var ownership = StartupStoreEvidence.Begin(selected.CatalogDatabasePath, "Catalog", false,
                _startupCompletion ?? preflightCompletion, bound.CatalogDatabasePath) ?? throw new IOException("Catalog ownership could not be established.");
            ownership.RequireOwnership(bound.CatalogDatabasePath);
            var transferredIdentity = VerifySelectedTransfer(bound.CatalogDirectory, ownership.HadValidatedCatalog);
            var recovery = new SqliteCatalogRecoveryService(bound);
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
            SqliteCatalogRecoveryService.ReadExternalLutDependencies(bound.CatalogDatabasePath);
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

    internal static Guid? VerifySelectedTransfer(string directory, bool previouslyValidated)
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
            var manifest = SqliteCatalogRecoveryService.VerifyTransport(directory, allowCatalogOwnershipEvidence: true);
            if (manifest.PublicationMode != ClosedCatalogPublicationMode.AtomicDirectory)
                throw new InvalidDataException("Select a standard Catalog, not a backup transport container.");
            return manifest.CatalogId;
        }
        else
        {
            var manifest = ClosedCatalogManifestFormat.Deserialize(WindowsClosedCatalogTransferOperation.ReadBounded(manifestPath));
            if (manifest.PublicationMode != ClosedCatalogPublicationMode.AtomicDirectory)
                throw new InvalidDataException("A backup container is not an active Catalog.");
            var ready = JsonSerializer.Deserialize<ClosedCatalogReadiness>(WindowsClosedCatalogTransferOperation.ReadBounded(readyPath))
                ?? throw new InvalidDataException("Missing readiness receipt.");
            ClosedCatalogManifestFormat.ValidateReadiness(manifest, ready);
            WindowsClosedCatalogTransferOperation.VerifyMetadata(directory, manifest, ready);
            return manifest.CatalogId;
        }
    }
}
