using System.Text.Json;
using System.Text.Json.Serialization;
namespace LightflowStudio;

public enum DerivedMediaKind { Image, Video, Audio }
public enum DerivedMetadataStatus { Succeeded, Current, SourceChanged, AssetNotFound, RootUnavailable, SourceMissing, Unsupported, ProbeUnavailable, Malformed, Failed, Deferred }

public sealed record DerivedVideoMetadata(
    string Codec,
    string? Profile,
    int Width,
    int Height,
    double? FrameRate,
    string? PixelFormat,
    int? BitDepth,
    [property: JsonPropertyName("colorSpace")] string? ColorMatrix,
    string? ColorTransfer,
    string? ColorPrimaries)
{
    public MediaAspectRatio? SourceDisplayAspectRatio { get; init; }
    public string? ChromaSubsampling { get; init; }
}

public sealed record DerivedAudioMetadata(
    string Codec,
    int? Channels,
    string? ChannelLayout,
    int? SampleRate,
    long? BitRate);

public sealed record DerivedImageMetadata(
    string Format,
    int Width,
    int Height,
    int? BitDepth,
    int? Orientation,
    string? CameraMake,
    string? CameraModel,
    string? LensModel,
    string? CapturedAt);

public sealed record DerivedMediaMetadata(
    DerivedMediaKind Kind,
    string? Container,
    double? DurationSeconds,
    double? StartTimestampSeconds,
    long FileSizeBytes,
    long? BitRate,
    DerivedVideoMetadata? Video,
    DerivedAudioMetadata? Audio,
    DerivedImageMetadata? Image);

public sealed record DerivedMetadataResult(
    DerivedMetadataStatus Status,
    DerivedMediaMetadata? Metadata = null,
    string? Diagnostic = null)
{
    public bool Succeeded => Status is DerivedMetadataStatus.Succeeded or DerivedMetadataStatus.Current;
}

public sealed record MediaProbeResult(
    DerivedMetadataStatus Status,
    DerivedMediaMetadata? Metadata = null,
    string? RawMetadata = null,
    string? Diagnostic = null);

public interface IMediaMetadataProbe
{
    Task<MediaProbeResult> ProbeAsync(string path, string mediaType, long fileSizeBytes,
        CancellationToken cancellationToken = default);
}

public interface IDerivedMediaMetadataService : IDisposable
{
    Task<DerivedMetadataResult> ProbeAsync(Guid assetId, bool forceRefresh = false,
        CancellationToken cancellationToken = default);
}

public sealed class DerivedMediaMetadataService : IDerivedMediaMetadataService, IDisposable
{
    public const int CurrentProbeVersion = 2;
    // WIC normalization did not change. Only FFprobe-derived metadata needs refreshing.
    public static int ProbeVersionFor(string? mediaType) =>
        string.Equals(mediaType, "image", StringComparison.OrdinalIgnoreCase) ? 1 : CurrentProbeVersion;
    private readonly TimeProvider _time;
    private readonly IMediaAssetService _assets;
    private readonly IPreviewStoreService _previews;
    private readonly IMediaMetadataProbe _probe;
    private readonly SemaphoreSlim _concurrency;
    private readonly IPreviewOperationCoordinator? _operations;

    public DerivedMediaMetadataService(IMediaAssetService assets, IPreviewStoreService previews,
        IMediaMetadataProbe probe, int maximumConcurrency = 2, IPreviewOperationCoordinator? operations = null, TimeProvider? time = null)
    {
        if (maximumConcurrency <= 0) throw new ArgumentOutOfRangeException(nameof(maximumConcurrency));
        _time = time ?? TimeProvider.System;
        _assets = assets;
        _previews = previews;
        _probe = probe;
        _concurrency = new(maximumConcurrency, maximumConcurrency);
        _operations = operations;
    }

    public async Task<DerivedMetadataResult> ProbeAsync(Guid assetId, bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        using var operationLease = _operations is null ? null :
            await _operations.EnterOperationAsync(cancellationToken).ConfigureAwait(false);
        using var assetLease = await PreviewRetryPolicy.MetadataGate(_previews).EnterAsync(assetId, cancellationToken).ConfigureAwait(false);
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
        var probeVersion = ProbeVersionFor(asset.MediaType);
        if (asset.Fingerprint is null)
            return new(DerivedMetadataStatus.Failed, Diagnostic: "The media source has no current fingerprint.");
        var source = new PreviewSourceIdentity(asset.FileSizeBytes, asset.LastWriteUtcTicks,
            asset.Fingerprint.Version, asset.Fingerprint.Value);
        var preview = await _previews.ObserveSourceAsync(assetId, source, cancellationToken).ConfigureAwait(false);
        if (!forceRefresh && preview.MetadataState == PreviewComponentState.Current &&
            preview.MetadataProbeVersion == probeVersion && TryDeserialize(preview.MetadataJson, out var current))
            return new(DerivedMetadataStatus.Current, current);

        if (!forceRefresh && PreviewRetryPolicy.MetadataDeferred(preview, probeVersion, _time.GetUtcNow()))
            return new(DerivedMetadataStatus.Deferred, Diagnostic: "Metadata retry is deferred after the previous failure.");

        await _concurrency.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var result = await _probe.ProbeAsync(observed.Asset.PhysicalPath, asset.MediaType,
                asset.FileSizeBytes, cancellationToken).ConfigureAwait(false);
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
            if (result.Status == DerivedMetadataStatus.Succeeded && result.Metadata is not null)
            {
                var normalized = JsonSerializer.Serialize(result.Metadata, DerivedMetadataJson.Options);
                await _previews.SetMetadataAsync(assetId, new(probeVersion, PreviewComponentState.Current,
                    PayloadJson: normalized, RawPayloadJson: result.RawMetadata, ExpectedSource: source), cancellationToken).ConfigureAwait(false);
                return new(DerivedMetadataStatus.Succeeded, result.Metadata);
            }

            await _previews.SetMetadataAsync(assetId, new(probeVersion, PreviewComponentState.Failed,
                PayloadJson: preview.MetadataJson, RawPayloadJson: preview.RawMetadataJson ?? result.RawMetadata, ExpectedSource: source), cancellationToken)
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

public static class DerivedMetadataJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };
}
