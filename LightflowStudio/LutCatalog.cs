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

    public static IReadOnlyList<LutOption> Options(string folder, IEnumerable<LutOption> managed) =>
        [NoLut, .. managed.OrderBy(option => option.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(option => option.LutId), .. Discover(folder)];

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
