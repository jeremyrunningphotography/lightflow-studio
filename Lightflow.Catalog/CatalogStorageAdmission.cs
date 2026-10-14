using System.IO;
using Lightflow.Application;
using Lightflow.Domain;

namespace LightflowStudio;

/// <summary>Composition adapter only: shared policy decides suitability; existing lifecycle owns use.</summary>
public class CatalogStorageAdmission : IDisposable
{
    private readonly IStorageLocationAssessor _assessor;
    private readonly ICatalogAdmissionNativeEffects _effects;
    private readonly StorageAssessmentRequest _request;
    private readonly ILightflowStorageLocations _locations;
    private StorageLocationAssessment? _previous;
    private int _probe;

    public CatalogStorageAdmission(ILightflowStorageLocations locations, StorageOperation operation,
        ICatalogAdmissionNativeEffects effects)
    {
        _locations = locations;
        _request = new(Guid.NewGuid(), 0, StorageRole.ActiveCatalog, operation, locations.CatalogDirectory);
        _effects = effects ?? throw new ArgumentNullException(nameof(effects));
        _assessor = effects.Assessor;
    }

    public async Task ValidateAsync(CancellationToken cancellationToken, string phase = "CoordinatorPreflight_BeforeDatabaseServiceUse")
    {
        cancellationToken.ThrowIfCancellationRequested();
        _effects.GuardAccess(_locations.CatalogDatabasePath);
        var current = await _assessor.AssessAsync(_request, cancellationToken).ConfigureAwait(false);
        var context = $"Catalog admission: phase={phase}; boundary={(_previous is null ? "InitialAssessment" : "Revalidation")}; probe={++_probe}; " +
            $"operation={_request.Operation}; operationId={_request.OperationId:N}; generation={_request.Generation}; assessmentId={current.AssessmentId:N}";
        StartupDiagnostics.Note(context);
        var result = _previous is null
            ? StorageLocationPolicy.Evaluate(_request, current, DateTimeOffset.UtcNow, cancellationToken)
            : StorageLocationPolicy.Revalidate(_request, _previous, current, DateTimeOffset.UtcNow, cancellationToken);
        if (result.Decision == StorageLocationDecision.Cancelled) throw new OperationCanceledException(cancellationToken);
        if (result.Decision != StorageLocationDecision.Eligible)
        {
            var diagnostic = result.Reason == StorageLocationReason.NetworkActiveCatalog
                ? "Lightflow Catalogs must be stored on a supported local drive. Network locations are supported for media and Catalog backups, but not for an active Catalog."
                : result.Diagnostic;
            throw new CatalogLocationAdmissionException($"{diagnostic}\n\nConfigured Catalog: {_request.RequestedLocation}\n{context}" +
                (current.ProviderDiagnostic is null ? "" : $"\n{current.ProviderDiagnostic}"), result.Reason);
        }
        _previous = current;
        cancellationToken.ThrowIfCancellationRequested();
    }
    public ILightflowStorageLocations LocationsForUse()
    {
        if (_previous?.Identity is null) throw new InvalidOperationException("Assess the Catalog location before use.");
        return _effects.RouteForUse(_locations, _previous);
    }

    public void ValidateClosedDatabaseBoundary(CancellationToken token, CatalogStorageBoundary boundary)
    {
        ValidateAsync(token, boundary.ToString()).GetAwaiter().GetResult();
        if (boundary == CatalogStorageBoundary.FailedCreationCleanup_SQLiteClosed)
        {
            _effects.ReleaseMainDatabaseGuard();
            return;
        }
        if (boundary is CatalogStorageBoundary.AfterAtomicCreation_BeforeInitialSQLiteUse or CatalogStorageBoundary.BeforeInspection_BeforeInitialSQLiteUse)
        {
            try { _effects.GuardMainDatabase(LocationsForUse().CatalogDatabasePath); }
            catch (Exception error) when (error is System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException)
            {
                throw new CatalogLocationAdmissionException($"The main Catalog identity guard could not be established. {error.Message}", StorageLocationReason.AssessmentFailed);
            }
        }
    }

    // Only after the existing owner has disposed the pending writer/SQL scopes. Fresh full
    // validation still precedes intentional replacement/cleanup; never release on revocation
    // as permission to delete through that spelling.
    public async Task ReleaseForClosedReplacementAsync(CancellationToken token)
    {
        await ValidateAsync(token, "ClosedReplacement_BeforeGuardRelease").ConfigureAwait(false);
        _effects.ReleaseMainDatabaseGuard();
    }
    public void RequireActiveSessionBinding(CatalogDatabaseSession? session)
    {
        if (_effects.HasNativeBinding && session is not null && !_effects.SameDatabase(
            LocationsForUse().CatalogDatabasePath, session.ResolvedDatabasePath))
            throw new CatalogLocationAdmissionException(
                "The configured Catalog now resolves to a different location from the active Catalog. Restart Lightflow before changing storage.",
                StorageLocationReason.LocationChanged);
    }
    public void Dispose() => _effects.Dispose();
}
