namespace LightflowStudio;

internal enum JobState
{
    Planned,
    Queued,
    Running,
    Pausing,
    Paused,
    Cancelling,
    NeedsAttention,
    Completed,
    CompletedWithWarnings,
    Skipped,
    Cancelled,
    Failed
}

internal enum JobIssueSeverity
{
    Warning,
    Error
}

internal enum JobWorkUnit
{
    Items,
    Bytes,
    MediaDuration
}

internal enum JobPlanDisposition
{
    Process,
    Skip
}

internal sealed record JobIssue(string Code, string Message, JobIssueSeverity Severity);

internal sealed record JobWorkEstimate(JobWorkUnit Unit, double? Value)
{
    public bool IsDeterminate => Value is > 0;

    public static JobWorkEstimate Determinate(JobWorkUnit unit, double value)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
        return new(unit, value);
    }

    public static JobWorkEstimate Indeterminate(JobWorkUnit unit) => new(unit, null);
}

internal sealed record MediaRange(TimeSpan SourceDuration, TimeSpan? In = null, TimeSpan? Out = null)
{
    public TimeSpan EffectiveIn => In ?? TimeSpan.Zero;
    public TimeSpan EffectiveOut => Out ?? SourceDuration;
    public TimeSpan EffectiveDuration => EffectiveOut - EffectiveIn;
    public bool IsFullSource => In is null && Out is null;

    public IReadOnlyList<JobIssue> Validate()
    {
        var issues = new List<JobIssue>();
        if (SourceDuration <= TimeSpan.Zero)
            issues.Add(new("media.source-duration", "Source duration must be greater than zero.", JobIssueSeverity.Error));
        if (EffectiveIn < TimeSpan.Zero)
            issues.Add(new("media.in-before-start", "The media range cannot begin before the source.", JobIssueSeverity.Error));
        if (EffectiveOut > SourceDuration)
            issues.Add(new("media.out-after-end", "The media range cannot end after the source.", JobIssueSeverity.Error));
        if (EffectiveDuration <= TimeSpan.Zero)
            issues.Add(new("media.empty-range", "The media range must contain positive work.", JobIssueSeverity.Error));
        return issues;
    }
}

internal sealed record ResolvedMediaRange(
    MediaRange RequestedRange,
    TimeSpan SourceStartTimestamp,
    TimeSpan AbsoluteIn,
    TimeSpan ExclusiveOut,
    TimeSpan EffectiveDuration)
{
    public IReadOnlyList<JobIssue> Validate()
    {
        var issues = RequestedRange.Validate().ToList();
        if (AbsoluteIn < SourceStartTimestamp)
            issues.Add(new("media.absolute-in-before-start", "The resolved trim begins before the source timeline.", JobIssueSeverity.Error));
        if (ExclusiveOut <= AbsoluteIn || EffectiveDuration <= TimeSpan.Zero)
            issues.Add(new("media.empty-resolved-range", "The resolved trim must contain at least one complete decoded frame.", JobIssueSeverity.Error));
        return issues;
    }
}

internal sealed record JobItemDefinition(
    Guid Id,
    string SourceIdentity,
    long? SourceSizeBytes = null,
    MediaRange? MediaRange = null,
    ResolvedMediaRange? ResolvedRange = null,
    long? SourceLastWriteUtcTicks = null,
    bool? SourceHasAudio = null,
    MaterializedColorPipeline? AssignedColor = null,
    MaterializedExportSettings? MaterializedExport = null);

internal sealed record JobDefinition<TOptions>(
    Guid Id,
    string Capability,
    DateTimeOffset CreatedAt,
    TOptions Options,
    IReadOnlyList<JobItemDefinition> Items);

internal sealed record JobPlanItem(
    JobItemDefinition Definition,
    IReadOnlyList<string> OutputPaths,
    JobPlanDisposition Disposition,
    JobWorkEstimate WorkEstimate,
    IReadOnlyList<JobIssue> Issues)
{
    public bool IsValid => Issues.All(issue => issue.Severity != JobIssueSeverity.Error);
}

internal sealed record JobPlan<TOptions>(
    JobDefinition<TOptions> Definition,
    DateTimeOffset PlannedAt,
    IReadOnlyList<JobPlanItem> Items,
    IReadOnlyList<JobIssue> Issues,
    JobWorkUnit WorkUnit)
{
    public bool IsValid => Issues.All(issue => issue.Severity != JobIssueSeverity.Error)
                           && Items.All(item => item.IsValid);
    public IReadOnlyList<JobPlanItem> ExecutableItems => Items
        .Where(item => item.Disposition == JobPlanDisposition.Process && item.IsValid)
        .ToList();
}

internal sealed record JobItemResult<TData>(
    Guid ItemId,
    JobState State,
    IReadOnlyList<string> OutputPaths,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors,
    TData? Data = default);

internal sealed record JobResultSummary(
    int Total,
    int Completed,
    int CompletedWithWarnings,
    int Skipped,
    int Cancelled,
    int Failed);

internal sealed record JobResult<TData>(
    Guid JobId,
    JobState State,
    DateTimeOffset? StartedAt,
    DateTimeOffset CompletedAt,
    IReadOnlyList<JobItemResult<TData>> Items,
    JobResultSummary Summary,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors);

internal sealed record JobProgressSnapshot(
    double? OverallPercent,
    double? CurrentItemPercent,
    double CompletedWork,
    double? TotalWork,
    JobWorkUnit WorkUnit);

internal sealed record JobItemRuntimeSnapshot<TData>(
    Guid ItemId,
    int Order,
    JobState State,
    double? ProgressPercent,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors,
    TData? Data);

internal sealed record JobRuntimeCounts(
    int Total, int Waiting, int Running, int Completed, int Failed, int Cancelled, int Skipped);

internal sealed record JobRuntimeSnapshot<TData>(
    Guid JobId,
    JobState State,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    TimeSpan Elapsed,
    TimeSpan? Eta,
    JobProgressSnapshot Progress,
    JobRuntimeCounts Counts,
    IReadOnlyList<JobItemRuntimeSnapshot<TData>> Items,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors);
