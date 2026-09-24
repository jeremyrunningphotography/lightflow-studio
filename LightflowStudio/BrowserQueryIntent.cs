using System.Text.Json;
using System.Text.Json.Serialization;

namespace LightflowStudio;

/// <summary>Version 3 uses one row per Field: alternatives OR within a field, MatchMode between fields.
/// FileOrPath is an ordinary field. SearchText is accepted only as development-era input; writes normalize it.
/// Sort is presentation. There is no recursive Boolean expression tree.</summary>
internal sealed record BrowserQueryIntent
{
    public int Version { get; init; } = 3;
    public BrowserMatchMode MatchMode { get; init; }
    public string SearchText { get; init; } = "";
    public IReadOnlyList<BrowserFilterPredicate> Filters { get; init; } = [];
    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new JsonStringEnumConverter() },
        IgnoreReadOnlyProperties = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static BrowserQueryIntent Capture(BrowserQuery query) => new()
    { MatchMode = BrowserMatchMode.All, Filters = WithSearch(query.Filters, query.SearchText) };
    public BrowserQuery ToQuery()
    {
        Validate();
        return new() { MatchMode = MatchMode, SearchText = "", Filters = WithSearch(Filters, SearchText) };
    }
    public string Serialize()
    {
        Validate();
        return JsonSerializer.Serialize(new Document { MatchMode = MatchMode, Filters = WithSearch(Filters, SearchText) }, Options);
    }
    private sealed class Document
    {
        public int Version { get; set; } = 3;
        public BrowserMatchMode MatchMode { get; set; }
        public BrowserFilterPredicate[] Filters { get; set; } = [];
    }
    public static BrowserQueryIntent Deserialize(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty(nameof(Version), out var version) || !version.TryGetInt32(out var number) || number is not (1 or 2 or 3))
            throw new ArgumentException("This saved query uses an unsupported or missing version.");
        var result = JsonSerializer.Deserialize<BrowserQueryIntent>(json, Options)
            ?? throw new ArgumentException("The saved query is missing.");
        result.Validate();
        return result with { Version = 3, SearchText = "", Filters = WithSearch(result.Filters, result.SearchText) };
    }
    private static BrowserFilterPredicate[] WithSearch(IReadOnlyList<BrowserFilterPredicate> filters, string search) =>
        string.IsNullOrWhiteSpace(search) ? filters.ToArray() :
        [.. filters, BrowserFilterPredicate.ForText(BrowserFilterField.FileOrPath, search.Trim())];
    public void Validate()
    {
        if (Version is not (1 or 2 or 3) || !Enum.IsDefined(MatchMode) || SearchText is null || Filters is null ||
            Filters.Any(p => p is null || !Enum.IsDefined(p.Field) || !Enum.IsDefined(p.Comparison) ||
                (p.MatchUnset && (p.Field != BrowserFilterField.ColorLabel || p.TextValue is not null)) ||
                (p.Field == BrowserFilterField.AspectRatio && p.AspectRatioValue is not { Numerator: > 0, Denominator: > 0 }) ||
                (p.Field == BrowserFilterField.Duration && p.Comparison is not (BrowserNumberComparison.GreaterThanOrEqual or BrowserNumberComparison.LessThanOrEqual))))
            throw new ArgumentException("This saved query uses an unsupported version or predicate.");
    }
}
