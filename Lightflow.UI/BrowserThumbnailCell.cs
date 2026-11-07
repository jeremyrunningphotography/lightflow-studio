using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using System.ComponentModel;
using LightflowStudio;
namespace Lightflow.UI;

/// <summary>A realized row owns its detached bitmap. Unload/recycling releases it immediately.</summary>
internal sealed class BrowserThumbnailCell : Border
{
    private readonly BrowserViewModel _model; private readonly Guid _id;
    private readonly Image _image = new() { Stretch = Stretch.Uniform };
    private readonly TextBlock _state = new() { FontSize = 10, TextWrapping = TextWrapping.Wrap, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
    private readonly BrowserPreviewActivity _activity;
    private Bitmap? _bitmap; private string? _path; private bool _attached;
    public static int RetainedBitmaps => Volatile.Read(ref _retained);
    private static int _retained;
    public BrowserThumbnailCell(BrowserViewModel model, Guid id, bool details=false)
    {
        _activity = new BrowserPreviewActivity(details);
        _model = model; _id = id; Width = 64; Height = 48; Background = new SolidColorBrush(Color.Parse("#05070A"));
        SizeChanged += (_, _) => _activity.MaxWidth = Math.Max(0, Bounds.Width);
        var grid = new Grid(); grid.Children.Add(_image); grid.Children.Add(_state); grid.Children.Add(_activity); Child = grid;
        Avalonia.Automation.AutomationProperties.SetAutomationId(_activity, "BrowserPreviewActivity");
        Avalonia.Automation.AutomationProperties.SetName(_activity, "Generating Preview");
        AttachedToVisualTree += (_, _) =>
        {
            _attached = true; _model.PropertyChanged += Changed; Apply();
            if (ThumbnailPerformance.Source.HasListeners())
            {
                var scope = _model.ThumbnailScopeRevision;
                TopLevel.GetTopLevel(this)?.RequestAnimationFrame(_ =>
                {
                    if (_attached && scope == _model.ThumbnailScopeRevision)
                    {
                        using var presented = ThumbnailPerformance.Measure("ui.asset.next_frame", _id);
                        presented?.SetTag("scope", scope);
                        presented?.SetTag("presentation", "realized asset at next render-frame callback; pixels not GPU-fenced");
                    }
                });
            }
        };
        DetachedFromVisualTree += (_, _) => { _attached = false; _activity.SetGenerating(false); _model.PropertyChanged -= Changed; Release(); };
    }
    private void Changed(object? sender, PropertyChangedEventArgs e) { if (e.PropertyName == nameof(BrowserViewModel.Thumbnails)) Apply(); }
    private void Release() { _image.Source = null; if (_bitmap is not null) { _bitmap.Dispose(); _bitmap = null; Interlocked.Decrement(ref _retained); } _path = null; }
    private void Apply()
    {
        if (!_attached) return;
        var value = _model.Thumbnails.GetValueOrDefault(_id);
        _activity.SetGenerating(_model.IsPreviewGenerating(_id));
        if (_path != value?.Path)
        {
            Release();
            if (value?.Path is { } path)
            {
                try
                {
                    using var timing = ThumbnailPerformance.Measure("ui.bitmap.assign", _id); using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete); _bitmap = new Bitmap(stream); Interlocked.Increment(ref _retained); _path = path; _image.Source = _bitmap;
                    if (ThumbnailPerformance.Source.HasListeners())
                    {
                        var expected = _bitmap;
                        TopLevel.GetTopLevel(this)?.RequestAnimationFrame(_ =>
                        {
                            if (_attached && ReferenceEquals(_bitmap, expected))
                            {
                                using var presented = ThumbnailPerformance.Measure("ui.present.next_frame", _id);
                                presented?.SetTag("presentation", "next render-frame callback");
                            }
                        });
                    }
                }
                catch (Exception error) when (error is IOException or ArgumentException or NotSupportedException) { _state.Text = "Preview unavailable"; }
            }
        }
        _image.RenderTransformOrigin = new RelativePoint(.5, .5, RelativeUnit.Relative);
        _image.RenderTransform = new RotateTransform(value?.RotationDegrees ?? 0);
        var cached = value?.State == "Cached";
        _state.Text = value?.Path is not null && _bitmap is null ? "Preview unavailable" : cached ? "Loading" : value?.State ?? "Loading";
        _state.IsVisible = _bitmap is null || value?.State is not ("Ready" or "Cached");
        _state.Background = new SolidColorBrush(Color.FromArgb(200, 5, 7, 10));
        ToolTip.SetTip(this, cached ? "Preview" : value?.Diagnostic ?? value?.State ?? "Loading thumbnail");
        Avalonia.Automation.AutomationProperties.SetName(this, cached ? "Preview" : value?.State ?? "Loading thumbnail");
    }
}
