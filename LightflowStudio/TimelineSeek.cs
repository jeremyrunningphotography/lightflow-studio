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

/// <summary>Maps a click coordinate to a bounded numeric slider value.</summary>
internal static class SliderClickToSet
{
    public static double ValueFromCoordinate(double x, double usableWidth, double minimum, double maximum)
    {
        if (maximum <= minimum || !double.IsFinite(usableWidth) || usableWidth <= 0) return minimum;
        var boundedX = double.IsFinite(x) ? Math.Clamp(x, 0, usableWidth) : 0;
        return minimum + (maximum - minimum) * boundedX / usableWidth;
    }
}
