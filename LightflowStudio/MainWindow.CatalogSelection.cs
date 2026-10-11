using System.IO;
using System.Windows;

namespace LightflowStudio;

public partial class MainWindow
{
    private void OpenExistingCatalog_Click(object sender, RoutedEventArgs e)
    {
        if (!CanRestartForCatalog()) return;
        var directory = PickFolder("Choose an existing Catalog folder containing LightflowCatalog.db", _storage.Locations.CatalogDirectory);
        if (directory is null) return;
        CatalogSelectionResult? selected = null;
        var operation = new CatalogOperationDialog("Validate existing Catalog", directory, async (token, _) =>
        {
            selected = await _storage.InspectCatalogSelectionAsync(directory, token);
            if (!selected.Succeeded) throw new IOException(selected.Diagnostic);
            return selected.Diagnostic ?? "The Catalog is valid. Its identity and authored data will be preserved.";
        }) { Owner = this };
        operation.ShowDialog();
        if (!operation.CompletedSuccessfully || selected?.Succeeded != true) return;
        if (_storage.CatalogAvailable && string.Equals(selected.Directory, _storage.Locations.CatalogDirectory, StringComparison.OrdinalIgnoreCase))
        { SettingsMessage.Text = "That Catalog is already open."; return; }
        if (!ConfirmationDialog.Confirm(this, "Open Existing Catalog", "Restart and open this Catalog?",
            "Lightflow will close this workspace and validate the selected Catalog again at startup. Configuration changes only after ownership and activation succeed. Media Roots and LUT folders may need configuration on this machine.",
            selected.Directory + "\nCatalog " + selected.CatalogId, "Restart and Open Catalog")) return;
        RestartForCatalog(selected.Directory, selected.CatalogId);
    }
    private void RetryCatalog_Click(object sender, RoutedEventArgs e)
    {
        if (CanRestartForCatalog()) RestartForCatalog();
    }
    private bool CanRestartForCatalog()
    {
        if (_exportCoordinator.ActiveCount > 0 || _jobCancellation is not null || _premiereBridge?.HasUnresolvedDispatchedHandoff == true)
        {
            NoticeDialog.Show(this, "Catalog restart", "Finish active work first",
                "Finish or stop active Jobs and Premiere handoffs before restarting to open a Catalog.");
            return false;
        }
        return TryLeaveInspectorContext();
    }
    private void RestartForCatalog(string? directory = null, Guid? id = null)
    {
        if (System.Windows.Application.Current is not App app) return;
        app.PrepareCatalogRestart(directory, id);
        Close();
        if (!_workspaceClosed)
        {
            app.CancelCatalogRestart();
            SettingsMessage.Text = "Catalog restart cancelled. The current Catalog remains configured.";
        }
    }
}
