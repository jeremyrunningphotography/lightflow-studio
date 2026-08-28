using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace LightflowStudio;

internal enum NamePartKind
{
    OriginalName,
    CustomText,
    Date,
    Time,
    Sequence1,
    Sequence01,
    Sequence001,
    Sequence0001,
    Sequence00001,
    IndexNumber
}

internal enum NamePartSeparator { Underscore, Hyphen, Space, None }

internal sealed record NamePart(NamePartKind Kind, string? Text = null);

internal sealed record NamePartsDefinition(
    IReadOnlyList<NamePart> Parts,
    NamePartSeparator Separator = NamePartSeparator.Underscore);

internal sealed record NamingInput(string OriginalName, int Sequence, DateTimeOffset? Timestamp = null,
    string? IndexNumberBasis = null);

internal sealed record MaterializedName(
    string? Stem,
    int Sequence,
    string? IndexNumber,
    DateTimeOffset? Timestamp,
    string? Problem = null);

internal static partial class NamePartsRenderer
{
    public static MaterializedName Materialize(NamePartsDefinition definition, NamingInput input)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(input);
        var indexNumber = TrailingDecimalRun().Match(input.IndexNumberBasis ?? input.OriginalName) is { Success: true } match
            ? match.Value
            : null;
        var values = new List<string>(definition.Parts.Count);
        foreach (var part in definition.Parts)
        {
            switch (part.Kind)
            {
                case NamePartKind.OriginalName: values.Add(input.OriginalName); break;
                case NamePartKind.CustomText: values.Add(part.Text ?? ""); break;
                case NamePartKind.Date when input.Timestamp is { } timestamp:
                    values.Add(timestamp.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)); break;
                case NamePartKind.Time when input.Timestamp is { } timestamp:
                    values.Add(timestamp.ToString("HH-mm-ss", CultureInfo.InvariantCulture)); break;
                case NamePartKind.Date:
                    return new(null, input.Sequence, indexNumber, input.Timestamp,
                        $"Date cannot be resolved for '{input.OriginalName}' because no explicit naming timestamp is available.");
                case NamePartKind.Time:
                    return new(null, input.Sequence, indexNumber, input.Timestamp,
                        $"Time cannot be resolved for '{input.OriginalName}' because no explicit naming timestamp is available.");
                case NamePartKind.IndexNumber when indexNumber is not null: values.Add(indexNumber); break;
                case NamePartKind.IndexNumber:
                    return new(null, input.Sequence, null, input.Timestamp,
                        $"Index Number cannot be resolved for '{input.OriginalName}' because its name has no trailing decimal run.");
                case NamePartKind.Sequence1: values.Add(Sequence(input.Sequence, 1)); break;
                case NamePartKind.Sequence01: values.Add(Sequence(input.Sequence, 2)); break;
                case NamePartKind.Sequence001: values.Add(Sequence(input.Sequence, 3)); break;
                case NamePartKind.Sequence0001: values.Add(Sequence(input.Sequence, 4)); break;
                case NamePartKind.Sequence00001: values.Add(Sequence(input.Sequence, 5)); break;
                default: throw new ArgumentOutOfRangeException(nameof(part.Kind), part.Kind, null);
            }
        }
        return new(string.Join(Separator(definition.Separator), values), input.Sequence, indexNumber, input.Timestamp);
    }

    public static string Preview(NamePartsDefinition definition, NamingInput input) =>
        Materialize(definition, input) is { Problem: null, Stem: { } stem } ? stem : "";

    private static string Sequence(int value, int width) => value.ToString(new string('0', width), CultureInfo.InvariantCulture);
    private static string Separator(NamePartSeparator separator) => separator switch
    {
        NamePartSeparator.Underscore => "_",
        NamePartSeparator.Hyphen => "-",
        NamePartSeparator.Space => " ",
        NamePartSeparator.None => "",
        _ => throw new ArgumentOutOfRangeException(nameof(separator), separator, null)
    };

    [GeneratedRegex(@"\d+$", RegexOptions.CultureInvariant)]
    private static partial Regex TrailingDecimalRun();
}

internal static class WindowsOutputNameValidator
{
    private static readonly HashSet<string> Reserved = BuildReservedNames();

    public static string? ValidateStem(string stem)
    {
        if (string.IsNullOrEmpty(stem)) return "The rendered output filename is empty.";
        if (stem.IndexOfAny(['<', '>', ':', '"', '/', '\\', '|', '?', '*']) >= 0 || stem.Any(character => character < 32))
            return $"The rendered output filename '{stem}' contains characters that are invalid on Windows.";
        if (stem.EndsWith(' ') || stem.EndsWith('.'))
            return $"The rendered output filename '{stem}' cannot end with a dot or space on Windows.";
        var deviceBasis = stem.Split('.')[0];
        if (Reserved.Contains(deviceBasis))
            return $"The rendered output filename '{stem}' is a reserved Windows device name.";
        return null;
    }

    private static HashSet<string> BuildReservedNames()
    {
        var names = new HashSet<string>(["CON", "PRN", "AUX", "NUL"], StringComparer.OrdinalIgnoreCase);
        for (var number = 1; number <= 9; number++) { names.Add($"COM{number}"); names.Add($"LPT{number}"); }
        return names;
    }
}
