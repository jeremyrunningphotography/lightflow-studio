using System.IO;

namespace LightflowStudio;

internal sealed record ExportSourceProbe(MediaMetadata? Metadata, TrimSourceIdentity? Identity, string? Diagnostic = null);

/// <summary>Authoritative export probes, shared by the dialog and executable integration coverage.</summary>
internal sealed class ExportSourcePreflight(string? ffprobe, IProbeProcessRunner runner)
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
            // EOF always needs a decoded terminal duration and current stream/source endpoints.
            var frames = await CaptureAsync(FfmpegCommandBuilder.ProbeVideoFrames(input.SourcePath, range, metadata.StartTimestamp), token);
            resolved = EncodingRangeResolver.Resolve(range, metadata.StartTimestamp, frames, metadata.EofEvidence);
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
