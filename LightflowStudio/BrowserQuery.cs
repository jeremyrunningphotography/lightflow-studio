using System.Globalization;
using System.Text.Json;

namespace LightflowStudio;

internal enum BrowserSortMode { Name, CaptureDate, ModifiedDate, MediaType, FileSize, Duration }

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

    /// <summary>Null means every presentable Browser media category (#108: still image, RAW, video).</summary>
    public MediaTypeCategory? MediaFilter { get; init; }

    public string SearchText { get; init; } = "";

    public static BrowserQuery Default { get; } = new();
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
        if (query.MediaFilter is { } filter) filtered = filtered.Where(tile => tile.Category == filter);

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
