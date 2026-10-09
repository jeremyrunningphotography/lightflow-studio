using Lightflow.Domain;

namespace Lightflow.Application;

/// <summary>
/// OS capability port. Resolve current filesystem/volume/alias/containment facts without creating,
/// deleting or replacing storage. Return explicit Failed/Cancelled snapshots for assessment errors;
/// never reinterpret errors as absent Catalogs. Cancellation may also throw OperationCanceledException:
/// the caller must stop before admission. Do not return a cached snapshot at identity-sensitive
/// boundaries. Native handles and platform APIs remain private to the implementation.
/// </summary>
public interface IStorageLocationAssessor
{
    Task<StorageLocationAssessment> AssessAsync(StorageAssessmentRequest request,
        CancellationToken cancellationToken = default);
}
