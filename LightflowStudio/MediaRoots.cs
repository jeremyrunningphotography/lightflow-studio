using Lightflow.Application;
using System.IO;
using Microsoft.Data.Sqlite;

namespace LightflowStudio;

internal static class MediaPathSemantics
{
    public static string NormalizeRootPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!Path.IsPathFullyQualified(path)) throw new ArgumentException("Media Root paths must be absolute.", nameof(path));
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path.Trim()));
    }

    public static string NormalizeRelativePath(string path) => CatalogRelativePaths.NormalizeRelativePath(path);
    public static string RelativePathKey(string path) => CatalogRelativePaths.RelativePathKey(path);

    public static string ResolveContained(string rootPath, string relativePath)
    {
        var root = NormalizeRootPath(rootPath);
        var relative = NormalizeRelativePath(relativePath);
        var resolved = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!IsSameOrAncestor(root, resolved)) throw new ArgumentException("The media path leaves its Media Root.", nameof(relativePath));
        return ApplicationDataProfile.GuardAccess(resolved);
    }

    public static bool Overlaps(string left, string right) =>
        IsSameOrAncestor(NormalizeRootPath(left), NormalizeRootPath(right)) ||
        IsSameOrAncestor(NormalizeRootPath(right), NormalizeRootPath(left));

    public static bool Contains(string rootPath, string candidatePath) =>
        IsSameOrAncestor(NormalizeRootPath(rootPath), NormalizeRootPath(candidatePath));

    public static string RelativeFolder(string rootPath, string candidatePath)
    {
        var root = NormalizeRootPath(rootPath);
        var candidate = NormalizeRootPath(candidatePath);
        if (!IsSameOrAncestor(root, candidate))
            throw new ArgumentException("The folder is outside its Media Root.", nameof(candidatePath));
        var relative = Path.GetRelativePath(root, candidate);
        return relative == "." ? "" : NormalizeRelativePath(relative);
    }

    private static bool IsSameOrAncestor(string parent, string child)
    {
        if (string.Equals(parent, child, StringComparison.OrdinalIgnoreCase)) return true;
        var prefix = Path.EndsInDirectorySeparator(parent) ? parent : parent + Path.DirectorySeparatorChar;
        return child.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }
}

internal sealed class MediaRootFileSystem : IMediaRootFileSystem
{
    public bool DirectoryExists(string path) => Directory.Exists(path);
    public bool FileExists(string path) => File.Exists(path);
}

internal sealed class MediaRootService(Func<CatalogDatabaseSession?> session, IMachineIdentityProvider machine,
    IMediaRootFileSystem fileSystem) : CatalogMediaRootService(session, machine, fileSystem, new WindowsMediaRootPathEffects());
internal sealed class WindowsMediaRootPathEffects : IMediaRootPathEffects
{
    public IMediaRootPathAdmission OpenAdmission(string root) => new MediaPathIdentityAdmission(root);
    public void ValidatePaths(string root, IReadOnlyList<PathIdentityCandidate> candidates, CancellationToken token) => MediaPathIdentityAdmission.ValidatePaths(root, candidates, token);
    public string NormalizeRootPath(string path) => MediaPathSemantics.NormalizeRootPath(path);
    public string NormalizeRelativePath(string path) => MediaPathSemantics.NormalizeRelativePath(path);
    public string RelativePathKey(string path) => MediaPathSemantics.RelativePathKey(path);
    public string ResolveContained(string root, string relative) => MediaPathSemantics.ResolveContained(root, relative);
    public bool Overlaps(string left, string right) => MediaPathSemantics.Overlaps(left, right);
    public bool Contains(string root, string candidate) => MediaPathSemantics.Contains(root, candidate);
}
