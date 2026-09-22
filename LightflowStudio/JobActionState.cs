namespace LightflowStudio;

// Eligibility is operation-specific. Capability adapters supply contracts; lifecycle supplies safety.
internal sealed record JobActionState(bool CanPause, bool CanResume, bool CanRetry,
    bool CanCancel, bool CanBulkCancel, bool CanReorder, bool CanClear, bool CanReviewAndRerun)
{
    public static JobActionState For(JobState state, bool current = true, bool queueControls = false,
        bool retry = false, bool reviewAndRerun = false, bool removable = true) => new(
        current && queueControls && state == JobState.Queued,
        current && queueControls && state == JobState.Paused,
        current && (retry && JobsPresentation.IsTerminal(state) || queueControls && state == JobState.NeedsAttention),
        current && state is JobState.Queued or JobState.Running or JobState.Pausing or JobState.Paused or JobState.NeedsAttention,
        current && JobsPresentation.IsBulkActive(state),
        current && queueControls && state == JobState.Queued,
        removable && JobsPresentation.IsTerminal(state),
        reviewAndRerun && JobsPresentation.IsTerminal(state));
}

internal enum JobRemovalKind { Session, ExportHistory, FileOperationHistory, RetainedProvenance }

internal sealed record JobsQueueActionState(bool IsPaused, bool CanToggle)
{
    public static JobsQueueActionState For(bool paused, bool hasWork) => new(paused, paused || hasWork);
    public static JobsQueueActionState For(bool paused, IEnumerable<ExportJobSnapshot> jobs) =>
        new(paused, paused || jobs.Any(job => job.State is JobState.Queued or JobState.Running));
}
