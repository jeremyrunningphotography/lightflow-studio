using System.Globalization;
using System.Text.Json;

namespace LightflowStudio;

internal enum BrowserSortMode { Name, CaptureDate, ModifiedDate, MediaType, FileSize, Duration }

/// <summary>
/// Browser-only creator-facing frame-rate normalization. Authoritative Preview metadata keeps its precise
/// value; this boundary supplies stable query buckets without changing playback/export/diagnostic truth.
/// </summary>
internal static class BrowserFrameRate
{
    internal const double StandardRateRelativeTolerance = 0.005;
    private static readonly double[] StandardRates = [23.976, 24, 25, 29.97, 30, 50, 59.94, 60];

    public static double? Canonicalize(double? observed)
    {
        if (observed is not { } value || !double.IsFinite(value) || value <= 0) return null;

        var nearest = StandardRates
            .Select(standard => (Standard: standard, Distance: Math.Abs(value - standard)))
            .OrderBy(candidate => candidate.Distance)
            .ThenBy(candidate => candidate.Standard)
            .First();
        return nearest.Distance <= nearest.Standard * StandardRateRelativeTolerance
            ? nearest.Standard
            : Math.Round(value, 3, MidpointRounding.AwayFromZero);
    }
}

/// <summary>
/// The field a <see cref="BrowserFilterPredicate"/> constrains. Only <see cref="MediaType"/> is implemented;
/// this enum exists so later predicate kinds (date, file size, duration, camera, lens, resolution, frame
/// rate, rating, labels, flags, keywords) extend the same representation rather than requiring a redesign.
/// </summary>
internal enum BrowserFilterField
{
    MediaType,
    Camera,
    Lens,
    CaptureDate,
    Duration,
    Resolution,
    FrameRate,
    ColorState,
    CameraLutState,
    CreativeLutState,
    ReviewRangeState,
    SubclipState
}

/// <summary>
/// One stackable filter condition (e.g. "Video"). Multiple active predicates combine with AND semantics —
/// no OR/grouping UI yet. Deliberately plain, equatable data (not a stored delegate) so two predicates
/// describing the same condition are structurally equal, and so a future Smart Collection can persist this
/// shape directly as saved query intent. <see cref="Matches"/> and <see cref="Label"/> are computed, not
/// stored, so they never affect equality.
/// </summary>
internal sealed record BrowserFilterPredicate
{
    public required BrowserFilterField Field { get; init; }

    /// <summary>Populated when <see cref="Field"/> is <see cref="BrowserFilterField.MediaType"/>.</summary>
    public MediaTypeCategory? MediaTypeValue { get; init; }
    public string? TextValue { get; init; }
    public double? NumberValue { get; init; }
    public double? NumberValue2 { get; init; }
    public DateTime? DateFrom { get; init; }
    public DateTime? DateTo { get; init; }
    public bool? BooleanValue { get; init; }

    public static BrowserFilterPredicate ForMediaType(MediaTypeCategory category) =>
        new() { Field = BrowserFilterField.MediaType, MediaTypeValue = category };
    public static BrowserFilterPredicate ForText(BrowserFilterField field, string value) =>
        new() { Field = field, TextValue = value };
    public static BrowserFilterPredicate ForMinimum(BrowserFilterField field, double value) =>
        new() { Field = field, NumberValue = value };
    public static BrowserFilterPredicate ForFrameRate(double value) =>
        new() { Field = BrowserFilterField.FrameRate, NumberValue = BrowserFrameRate.Canonicalize(value) };
    public static BrowserFilterPredicate ForResolution(int width, int height) =>
        new() { Field = BrowserFilterField.Resolution, NumberValue = width, NumberValue2 = height };
    public static BrowserFilterPredicate ForDateRange(DateTime? from, DateTime? to) =>
        new() { Field = BrowserFilterField.CaptureDate, DateFrom = from?.Date, DateTo = to?.Date };
    public static BrowserFilterPredicate ForState(BrowserFilterField field, bool value) =>
        new() { Field = field, BooleanValue = value };

    /// <summary>Compact chip text, e.g. "Video".</summary>
    public string Label => Field switch
    {
        BrowserFilterField.MediaType => MediaTypeValue switch
        {
            MediaTypeCategory.StillImage => "Images",
            MediaTypeCategory.RawImage => "RAW",
            MediaTypeCategory.Video => "Video",
            _ => "Media type"
        },
        BrowserFilterField.Camera => $"Camera: {TextValue}",
        BrowserFilterField.Lens => $"Lens: {TextValue}",
        BrowserFilterField.CaptureDate => DateRangeLabel(),
        BrowserFilterField.Duration => $"Duration ≥ {FormatDuration(NumberValue)}",
        BrowserFilterField.Resolution => $"Resolution: {NumberValue:0}×{NumberValue2:0}",
        BrowserFilterField.FrameRate => $"Frame rate: {BrowserFrameRate.Canonicalize(NumberValue):0.###} fps",
        BrowserFilterField.ColorState => BooleanValue == true ? "Color applied" : "Original color",
        BrowserFilterField.CameraLutState => BooleanValue == true ? "Camera LUT assigned" : "No Camera LUT",
        BrowserFilterField.CreativeLutState => BooleanValue == true ? "Creative LUT assigned" : "No Creative LUT",
        BrowserFilterField.ReviewRangeState => BooleanValue == true ? "Has saved range" : "No saved range",
        BrowserFilterField.SubclipState => BooleanValue == true ? "Has Subclips" : "No Subclips",
        _ => "Filter"
    };

