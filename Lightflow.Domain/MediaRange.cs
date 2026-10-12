namespace LightflowStudio;

public enum JobIssueSeverity
{
    Warning,
    Error
}

public sealed record JobIssue(string Code, string Message, JobIssueSeverity Severity);

public sealed record MediaRange(TimeSpan SourceDuration, TimeSpan? In = null, TimeSpan? Out = null)
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
