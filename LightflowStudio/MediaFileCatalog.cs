using System.IO;

namespace LightflowStudio;

internal static class MediaFileCatalog
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mov", ".mkv", ".mxf"
    };

    public static bool IsSupported(string path) => SupportedExtensions.Contains(Path.GetExtension(path));

    public static IReadOnlyList<string> Discover(string folder, bool recursive, string? excludedFolder = null, string? excludedFilenameSuffix = null)
    {
        if (!Directory.Exists(folder)) return [];
        var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        return Directory.EnumerateFiles(folder, "*", option)
            .Where(IsSupported)
            .Where(path => string.IsNullOrWhiteSpace(excludedFolder) || !IsWithin(excludedFolder, path))
            .Where(path => !IsGeneratedOutput(folder, path))
            .Where(path => !IsResolutionSuffixedOutput(path))
            .Where(path => string.IsNullOrWhiteSpace(excludedFilenameSuffix) || !Path.GetFileNameWithoutExtension(path).EndsWith(excludedFilenameSuffix, StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool IsGeneratedOutput(string root, string path) => Path.GetRelativePath(root, path)
        .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
        .SkipLast(1)
        .Any(part => part.StartsWith("Lightflow-", StringComparison.OrdinalIgnoreCase)
            || part.StartsWith("Toolkit-", StringComparison.OrdinalIgnoreCase));

    private static bool IsWithin(string folder, string path)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(folder), Path.GetFullPath(path));
        return relative != ".." && !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) && !Path.IsPathRooted(relative);
    }

    private static bool IsResolutionSuffixedOutput(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        return name.EndsWith("_1080p", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("_4K", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("_Source", StringComparison.OrdinalIgnoreCase);
    }
}
