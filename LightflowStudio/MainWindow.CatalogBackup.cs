using System.Windows;

namespace LightflowStudio;

public partial class MainWindow
{
    private bool _catalogCloseDialogActive;
    private bool TryPrepareCatalogExit()
    {
        if (_catalogCloseDialogActive) return false;
        // Packaging's noninteractive smoke must exercise shutdown without opening a user prompt.
        if (!_storage.Settings.BackupCatalogOnClose || Environment.GetCommandLineArgs().Contains("--startup-smoke-test")) return true;
        _catalogCloseDialogActive = true;
        try
        {
            var dialog = new CatalogBackupDialog(_storage, message => _activityLogFile.TryAppend(message)) { Owner = this };
            dialog.ShowDialog();
            _settings = _storage.Settings;
            SettingsCatalogBackupDirectory.Text = _storage.BackupDirectory;
            if (!dialog.ExitApproved) { _storage.CancelPreparedExit(); _forceClose = false; _closeAfterCurrent = false; }
            return dialog.ExitApproved;
        }
        finally { _catalogCloseDialogActive = false; }
    }

    private void ChooseCatalogBackupDirectory_Click(object sender, RoutedEventArgs e)
    {
        var selected = PickFolder("Choose Catalog backup location", SettingsCatalogBackupDirectory.Text);
        if (!string.IsNullOrWhiteSpace(selected)) SettingsCatalogBackupDirectory.Text = selected;
    }

    private void BackupCatalog_Click(object sender, RoutedEventArgs e)
    {
        var previousDestination = _storage.BackupDirectory;
        var dialog = new CatalogBackupDialog(_storage, message => _activityLogFile.TryAppend(message),
            exit: false, destination: SettingsCatalogBackupDirectory.Text) { Owner = this };
        dialog.ShowDialog();
        _settings = _storage.Settings;
        if (_storage.BackupDirectory != previousDestination || dialog.ExitApproved)
            SettingsCatalogBackupDirectory.Text = _storage.BackupDirectory;
        RefreshCatalogBackups();
    }
}
