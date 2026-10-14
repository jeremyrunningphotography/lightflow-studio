namespace LightflowStudio;

public sealed record CatalogStartupResult(StorageStartupStatus Status, CatalogLifecycle? Coordinator = null, string? Diagnostic = null,
    CatalogRecoveryReason RecoveryReason = CatalogRecoveryReason.None)
{
    public bool IsReady => Status == StorageStartupStatus.Ready;
}
