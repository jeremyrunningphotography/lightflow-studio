using Lightflow.Domain;

namespace Lightflow.Application;

public enum StorageLocationDecision { Rejected, Eligible, RequiresVerifiedStaging, Cancelled }
public enum StorageLocationReason
{
    Eligible, VerifiedStagingRequired, Cancelled, InvalidRequest, AssessmentFailed,
    IncompleteAssessment, RequestMismatch, PolicyVersionMismatch, StaleAssessment,
    MissingIdentity, LocationChanged, VolumeChanged, FileSystemChanged, CapabilityFactsChanged,
    LocationUnavailable, UnknownLocality, NetworkActiveCatalog, NetworkPreview,
    UnresolvedAliases, AmbiguousContainment, MissingCapabilities, ReadUnavailable,
    ReadOnly, UnsupportedCatalogFileSystem, UnsupportedCatalogCapabilities
}

/// <summary>Location suitability only. No decision grants Catalog creation, mutation or writer ownership.</summary>
public sealed record StorageLocationPolicyResult(StorageLocationDecision Decision,
    StorageLocationReason Reason, string Diagnostic, string PolicyVersion,
    StorageAssessmentRequest Request, StorageLocationAssessment? Assessment);

/// <summary>One deterministic role policy. No filesystem access, clocks, path substitution or writer lifecycle.</summary>
public static class StorageLocationPolicy
{
    public const string Version = "storage-roles/1";

    public static StorageLocationPolicyResult Evaluate(StorageAssessmentRequest request,
        StorageLocationAssessment? assessment, DateTimeOffset nowUtc, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        StorageLocationPolicyResult Finish(StorageLocationReason reason) => Result(request, assessment, reason);
        if (cancellationToken.IsCancellationRequested || assessment?.Status == StorageAssessmentStatus.Cancelled)
            return Finish(StorageLocationReason.Cancelled);
        if (!ValidRequest(request)) return Finish(StorageLocationReason.InvalidRequest);
        if (assessment is null) return Finish(StorageLocationReason.IncompleteAssessment);
        if (assessment.Request != request) return Finish(StorageLocationReason.RequestMismatch);
        if (assessment.PolicyVersion != Version) return Finish(StorageLocationReason.PolicyVersionMismatch);
        if (assessment.Status == StorageAssessmentStatus.Failed) return Finish(StorageLocationReason.AssessmentFailed);
        if (assessment.Status != StorageAssessmentStatus.Complete || assessment.AssessmentId == Guid.Empty)
            return Finish(StorageLocationReason.IncompleteAssessment);
        if (assessment.AssessedAtUtc > nowUtc || assessment.ExpiresAtUtc <= nowUtc ||
            assessment.ExpiresAtUtc <= assessment.AssessedAtUtc)
            return Finish(StorageLocationReason.StaleAssessment);
        if (assessment.Locality == StorageLocality.Unavailable ||
            assessment.VolumeAvailability == StorageAvailability.Unavailable ||
            assessment.PathAvailability == StorageAvailability.Unavailable)
            return Finish(StorageLocationReason.LocationUnavailable);
        if (assessment.VolumeAvailability != StorageAvailability.Available ||
            assessment.PathAvailability != StorageAvailability.Available)
            return Finish(StorageLocationReason.IncompleteAssessment);
        if (assessment.Locality is not (StorageLocality.Local or StorageLocality.Network))
            return Finish(StorageLocationReason.UnknownLocality);
        if (request.Role == StorageRole.ActiveCatalog && assessment.Locality == StorageLocality.Network)
            return Finish(StorageLocationReason.NetworkActiveCatalog);
        if (request.Role == StorageRole.RebuildablePreview && assessment.Locality == StorageLocality.Network)
            return Finish(StorageLocationReason.NetworkPreview);
        if (assessment.Identity is not { } identity || string.IsNullOrWhiteSpace(identity.CanonicalLocation) ||
            string.IsNullOrWhiteSpace(identity.LocationIdentity) || string.IsNullOrWhiteSpace(identity.VolumeIdentity) ||
            string.IsNullOrWhiteSpace(identity.FileSystemIdentity)) return Finish(StorageLocationReason.MissingIdentity);
        if (assessment.AliasResolution != StorageResolutionConfidence.Resolved)
            return Finish(StorageLocationReason.UnresolvedAliases);
        if (assessment.Containment != StorageResolutionConfidence.Resolved)
            return Finish(StorageLocationReason.AmbiguousContainment);
        if (assessment.Capabilities is not { } facts) return Finish(StorageLocationReason.MissingCapabilities);
        if (RequiresRead(request) && facts.Read != StorageCapability.Supported)
            return Finish(facts.Read == StorageCapability.Unsupported ? StorageLocationReason.ReadUnavailable : StorageLocationReason.MissingCapabilities);
        if (RequiresWrite(request) && facts.Write != StorageCapability.Supported)
            return Finish(facts.Write == StorageCapability.Unsupported ? StorageLocationReason.ReadOnly : StorageLocationReason.MissingCapabilities);
        if (request.Role == StorageRole.ActiveCatalog)
        {
            if (facts.QualifiedCatalogFileSystem != StorageCapability.Supported)
                return Finish(facts.QualifiedCatalogFileSystem == StorageCapability.Unsupported
                    ? StorageLocationReason.UnsupportedCatalogFileSystem : StorageLocationReason.MissingCapabilities);
            var required = new[] { facts.DurableWrites, facts.FileLocking, facts.CompanionFiles };
            if (required.Any(f => f == StorageCapability.Unsupported)) return Finish(StorageLocationReason.UnsupportedCatalogCapabilities);
            if (required.Any(f => f != StorageCapability.Supported)) return Finish(StorageLocationReason.MissingCapabilities);
        }
        return Finish(request.Role == StorageRole.ClosedCatalogBackupTransfer
            ? StorageLocationReason.VerifiedStagingRequired : StorageLocationReason.Eligible);
    }

