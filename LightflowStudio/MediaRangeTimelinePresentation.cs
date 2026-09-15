namespace LightflowStudio;

/// <summary>Shared source-range timeline presentation used by Export and Premiere handoff review.</summary>
internal sealed record MediaRangeTimelinePresentation(double SegmentLeft, double SegmentWidth,
    string ToolTip, string AutomationName)
{
    internal const double Width = 240;

    internal static MediaRangeTimelinePresentation For(string sourceName, TimeSpan sourceDuration,
        MediaRange? range, bool useRange, string operation)
    {
        var selected = useRange && range is not null;
        var input = selected ? range!.EffectiveIn : TimeSpan.Zero;
        var output = selected ? range!.EffectiveOut : sourceDuration;
        var hasDuration = sourceDuration > TimeSpan.Zero;
        var left = hasDuration ? Math.Clamp(input.TotalSeconds / sourceDuration.TotalSeconds, 0, 1) * Width : 0;
        var right = hasDuration ? Math.Clamp(output.TotalSeconds / sourceDuration.TotalSeconds, 0, 1) * Width : Width;
        var segmentWidth = Math.Max(2, right - left);
        var tooltip = selected
            ? $"{FormatTime(input)} – {FormatTime(output)} · {FormatDuration(output - input)} selected of {FormatDuration(sourceDuration)}"
            : $"Full source · {FormatDuration(sourceDuration)}";
        var automation = selected
            ? $"{operation} range {FormatTime(input)} to {FormatTime(output)} of {FormatDuration(sourceDuration)} for {sourceName}"
            : $"{operation} full source, {FormatDuration(sourceDuration)}, for {sourceName}";
        return new(left, segmentWidth, tooltip, automation);
    }

    private static string FormatTime(TimeSpan value) => value.TotalHours >= 1
        ? value.ToString(@"h\:mm\:ss\.f") : value.ToString(@"mm\:ss\.f");
    private static string FormatDuration(TimeSpan value) => value.TotalHours >= 1
        ? value.ToString(@"h\:mm\:ss\.f") : value.TotalMinutes >= 1 ? value.ToString(@"m\:ss\.f") : $"{value.TotalSeconds:0.0} s";
}
