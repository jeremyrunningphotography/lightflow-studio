namespace LightflowStudio;

public interface ILightflowStorageLocations
{
    bool IsIsolated => false;
    string ApplicationDataDirectory { get; }
    string SettingsPath { get; }
    string StatePath { get; }
    string WorkspaceStatePath { get; }
    string JobHistoryPath { get; }
    string FileOperationHistoryPath { get; }
    string TrimHistoryPath { get; }
    string OutputIdentityDirectory { get; }
    string CatalogDirectory { get; }
    string CatalogDatabasePath { get; }
    string CatalogBackupsDirectory { get; }
    string PreviewsDirectory { get; }
    string PreviewsDatabasePath { get; }
    string ThumbnailCacheDirectory { get; }
    string StandardPreviewCacheDirectory { get; }
    string TemporaryDirectory { get; }
    string LogsDirectory { get; }
    string ActivityLogPath { get; }
    string MachineIdentityPath { get; }
}
