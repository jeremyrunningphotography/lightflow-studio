global using LightflowStorageLocations = Lightflow.Catalog.Tests.NeutralCatalogLocations;
using LightflowStudio;
namespace Lightflow.Catalog.Tests;
internal sealed record NeutralCatalogLocations(string ApplicationDataDirectory, string CatalogDirectory) : ILightflowStorageLocations
{
    internal const string CatalogFileName = "LightflowCatalog.db";
    internal static NeutralCatalogLocations Create(string root, LightflowStorageLocationOptions? options = null) => new(Path.GetFullPath(root), Path.GetFullPath(options?.CatalogDirectory ?? Path.Combine(root, "Catalog")));
    public string CatalogDatabasePath => Path.Combine(CatalogDirectory, CatalogFileName);
    public string CatalogBackupsDirectory => Path.Combine(CatalogDirectory, "Backups");
    public string SettingsPath => Path.Combine(ApplicationDataDirectory, "SettingsPath");
    public string StatePath => Path.Combine(ApplicationDataDirectory, "StatePath");
    public string WorkspaceStatePath => Path.Combine(ApplicationDataDirectory, "WorkspaceStatePath");
    public string JobHistoryPath => Path.Combine(ApplicationDataDirectory, "JobHistoryPath");
    public string FileOperationHistoryPath => Path.Combine(ApplicationDataDirectory, "FileOperationHistoryPath");
    public string TrimHistoryPath => Path.Combine(ApplicationDataDirectory, "TrimHistoryPath");
    public string OutputIdentityDirectory => Path.Combine(ApplicationDataDirectory, "OutputIdentityDirectory");
    public string PreviewsDirectory => Path.Combine(ApplicationDataDirectory, "PreviewsDirectory");
    public string PreviewsDatabasePath => Path.Combine(ApplicationDataDirectory, "PreviewsDatabasePath");
    public string ThumbnailCacheDirectory => Path.Combine(ApplicationDataDirectory, "ThumbnailCacheDirectory");
    public string StandardPreviewCacheDirectory => Path.Combine(ApplicationDataDirectory, "StandardPreviewCacheDirectory");
    public string TemporaryDirectory => Path.Combine(ApplicationDataDirectory, "TemporaryDirectory");
    public string LogsDirectory => Path.Combine(ApplicationDataDirectory, "LogsDirectory");
    public string ActivityLogPath => Path.Combine(ApplicationDataDirectory, "ActivityLogPath");
    public string MachineIdentityPath => Path.Combine(ApplicationDataDirectory, "MachineIdentityPath");
}
