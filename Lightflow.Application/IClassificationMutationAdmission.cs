namespace Lightflow.Application;

/// <summary>
/// Runs one complete logical operation under the existing Catalog lifecycle.
/// This port supplies admission/drain accounting, not writer serialization or a transaction.
/// Implementations must preserve nested admission and the caller's execution context.
/// </summary>
public interface IClassificationMutationAdmission
{
    Task<T> RunAsync<T>(Func<Task<T>> operation, CancellationToken token = default);
}
