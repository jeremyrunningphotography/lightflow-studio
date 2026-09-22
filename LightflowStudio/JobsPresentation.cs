using System.IO;
using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;

namespace LightflowStudio;

internal static class JobsPresentation
{
    public const int MaximumRecentTerminalJobs = 8;

    public static bool IsTerminal(JobState state) => state is JobState.Completed or JobState.CompletedWithWarnings
        or JobState.Skipped or JobState.Cancelled or JobState.Failed;

    public static bool HasNonTerminalJobs(IEnumerable<ExportJobSnapshot> jobs) => jobs.Any(job => !IsTerminal(job.State));

    public static IReadOnlyList<ExportJobSnapshot> CancellableJobs(IEnumerable<ExportJobSnapshot> jobs) =>
        jobs.Where(job => !IsTerminal(job.State)).ToList();

    public static bool IsBulkActive(JobState state) => state is JobState.Queued or JobState.Running or JobState.Paused;

    public static IReadOnlyList<ExportJobSnapshot> BulkCancellableJobs(IEnumerable<ExportJobSnapshot> jobs)
    {
        var active = jobs.Where(job => IsBulkActive(job.State)).ToList();
        var cancellableIds = CancellableJobs(active).Select(job => job.JobId).ToHashSet();
        return active.Where(job => cancellableIds.Contains(job.JobId)).ToList();
    }

    public static bool IsDismissibleDrawerRow(JobState state) => IsTerminal(state);

    public static string StatusText(IEnumerable<ExportJobSnapshot> jobs, bool queuePaused = false)
    {
        var current = jobs.Where(job => !IsTerminal(job.State)).ToList();
        if (queuePaused)
        {
            var parts = new List<string> { "Jobs", "Queue paused" };
            var exportingWhileHeld = current.Count(job => job.State == JobState.Running);
            var waitingWhileHeld = current.Count(job => job.State == JobState.Queued);
            if (exportingWhileHeld > 0) parts.Add($"{exportingWhileHeld} exporting");
            if (waitingWhileHeld > 0) parts.Add($"{waitingWhileHeld} waiting");
            return string.Join(" · ", parts);
        }
        if (current.Count == 0) return "Jobs";
        var exporting = current.Count(job => job.State == JobState.Running);
        var waiting = current.Count(job => job.State == JobState.Queued);
        return exporting > 0 && waiting > 0 ? $"Jobs · {exporting} exporting · {waiting} waiting"
            : $"Jobs · {current.Count} active";
    }


    public static string StateText(JobState state) => state switch
    {
        JobState.Queued => "Waiting", JobState.Running => "Exporting", JobState.NeedsAttention => "Needs attention",
        JobState.CompletedWithWarnings => "Completed with warnings", JobState.Skipped => "Completed",
        _ => state.ToString()
    };

    public static string Glyph(JobState state) => state switch
    {
        JobState.Running => "◔", JobState.Queued => "○", JobState.Paused => "Ⅱ",
        JobState.Completed or JobState.CompletedWithWarnings or JobState.Skipped => "✓",
        JobState.Failed or JobState.NeedsAttention => "!", JobState.Cancelled => "×", _ => "○"
    };

    public static bool IsClearableFinished(JobState state) => state is JobState.Completed or JobState.CompletedWithWarnings
        or JobState.Skipped or JobState.Cancelled or JobState.Failed;

    public static IReadOnlyList<ExportJobSnapshot> VisibleJobs(IEnumerable<ExportJobSnapshot> jobs,
        IReadOnlySet<Guid>? dismissedTerminalJobIds = null)
    {
        var ordered = jobs.OrderBy(job => job.QueueOrder).ToList();
        var recentIds = ordered.Where(job => IsTerminal(job.State)).OrderByDescending(job => job.CompletedAt)
            .Take(MaximumRecentTerminalJobs).Select(job => job.JobId).ToHashSet();
        return ordered.Where(job => (!IsTerminal(job.State) || recentIds.Contains(job.JobId))
            && !(dismissedTerminalJobIds?.Contains(job.JobId) ?? false)).ToList();
    }

