using System.Globalization;

namespace LightflowStudio;

/// <summary>Structured authoring metadata and conversion into existing predicates, never evaluation.</summary>
internal sealed record BrowserStructuredFilterInput(string[] Components, string Operator, string AlternativesOperator,
    string Separator, string Unit, string Help, Func<string[], BrowserFilterPredicate> Parse, Func<BrowserFilterPredicate, string[]> Format)
{
    public static BrowserStructuredFilterInput Resolution { get; } = new(["Width", "Height"], "is", "is any of", "×", "px",
        "Enter positive whole-pixel dimensions.",
        values => BrowserFilterPredicate.ForResolution(PositiveInteger(values[0]), PositiveInteger(values[1])),
        p => [p.NumberValue?.ToString("0", CultureInfo.CurrentCulture) ?? "", p.NumberValue2?.ToString("0", CultureInfo.CurrentCulture) ?? ""]);
    public static BrowserStructuredFilterInput FrameRate { get; } = new(["Frame rate"], "is", "is any of", "", "fps",
        "Enter a positive frame rate. Standard rates use Browser normalization.",
        values => NormalizedRate(values[0]),
        p => [p.NumberValue?.ToString("0.###", CultureInfo.CurrentCulture) ?? ""]);
    public static BrowserStructuredFilterInput Duration { get; } = new(["mm:ss"], "is at least", "is at least any of", "", "",
        "Enter seconds, mm:ss, or hh:mm:ss. Duration limits are inclusive.",
        values => BrowserFilterPredicate.ForMinimum(BrowserFilterField.Duration, ParseDuration(values[0])),
        p => [FormatDuration(p.NumberValue)]);
    private static int PositiveInteger(string text) => int.TryParse(text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var number) && number > 0
        ? number : throw new FormatException("Enter positive whole-pixel dimensions.");
    private static double PositiveNumber(string text) => double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var number) && double.IsFinite(number) && number > 0
        ? number : throw new FormatException("Enter a positive frame rate.");
    private static BrowserFilterPredicate NormalizedRate(string text)
    {
        var predicate = BrowserFilterPredicate.ForFrameRate(PositiveNumber(text));
        return predicate.NumberValue > 0 ? predicate : throw new FormatException("Enter a frame rate of at least 0.001 fps.");
    }
    internal static double ParseDuration(string text)
    {
        var parts = text.Trim().Split(':');
        if (parts.Length is < 1 or > 3) throw new FormatException(Duration.Help);
        double total = 0;
        for (var i = 0; i < parts.Length; i++)
        {
            if (!double.TryParse(parts[i], parts.Length == 1 ? NumberStyles.Float : NumberStyles.AllowDecimalPoint, CultureInfo.CurrentCulture, out var value) ||
                !double.IsFinite(value) || value < 0 || (i > 0 && value >= 60) || (i < parts.Length - 1 && value != Math.Truncate(value)))
                throw new FormatException(Duration.Help);
            total = total * 60 + value;
        }
        if (!double.IsFinite(total) || total > TimeSpan.MaxValue.TotalSeconds) throw new FormatException(Duration.Help);
        return total;
    }
    private static string FormatDuration(double? number)
    {
        if (number is not { } seconds) return "";
        // Preserve full fractional thresholds; formatting must not silently change existing query intent.
        if (seconds != Math.Truncate(seconds)) return seconds.ToString("R", CultureInfo.CurrentCulture);
        var hours = Math.Floor(seconds / 3600); var minutes = Math.Floor(seconds % 3600 / 60); var remainder = seconds % 60;
        return hours > 0 ? $"{hours:0}:{minutes:00}:{remainder:00}" : $"{minutes:00}:{remainder:00}";
    }
}
