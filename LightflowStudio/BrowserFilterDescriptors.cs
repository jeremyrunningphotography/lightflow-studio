namespace LightflowStudio;

internal enum BrowserFilterEditorKind { Choices, Rating, DateRanges, Text }

/// <summary>Shared field presentation and value discovery. Predicates remain the sole evaluator.
/// New faceted fields register their value source here for both Browser and saved filters.</summary>
internal sealed record BrowserFilterDescriptor(BrowserFilterField Field, string Name, BrowserFilterEditorKind Editor,
    Func<IReadOnlyList<BrowserGridTile>, IEnumerable<BrowserFilterPredicate>> Values);

internal static class BrowserFilterDescriptors
{
    private static BrowserFilterDescriptor Choices(BrowserFilterField field, string name,
        Func<IReadOnlyList<BrowserGridTile>, IEnumerable<BrowserFilterPredicate>> values) =>
        new(field, name, BrowserFilterEditorKind.Choices, values);
    private static IEnumerable<BrowserFilterPredicate> TextValues(BrowserFilterField field, IEnumerable<string?> values) =>
        values.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase).Select(value => BrowserFilterPredicate.ForText(field, value!));
    public static IReadOnlyList<BrowserFilterDescriptor> All { get; } =
    [
        Choices(BrowserFilterField.MediaType, "Media Type", _ => BrowserGridModel.PresentableCategories.Select(BrowserFilterPredicate.ForMediaType)),
        Choices(BrowserFilterField.Camera, "Camera", tiles => TextValues(BrowserFilterField.Camera, tiles.Select(t => t.CameraDisplayName))),
        Choices(BrowserFilterField.Lens, "Lens", tiles => TextValues(BrowserFilterField.Lens, tiles.Select(t => t.LensModel))),
        new(BrowserFilterField.CaptureDate, "Capture Date", BrowserFilterEditorKind.DateRanges, _ => []),
        Choices(BrowserFilterField.Duration, "Duration", tiles => new[] { 10d, 30d, 60d, 300d }
            .Where(n => tiles.Any(t => t.MetadataApplied && t.DurationSeconds >= n) && tiles.Any(t => t.MetadataApplied && t.DurationSeconds > 0 && t.DurationSeconds < n))
            .Select(n => BrowserFilterPredicate.ForMinimum(BrowserFilterField.Duration, n))),
        Choices(BrowserFilterField.Resolution, "Resolution", tiles => tiles.Where(t => t.PixelWidth is > 0 && t.PixelHeight is > 0)
            .Select(t => (W: t.PixelWidth!.Value, H: t.PixelHeight!.Value)).Distinct().OrderBy(s => (long)s.W * s.H)
            .Select(s => BrowserFilterPredicate.ForResolution(s.W, s.H))),
        Choices(BrowserFilterField.FrameRate, "Frame Rate", tiles => tiles.Select(t => BrowserFrameRate.Canonicalize(t.FrameRate))
            .Where(n => n is not null).Select(n => n!.Value).Distinct().OrderBy(n => n).Select(BrowserFilterPredicate.ForFrameRate)),
        State(BrowserFilterField.ColorState, "Color"),
        State(BrowserFilterField.CameraLutState, "Camera LUT"),
        State(BrowserFilterField.CreativeLutState, "Creative LUT"),
        State(BrowserFilterField.ReviewRangeState, "Saved Range"),
        State(BrowserFilterField.SubclipState, "Subclips"),
        new(BrowserFilterField.Rating, "Rating", BrowserFilterEditorKind.Rating, _ => []),
        Choices(BrowserFilterField.Flag, "Flag", _ => BrowserClassificationFilterChoices.Flags),
        Choices(BrowserFilterField.ColorLabel, "Color Label", _ => BrowserClassificationFilterChoices.ColorLabels),
        Choices(BrowserFilterField.Keyword, "Keywords", tiles => BrowserClassificationFilterChoices.Keywords(tiles.SelectMany(t => t.Keywords))),
        new(BrowserFilterField.FileOrPath, "File or path", BrowserFilterEditorKind.Text, _ => [])
    ];
    private static BrowserFilterDescriptor State(BrowserFilterField field, string name) => Choices(field, name,
        _ => new[] { BrowserFilterPredicate.ForState(field, true), BrowserFilterPredicate.ForState(field, false) });
    public static BrowserFilterDescriptor Get(BrowserFilterField field) => All.Single(d => d.Field == field);
    public static IEnumerable<BrowserFilterPredicate> Values(BrowserFilterField field, IReadOnlyList<BrowserGridTile> tiles) => Get(field).Values(tiles);
    public static string ValueLabel(BrowserFilterPredicate predicate)
    {
        var label = predicate.Label;
        var colon = label.IndexOf(": ", StringComparison.Ordinal);
        return colon < 0 ? label : label[(colon + 2)..];
    }
}
