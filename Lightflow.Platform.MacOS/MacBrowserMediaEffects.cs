using Lightflow.Application;
using Lightflow.Domain;
using LightflowStudio;

namespace Lightflow.Platform.MacOS;

/// <summary>Native read-only media effects. Catalog storage restrictions do not define MediaSource policy.</summary>
internal sealed class MacBrowserMediaEffects(string[] protectedRoots) : IBrowserFolderPaths, IMediaFolderFileSystem, IBrowserSourceAvailability
{
    public string NormalizeRootPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!Path.IsPathFullyQualified(path) || path != path.Trim()) throw new ArgumentException("Use an absolute Media Root mapping without trimming.");
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }
    public string ResolveContained(string root, string relative)
    {
        var check = PortablePathIdentityValidator.ValidateRelativePath(relative);
        if (!check.IsSafe) throw new IOException("This media path needs review; the Catalog record was preserved.");
        var parent = NormalizeRootPath(root);
        var result = Path.GetFullPath(Path.Combine(parent, relative));
        if (!result.StartsWith(parent == "/" ? "/" : parent + "/", StringComparison.Ordinal))
            throw new IOException("The media path leaves its Media Root.");
        return result;
    }
    private static async Task<StorageLocationAssessment> ValidateAsync(MacStorageLocationAssessor assessor,
        StorageAssessmentRequest request, CancellationToken token, StorageLocationAssessment? previous = null)
    {
        var first = await assessor.AssessAsync(request,token).ConfigureAwait(false);
        var policy = StorageLocationPolicy.Evaluate(request,first,DateTimeOffset.UtcNow,token);
        var check = PortablePathIdentityValidator.ValidateNativeMapping(request,first,DateTimeOffset.UtcNow,previous);
        token.ThrowIfCancellationRequested();
        if (policy.Decision != StorageLocationDecision.Eligible || !check.IsSafe) throw new IOException("This media location is unavailable or its path needs review. Reconnect it or review its mapping; Catalog records are preserved.");
        return first;
    }
    public async Task<IReadOnlyList<MediaFolderFileSystemEntry>> EnumerateAsync(string mediaRootPath, string folderPath, CancellationToken cancellationToken = default)
    {
        using var assessor=new MacStorageLocationAssessor(protectedRoots);
        var request=new StorageAssessmentRequest(Guid.NewGuid(),0,StorageRole.MediaSource,StorageOperation.Read,mediaRootPath);
        var first=await ValidateAsync(assessor,request,cancellationToken).ConfigureAwait(false);
        var result=await new ContainedMediaFolderFileSystem(this).EnumerateAsync(mediaRootPath,folderPath,cancellationToken).ConfigureAwait(false);
        await ValidateAsync(assessor,request,cancellationToken,first).ConfigureAwait(false);
        return result;
    }
    public async Task<bool> FileExistsAsync(MediaRootInfo root, string relativePath, CancellationToken token)
    {
        if(root.Availability != MediaRootAvailability.Online || root.PhysicalPath is null) return false;
        using var assessor=new MacStorageLocationAssessor(protectedRoots);
        var request=new StorageAssessmentRequest(Guid.NewGuid(),0,StorageRole.MediaSource,StorageOperation.Read,root.PhysicalPath);
        var first=await ValidateAsync(assessor,request,token).ConfigureAwait(false);
        var path=ResolveContained(root.PhysicalPath,relativePath);
        // No opening, probing or authored observation. Refuse linked path components before an existence observation.
        var current=NormalizeRootPath(root.PhysicalPath);
        foreach(var component in relativePath.Split('/'))
        {
            token.ThrowIfCancellationRequested(); current=Path.Combine(current,component);
            if (!File.Exists(current) && !Directory.Exists(current)) return false;
            if ((File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0) return false;
        }
        var exists=File.Exists(path);
        await ValidateAsync(assessor,request,token,first).ConfigureAwait(false);
        return exists;
    }
}
