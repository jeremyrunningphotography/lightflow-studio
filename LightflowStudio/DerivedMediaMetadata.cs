using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Media.Imaging;

namespace LightflowStudio;

internal enum DerivedMediaKind { Image, Video, Audio }
internal enum DerivedMetadataStatus { Succeeded, Current, SourceChanged, AssetNotFound, RootUnavailable, SourceMissing, Unsupported, ProbeUnavailable, Malformed, Failed }

internal sealed record DerivedVideoMetadata(
    string Codec,
    string? Profile,
    int Width,
    int Height,
    double? FrameRate,
    string? PixelFormat,
    int? BitDepth,
    string? ColorSpace,
    string? ColorTransfer,
    string? ColorPrimaries);

internal sealed record DerivedAudioMetadata(
    string Codec,
    int? Channels,
    string? ChannelLayout,
    int? SampleRate,
    long? BitRate);

internal sealed record DerivedImageMetadata(
    string Format,
    int Width,
    int Height,
    int? BitDepth,
    int? Orientation,
    string? CameraMake,
    string? CameraModel,
    string? LensModel,
    string? CapturedAt);

internal sealed record DerivedMediaMetadata(
    DerivedMediaKind Kind,
    string? Container,
    double? DurationSeconds,
    double? StartTimestampSeconds,
    long FileSizeBytes,
    long? BitRate,
    DerivedVideoMetadata? Video,
    DerivedAudioMetadata? Audio,
    DerivedImageMetadata? Image);

internal sealed record DerivedMetadataResult(
    DerivedMetadataStatus Status,
    DerivedMediaMetadata? Metadata = null,
    string? Diagnostic = null)
{
    public bool Succeeded => Status is DerivedMetadataStatus.Succeeded or DerivedMetadataStatus.Current;
}

internal sealed record MediaProbeResult(
    DerivedMetadataStatus Status,
    DerivedMediaMetadata? Metadata = null,
    string? RawMetadata = null,
    string? Diagnostic = null);

internal interface IMediaMetadataProbe
{
    Task<MediaProbeResult> ProbeAsync(string path, string mediaType, long fileSizeBytes,
        CancellationToken cancellationToken = default);
}

internal interface IProbeProcessRunner
{
    Task<ProbeProcessResult> RunAsync(string executable, IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default);
}

internal sealed record ProbeProcessResult(int ExitCode, string StandardOutput, string StandardError);

internal sealed class ProbeProcessRunner : IProbeProcessRunner
{
    public async Task<ProbeProcessResult> RunAsync(string executable, IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException($"Could not start {executable}.");
        var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardError = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return new(process.ExitCode, await standardOutput.ConfigureAwait(false), await standardError.ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            try { await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
            try { await Task.WhenAll(standardOutput, standardError).ConfigureAwait(false); } catch { }
            throw;
        }
    }
}

internal interface IImageMetadataReader
{
    Task<MediaProbeResult> ReadAsync(string path, long fileSizeBytes, CancellationToken cancellationToken = default);
}

internal sealed class WicImageMetadataReader : IImageMetadataReader
{
    public Task<MediaProbeResult> ReadAsync(string path, long fileSizeBytes, CancellationToken cancellationToken = default) =>
        Task.Run(() => Read(path, fileSizeBytes, cancellationToken), cancellationToken);

