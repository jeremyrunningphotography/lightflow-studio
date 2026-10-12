namespace LightflowStudio;

public enum StorageStartupStatus { Ready, CatalogUnavailable, CatalogMissing, CatalogUnreadable, CatalogCorrupt, CatalogIdentityMismatch, InvalidConfiguration, FirstRunChoiceRequired }
public enum StorageChangeStatus { Succeeded, SucceededWithWarning, EquivalentLocation, InvalidDestination, ConflictingCatalog, Failed }
public sealed record StorageChangeResult(StorageChangeStatus Status, string? Diagnostic = null)
{
    public bool Succeeded => Status is StorageChangeStatus.Succeeded or StorageChangeStatus.SucceededWithWarning;
}

public enum CatalogRecoveryReason { None, LocationUnavailable, CompetingOwner, PendingRecovery, ValidationFailed, OwnershipUnavailable, LocationRefused, UnsupportedStorage, CatalogDirectoryRequired, InitialLocationUnavailable, OutsideWorkspace, ConfigurationInvalid, OperationFailed }

public sealed record CatalogSelectionResult(bool Succeeded, Guid? CatalogId = null, string? Directory = null, string? Diagnostic = null,
    CatalogRecoveryReason RecoveryReason = CatalogRecoveryReason.None);
