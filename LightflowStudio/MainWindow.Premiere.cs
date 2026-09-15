using System.Windows;

namespace LightflowStudio;

public partial class MainWindow
{
    private bool _premiereClosing;
    private async Task ResumePremiereAsync()
    {
        if (!System.IO.File.Exists(System.IO.Path.Combine(_storage.Locations.PremierePairingDirectory, "lightflow-pairing.json"))) return;
        try { await EnsurePremiereAsync(); }
        catch (Exception error) { AppendLog($"Premiere automatic connection unavailable: {error.Message}"); }
    }
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
            if (_premiereBridge is not null || _premiereClosing) return;
            var journal = new CatalogPremiereHandoffs(() => _storage.CatalogAvailable ? _storage.CatalogSession : null);
            var bridge = new PremiereBridge(journal, _storage.Locations.PremierePairingDirectory);
            await bridge.StartAsync();
            if (_premiereClosing) { await bridge.DisposeAsync(); return; }
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
                if (route == PremiereSendRoute.Settings || NoticeDialog.OfferAction(this,
                    "Send to Premiere Pro", "Premiere is not connected",
                    $"{connection.Message}\n\nOpen Integration Settings for setup and connection help.", "Open Integration Settings"))
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
                    PremiereRangeProjection? range = null;
                    string? rangeIssue = null;
                    if (PremiereSendPlanning.IsVideo(path) && await _storage.MediaRanges.RestoreAsync(assetId) is { } savedRange
                        && !PremiereRangeProjection.TryCreate(savedRange, out range))
                        rangeIssue = "Saved In/Out cannot be transferred exactly";
                    var source = new PremiereSource(assetId, path,
                        resolved.Asset.FileSizeBytes.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        resolved.Asset.LastWriteUtcTicks.ToString(System.Globalization.CultureInfo.InvariantCulture), range) { RangeIssue = rangeIssue };
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
