using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using ComboBox = System.Windows.Controls.ComboBox;
using Control = System.Windows.Controls.Control;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using KeyEventHandler = System.Windows.Input.KeyEventHandler;
using ButtonBase = System.Windows.Controls.Primitives.ButtonBase;
using TextBoxBase = System.Windows.Controls.Primitives.TextBoxBase;
using TabControl = System.Windows.Controls.TabControl;

namespace LightflowStudio;

/// <summary>Windows input ownership, shared by semantic and legacy Player shortcuts. Focus is not a Player target.</summary>
internal static class PlayerKeyboardOwnership
{
    internal static bool Owns(Key key, ModifierKeys modifiers, DependencyObject? input, DependencyObject player, DependencyObject filmstrip)
    {
        var original = input;
        var navigation = key is Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End or Key.PageUp or Key.PageDown;
        var activation = key is Key.Space or Key.Enter or Key.Escape;
        var letter = key >= Key.A && key <= Key.Z && modifiers == ModifierKeys.None;
        var digit = key >= Key.D0 && key <= Key.D9 && modifiers == ModifierKeys.None;
        for (var current = input; current is not null; current = Parent(current))
        {
            if (ReferenceEquals(current, player) || ReferenceEquals(current, filmstrip)) return false;
            if (current is TextBoxBase || current is ComboBox { IsEditable: true }) return true;
            if (current is MenuBase or MenuItem) return true;
            // Popup item containers do not have the ComboBox in their visual ancestry.
            if (current is ComboBoxItem item && ItemsControl.ItemsControlFromItemContainer(item) is ComboBox owner)
                return owner.IsDropDownOpen || navigation || activation || letter || digit;
            if (current is ComboBox combo) return combo.IsDropDownOpen || navigation || activation || ((letter || digit) && combo.IsTextSearchEnabled);
            if (current is Slider or Thumb) return navigation;
            if (current is ButtonBase && key is Key.Space or Key.Enter) return true;
            if (current is TabControl tabs && tabs.SelectedContent is DependencyObject content && Within(original, content)) return false;
            if (current is Selector selector) return navigation || activation || ((letter || digit) && selector.IsTextSearchEnabled);
        }
        return false;
    }
    internal static DependencyObject? Parent(DependencyObject value) => value is Visual or Visual3D
        ? VisualTreeHelper.GetParent(value) ?? LogicalTreeHelper.GetParent(value)
        : value is FrameworkContentElement content ? content.Parent : LogicalTreeHelper.GetParent(value);
    internal static bool Within(DependencyObject? value, DependencyObject ancestor)
    {
        for (; value is not null; value = Parent(value)) if (ReferenceEquals(value, ancestor)) return true;
        return false;
    }
}

/// <summary>
/// Mouse-completed chrome returns to review, while deliberate keyboard/assistive focus stays a real local interaction.
/// WPF events, never timers, define completion. This class does not resolve semantic eligibility or change Tab stops.
/// </summary>
internal sealed class PlayerReviewFocus : IDisposable
{
    private readonly UIElement _root;
    private readonly Func<bool> _active;
    private readonly Action _review;
    private Control? _mouseControl;
    private ComboBox? _mouseCombo;
    internal PlayerReviewFocus(UIElement root, Func<bool> active, Action review)
    {
        _root = root; _active = active; _review = review;
        root.AddHandler(Mouse.PreviewMouseDownEvent, new MouseButtonEventHandler(MouseDown), true);
        root.AddHandler(Mouse.MouseUpEvent, new MouseButtonEventHandler(MouseUp), true);
        root.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler(Clicked), true);
        root.AddHandler(Keyboard.PreviewKeyDownEvent, new KeyEventHandler(KeyDown), true);
    }
    private void MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || !_active()) return;
        ClearMouseInteraction();
        ButtonBase? button = null;
        for (var item = e.OriginalSource as DependencyObject; item is not null; item = PlayerKeyboardOwnership.Parent(item))
        {
            if (item is ComboBoxItem popupItem && ItemsControl.ItemsControlFromItemContainer(popupItem) is ComboBox owner)
                item = owner;
            if (item is TextBoxBase) return;
            if (item is ComboBox combo)
            {
                if (combo.IsEditable) return;
                _mouseControl = _mouseCombo = combo;
                combo.DropDownClosed += ComboClosed;
                return;
            }
            if (item is ButtonBase candidate) button ??= candidate;
            if (ReferenceEquals(item, _root)) break;
        }
        _mouseControl = button;
    }
    private void KeyDown(object sender, KeyEventArgs e)
    {
        // Tab is an explicit change to keyboard navigation, even when a mouse-opened popup closes as a result.
        if (e.Key == Key.Tab) ClearMouseInteraction();
    }
    private void Clicked(object sender, RoutedEventArgs e)
    {
        if (_mouseControl is not ButtonBase button || !PlayerKeyboardOwnership.Within(e.OriginalSource as DependencyObject, button)) return;
        ClearMouseInteraction();
        ReturnFromChrome();
    }
    private void MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || _mouseControl is null) return;
        if (_mouseCombo?.IsDropDownOpen == true) return;
        ClearMouseInteraction();
        ReturnFromChrome();
    }
    private void ComboClosed(object? sender, EventArgs e)
    {
        ClearMouseInteraction();
        ReturnFromChrome();
    }
    private void ReturnFromChrome()
    {
        if (!_active()) return;
        // A button may have opened an editor/menu, or disabling it may have caused WPF focus fallback.
        // Preserve genuine new interaction, but return from incidental Buttons/ScrollViewers/closed pickers.
        for (var item = Keyboard.FocusedElement as DependencyObject; item is not null; item = PlayerKeyboardOwnership.Parent(item))
        {
            if (item is ComboBoxItem popupItem && ItemsControl.ItemsControlFromItemContainer(popupItem) is ComboBox owner)
                item = owner;
            if (item is MediaInspectorView inspector && inspector.DescriptionInteractionPending) return;
            if (item is TextBoxBase or MenuBase or MenuItem or Slider or Thumb || item is ComboBox { IsDropDownOpen: true }) return;
            if (item is Selector && item is not ComboBox and not TabControl) return;
            if (ReferenceEquals(item, _root)) break;
        }
        _review();
    }
    private void ClearMouseInteraction()
    {
        if (_mouseCombo is not null) _mouseCombo.DropDownClosed -= ComboClosed;
        _mouseControl = _mouseCombo = null;
    }
    public void Dispose()
    {
        ClearMouseInteraction();
        _root.RemoveHandler(Mouse.PreviewMouseDownEvent, new MouseButtonEventHandler(MouseDown));
        _root.RemoveHandler(Mouse.MouseUpEvent, new MouseButtonEventHandler(MouseUp));
        _root.RemoveHandler(ButtonBase.ClickEvent, new RoutedEventHandler(Clicked));
        _root.RemoveHandler(Keyboard.PreviewKeyDownEvent, new KeyEventHandler(KeyDown));
    }
}
