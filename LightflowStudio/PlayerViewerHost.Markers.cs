using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace LightflowStudio;

public partial class PlayerViewerHost
{
    private readonly IMarkerService? _markers;
    private IReadOnlyList<TimelineMarker> _markerItems = [];
    private bool _loadingMarkers;
    private bool _markerBusy;
    internal event EventHandler? MarkersChanged;

    private void ResetMarkers()
    {
        _markerItems = [];
        _loadingMarkers = true;
        MarkerChoice.ItemsSource = null;
        MarkerName.Text = "";
        _loadingMarkers = false;
        UpdateMarkerPresentation();
    }

    private async Task LoadMarkersAsync(Guid assetId, long generation, Guid? select = null)
    {
        if (_markers is null) return;
        try
        {
            var markers = await _markers.ListAsync(assetId);
            if (generation != _generation || _currentAsset?.AssetId != assetId) return;
            _markerItems = markers;
            _loadingMarkers = true;
            MarkerChoice.ItemsSource = markers;
            MarkerChoice.SelectedItem = markers.FirstOrDefault(m => m.MarkerId == select);
            MarkerName.Text = (MarkerChoice.SelectedItem as TimelineMarker)?.Name ?? "";
            _loadingMarkers = false;
            UpdateMarkerPresentation();
        }
        catch (Exception error) { if (generation == _generation) SetStatus($"Markers unavailable: {error.Message}"); }
    }

    private void UpdateMarkerPresentation()
    {
        if (MarkerTrack is null) return;
        MarkerControls.Visibility = _markers is not null && _currentAsset is { Kind: MediaPresentationKind.Video, AssetId: not null }
            ? Visibility.Visible : Visibility.Collapsed;
        MarkerControls.IsEnabled = !_markerBusy;
        var canSeek = _service is not null && PositionSlider.IsEnabled;
        AddMarkerButton.IsEnabled = canSeek;
        PreviousMarkerButton.IsEnabled = NextMarkerButton.IsEnabled = canSeek && _markerItems.Count > 0;
        RenameMarkerButton.IsEnabled = MarkerChoice.SelectedItem is TimelineMarker;
        RemoveMarkerButton.IsEnabled = _markerItems.Count > 0;
        MarkerTrack.Children.Clear();
        var duration = _service?.SourceInfo?.Duration ?? TimeSpan.Zero;
        foreach (var marker in _markerItems)
        {
            var button = new System.Windows.Controls.Button
            {
                Content = "◆", Width = 14, Height = 14, Padding = new Thickness(0), Margin = new Thickness(0),
                MinWidth = 0, MinHeight = 0, Background = System.Windows.Media.Brushes.Transparent, BorderThickness = new Thickness(0),
                ToolTip = $"{marker.DisplayName} · {marker.PositionLabel}", Tag = marker,
                IsEnabled = _service is not null && PositionSlider.IsEnabled
            };
            button.SetResourceReference(ForegroundProperty, "OrangeBrush");
            System.Windows.Automation.AutomationProperties.SetName(button, $"Seek to {marker.DisplayName} at {marker.PositionLabel}");
            button.Click += async (_, _) => await SeekMarkerAsync(marker);
            Canvas.SetLeft(button, MarkerNavigation.Fraction(marker.Position, duration) * Math.Max(0, MarkerTrack.ActualWidth - 14));
            MarkerTrack.Children.Add(button);
        }
    }

    private async Task MarkerMutationAsync(Func<Guid, Task<Guid?>> mutation)
    {
        if (_markerBusy || _markers is null || _currentAsset?.AssetId is not { } assetId) return;
        var generation = _generation;
        _markerBusy = true;
        UpdateMarkerPresentation();
        try
        {
            var selected = await mutation(assetId);
            await LoadMarkersAsync(assetId, generation, selected);
            MarkersChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception error)
        {
            if (generation == _generation)
            {
                SetStatus($"Marker change failed: {error.Message}");
                await LoadMarkersAsync(assetId, generation);
            }
        }
        finally { _markerBusy = false; UpdateMarkerPresentation(); }
    }

    private async void AddMarker_Click(object sender, RoutedEventArgs e)
    {
        var timestamp = _retainedSteppedFrame?.Timestamp ?? _service?.Snapshot.DisplayedTimestamp;
        if (timestamp is not { IsDecodedPresentationTimestamp: true } || !PositionSlider.IsEnabled) return;
        await MarkerMutationAsync(async asset => (await _markers!.CreateAsync(asset, timestamp.Position)).Marker.MarkerId);
    }
    private async void RenameMarker_Click(object sender, RoutedEventArgs e)
    {
        if (MarkerChoice.SelectedItem is not TimelineMarker marker) return;
        var name = MarkerName.Text;
        await MarkerMutationAsync(async _ => { await _markers!.RenameAsync(marker.MarkerId, marker.Revision, name); return marker.MarkerId; });
    }
    private async void RemoveMarker_Click(object sender, RoutedEventArgs e)
    {
        var marker = MarkerChoice.SelectedItem as TimelineMarker ?? _markerItems.FirstOrDefault(m => m.Position == _service?.Snapshot.DisplayedTimestamp?.Position);
        if (marker is null) return;
        await MarkerMutationAsync(async _ => { await _markers!.DeleteAsync(marker.MarkerId, marker.Revision); return null; });
    }
    private async void PreviousMarker_Click(object sender, RoutedEventArgs e) =>
        await SeekMarkerAsync(MarkerNavigation.Previous(_markerItems, _service?.Snapshot.DisplayedTimestamp?.Position ?? TimeSpan.Zero));
    private async void NextMarker_Click(object sender, RoutedEventArgs e) =>
        await SeekMarkerAsync(MarkerNavigation.Next(_markerItems, _service?.Snapshot.DisplayedTimestamp?.Position ?? TimeSpan.Zero));
    private async void MarkerChoice_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loadingMarkers) await SeekMarkerAsync(MarkerChoice.SelectedItem as TimelineMarker);
    }
    internal async Task SeekMarkerAsync(TimelineMarker? marker)
    {
        if (marker is null || marker.AssetId != _currentAsset?.AssetId) return;
        marker = _markerItems.FirstOrDefault(m => m.MarkerId == marker.MarkerId);
        if (marker is null) return;
        _loadingMarkers = true;
        MarkerChoice.SelectedItem = _markerItems.FirstOrDefault(m => m.MarkerId == marker.MarkerId);
        MarkerName.Text = marker.Name;
        _loadingMarkers = false;
        UpdateMarkerPresentation();
        if (_service is null || !PositionSlider.IsEnabled) return;
        if (_service.SourceInfo is { } source && marker.Position > source.Duration)
        {
            SetStatus("This marker is beyond the available source duration.");
            return;
        }
        var generation = _generation;
        RestoreLiveVideoSurface();
        try { await _service.SeekAsync(marker.Position); }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (generation == _generation) SetStatus($"Marker seek failed: {error.Message}"); }
    }
    private void MarkerTrack_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateMarkerPresentation();
}
