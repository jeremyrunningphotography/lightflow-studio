using System.Globalization;
using System.Text.Json;

namespace LightflowStudio;

internal enum BrowserSortMode { Name, CaptureDate, ModifiedDate, MediaType, FileSize, Duration }

/// <summary>
/// The field a <see cref="BrowserFilterPredicate"/> constrains. Only <see cref="MediaType"/> is implemented;
/// this enum exists so later predicate kinds (date, file size, duration, camera, lens, resolution, frame
/// rate, rating, labels, flags, keywords) extend the same representation rather than requiring a redesign.
/// </summary>
internal enum BrowserFilterField { MediaType }

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

    public static BrowserFilterPredicate ForMediaType(MediaTypeCategory category) =>
        new() { Field = BrowserFilterField.MediaType, MediaTypeValue = category };

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
        _ => "Filter"
    };

    public string RemoveAutomationLabel => $"Remove {Label} filter";

    public bool Matches(BrowserGridTile tile) => Field switch
    {
        BrowserFilterField.MediaType => MediaTypeValue is null || tile.Category == MediaTypeValue,
        _ => true
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

    /// <summary>Replaces every predicate for <paramref name="predicate"/>'s field with exactly this one — a quick single-value pick (e.g. a persistent "Video" button) rather than a stackable add.</summary>
    public BrowserQuery WithOnlyFilter(BrowserFilterPredicate predicate) =>
        this with { Filters = [.. Filters.Where(existing => existing.Field != predicate.Field), predicate] };

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
    /// Extracts the two Browser-sortable fields from a #91 <c>PreviewRecords.MetadataJson</c> payload:
    /// image capture date and video duration. Malformed/unexpected JSON yields (null, null) rather than
    /// throwing — a Browser tile can always simply lack this optional metadata.
    /// </summary>
    public static (DateTime? CaptureDate, double? DurationSeconds) ExtractSortableMetadata(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson)) return (null, null);
        DerivedMediaMetadata? metadata;
        try { metadata = JsonSerializer.Deserialize<DerivedMediaMetadata>(metadataJson, DerivedMetadataJson.Options); }
        catch (JsonException) { return (null, null); }
        return metadata is null ? (null, null) : (ParseExifCaptureDate(metadata.Image?.CapturedAt), metadata.DurationSeconds);
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
