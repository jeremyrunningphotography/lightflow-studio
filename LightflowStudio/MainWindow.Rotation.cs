using System.Windows;

namespace LightflowStudio;

public partial class MainWindow
{
    private long _browserRotationGeneration;
    private void BrowserVideoRotationsChanged(object? sender, IReadOnlyList<AssetVideoRotation> values) =>
        Dispatcher.BeginInvoke(() =>
        {
            _browserGrid.ApplyVideoRotations(values);
            _browserGrid.ReapplyQuery();
            UpdateBrowserStatusText();
        });

    private void BrowserVideoRotationsInvalidated(object? sender, EventArgs args) =>
        Dispatcher.BeginInvoke(() =>
        {
            _browserRotationGeneration++;
            _browserGrid.InvalidateVideoRotations();
            _browserGrid.ReapplyQuery();
            _ = ReloadBrowserRotationsAsync();
        });
    private async Task ReloadBrowserRotationsAsync()
    {
        var generation = _browserUiGeneration;
        var rotationGeneration = _browserRotationGeneration;
        try
        {
            var ids = _browserGrid.ScopeAssetIds;
            var rotations = await _storage.VideoRotations.GetAsync(ids);
            if (generation != _browserUiGeneration || rotationGeneration != _browserRotationGeneration) return;
            _browserGrid.ApplyVideoRotations(rotations.Values);
            _browserGrid.ReapplyQuery();
            UpdateBrowserStatusText();
        }
        catch (InvalidOperationException) { }
    }

    private void BrowserRotateLeft_Click(object sender, RoutedEventArgs e) => _ = RotateBrowserAsync(false);
    private void BrowserRotateRight_Click(object sender, RoutedEventArgs e) => _ = RotateBrowserAsync(true);
    private async Task RotateBrowserAsync(bool right)
    {
        await _storage.Mutations.RunAsync(async () => {
        if (!CurrentBrowserSelectionActions().CanRotate) return;
        var ids = _browserGrid.SelectedAssetIdsInBrowserOrder.ToArray();
        try
        {
            var values = await _storage.VideoRotations.GetAsync(ids);
            if (values.Count != ids.Length) throw new InvalidOperationException("A selected video is no longer in the Catalog.");
            await _storage.VideoRotations.RotateAsync(values.ToDictionary(p => p.Key, p => p.Value.Revision), right);
        }
        catch (Exception error) { BrowserStatusText.Text = error.Message; }
        });
    }
}