    private static MediaProbeResult Read(string path, long fileSizeBytes, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 64 * 1024, FileOptions.SequentialScan);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            cancellationToken.ThrowIfCancellationRequested();
            if (decoder.Frames.Count == 0)
                return new(DerivedMetadataStatus.Malformed, Diagnostic: "The image contains no decodable frames.");
            var frame = decoder.Frames[0];
            var metadata = frame.Metadata as BitmapMetadata;
            var raw = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["containerFormat"] = decoder.CodecInfo?.FriendlyName,
                ["pixelFormat"] = frame.Format.ToString(),
                ["cameraMake"] = Query(metadata, "/app1/ifd/{ushort=271}"),
                ["cameraModel"] = Query(metadata, "/app1/ifd/{ushort=272}"),
                ["orientation"] = Query(metadata, "/app1/ifd/{ushort=274}"),
                ["capturedAt"] = Query(metadata, "/app1/ifd/exif/{ushort=36867}") ?? Query(metadata, "/app1/ifd/{ushort=306}"),
                ["lensModel"] = Query(metadata, "/app1/ifd/exif/{ushort=42036}")
            };
            var image = new DerivedImageMetadata(
                decoder.CodecInfo?.FriendlyName ?? Path.GetExtension(path).TrimStart('.').ToUpperInvariant(),
                frame.PixelWidth,
                frame.PixelHeight,
                frame.Format.BitsPerPixel > 0 ? frame.Format.BitsPerPixel : null,
                ParseInt(raw["orientation"]),
                raw["cameraMake"], raw["cameraModel"], raw["lensModel"], raw["capturedAt"]);
            var normalized = new DerivedMediaMetadata(DerivedMediaKind.Image, image.Format, null, null,
                fileSizeBytes, null, null, null, image);
            return new(DerivedMetadataStatus.Succeeded, normalized, JsonSerializer.Serialize(raw, DerivedMetadataJson.Options));
        }
        catch (FileFormatException exception)
        {
            return new(DerivedMetadataStatus.Malformed, Diagnostic: $"The image is malformed: {exception.Message}");
        }
        catch (NotSupportedException exception)
        {
            return new(DerivedMetadataStatus.Unsupported, Diagnostic: $"The image format is unsupported: {exception.Message}");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new(DerivedMetadataStatus.Failed, Diagnostic: $"The image could not be read: {exception.Message}");
        }
    }

    private static string? Query(BitmapMetadata? metadata, string query)
    {
        if (metadata is null) return null;
        try
        {
            if (!metadata.ContainsQuery(query)) return null;
            return Convert.ToString(metadata.GetQuery(query), CultureInfo.InvariantCulture)?.Trim() is { Length: > 0 } value ? value : null;
        }
        catch (Exception exception) when (exception is NotSupportedException or ArgumentException) { return null; }
    }

    private static int? ParseInt(string? value) => int.TryParse(value, NumberStyles.Integer,
        CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
}

internal sealed class FfprobeMediaMetadataReader(string? executable, IProbeProcessRunner runner) : IMediaMetadataProbe
{
    public async Task<MediaProbeResult> ProbeAsync(string path, string mediaType, long fileSizeBytes,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
            return new(DerivedMetadataStatus.ProbeUnavailable, Diagnostic: "FFprobe is unavailable.");
        try
        {
            var result = await runner.RunAsync(executable, FfmpegCommandBuilder.ProbeDerivedMetadata(path), cancellationToken)
                .ConfigureAwait(false);
            if (result.ExitCode != 0)
                return new(DerivedMetadataStatus.Malformed, RawMetadata: result.StandardOutput,
                    Diagnostic: FirstDiagnostic(result.StandardError, "FFprobe could not read this media file."));
            return FfprobeMetadataNormalizer.Normalize(result.StandardOutput, fileSizeBytes);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException
            or System.ComponentModel.Win32Exception)
        {
            return new(DerivedMetadataStatus.ProbeUnavailable, Diagnostic: $"FFprobe could not be started: {exception.Message}");
        }
    }

    private static string FirstDiagnostic(string output, string fallback) =>
        output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? fallback;
}

internal sealed class CompositeMediaMetadataProbe(IImageMetadataReader images, IMediaMetadataProbe ffprobe) : IMediaMetadataProbe
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".png", ".tif", ".tiff", ".bmp", ".gif", ".wdp", ".jxr" };

    public Task<MediaProbeResult> ProbeAsync(string path, string mediaType, long fileSizeBytes,
        CancellationToken cancellationToken = default)
    {
        if (string.Equals(mediaType, "image", StringComparison.OrdinalIgnoreCase) || ImageExtensions.Contains(Path.GetExtension(path)))
            return images.ReadAsync(path, fileSizeBytes, cancellationToken);
        if (mediaType is "video" or "audio" or "unknown" || !string.IsNullOrWhiteSpace(Path.GetExtension(path)))
            return ffprobe.ProbeAsync(path, mediaType, fileSizeBytes, cancellationToken);
        return Task.FromResult(new MediaProbeResult(DerivedMetadataStatus.Unsupported,
            Diagnostic: "The asset is not a supported image, video, or audio file."));
    }
}

