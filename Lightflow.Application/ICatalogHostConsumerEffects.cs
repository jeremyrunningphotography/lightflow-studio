namespace Lightflow.Application;

/// <summary>Effects on the host's existing consumers. The Catalog lifecycle owns ordering and admission.</summary>
public interface ICatalogHostConsumerEffects
{
    void CancelReadUsage();
    void DisposeCaches();
    Task StopMonitoringAsync();
    Task StopDerivedWorkAsync();
    Task RestartAsync();
    Task<IDisposable> EnterCompletionPeerMaintenanceAsync();
    bool HasQualifiedCompletionPeer { get; }
    Task CloseCompletionPeerAsync();
    void CompleteCompletionPeer();
    bool CompletionPeerCompleted { get; }
    void ReleaseCompletionPeerEvidence();
    void DisposeReadUsage();
}
