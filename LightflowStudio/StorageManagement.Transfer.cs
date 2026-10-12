using System.IO;
using System.Text.Json;
using Lightflow.Application;
using Lightflow.Domain;

namespace LightflowStudio;

internal sealed partial class LightflowStorageCoordinator
{
    public async Task<StorageChangeResult> RelocateCatalogAsync(string destinationDirectory, CancellationToken cancellationToken = default,
        IProgress<ClosedCatalogTransferState>? progress = null)
    {
        LightflowStorageLocations destination;
        try
        {
            destination = ValidateDestination(destinationDirectory, catalog: true);
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
            var changed = sourceSettings with { CatalogDirectory = destination.CatalogDirectory, CatalogId = identity };
            var request = new ClosedCatalogTransferRequest(Guid.NewGuid(), 0, ClosedCatalogTransferMode.MoveAndActivate,
                identity, schema, CatalogDatabaseService.SqliteApplicationId);
            using var sourceAdmission = new CatalogLocationAdmission(sourceLocations, StorageOperation.Open, _assessor);
            using var destinationAdmission = new CatalogLocationAdmission(destination, StorageOperation.Relocate, _assessor);
            await using var journal = new ClosedCatalogTransferJournal(sourceLocations, request.OperationId);
            var operation = new WindowsClosedCatalogTransferOperation(sourceLocations, destination.CatalogDirectory,
                JsonSerializer.Serialize(sourceSettings), JsonSerializer.Serialize(changed),
                async token =>
                {
                    await sourceAdmission.ValidateAsync(token).ConfigureAwait(false);
                    sourceAdmission.RequireActiveSessionBinding(_catalogSession);
                    quiet = await Mutations.QuiesceAsync(token).ConfigureAwait(false);
                    consumersStopped = true;
                    await _lifecycle.StopConsumersAsync().ConfigureAwait(false);
                    var current = _catalogSession!;
                    _catalogSession = null;
                    sourceClosed = true;
                    await current.DisposeAsync().ConfigureAwait(false);
                },
                (r, local, token) => new SqliteCatalogRecoveryService(sourceLocations).FinalizeTransferSnapshotAsync(r, local, token, _transfer),
                async token =>
                {
                    await destinationAdmission.ValidateAsync(token).ConfigureAwait(false);
                    var bound = destinationAdmission.LocationsForUse();
                    destinationOwnership = StartupStoreEvidence.Begin(WindowsCatalogEvidenceEffects.Instance, destination.CatalogDatabasePath, "Catalog", false,
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
                    var activated = _activator.Activate(provisional);
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
                    _recovery = new SqliteCatalogRecoveryService(destination);
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
                    await _lifecycle.RestartConsumersAsync().ConfigureAwait(false);
                }
            }
            }
            finally { _mutationGate.Release(); }
        }
    }
}
