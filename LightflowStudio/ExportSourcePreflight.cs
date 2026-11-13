using System.Text.Json;
using System.IO;

namespace LightflowStudio;

internal sealed record ExportSourceProbe(MediaMetadata? Metadata, TrimSourceIdentity? Identity, string? Diagnostic = null);

/// <summary>Authoritative export probes, shared by the dialog and executable integration coverage.</summary>
internal sealed class ExportSourcePreflight(string? ffprobe, IProbeProcessRunner runner, Action<string>? diagnostic = null)
{
    public async Task<ExportSourceProbe> ProbeAsync(EncodingHandoffInput input, CancellationToken token = default)
    {
        try
        {
            var identity = TrimSourceIdentity.Read(input.SourcePath);
            if (identity is null || identity.FileSizeBytes != input.FileSizeBytes
                || input.SourceIdentity is { } expected && !expected.Matches(identity))
                throw new InvalidDataException("The source changed or is unavailable. Reopen Export after refreshing the source.");
            var json = await CaptureAsync(FfmpegCommandBuilder.ProbeMetadata(input.SourcePath), token);
            if (!MediaMetadataParser.TryParse(json, input.FileSizeBytes, out var metadata)
                || string.IsNullOrWhiteSpace(metadata.VideoCodec) || string.IsNullOrWhiteSpace(metadata.Container))
                throw new InvalidDataException("FFprobe did not identify a readable source video codec and container.");
            RequireUnchanged(input, identity);
            return new(metadata, identity);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException
            or ArgumentException or System.ComponentModel.Win32Exception or System.Text.Json.JsonException)
        { return new(null, null, $"Source metadata could not be read: {ex.Message}"); }
    }

    public async Task<ResolvedMediaRange?> ResolveAsync(EncodingHandoffInput input, ExportSourceProbe probe,
        CancellationToken token = default)
    {
        if (input.InitialTrim is not { IsFullSource: false } range) return null;
        if (probe.Metadata is not { } metadata || probe.Identity is not { } identity)
            throw new InvalidDataException(probe.Diagnostic ?? "Source metadata must be read before validating a saved range.");
        RequireUnchanged(input, identity);
        ResolvedMediaRange resolved;
        if (range.Out == range.SourceDuration)
        {
            try
            {
                var arguments = FfmpegCommandBuilder.ProbeVideoFrames(input.SourcePath, range, metadata.StartTimestamp);
                // FFprobe seeks back to a keyframe, decodes through real EOF, and
                // the resolver must still prove terminal duration/end against metadata.
                var start = metadata.StartTimestamp + (range.SourceDuration > TimeSpan.FromMilliseconds(50)
                    ? range.SourceDuration - TimeSpan.FromMilliseconds(50) : TimeSpan.Zero);
                arguments[arguments.IndexOf("-read_intervals") + 1] = start.TotalSeconds
                    .ToString("0.#######", System.Globalization.CultureInfo.InvariantCulture) + "%";
                var frames = await CaptureAsync(arguments, token);
                // First independently prove the decoded terminal end. An In probe
                // must never stand in for missing EOF evidence.
                _ = EncodingRangeResolver.Resolve(range with { In = null }, metadata.StartTimestamp, frames, metadata.EofEvidence);
                if (range.In is { } requestedIn && !EncodingRangeResolver.ParseFrameTimestamps(frames)
                    .Any(timestamp => Math.Abs((timestamp - metadata.StartTimestamp - requestedIn).TotalMilliseconds) <= 2))
                {
                    var inArguments = FfmpegCommandBuilder.ProbeVideoFrames(input.SourcePath, range, metadata.StartTimestamp);
                    var inStart = metadata.StartTimestamp + (requestedIn > TimeSpan.FromMilliseconds(2)
                        ? requestedIn - TimeSpan.FromMilliseconds(2) : TimeSpan.Zero);
                    var inEnd = metadata.StartTimestamp + requestedIn + TimeSpan.FromMilliseconds(2);
                    inArguments[inArguments.IndexOf("-read_intervals") + 1] = inStart.TotalSeconds
                        .ToString("0.#######", System.Globalization.CultureInfo.InvariantCulture) + "%" + inEnd.TotalSeconds
                        .ToString("0.#######", System.Globalization.CultureInfo.InvariantCulture);
                    var inFrames = await CaptureAsync(inArguments, token);
                    using var terminalDocument = JsonDocument.Parse(frames);
                    using var inDocument = JsonDocument.Parse(inFrames);
                    var combined = terminalDocument.RootElement.GetProperty("frames").EnumerateArray()
                        .Concat(inDocument.RootElement.GetProperty("frames").EnumerateArray())
                        .DistinctBy(frame => frame.GetProperty("best_effort_timestamp_time").GetString() + "|" +
                            (frame.TryGetProperty("duration", out var duration) ? duration.ToString()
                                : frame.TryGetProperty("duration_time", out var seconds) ? seconds.ToString() : "missing"));
                    frames = JsonSerializer.Serialize(new { frames = combined.ToArray() });
                }
                resolved = EncodingRangeResolver.Resolve(range, metadata.StartTimestamp, frames, metadata.EofEvidence);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidDataException)
            {
                diagnostic?.Invoke("Targeted decoded boundary probe was insufficient; using the full boundary probe.");
                var frames = await CaptureAsync(FfmpegCommandBuilder.ProbeVideoFrames(input.SourcePath, range, metadata.StartTimestamp), token);
                resolved = EncodingRangeResolver.Resolve(range, metadata.StartTimestamp, frames, metadata.EofEvidence);
            }
        }
        else
        {
            var packets = await CaptureAsync(FfmpegCommandBuilder.ProbeVideoPackets(input.SourcePath, range, metadata.StartTimestamp), token);
            try { resolved = EncodingRangeResolver.Resolve(range, metadata.StartTimestamp, packets); }
            catch (Exception ex) when (ex is ArgumentException or InvalidDataException)
            {
                var frames = await CaptureAsync(FfmpegCommandBuilder.ProbeVideoFrames(input.SourcePath, range, metadata.StartTimestamp), token);
                resolved = EncodingRangeResolver.Resolve(range, metadata.StartTimestamp, frames);
            }
        }
        RequireUnchanged(input, identity);
        if (resolved.BoundaryDiagnostic is { } detail)
            diagnostic?.Invoke($"Requested In {range.EffectiveIn.TotalSeconds:F7}s, Out {range.EffectiveOut.TotalSeconds:F7}s; " +
                $"stream start {metadata.StartTimestamp.TotalSeconds:F7}s, time base {metadata.EofEvidence?.VideoTimeBase ?? "unavailable"}; " +
                $"tolerance: one verified local frame interval, capped at 50 ms. {detail}");
        return resolved;
    }

    private static void RequireUnchanged(EncodingHandoffInput input, TrimSourceIdentity identity)
    {
        var current = TrimSourceIdentity.Read(input.SourcePath);
        if (current is null || !identity.Matches(current))
            throw new InvalidDataException("The source changed during export preflight. Refresh the source and reopen Export.");
    }

    private async Task<string> CaptureAsync(IReadOnlyList<string> arguments, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(ffprobe)) throw new InvalidDataException("FFprobe is unavailable. Check application dependencies.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        ProbeProcessResult result;
        try { result = await runner.RunAsync(ffprobe, arguments, timeout.Token); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { throw new InvalidDataException("FFprobe timed out while reading source information. Check source availability and try again."); }
        if (result.ExitCode != 0)
            throw new InvalidDataException($"FFprobe failed (exit {result.ExitCode}): {result.StandardError.Trim()}");
        return result.StandardOutput;
    }
}
