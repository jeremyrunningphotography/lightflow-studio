using System.IO;

namespace LightflowStudio;

internal enum JobsWorkspaceFilter { All, Active, Waiting, Paused, NeedsAttention, Failed, Completed, Cancelled }

internal sealed record JobsWorkspaceItem(
    Guid JobId, Guid? HistoryRecordId, EncodingJobHistoryRecord? HistoryRecord, bool SchedulerOwned, bool IsLegacyProjection,
    string Name, string Capability, JobState State, double? Progress, string Timing, string SourcePath,
    string OutputPath, string Issue, string Details, DateTimeOffset SortTime, long QueueOrder)
{
    public string StateText => JobsPresentation.StateText(State);
    public bool IsCurrent => SchedulerOwned;
    public bool CanPause => IsCurrent && State == JobState.Queued;
    public bool CanResume => IsCurrent && State == JobState.Paused;
    public bool CanRetry => IsCurrent && State == JobState.NeedsAttention;
    public bool CanCancel => IsCurrent && State is JobState.Queued or JobState.Running or JobState.Paused or JobState.NeedsAttention;
    public bool CanReorder => IsCurrent && State == JobState.Queued;
    public bool CanReviewAndRerun => HistoryRecord is not null;
    public bool CanRemoveHistory => HistoryRecordId is not null;
    public string LegacyNote => IsLegacyProjection ? "Legacy History record · record-level rerun and removal" : "";
}

internal sealed record JobsSelectionEligibility(
    IReadOnlyList<JobsWorkspaceItem> Items, bool CanPause, bool CanResume, bool CanCancel, bool CanClearHistory)
{
    public bool IsSingle => Items.Count == 1;

    public static JobsSelectionEligibility For(IEnumerable<JobsWorkspaceItem> selected)
    {
        var items = selected.ToList();
        return new(items,
            items.Count > 0 && items.All(item => item.CanPause),
            items.Count > 0 && items.All(item => item.CanResume),
            items.Count > 0 && items.All(item => item.CanCancel),
            items.Count > 0 && items.All(item => item.CanRemoveHistory));
    }
}

internal static class JobsWorkspacePresentation
{
    public static IReadOnlyList<JobsWorkspaceItem> Project(IReadOnlyList<ExportJobSnapshot> current,
        IReadOnlyList<EncodingJobHistoryRecord> history, string? search = null,
        JobsWorkspaceFilter filter = JobsWorkspaceFilter.All)
    {
        var historyById = history.ToDictionary(record => record.JobId);
        var currentIds = current.Select(job => job.JobId).ToHashSet();
        var items = current.Select(job => FromCurrent(job, historyById.GetValueOrDefault(job.JobId))).Concat(history
            .Where(record => !currentIds.Contains(record.JobId))
            .SelectMany(FromHistory));
        if (!string.IsNullOrWhiteSpace(search))
        {
            var value = search.Trim();
            items = items.Where(item => item.Name.Contains(value, StringComparison.OrdinalIgnoreCase)
                || item.SourcePath.Contains(value, StringComparison.OrdinalIgnoreCase)
                || item.OutputPath.Contains(value, StringComparison.OrdinalIgnoreCase)
                || item.StateText.Contains(value, StringComparison.OrdinalIgnoreCase));
        }
        items = items.Where(item => Matches(item.State, filter));
        return items.OrderBy(item => JobsPresentation.IsTerminal(item.State) ? 1 : 0)
            .ThenBy(item => item.State == JobState.Queued ? item.QueueOrder : long.MinValue)
            .ThenBy(item => item.IsCurrent ? 0 : 1).ThenByDescending(item => item.SortTime).ToList();
    }

    public static IReadOnlySet<Guid> BackingHistoryRecordIds(IEnumerable<JobsWorkspaceItem> items) =>
        items.Where(item => item.HistoryRecordId is not null).Select(item => item.HistoryRecordId!.Value).ToHashSet();

    public static IReadOnlySet<Guid> SurvivingSelection(IEnumerable<Guid> selectedJobIds,
        IEnumerable<JobsWorkspaceItem> visibleItems)
    {
        var selected = selectedJobIds.ToHashSet();
        return visibleItems.Where(item => selected.Contains(item.JobId)).Select(item => item.JobId).ToHashSet();
    }

