namespace LightflowStudio;

internal enum TimelinePointerTarget
{
    Track,
    PlayheadThumb,
    TrimMarker
}

internal static class TimelineSeek
{
    public static bool ShouldSeek(TimelinePointerTarget target) => target == TimelinePointerTarget.Track;

    public static TimeSpan PositionFromCoordinate(double x, double usableWidth, TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero || !double.IsFinite(usableWidth) || usableWidth <= 0) return TimeSpan.Zero;
        var boundedX = double.IsFinite(x) ? Math.Clamp(x, 0, usableWidth) : 0;
        return TimeSpan.FromTicks((long)Math.Round(duration.Ticks * boundedX / usableWidth));
    }
}
