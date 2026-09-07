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
        HomeRightPanel.CloseRequested += (_, _) => { SetRightPanelOpen(false); RightPanelToggle.Focus(); };
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
