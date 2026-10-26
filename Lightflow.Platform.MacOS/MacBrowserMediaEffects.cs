using Lightflow.Application;
using Lightflow.Domain;
using LightflowStudio;

namespace Lightflow.Platform.MacOS;

/// <summary>Native read-only media effects. Catalog storage restrictions do not define MediaSource policy.</summary>
internal sealed class MacBrowserMediaEffects(string[] protectedRoots) : IMediaRootPathEffects, IMediaFolderFileSystem, IBrowserSourceAvailability
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
        if (policy.Decision != StorageLocationDecision.Eligible || !check.IsSafe) throw new IOException($"This media location needs review ({policy.Reason}; {check.Status}): {check.Diagnostic}");
        return first;
    }
    // Application packages are opaque navigation boundaries, including explicit typed paths.
    // This intentionally supports .app only; it does not infer other Finder package types.
    internal static bool IsApplicationPackagePath(string path) =>
        Path.GetFullPath(path).Split('/').Any(component => component.EndsWith(".app", StringComparison.OrdinalIgnoreCase));
    internal static void RefuseApplicationPackage(string path)
    {
        if (IsApplicationPackagePath(path))
            throw new IOException("Application packages cannot be browsed as media folders.");
    }

    public async Task<IReadOnlyList<MediaFolderFileSystemEntry>> EnumerateAsync(string mediaRootPath, string folderPath, CancellationToken cancellationToken = default)
    {
        using var assessor=new MacStorageLocationAssessor(protectedRoots, allowMediaSourceAncestor: true);
        var request=new StorageAssessmentRequest(Guid.NewGuid(),0,StorageRole.MediaSource,StorageOperation.Read,mediaRootPath);
        RefuseProtected(folderPath);
        RefuseApplicationPackage(folderPath);
        if (GetAnchor(mediaRootPath) != GetAnchor(folderPath))
            throw new IOException("Browse this mounted volume through its own location. Catalog records are preserved.");
        using var timing = BrowserPerformance.Measure("mac.filesystem.enumeration");
        var first=await ValidateAsync(assessor,request,cancellationToken).ConfigureAwait(false);
        var result=await new ContainedMediaFolderFileSystem(this).EnumerateAsync(mediaRootPath,folderPath,cancellationToken).ConfigureAwait(false);
        result = result.Where(entry => !IsProtected(entry.FullPath) &&
            !(entry.IsDirectory && !entry.IsReparsePoint && IsApplicationPackagePath(entry.FullPath))).ToArray();
        await ValidateAsync(assessor,request,cancellationToken,first).ConfigureAwait(false);
        return result;
    }
    public async Task<bool> FileExistsAsync(MediaRootInfo root, string relativePath, CancellationToken token)
    {
        if(root.Availability != MediaRootAvailability.Online || root.PhysicalPath is null) return false;
        using var assessor=new MacStorageLocationAssessor(protectedRoots, allowMediaSourceAncestor: true);
        var request=new StorageAssessmentRequest(Guid.NewGuid(),0,StorageRole.MediaSource,StorageOperation.Read,root.PhysicalPath);
        var first=await ValidateAsync(assessor,request,token).ConfigureAwait(false);
        var path=ResolveContained(root.PhysicalPath,relativePath);
        RefuseProtected(path);
        RefuseApplicationPackage(path);
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
    private string[] ProtectedRoots => protectedRoots;
    public bool IsProtected(string path) => protectedRoots.Any(root => Contains(root, path));
    private void RefuseProtected(string path)
    {
        if (IsProtected(path)) throw new IOException("This folder belongs to Lightflow storage and cannot be browsed as media.");
    }
    public bool Contains(string root, string path) => path == root || path.StartsWith(root == "/" ? "/" : root + "/", StringComparison.Ordinal);
    public bool Overlaps(string left, string right) => Contains(left, right) || Contains(right, left);
    public string NormalizeRelativePath(string path) => CatalogRelativePaths.NormalizeRelativePath(path);
    public string RelativePathKey(string path) => CatalogRelativePaths.RelativePathKey(path);
    public string GetAnchor(string path) => MacBrowserMounts.Anchor(path, DriveInfo.GetDrives().Select(drive => drive.Name));
    public bool IsNaturalAnchor(string path) => GetAnchor(path) == NormalizeRootPath(path);
    public IMediaRootPathAdmission OpenAdmission(string root) => new Admission(this, root);
    public void ValidatePaths(string root, IReadOnlyList<PathIdentityCandidate> candidates, CancellationToken token)
    {
        Require(PortablePathIdentityValidator.ValidateCandidates(candidates, token));
        foreach (var candidate in candidates)
        {
            var current = NormalizeRootPath(root);
            foreach (var component in candidate.RelativePath.Split('/'))
            {
                token.ThrowIfCancellationRequested();
                var key = PortablePathIdentityValidator.ValidateRelativePath(component).ComparisonKey;
                var matches = Directory.EnumerateFileSystemEntries(current)
                    .Where(path => PortablePathIdentityValidator.ValidateRelativePath(Path.GetFileName(path)).ComparisonKey == key)
                    .Select(path => new PathIdentityCandidate(candidate.RootId, Guid.Empty, Path.GetFileName(path))).ToArray();
                Require(PortablePathIdentityValidator.ValidateCandidates(matches, token));
                Require(PortablePathIdentityValidator.ValidateReconciliation([new(candidate.RootId, candidate.AssetId, component)], matches, token));
                current = Path.Combine(current, component);
                RefuseProtected(current);
                if (!File.Exists(current) && !Directory.Exists(current)) break;
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Linked media paths are not supported.");
            }
        }
    }
    private static void Require(PathIdentityResult result)
    {
        if (result.Status == PathIdentityStatus.Cancelled) throw new OperationCanceledException();
        if (!result.IsSafe) throw new MediaPathIdentityException(result);
    }
    private sealed class Admission(MacBrowserMediaEffects paths, string root) : IMediaRootPathAdmission
    {
        private readonly MacStorageLocationAssessor _assessor = new(paths.ProtectedRoots, allowMediaSourceAncestor: true);
        private readonly StorageAssessmentRequest _request = new(Guid.NewGuid(), 0, StorageRole.MediaSource, StorageOperation.Read, root);
        private StorageLocationAssessment? _previous;
        public async Task ValidateAsync(CancellationToken token, string? catalogDirectory = null)
        {
            paths.RefuseProtected(root);
            _previous = await ValidateAsyncCore(token);
        }
        private Task<StorageLocationAssessment> ValidateAsyncCore(CancellationToken token) =>
            MacBrowserMediaEffects.ValidateAsync(_assessor, _request, token, _previous);
        public string? ConflictingMapping(Guid existingRoot, Guid requestedRoot, string path, bool ignoreAncestorAnchor, CancellationToken token)
        {
            var request = new StorageAssessmentRequest(_request.OperationId, 0, StorageRole.MediaSource, StorageOperation.Read, path);
            var other = MacBrowserMediaEffects.ValidateAsync(_assessor, request, token).GetAwaiter().GetResult();
            var candidate = _previous!.Identity!;
            var existing = other.Identity!;
            var check = PortablePathIdentityValidator.ValidateRootMapping(existingRoot, requestedRoot, existing.LocationIdentity, candidate.LocationIdentity, token);
            Require(check);
            return !ignoreAncestorAnchor && paths.Overlaps(candidate.CanonicalLocation, existing.CanonicalLocation)
                ? "The resolved folders overlap another location." : null;
        }
        public void Dispose() => _assessor.Dispose();
    }
    internal async Task<SourceFingerprint> FingerprintAsync(string path, CancellationToken token)
    {
        RefuseProtected(path);
        RefuseApplicationPackage(path);
        using var timing = BrowserPerformance.Measure("mac.source.fingerprint");
        var parent = Path.GetDirectoryName(path)!;
        using var assessor = new MacStorageLocationAssessor(protectedRoots, allowMediaSourceAncestor: true);
        var request = new StorageAssessmentRequest(Guid.NewGuid(), 0, StorageRole.MediaSource, StorageOperation.Read, parent);
        var before = await ValidateAsync(assessor, request, token);
        // Validate all source components; no alias traversal or ambiguous case/Unicode identity.
        ValidatePaths("/", [new(Guid.NewGuid(), Guid.Empty, path.TrimStart('/'))], token);
        var fingerprint = await new SampledSourceFingerprintService().CreateAsync(path, token);
        await ValidateAsync(assessor, request, token, before);
        return fingerprint;
    }

}
