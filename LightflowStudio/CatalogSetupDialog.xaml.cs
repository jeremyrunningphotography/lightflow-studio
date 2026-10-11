using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace LightflowStudio;

public partial class CatalogSetupDialog : Window
{
    private readonly LightflowStorageCoordinator _storage;
    internal bool CreateNew { get; private set; }
    internal CatalogSelectionResult? Selection { get; private set; }
    internal CatalogSetupDialog(LightflowStorageCoordinator storage)
    {
        InitializeComponent(); _storage = storage; NewLocation.Text = storage.Locations.CatalogDirectory;
        SourceInitialized += (_, _) => WindowAppearance.EnableDarkTitleBar(this);
    }
    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
    private void Create_Click(object sender, RoutedEventArgs e) { CreateNew = true; DialogResult = true; }
    private void Open_Click(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFolderDialog { Title = "Choose a Catalog folder containing LightflowCatalog.db" };
        if (picker.ShowDialog(this) != true) return;
        CatalogSelectionResult? selected = null;
        var progress = new CatalogOperationDialog("Validate Catalog", picker.FolderName, async (token, _) =>
        {
            selected = await _storage.InspectCatalogSelectionAsync(picker.FolderName, token);
            if (!selected.Succeeded) throw new IOException(selected.Diagnostic);
            return "Catalog validated. Its existing identity and authored data will be preserved.";
        }) { Owner = this };
        progress.ShowDialog();
        if (selected?.Succeeded != true || !progress.CompletedSuccessfully) return;
        if (!ConfirmationDialog.Confirm(this, "Open Existing Catalog", "Open this Catalog?",
            "Lightflow will preserve its identity and data. Media Roots and LUT folders may need configuration on this machine.",
            selected.Directory + "\nCatalog " + selected.CatalogId, "Open Catalog")) return;
        Selection = selected; DialogResult = true;
    }
}
