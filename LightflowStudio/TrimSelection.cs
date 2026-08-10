namespace LightflowStudio;

internal sealed class TrimSelection
{
    private readonly MediaRange? _originalRange;

    public TrimSelection(TimeSpan sourceDuration, MediaRange? appliedRange = null)
    {
        if (sourceDuration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(sourceDuration));
        SourceDuration = sourceDuration;
        _originalRange = appliedRange;
        In = appliedRange?.EffectiveIn ?? TimeSpan.Zero;
        Out = appliedRange?.EffectiveOut ?? sourceDuration;
        if (!IsValid(In, Out)) Reset();
    }

    public TimeSpan SourceDuration { get; }
    public TimeSpan In { get; private set; }
    public TimeSpan Out { get; private set; }

    public bool SetIn(TimeSpan timestamp)
    {
        var candidate = Clamp(timestamp);
        if (!IsValid(candidate, Out)) return false;
        In = candidate;
        return true;
    }

    public bool SetOut(TimeSpan timestamp)
    {
        var candidate = Clamp(timestamp);
        if (!IsValid(In, candidate)) return false;
        Out = candidate;
        return true;
    }

    public void Reset()
    {
        In = TimeSpan.Zero;
        Out = SourceDuration;
    }

    public MediaRange? Apply()
    {
        if (!IsValid(In, Out)) throw new InvalidOperationException("The trim range must contain playable media.");
        if (In == TimeSpan.Zero && Out == SourceDuration) return null;
        return new(SourceDuration, In == TimeSpan.Zero ? null : In, Out == SourceDuration ? null : Out);
    }

    public MediaRange? Cancel() => _originalRange;

    private TimeSpan Clamp(TimeSpan value) => value < TimeSpan.Zero
        ? TimeSpan.Zero
        : value > SourceDuration ? SourceDuration : value;

    private bool IsValid(TimeSpan start, TimeSpan end) =>
        start >= TimeSpan.Zero && end <= SourceDuration && start < end;
}