internal static class DerivedMediaMetadataFactory
{
    public static IDerivedMediaMetadataService Create(IMediaAssetService assets, IPreviewStoreService previews,
        AppSettings settings, string? applicationDirectory = null, int maximumConcurrency = 2)
    {
        applicationDirectory ??= AppContext.BaseDirectory;
        var configuredDirectory = string.IsNullOrWhiteSpace(settings.FfmpegPath)
            ? null
            : Path.GetDirectoryName(settings.FfmpegPath);
        var configured = configuredDirectory is null ? null : Path.Combine(configuredDirectory, "ffprobe.exe");
        var ffprobe = ExecutableLocator.Find("ffprobe.exe",
            Path.Combine(applicationDirectory, "ffmpeg", "bin", "ffprobe.exe"), configured: configured);
        var probe = new CompositeMediaMetadataProbe(new WicImageMetadataReader(),
            new FfprobeMediaMetadataReader(ffprobe, new ProbeProcessRunner()));
        return new DerivedMediaMetadataService(assets, previews, probe, maximumConcurrency);
    }
}

internal interface IDerivedMediaMetadataService : IDisposable
{
    Task<DerivedMetadataResult> ProbeAsync(Guid assetId, bool forceRefresh = false,
        CancellationToken cancellationToken = default);
}

internal sealed class DerivedMediaMetadataService : IDerivedMediaMetadataService, IDisposable
{
    internal const int CurrentProbeVersion = 1;
    private readonly IMediaAssetService _assets;
    private readonly IPreviewStoreService _previews;
    private readonly IMediaMetadataProbe _probe;
    private readonly SemaphoreSlim _concurrency;

    public DerivedMediaMetadataService(IMediaAssetService assets, IPreviewStoreService previews,
        IMediaMetadataProbe probe, int maximumConcurrency = 2)
    {
        if (maximumConcurrency <= 0) throw new ArgumentOutOfRangeException(nameof(maximumConcurrency));
        _assets = assets;
        _previews = previews;
        _probe = probe;
        _concurrency = new(maximumConcurrency, maximumConcurrency);
    }

