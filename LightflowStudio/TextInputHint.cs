using System.Windows;

namespace LightflowStudio;

/// <summary>Optional hint rendered by the ordinary TextBox template with its own padding/alignment.</summary>
public static class TextInputHint
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached(
        "Text", typeof(string), typeof(TextInputHint), new PropertyMetadata(""));
    public static string GetText(DependencyObject element) => (string)element.GetValue(TextProperty);
    public static void SetText(DependencyObject element, string value) => element.SetValue(TextProperty, value);
}
