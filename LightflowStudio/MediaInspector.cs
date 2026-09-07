using System.IO;
using System.Globalization;
using System.Text.Json;

namespace LightflowStudio;

// Immutable inputs copied from the current Home context, never a second selection model.
internal sealed record InspectorAsset(Guid? AssetId, string Name, string RelativePath, MediaPresentationKind Kind,
    long? FileSizeBytes = null);
internal sealed record InspectorField(string Group, string Name, string Value, string? ComparisonValue = null);
internal sealed record InspectorRawField(string Source, string Path, string Value);
internal sealed record InspectorSnapshot(string Title, string Status, IReadOnlyList<InspectorField> Fields,
    IReadOnlyList<InspectorRawField> Raw, string? PreviewPath, bool RawTruncated = false);

/// <summary>Read-only projection of #70 and Catalog contracts. Work and intermediate raw snapshots are batched
/// off the UI thread; the multi-selection result has one row per supported field, independent of selection size.</summary>
internal sealed class MediaInspectorService(IPreviewStoreService? previews, IAssetClassificationStore classifications,
    string previewsDirectory)
{
    internal const int BatchSize = 128;
    internal const int MaximumRawFields = 10000;

    public Task<InspectorSnapshot> ReadAsync(IReadOnlyList<InspectorAsset> assets, CancellationToken token) =>
        Task.Run(async () =>
        {
            var summary = new Dictionary<(string Group, string Name), FieldSummary>();
            var states = new Dictionary<string, int>();
            var raw = new List<InspectorRawField>();
            string? previewPath = null;
            var rawTruncated = false;
            decimal totalSize = 0;
            double totalDuration = 0;
            int sizeCount = 0, durationCount = 0, videoCount = 0;
            foreach (var batch in assets.Chunk(BatchSize))
            {
                token.ThrowIfCancellationRequested();
                var ids = batch.Where(a => a.AssetId.HasValue).Select(a => a.AssetId!.Value).Distinct().ToArray();
                var records = previews is null ? new Dictionary<Guid, PreviewRecord>() :
                    await previews.GetManyAsync(ids, token).ConfigureAwait(false);
                var catalog = await classifications.GetAsync(ids, token).ConfigureAwait(false);
                foreach (var asset in batch)
                {
                    token.ThrowIfCancellationRequested();
                    var record = asset.AssetId is { } id ? records.GetValueOrDefault(id) : null;
                    DerivedMediaMetadata? metadata = null;
                    var state = MetadataState(record);
                    if (record?.MetadataState == PreviewComponentState.Current)
                    {
                        try { metadata = record.MetadataJson is { } json ? JsonSerializer.Deserialize<DerivedMediaMetadata>(json, DerivedMetadataJson.Options) : null; }
                        catch (JsonException) { state = "Metadata could not be read"; }
                        if (metadata is null) state = "Metadata could not be read";
                    }
                    states[state] = states.GetValueOrDefault(state) + 1;
                    var size = metadata?.FileSizeBytes ?? asset.FileSizeBytes;
                    if (size is >= 0) { totalSize += size.Value; sizeCount++; }
                    if (asset.Kind == MediaPresentationKind.Video)
                    {
                        videoCount++;
                        if (metadata?.DurationSeconds is { } duration && double.IsFinite(duration) && duration >= 0)
                        { totalDuration += duration; durationCount++; }
                    }
                    foreach (var field in Fields(asset, metadata,
                        asset.AssetId is { } aid ? catalog.GetValueOrDefault(aid) : null))
                    {
                        var key = (field.Group, field.Name);
                        if (!summary.TryGetValue(key, out var accumulator)) summary[key] = accumulator = new();
                        accumulator.Add(field.Value, field.ComparisonValue);
                    }
                    if (assets.Count == 1 && record is not null)
                    {
                        // A retained offline Preview is still useful; metadata freshness is stated separately.
                        var relative = record.StandardPreviewState == PreviewComponentState.Current
                            ? record.StandardPreviewRelativePath : record.ThumbnailRelativePath;
                        previewPath = ResolvePreviewPath(previewsDirectory, relative);
                        if (record.RawMetadataJson is { } rawJson)
                        {
                            try { raw = FlattenRaw(rawJson, null, token, out rawTruncated); }
                            catch (JsonException) { states["Raw snapshot could not be read"] = 1; }
                        }
                    }
                }
            }
            var fields = summary.Select(pair => new InspectorField(pair.Key.Group, pair.Key.Name,
                pair.Value.Describe(assets.Count))).ToList();
            if (assets.Count > 1)
            {
                fields.InsertRange(0, new[] {
                    new InspectorField("Selection", "Selected", assets.Count.ToString("N0")),
                    new InspectorField("Selection", "Total size", $"{Bytes(totalSize)} · known for {sizeCount:N0} of {assets.Count:N0}"),
                    new InspectorField("Selection", "Video duration", $"{Seconds(totalDuration)} · known for {durationCount:N0} of {videoCount:N0} videos") });
            }
            return new InspectorSnapshot(assets.Count == 1 ? assets[0].Name : $"{assets.Count:N0} selected assets",
                string.Join(" · ", states.Select(p => assets.Count == 1 ? p.Key : $"{p.Value:N0} {p.Key.ToLowerInvariant()}")),
                fields, raw, previewPath, rawTruncated);
        }, token);

    internal static string MetadataState(PreviewRecord? record) => record switch
    {
        null => "Metadata pending",
        { SourceAvailability: PreviewSourceAvailability.Missing } => "Source missing · cached information only",
        { SourceAvailability: PreviewSourceAvailability.Unavailable } => "Source unavailable · cached information only",
        { MetadataState: PreviewComponentState.Failed } => "Metadata failed",
        { MetadataState: PreviewComponentState.Stale } => "Metadata stale · awaiting refresh",
        { MetadataState: PreviewComponentState.Missing } => "Metadata pending",
        _ => "Metadata available"
    };

    private static IEnumerable<InspectorField> Fields(InspectorAsset asset, DerivedMediaMetadata? m, AssetClassification? c)
    {
        yield return new("File", "Name", asset.Name);
        yield return new("File", "Relative path", asset.RelativePath);
        yield return new("File", "Media type", asset.Kind.ToString());
        yield return new("File", "Size", (m?.FileSizeBytes ?? asset.FileSizeBytes) is { } size ? $"{Bytes(size)} ({size:N0} bytes)" : "");
        yield return new("File", "Container", m?.Container ?? "");
        if (m?.Image is { } image)
        {
            yield return new("Image", "Dimensions", $"{image.Width:N0} × {image.Height:N0} px");
            yield return new("Image", "Pixel depth", image.BitDepth is { } bits ? $"{bits} bits per pixel" : "");
            yield return new("Image", "Orientation", image.Orientation switch
            {
                1 => "Normal", 2 => "Mirrored horizontally", 3 => "Rotated 180°", 4 => "Mirrored vertically",
                5 => "Transposed", 6 => "Rotated 90° clockwise", 7 => "Transverse", 8 => "Rotated 90° counterclockwise",
                _ => image.Orientation?.ToString() ?? ""
            });
            if (image.CapturedAt is { Length: > 0 }) yield return new("Capture", "Recorded date", image.CapturedAt);
            if (image.CameraMake is { Length: > 0 }) yield return new("Camera", "Make", image.CameraMake);
            if (image.CameraModel is { Length: > 0 }) yield return new("Camera", "Model", image.CameraModel);
            if (image.LensModel is { Length: > 0 }) yield return new("Lens", "Model", image.LensModel);
        }
        if (asset.Kind == MediaPresentationKind.Video)
            yield return new("Video", "Duration", m?.DurationSeconds is { } seconds && double.IsFinite(seconds) ? Seconds(seconds) : "",
                m?.DurationSeconds?.ToString("R", CultureInfo.InvariantCulture));
        if (m?.Video is { } v)
        {
            yield return new("Video", "Codec", v.Codec);
            yield return new("Video", "Profile", v.Profile ?? "");
            yield return new("Video", "Dimensions", $"{v.Width:N0} × {v.Height:N0} px");
            yield return new("Video", "Frame rate", v.FrameRate is { } fps ? $"{fps:0.###} fps" : "",
                v.FrameRate?.ToString("R", CultureInfo.InvariantCulture));
            yield return new("Video", "Pixel format", v.PixelFormat ?? "");
            yield return new("Color", "Bit depth", v.BitDepth is { } bits ? $"{bits} bit" : "");
            yield return new("Color", "Space", v.ColorSpace ?? "");
            yield return new("Color", "Transfer", v.ColorTransfer ?? "");
            yield return new("Color", "Primaries", v.ColorPrimaries ?? "");
        }
        if (m?.Audio is { } a)
        {
            yield return new("Audio", "Codec", a.Codec);
            yield return new("Audio", "Channels", a.Channels?.ToString() ?? "");
            yield return new("Audio", "Channel layout", a.ChannelLayout ?? "");
            yield return new("Audio", "Sample rate", a.SampleRate is { } rate ? $"{rate:N0} Hz" : "");
            yield return new("Audio", "Bit rate", a.BitRate is { } bitRate ? $"{bitRate / 1000d:N0} kb/s" : "",
                a.BitRate?.ToString(CultureInfo.InvariantCulture));
        }
        yield return new("Lightflow Catalog", "Rating", c is null ? "" : c.Rating == 0 ? "Unrated" : $"{c.Rating} / 5");
        yield return new("Lightflow Catalog", "Flag", c?.Flag.ToString() ?? "");
        yield return new("Lightflow Catalog", "Color label", c is null ? "" : c.ColorLabel?.ToString() ?? "None");
        yield return new("Lightflow Catalog", "Keywords", c is null ? "" : c.Keywords.Count == 0 ? "None" :
            string.Join(", ", c.Keywords.Order(StringComparer.OrdinalIgnoreCase)));
    }

    internal sealed class FieldSummary
    {
        private string? _first;
        private string? _identity;
        private bool _mixed;
        private int _present;
        public void Add(string value, string? comparisonValue = null)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            _present++;
            if (_first is null) { _first = value; _identity = comparisonValue ?? value; }
            else if (!string.Equals(_identity, comparisonValue ?? value, StringComparison.Ordinal)) _mixed = true;
        }
        public string Describe(int total) => _present == 0 ? "Missing" :
            (_mixed ? "Mixed values" : _first + (total > 1 ? " · common" : "")) +
            (_present < total ? $" · {total - _present:N0} missing / not applicable" : "");
    }

    internal static string Bytes(decimal bytes) => bytes >= 1073741824 ? $"{bytes / 1073741824:N2} GB" :
        bytes >= 1048576 ? $"{bytes / 1048576:N2} MB" : bytes >= 1024 ? $"{bytes / 1024:N1} KB" : $"{bytes:N0} bytes";
    internal static string Seconds(double seconds)
    {
        seconds = Math.Round(seconds, 3);
        return $"{Math.Floor(seconds / 3600):00}:{Math.Floor(seconds / 60) % 60:00}:{seconds % 60:00.###}";
    }

    internal static string? ResolvePreviewPath(string root, string? relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative)) return null;
        var basePath = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(root, relative));
        return path.StartsWith(basePath, StringComparison.OrdinalIgnoreCase) ? path : null;
    }

    internal static List<InspectorRawField> FlattenRaw(string json, string? source, CancellationToken token, out bool truncated)
    {
        using var document = JsonDocument.Parse(json);
        // #70 has two snapshot shapes. RAW Browser category alone does not identify the provider.
        source ??= document.RootElement.ValueKind == JsonValueKind.Object &&
            (document.RootElement.TryGetProperty("streams", out _) || document.RootElement.TryGetProperty("format", out _))
            ? "FFprobe" : "WIC";
        var result = new List<InspectorRawField>();
        var limit = false;
        void Visit(JsonElement element, string path)
        {
            token.ThrowIfCancellationRequested();
            if (result.Count >= MaximumRawFields) { limit = true; return; }
            if (element.ValueKind == JsonValueKind.Object)
                foreach (var property in element.EnumerateObject())
                {
                    Visit(property.Value, path + "/" + property.Name.Replace("~", "~0").Replace("/", "~1"));
                    if (limit) break;
                }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    Visit(item, path + "/" + index++);
                    if (limit) break;
                }
            }
            else result.Add(new(source, path, element.ValueKind == JsonValueKind.String ? element.GetString() ?? "" : element.GetRawText()));
        }
        Visit(document.RootElement, "");
        truncated = limit;
        return result;
    }

    internal static IReadOnlyList<InspectorRawField> SearchRaw(IReadOnlyList<InspectorRawField> rows, string query) =>
        rows.Where(r => r.Source.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            r.Path.Contains(query, StringComparison.OrdinalIgnoreCase) || r.Value.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
}
