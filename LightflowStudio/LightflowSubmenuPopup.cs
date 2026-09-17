using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace LightflowStudio;

/// <summary>
/// Gives the shared right-chevron menu an explicit preference while leaving monitor/work-area
/// scoring, DPI conversion, edge nudging and popup lifetime to WPF.
/// </summary>
public sealed class LightflowSubmenuPopup : Popup
{
    private DispatcherOperation? _directionUpdate;

    private static readonly DependencyPropertyKey OpensLeftPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(OpensLeft), typeof(bool), typeof(LightflowSubmenuPopup), new PropertyMetadata(false));

    public static readonly DependencyProperty OpensLeftProperty = OpensLeftPropertyKey.DependencyProperty;
    public bool OpensLeft => (bool)GetValue(OpensLeftProperty);

    public LightflowSubmenuPopup()
    {
        // PlacementMode.Right also follows Windows MenuDropAlignment and the ancestor popup's
        // DropOpposite flag. Custom candidates express our style's direction without changing
        // either global Windows preferences or private WPF state.
        Placement = PlacementMode.Custom;
        CustomPopupPlacementCallback = GetPlacements;
    }

    // WPF supplies target/child bounds in the same coordinate space and applies offsets itself.
    private static CustomPopupPlacement[] GetPlacements(Size popup, Size target, Point offset) =>
    [
        new(new Point(target.Width, 0), PopupPrimaryAxis.Vertical),
        new(new Point(target.Width, target.Height - popup.Height), PopupPrimaryAxis.Vertical),
        new(new Point(-popup.Width, 0), PopupPrimaryAxis.Vertical),
        new(new Point(-popup.Width, target.Height - popup.Height), PopupPrimaryAxis.Vertical)
    ];

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (Child is not null) Child.LayoutUpdated += ChildLayoutUpdated;
        UpdateDirection();
    }

    protected override void OnClosed(EventArgs e)
    {
        if (Child is not null) Child.LayoutUpdated -= ChildLayoutUpdated;
        _directionUpdate?.Abort();
        _directionUpdate = null;
        SetValue(OpensLeftPropertyKey, false);
        base.OnClosed(e);
    }

    private void ChildLayoutUpdated(object? sender, EventArgs e)
    {
        if (!IsOpen || _directionUpdate is not null) return;
        // Popup moves its native window after child layout. Observe that final position,
        // coalescing layout notifications rather than reading the previous window position.
        _directionUpdate = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            _directionUpdate = null;
            UpdateDirection();
        }));
    }

    private void UpdateDirection()
    {
        if (!IsOpen || Child is not FrameworkElement child || PlacementTarget is not FrameworkElement target ||
            PresentationSource.FromVisual(child) is null || PresentationSource.FromVisual(target) is null) return;

        // Observe the resolved placement, including remeasurement of generated LUT items.
        // This only changes the glyph; it never moves a popup or changes keyboard navigation.
        var childCenter = child.PointToScreen(new Point(child.ActualWidth / 2, 0));
        var targetCenter = target.PointToScreen(new Point(target.ActualWidth / 2, 0));
        SetValue(OpensLeftPropertyKey, childCenter.X < targetCenter.X);
    }
}
