using System.IO;

namespace LightflowStudio;

internal static class JobsPresentation
{
    public const int MaximumRecentTerminalJobs = 8;

    public static bool IsTerminal(JobState state) => state is JobState.Completed or JobState.CompletedWithWarnings
        or JobState.Skipped or JobState.Cancelled or JobState.Failed;

    public static bool HasNonTerminalJobs(IEnumerable<ExportJobSnapshot> jobs) => jobs.Any(job => !IsTerminal(job.State));

    public static IReadOnlyList<ExportJobSnapshot> CancellableJobs(IEnumerable<ExportJobSnapshot> jobs) =>
        jobs.Where(job => !IsTerminal(job.State)).ToList();

    public static string StatusText(IEnumerable<ExportJobSnapshot> jobs)
    {
        var current = jobs.Where(job => !IsTerminal(job.State)).ToList();
        if (current.Count == 0) return "Jobs";
        var exporting = current.Count(job => job.State == JobState.Running);
        var waiting = current.Count(job => job.State == JobState.Queued);
        return exporting > 0 && waiting > 0 ? $"Jobs · {exporting} exporting · {waiting} waiting"
            : $"Jobs · {current.Count} active";
    }

    public static JobsRoute Route(IEnumerable<ExportJobSnapshot> jobs) =>
        HasNonTerminalJobs(jobs) ? JobsRoute.Drawer : JobsRoute.HistoryCompatibility;

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

    public static IReadOnlyList<ExportJobSnapshot> VisibleJobs(IEnumerable<ExportJobSnapshot> jobs)
    {
        var ordered = jobs.OrderBy(job => job.QueueOrder).ToList();
        var recentIds = ordered.Where(job => IsTerminal(job.State)).OrderByDescending(job => job.CompletedAt)
            .Take(MaximumRecentTerminalJobs).Select(job => job.JobId).ToHashSet();
        return ordered.Where(job => !IsTerminal(job.State) || recentIds.Contains(job.JobId)).ToList();
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

    private static string FormatDuration(TimeSpan value) => value.TotalHours >= 1 ? value.ToString(@"h\:mm\:ss") : value.ToString(@"m\:ss");
}

internal enum JobsRoute { Drawer, HistoryCompatibility }

internal sealed record JobCardPresentation(Guid JobId, string Name, string Glyph, string State, double Progress,
    bool ShowProgress, string Elapsed, string? Eta, string OutputPath, string ResolutionAndFrameRate,
    string CodecAndContainer, string Quality, string Audio, string Color, string? Issue, bool IsExpanded,
    bool CanPause, bool CanResume, bool CanRetry, bool CanCancel, bool CanReorder);
