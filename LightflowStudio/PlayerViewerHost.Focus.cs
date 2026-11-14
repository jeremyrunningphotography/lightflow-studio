using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ButtonBase = System.Windows.Controls.Primitives.ButtonBase;
using MenuItem = System.Windows.Controls.MenuItem;
using TextBoxBase = System.Windows.Controls.Primitives.TextBoxBase;

namespace LightflowStudio;

public partial class PlayerViewerHost
{
    private int _speedBeforePopup = -1;

    private void SpeedChoice_DropDownOpened(object? sender, EventArgs e) =>
        _speedBeforePopup = SpeedChoice.SelectedIndex;

    private void SpeedChoice_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape && SpeedChoice.IsDropDownOpen && _speedBeforePopup >= 0)
            SpeedChoice.SelectedIndex = _speedBeforePopup;
        // Let ComboBox close/cancel through its own key handler.
    }

    private void SpeedChoice_DropDownClosed(object? sender, EventArgs e) =>
        RestorePlayerFocusAfterSpeedInteraction();

    private void RestorePlayerFocusAfterSpeedInteraction()
    {
        if (_updatingReview) return;
        var generation = _generation;
        // Popup closure restores ComboBox focus itself. Wait until that input has
        // finished, then release only focus that still belongs to this dropdown.
        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
        {
            if (generation != _generation || !IsLoaded || !IsVisible || !IsEnabled ||
                _service is null || !PositionSlider.IsEnabled || SpeedChoice.IsDropDownOpen ||
                Mouse.Captured is MenuBase ||
                !SpeedOwnsKeyboardFocus() || Window.GetWindow(this)?.IsActive != true)
                return;
            Focus();
        }));
    }

    private bool SpeedOwnsKeyboardFocus()
    {
        if (SpeedChoice.IsKeyboardFocusWithin) return true;
        // Popup item visuals live under a separate HWND, outside the host tree.
        var focused = Keyboard.FocusedElement as DependencyObject;
        while (focused is not null)
        {
            if (focused is ComboBoxItem item &&
                ReferenceEquals(ItemsControl.ItemsControlFromItemContainer(item), SpeedChoice))
                return true;
            focused = focused is Visual ? VisualTreeHelper.GetParent(focused) : null;
        }
        return false;
    }

    private void PlayerNeutralSurface_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.Handled || _service is null || !PositionSlider.IsEnabled) return;
        var element = e.OriginalSource as DependencyObject;
        while (element is not null && !ReferenceEquals(element, this))
        {
            // Neutral Player chrome may be non-focusable. Input/selection owners
            // keep native mouse and keyboard behavior; no event is consumed here.
            if (element is TextBoxBase or PasswordBox or Selector or Slider or Thumb or
                ButtonBase or MenuBase or MenuItem or ScrollViewer or SubclipsView)
                return;
            element = element is Visual ? VisualTreeHelper.GetParent(element) : null;
        }
        if (ReferenceEquals(element, this)) Focus();
    }
}

