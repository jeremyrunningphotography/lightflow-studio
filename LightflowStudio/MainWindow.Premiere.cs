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
            if (_premiereClosing) return;
            if (_premiereBridge is null)
            {
                var journal = new CatalogPremiereHandoffs(() => _storage.CatalogAvailable ? _storage.CatalogSession : null);
                // Retain the profile-owned bridge on startup failure so Settings can explain and retry it.
                _premiereBridge = new PremiereBridge(journal, _storage.Locations);
                _premiereJobs = new(journal, _premiereBridge);
                _premiereJobs.Changed += () => Dispatcher.BeginInvoke(() => ApplyJobsPresentation(_exportScheduler.Jobs));
            }
            await _premiereBridge.StartAsync();
            if (_premiereClosing) { await _premiereBridge.DisposeAsync(); return; }
            await _premiereJobs!.RefreshHistoryAsync();
        }
        finally { _premiereStart.Release(); }
    }
    private async void PremiereIntegration_Click(object sender, RoutedEventArgs e) => await OpenPremiereAsync(false);
    private async void BrowserSendPremiere_Click(object sender, RoutedEventArgs e) => await OpenPremiereAsync(true);
    private async Task OpenPremiereAsync(bool send)
    {
        try
        {
            try { await EnsurePremiereAsync(); }
            catch (Exception error) when (_premiereBridge is not null)
            {
                AppendLog($"Premiere connection unavailable: {error}");
            }
            if (_premiereClosing) return;
            await PremiereSendState.NavigateAsync(send,
                PremiereSendState.Route(_premiereBridge!.Connection, _premiereBridge.HasCompletedSetup),
                () => new PremiereIntegrationWindow(_premiereBridge) { Owner = this }.ShowDialog(),
                OpenPremiereSendAsync);
        }
        catch (Exception error)
        {
            var message = $"Premiere connection problem: {error.Message}";
            if (send) BrowserStatusText.Text = message;
            else SettingsMessage.Text = message;
            NoticeDialog.Show(this, "Premiere Pro", "Premiere connection problem", message);
        }
    }
    private async Task OpenPremiereSendAsync()
    {
        if (_premiereClosing) return;
        var sources = new List<PremiereSource>();
        var savedSubclips = new Dictionary<Guid, IReadOnlyList<Subclip>>();
        foreach (var assetId in _browserGrid.SelectedAssetIdsInBrowserOrder)
        {
            var resolved = await _storage.MediaAssets.GetAsync(assetId);
            if (resolved?.PhysicalPath is not { } path) throw new InvalidOperationException("A selected Catalog source is unavailable.");
            PremiereRangeProjection? range = null;
            string? rangeIssue = null;
            if (PremiereSendPlanning.IsVideo(path) && await _storage.MediaRanges.RestoreAsync(assetId) is { } savedRange
                && !PremiereRangeProjection.TryCreate(savedRange, out range))
                rangeIssue = "Saved In/Out is too short for Premiere";
            var source = new PremiereSource(assetId, path,
                resolved.Asset.FileSizeBytes.ToString(System.Globalization.CultureInfo.InvariantCulture),
                resolved.Asset.LastWriteUtcTicks.ToString(System.Globalization.CultureInfo.InvariantCulture), range) { RangeIssue = rangeIssue };
            CatalogPremiereHandoffs.ValidateSource(source);
            sources.Add(source);
            savedSubclips[assetId] = await _storage.Subclips.ListAsync(assetId);
        }
        var subclipPlan = PremiereSendPlanning.Subclips(sources, savedSubclips);
        new PremiereSendWindow(_premiereBridge!, _premiereJobs!, sources, subclipPlan) { Owner = this }.ShowDialog();
        if (_premiereJobs!.Jobs.Any(job => job.State is JobState.Queued or JobState.Running)) OpenJobsPanel();
    }
}
