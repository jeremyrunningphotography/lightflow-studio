using System.Windows;
using System.Windows.Controls;

namespace LightflowStudio;

public partial class PlayerViewerHost
{
    private IAssetVideoRotationStore? _rotations;
    private VideoRotation _videoRotation;
    private long _rotationRevision;
    private void InitializeRotation(IAssetVideoRotationStore? store)
    {
        _rotations = store;
        if (store is null) return;
        OrientedPreviewImage.SetStore(this, store);
        Loaded += (_, _) =>
        {
            store.Changed -= RotationChanged; store.Changed += RotationChanged;
            store.Invalidated -= RotationInvalidated; store.Invalidated += RotationInvalidated;
        };
        Unloaded += (_, _) => { store.Changed -= RotationChanged; store.Invalidated -= RotationInvalidated; };
        InstallRotationMenu(MediaSurfaceHost);
    }

    private void InstallRotationMenu(FrameworkElement surface)
    {
        var menu = new ContextMenu { Style = (Style)FindResource("LightflowContextMenuStyle") };
        foreach (var right in new[] { false, true })
        {
            var item = new MenuItem { Header = right ? "Rotate Right" : "Rotate Left",
                Style = (Style)FindResource("LightflowMenuItemStyle") };
            item.Click += async (_, _) => await RotateCurrentAsync(right);
            menu.Items.Add(item);
        }
        surface.ContextMenu = menu;
        menu.Opened += (_, _) =>
        {
            foreach (MenuItem item in menu.Items)
                item.IsEnabled = _rotations is not null && _currentAsset is { AssetId: not null, Kind: MediaPresentationKind.Video };
        };
    }

    private async Task RestoreRotationAsync(long generation, CancellationToken token)
    {
        _videoRotation = default;
        _rotationRevision = -1;
        if (_rotations is null || _currentAsset?.AssetId is not Guid id) return;
        var values = await _rotations.GetAsync([id], token);
        if (generation != _generation || token.IsCancellationRequested) return;
        ApplyRotation(values.GetValueOrDefault(id) ?? new(id, default));
    }

    private void RotationChanged(object? sender, IReadOnlyList<AssetVideoRotation> values)
    {
        var generation = _generation;
        Dispatcher.BeginInvoke(() =>
        {
            if (generation != _generation) return;
            foreach (var value in values)
                if (_currentAsset?.AssetId == value.AssetId && _currentAsset.Kind == MediaPresentationKind.Video)
                    ApplyRotation(value);
        });
    }

    private void ApplyRotation(AssetVideoRotation value)
    {
        if (value.Revision < _rotationRevision) return;
        _rotationRevision = value.Revision;
        _videoRotation = value.Rotation;
        if (_service?.SourceInfo is not { } info) return;
        _service.SetVideoRotation(value.Rotation);
        var size = info.SourceRotation.Compose(value.Rotation).Dimensions(info.Width, info.Height);
        _pixelWidth = size.Width; _pixelHeight = size.Height;
        // Retained reverse-step pixels have the previous orientation. The native retained frame remains
        // at the same decoded timestamp and can be redrawn without a seek, pause, or second decoder.
        if (_retainedSteppedFrame is not null) RestoreLiveVideoSurface();
        ApplyViewport();
    }

    private void RotationInvalidated(object? sender, EventArgs args) => Dispatcher.BeginInvoke(async () =>
    {
        if (_currentAsset?.Kind != MediaPresentationKind.Video) return;
        var generation = _generation;
        try { await RestoreRotationAsync(generation, CancellationToken.None); }
        catch (Exception error) { if (generation == _generation) SetStatus(error.Message); }
    });

    private async Task RotateCurrentAsync(bool right)
    {
        await CatalogMutations.RunAsync(async () => {
        if (_rotations is null || _currentAsset?.AssetId is not Guid id || _currentAsset.Kind != MediaPresentationKind.Video) return;
        try
        {
            var values = await _rotations.GetAsync([id]);
            if (_currentAsset?.AssetId != id) return;
            if (!values.TryGetValue(id, out var value)) throw new InvalidOperationException("The video is no longer in the Catalog.");
            await _rotations.RotateAsync(new Dictionary<Guid, long> { [id] = value.Revision }, right);
        }
        catch (Exception error) { if (_currentAsset?.AssetId == id) SetStatus(error.Message); }
        });
    }
}