    public async Task<DerivedMetadataResult> ProbeAsync(Guid assetId, bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        var observed = await _assets.ObserveAsync(assetId, cancellationToken).ConfigureAwait(false);
        if (observed.Status == MediaAssetOperationStatus.NotFound)
            return new(DerivedMetadataStatus.AssetNotFound, Diagnostic: observed.Diagnostic);
        if (observed.Status == MediaAssetOperationStatus.RootUnavailable)
            return await RetainOfflineAsync(assetId, PreviewSourceAvailability.Unavailable,
                DerivedMetadataStatus.RootUnavailable, observed.Diagnostic, cancellationToken).ConfigureAwait(false);
        if (observed.Status == MediaAssetOperationStatus.SourceMissing)
            return await RetainOfflineAsync(assetId, PreviewSourceAvailability.Missing,
                DerivedMetadataStatus.SourceMissing, observed.Diagnostic, cancellationToken).ConfigureAwait(false);
        if (!observed.Succeeded || observed.Asset?.PhysicalPath is null)
            return new(DerivedMetadataStatus.Failed, Diagnostic: observed.Diagnostic ?? "The media source could not be observed.");

        var asset = observed.Asset.Asset;
        if (asset.Fingerprint is null)
            return new(DerivedMetadataStatus.Failed, Diagnostic: "The media source has no current fingerprint.");
        var source = new PreviewSourceIdentity(asset.FileSizeBytes, asset.LastWriteUtcTicks,
            asset.Fingerprint.Version, asset.Fingerprint.Value);
        var preview = await _previews.ObserveSourceAsync(assetId, source, cancellationToken).ConfigureAwait(false);
        if (!forceRefresh && preview.MetadataState == PreviewComponentState.Current &&
            preview.MetadataProbeVersion == CurrentProbeVersion && TryDeserialize(preview.MetadataJson, out var current))
            return new(DerivedMetadataStatus.Current, current);

        await _concurrency.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var result = await _probe.ProbeAsync(observed.Asset.PhysicalPath, asset.MediaType,
                asset.FileSizeBytes, cancellationToken).ConfigureAwait(false);
            if (result.Status == DerivedMetadataStatus.Succeeded && result.Metadata is not null)
            {
                var verified = await _assets.ObserveAsync(assetId, cancellationToken).ConfigureAwait(false);
                if (verified.Status == MediaAssetOperationStatus.NotFound)
                    return new(DerivedMetadataStatus.AssetNotFound, Diagnostic: verified.Diagnostic);
                if (verified.Status == MediaAssetOperationStatus.RootUnavailable)
                    return await RetainOfflineAsync(assetId, PreviewSourceAvailability.Unavailable,
                        DerivedMetadataStatus.RootUnavailable, verified.Diagnostic, cancellationToken).ConfigureAwait(false);
                if (verified.Status == MediaAssetOperationStatus.SourceMissing)
                    return await RetainOfflineAsync(assetId, PreviewSourceAvailability.Missing,
                        DerivedMetadataStatus.SourceMissing, verified.Diagnostic, cancellationToken).ConfigureAwait(false);
                if (!verified.Succeeded || verified.Asset?.Asset.Fingerprint is null)
                    return new(DerivedMetadataStatus.Failed,
                        Diagnostic: verified.Diagnostic ?? "The media source could not be verified after probing.");

                var verifiedAsset = verified.Asset.Asset;
                var verifiedSource = new PreviewSourceIdentity(verifiedAsset.FileSizeBytes, verifiedAsset.LastWriteUtcTicks,
                    verifiedAsset.Fingerprint.Version, verifiedAsset.Fingerprint.Value);
                await _previews.ObserveSourceAsync(assetId, verifiedSource, cancellationToken).ConfigureAwait(false);
                if (!SameSourceIdentity(source, verifiedSource))
                    return new(DerivedMetadataStatus.SourceChanged,
                        Diagnostic: "The source changed while metadata was being read. Retry after the file is stable.");

                var normalized = JsonSerializer.Serialize(result.Metadata, DerivedMetadataJson.Options);
                await _previews.SetMetadataAsync(assetId, new(CurrentProbeVersion, PreviewComponentState.Current,
                    PayloadJson: normalized, RawPayloadJson: result.RawMetadata), cancellationToken).ConfigureAwait(false);
                return new(DerivedMetadataStatus.Succeeded, result.Metadata);
            }

            await _previews.SetMetadataAsync(assetId, new(CurrentProbeVersion, PreviewComponentState.Failed,
                PayloadJson: preview.MetadataJson, RawPayloadJson: preview.RawMetadataJson ?? result.RawMetadata), cancellationToken)
                .ConfigureAwait(false);
            return new(result.Status, Diagnostic: result.Diagnostic);
        }
        finally { _concurrency.Release(); }
    }

    private async Task<DerivedMetadataResult> RetainOfflineAsync(Guid assetId, PreviewSourceAvailability availability,
        DerivedMetadataStatus status, string? diagnostic, CancellationToken cancellationToken)
    {
        var existing = await _previews.GetAsync(assetId, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
            await _previews.SetSourceAvailabilityAsync(assetId, availability, cancellationToken).ConfigureAwait(false);
        return new(status, TryDeserialize(existing?.MetadataJson, out var retained) ? retained : null, diagnostic);
    }

    private static bool TryDeserialize(string? json, out DerivedMediaMetadata? metadata)
    {
        metadata = null;
        if (string.IsNullOrWhiteSpace(json)) return false;
        try { metadata = JsonSerializer.Deserialize<DerivedMediaMetadata>(json, DerivedMetadataJson.Options); return metadata is not null; }
        catch (JsonException) { return false; }
    }

    private static bool SameSourceIdentity(PreviewSourceIdentity left, PreviewSourceIdentity right) =>
        left.FileSizeBytes == right.FileSizeBytes &&
        left.LastWriteUtcTicks == right.LastWriteUtcTicks &&
        left.FingerprintVersion == right.FingerprintVersion &&
        string.Equals(left.Fingerprint, right.Fingerprint, StringComparison.OrdinalIgnoreCase);

    public void Dispose() => _concurrency.Dispose();
}

