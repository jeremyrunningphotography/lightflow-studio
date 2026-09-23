using System.Globalization;
using System.Text.RegularExpressions;

namespace LightflowStudio;

/// <summary>Shared input conversion only; evaluation always belongs to BrowserFilterPredicate.</summary>
internal static class BrowserPredicateEditor
{
    public static string FieldName(BrowserFilterField field) => Regex.Replace(field.ToString(), "([a-z])([A-Z])", "$1 $2");
    public static string Hint(BrowserFilterField field) => field switch
    {
        BrowserFilterField.MediaType => "Images, RAW, or Video",
        BrowserFilterField.Rating => "0–5 stars",
        BrowserFilterField.Duration => "Minimum duration in seconds",
        BrowserFilterField.Resolution => "Width × height, for example 3840 × 2160",
        BrowserFilterField.FrameRate => "Frames per second, for example 29.97",
        BrowserFilterField.CaptureDate => "From / to dates, for example 2026-01-01 / 2026-12-31",
        BrowserFilterField.Flag => "Picked, Rejected, or Unflagged",
        BrowserFilterField.ColorLabel => string.Join(", ", Enum.GetNames<AssetColorLabel>()),
        BrowserFilterField.ColorState or BrowserFilterField.CameraLutState or BrowserFilterField.CreativeLutState or
            BrowserFilterField.ReviewRangeState or BrowserFilterField.SubclipState => "True or false",
        _ => "Exact value (case insensitive)"
    };
    public static BrowserFilterPredicate Create(BrowserFilterField field, string value, BrowserNumberComparison comparison)
    {
        value = value.Trim();
        if (value.Length == 0) throw new ArgumentException("Enter a rule value.");
        double Number(string text) => double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var result) &&
            double.IsFinite(result) && result >= 0 ? result : throw new ArgumentException("Enter a valid positive number.");
        switch (field)
        {
            case BrowserFilterField.MediaType:
                value = value.Equals("Images", StringComparison.OrdinalIgnoreCase) ? "StillImage" : value.Equals("RAW", StringComparison.OrdinalIgnoreCase) ? "RawImage" : value;
                if (!Enum.TryParse<MediaTypeCategory>(value, true, out var category) || !BrowserGridModel.PresentableCategories.Contains(category))
                    throw new ArgumentException(Hint(field));
                return BrowserFilterPredicate.ForMediaType(category);
            case BrowserFilterField.Rating:
                var rating = Number(value);
                if (rating != Math.Truncate(rating)) throw new ArgumentException("Choose a whole number of stars.");
                return BrowserFilterPredicate.ForRating(comparison, (int)rating);
            case BrowserFilterField.Duration: return BrowserFilterPredicate.ForMinimum(field, Number(value));
            case BrowserFilterField.FrameRate: return BrowserFilterPredicate.ForFrameRate(Number(value));
            case BrowserFilterField.Resolution:
                var size = value.ToLowerInvariant().Split(['x', '×'], StringSplitOptions.TrimEntries);
                if (size.Length != 2) throw new ArgumentException(Hint(field));
                return BrowserFilterPredicate.ForResolution((int)Number(size[0]), (int)Number(size[1]));
            case BrowserFilterField.CaptureDate:
                var dates = value.Split('/', StringSplitOptions.TrimEntries);
                DateTime? Date(string text) => text.Length == 0 ? null : DateTime.Parse(text, CultureInfo.CurrentCulture);
                if (dates.Length != 2) throw new ArgumentException(Hint(field));
                var from = Date(dates[0]); var to = Date(dates[1]);
                if (from is null && to is null || from > to) throw new ArgumentException("Choose a valid date range.");
                return BrowserFilterPredicate.ForDateRange(from, to);
            case BrowserFilterField.ColorState: case BrowserFilterField.CameraLutState: case BrowserFilterField.CreativeLutState:
            case BrowserFilterField.ReviewRangeState: case BrowserFilterField.SubclipState:
                return BrowserFilterPredicate.ForState(field, bool.Parse(value));
            case BrowserFilterField.Flag:
                if (!Enum.TryParse<AssetFlag>(value, true, out var flag) || !Enum.IsDefined(flag)) throw new ArgumentException(Hint(field));
                return BrowserFilterPredicate.ForText(field, flag.ToString());
            case BrowserFilterField.ColorLabel:
                if (!Enum.TryParse<AssetColorLabel>(value, true, out var label) || !Enum.IsDefined(label)) throw new ArgumentException(Hint(field));
                return BrowserFilterPredicate.ForText(field, label.ToString());
            default: return BrowserFilterPredicate.ForText(field, value);
        }
    }
}
