using Lightflow.Application;
namespace LightflowStudio;
internal sealed partial class LightflowStorageCoordinator
{
    public Task<StorageChangeResult> RelocateCatalogAsync(string destinationDirectory, CancellationToken cancellationToken = default,
        IProgress<ClosedCatalogTransferState>? progress = null)
        => _lifecycle.RelocateCatalogAsync(destinationDirectory, cancellationToken, progress);
}