    public static JobCardPresentation Card(ExportJobSnapshot job, bool expanded, bool hasHistory = false)
    {
        var settings = job.Definition.PlanItem.Definition.MaterializedExport
            ?? EncodingJobPlanner.LegacySettings(job.Definition.Recipe, job.Definition.PlanItem.Definition);
        var encoding = settings.Encoding;
        var quality = encoding.RateControl switch
        {
            RateControlMode.ConstantQuality => $"Constant quality {encoding.Quality}",
            RateControlMode.VariableBitrate => $"Variable bitrate {encoding.TargetBitrateMbps}–{encoding.MaxBitrateMbps} Mbps",
            _ => $"Constant bitrate {encoding.TargetBitrateMbps} Mbps"
        };
        var color = settings.Color is { ColorEnabled: true } pipeline
            ? string.Join(" → ", pipeline.OrderedPipeline.Select(item => item.DisplayName)) : "Original";
        var audio = settings.Audio.Mode switch
        {
            MaterializedAudioMode.SourceCopyPreferred => "Copy source when compatible",
            MaterializedAudioMode.EncodedAac => $"AAC {settings.Audio.Fallback?.BitrateKbps ?? encoding.AudioBitrateKbps} kbps",
            _ => "None"
        };
        var frameRate = encoding.FrameRate > 0 ? $"{encoding.FrameRate:0.###} fps" : "Same as source";
        var issue = job.Errors.FirstOrDefault() ?? job.Warnings.FirstOrDefault();
        var details = new ExportJobDetailsPresentation(job.OutputPath,
            $"{EncodingPathPlanner.ResolutionName(settings.Resolution)} · {frameRate}",
            $"{encoding.Codec} · {encoding.Container}", quality, audio, color);
        return new(job.JobId, job.DisplayName, Glyph(job.State), StateText(job.State), job.ProgressPercent ?? 0,
            job.State == JobState.Running, FormatDuration(job.Elapsed), job.Eta is { } eta ? $"About {FormatDuration(eta)} remaining" : null,
            details, issue, expanded,
            JobActionState.For(job.State, queueControls: true, reviewAndRerun: hasHistory));
    }

    public static JobCardPresentation Card(FileOperationJobSnapshot job, bool expanded)
    {
        var state = job.State switch
        {
            FileOperationState.Waiting => "Waiting", FileOperationState.Running => job.Intent.Kind.ToString(),
            FileOperationState.CompletedWithFailures => "Completed with warnings", _ => job.State.ToString()
        };
        var progress = job.Intent.EstimatedBytes is > 0 ? Math.Clamp(job.CompletedBytes * 100d / job.Intent.EstimatedBytes.Value, 0, 100)
            : job.Intent.Sources.Count > 0 ? job.CompletedItems * 100d / job.Intent.Sources.Count : 0;
        var glyph = job.State switch { FileOperationState.Waiting => "○", FileOperationState.Running => "◔",
            FileOperationState.Completed => "✓", FileOperationState.Cancelled => "×", _ => "!" };
        var details = FileSystemDetails(job);
        var completedAt = job.Result?.CompletedUtc ?? DateTimeOffset.UtcNow;
        var elapsed = completedAt <= job.Intent.CreatedUtc ? TimeSpan.Zero : completedAt - job.Intent.CreatedUtc;
        return new(job.Intent.OperationId, $"{job.Intent.Kind} {job.Intent.Sources.Count} item{(job.Intent.Sources.Count == 1 ? "" : "s")}",
            glyph, state, progress, job.State == FileOperationState.Running, FormatDuration(elapsed), null, details,
            job.Failures.FirstOrDefault()?.Diagnostic, expanded, JobActionState.For(FileOperationStateToJobState(job.State)));
    }

    internal static JobState FileOperationStateToJobState(FileOperationState state) => state switch
    {
        FileOperationState.Waiting => JobState.Queued, FileOperationState.Running => JobState.Running,
        FileOperationState.Completed => JobState.Completed, FileOperationState.CompletedWithFailures => JobState.CompletedWithWarnings,
        FileOperationState.Cancelled => JobState.Cancelled, _ => JobState.Failed
    };

    public static FileSystemJobDetailsPresentation FileSystemDetails(FileOperationJobSnapshot job)
    {
        var total = job.Intent.Sources.Count;
        var noun = total == 1 ? "item" : "items";
        var source = total switch
        {
            0 => "Source details unavailable",
            1 => job.Intent.Sources[0].Path,
            _ => $"{total} selected items · first: {job.Intent.Sources[0].Path}"
        };
        var destination = job.Intent.Kind == FileOperationKind.Recycle ? "Windows Recycle Bin"
            : job.Intent.Destination ?? "Not applicable";
        var bytes = job.Intent.EstimatedBytes is { } estimated
            ? $"{job.CompletedBytes:N0} of {estimated:N0} bytes"
            : $"{job.CompletedBytes:N0} bytes processed";
        var result = job.State switch
        {
            FileOperationState.Waiting => "Waiting to start",
            FileOperationState.Running => $"{job.CompletedItems} of {total} {noun} completed",
            FileOperationState.Completed => $"Completed {job.CompletedItems} of {total} {noun}",
            FileOperationState.CompletedWithFailures => $"Completed {job.CompletedItems} of {total} {noun} with failures",
            FileOperationState.Cancelled => $"Cancelled after {job.CompletedItems} of {total} {noun}",
            FileOperationState.Interrupted => $"Interrupted after {job.CompletedItems} of {total} {noun}",
            _ => $"Failed after {job.CompletedItems} of {total} {noun}"
        };
        var failures = string.Join(Environment.NewLine, job.Failures.Select(failure =>
            $"{failure.Path} — {failure.Diagnostic}"));
        return new(job.Intent.Kind.ToString(), source, destination,
            $"{job.CompletedItems} of {total} {noun}", job.CurrentItem ?? "—", bytes, result, failures);
    }

