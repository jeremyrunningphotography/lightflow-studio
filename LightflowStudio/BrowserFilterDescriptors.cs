namespace LightflowStudio;

internal enum BrowserFilterEditorKind { Choices, Rating, DateRanges, Text, Structured, State }

/// <summary>Shared field presentation and value discovery. Predicates remain the sole evaluator.
/// New faceted fields register their value source here for both Browser and saved filters.</summary>
internal sealed record BrowserFilterDescriptor(BrowserFilterField Field, string Name, BrowserFilterEditorKind Editor,
    Func<IReadOnlyList<BrowserGridTile>, IEnumerable<BrowserFilterPredicate>> Values)
{
    public BrowserStructuredFilterInput? Input { get; init; }
    public string[] StateOperators { get; init; } = [];
    public bool OfferSuggestions { get; init; } = true;
    public string SuggestionsLabel { get; init; } = "Values";
    public Func<IReadOnlyList<BrowserGridTile>, IEnumerable<BrowserFilterPredicate>>? Suggestions { get; init; }
    public bool CanAuthor(IReadOnlyList<BrowserGridTile> tiles) => Editor != BrowserFilterEditorKind.Choices || Values(tiles).Any();
}

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
        new(BrowserFilterField.Duration, "Duration", BrowserFilterEditorKind.Structured, tiles => new[] { 10d, 30d, 60d, 300d }
            .Where(n => tiles.Any(t => t.MetadataApplied && t.DurationSeconds >= n) && tiles.Any(t => t.MetadataApplied && t.DurationSeconds > 0 && t.DurationSeconds < n))
            .Select(n => BrowserFilterPredicate.ForMinimum(BrowserFilterField.Duration, n))) { Input = BrowserStructuredFilterInput.Duration, OfferSuggestions = false },
        new(BrowserFilterField.Resolution, "Resolution", BrowserFilterEditorKind.Structured, tiles => tiles.Where(t => t.PixelWidth is > 0 && t.PixelHeight is > 0)
            .Select(t => (W: t.PixelWidth!.Value, H: t.PixelHeight!.Value)).Distinct().OrderBy(s => (long)s.W * s.H)
            .Select(s => BrowserFilterPredicate.ForResolution(s.W, s.H))) { Input = BrowserStructuredFilterInput.Resolution, SuggestionsLabel = "Sizes" },
        new(BrowserFilterField.FrameRate, "Frame Rate", BrowserFilterEditorKind.Structured, tiles => tiles.Select(t => BrowserFrameRate.Canonicalize(t.FrameRate))
            .Where(n => n is not null).Select(n => n!.Value).Distinct().OrderBy(n => n).Select(BrowserFilterPredicate.ForFrameRate))
            { Input = BrowserStructuredFilterInput.FrameRate, SuggestionsLabel = "Rates", Suggestions = tiles => MediaFrameRate.Canonical.Select(r => BrowserFilterPredicate.ForFrameRate(r.DisplayValue))
                .Concat(Values(BrowserFilterField.FrameRate, tiles)).Distinct().OrderBy(p => p.NumberValue) },
        State(BrowserFilterField.ColorState, "Color", "is applied", "is original", "is applied or original"),
        State(BrowserFilterField.CameraLutState, "Camera LUT", "is assigned", "is not assigned", "is assigned or not assigned"),
        State(BrowserFilterField.CreativeLutState, "Creative LUT", "is assigned", "is not assigned", "is assigned or not assigned"),
        State(BrowserFilterField.ReviewRangeState, "In/Out Range", "is set", "is not set", "is set or not set"),
        State(BrowserFilterField.SubclipState, "Subclips", "exist", "do not exist", "exist or do not exist"),
        new(BrowserFilterField.Rating, "Rating", BrowserFilterEditorKind.Rating, _ => []),
        Choices(BrowserFilterField.Flag, "Flag", _ => BrowserClassificationFilterChoices.Flags),
        Choices(BrowserFilterField.ColorLabel, "Color Label", _ => BrowserClassificationFilterChoices.ColorLabels),
        Choices(BrowserFilterField.Keyword, "Keywords", tiles => BrowserClassificationFilterChoices.Keywords(tiles.SelectMany(t => t.Keywords))),
        new(BrowserFilterField.FileOrPath, "File or path", BrowserFilterEditorKind.Text, _ => [])
    ];
    private static BrowserFilterDescriptor State(BrowserFilterField field, string name, params string[] operators) =>
        new(field, name, BrowserFilterEditorKind.State,
            _ => new[] { BrowserFilterPredicate.ForState(field, true), BrowserFilterPredicate.ForState(field, false) }) { StateOperators = operators };
    public static BrowserFilterDescriptor Get(BrowserFilterField field) => All.Single(d => d.Field == field);
    public static IEnumerable<BrowserFilterPredicate> Values(BrowserFilterField field, IReadOnlyList<BrowserGridTile> tiles) => Get(field).Values(tiles);
    public static string ValueLabel(BrowserFilterPredicate predicate)
    {
        var label = predicate.Label;
        var colon = label.IndexOf(": ", StringComparison.Ordinal);
        return colon < 0 ? label : label[(colon + 2)..];
    }
}
