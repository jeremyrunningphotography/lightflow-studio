using System.ComponentModel;
using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace LightflowStudio;

public partial class CatalogSetupDialog : Window
{
    private readonly CatalogStartupGate _gate;
    private readonly LightflowStorageLocations _profile;
    private CancellationTokenSource? _operation;
    private bool _exitRequested;
    private bool _accepted;
    internal CatalogSetupDialog(CatalogStartupGate gate, LightflowStorageLocations profile)
    {
        InitializeComponent(); _gate = gate; _profile = profile; RefreshPresentation();
        SourceInitialized += (_, _) => WindowAppearance.EnableDarkTitleBar(this);
        Closing += OnGateClosing;
    }
    internal void RefreshPresentation()
    {
        var state = _gate.Current;
        var presentation = CatalogRecoveryPresentation.Create(state.Status, state.RecoveryReason, state.Diagnostic);
        GateHeading.Text = presentation.Heading; GateDescription.Text = presentation.Description;
        NewLocation.Text = _gate.DisplayCatalogDirectory;
        GateAssurance.Visibility = presentation.NoReplacementCreated ? Visibility.Visible : Visibility.Collapsed;
        GateDiagnostic.Text = presentation.Diagnostic;
        GateDetails.IsExpanded = false;
        GateDetails.Visibility = presentation.Diagnostic.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        GateRetryButton.Visibility = _gate.CanRetry ? Visibility.Visible : Visibility.Collapsed;
        GateRetryButton.IsDefault = _gate.CanRetry;
        GateCreateButton.IsDefault = !_gate.CanRetry;
        GateRetryButton.Style = (Style)FindResource("PrimaryButton");
        GateCreateButton.Style = (Style)FindResource(_gate.CanRetry ? typeof(System.Windows.Controls.Button) : "PrimaryButton");
    }
    private void OnGateClosing(object? sender, CancelEventArgs e)
    {
        if (!_accepted) _gate.CloseAdmission();
        if (_operation is null) return;
        _exitRequested = true; _operation.Cancel(); e.Cancel = true;
    }
    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
    private async void Retry_Click(object sender, RoutedEventArgs e) => await RunAsync(token => _gate.RetryAsync(token));
    private async void Create_Click(object sender, RoutedEventArgs e)
    {
        var proposal = _gate.ProposedNewDirectory;
        var picker = new OpenFolderDialog { Title = "Choose a location for your new Catalog", InitialDirectory = Path.GetDirectoryName(proposal) ?? _profile.ApplicationDataDirectory };
        if (picker.ShowDialog(this) != true) return;
        var directory = string.Equals(picker.FolderName, Path.GetDirectoryName(proposal), StringComparison.OrdinalIgnoreCase)
            ? proposal : Path.Combine(picker.FolderName, "Lightflow Catalog");
        if (string.IsNullOrEmpty(directory)) return;
        if (!ConfirmationDialog.Confirm(this, "Create New Catalog", "Create a new Catalog here?",
            "Lightflow Studio will create a new independent Catalog. Your previous Catalog and its files will be preserved. The new folder must be unused.", directory, "Create New Catalog")) return;
        await RunAsync(token => _gate.CreateAsync(directory, token));
    }
    private async void Open_Click(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFolderDialog { Title = "Open Existing Catalog" };
        if (picker.ShowDialog(this) != true) return;
        await RunAsync(token => _gate.OpenAsync(picker.FolderName, token, selected =>
            ConfirmationDialog.Confirm(this, "Open Existing Catalog", "Open this Catalog?",
                "Lightflow Studio will preserve its identity and authored data. Media Roots and LUT folders may need configuration on this machine.",
                selected.Directory + "\nCatalog " + selected.CatalogId, "Open Catalog")));
    }
    internal async Task RunAsync(Func<CancellationToken, Task> operation)
    {
        if (_operation is not null) return;
        _operation = new();
        GateRetryButton.IsEnabled = GateCreateButton.IsEnabled = GateOpenButton.IsEnabled = false;
        GateProgress.Visibility = Visibility.Visible;
        try { await operation(_operation.Token); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or OperationCanceledException)
        { _gate.ReportFailure(error.Message); }
        finally
        {
            _operation.Dispose(); _operation = null;
            GateProgress.Visibility = Visibility.Collapsed;
            GateRetryButton.IsEnabled = GateCreateButton.IsEnabled = GateOpenButton.IsEnabled = true;
        }
        if (_exitRequested) { Close(); return; }
        if (_gate.CanEnterWorkspace) { _accepted = true; DialogResult = true; return; }
        RefreshPresentation();
    }
}
