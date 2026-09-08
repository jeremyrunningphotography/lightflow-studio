using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;

namespace LightflowStudio;

public partial class MainWindow
{
    private MediaInspectorView? _inspector;
    private bool _rightPanelOpen;
    private double _rightPanelPreferredWidth = 360;
    private readonly DispatcherTimer _inspectorRefreshTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };

    private void InitializeRightPanel()
    {
        _inspector = new MediaInspectorView();
        _inspector.Initialize(() => new MediaInspectorService(_storage.Previews, _storage.AssetClassifications,
            _storage.Locations.PreviewsDirectory));
        _inspector.OpenPlayerRequested += (_, _) =>
        {
            if (_browserPresentation != BrowserPresentationMode.Grid) return;
            var selected = _browserGrid.SelectedTilesInBrowserOrder;
            if (selected.Count == 1) _ = OpenBrowserPlayerViewerAsync(selected[0]);
        };
        HomeRightPanel.AddSurface("inspector", "Inspector", _inspector);
        _inspector.OpenFolder = OpenInspectorFolderAsync;
        HomeRightPanel.ActiveSurfaceChanged += (_, _) => ScheduleRightPanelSave();
        _inspectorRefreshTimer.Tick += (_, _) =>
        {
            _inspectorRefreshTimer.Stop();
            UpdateInspectorContext(force: true);
        };
        var layout = _workspaceState.Current.Layout;
        _rightPanelPreferredWidth = layout?.RightPanelWidth ?? 360;
        HomeRightPanel.SelectSurface(layout?.RightPanelActiveSurface);
        SetRightPanelOpen(layout?.RightPanelOpen == true);
    }

    private void UpdateInspectorContext(bool force = false)
    {
        if (_inspector is null || !_rightPanelOpen) return;
        var player = _browserPresentation == BrowserPresentationMode.PlayerViewer;
        IReadOnlyList<InspectorAsset> context = player
            ? _playerViewerHost?.CurrentAsset is { } asset
                ? [new(asset.AssetId, asset.Name, asset.RelativePath, asset.Kind)] : []
            : _browserGrid.SelectedTilesInBrowserOrder.Select(tile => new InspectorAsset(tile.AssetId, tile.Name,
                tile.RelativePath, MediaPresentationClassification.KindFor(tile.Category), tile.FileSizeBytes)).ToArray();
        _inspector.SetContext(context, player, force);
    }

    private void InvalidateInspector()
    {
        if (!_rightPanelOpen || _inspectorRefreshTimer.IsEnabled) return;
        _inspectorRefreshTimer.Start();
    }

    private async Task OpenInspectorFolderAsync()
    {
        // Resolve at action time through the same root mapping as Browser/Player; never persist an absolute identity.
        Guid rootId;
        string relativePath;
        if (_browserPresentation == BrowserPresentationMode.PlayerViewer && _playerViewerHost?.CurrentAsset is { } asset)
        { rootId = asset.RootId; relativePath = asset.RelativePath; }
        else if (_browserGrid.SelectedTilesInBrowserOrder is { Count: 1 } selected)
        { rootId = selected[0].RootId; relativePath = selected[0].RelativePath; }
        else return;
        var resolved = await _storage.MediaRoots.ResolveAsync(rootId, relativePath);
        var start = InspectorFolderStartInfo(resolved);
        if (!await Task.Run(() => System.IO.Directory.Exists(start.ArgumentList[0])))
            throw new System.IO.DirectoryNotFoundException("The containing folder is unavailable.");
        System.Diagnostics.Process.Start(start);
    }

    internal static System.Diagnostics.ProcessStartInfo InspectorFolderStartInfo(MediaPathResolution resolved)
    {
        if (resolved.RootAvailability != MediaRootAvailability.Online || resolved.PhysicalPath is null)
            throw new System.IO.DirectoryNotFoundException("The containing folder is unavailable. Connect the media root and try again.");
        var folder = System.IO.Path.GetDirectoryName(resolved.PhysicalPath)
            ?? throw new System.IO.DirectoryNotFoundException("The containing folder is unavailable.");
        var start = new System.Diagnostics.ProcessStartInfo("explorer.exe") { UseShellExecute = true };
        start.ArgumentList.Add(folder);
        return start;
    }

    private void RightPanelToggle_Click(object sender, RoutedEventArgs e) => SetRightPanelOpen(RightPanelToggle.IsChecked == true);
    private void SetRightPanelOpen(bool open)
    {
        _rightPanelOpen = open;
        RightPanelToggle.IsChecked = open;
        HomeRightPanel.Visibility = RightPanelSplitter.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        ApplyBrowserResponsiveLayout();
        UpdateInspectorContext();
        ScheduleRightPanelSave();
    }

    private void ApplyRightPanelLayout()
    {
        var maximumWidth = Math.Min(WorkspaceState.MaxRightPanelWidth,
            Math.Max(220, BrowserWorkspaceRoot.ActualWidth - 140 - 220 - 16));
        var width = _rightPanelOpen ? Math.Min(_rightPanelPreferredWidth, maximumWidth) : 0;
        RightPanelColumn.MinWidth = _rightPanelOpen ? Math.Min(WorkspaceState.MinRightPanelWidth, width) : 0;
        RightPanelColumn.MaxWidth = _rightPanelOpen ? maximumWidth : 0;
        RightPanelColumn.Width = new GridLength(width);
        RightPanelSplitterColumn.Width = new GridLength(_rightPanelOpen ? 8 : 0);
    }

    private void RightPanelSplitter_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        // GridSplitter commits Width before the next layout pass updates ActualWidth.
        _rightPanelPreferredWidth = Math.Clamp(RightPanelColumn.Width.Value,
            WorkspaceState.MinRightPanelWidth, WorkspaceState.MaxRightPanelWidth);
        ApplyBrowserResponsiveLayout();
        ScheduleRightPanelSave();
    }

    private void RightPanelSplitter_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key is not (Key.Left or Key.Right)) return;
        _rightPanelPreferredWidth = Math.Clamp(_rightPanelPreferredWidth + (e.Key == Key.Left ? 16 : -16),
            WorkspaceState.MinRightPanelWidth, WorkspaceState.MaxRightPanelWidth);
        ApplyBrowserResponsiveLayout();
        ScheduleRightPanelSave();
        e.Handled = true;
    }

    private void ScheduleRightPanelSave()
    {
        _workspaceSaveTimer.Stop();
        _workspaceSaveTimer.Start();
    }
}
