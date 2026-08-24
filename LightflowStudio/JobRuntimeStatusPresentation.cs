namespace LightflowStudio;

internal static class JobRuntimeStatusPresentation
{
    public static string Describe<TData>(JobRuntimeSnapshot<TData> snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return Describe(snapshot.State, snapshot.Counts);
    }

    internal static string Describe(JobState state, JobRuntimeCounts counts)
    {
        var activity = state switch
        {
            JobState.Pausing => $"{counts.Running} exporting (draining; no new exports will start)",
            JobState.Cancelling => $"{counts.Running} exporting (cancelling)",
            _ => $"{counts.Running} exporting"
        };
        var parts = new List<string>
        {
            activity,
            $"{counts.Waiting} waiting",
            $"{counts.Completed} complete"
        };
        if (counts.Skipped > 0) parts.Add($"{counts.Skipped} skipped");
        if (counts.Failed > 0) parts.Add($"{counts.Failed} failed");
        if (counts.Cancelled > 0) parts.Add($"{counts.Cancelled} cancelled");
        parts.Add(state switch
        {
            JobState.Planned => "planned",
            JobState.Queued => "queued",
            JobState.Running => "active",
            JobState.Pausing => "pausing",
            JobState.Paused => "paused",
            JobState.Cancelling => "cancelling",
            JobState.Completed => "completed",
            JobState.CompletedWithWarnings => "completed with warnings",
            JobState.Failed => "failed",
            JobState.Cancelled => "cancelled",
            JobState.Skipped => "skipped",
            JobState.NeedsAttention => "needs attention",
            _ => state.ToString().ToLowerInvariant()
        });
        return string.Join(" • ", parts);
    }
}
