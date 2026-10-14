using System.IO;
namespace LightflowStudio;
public static class CatalogRelativePaths
{
    public static string NormalizeRelativePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var candidate = path.Trim();
        if (Path.IsPathFullyQualified(candidate) || candidate.StartsWith('/') || candidate.StartsWith('\\') ||
            (candidate.Length >= 2 && char.IsLetter(candidate[0]) && candidate[1] == ':'))
            throw new ArgumentException("Media paths must be relative to their Media Root.", nameof(path));
        var parts = candidate.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var normalized = new List<string>(parts.Length);
        foreach (var part in parts)
        {
            if (part == ".") continue;
            if (part == "..") throw new ArgumentException("Media paths cannot leave their Media Root.", nameof(path));
            normalized.Add(part);
        }
        if (normalized.Count == 0) throw new ArgumentException("A media path cannot be empty.", nameof(path));
        return string.Join('/', normalized);
    }

    public static string RelativePathKey(string path) => NormalizeRelativePath(path).ToUpperInvariant();

}