    public string RemoveAutomationLabel => $"Remove {Label} filter";

    public bool Matches(BrowserGridTile tile) => Field switch
    {
        BrowserFilterField.MediaType => MediaTypeValue is null || tile.Category == MediaTypeValue,
        BrowserFilterField.Camera => tile.MetadataApplied && TextEquals(tile.CameraDisplayName, TextValue),
        BrowserFilterField.Lens => tile.MetadataApplied && TextEquals(tile.LensModel, TextValue),
        BrowserFilterField.CaptureDate => tile.MetadataApplied && tile.CaptureDate is { } captured &&
            (DateFrom is null || captured.Date >= DateFrom.Value.Date) &&
            (DateTo is null || captured.Date <= DateTo.Value.Date),
        BrowserFilterField.Duration => tile.MetadataApplied && tile.DurationSeconds is { } duration &&
            NumberValue is { } minimumDuration && duration >= minimumDuration,
        BrowserFilterField.Resolution => tile.MetadataApplied && tile.PixelWidth == NumberValue && tile.PixelHeight == NumberValue2,
        BrowserFilterField.FrameRate => tile.MetadataApplied && BrowserFrameRate.Canonicalize(tile.FrameRate) is { } frameRate &&
            BrowserFrameRate.Canonicalize(NumberValue) is { } expected && frameRate == expected,
        BrowserFilterField.ColorState => MatchesState(tile, tile.HasColorState),
        BrowserFilterField.CameraLutState => MatchesState(tile, tile.HasCameraLut),
        BrowserFilterField.CreativeLutState => MatchesState(tile, tile.HasCreativeLut),
        BrowserFilterField.ReviewRangeState => MatchesState(tile, tile.HasReviewRange),
        BrowserFilterField.SubclipState => MatchesState(tile, tile.HasSubclips),
        _ => true
    };

    private bool MatchesState(BrowserGridTile tile, bool actual) =>
        tile.AssetStateApplied && BooleanValue is { } expected && actual == expected;

    private static bool TextEquals(string? actual, string? expected) =>
        actual is not null && expected is not null && string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);

    private string DateRangeLabel() => (DateFrom, DateTo) switch
    {
        ({ } from, { } to) => $"Capture date: {from:d} – {to:d}",
        ({ } from, null) => $"Capture date ≥ {from:d}",
        (null, { } to) => $"Capture date ≤ {to:d}",
        _ => "Capture date"
    };

    private static string FormatDuration(double? seconds) => seconds switch
    {
        >= 3600 => TimeSpan.FromSeconds(seconds.Value).ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture),
        > 0 => TimeSpan.FromSeconds(seconds.Value).ToString(@"m\:ss", CultureInfo.InvariantCulture),
        _ => "0:00"
    };
}

/// <summary>
/// The current media-area presentation intent (sort, filter, search) over the Browser's active scope.
/// Deliberately a small, self-contained value with no WPF/UI coupling and no dependency on how the current
/// scope was reached (filesystem folder today; Favorite/Collection/Smart Collection later per #74) — a
/// future Smart Collection can capture one of these as saved query intent without any transient UI control
/// ever being coupled directly to Collection persistence.
/// </summary>
internal sealed record BrowserQuery
{
    public BrowserSortMode SortMode { get; init; } = BrowserSortMode.Name;
    public bool SortDescending { get; init; }

    /// <summary>Empty means every presentable Browser media category (#108: still image, RAW, video) — no active filters.</summary>
    public IReadOnlyList<BrowserFilterPredicate> Filters { get; init; } = [];

    public string SearchText { get; init; } = "";

    public static BrowserQuery Default { get; } = new();

    public BrowserQuery WithFilterAdded(BrowserFilterPredicate predicate) =>
        Filters.Contains(predicate) ? this : this with { Filters = [.. Filters, predicate] };

    public BrowserQuery WithFilterRemoved(BrowserFilterPredicate predicate) =>
        Filters.Contains(predicate) ? this with { Filters = Filters.Where(existing => existing != predicate).ToArray() } : this;

