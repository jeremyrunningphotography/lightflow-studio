using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

using Point = System.Windows.Point;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace LightflowStudio;

/// <summary>
/// Keeps WPF's native header drag, destination calculation and column movement.
/// Only the native separator's paint and visible drop region are customized.
/// </summary>
public sealed class BrowserDetailsHeaderPresenter : GridViewHeaderRowPresenter
{
    private Separator? _indicator;
    private Window? _owner;

    public BrowserDetailsHeaderPresenter()
    {
        Loaded += (_, _) =>
        {
            _owner = Window.GetWindow(this);
            _owner?.AddHandler(Keyboard.PreviewKeyDownEvent, new System.Windows.Input.KeyEventHandler(OwnerKeyDown), true);
        };
        Unloaded += (_, _) =>
        {
            CancelFeedback();
            _owner?.RemoveHandler(Keyboard.PreviewKeyDownEvent, new System.Windows.Input.KeyEventHandler(OwnerKeyDown));
            _owner = null;
        };
        IsVisibleChanged += (_, _) => { if (!IsVisible) CancelFeedback(); };
    }

    protected override void OnVisualChildrenChanged(DependencyObject added, DependencyObject removed)
    {
        base.OnVisualChildrenChanged(added, removed);
        if (ReferenceEquals(removed, _indicator)) _indicator = null;
        // WPF creates exactly one direct Separator for its native insertion feedback.
        if (added is not Separator indicator) return;
        _indicator = indicator;
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetResourceReference(Border.BackgroundProperty, "ShellFocusBrush");
        indicator.Template = new ControlTemplate(typeof(Separator)) { VisualTree = border };
        indicator.IsHitTestVisible = false;
        indicator.Focusable = false;
        indicator.Opacity = 0;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        UpdateFeedback(e.GetPosition(this));
    }

    internal void UpdateFeedback(Point point)
    {
        if (_indicator is null) return;
        var valid = IsVisible && point.Y >= 0 && point.Y < ActualHeight && point.X >= 0 && point.X <= ActualWidth;
        // Clip to the *visible* header viewport, including horizontal scrolling.
        for (DependencyObject? parent = VisualTreeHelper.GetParent(this); valid && parent is not null; parent = VisualTreeHelper.GetParent(parent))
        {
            if (parent is not ScrollViewer viewport) continue;
            var visible = TransformToAncestor(viewport).Transform(point);
            valid = visible.X >= 0 && visible.X < viewport.ActualWidth && visible.Y >= 0 && visible.Y < viewport.ActualHeight;
            break;
        }
        _indicator.Opacity = valid ? 1 : 0;
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (_indicator is not null) _indicator.Opacity = 0;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_indicator is not null) _indicator.Opacity = 0;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        if (_indicator is not null) _indicator.Opacity = 0;
    }

    private void OwnerKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && Mouse.Captured is DependencyObject captured && IsAncestorOf(captured))
        { CancelFeedback(); e.Handled = true; }

    }

    internal void CancelFeedback()
    {
        if (_indicator is not null) _indicator.Opacity = 0;
        // Native WPF cancels its drag on capture loss. Never move columns here.
        if (Mouse.Captured is DependencyObject captured && IsAncestorOf(captured)) Mouse.Capture(null);
    }
}
