using System.IO;
using Lightflow.Application;
using Lightflow.Domain;

namespace LightflowStudio;

/// <summary>Composition adapter only: shared policy decides suitability; existing lifecycle owns use.</summary>
internal sealed class CatalogLocationAdmission : IDisposable
{
    private readonly IStorageLocationAssessor _assessor;
    private readonly WindowsStorageLocationAssessor? _owned;
    private readonly StorageAssessmentRequest _request;
    private readonly LightflowStorageLocations _locations;
    private StorageLocationAssessment? _previous;

    internal CatalogLocationAdmission(LightflowStorageLocations locations, StorageOperation operation,
        IStorageLocationAssessor? assessor = null)
    {
        _locations = locations;
        _request = new(Guid.NewGuid(), 0, StorageRole.ActiveCatalog, operation, locations.CatalogDirectory);
        _assessor = assessor ?? (_owned = new WindowsStorageLocationAssessor(
            [locations.PreviewsDirectory, locations.TemporaryDirectory]));
    }

    internal async Task ValidateAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_locations.IsIsolated) ApplicationDataProfile.GuardAccess(_locations.CatalogDatabasePath);
        var current = await _assessor.AssessAsync(_request, cancellationToken).ConfigureAwait(false);
        var result = _previous is null
            ? StorageLocationPolicy.Evaluate(_request, current, DateTimeOffset.UtcNow, cancellationToken)
            : StorageLocationPolicy.Revalidate(_request, _previous, current, DateTimeOffset.UtcNow, cancellationToken);
        if (result.Decision == StorageLocationDecision.Cancelled) throw new OperationCanceledException(cancellationToken);
        if (result.Decision != StorageLocationDecision.Eligible)
        {
            var diagnostic = result.Reason == StorageLocationReason.NetworkActiveCatalog
                ? "Lightflow Catalogs must be stored on a supported local drive. Network locations are supported for media and Catalog backups, but not for an active Catalog."
                : result.Diagnostic;
            throw new CatalogLocationAdmissionException($"{diagnostic}\n\nConfigured Catalog: {_request.RequestedLocation}" +
                (current.ProviderDiagnostic is null ? "" : $"\n{current.ProviderDiagnostic}"), result.Reason);
        }
        _previous = current;
        cancellationToken.ThrowIfCancellationRequested();
    }
    internal LightflowStorageLocations LocationsForUse()
    {
        if (_previous?.Identity is not { } identity) throw new InvalidOperationException("Assess the Catalog location before use.");
        if (_owned is null) return _locations; // Controllable port fixtures are not native path bindings.
        // Operation-only canonical routing; never persist this in place of configured locations.
        return _locations with
        {
            CatalogDirectory = identity.CanonicalLocation,
            CatalogDatabasePath = Path.Combine(identity.CanonicalLocation, LightflowStorageLocations.CatalogFileName),
            CatalogBackupsDirectory = Path.Combine(identity.CanonicalLocation, "Backups")
        };
    }
    internal void RequireActiveSessionBinding(CatalogDatabaseSession? session)
    {
        if (_owned is not null && session is not null && !string.Equals(
            LocationsForUse().CatalogDatabasePath, session.ResolvedDatabasePath, StringComparison.OrdinalIgnoreCase))
            throw new CatalogLocationAdmissionException(
                "The configured Catalog now resolves to a different location from the active Catalog. Restart Lightflow before changing storage.",
                StorageLocationReason.LocationChanged);
    }
    public void Dispose() => _owned?.Dispose();
}

internal sealed class CatalogLocationAdmissionException(string diagnostic, StorageLocationReason reason) : IOException(diagnostic)
{
    internal StorageLocationReason Reason { get; } = reason;
}
