using System.Globalization;

namespace LightflowStudio;

internal static class EncodingFrameProbeWindow
{
    private static readonly TimeSpan Margin = TimeSpan.FromSeconds(2);

    public static string For(MediaRange range, TimeSpan sourceStartTimestamp = default)
    {
        var boundaries = new[] { range.In, range.Out }
            .Where(value => value is not null)
            .Select(value => value!.Value)
            .Order()
            .ToList();
        if (boundaries.Count == 0) throw new ArgumentException("A frame probe window requires a trimmed boundary.", nameof(range));

        var windows = boundaries
            .Select(boundary => new Window(
                Max(TimeSpan.Zero, boundary - Margin),
                Min(range.SourceDuration, boundary + Margin)))
            .Aggregate(new List<Window>(), (result, window) =>
            {
                if (result.Count > 0 && window.Start <= result[^1].End)
                    result[^1] = result[^1] with { End = Max(result[^1].End, window.End) };
                else
                    result.Add(window);
                return result;
            });

        return string.Join(',', windows.Select(window =>
            $"{Seconds(sourceStartTimestamp + window.Start)}%{Seconds(sourceStartTimestamp + window.End)}"));
    }

    private static string Seconds(TimeSpan value) =>
        value.TotalSeconds.ToString("0.#########", CultureInfo.InvariantCulture);
    private static TimeSpan Min(TimeSpan first, TimeSpan second) => first <= second ? first : second;
    private static TimeSpan Max(TimeSpan first, TimeSpan second) => first >= second ? first : second;
    private sealed record Window(TimeSpan Start, TimeSpan End);
}
