using System.IO;
using Lightflow.Application;
namespace LightflowStudio;
public enum MediaRootAvailability { Online, Unavailable, Unmapped }

public sealed record MediaRootInfo(Guid RootId, string DisplayName, string? PhysicalPath,
    MediaRootAvailability Availability, string? Diagnostic = null);

public sealed record MediaPathResolution(Guid RootId, string RelativePath, string RelativePathKey,
    string? PhysicalPath, MediaRootAvailability RootAvailability, bool Exists, string? Diagnostic = null, PathIdentityStatus IdentityStatus = PathIdentityStatus.Safe);

public sealed record MediaRootChangeResult(bool Succeeded, MediaRootInfo? Root = null, string? Diagnostic = null);

public interface IMachineIdentityProvider
{
    string GetMachineId();
}

public enum MachineIdentityFailure { Malformed, Unavailable }

public sealed class MachineIdentityException(MachineIdentityFailure failure, string diagnostic, Exception? innerException = null)
    : Exception(diagnostic, innerException)
{
    public MachineIdentityFailure Failure { get; } = failure;
}

public interface IMediaRootFileSystem
{
    bool DirectoryExists(string path);
    bool FileExists(string path);
}

public interface IMediaRootReader
{
    Task<IReadOnlyList<MediaRootInfo>> ListAsync(CancellationToken cancellationToken = default);
    Task<MediaRootInfo?> GetAsync(Guid rootId, CancellationToken cancellationToken = default);
}

public interface IMediaRootService : IMediaRootReader
{
    Task<MediaRootChangeResult> CreateAsync(string displayName, string physicalPath, CancellationToken cancellationToken = default);
    Task<MediaRootChangeResult> CreateBrowserAnchorAsync(string displayName, string physicalPath,
        CancellationToken cancellationToken = default) => CreateAsync(displayName, physicalPath, cancellationToken);
    Task<MediaRootChangeResult> RenameAsync(Guid rootId, string displayName, CancellationToken cancellationToken = default);
    Task<MediaRootChangeResult> RemapAsync(Guid rootId, string physicalPath, CancellationToken cancellationToken = default);
    Task<MediaPathResolution> ResolveAsync(Guid rootId, string relativePath, CancellationToken cancellationToken = default);
}

public sealed class MediaPathIdentityException(PathIdentityResult result) : IOException($"{result.Status}: {result.Diagnostic}")
{
    public PathIdentityStatus Status { get; } = result.Status;
}


public interface IMediaRootPathAdmission : IDisposable
{
    Task ValidateAsync(CancellationToken token, string? catalogDirectory = null);
    string? ConflictingMapping(Guid existingRoot, Guid requestedRoot, string path, bool ignoreAncestorAnchor, CancellationToken token);
}
public interface IBrowserFolderPaths
{
    string NormalizeRootPath(string path);
    string ResolveContained(string root, string relative);
}

public interface IMediaRootPathEffects : IBrowserFolderPaths
{
    bool IsNaturalAnchor(string path)
    {
        var normalized = NormalizeRootPath(path);
        var root = Path.GetPathRoot(normalized);
        return !string.IsNullOrWhiteSpace(root) && string.Equals(normalized, NormalizeRootPath(root), StringComparison.OrdinalIgnoreCase);
    }
    IMediaRootPathAdmission OpenAdmission(string root);
    void ValidatePaths(string root, IReadOnlyList<PathIdentityCandidate> candidates, CancellationToken token);
    string NormalizeRelativePath(string path);
    string RelativePathKey(string path);
    bool Overlaps(string left, string right);
    bool Contains(string root, string candidate);
}