internal static class FfprobeMetadataNormalizer
{
    public static MediaProbeResult Normalize(string json, long fileSizeBytes)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("streams", out var streamsElement) || streamsElement.ValueKind != JsonValueKind.Array)
                return new(DerivedMetadataStatus.Malformed, RawMetadata: json, Diagnostic: "FFprobe returned no stream list.");
            var streams = streamsElement.EnumerateArray().ToList();
            var videoElement = streams.FirstOrDefault(StreamType("video"));
            var audioElement = streams.FirstOrDefault(StreamType("audio"));
            var hasVideo = videoElement.ValueKind != JsonValueKind.Undefined;
            var hasAudio = audioElement.ValueKind != JsonValueKind.Undefined;
            if (!hasVideo && !hasAudio)
                return new(DerivedMetadataStatus.Unsupported, RawMetadata: json, Diagnostic: "The file contains no supported audio or video streams.");

            var format = document.RootElement.TryGetProperty("format", out var formatElement) ? formatElement : default;
            var duration = Positive(ReadDouble(format, "duration")) ?? Positive(ReadDouble(videoElement, "duration"))
                ?? Positive(ReadDouble(audioElement, "duration"));
            var start = ReadDouble(format, "start_time") ?? ReadDouble(videoElement, "start_time") ?? ReadDouble(audioElement, "start_time");
            var video = hasVideo ? new DerivedVideoMetadata(
                ReadString(videoElement, "codec_name") ?? "unknown",
                ReadString(videoElement, "profile"), ReadInt(videoElement, "width") ?? 0, ReadInt(videoElement, "height") ?? 0,
                Positive(MediaMetadataParser.ReadFrameRate(ReadString(videoElement, "avg_frame_rate") ?? "0")),
                ReadString(videoElement, "pix_fmt"), ReadInt(videoElement, "bits_per_raw_sample"),
                ReadString(videoElement, "color_space"), ReadString(videoElement, "color_transfer"), ReadString(videoElement, "color_primaries")) : null;
            var audio = hasAudio ? new DerivedAudioMetadata(
                ReadString(audioElement, "codec_name") ?? "unknown", ReadInt(audioElement, "channels"),
                ReadString(audioElement, "channel_layout"), ReadInt(audioElement, "sample_rate"), ReadLong(audioElement, "bit_rate")) : null;
            var normalized = new DerivedMediaMetadata(hasVideo ? DerivedMediaKind.Video : DerivedMediaKind.Audio,
                ReadString(format, "format_name")?.Split(',')[0], duration, start, fileSizeBytes,
                ReadLong(format, "bit_rate"), video, audio, null);
            return new(DerivedMetadataStatus.Succeeded, normalized, json);
        }
        catch (JsonException exception)
        {
            return new(DerivedMetadataStatus.Malformed, RawMetadata: json,
                Diagnostic: $"FFprobe returned malformed metadata: {exception.Message}");
        }
    }

    private static Func<JsonElement, bool> StreamType(string type) => element =>
        string.Equals(ReadString(element, "codec_type"), type, StringComparison.OrdinalIgnoreCase);
    private static string? ReadString(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value)) return null;
        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
    }
    private static int? ReadInt(JsonElement element, string name) => int.TryParse(ReadString(element, name),
        NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;
    private static long? ReadLong(JsonElement element, string name) => long.TryParse(ReadString(element, name),
        NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;
    private static double? ReadDouble(JsonElement element, string name) => double.TryParse(ReadString(element, name),
        NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;
    private static double? Positive(double? value) => value > 0 ? value : null;
}

internal static class DerivedMetadataJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };
}
