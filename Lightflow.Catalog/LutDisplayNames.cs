using System.IO;
using System.Text.RegularExpressions;
namespace LightflowStudio;
public static partial class LutDisplayNames
{
    public static string MakeDisplayName(string path)
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
