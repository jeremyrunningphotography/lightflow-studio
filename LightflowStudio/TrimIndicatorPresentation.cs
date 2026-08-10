namespace LightflowStudio;

internal sealed record TrimIndicatorPresentation(bool HasActiveTrim, bool HasProportions, double StartFraction, double WidthFraction)
{
    public static TrimIndicatorPresentation For(MediaRange? range, TimeSpan? knownDuration)
    {
        if (range is null) return new(false, knownDuration > TimeSpan.Zero, 0, 1);
        var duration = knownDuration > TimeSpan.Zero ? knownDuration.Value : range.SourceDuration;
        if (duration <= TimeSpan.Zero || range.EffectiveIn < TimeSpan.Zero || range.EffectiveOut > duration || range.EffectiveDuration <= TimeSpan.Zero)
            return new(true, false, 0, 0);
        return new(true, true,
            Math.Clamp(range.EffectiveIn.TotalSeconds / duration.TotalSeconds, 0, 1),
            Math.Clamp(range.EffectiveDuration.TotalSeconds / duration.TotalSeconds, 0, 1));
    }
}
