using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace LightflowStudio;

/// <summary>One gesture owner for WPF still/retained frames and the native video window.</summary>
internal sealed class PlayerSurfaceInput : IDisposable
{
    private readonly FrameworkElement _surface;
    private readonly Action _click;
    private readonly Action _fullscreen;
    private readonly Action<double, double> _pan;
    private readonly Action<int> _zoom;
    private readonly Func<Key, DependencyObject?, bool> _key;
    private readonly Func<Key, bool> _keyUp;
    private readonly DispatcherTimer _single;
    private System.Windows.Point? _origin;
    private System.Windows.Point _previous;
    private bool _dragged;
    private bool _double;

    internal PlayerSurfaceInput(FrameworkElement surface, Action click, Action fullscreen,
        Action<double, double> pan, Action<int> zoom,
        Func<Key, DependencyObject?, bool> key, Func<Key, bool> keyUp)
    {
        _surface = surface; _click = click; _fullscreen = fullscreen; _pan = pan;
        _zoom = zoom; _key = key; _keyUp = keyUp;
        _single = new DispatcherTimer(DispatcherPriority.Input, surface.Dispatcher)
        { Interval = TimeSpan.FromMilliseconds(System.Windows.Forms.SystemInformation.DoubleClickTime) };
        _single.Tick += Single;
        surface.PreviewMouseLeftButtonDown += Down;
        surface.PreviewMouseLeftButtonUp += Up;
        surface.PreviewMouseMove += Move;
        surface.PreviewMouseWheel += Wheel;
        surface.LostMouseCapture += Lost;
        surface.PreviewKeyDown += KeyDown;
        surface.PreviewKeyUp += KeyUp;
        surface.Unloaded += Unloaded;
    }

    private void Down(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        _surface.Focus();
        BeginGesture(e.GetPosition(_surface), e.ClickCount);
        _surface.CaptureMouse();
    }

    internal void BeginGesture(System.Windows.Point point, int clickCount)
    {
        _origin = _previous = point;
        _dragged = false;
        _double = clickCount == 2;
        if (_double) _single.Stop();
    }

    private void Move(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (MoveGesture(e.GetPosition(_surface), e.LeftButton == MouseButtonState.Pressed)) e.Handled = true;
    }

    internal bool MoveGesture(System.Windows.Point point, bool leftPressed)
    {
        if (_origin is not { } origin || !leftPressed) return false;
        if (!_dragged && Math.Abs(point.X - origin.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(point.Y - origin.Y) < SystemParameters.MinimumVerticalDragDistance) return false;
        _dragged = true;
        _single.Stop();
        _pan(point.X - _previous.X, point.Y - _previous.Y);
        _previous = point;
        return true;
    }

    private void Up(object sender, MouseButtonEventArgs e)
    {
        if (_origin is null) return;
        e.Handled = true;
        EndGesture();
    }

    internal void EndGesture()
    {
        if (_origin is null) return;
        var click = !_dragged;
        var fullscreen = _double;
        _origin = null;
        _surface.ReleaseMouseCapture();
        if (!click) return;
        if (fullscreen) _fullscreen();
        else _single.Start();
    }

    private void Single(object? sender, EventArgs e) { _single.Stop(); _click(); }
    private void Lost(object sender, System.Windows.Input.MouseEventArgs e) => _origin = null;
    private void Wheel(object sender, MouseWheelEventArgs e)
    {
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) return;
        e.Handled = true; _zoom(Math.Sign(e.Delta));
    }
    private void KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (!e.Handled && !e.IsRepeat) e.Handled = _key(e.Key, e.OriginalSource as DependencyObject);
        else if (e.IsRepeat && e.Key == Key.Space) e.Handled = true;
    }
    private void KeyUp(object sender, System.Windows.Input.KeyEventArgs e)
    { if (!e.Handled) e.Handled = _keyUp(e.Key); }
    private void Unloaded(object sender, RoutedEventArgs e) => Cancel();
    internal void Cancel() { _single.Stop(); _origin = null; if (_surface.IsMouseCaptured) _surface.ReleaseMouseCapture(); }
    public void Dispose()
    {
        Cancel();
        _single.Tick -= Single;
        _surface.PreviewMouseLeftButtonDown -= Down;
        _surface.PreviewMouseLeftButtonUp -= Up;
        _surface.PreviewMouseMove -= Move;
        _surface.PreviewMouseWheel -= Wheel;
        _surface.LostMouseCapture -= Lost;
        _surface.PreviewKeyDown -= KeyDown;
        _surface.PreviewKeyUp -= KeyUp;
        _surface.Unloaded -= Unloaded;
    }
}
