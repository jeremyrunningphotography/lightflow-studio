using System.Windows;

namespace LightflowStudio;

public partial class MainWindow
{
    private void BrowserRotateLeft_Click(object sender, RoutedEventArgs e) => _ = RotateBrowserAsync(false);
    private void BrowserRotateRight_Click(object sender, RoutedEventArgs e) => _ = RotateBrowserAsync(true);
    private async Task RotateBrowserAsync(bool right)
    {
        if (!CurrentBrowserSelectionActions().CanRotate) return;
        var ids = _browserGrid.SelectedAssetIdsInBrowserOrder.ToArray();
        try
        {
            var values = await _storage.VideoRotations.GetAsync(ids);
            if (values.Count != ids.Length) throw new InvalidOperationException("A selected video is no longer in the Catalog.");
            await _storage.VideoRotations.RotateAsync(values.ToDictionary(p => p.Key, p => p.Value.Revision), right);
        }
        catch (Exception error) { BrowserStatusText.Text = error.Message; }
    }
}
