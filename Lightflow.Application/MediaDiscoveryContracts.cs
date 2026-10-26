namespace LightflowStudio;

public sealed record DerivedWorkDiagnostics(long Enqueued, long Processed, long MetadataAttempts, long ThumbnailAttempts, int Outstanding);

public enum DerivedWorkPriority { Background, Normal, Visible }
public enum DerivedWorkBatchStatus { Running, Completed, Canceled }
public enum DerivedWorkItemOutcome { Generated, Current, PartialFailure, Failed, SkippedUnavailable, Canceled }
public enum DerivedWorkComponentOutcome { NotNeeded, Succeeded, Current, SkippedUnavailable, Failed, Canceled }

public sealed record DerivedWorkItemResult(
    Guid AssetId,
    DerivedWorkItemOutcome Outcome,
    DerivedWorkComponentOutcome Metadata,
    DerivedWorkComponentOutcome Thumbnail,
    string? Diagnostic = null,
    PreviewFailureReason ThumbnailFailureReason = PreviewFailureReason.Unknown);

public sealed record DerivedWorkProgress(
    Guid BatchId,
    DerivedWorkBatchStatus Status,
    int Total,
    int Pending,
    int Running,
    int Completed,
    int Generated,
    int Current,
    int Failed,
    int Skipped,
    int Canceled);

public interface IDerivedWorkBatch
{
    Guid BatchId { get; }
    CatalogReconciliationResult Reconciliation { get; }
    DerivedWorkProgress Progress { get; }
    IReadOnlyList<DerivedWorkItemResult> Results { get; }
    Task<DerivedWorkProgress> Completion { get; }
    event EventHandler<DerivedWorkProgress>? ProgressChanged;
    void Cancel();
}

public interface IDerivedWorkScheduler : IAsyncDisposable
{
    DerivedWorkSchedulingResult TrySchedule(CatalogReconciliationResult reconciliation,
        DerivedWorkPriority priority = DerivedWorkPriority.Background,
        CancellationToken cancellationToken = default);
}

public enum DerivedWorkSchedulingStatus { Accepted, SchedulerUnavailable }

public sealed record DerivedWorkSchedulingResult(
    DerivedWorkSchedulingStatus Status,
    IDerivedWorkBatch? Batch = null,
    string? Diagnostic = null)
{
    public bool Accepted => Status == DerivedWorkSchedulingStatus.Accepted && Batch is not null;
}

public sealed record MediaDiscoveryRefreshResult(
    CatalogReconciliationResult Reconciliation,
    IDerivedWorkBatch? DerivedWork,
    string? Diagnostic = null);

public interface IMediaDiscoveryRefreshService
{
    Task<MediaDiscoveryRefreshResult> RefreshAsync(MediaFolderEnumerationRequest request,
        DerivedWorkPriority priority = DerivedWorkPriority.Background,
        CancellationToken cancellationToken = default,
        CancellationToken derivedWorkCancellationToken = default);
}