    public static void Reconcile(ObservableCollection<JobCardPresentation> cards,
        IReadOnlyList<JobCardPresentation> desired)
    {
        if (cards.Count == desired.Count && desired.Select((card, index) =>
            card.JobId == cards[index].JobId).All(matches => matches))
        {
            for (var index = 0; index < desired.Count; index++) cards[index].Apply(desired[index]);
            return;
        }
        var desiredIds = desired.Select(card => card.JobId).ToHashSet();
        for (var index = cards.Count - 1; index >= 0; index--)
            if (!desiredIds.Contains(cards[index].JobId)) cards.RemoveAt(index);
        for (var index = 0; index < desired.Count; index++)
        {
            var existingIndex = -1;
            for (var candidate = 0; candidate < cards.Count; candidate++)
                if (cards[candidate].JobId == desired[index].JobId) { existingIndex = candidate; break; }
            if (existingIndex < 0) cards.Insert(index, desired[index]);
            else
            {
                var existing = cards[existingIndex];
                existing.Apply(desired[index]);
                if (existingIndex != index) cards.Move(existingIndex, index);
            }
        }
    }

    private static string FormatDuration(TimeSpan value) => value.TotalHours >= 1 ? value.ToString(@"h\:mm\:ss") : value.ToString(@"m\:ss");
}

internal abstract record JobDetailsPresentation;
internal sealed record ExportJobDetailsPresentation(string OutputPath, string Video, string Format,
    string Quality, string Audio, string Color) : JobDetailsPresentation;
internal sealed record FileSystemJobDetailsPresentation(string Operation, string SourceSummary, string Destination,
    string ItemProgress, string CurrentItem, string ByteProgress, string ResultSummary,
    string FailureSummary) : JobDetailsPresentation;
internal sealed record JobMessageDetailsPresentation(string Text, string? Success = null) : JobDetailsPresentation;
internal sealed record PremiereJobItemDetailsPresentation(string Name, string Status, string Detail,
    PremiereJobItemState State);
internal sealed record PremiereJobDetailsPresentation(string Project, string Progress,
    IReadOnlyList<PremiereJobItemDetailsPresentation> Items) : JobDetailsPresentation;

internal sealed class JobCardPresentation(Guid jobId, string name, string glyph, string state, double progress,
    bool showProgress, string elapsed, string? eta, JobDetailsPresentation details, string? issue, bool isExpanded,
    JobActionState actions) : INotifyPropertyChanged
{
    public Guid JobId { get; } = jobId;
    public string Name { get; private set; } = name;
    public string Glyph { get; private set; } = glyph;
    public string State { get; private set; } = state;
    public double Progress { get; private set; } = progress;
    public bool ShowProgress { get; private set; } = showProgress;
    public string Elapsed { get; private set; } = elapsed;
    public string? Eta { get; private set; } = eta;
    public JobDetailsPresentation Details { get; private set; } = details;
    public string? Issue { get; private set; } = issue;
    public bool IsExpanded { get; private set; } = isExpanded;
    public JobActionState Actions { get; private set; } = actions;
    public bool CanPause => Actions.CanPause;
    public bool CanResume => Actions.CanResume;
    public bool CanRetry => Actions.CanRetry;
    public bool CanCancel => Actions.CanCancel;
    public bool CanBulkCancel => Actions.CanBulkCancel;
    public bool CanReorder => Actions.CanReorder;
    public bool CanClear => Actions.CanClear;

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Apply(JobCardPresentation value)
    {
        if (value.JobId != JobId) throw new ArgumentException("A Job card can only be updated from the same JobId.", nameof(value));
        Set(Name, value.Name, next => Name = next); Set(Glyph, value.Glyph, next => Glyph = next);
        Set(State, value.State, next => State = next); Set(Progress, value.Progress, next => Progress = next);
        Set(ShowProgress, value.ShowProgress, next => ShowProgress = next); Set(Elapsed, value.Elapsed, next => Elapsed = next);
        Set(Eta, value.Eta, next => Eta = next); Set(Details, value.Details, next => Details = next);
        Set(Issue, value.Issue, next => Issue = next);
        SetExpanded(value.IsExpanded);
        if (Actions != value.Actions)
        {
            Actions = value.Actions;
            foreach (var property in new[] { nameof(Actions), nameof(CanPause), nameof(CanResume), nameof(CanRetry),
                         nameof(CanCancel), nameof(CanBulkCancel), nameof(CanReorder), nameof(CanClear) })
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
        }
    }

    public void SetExpanded(bool expanded) => Set(IsExpanded, expanded, next => IsExpanded = next);

    private void Set<T>(T current, T value, Action<T> assign,
        [CallerArgumentExpression(nameof(current))] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(current, value)) return;
        assign(value);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
