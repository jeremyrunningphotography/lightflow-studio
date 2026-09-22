using System.Windows;

namespace LightflowStudio;

// Folder containers opt in; the shared tree template also serves Collections.
public static class TreeDropPresentation
{
    public static readonly DependencyProperty IsValidProperty = DependencyProperty.RegisterAttached(
        "IsValid", typeof(bool), typeof(TreeDropPresentation), new PropertyMetadata(false));
    public static bool GetIsValid(DependencyObject target) => (bool)target.GetValue(IsValidProperty);
    public static void SetIsValid(DependencyObject target, bool value) => target.SetValue(IsValidProperty, value);
    public static readonly DependencyProperty IsInvalidProperty = DependencyProperty.RegisterAttached(
        "IsInvalid", typeof(bool), typeof(TreeDropPresentation), new PropertyMetadata(false));
    public static bool GetIsInvalid(DependencyObject target) => (bool)target.GetValue(IsInvalidProperty);
    public static void SetIsInvalid(DependencyObject target, bool value) => target.SetValue(IsInvalidProperty, value);
}