    /// <summary>Clears every active predicate for one field (e.g. a persistent "All" button clearing the media-type facet entirely).</summary>
    public BrowserQuery WithoutField(BrowserFilterField field) =>
        Filters.Any(existing => existing.Field == field)
            ? this with { Filters = Filters.Where(existing => existing.Field != field).ToArray() }
            : this;
}

/// <summary>
/// Pure filter/search/sort over an already-populated tile set. Operates entirely on data already resident
/// on each <see cref="BrowserGridTile"/> (Catalog/Preview-indexed or filesystem-enumerated); it never touches
/// the filesystem, Catalog, or Preview store itself, so applying a query can never synchronously probe a
/// source file.
/// </summary>
internal static class BrowserQueryEngine
{
    private static readonly string[] ExifDateFormats = ["yyyy:MM:dd HH:mm:ss", "yyyy:MM:dd"];

    public static IReadOnlyList<BrowserGridTile> Apply(IReadOnlyList<BrowserGridTile> tiles, BrowserQuery query)
    {
        IEnumerable<BrowserGridTile> filtered = tiles;
        // Predicates for the SAME field are alternative values of one facet and OR together (checking both
        // "Images" and "RAW" means either is acceptable — a still-photos view, not an impossible
        // intersection); predicates for DIFFERENT fields AND together, each narrowing the previous group's
        // result further (e.g. "Video" AND "Duration > 1:00"). This mirrors ordinary faceted search and is
        // never exposed to the user as an explicit AND/OR choice — only which values are checked where.
        foreach (var group in query.Filters.GroupBy(predicate => predicate.Field))
        {
            var predicatesInGroup = group.ToArray();
            filtered = filtered.Where(tile => predicatesInGroup.Any(predicate => predicate.Matches(tile)));
        }

        var search = query.SearchText.Trim();
        if (search.Length > 0)
            filtered = filtered.Where(tile => tile.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                tile.RelativePath.Contains(search, StringComparison.OrdinalIgnoreCase));

        return Sort(filtered.ToArray(), query.SortMode, query.SortDescending);
    }

    /// <summary>
    /// The universe from which the advanced Filter dropdown discovers available values: current media-type
    /// toolbar predicates plus text search, deliberately before every advanced predicate. This keeps choice
    /// discovery contextual without allowing an advanced facet to recursively erase its alternatives.
    /// Operates only on already-resident Browser tiles.
    /// </summary>
    public static IReadOnlyList<BrowserGridTile> ApplyAdvancedFilterContext(
        IReadOnlyList<BrowserGridTile> tiles, BrowserQuery query)
    {
        IEnumerable<BrowserGridTile> contextual = tiles;
        var mediaTypes = query.Filters.Where(predicate => predicate.Field == BrowserFilterField.MediaType).ToArray();
        if (mediaTypes.Length > 0)
            contextual = contextual.Where(tile => mediaTypes.Any(predicate => predicate.Matches(tile)));
        var search = query.SearchText.Trim();
        if (search.Length > 0)
            contextual = contextual.Where(tile => tile.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                tile.RelativePath.Contains(search, StringComparison.OrdinalIgnoreCase));
        return contextual.ToArray();
    }

