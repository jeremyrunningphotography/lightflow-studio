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
    public bool CanRemoveHistory => HistoryRecordId is not null && (!SchedulerOwned || JobsPresentation.IsTerminal(State));
    public string LegacyNote => IsLegacyProjection ? "Older Jobs saved together · group-level Review & Rerun and removal" : "";
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
    public static IReadOnlyList<JobsWorkspaceItem> ProjectFileOperations(
        IReadOnlyList<FileOperationJobSnapshot> current, IReadOnlyList<FileOperationHistoryRecord> history,
        string? search = null, JobsWorkspaceFilter filter = JobsWorkspaceFilter.All)
    {
        var currentIds = current.Select(job => job.Intent.OperationId).ToHashSet();
        var items = current.Select(FromFileOperation).Concat(history.Where(record => !currentIds.Contains(record.Intent.OperationId))
            .Select(record => FromFileOperation(new(record.Intent, record.Result.State, record.Result.CompletedItems,
                record.Result.CompletedBytes, null, record.Result.Failures, record.Result))));
        if (!string.IsNullOrWhiteSpace(search))
        {
            var value = search.Trim();
            items = items.Where(item => item.Name.Contains(value, StringComparison.OrdinalIgnoreCase) ||
                item.SourcePath.Contains(value, StringComparison.OrdinalIgnoreCase) || item.OutputPath.Contains(value, StringComparison.OrdinalIgnoreCase));
        }
        return items.Where(item => Matches(item.State, filter)).ToArray();
    }

    private static JobsWorkspaceItem FromFileOperation(FileOperationJobSnapshot job)
    {
        var state = job.State switch { FileOperationState.Waiting => JobState.Queued, FileOperationState.Running => JobState.Running,
            FileOperationState.Completed => JobState.Completed, FileOperationState.CompletedWithFailures => JobState.CompletedWithWarnings,
            FileOperationState.Cancelled => JobState.Cancelled, _ => JobState.Failed };
        var progress = job.Intent.EstimatedBytes is > 0 ? job.CompletedBytes * 100d / job.Intent.EstimatedBytes.Value :
            job.Intent.Sources.Count > 0 ? job.CompletedItems * 100d / job.Intent.Sources.Count : 0;
        var detail = string.Join(Environment.NewLine, new[] { $"Job: {job.Intent.OperationId}", $"Operation: {job.Intent.Kind}",
            $"Items: {job.CompletedItems} of {job.Intent.Sources.Count}", $"Bytes: {job.CompletedBytes:N0}",
            $"Destination: {job.Intent.Destination}" }.Concat(job.Failures.Select(failure => $"Failed: {failure.Path} — {failure.Diagnostic}")));
        return new(job.Intent.OperationId, null, null, job.Result is null, false,
            $"{job.Intent.Kind} {job.Intent.Sources.Count} item(s)", "File operation", state, progress,
            job.Result?.CompletedUtc.ToLocalTime().ToString("MMM d, HH:mm") ?? "Active",
            job.Intent.Sources.FirstOrDefault()?.Path ?? "", job.Intent.Destination ?? "",
            job.Failures.FirstOrDefault()?.Diagnostic ?? "", detail, job.Result?.CompletedUtc ?? job.Intent.CreatedUtc, long.MaxValue);
    }
    public static IReadOnlyList<JobsWorkspaceItem> Project(IReadOnlyList<ExportJobSnapshot> current,
        IReadOnlyList<EncodingJobHistoryRecord> history, string? search = null,
        JobsWorkspaceFilter filter = JobsWorkspaceFilter.All,
        IReadOnlySet<Guid>? suppressedTerminalJobIds = null)
    {
        var historyById = history.ToDictionary(record => record.JobId);
        var currentIds = current.Select(job => job.JobId).ToHashSet();
        var items = current.Where(job => suppressedTerminalJobIds?.Contains(job.JobId) != true || !JobsPresentation.IsTerminal(job.State))
            .Select(job => FromCurrent(job, historyById.GetValueOrDefault(job.JobId))).Concat(history
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

    public static IReadOnlySet<Guid> TerminalSchedulerJobIdsForDeletedHistory(IEnumerable<JobsWorkspaceItem> items,
        IReadOnlySet<Guid> deletedHistoryRecordIds) => items.Where(item => item.SchedulerOwned
            && JobsPresentation.IsTerminal(item.State)
            && item.HistoryRecordId is { } historyId && deletedHistoryRecordIds.Contains(historyId))
        .Select(item => item.JobId).ToHashSet();

    public static IReadOnlySet<Guid> SurvivingSelection(IEnumerable<Guid> selectedJobIds,
        IEnumerable<JobsWorkspaceItem> visibleItems)
    {
        var selected = selectedJobIds.ToHashSet();
        return visibleItems.Where(item => selected.Contains(item.JobId)).Select(item => item.JobId).ToHashSet();
    }

    public static string RemovalScope(IReadOnlyList<EncodingJobHistoryRecord> records)
    {
        if (records.Count == 0) return "No saved Jobs are selected.";
        var oldest = records.Min(record => record.CompletedAt).ToLocalTime();
        var newest = records.Max(record => record.CompletedAt).ToLocalTime();
        var jobStates = records.SelectMany(record => record.Plan.Items.Select(item =>
            record.Result.Items.FirstOrDefault(result => result.ItemId == item.Definition.Id)?.State ?? record.State));
        var states = string.Join(", ", jobStates.GroupBy(JobsPresentation.StateText)
            .OrderBy(group => group.Key).Select(group => $"{group.Count()} {group.Key.ToLowerInvariant()}"));
        var jobs = records.Sum(record => record.Plan.Items.Count);
        return $"Permanently delete {jobs} saved Job{(jobs == 1 ? "" : "s")} ({states}) from {oldest:g} through {newest:g}?";
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
            job.State == JobState.Running && job.Eta is { } eta ? $"ETA {eta:hh\\:mm\\:ss}" : CompactTimestamp(job.Definition.AcceptedAt),
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
                CompactTimestamp(record.CompletedAt), item.Definition.SourceIdentity, output,
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

    private static string CompactTimestamp(DateTimeOffset value) => value.ToLocalTime().ToString("MMM d, HH:mm");

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
