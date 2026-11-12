using System.Globalization;
using System.Text.Json;
using System.IO;

namespace LightflowStudio;

internal sealed record SourceEofEvidence(TimeSpan SourceDuration, TimeSpan VideoDuration, string VideoTimeBase);

internal static class EncodingRangeResolver
{
    private static readonly TimeSpan TimestampTolerance = TimeSpan.FromMilliseconds(2);

    public static ResolvedMediaRange Resolve(MediaRange requested, TimeSpan sourceStartTimestamp, string frameProbeJson,
        SourceEofEvidence? eofEvidence = null)
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
        if (requested.Out == requested.SourceDuration)
            return ResolveEof(requested, sourceStartTimestamp, actualIn, frameProbeJson, eofEvidence);
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

    private static ResolvedMediaRange ResolveEof(MediaRange requested, TimeSpan sourceStart, TimeSpan actualIn,
        string json, SourceEofEvidence? evidence)
    {
        using var document = JsonDocument.Parse(json);
        if (evidence is null || !document.RootElement.TryGetProperty("frames", out var frames))
            throw new ArgumentException("Saved source EOF requires current source duration and decoded terminal frame evidence. Reopen the source and try again.");
        var parts = evidence.VideoTimeBase.Split('/');
        if (parts.Length != 2 || !decimal.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var numerator)
            || !decimal.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var denominator)
            || numerator <= 0 || denominator <= 0)
            throw new ArgumentException("The video time base is unavailable; source EOF cannot be verified safely.");
        var tickSeconds = numerator / denominator;
        TimeSpan? ReadTime(JsonElement frame, string ticksName, string secondsName)
        {
            if (frame.TryGetProperty(ticksName, out var ticks) && ticks.TryGetInt64(out var value))
                return TimeSpan.FromTicks((long)decimal.Round(value * tickSeconds * TimeSpan.TicksPerSecond));
            if (frame.TryGetProperty(secondsName, out var seconds)
                && decimal.TryParse(seconds.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                return TimeSpan.FromTicks((long)decimal.Round(number * TimeSpan.TicksPerSecond));
            return null;
        }
        var decoded = frames.EnumerateArray().Select(frame => new
        {
            Pts = ReadTime(frame, "best_effort_timestamp", "best_effort_timestamp_time"),
            Duration = ReadTime(frame, "duration", "duration_time")
        }).Where(frame => frame.Pts is not null).OrderBy(frame => frame.Pts).ToArray();
        if (decoded.Length == 0 || decoded[^1].Duration is not { } interval || interval <= TimeSpan.Zero)
            throw new ArgumentException("The decoded final video frame duration is missing or invalid; source EOF cannot be verified safely.");
        var last = decoded[^1].Pts!.Value - sourceStart;
        var end = last + interval;
        var precision = TimeSpan.FromTicks(Math.Max(10, (long)decimal.Ceiling(tickSeconds * TimeSpan.TicksPerSecond)));
        var tail = requested.SourceDuration - end;
        var diagnostic = $"Saved exclusive source EOF {requested.SourceDuration.TotalSeconds:F7}s; final decoded frame start {last.TotalSeconds:F7}s, end {end.TotalSeconds:F7}s, duration {interval.TotalMilliseconds:F4} ms; video-to-EOF tail {tail.TotalMilliseconds:F4} ms.";
        if (Math.Abs((evidence.SourceDuration - requested.SourceDuration).Ticks) > precision.Ticks
            || Math.Abs((evidence.VideoDuration - end).Ticks) > precision.Ticks
            || tail < -precision || tail > interval || tail > TimeSpan.FromMilliseconds(50)
            || actualIn >= end || decoded.Count(frame => frame.Pts == decoded[^1].Pts) != 1)
            throw new ArgumentException($"Source EOF could not be verified: stale source duration, incomplete/ambiguous terminal frame, or unsafe tail. {diagnostic} Reopen the Subclip and review its boundaries.");
        var absoluteIn = sourceStart + actualIn;
        var exclusiveOut = sourceStart + requested.SourceDuration;
        return new(requested, sourceStart, absoluteIn, exclusiveOut, exclusiveOut - absoluteIn)
        { RetainsSourceEof = true, BoundaryDiagnostic = diagnostic + " Saved endpoint retained unchanged; final video and audio are included without extending the range." };
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
