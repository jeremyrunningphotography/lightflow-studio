using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace LightflowStudio;

public partial class PlayerViewerHost
{
    private readonly IMarkerService? _markers;
    private IReadOnlyList<TimelineMarker> _markerItems = [];
    private Guid? _selectedMarkerId;
    internal Guid? SelectedMarkerId => _selectedMarkerId;
    internal IReadOnlyList<TimelineMarker> CurrentMarkers => _markerItems;
    private bool _markerBusy;
    internal event EventHandler<Guid>? MarkersChanged;

    private void ResetMarkers()
    {
        _markerItems = [];
        _selectedMarkerId = null;
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
            _selectedMarkerId = markers.FirstOrDefault(m => m.MarkerId == select)?.MarkerId;
            UpdateMarkerPresentation();
        }
        catch (Exception error) { if (generation == _generation) SetStatus($"Markers unavailable: {error.Message}"); }
    }

    private void UpdateMarkerPresentation()
    {
        if (MarkerTrack is null) return;
        MarkerTransport.Visibility = _markers is not null && _currentAsset is { Kind: MediaPresentationKind.Video, AssetId: not null }
            ? Visibility.Visible : Visibility.Collapsed;
        MarkerTransport.IsEnabled = !_markerBusy;
        var canSeek = _service is not null && PositionSlider.IsEnabled;
        AddMarkerButton.IsEnabled = canSeek;
        PreviousMarkerButton.IsEnabled = NextMarkerButton.IsEnabled = canSeek && _markerItems.Count > 0;
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
            button.ContextMenu = MarkerMenus.Create(button, () => PromptRenameMarker(marker), () => ClearMarkerAsync(marker));
            Canvas.SetLeft(button, MarkerNavigation.Fraction(marker.Position, duration) * MarkerTrack.ActualWidth - 7);
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
            MarkersChanged?.Invoke(this, assetId);
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
    internal Task RenameMarkerAsync(TimelineMarker marker, string name) =>
        marker.AssetId != _currentAsset?.AssetId ? Task.CompletedTask : MarkerMutationAsync(async _ => { await _markers!.RenameAsync(marker.MarkerId, marker.Revision, name); return marker.MarkerId; });
    internal Task ClearMarkerAsync(TimelineMarker marker) =>
        marker.AssetId != _currentAsset?.AssetId ? Task.CompletedTask : MarkerMutationAsync(async _ => { await _markers!.DeleteAsync(marker.MarkerId, marker.Revision); return null; });
    private async void PromptRenameMarker(TimelineMarker marker)
    {
        if (marker.AssetId != _currentAsset?.AssetId) return;
        var dialog = new TextEntryDialog("Rename Marker", "Marker name (optional)", marker.Name, allowEmpty: true)
            { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() == true && marker.AssetId == _currentAsset?.AssetId) await RenameMarkerAsync(marker, dialog.Value);
    }
    internal Task ReloadMarkersAsync() => _currentAsset?.AssetId is { } id
        ? LoadMarkersAsync(id, _generation, _selectedMarkerId) : Task.CompletedTask;
    internal ContextMenu BuildTimelineMenu()
    {
        var menu = MarkerMenus.Empty(this);
        menu.Items.Add(MarkerMenus.Item(this, "Add Marker", () => AddMarker_Click(this, new RoutedEventArgs()), AddMarkerButton.IsEnabled));
        var go = MarkerMenus.Item(this, "Go to Marker", () => { }, _markerItems.Count > 0 && PositionSlider.IsEnabled);
        foreach (var marker in _markerItems)
            go.Items.Add(MarkerMenus.Item(this, MarkerMenus.Label(marker), async () => await SeekMarkerAsync(marker)));
        menu.Items.Add(go);
        menu.Items.Add(MarkerMenus.Item(this, "Go to In", async () => await SeekToBoundaryAsync(PresentedRange?.In), PresentedRange?.In is not null && PositionSlider.IsEnabled));
        menu.Items.Add(MarkerMenus.Item(this, "Go to Out", async () => await SeekToBoundaryAsync(PresentedRange?.Out), PresentedRange?.Out is not null && PositionSlider.IsEnabled));
        return menu;
    }
    private void Timeline_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        // A diamond owns its two-action menu; every other timeline descendant, including the Thumb, shares this one.
        if (e.OriginalSource is DependencyObject origin)
            for (var current = origin; current is not null && current != TimelineSurface; current = current is Visual ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current))
                if (current is FrameworkElement { Tag: TimelineMarker }) return;
        TimelineSurface.ContextMenu = BuildTimelineMenu();
    }
    private async void PreviousMarker_Click(object sender, RoutedEventArgs e) =>
        await SeekMarkerAsync(MarkerNavigation.Previous(_markerItems, _service?.Snapshot.DisplayedTimestamp?.Position ?? TimeSpan.Zero));
    private async void NextMarker_Click(object sender, RoutedEventArgs e) =>
        await SeekMarkerAsync(MarkerNavigation.Next(_markerItems, _service?.Snapshot.DisplayedTimestamp?.Position ?? TimeSpan.Zero));
    internal async Task SeekMarkerAsync(TimelineMarker? marker)
    {
        if (marker is null || marker.AssetId != _currentAsset?.AssetId) return;
        marker = _markerItems.FirstOrDefault(m => m.MarkerId == marker.MarkerId);
        if (marker is null) return;
        _selectedMarkerId = marker.MarkerId;
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
