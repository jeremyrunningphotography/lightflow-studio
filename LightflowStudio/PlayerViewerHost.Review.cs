using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace LightflowStudio;

public partial class PlayerViewerHost
{
    private bool _updatingReview;
    private PlayerSurfaceInput? _nativeInput;
    private PlayerSurfaceInput? _wpfInput;
    private PlayerFullscreenPresentation? _fullscreen;
    private PlayerFullscreenOverlay? _fullscreenOverlay;
    private PlayerSurfaceInput? _overlayInput;
    private bool _shownFullscreenHint;
    private Visibility _savedTransportVisibility;
    private Thickness _savedBorderThickness, _savedBorderMargin, _savedPlayerMargin;
    private CornerRadius _savedCornerRadius;
    private int _pixelWidth, _pixelHeight;
    private double? _pixelZoom;
    private double _panX, _panY;
    internal bool IsFullscreen => _fullscreen is not null;

    private void InitializeReviewControls()
    {
        _updatingReview = true;
        SpeedChoice.ItemsSource = new[] { "1/8×", "1/4×", "1/2×", "1×", "2×", "4×" };
        SpeedChoice.SelectedIndex = 3;
        CadenceChoiceBox.ItemsSource = CadenceChoice.ForSource(0);
        CadenceChoiceBox.SelectedIndex = 0;
        ZoomChoice.SelectedIndex = 0;
        _updatingReview = false;
        _wpfInput = CreateSurfaceInput(MediaSurfaceHost);
        FullscreenButton.Content = PlayerFullscreenOverlay.Icon(PlayerFullscreenOverlay.Outward);
    }

    private PlayerSurfaceInput CreateSurfaceInput(FrameworkElement surface) => new(surface,
        () => { if (_service is not null && PositionSlider.IsEnabled) PlayPause_Click(this, new RoutedEventArgs()); },
        ToggleFullscreen, PanViewport, ZoomViewport, TryHandleShortcut, TryHandleShortcutKeyUp,
        () => { if (IsFullscreen) _fullscreenOverlay?.PointerMoved(); });

    private void MediaView_Loaded(object sender, RoutedEventArgs e)
    {
        var view = _mediaView;
        // Flyleaf creates its separate HWND Window in its own Loaded handler.
        Dispatcher.BeginInvoke(() =>
        {
            if (view is null || !ReferenceEquals(view, _mediaView) || !view.IsLoaded) return;
            _nativeInput?.Dispose();
            _nativeInput = CreateSurfaceInput(view.InputSurface);
            if (IsFullscreen) AttachFullscreenOverlay();
            ApplyViewport();
        });
    }

    private void InitializeSourceReview(int width, int height, double fps)
    {
        _pixelWidth = width; _pixelHeight = height;
        _updatingReview = true;
        CadenceChoiceBox.ItemsSource = CadenceChoice.ForSource(fps);
        CadenceChoiceBox.SelectedIndex = 0;
        _updatingReview = false;
        ApplyViewport();
    }

    private void ResetReviewPresentation()
    {
        _nativeInput?.Dispose(); _nativeInput = null;
        _wpfInput?.Cancel();
        ExitFullscreen();
        _pixelWidth = _pixelHeight = 0;
        _pixelZoom = null; _panX = _panY = 0;
        _updatingReview = true;
        SpeedChoice.SelectedIndex = 3; ZoomChoice.SelectedIndex = 0;
        CadenceChoiceBox.ItemsSource = CadenceChoice.ForSource(0);
        CadenceChoiceBox.SelectedIndex = 0;
        LoopChoice.IsChecked = false;
        _updatingReview = false;
        ImageSurface.RenderTransform = Transform.Identity;
        SteppedFrameSurface.RenderTransform = Transform.Identity;
    }