    /// <summary>
    /// EXIF <c>DateTimeOriginal</c>/<c>DateTime</c> tags carry no timezone; the parsed value is therefore an
    /// unqualified local wall-clock reading used only for relative ordering, never displayed as an absolute
    /// instant. Returns null (rather than throwing) for missing/malformed text, matching #91/#92's existing
    /// stance that a Browser media item can always lack this optional metadata.
    /// </summary>
    public static DateTime? ParseExifCaptureDate(string? raw) =>
        !string.IsNullOrWhiteSpace(raw) &&
        DateTime.TryParseExact(raw.Trim(), ExifDateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : null;

    /// <summary>
    /// Projects the Browser-queryable subset of a #91 <c>PreviewRecords.MetadataJson</c> payload.
    /// Malformed/unexpected JSON yields an empty projection rather than throwing — a Browser tile can
    /// always simply lack any optional normalized metadata field.
    /// </summary>
    public static BrowserTechnicalMetadata ExtractMetadata(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson)) return BrowserTechnicalMetadata.Empty;
        DerivedMediaMetadata? metadata;
        try { metadata = JsonSerializer.Deserialize<DerivedMediaMetadata>(metadataJson, DerivedMetadataJson.Options); }
        catch (JsonException) { return BrowserTechnicalMetadata.Empty; }
        if (metadata is null) return BrowserTechnicalMetadata.Empty;
        var image = metadata.Image;
        var video = metadata.Video;
        return new BrowserTechnicalMetadata(
            ParseExifCaptureDate(image?.CapturedAt), metadata.DurationSeconds,
            image?.CameraMake, image?.CameraModel, image?.LensModel,
            image?.Width > 0 ? image.Width : video?.Width > 0 ? video.Width : null,
            image?.Height > 0 ? image.Height : video?.Height > 0 ? video.Height : null,
            video?.FrameRate);
    }

    public static (DateTime? CaptureDate, double? DurationSeconds) ExtractSortableMetadata(string? metadataJson)
    {
        var metadata = ExtractMetadata(metadataJson);
        return (metadata.CaptureDate, metadata.DurationSeconds);
    }

    private static IReadOnlyList<BrowserGridTile> Sort(IReadOnlyList<BrowserGridTile> tiles, BrowserSortMode mode, bool descending)
    {
        if (mode is BrowserSortMode.CaptureDate or BrowserSortMode.Duration or BrowserSortMode.FileSize)
        {
            // Items that cannot be sorted meaningfully by this criterion (missing capture date/duration/size)
            // always sit at the very end, in a stable name order, regardless of ascending/descending — so
            // toggling direction never makes "unknown" items jump to the top.
            var withValue = OrderAscending(tiles.Where(HasValue(mode)).ToArray(), mode);
            if (descending) withValue = [.. withValue.Reverse()];
            var withoutValue = tiles.Where(tile => !HasValue(mode)(tile))
                .OrderBy(tile => tile.Name, StringComparer.OrdinalIgnoreCase).ThenBy(tile => tile.Key, StringComparer.Ordinal);
            return [.. withValue, .. withoutValue];
        }

        var ordered = OrderAscending(tiles, mode);
        return descending ? [.. ordered.Reverse()] : ordered;
    }

    private static Func<BrowserGridTile, bool> HasValue(BrowserSortMode mode) => mode switch
    {
        BrowserSortMode.CaptureDate => tile => tile.CaptureDate is not null,
        BrowserSortMode.Duration => tile => tile.DurationSeconds is not null,
        BrowserSortMode.FileSize => tile => tile.FileSizeBytes is not null,
        _ => _ => true
    };

    private static IReadOnlyList<BrowserGridTile> OrderAscending(IReadOnlyList<BrowserGridTile> tiles, BrowserSortMode mode)
    {
        IOrderedEnumerable<BrowserGridTile> ordered = mode switch
        {
            BrowserSortMode.Name => tiles.OrderBy(tile => tile.Name, StringComparer.OrdinalIgnoreCase),
            BrowserSortMode.CaptureDate => tiles.OrderBy(tile => tile.CaptureDate),
            BrowserSortMode.ModifiedDate => tiles.OrderBy(tile => tile.ModifiedUtc),
            BrowserSortMode.MediaType => tiles.OrderBy(tile => CategoryRank(tile.Category)),
            BrowserSortMode.FileSize => tiles.OrderBy(tile => tile.FileSizeBytes),
            BrowserSortMode.Duration => tiles.OrderBy(tile => tile.DurationSeconds),
            _ => tiles.OrderBy(tile => tile.Name, StringComparer.OrdinalIgnoreCase)
        };
        // A deterministic tie-breaker beneath every sort mode: two items with the same size/type/date never
        // reorder unpredictably between passes, and Name-mode itself needs no separate primary/secondary split.
        return [.. ordered.ThenBy(tile => tile.Name, StringComparer.OrdinalIgnoreCase).ThenBy(tile => tile.Key, StringComparer.Ordinal)];
    }

    private static int CategoryRank(MediaTypeCategory category) => category switch
    {
        MediaTypeCategory.StillImage => 0,
        MediaTypeCategory.RawImage => 1,
        MediaTypeCategory.Video => 2,
        _ => 3
    };
}

internal sealed record BrowserTechnicalMetadata(
    DateTime? CaptureDate,
    double? DurationSeconds,
    string? CameraMake,
    string? CameraModel,
    string? LensModel,
    int? PixelWidth,
    int? PixelHeight,
    double? FrameRate)
{
    public static BrowserTechnicalMetadata Empty { get; } = new(null, null, null, null, null, null, null, null);
}

internal sealed record BrowserFilterOption(
    BrowserFilterPredicate Predicate,
    bool IsActive,
    bool IsEnabled = true,
    int? ContextCount = null)
{
    public string Label => Predicate.Label;
    public string DisplayLabel => ContextCount is { } count ? $"{Label} ({count})" : Label;
    public string DescriptiveValueLabel => Predicate.Field switch
    {
        BrowserFilterField.Camera or BrowserFilterField.Lens => Predicate.TextValue ?? Label,
        BrowserFilterField.Resolution => $"{Predicate.NumberValue:0}×{Predicate.NumberValue2:0}",
        BrowserFilterField.FrameRate => $"{BrowserFrameRate.Canonicalize(Predicate.NumberValue):0.###} fps",
        _ => Label
    };
}
