using System.IO;
using Lightflow.Application;
using Lightflow.Domain;

namespace LightflowStudio;

internal sealed class MediaPathIdentityException(PathIdentityResult result) : IOException($"{result.Status}: {result.Diagnostic}")
{
    public PathIdentityStatus Status { get; } = result.Status;
}

/// <summary>Windows compatibility adapter. Read-only, operation-owned native facts; no durable identities or writer gate.</summary>
internal sealed class MediaPathIdentityAdmission : IDisposable
{
    private readonly WindowsStorageLocationAssessor _assessor = new();
    private readonly StorageAssessmentRequest _request;
    private StorageLocationAssessment? _previous;

    public MediaPathIdentityAdmission(string root) => _request = new(Guid.NewGuid(), 0,
        StorageRole.MediaSource, StorageOperation.Read, root);

    public async Task ValidateAsync(CancellationToken token, string? catalogDirectory = null)
    {
        var current = await _assessor.AssessAsync(_request, token).ConfigureAwait(false);
        Require(PortablePathIdentityValidator.ValidateNativeMapping(_request, current, DateTimeOffset.UtcNow, _previous, token));
        if (catalogDirectory is not null)
        {
            var protectedRequest = new StorageAssessmentRequest(_request.OperationId, 0, StorageRole.MediaSource, StorageOperation.Read, catalogDirectory);
            var protectedFacts = await _assessor.AssessAsync(protectedRequest, token).ConfigureAwait(false);
            Require(PortablePathIdentityValidator.ValidateNativeMapping(protectedRequest, protectedFacts, DateTimeOffset.UtcNow, token: token));
            // A Browser volume anchor may contain Catalog storage, but may not resolve inside it.
            Require(PortablePathIdentityValidator.ValidateContainment(
                MediaPathSemantics.Contains(protectedFacts.Identity!.CanonicalLocation, current.Identity!.CanonicalLocation)
                    ? StorageResolutionConfidence.Ambiguous : StorageResolutionConfidence.Resolved, token));
        }
        _previous = current;
    }

    public string? ConflictingMapping(Guid existingRoot, Guid requestedRoot, string path, bool ignoreAncestorAnchor, CancellationToken token)
    {
        var request = new StorageAssessmentRequest(_request.OperationId, 0, StorageRole.MediaSource, StorageOperation.Read, path);
        var other = _assessor.AssessAsync(request, token).GetAwaiter().GetResult();
        Require(PortablePathIdentityValidator.ValidateNativeMapping(request, other, DateTimeOffset.UtcNow, token: token));
        var candidate = _previous!.Identity!;
        var existing = other.Identity!;
        var result = PortablePathIdentityValidator.ValidateRootMapping(existingRoot, requestedRoot,
            existing.LocationIdentity, candidate.LocationIdentity, token);
        if (!result.IsSafe) return result.Diagnostic;
        if (!ignoreAncestorAnchor && LightflowStorageLocations.PathsOverlap(candidate.CanonicalLocation, existing.CanonicalLocation))
            return "The resolved physical folders overlap another Media Root; an alias cannot establish a second identity.";
        return null;
    }

    public static void ValidatePaths(string root, IReadOnlyList<PathIdentityCandidate> candidates, CancellationToken token)
    {
        Require(PortablePathIdentityValidator.ValidateCandidates(candidates, token));
        foreach (var candidate in candidates)
        {
            token.ThrowIfCancellationRequested();
            var current = root;
            foreach (var component in candidate.RelativePath.Split('/'))
            {
                token.ThrowIfCancellationRequested();
                // Inspect all spellings that could collide with this component, including directories.
                var comparisonKey = PortablePathIdentityValidator.ValidateRelativePath(component).ComparisonKey;
                var matches = Directory.EnumerateFileSystemEntries(current)
                    .Where(path => PortablePathIdentityValidator.ValidateRelativePath(Path.GetFileName(path)).ComparisonKey == comparisonKey)
                    .Select(path => new PathIdentityCandidate(candidate.RootId, Guid.Empty, Path.GetFileName(path))).ToArray();
                Require(PortablePathIdentityValidator.ValidateCandidates(matches, token));
                Require(PortablePathIdentityValidator.ValidateReconciliation(
                    [new(candidate.RootId, candidate.AssetId, component)], matches, token));
                current = Path.Combine(current, component);
                try
                {
                    Require(PortablePathIdentityValidator.ValidateContainment(
                        (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0
                            ? StorageResolutionConfidence.Ambiguous : WindowsStorageLocationAssessor.ObserveMediaContainment(current), token));
                }
                catch (FileNotFoundException) { break; } // Missing media never authorizes reassignment.
                catch (DirectoryNotFoundException) { break; }
            }
        }
    }

    public static void Require(PathIdentityResult result)
    {
        if (result.Status == PathIdentityStatus.Cancelled) throw new OperationCanceledException();
        if (!result.IsSafe) throw new MediaPathIdentityException(result);
    }

    public void Dispose() => _assessor.Dispose();
}
