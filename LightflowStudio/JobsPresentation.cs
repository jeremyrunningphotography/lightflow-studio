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

    public static bool IsDismissibleDrawerRow(JobState state) => IsTerminal(state) || state == JobState.NeedsAttention;

    public static JobsBulkAction BulkAction(IEnumerable<ExportJobSnapshot> visibleJobs)
    {
        var jobs = visibleJobs.ToList();
        if (BulkCancellableJobs(jobs).Count > 0) return JobsBulkAction.CancelAll;
        return jobs.Any(job => IsDismissibleDrawerRow(job.State)) ? JobsBulkAction.ClearAll : JobsBulkAction.None;
    }

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

    public static JobCardPresentation Card(ExportJobSnapshot job, bool expanded)
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
        return new(job.JobId, job.DisplayName, Glyph(job.State), StateText(job.State), job.ProgressPercent ?? 0,
            job.State == JobState.Running, FormatDuration(job.Elapsed), job.Eta is { } eta ? $"About {FormatDuration(eta)} remaining" : null,
            job.OutputPath, $"{EncodingPathPlanner.ResolutionName(settings.Resolution)} · {frameRate}",
            $"{encoding.Codec} · {encoding.Container}", quality, audio, color, issue, expanded,
            job.State == JobState.Queued, job.State == JobState.Paused, job.State == JobState.NeedsAttention,
            !IsTerminal(job.State), job.State == JobState.Queued);
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
        return new(job.Intent.OperationId, $"{job.Intent.Kind} {job.Intent.Sources.Count} item{(job.Intent.Sources.Count == 1 ? "" : "s")}",
            glyph, state, progress, job.State == FileOperationState.Running, "", null, job.Intent.Destination ?? "",
            job.CurrentItem is null ? "Filesystem operation" : Path.GetFileName(job.CurrentItem),
            $"{job.CompletedItems} of {job.Intent.Sources.Count} items", $"{job.CompletedBytes:N0} bytes", "", "",
            job.Failures.FirstOrDefault()?.Diagnostic, expanded, false, false, false,
            job.State is FileOperationState.Waiting or FileOperationState.Running, false);
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

internal enum JobsBulkAction { None, CancelAll, ClearAll }

internal sealed class JobCardPresentation(Guid jobId, string name, string glyph, string state, double progress,
    bool showProgress, string elapsed, string? eta, string outputPath, string resolutionAndFrameRate,
    string codecAndContainer, string quality, string audio, string color, string? issue, bool isExpanded,
    bool canPause, bool canResume, bool canRetry, bool canCancel, bool canReorder) : INotifyPropertyChanged
{
    public Guid JobId { get; } = jobId;
    public string Name { get; private set; } = name;
    public string Glyph { get; private set; } = glyph;
    public string State { get; private set; } = state;
    public double Progress { get; private set; } = progress;
    public bool ShowProgress { get; private set; } = showProgress;
    public string Elapsed { get; private set; } = elapsed;
    public string? Eta { get; private set; } = eta;
    public string OutputPath { get; private set; } = outputPath;
    public string ResolutionAndFrameRate { get; private set; } = resolutionAndFrameRate;
    public string CodecAndContainer { get; private set; } = codecAndContainer;
    public string Quality { get; private set; } = quality;
    public string Audio { get; private set; } = audio;
    public string Color { get; private set; } = color;
    public string? Issue { get; private set; } = issue;
    public bool IsExpanded { get; private set; } = isExpanded;
    public bool CanPause { get; private set; } = canPause;
    public bool CanResume { get; private set; } = canResume;
    public bool CanRetry { get; private set; } = canRetry;
    public bool CanCancel { get; private set; } = canCancel;
    public bool CanReorder { get; private set; } = canReorder;

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Apply(JobCardPresentation value)
    {
        if (value.JobId != JobId) throw new ArgumentException("A Job card can only be updated from the same JobId.", nameof(value));
        Set(Name, value.Name, next => Name = next); Set(Glyph, value.Glyph, next => Glyph = next);
        Set(State, value.State, next => State = next); Set(Progress, value.Progress, next => Progress = next);
        Set(ShowProgress, value.ShowProgress, next => ShowProgress = next); Set(Elapsed, value.Elapsed, next => Elapsed = next);
        Set(Eta, value.Eta, next => Eta = next); Set(OutputPath, value.OutputPath, next => OutputPath = next);
        Set(ResolutionAndFrameRate, value.ResolutionAndFrameRate, next => ResolutionAndFrameRate = next);
        Set(CodecAndContainer, value.CodecAndContainer, next => CodecAndContainer = next);
        Set(Quality, value.Quality, next => Quality = next); Set(Audio, value.Audio, next => Audio = next);
        Set(Color, value.Color, next => Color = next); Set(Issue, value.Issue, next => Issue = next);
        SetExpanded(value.IsExpanded); Set(CanPause, value.CanPause, next => CanPause = next);
        Set(CanResume, value.CanResume, next => CanResume = next); Set(CanRetry, value.CanRetry, next => CanRetry = next);
        Set(CanCancel, value.CanCancel, next => CanCancel = next); Set(CanReorder, value.CanReorder, next => CanReorder = next);
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
