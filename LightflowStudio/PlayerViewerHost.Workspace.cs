namespace LightflowStudio;

public partial class PlayerViewerHost
{
    private bool _applyingWorkspaceReview;

    internal WorkspacePlayerState? CaptureWorkspaceState() => _currentAsset?.AssetId is null ? null : new()
    {
        Asset = _currentAsset,
        Position = _retainedSteppedFrame?.Timestamp.Position ?? _service?.Snapshot.DisplayedTimestamp?.Position ?? TimeSpan.Zero,
        PixelZoom = _pixelZoom, PanX = _panX, PanY = _panY,
        Loop = LoopChoice.IsChecked == true,
        Speed = PlaybackReviewOptions.Speeds[Math.Clamp(SpeedChoice.SelectedIndex, 0, PlaybackReviewOptions.Speeds.Length - 1)],
        Cadence = (CadenceChoiceBox.SelectedItem as CadenceChoice)?.Rate,
        Volume = _service?.Volume ?? 100, Muted = _service?.Mute ?? false,
        ActiveSubclipId = ActiveSubclipId, SelectedSubclipIds = SelectedSubclipIds.ToArray()
    };

    private void ApplyWorkspaceReview(WorkspacePlayerState state)
    {
        _updatingReview = _applyingWorkspaceReview = true;
        try
        {
            _pixelZoom = state.PixelZoom; _panX = state.PanX; _panY = state.PanY;
            ZoomChoice.SelectedIndex = state.PixelZoom switch { 0.5 => 1, 1 => 2, 2 => 3, 4 => 4, _ => 0 };
            LoopChoice.IsChecked = state.Loop;
            SpeedChoice.SelectedIndex = Array.IndexOf(PlaybackReviewOptions.Speeds, state.Speed);
            var choices = CadenceChoiceBox.Items.Cast<CadenceChoice>().ToArray();
            CadenceChoiceBox.SelectedItem = choices.FirstOrDefault(choice => choice.Rate == state.Cadence) ?? choices[0];
            if (_service is { } service) { service.Volume = (int)state.Volume; service.Mute = state.Muted; }
            SubclipsList.SelectedItems.Clear();
            foreach (var item in _subclipItems.Where(item => state.SelectedSubclipIds.Contains(item.SubclipId)))
                SubclipsList.SelectedItems.Add(item);
            if (_subclipItems.FirstOrDefault(item => item.SubclipId == state.ActiveSubclipId) is { } active)
                SetActiveSubclipReview(active);
        }
        finally { _updatingReview = _applyingWorkspaceReview = false; }
        ApplyViewport();
    }
}
