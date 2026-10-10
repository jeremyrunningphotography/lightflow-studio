using System.Text;
using Lightflow.Domain;

namespace Lightflow.Application;

public enum PathIdentityStatus { Safe, UnsupportedPortableName, AmbiguousMapping, UnsafeContainment, NativeEvidenceUnavailable, Cancelled }
public sealed record PathIdentityResult(PathIdentityStatus Status, string? Diagnostic = null, string? ComparisonKey = null)
{
    public bool IsSafe => Status == PathIdentityStatus.Safe;
}
public sealed record PathIdentityCandidate(Guid RootId, Guid AssetId, string RelativePath, string? LegacyKey = null);

/// <summary>Read-only comparison keys are never persisted. Existing repositories retain all identity ownership.</summary>
public static class PortablePathIdentityValidator
{
    public static PathIdentityResult ValidateRelativePath(string? path, CancellationToken token = default)
    {
        if (token.IsCancellationRequested) return new(PathIdentityStatus.Cancelled);
        if (string.IsNullOrEmpty(path)) return Unsupported("Supply a nonempty relative media path.");
        if (path.StartsWith('/') || path.StartsWith('\\') || path.Contains(':'))
            return Unsupported("Absolute, drive-relative and colon paths are not portable relative paths.");
        foreach (var component in path.Split('/'))
        {
            if (token.IsCancellationRequested) return new(PathIdentityStatus.Cancelled);
            if (component.Length == 0 || component is "." or "..")
                return new(PathIdentityStatus.UnsafeContainment, "Empty, dot and traversal components are refused; retain the original path.");
            if (component.EndsWith(' ') || component.EndsWith('.') || char.IsWhiteSpace(component[0]) ||
                char.IsWhiteSpace(component[^1]))
                return Unsupported("Leading/trailing whitespace or trailing periods require explicit review; no trimming is performed.");
            if (component.Any(c => c < 32 || c == 127 || "\\<>:\"|?*".Contains(c)))
                return Unsupported("The name contains a separator or character unsupported by portable Windows/macOS paths.");
            var stem = component.Split('.')[0].ToUpperInvariant();
            if (stem is "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$" ||
                (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal)) &&
                 "123456789¹²³".Contains(stem[3])))
                return Unsupported("Windows reserved device names cannot be mapped portably.");
        }
        try
        {
            // Validate UTF-16 as well as normalization; no replacement encoding is permitted.
            _ = new UTF8Encoding(false, true).GetByteCount(path);
            return new(PathIdentityStatus.Safe, ComparisonKey: path.Normalize(NormalizationForm.FormC).ToUpperInvariant());
        }
        catch (ArgumentException) { return Unsupported("Malformed Unicode cannot establish portable path identity."); }
    }

    public static PathIdentityResult ValidateCandidates(IEnumerable<PathIdentityCandidate> candidates,
        CancellationToken token = default)
    {
        if (token.IsCancellationRequested) return new(PathIdentityStatus.Cancelled);
        var keys = new Dictionary<(Guid, string), PathIdentityCandidate>();
        var identities = new Dictionary<Guid, PathIdentityCandidate>();
        var components = new Dictionary<(Guid, string), string>();
        foreach (var candidate in candidates)
        {
            if (token.IsCancellationRequested) return new(PathIdentityStatus.Cancelled);
            if (candidate.RootId == Guid.Empty) return new(PathIdentityStatus.AmbiguousMapping, "A logical RootId is required.");
            var result = ValidateRelativePath(candidate.RelativePath, token);
            if (!result.IsSafe) return result;
            var prefix = "";
            foreach (var component in candidate.RelativePath.Split('/'))
            {
                prefix = prefix.Length == 0 ? component : prefix + "/" + component;
                var componentKey = (candidate.RootId, ValidateRelativePath(prefix).ComparisonKey!);
                if (components.TryGetValue(componentKey, out var spelling) && spelling != prefix)
                    return new(PathIdentityStatus.AmbiguousMapping, "Directory or filename components differ only by case/Unicode normalization. Preserve each historical path for review.");
                components[componentKey] = prefix;
            }
            var key = (candidate.RootId, result.ComparisonKey!);
            if (keys.TryGetValue(key, out var prior))
                return new(PathIdentityStatus.AmbiguousMapping,
                    $"Multiple candidates map to ‘{candidate.RelativePath}’ after case/Unicode comparison. Preserve both identities for review.");
            keys.Add(key, candidate);
            if (candidate.AssetId != Guid.Empty && !identities.TryAdd(candidate.AssetId, candidate))
                return new(PathIdentityStatus.AmbiguousMapping, "One AssetId has multiple location candidates. Preserve the original rows.");
        }
        return new(PathIdentityStatus.Safe);
    }

    /// <summary>Match observations to historical rows only when spelling is exact. Never rewrite a historical key.</summary>
    public static PathIdentityResult ValidateReconciliation(IReadOnlyList<PathIdentityCandidate> persisted,
        IReadOnlyList<PathIdentityCandidate> observed, CancellationToken token = default)
    {
        var result = ValidateCandidates(persisted, token);
        if (!result.IsSafe) return result;
        if (persisted.Any(candidate => candidate.LegacyKey is not null && candidate.LegacyKey != candidate.RelativePath.ToUpperInvariant()))
            return new(PathIdentityStatus.AmbiguousMapping, "The historical Windows path key disagrees with its stored spelling. Path-based reconciliation requires explicit review; no keys were rewritten.");
        result = ValidateCandidates(observed, token);
        if (!result.IsSafe) return result;
        var prior = persisted.ToDictionary(c => (c.RootId, ValidateRelativePath(c.RelativePath).ComparisonKey!));
        var combined = new List<PathIdentityCandidate>(persisted);
        foreach (var item in observed)
        {
            if (token.IsCancellationRequested) return new(PathIdentityStatus.Cancelled);
            if (prior.TryGetValue((item.RootId, ValidateRelativePath(item.RelativePath).ComparisonKey!), out var row) &&
                !string.Equals(row.RelativePath, item.RelativePath, StringComparison.Ordinal))
                return new(PathIdentityStatus.AmbiguousMapping, "Observed and historical paths differ only by case or Unicode normalization. Explicit review is required; no identity was changed.");
            if (row is null) combined.Add(item);
        }
        return ValidateCandidates(combined, token);
    }

    public static PathIdentityResult ValidateCatalogIdentity(Guid expected, Guid actual, CancellationToken token = default) =>
        token.IsCancellationRequested ? new(PathIdentityStatus.Cancelled) :
        expected == Guid.Empty || actual == Guid.Empty || expected != actual
            ? new(PathIdentityStatus.AmbiguousMapping, "The CatalogId does not match. Preserve the existing Catalog and configuration.")
            : new(PathIdentityStatus.Safe);

    public static PathIdentityResult ValidateContainment(StorageResolutionConfidence containment, CancellationToken token = default) =>
        token.IsCancellationRequested ? new(PathIdentityStatus.Cancelled) : containment switch
        {
            StorageResolutionConfidence.Resolved => new(PathIdentityStatus.Safe),
            StorageResolutionConfidence.Ambiguous => new(PathIdentityStatus.UnsafeContainment,
                "Filesystem links or aliases cannot establish unambiguous containment beneath this logical root. Preserve its mapping and authored rows."),
            _ => new(PathIdentityStatus.NativeEvidenceUnavailable, "Native containment evidence is unavailable. Retry without changing identity.")
        };

    public static PathIdentityResult ValidateRootMapping(Guid existingRoot, Guid requestedRoot,
        string? existingPhysicalIdentity, string? requestedPhysicalIdentity, CancellationToken token = default) =>
        token.IsCancellationRequested ? new(PathIdentityStatus.Cancelled) :
        string.IsNullOrWhiteSpace(existingPhysicalIdentity) || string.IsNullOrWhiteSpace(requestedPhysicalIdentity)
            ? new(PathIdentityStatus.NativeEvidenceUnavailable, "Resolve physical root identities before mapping.") :
        existingPhysicalIdentity == requestedPhysicalIdentity && existingRoot != requestedRoot
            ? new(PathIdentityStatus.AmbiguousMapping, "That physical folder already belongs to another logical Media Root. Preserve its RootId; use an explicit remap.")
            : new(PathIdentityStatus.Safe);

    /// <summary>Accepted storage facts, scoped to one operation. OS volume namespaces are never compared across machines.</summary>
    public static PathIdentityResult ValidateNativeMapping(StorageAssessmentRequest request,
        StorageLocationAssessment? current, DateTimeOffset now, StorageLocationAssessment? previous = null,
        CancellationToken token = default)
    {
        var result = previous is not null && current is not null
            ? StorageLocationPolicy.Revalidate(request, previous, current, now, token)
            : StorageLocationPolicy.Evaluate(request, current, now, token);
        return result.Decision switch
        {
            StorageLocationDecision.Cancelled => new(PathIdentityStatus.Cancelled, result.Diagnostic),
            StorageLocationDecision.Eligible or StorageLocationDecision.RequiresVerifiedStaging => new(PathIdentityStatus.Safe),
            _ => new(result.Reason is StorageLocationReason.AmbiguousContainment or StorageLocationReason.UnresolvedAliases
                ? PathIdentityStatus.UnsafeContainment : PathIdentityStatus.NativeEvidenceUnavailable, result.Diagnostic)
        };
    }

    private static PathIdentityResult Unsupported(string diagnostic) => new(PathIdentityStatus.UnsupportedPortableName, diagnostic);
}