    /// <summary>
    /// Compare a new boundary snapshot to the prior snapshot of the same operation. A changed mount,
    /// target or fact invalidates prior approval; caller must explicitly restart assessment/preflight.
    /// Reusing the old snapshot is rejected even before its expiry. This is not an atomic OS-use guard.
    /// </summary>
    public static StorageLocationPolicyResult Revalidate(StorageAssessmentRequest request,
        StorageLocationAssessment previous, StorageLocationAssessment current, DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(current);
        var result = Evaluate(request, current, nowUtc, cancellationToken);
        if (result.Decision is StorageLocationDecision.Rejected or StorageLocationDecision.Cancelled) return result;
        StorageLocationPolicyResult Finish(StorageLocationReason reason) => Result(request, current, reason);
        if (previous.Request != request) return Finish(StorageLocationReason.RequestMismatch);
        if (previous.PolicyVersion != Version) return Finish(StorageLocationReason.PolicyVersionMismatch);
        var priorResult = Evaluate(request, previous, previous.AssessedAtUtc);
        if (priorResult.Decision is StorageLocationDecision.Rejected or StorageLocationDecision.Cancelled || previous.Identity is null)
            return Finish(StorageLocationReason.IncompleteAssessment);
        if (current.AssessmentId == previous.AssessmentId || current.AssessedAtUtc < previous.AssessedAtUtc)
            return Finish(StorageLocationReason.StaleAssessment);
        if (previous.Identity.VolumeIdentity != current.Identity!.VolumeIdentity) return Finish(StorageLocationReason.VolumeChanged);
        if (previous.Identity.FileSystemIdentity != current.Identity.FileSystemIdentity) return Finish(StorageLocationReason.FileSystemChanged);
        if (previous.Identity.LocationIdentity != current.Identity.LocationIdentity ||
            previous.Identity.CanonicalLocation != current.Identity.CanonicalLocation) return Finish(StorageLocationReason.LocationChanged);
        if (previous.Capabilities != current.Capabilities || previous.Locality != current.Locality ||
            previous.AliasResolution != current.AliasResolution || previous.Containment != current.Containment ||
            previous.VolumeAvailability != current.VolumeAvailability || previous.PathAvailability != current.PathAvailability)
            return Finish(StorageLocationReason.CapabilityFactsChanged);
        return result;
    }

    private static bool ValidRequest(StorageAssessmentRequest request) => request.OperationId != Guid.Empty &&
        request.Generation >= 0 && !string.IsNullOrWhiteSpace(request.RequestedLocation) && request.Role switch
        {
            StorageRole.ActiveCatalog => request.Operation is StorageOperation.Open or StorageOperation.Create or StorageOperation.Relocate or StorageOperation.RestoreActivation,
            StorageRole.ClosedCatalogBackupTransfer or StorageRole.TemporaryProfile => request.Operation is StorageOperation.Read or StorageOperation.Write,
            StorageRole.MediaSource => request.Operation == StorageOperation.Read,
            StorageRole.RebuildablePreview => request.Operation is StorageOperation.Read or StorageOperation.Write or StorageOperation.Relocate,
            _ => false
        };

