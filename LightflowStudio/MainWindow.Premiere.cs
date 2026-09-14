using System.Windows;

namespace LightflowStudio;

public partial class MainWindow
{
    private PremiereBridge? _premiereBridge;
    private PremiereJobs? _premiereJobs;
    private IReadOnlyList<JobsWorkspaceItem> _premiereHistory = [];
    private async Task LoadPremiereHistoryAsync()
    {
        try
        {
            var journal = new CatalogPremiereHandoffs(() => _storage.CatalogAvailable ? _storage.CatalogSession : null);
            _premiereHistory = PremiereJobs.ProjectHistory(await journal.ListAsync());
            RefreshJobsWorkspace();
        }
        catch (Exception error) { AppendLog($"Premiere handoff history unavailable: {error.Message}"); }
    }
    private readonly SemaphoreSlim _premiereStart = new(1, 1);
    private async Task EnsurePremiereAsync()
    {
        await _premiereStart.WaitAsync();
        try
        {
            if (_premiereBridge is not null) return;
            var journal = new CatalogPremiereHandoffs(() => _storage.CatalogAvailable ? _storage.CatalogSession : null);
            var bridge = new PremiereBridge(journal, _storage.Locations.PremierePairingDirectory);
            await bridge.StartAsync();
            _premiereBridge = bridge;
            _premiereJobs = new(journal, bridge);
            _premiereJobs.Changed += () => Dispatcher.BeginInvoke(() => ApplyJobsPresentation(_exportScheduler.Jobs));
            await _premiereJobs.RefreshHistoryAsync();
        }
        finally { _premiereStart.Release(); }
    }
    private async void PremiereIntegration_Click(object sender, RoutedEventArgs e) => await OpenPremiereAsync(false);
    private async void BrowserSendPremiere_Click(object sender, RoutedEventArgs e) => await OpenPremiereAsync(true);
    private async Task OpenPremiereAsync(bool selection)
    {
        try
        {
            await EnsurePremiereAsync();
            if (!selection)
            {
                new PremiereIntegrationWindow(_premiereBridge!) { Owner = this }.ShowDialog();
                return;
            }
            var live = _premiereBridge!.Connection;
            var connection = live;
            if (live.State == PremiereConnectionState.Ready)
            {
                var installation = await PremiereInstallation.InspectAsync();
                connection = PremiereSendState.WithInstallation(_premiereBridge.Connection, installation);
            }
            var route = PremiereSendState.Route(connection);
            if (route != PremiereSendRoute.Send)
            {
                if (route == PremiereSendRoute.Settings || System.Windows.MessageBox.Show(this,
                    $"Premiere is not connected. {connection.Message}\n\nOpen integration Settings?",
                    "Send to Premiere Pro", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
                    new PremiereIntegrationWindow(_premiereBridge) { Owner = this }.ShowDialog();
                return;
            }
            var sources = new List<PremiereSource>();
            if (selection)
            {
                foreach (var assetId in _browserGrid.SelectedAssetIdsInBrowserOrder)
                {
                    var resolved = await _storage.MediaAssets.GetAsync(assetId);
                    if (resolved?.PhysicalPath is not { } path) throw new InvalidOperationException("A selected Catalog source is unavailable.");
                    var source = new PremiereSource(assetId, path,
                        resolved.Asset.FileSizeBytes.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        resolved.Asset.LastWriteUtcTicks.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    CatalogPremiereHandoffs.ValidateSource(source);
                    sources.Add(source);
                }
            }
            new PremiereSendWindow(_premiereBridge!, _premiereJobs!, sources) { Owner = this }.ShowDialog();
            if (_premiereJobs!.Jobs.Any(job => job.State is JobState.Queued or JobState.Running)) OpenJobsPanel();
        }
        catch (Exception error)
        {
            var message = $"Premiere connection problem: {error.Message}";
            if (selection) BrowserStatusText.Text = message;
            else SettingsMessage.Text = message;
        }
    }
}
