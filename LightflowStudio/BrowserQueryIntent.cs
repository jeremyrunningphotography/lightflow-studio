using System.Text.Json;
using System.Text.Json.Serialization;

namespace LightflowStudio;

/// <summary>Durable query vocabulary shared by Browser and Catalog organization. Sort is presentation.</summary>
internal sealed record BrowserQueryIntent
{
    public int Version { get; init; } = 1;
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
    { MatchMode = query.MatchMode, SearchText = query.SearchText, Filters = query.Filters.ToArray() };
    public BrowserQuery ToQuery()
    {
        Validate();
        return new() { MatchMode = MatchMode, SearchText = SearchText, Filters = Filters.ToArray() };
    }
    public string Serialize() { Validate(); return JsonSerializer.Serialize(this, Options); }
    public static BrowserQueryIntent Deserialize(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty(nameof(Version), out var version) || !version.TryGetInt32(out var number) || number != 1)
            throw new ArgumentException("This saved query uses an unsupported or missing version.");
        var result = JsonSerializer.Deserialize<BrowserQueryIntent>(json, Options)
            ?? throw new ArgumentException("The saved query is missing.");
        result.Validate();
        return result;
    }
    public void Validate()
    {
        if (Version != 1 || !Enum.IsDefined(MatchMode) || SearchText is null || Filters is null ||
            Filters.Any(p => p is null || !Enum.IsDefined(p.Field) || !Enum.IsDefined(p.Comparison)))
            throw new ArgumentException("This saved query uses an unsupported version or predicate.");
    }
}
