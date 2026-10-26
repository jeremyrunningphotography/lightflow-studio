using Lightflow.Application;
using System.IO;

namespace LightflowStudio;

public enum CatalogReconciliationStatus
{
    Succeeded,
    RootNotFound,
    RootUnavailable,
    FolderUnavailable,
    InvalidRequest,
    EnumerationFailed,
    Canceled,
    Failed
}

public enum CatalogReconciliationItemStatus { New, Unchanged, Changed, Missing }

public sealed record CatalogReconciliationItem(
    Guid AssetId,
    string RelativePath,
    CatalogReconciliationItemStatus Status);

public sealed record CatalogReconciliationResult(
    CatalogReconciliationStatus Status,
    Guid RootId,
    string RelativeFolder,
    IReadOnlyList<CatalogReconciliationItem> Items,
    int UnsupportedCount = 0,
    string? Diagnostic = null)
{
    public bool Succeeded => Status == CatalogReconciliationStatus.Succeeded;
    public int NewCount => Items.Count(item => item.Status == CatalogReconciliationItemStatus.New);
    public int UnchangedCount => Items.Count(item => item.Status == CatalogReconciliationItemStatus.Unchanged);
    public int ChangedCount => Items.Count(item => item.Status == CatalogReconciliationItemStatus.Changed);
    public int MissingCount => Items.Count(item => item.Status == CatalogReconciliationItemStatus.Missing);
}

public interface ICatalogReconciliationService
{
    Task<CatalogReconciliationResult> ReconcileAsync(MediaFolderEnumerationRequest request,
        CancellationToken cancellationToken = default);
}
