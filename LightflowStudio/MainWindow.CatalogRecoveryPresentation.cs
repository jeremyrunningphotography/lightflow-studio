using System.Windows;

namespace LightflowStudio;

public partial class MainWindow
{
    private readonly CatalogRecoveryReason _catalogRecoveryReason;

    internal void ApplyCatalogRecoveryPresentation(StorageStartupStatus status, CatalogRecoveryReason reason, string? diagnostic)
    {
        var presentation = CatalogRecoveryPresentation.Create(status, reason, diagnostic);
        var visibility = presentation.ShowRecovery ? Visibility.Visible : Visibility.Collapsed;
        SettingsRecoveryBanner.Visibility = CatalogRecoveryPanel.Visibility = RetryCatalogButton.Visibility = visibility;
        CatalogRecoveryHeading.Text = presentation.Heading;
        CatalogRecoveryMessage.Text = presentation.Description;
        CatalogRecoveryAssurance.Text = presentation.NoReplacementCreated ? "No replacement Catalog was created." : "";
        CatalogRecoveryAssurance.Visibility = presentation.NoReplacementCreated ? Visibility.Visible : Visibility.Collapsed;
        CatalogRecoveryDiagnostics.Text = presentation.Diagnostic;
        CatalogRecoveryDetails.IsExpanded = false;
        CatalogRecoveryDetails.Visibility = presentation.ShowRecovery && presentation.Diagnostic.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        SettingsCatalogDirectory.Text = _storage.Locations.CatalogDirectory;
        MoveCatalogButton.IsEnabled = !presentation.ShowRecovery;
        SettingsMessage.Text = presentation.ShowRecovery ? "Choose a Catalog recovery action above."
            : "Preferences apply when you save. Administration actions apply immediately.";
    }
}
