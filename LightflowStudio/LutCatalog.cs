using System.IO;
using System.Text.RegularExpressions;

namespace LightflowStudio;

internal sealed record LutOption(string DisplayName, string FilePath, Guid? LutId = null, bool IsManaged = false,
    LutResourceAvailability Availability = LutResourceAvailability.Available);

internal static partial class LutCatalog
{
    public const string DefaultFolder = @"J:\Photography\LUTs";
    public static readonly LutOption NoLut = new("No LUT", "");

    public static IReadOnlyList<LutOption> Options(string folder) => [NoLut, .. Discover(folder)];

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
                && File.Exists(option.FilePath)
                && (!option.IsManaged || IsSupportedCube(option.FilePath))));

    private static bool IsSupportedCube(string path)
    {
        try { return CubeLutValidator.Validate(File.ReadAllBytes(path)).IsValid; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return false; }
    }

    public static IReadOnlyList<LutOption> Discover(string folder)
    {
        if (!Directory.Exists(folder)) return [];

        var candidates = Directory.EnumerateFiles(folder, "*", SearchOption.TopDirectoryOnly)
            .Where(path => string.Equals(Path.GetExtension(path), ".cube", StringComparison.OrdinalIgnoreCase))
            .Select(path => new { Path = path, Name = MakeDisplayName(path) })
            .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return candidates
            .GroupBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .SelectMany(group => group.Count() == 1
                ? group.Select(item => new LutOption(item.Name, item.Path))
                : group.Select((item, index) => new LutOption($"{item.Name} ({index + 1})", item.Path)))
            .ToList();
    }

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
