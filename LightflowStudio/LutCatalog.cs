using System.IO;
using System.Text.RegularExpressions;

namespace LightflowStudio;

internal sealed record LutOption(string DisplayName, string FilePath, Guid? LutId = null, bool IsManaged = false,
    LutResourceAvailability Availability = LutResourceAvailability.Available);

internal static partial class LutCatalog
{
    public const string DefaultFolder = @"J:\Photography\LUTs";
    public static readonly LutOption NoLut = new("No LUT", "");

    public static IReadOnlyList<LutOption> Options(IEnumerable<ManagedLutResource> resources)
    {
        var candidates = resources.Where(resource => resource.Availability == LutResourceAvailability.Available
                                                     && resource.FilePath is not null)
            .OrderBy(resource => resource.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(resource => resource.FilePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var options = candidates.GroupBy(resource => resource.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .SelectMany(group => group.Count() == 1
                ? group.Select(resource => new LutOption(resource.DisplayName, resource.FilePath!, resource.LutId, true))
                : group.Select((resource, index) => new LutOption($"{resource.DisplayName} ({index + 1})",
                    resource.FilePath!, resource.LutId, true)))
            .ToArray();
        return [NoLut, .. options];
    }

    /// <summary>Encoding's legacy general LUT picker sees the validated union of both Color folders.
    /// Stable identity removes duplicate content even when it exists at two paths.</summary>
    public static IReadOnlyList<LutOption> CombinedOptions(params IEnumerable<ManagedLutResource>[] folders) =>
        Options(folders.SelectMany(resources => resources).GroupBy(resource => resource.LutId)
            .Select(group => group.First()));

    public static LutOption SelectPreferred(IReadOnlyList<LutOption> options, string? preferredPath) =>
        options.FirstOrDefault(option =>
            string.Equals(option.FilePath, preferredPath, StringComparison.OrdinalIgnoreCase))
        ?? options.FirstOrDefault()
        ?? NoLut;

    public static bool IsValidSelection(LutOption? option) =>
        option is not null
        && (option == NoLut
            || (!string.IsNullOrWhiteSpace(option.FilePath)
                && option.FilePath.EndsWith(".cube", StringComparison.OrdinalIgnoreCase)
                && File.Exists(option.FilePath)));

    internal static string MakeDisplayName(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        name = SeparatorsRegex().Replace(name, " ");
        name = CamelCaseRegex().Replace(name, "$1 $2");
        name = SpecialCharactersRegex().Replace(name, " ");
        name = WhitespaceRegex().Replace(name, " ").Trim();
        return string.IsNullOrWhiteSpace(name) ? "Unnamed LUT" : name;
    }

    [GeneratedRegex(@"[_\-.]+")]
    private static partial Regex SeparatorsRegex();

    [GeneratedRegex(@"([a-z0-9])([A-Z])")]
    private static partial Regex CamelCaseRegex();

    [GeneratedRegex(@"[^\p{L}\p{N} ]+")]
    private static partial Regex SpecialCharactersRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
