namespace LightflowStudio;
public sealed class MediaDiscoveryRefreshService(
    ICatalogReconciliationService reconciliation,
    Func<IDerivedWorkScheduler?> scheduler) : IMediaDiscoveryRefreshService
{
    public async Task<MediaDiscoveryRefreshResult> RefreshAsync(MediaFolderEnumerationRequest request,
        DerivedWorkPriority priority = DerivedWorkPriority.Background,
        CancellationToken cancellationToken = default,
        CancellationToken derivedWorkCancellationToken = default)
    {
        var result = await reconciliation.ReconcileAsync(request, cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded) return new(result, null, result.Diagnostic);
        var activeScheduler = scheduler();
        if (activeScheduler is null)
            return new(result, null, "Preview storage is unavailable; Catalog reconciliation completed without derived work.");
        var scheduled = activeScheduler.TrySchedule(result, priority, derivedWorkCancellationToken);
        return scheduled.Accepted
            ? new(result, scheduled.Batch)
            : new(result, null, scheduled.Diagnostic ??
                "Preview work was not scheduled because storage lifecycle is changing. Refresh again later.");
    }
}
