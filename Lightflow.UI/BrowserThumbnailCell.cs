using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using System.ComponentModel;
namespace Lightflow.UI;

/// <summary>A realized row owns its detached bitmap. Unload/recycling releases it immediately.</summary>
internal sealed class BrowserThumbnailCell : Border
{
    private readonly BrowserViewModel _model; private readonly Guid _id;
    private readonly Image _image = new() { Stretch = Stretch.Uniform };
    private readonly TextBlock _state = new() { FontSize = 10, TextWrapping = TextWrapping.Wrap, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
    private Bitmap? _bitmap; private string? _path; private bool _attached;
    public static int RetainedBitmaps => Volatile.Read(ref _retained);
    private static int _retained;
    public BrowserThumbnailCell(BrowserViewModel model, Guid id)
    {
        _model = model; _id = id; Width = 64; Height = 48; Background = new SolidColorBrush(Color.Parse("#05070A"));
        var grid = new Grid(); grid.Children.Add(_image); grid.Children.Add(_state); Child = grid;
        AttachedToVisualTree += (_, _) => { _attached = true; _model.PropertyChanged += Changed; Apply(); };
        DetachedFromVisualTree += (_, _) => { _attached = false; _model.PropertyChanged -= Changed; Release(); };
    }
    private void Changed(object? sender, PropertyChangedEventArgs e) { if (e.PropertyName == nameof(BrowserViewModel.Thumbnails)) Apply(); }
    private void Release() { _image.Source = null; if (_bitmap is not null) { _bitmap.Dispose(); _bitmap = null; Interlocked.Decrement(ref _retained); } _path = null; }
    private void Apply()
    {
        if (!_attached) return;
        var value = _model.Thumbnails.GetValueOrDefault(_id);
        if (_path != value?.Path)
        {
            Release();
            if (value?.Path is { } path)
            {
                try { using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete); _bitmap = new Bitmap(stream); Interlocked.Increment(ref _retained); _path = path; _image.Source = _bitmap; }
                catch (Exception error) when (error is IOException or ArgumentException or NotSupportedException) { _state.Text = "Preview unavailable"; }
            }
        }
        _image.RenderTransformOrigin = new RelativePoint(.5, .5, RelativeUnit.Relative);
        _image.RenderTransform = new RotateTransform(value?.RotationDegrees ?? 0);
        _state.Text = value?.Path is not null && _bitmap is null ? "Preview unavailable" : value?.State ?? "Loading"; _state.IsVisible = _bitmap is null || value?.State != "Ready";
        _state.Background = new SolidColorBrush(Color.FromArgb(200, 5, 7, 10));
        ToolTip.SetTip(this, value?.Diagnostic ?? value?.State ?? "Loading thumbnail");
        Avalonia.Automation.AutomationProperties.SetName(this, value?.State ?? "Loading thumbnail");
    }
}