    public static string RemovalScope(IReadOnlyList<EncodingJobHistoryRecord> records)
    {
        if (records.Count == 0) return "No durable terminal History records are selected.";
        var oldest = records.Min(record => record.CompletedAt).ToLocalTime();
        var newest = records.Max(record => record.CompletedAt).ToLocalTime();
        var states = string.Join(", ", records.GroupBy(record => JobsPresentation.StateText(record.State))
            .OrderBy(group => group.Key).Select(group => $"{group.Count()} {group.Key.ToLowerInvariant()}"));
        return $"Permanently remove {records.Count} History record{(records.Count == 1 ? "" : "s")} ({states}) from {oldest:g} through {newest:g}?";
    }

    private static JobsWorkspaceItem FromCurrent(ExportJobSnapshot job, EncodingJobHistoryRecord? history)
    {
        var source = job.Definition.PlanItem.Definition.SourceIdentity;
        var details = new List<string> { $"Job: {job.JobId}", $"State: {JobsPresentation.StateText(job.State)}",
            $"Queued: {job.Definition.AcceptedAt.ToLocalTime():g}", $"Started: {(job.StartedAt is null ? "Not started" : job.StartedAt.Value.ToLocalTime().ToString("g"))}",
            $"Source: {source}", $"Output: {job.OutputPath}" };
        AppendMaterialized(details, job.Definition.PlanItem.Definition);
        details.AddRange(job.Warnings.Select(value => $"Warning: {value}"));
        details.AddRange(job.Errors.Select(value => $"Error: {value}"));
        return new(job.JobId, history?.JobId, history, true, false, job.DisplayName, "Export", job.State, job.ProgressPercent,
            job.State == JobState.Running && job.Eta is { } eta ? $"ETA {eta:hh\\:mm\\:ss}" : job.Definition.AcceptedAt.ToLocalTime().ToString("g"),
            source, job.OutputPath, job.Errors.FirstOrDefault() ?? job.Warnings.FirstOrDefault() ?? "",
            string.Join(Environment.NewLine, details), job.StartedAt ?? job.Definition.AcceptedAt, job.QueueOrder);
    }

    private static IEnumerable<JobsWorkspaceItem> FromHistory(EncodingJobHistoryRecord record)
    {
        var legacy = record.Plan.Items.Count != 1;
        foreach (var item in record.Plan.Items)
        {
            var result = record.Result.Items.FirstOrDefault(value => value.ItemId == item.Definition.Id);
            var output = result?.OutputPaths.FirstOrDefault() ?? item.OutputPaths.FirstOrDefault() ?? "";
            var state = result?.State ?? record.State;
            yield return new(item.Definition.Id, record.JobId, record, false, legacy,
                Path.GetFileName(output.Length == 0 ? item.Definition.SourceIdentity : output), "Export", state, 100,
                record.CompletedAt.ToLocalTime().ToString("g"), item.Definition.SourceIdentity, output,
                result?.Errors.FirstOrDefault() ?? result?.Warnings.FirstOrDefault() ?? "",
                record.DetailDisplay, record.CompletedAt, long.MaxValue);
        }
    }

    private static void AppendMaterialized(List<string> lines, JobItemDefinition item)
    {
        if (item.MediaRange is { IsFullSource: false } range)
            lines.Add($"Range: {range.EffectiveIn:c} – {range.EffectiveOut:c}");
        if (item.AssignedColor is { } color)
            lines.Add("Color: " + (color.OrderedPipeline.Count == 0 ? "Original" : string.Join(" → ", color.OrderedPipeline.Select(resource => resource.DisplayName))));
        if (item.MaterializedExport is { } export)
            lines.Add($"Export: {export.Encoding.Codec}, {export.Encoding.Container}, {export.Resolution}; audio {export.Audio.Mode}");
    }

    private static bool Matches(JobState state, JobsWorkspaceFilter filter) => filter switch
    {
        JobsWorkspaceFilter.All => true,
        JobsWorkspaceFilter.Active => state == JobState.Running,
        JobsWorkspaceFilter.Waiting => state == JobState.Queued,
        JobsWorkspaceFilter.Paused => state == JobState.Paused,
        JobsWorkspaceFilter.NeedsAttention => state == JobState.NeedsAttention,
        JobsWorkspaceFilter.Failed => state == JobState.Failed,
        JobsWorkspaceFilter.Completed => state is JobState.Completed or JobState.CompletedWithWarnings or JobState.Skipped,
        JobsWorkspaceFilter.Cancelled => state == JobState.Cancelled,
        _ => true
    };
}
