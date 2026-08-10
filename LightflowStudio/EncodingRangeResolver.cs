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
        var actualOut = requested.Out is { } requestedOut ? Match(timestamps, requestedOut, "Out") : (TimeSpan?)null;
        var next = actualOut is { } outTimestamp
            ? timestamps.FirstOrDefault(timestamp => timestamp > outTimestamp + TimestampTolerance)
            : TimeSpan.Zero;
        var exclusiveNormalizedOut = actualOut is null || next <= TimeSpan.Zero ? requested.SourceDuration : next;
        if (actualOut is { } selectedOut && exclusiveNormalizedOut <= selectedOut)
            throw new ArgumentException("The selected Out frame does not fit within the inspected media.", nameof(requested));

        var absoluteIn = sourceStartTimestamp + actualIn;
        var exclusiveOut = sourceStartTimestamp + exclusiveNormalizedOut;
        return new(requested, sourceStartTimestamp, absoluteIn, exclusiveOut, exclusiveOut - absoluteIn);
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