    private async void ReviewOptions_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingReview || _service is not { } service || SpeedChoice.SelectedIndex < 0 ||
            CadenceChoiceBox.SelectedItem is not CadenceChoice cadence) return;
        var generation = _generation;
        try
        {
            RestoreLiveVideoSurface();
            await service.SetReviewOptionsAsync(new(PlaybackReviewOptions.Speeds[SpeedChoice.SelectedIndex], cadence.Divisor,
                cadence.Divisor == 1 ? cadence.Rate : null));
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) { if (generation == _generation) SetStatus(exception.Message); }
    }

    private void Fullscreen_Click(object sender, RoutedEventArgs e) => ToggleFullscreen();
    internal void ToggleFullscreen()
    {
        _nativeInput?.Cancel(); _wpfInput?.Cancel();
        if (IsFullscreen) { ExitFullscreen(); return; }
        if (_currentAsset is null) return;
        _savedTransportVisibility = TransportBar.Visibility;
        _savedBorderThickness = PlayerBorder.BorderThickness; _savedBorderMargin = PlayerBorder.Margin;
        _savedPlayerMargin = Margin;
        _savedCornerRadius = PlayerBorder.CornerRadius;
        PlayerHeader.Visibility = TransportBar.Visibility = Visibility.Collapsed;
        PlayerBorder.BorderThickness = PlayerBorder.Margin = Margin = new Thickness(0);
        PlayerBorder.CornerRadius = new CornerRadius(0);
        _fullscreen = new PlayerFullscreenPresentation(this);
        _fullscreenOverlay = new PlayerFullscreenOverlay(ExitFullscreen);
        AttachFullscreenOverlay();
        _fullscreenOverlay.Enter(!_shownFullscreenHint);
        _shownFullscreenHint = true;
    }
    private void AttachFullscreenOverlay()
    {
        if (_fullscreenOverlay is not { } overlay) return;
        _mediaView?.SetOverlay(null);
        MediaSurfaceHost.Children.Remove(overlay);
        _overlayInput?.Dispose(); _overlayInput = null;
        if (VideoHost.Visibility == Visibility.Visible && _mediaView?.SetOverlay(overlay) == true) _overlayInput = CreateSurfaceInput(overlay);
        else MediaSurfaceHost.Children.Add(overlay);
    }
    internal void ExitFullscreen()
    {
        var fullscreen = _fullscreen; _fullscreen = null;
        if (fullscreen is null) return;
        _overlayInput?.Dispose(); _overlayInput = null;
        _fullscreenOverlay?.Reset();
        _mediaView?.SetOverlay(null);
        if (_fullscreenOverlay is { } overlay) MediaSurfaceHost.Children.Remove(overlay);
        _fullscreenOverlay = null;
        PlayerHeader.Visibility = Visibility.Visible; TransportBar.Visibility = _savedTransportVisibility;
        PlayerBorder.BorderThickness = _savedBorderThickness; PlayerBorder.Margin = _savedBorderMargin; Margin = _savedPlayerMargin;
        PlayerBorder.CornerRadius = _savedCornerRadius;
        fullscreen?.Dispose();
    }

    private bool TryLoop(MediaPlaybackSnapshot snapshot)
    {
        if (LoopChoice.IsChecked != true || _stoppingAtOut || _service?.SourceInfo is not { } info) return false;
        var boundary = ActivePlaybackRange?.EffectiveOut ?? info.Duration;
        if (snapshot.State != MediaPlaybackState.Ended &&
            !(snapshot.State == MediaPlaybackState.Playing && snapshot.DisplayedTimestamp?.Position >= boundary)) return false;
        _ = LoopAsync();
        return true;
    }

    private async Task LoopAsync()
    {
        var service = _service;
        if (service is null || _stoppingAtOut) return;
        var generation = _generation;
        var start = ActivePlaybackRange?.In ?? TimeSpan.Zero;
        _stoppingAtOut = true;
        try
        {
            await service.PauseAsync();
            if (generation != _generation) return;
            await service.SeekAsync(start);
            if (generation != _generation || LoopChoice.IsChecked != true) return;
            _stopAtOutDuringPlayback = true;
            await service.PlayAsync();
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) { if (generation == _generation) SetStatus(exception.Message); }
        finally { if (generation == _generation) _stoppingAtOut = false; }
    }

    private void ZoomChoice_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingReview) return;
        _pixelZoom = ZoomChoice.SelectedIndex switch { 1 => 0.5, 2 => 1, 3 => 2, 4 => 4, _ => null };
        _panX = _panY = 0;
        ApplyViewport();
    }
    private void ZoomViewport(int direction)
    {
        if (_pixelWidth <= 0 || _pixelHeight <= 0) return;
        var dpi = VisualTreeHelper.GetDpi(MediaSurfaceHost);
        var current = _pixelZoom ?? Math.Min(MediaSurfaceHost.ActualWidth * dpi.DpiScaleX / _pixelWidth,
            MediaSurfaceHost.ActualHeight * dpi.DpiScaleY / _pixelHeight);
        double[] levels = [0.5, 1, 2, 4];
        var index = direction > 0 ? Array.FindIndex(levels, value => value > current + 0.001)
            : Array.FindLastIndex(levels, value => value < current - 0.001);
        if (index >= 0) ZoomChoice.SelectedIndex = index + 1;
    }
    internal void PanViewport(double x, double y)
    {
        if (_pixelZoom is null) return;
        _panX += x; _panY += y;
        ApplyViewport();
    }
    private void MediaViewport_SizeChanged(object sender, SizeChangedEventArgs e) => ApplyViewport();
    private void ApplyViewport()
    {
        if (_pixelWidth <= 0 || _pixelHeight <= 0 || MediaSurfaceHost.ActualWidth <= 0 || MediaSurfaceHost.ActualHeight <= 0) return;
        var width = MediaSurfaceHost.ActualWidth; var height = MediaSurfaceHost.ActualHeight;
        var dpi = VisualTreeHelper.GetDpi(MediaSurfaceHost);
        var fit = Math.Min(width * dpi.DpiScaleX / _pixelWidth, height * dpi.DpiScaleY / _pixelHeight);
        var relative = (_pixelZoom ?? fit) / fit;
        var imageWidth = _pixelWidth * (_pixelZoom ?? fit) / dpi.DpiScaleX;
        var imageHeight = _pixelHeight * (_pixelZoom ?? fit) / dpi.DpiScaleY;
        _panX = Math.Clamp(_panX, -Math.Max(0, (imageWidth - width) / 2), Math.Max(0, (imageWidth - width) / 2));
        _panY = Math.Clamp(_panY, -Math.Max(0, (imageHeight - height) / 2), Math.Max(0, (imageHeight - height) / 2));
        _service?.SetViewport(new(relative, _panX / width, _panY / height));
        var transform = new TransformGroup();
        transform.Children.Add(new ScaleTransform(relative, relative));
        transform.Children.Add(new TranslateTransform(_panX, _panY));
        ImageSurface.RenderTransformOrigin = SteppedFrameSurface.RenderTransformOrigin = new(0.5, 0.5);
        ImageSurface.RenderTransform = SteppedFrameSurface.RenderTransform = transform;
    }
}
