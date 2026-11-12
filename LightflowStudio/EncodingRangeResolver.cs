using System.Globalization;
using System.Text.Json;
using System.IO;

namespace LightflowStudio;

internal static class EncodingRangeResolver
{
    private static readonly TimeSpan TimestampTolerance = TimeSpan.FromMilliseconds(2);

    public static ResolvedMediaRange Resolve(MediaRange requested, TimeSpan sourceStartTimestamp, string frameProbeJson)
    {
        var validation = requested.Validate();
        if (validation.Count != 0) throw new ArgumentException(validation[0].Message, nameof(requested));

        var timestamps = ParsePresentationTimestamps(frameProbeJson)
            .Select(timestamp => timestamp - sourceStartTimestamp)
            .Where(timestamp => timestamp >= TimeSpan.Zero)
            .Distinct()
            .Order()
            .ToList();
        if (timestamps.Count == 0) throw new InvalidDataException("FFprobe did not report decoded video timestamps.");

        var actualIn = requested.In is { } requestedIn ? Match(timestamps, requestedIn, "In") : TimeSpan.Zero;
        string? correction = null;
        var actualOut = requested.Out is { } requestedOut
            ? MatchOut(timestamps, requestedOut, requested.SourceDuration, frameProbeJson, out correction)
            : (TimeSpan?)null;
        var next = actualOut is { } outTimestamp
            ? timestamps.FirstOrDefault(timestamp => timestamp > outTimestamp)
            : TimeSpan.Zero;
        var exclusiveNormalizedOut = actualOut is null || next <= TimeSpan.Zero ? requested.SourceDuration : next;
        if (exclusiveNormalizedOut > requested.SourceDuration) exclusiveNormalizedOut = requested.SourceDuration;
        if (actualOut is { } selectedOut && exclusiveNormalizedOut <= selectedOut)
            throw new ArgumentException("The selected Out frame does not fit within the inspected media.", nameof(requested));

        var absoluteIn = sourceStartTimestamp + actualIn;
        var exclusiveOut = sourceStartTimestamp + exclusiveNormalizedOut;
        if (exclusiveOut <= absoluteIn)
            throw new ArgumentException("The resolved range contains no complete frames. Choose the Subclip boundaries again.");
        return new(requested, sourceStartTimestamp, absoluteIn, exclusiveOut, exclusiveOut - absoluteIn)
        { BoundaryDiagnostic = correction };
    }

    private static TimeSpan MatchOut(IReadOnlyList<TimeSpan> timestamps, TimeSpan requested,
        TimeSpan duration, string json, out string? diagnostic)
    {
        diagnostic = null;
        var nearest = timestamps.MinBy(timestamp => Math.Abs((timestamp - requested).Ticks));
        if (Math.Abs((nearest - requested).Ticks) <= TimestampTolerance.Ticks)
            return nearest;

        // Packet PTS are a fast exact-match path, never evidence for a decoded-frame correction.
        using var document = JsonDocument.Parse(json);
        var index = Array.IndexOf(timestamps.ToArray(), nearest);
        var previous = index > 0 ? timestamps[index - 1] : (TimeSpan?)null;
        var next = index + 1 < timestamps.Count ? timestamps[index + 1] : (TimeSpan?)null;
        var interval = next is { } following ? following - nearest
            : previous is { } preceding ? nearest - preceding : TimeSpan.Zero;
        var delta = requested - nearest;
        var tied = timestamps.Count(timestamp => Math.Abs((timestamp - requested).Ticks) == Math.Abs(delta.Ticks)) > 1;
        // Out is inclusive: select no frame starting after the saved timestamp.
        // At EOF require the final duration gap to fit the observed local cadence.
        var end = next ?? duration;
        if (!document.RootElement.TryGetProperty("frames", out _) || tied || delta <= TimeSpan.Zero
            || interval <= TimeSpan.Zero || delta > interval || delta > TimeSpan.FromMilliseconds(50)
            || (next is null && requested != duration))
            throw new ArgumentException($"The saved Out point {requested.TotalSeconds:F7}s cannot be safely matched to a decoded frame within one local frame interval (maximum 50 ms) without extending the range. Nearest timestamp: {nearest.TotalSeconds:F7}s. Reopen the Subclip and choose its Out frame again.");
        diagnostic = $"Saved Out {requested.TotalSeconds:F7}s resolved to decoded frame {nearest.TotalSeconds:F7}s; exclusive end {end.TotalSeconds:F7}s, local interval {interval.TotalMilliseconds:F4} ms. The saved Subclip is unchanged.";
        return nearest;
    }

    internal static IReadOnlyList<TimeSpan> ParseFrameTimestamps(string json)
        => ParsePresentationTimestamps(json);

    internal static IReadOnlyList<TimeSpan> ParsePresentationTimestamps(string json)
    {
        using var document = JsonDocument.Parse(json);
        var (items, property) = document.RootElement.TryGetProperty("frames", out var frames)
            ? (frames, "best_effort_timestamp_time")
            : document.RootElement.TryGetProperty("packets", out var packets)
                ? (packets, "pts_time")
                : (default, "");
        if (items.ValueKind != JsonValueKind.Array) return [];
        return items.EnumerateArray()
            .Select(item => item.TryGetProperty(property, out var value)
                && double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
                    ? TimeSpan.FromSeconds(seconds)
                    : (TimeSpan?)null)
            .Where(value => value is not null)
            .Select(value => value!.Value)
            .ToList();
    }

    private static TimeSpan Match(IReadOnlyList<TimeSpan> timestamps, TimeSpan requested, string boundary)
    {
        var match = timestamps.MinBy(timestamp => Math.Abs((timestamp - requested).Ticks));
        if (Math.Abs((match - requested).Ticks) > TimestampTolerance.Ticks)
            throw new ArgumentException($"The saved {boundary} point no longer matches a decoded frame in this media.");
        return match;
    }
}
