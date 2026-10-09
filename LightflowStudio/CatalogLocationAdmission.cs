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
    private StorageLocationAssessment? _previous;

    internal CatalogLocationAdmission(LightflowStorageLocations locations, StorageOperation operation,
        IStorageLocationAssessor? assessor = null)
    {
        _request = new(Guid.NewGuid(), 0, StorageRole.ActiveCatalog, operation, locations.CatalogDirectory);
        _assessor = assessor ?? (_owned = new WindowsStorageLocationAssessor(
            [locations.PreviewsDirectory, locations.TemporaryDirectory]));
    }

    internal async Task ValidateAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
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
    public void Dispose() => _owned?.Dispose();
}

internal sealed class CatalogLocationAdmissionException(string diagnostic, StorageLocationReason reason) : IOException(diagnostic)
{
    internal StorageLocationReason Reason { get; } = reason;
}
