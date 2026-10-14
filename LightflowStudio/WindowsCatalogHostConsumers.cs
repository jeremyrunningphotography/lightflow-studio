using Lightflow.Application;
namespace LightflowStudio;

internal sealed partial class LightflowStorageCoordinator
{
    // Existing consumer effects only; CatalogLifecycle owns shutdown ordering and completion decisions.
    private sealed class WindowsCatalogHostConsumers(LightflowStorageCoordinator owner) : ICatalogHostConsumerEffects
    {
        public void CancelReadUsage() => owner._readShutdown.Cancel();
        public void DisposeCaches() { if (owner.LutCache is IDisposable cache) cache.Dispose(); }
        public Task StopMonitoringAsync() => owner.DisposeMediaMonitoringAsync();
        public Task StopDerivedWorkAsync() => owner.DisposeDerivedWorkSchedulerAsync();
        public async Task RestartAsync()
        {
            owner.DerivedWork = owner.CreateDerivedWorkScheduler();
            await owner.RecreateMediaMonitoringAsync().ConfigureAwait(false);
        }
        public Task<IDisposable> EnterCompletionPeerMaintenanceAsync() => owner._previewOperations.EnterMaintenanceAsync();
        public bool HasQualifiedCompletionPeer => owner.Previews is not null;
        public async Task CloseCompletionPeerAsync()
        {
            if (owner.Previews is not null) await owner.Previews.DisposeAsync().ConfigureAwait(false);
            owner.Previews = null;
        }
        public void CompleteCompletionPeer() => owner._previewStartup?.Complete(owner.Locations.PreviewsDatabasePath);
        public bool CompletionPeerCompleted => owner._previewStartup?.Completed == true;
        public void ReleaseCompletionPeerEvidence() { owner._previewStartup?.Dispose(); owner._previewStartup = null; }
        public void DisposeReadUsage() => owner._readShutdown.Dispose();
    }
}
