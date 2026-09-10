using System.Globalization;
using System.Windows.Data;

namespace LightflowStudio;

internal enum BrowserLayoutMode { Grid, Details }

internal sealed record BrowserDetailsColumn(string Id, string Title, double Width, bool Visible,
    BrowserSortMode? Sort = null);

internal sealed record WorkspaceDetailsColumn
{
    public string Id { get; init; } = "";
    public double Width { get; init; }
    public bool Visible { get; init; }
}

/// <summary>Stable, curated columns over the resident Browser projection. No data access belongs here.</summary>
internal static class BrowserDetails
{
    public const double RowHeight = 38;
    public static IReadOnlyList<BrowserDetailsColumn> Columns { get; } =
    [
        new("preview", "Preview", 68, true),
        new("name", "Name", 240, true, BrowserSortMode.Name),
        new("rating", "Rating", 88, true, BrowserSortMode.Rating),
        new("flag", "Flag", 86, true, BrowserSortMode.Flag),
        new("capture-date", "Capture Date", 158, true, BrowserSortMode.CaptureDate),
        new("media-type", "Media Type", 95, true, BrowserSortMode.MediaType),
        new("dimensions", "Dimensions", 112, true, BrowserSortMode.Dimensions),
        new("duration", "Duration", 88, true, BrowserSortMode.Duration),
        new("frame-rate", "Frame Rate", 95, true, BrowserSortMode.FrameRate),
        new("file-size", "File Size", 95, true, BrowserSortMode.FileSize),
        new("modified-date", "Modified Date", 158, false, BrowserSortMode.ModifiedDate),
        new("color-label", "Color Label", 100, false),
        new("camera", "Camera", 180, false),
        new("lens", "Lens", 180, false),
        new("color", "Color / LUT", 180, false),
        new("range", "Saved Range", 100, false),
        new("subclips", "Subclips", 80, false),
        new("keywords", "Keywords", 220, false)
    ];

    public static IReadOnlyList<WorkspaceDetailsColumn> Normalize(IReadOnlyList<WorkspaceDetailsColumn>? saved)
    {
        var known = Columns.ToDictionary(c => c.Id);
        var seen = new HashSet<string>();
        var result = new List<WorkspaceDetailsColumn>();
        foreach (var column in saved ?? [])
            if (column is not null && known.TryGetValue(column.Id ?? "", out var definition) && seen.Add(definition.Id))
                result.Add(column with { Width = double.IsFinite(column.Width) && column.Width > 0
                    ? Math.Clamp(column.Width, 40, 1200) : definition.Width });
        foreach (var column in Columns.Where(c => !seen.Contains(c.Id)))
            result.Add(new() { Id = column.Id, Width = column.Width, Visible = column.Visible });
        if (!result.Any(c => c.Visible))
            result[result.FindIndex(c => c.Id == "name")] = result.First(c => c.Id == "name") with { Visible = true };
        return result;
    }

    public static BrowserQuery Sort(BrowserQuery query, BrowserSortMode mode) => query with
    { SortMode = mode, SortDescending = query.SortMode == mode && !query.SortDescending };

    public static string Text(BrowserGridTile tile, string id) => id switch
    {
        "name" => tile.Name,
        "rating" => tile.AssetStateApplied ? tile.RatingText : "",
        "flag" => tile.AssetStateApplied && tile.Flag != AssetFlag.Unflagged ? tile.Flag.ToString() : "",
        "capture-date" => tile.CaptureDate?.ToString("g") ?? "",
        "media-type" => tile.Category switch { MediaTypeCategory.StillImage => "Image", MediaTypeCategory.RawImage => "RAW", _ => "Video" },
        "dimensions" => tile.PixelWidth is > 0 && tile.PixelHeight is > 0 ? $"{tile.PixelWidth} × {tile.PixelHeight}" : "",
        "duration" => tile.DurationSeconds is > 0 ? BrowserFilterPredicate.FormatDuration(tile.DurationSeconds) : "",
        "frame-rate" => BrowserFrameRate.Canonicalize(tile.FrameRate) is { } rate ? $"{rate:0.###} fps" : "",
        "file-size" => tile.FileSizeBytes is { } size ? size >= 1048576 ? $"{size / 1048576d:0.#} MB" : $"{size / 1024d:0.#} KB" : "",
        "modified-date" => tile.ModifiedUtc == default ? "" : tile.ModifiedUtc.LocalDateTime.ToString("g"),
        "color-label" => tile.ColorLabel?.ToString() ?? "",
        "camera" => tile.CameraDisplayName ?? "",
        "lens" => tile.LensModel ?? "",
        "color" => string.Join(", ", new[] { tile.HasCameraLut ? "Camera LUT" : null, tile.HasCreativeLut ? "Creative LUT" : null,
            tile.HasColorState && !tile.HasCameraLut && !tile.HasCreativeLut ? "Applied" : null }.Where(s => s is not null)),
        "range" => tile.HasReviewRange ? "Saved" : "",
        "subclips" => tile.AssetStateApplied && tile.Category == MediaTypeCategory.Video ? tile.SubclipCount.ToString() : "",
        "keywords" => string.Join(", ", tile.Keywords),
        _ => ""
    };
}

internal sealed class BrowserDetailsTextConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values.FirstOrDefault() is BrowserGridTile tile ? BrowserDetails.Text(tile, (string)parameter) : "";
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
