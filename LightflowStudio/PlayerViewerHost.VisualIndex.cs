using System.Text.Json;

namespace LightflowStudio;

public partial class PlayerViewerHost
{
    internal VisualIndexView VisualIndexContent { get; } = new();
    private VisualIndexModel? _visualIndex;
    private TimeSpan? _visualIndexCachedDuration;
    private double _visualIndexCachedRate;
    private long _visualIndexContextGeneration;
    private long _visualIndexColorRevision;
    internal event EventHandler? VisualIndexDensityChanged;
    internal int VisualIndexCount => VisualIndexContent.Count;

    internal void InitializeVisualIndex(IPositionFrameService frames, Func<IPreviewStoreService?> previews, int count)
    {
        _visualIndex = new(frames);
        VisualIndexContent.Initialize(_visualIndex, count);
        VisualIndexContent.DensityChanged += (_, _) => { RefreshVisualIndex(); VisualIndexDensityChanged?.Invoke(this, EventArgs.Empty); };
        VisualIndexContent.IsVisibleChanged += (_, _) => RefreshVisualIndex();
        VisualIndexContent.Seek = SeekVisualIndexAsync;
        CurrentAssetChanged += async (_, _) =>
        {
            var generation = ++_visualIndexContextGeneration;
            _visualIndexCachedDuration = null; _visualIndexCachedRate = 0;
            RefreshVisualIndex();
            if (_currentAsset is not { Kind: MediaPresentationKind.Video, AssetId: Guid id }) return;
            try
            {
                var record = previews() is { } store ? await store.GetAsync(id) : null;
                var metadata = record is { MetadataState: PreviewComponentState.Current, MetadataJson: { } json }
                    ? JsonSerializer.Deserialize<DerivedMediaMetadata>(json, DerivedMetadataJson.Options) : null;
                if (generation != _visualIndexContextGeneration || _currentAsset?.AssetId != id) return;
                if (metadata?.DurationSeconds is double seconds && double.IsFinite(seconds) && seconds > 0 && seconds < TimeSpan.MaxValue.TotalSeconds)
                    _visualIndexCachedDuration = TimeSpan.FromSeconds(seconds);
                _visualIndexCachedRate = metadata?.Video?.FrameRate ?? 0;
                RefreshVisualIndex();
            }
            catch { /* Missing rebuildable metadata leaves an honest unknown-duration state. */ }
        };
    }
    internal void InvalidateVisualIndexColor(Guid assetId)
    {
        if (_currentAsset?.AssetId != assetId) return;
        ++_visualIndexColorRevision;
        RefreshVisualIndex();
    }
    private void RefreshVisualIndex()
    {
        var id = _currentAsset is { Kind: MediaPresentationKind.Video } ? _currentAsset.AssetId : null;
        _visualIndex?.SetContext(id, _service?.SourceInfo?.Duration ?? _visualIndexCachedDuration,
            _service?.SourceInfo?.FrameRate ?? _visualIndexCachedRate, VisualIndexContent.Count, VisualIndexContent.IsVisible, _visualIndexColorRevision);
        _visualIndex?.UpdatePosition(_service?.Snapshot.DisplayedTimestamp?.Position ?? TimeSpan.Zero);
    }
    internal async Task SeekVisualIndexAsync(VisualIndexCard card)
    {
        if (_currentAsset?.AssetId is not Guid id || _visualIndex?.Contains(id, card) != true ||
            _service is null || !PositionSlider.IsEnabled || card.Position >= _service.SourceInfo?.Duration) return;
        var generation = _generation;
        RestoreLiveVideoSurface();
        try { await _service.SeekAsync(card.Position); }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (generation == _generation) SetStatus($"Visual Index seek failed: {error.Message}"); }
    }
}