    private static bool RequiresRead(StorageAssessmentRequest request) => request.Role == StorageRole.ActiveCatalog ||
        request.Operation == StorageOperation.Read;
    private static bool RequiresWrite(StorageAssessmentRequest request) => request.Role == StorageRole.ActiveCatalog ||
        request.Operation is StorageOperation.Write or StorageOperation.Relocate;

    private static StorageLocationPolicyResult Result(StorageAssessmentRequest request, StorageLocationAssessment? assessment,
        StorageLocationReason reason) => new(reason switch
        {
            StorageLocationReason.Eligible => StorageLocationDecision.Eligible,
            StorageLocationReason.VerifiedStagingRequired => StorageLocationDecision.RequiresVerifiedStaging,
            StorageLocationReason.Cancelled => StorageLocationDecision.Cancelled,
            _ => StorageLocationDecision.Rejected
        }, reason, Diagnostic(reason), Version, request, assessment);

    private static string Diagnostic(StorageLocationReason reason) => reason switch
    {
        StorageLocationReason.Eligible => "Location is eligible for the requested role; retain its existing operation and ownership safeguards.",
        StorageLocationReason.VerifiedStagingRequired => "Use a locally finalized closed Catalog artifact, verified byte transfer and verified local copyback before restore activation. Location eligibility does not authorize live database transport.",
        StorageLocationReason.Cancelled => "Storage assessment was cancelled. Stop before admission and preserve existing storage.",
        StorageLocationReason.InvalidRequest => "Supply a valid operation identity, generation, location and role/operation pair.",
        StorageLocationReason.AssessmentFailed => "Storage assessment failed. Retry assessment without creating or replacing a Catalog.",
        StorageLocationReason.IncompleteAssessment => "Required storage assessment facts are incomplete. Resolve them before admission.",
        StorageLocationReason.RequestMismatch => "The assessment belongs to another operation, location or generation. Assess the requested operation again.",
        StorageLocationReason.PolicyVersionMismatch => "The storage policy version changed. Obtain a new assessment using the current policy.",
        StorageLocationReason.StaleAssessment => "The storage assessment is stale or reused. Resolve current facts at the operation boundary.",
        StorageLocationReason.MissingIdentity => "Resolve the location, volume and filesystem identities before admission.",
        StorageLocationReason.LocationChanged => "The resolved location changed. Restart preflight without substituting or replacing storage.",
        StorageLocationReason.VolumeChanged => "The resolved volume or mount changed. Restart preflight for the current volume.",
        StorageLocationReason.FileSystemChanged => "The filesystem identity changed. Restart preflight for the current filesystem.",
        StorageLocationReason.CapabilityFactsChanged => "Relevant storage facts changed. Restart preflight using current capabilities.",
        StorageLocationReason.LocationUnavailable => "The volume or path is unavailable. Reconnect or select an explicitly approved location; preserve the configured Catalog.",
        StorageLocationReason.UnknownLocality => "Storage locality cannot be established. Resolve it before admission.",
        StorageLocationReason.NetworkActiveCatalog => "Active Catalogs require qualified local storage. Network shares and mapped network drives are unsupported; preserve existing Catalog files.",
        StorageLocationReason.NetworkPreview => "Rebuildable Previews use machine-local storage. Choose a resolved local cache destination.",
        StorageLocationReason.UnresolvedAliases => "Resolve links and aliases confidently before admission; do not normalize or merge identities.",
        StorageLocationReason.AmbiguousContainment => "Resolve protected storage containment before admission; do not cross Catalog/cache ownership boundaries.",
        StorageLocationReason.MissingCapabilities => "Required filesystem capability facts are missing. Obtain a complete assessment.",
        StorageLocationReason.ReadUnavailable => "The operation requires readable storage. Resolve access before admission.",
        StorageLocationReason.ReadOnly => "The operation requires writable storage. Choose a qualified writable destination or restore access.",
        StorageLocationReason.UnsupportedCatalogFileSystem => "The filesystem is not qualified for active Catalog storage. Choose supported local storage.",
        StorageLocationReason.UnsupportedCatalogCapabilities => "Required Catalog durability, locking or companion-file capabilities are unsupported. Choose qualified local storage.",
        _ => throw new ArgumentOutOfRangeException(nameof(reason))
    };
}
