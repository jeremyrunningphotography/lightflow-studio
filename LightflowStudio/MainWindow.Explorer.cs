using System.Windows;
using System.Windows.Controls;

namespace LightflowStudio;

public partial class MainWindow
{
    private ExplorerTarget? _folderExplorerTarget;
    private ExplorerHandoff ExplorerHandoff => new(root => _storage.MediaRoots.GetAsync(root));

    private async Task UpdateExplorerMenuAsync(MenuItem item, ExplorerTarget? target)
    {
        // A recycled media container or a second context-menu opening must not receive stale capability results.
        var evaluation = new object();
        item.Tag = (evaluation, target);
        item.IsEnabled = false;
        var available = await ExplorerHandoff.CanOpenAsync(target);
        if (item.Tag is ValueTuple<object, ExplorerTarget?> current && ReferenceEquals(current.Item1, evaluation))
            item.IsEnabled = available;
    }

    private async void BrowserOpenContainingFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: ValueTuple<object, ExplorerTarget?> context })
            await OpenExplorerAsync(context.Item2);
    }

    private async void BrowserFolderOpenExplorer_Click(object sender, RoutedEventArgs e) =>
        await OpenExplorerAsync(_folderExplorerTarget);

    private async Task OpenExplorerAsync(ExplorerTarget? target)
    {
        try { await ExplorerHandoff.OpenAsync(target); }
        catch (Exception exception)
        {
            AppendLog($"[Explorer] Target {target}: {exception}");
            NoticeDialog.Show(this, "Windows Explorer", "Could not open Windows Explorer",
                "The folder or file may be unavailable, or Explorer could not start. Connect its location and try again. See the Activity Log for details.");
        }
    }
}
