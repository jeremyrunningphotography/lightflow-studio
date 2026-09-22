using System.Windows;

namespace LightflowStudio;

public partial class MainWindow
{
    private IPositionFrameService _visualIndexFrames = null!;
    private VisualIndexJobs _visualIndexJobs = null!;
    private readonly SemaphoreSlim _visualIndexMetadataGate = new(2);

    private void InitializeVisualIndexJobs()
    {
        _visualIndexFrames = _storage.CreatePositionFrameService();
        _visualIndexJobs = new(_visualIndexFrames, async (id, token) =>
        {
            await _visualIndexMetadataGate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (_storage.Previews is not { } previews) return new(DerivedMetadataStatus.Failed);
                using var metadata = DerivedMediaMetadataFactory.Create(_storage.MediaAssets, previews, _storage.Settings);
                return await metadata.ProbeAsync(id, cancellationToken: token).ConfigureAwait(false);
            }
            finally { _visualIndexMetadataGate.Release(); }
        });
        _visualIndexJobs.Initialize();
        _visualIndexJobs.Changed += () => Dispatcher.BeginInvoke(() => ApplyJobsPresentation(_exportScheduler.Jobs));
    }

    private void BrowserContextCreateVisualIndex_Click(object sender, RoutedEventArgs e)
    {
        if (!CurrentBrowserSelectionActions().CanCreateVisualIndex) return;
        var selected = _browserGrid.SelectedTilesInBrowserOrder.ToDictionary(tile => tile.AssetId!.Value, tile => tile.Name);
        _visualIndexJobs.Queue(new(VisualIndexJobs.Capability, _browserGrid.SelectedAssetIdsInBrowserOrder), id => selected[id]);
        OpenJobsPanel();
    }

    private void RetryVisualIndexOrExport(Guid id)
    {
        if (!_visualIndexJobs.Retry(id)) _exportScheduler.RetryNeedsAttention(id);
    }
}
