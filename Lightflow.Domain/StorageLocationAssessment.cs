namespace Lightflow.Domain;

public enum StorageRole { Unknown, ActiveCatalog, ClosedCatalogBackupTransfer, MediaSource, RebuildablePreview, TemporaryProfile }
public enum StorageOperation { Unknown, Open, Create, Relocate, RestoreActivation, Read, Write }
public enum StorageLocality { Unknown, Local, Network, Unavailable }
public enum StorageAvailability { Unknown, Available, Unavailable }
public enum StorageCapability { Unknown, Supported, Unsupported }
public enum StorageResolutionConfidence { Unknown, Resolved, Ambiguous }
public enum StorageAssessmentStatus { Unknown, Complete, Failed, Cancelled }

/// <summary>
/// Caller-owned operation identity and invalidation generation. RequestedLocation is opaque,
/// preserved exactly; neutral code never normalizes paths or infers locality from spelling.
/// </summary>
public sealed record StorageAssessmentRequest(Guid OperationId, long Generation, StorageRole Role,
    StorageOperation Operation, string RequestedLocation);

/// <summary>
/// Adapter-owned opaque identities. VolumeIdentity must distinguish replacement/remount epochs,
/// not merely a reusable drive letter/mount point. LocationIdentity includes the resolved target
/// (and intended leaf for creation); FileSystemIdentity identifies the filesystem instance/type.
/// </summary>
public sealed record ResolvedStorageIdentity(string CanonicalLocation, string LocationIdentity,
    string VolumeIdentity, string FileSystemIdentity);

/// <summary>
/// Facts, not admission decisions. QualifiedCatalogFileSystem means supported local Catalog
/// qualification, not a claim that every filesystem of that name is safe. Unknown is never true.
/// Catalog durability/locking/companion-file facts do not govern ordinary byte transport or caches.
/// </summary>
public sealed record StorageCapabilityFacts(StorageCapability Read, StorageCapability Write,
    StorageCapability QualifiedCatalogFileSystem, StorageCapability DurableWrites,
    StorageCapability FileLocking, StorageCapability CompanionFiles);

/// <summary>
/// Immutable snapshot, never a writer lease or proof that a Catalog exists/is safe to create.
/// Availability describes the resolved volume and accessible path/creation parent, not database
/// existence. Containment includes the requested role's protected ownership boundaries.
/// </summary>
public sealed record StorageLocationAssessment(StorageAssessmentRequest Request, Guid AssessmentId,
    DateTimeOffset AssessedAtUtc, DateTimeOffset ExpiresAtUtc, string PolicyVersion,
    StorageAssessmentStatus Status, ResolvedStorageIdentity? Identity, StorageLocality Locality,
    StorageAvailability VolumeAvailability, StorageAvailability PathAvailability,
    StorageResolutionConfidence AliasResolution, StorageResolutionConfidence Containment,
    StorageCapabilityFacts? Capabilities, string? ProviderDiagnostic = null);
