using Lightflow.Application;
using Lightflow.Domain;
namespace LightflowStudio;

public interface ICatalogCompletionPeerStore : IAsyncDisposable { }

/// <summary>Composition/native effects only. All Catalog transaction and recovery decisions belong to CatalogLifecycle.</summary>
public interface ICatalogLifecycleEffects
{
    ILightflowStorageLocations Profile { get; }
    ICatalogConfigurationEffects Configuration { get; }
    ICatalogHostConsumerEffects Consumers { get; }
    ICatalogEvidenceEffects Evidence { get; }
    ICatalogRecoveryNativeEffects RecoveryNative { get; }
    ICatalogTransferNativeEffects TransferNative { get; }
    ICatalogRecoveryService? RecoveryOverride { get; }
    ICatalogRelocationTransfer? RelocationTransfer { get; }
    bool SupportsCompletionPeer { get; }
    void RequireInitializedProfile();
    ILightflowStorageLocations WithOverrides(ILightflowStorageLocations configured, string? catalogDirectory, string? previewsDirectory);
    void GuardConfiguredLeaves(ILightflowStorageLocations defaults, ILightflowStorageLocations configured);
    CatalogStorageAdmission CreateAdmission(ILightflowStorageLocations locations, StorageOperation operation, string? reservedCatalogDirectory = null);
    bool SamePath(string left, string right);
    bool PathsOverlap(string left, string right);
    bool Contains(string root, string candidate);
    void RequireContained(string root, string candidate);
    CatalogDatabaseSession Activate(CatalogDatabaseSession session);
    StartupStoreEvidence? BeginCompletionPeer(ILightflowStorageLocations locations, ICatalogConfigurationSnapshot configuration, StartupSessionCompletion completion);
    Task<(ICatalogCompletionPeerStore? Store, string? Diagnostic)> OpenCompletionPeerAsync(ICatalogConfigurationSnapshot configuration, ILightflowStorageLocations locations, CancellationToken token, StartupStoreEvidence? evidence = null);
    void ComposeConsumers(CatalogLifecycle lifecycle, ICatalogCompletionPeerStore? peer, string? diagnostic, StartupStoreEvidence? peerEvidence);
    Task StartMonitoringAsync(CancellationToken token);
    void NotifyCatalogRestored();
}
