using Lightflow.Application;
using System.IO;
using Microsoft.Data.Sqlite;

namespace LightflowStudio;

internal sealed class MachineIdentityProvider(string path) : IMachineIdentityProvider
{
    private static readonly object Gate = new();
    private string? _value;

    public string GetMachineId()
    {
        lock (Gate)
        {
            if (_value is not null) return _value;
            if (File.Exists(path)) return _value = ReadEstablishedIdentity();

            var created = Guid.NewGuid().ToString("D");
            var temporary = path + $".{Guid.NewGuid():N}.tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(temporary, created);
                File.Move(temporary, path, overwrite: false);
                return _value = created;
            }
            catch (IOException exception) when (File.Exists(path))
            {
                return _value = ReadEstablishedIdentity(exception);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                throw new MachineIdentityException(MachineIdentityFailure.Unavailable,
                    "Lightflow could not establish this installation's Media Root identity.", exception);
            }
            finally { try { File.Delete(temporary); } catch { } }
        }
    }

    private string ReadEstablishedIdentity(Exception? raceException = null)
    {
        string contents;
        try { contents = File.ReadAllText(path); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new MachineIdentityException(MachineIdentityFailure.Unavailable,
                "Lightflow could not read this installation's established Media Root identity.", exception);
        }
        if (!Guid.TryParse(contents.Trim(), out var identity))
            throw new MachineIdentityException(MachineIdentityFailure.Malformed,
                "This installation's Media Root identity is malformed. Lightflow preserved it for diagnosis.", raceException);
        return identity.ToString("D");
    }
}

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
