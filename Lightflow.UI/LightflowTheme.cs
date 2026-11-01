using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;

namespace Lightflow.UI;

/// <summary>One reusable shared view vocabulary; no platform behavior or feature state.</summary>
public static class LightflowTheme
{
    public static void Apply(Window window)
    {
        window.Classes.Add("lightflow");
        window.Styles.Add(new StyleInclude(new Uri("avares://Lightflow.UI/"))
            { Source=new Uri("avares://Lightflow.UI/Themes/Lightflow.axaml") });
    }
    public static TextBlock Text(string text, string role="body") => new()
        { Text=text, TextWrapping=TextWrapping.Wrap, Classes={ "lf-text", role } };
    public static Button Button(string text, string role="secondary") => new()
        { Content=text, Classes={ "lf-button", role } };
    public static Border Panel(Control child, bool padding=true) => new()
        { Child=child, Classes={ "lf-panel", padding ? "padded" : "flush" } };
    public static void State(Control control, string state, bool enabled) => control.Classes.Set(state,enabled);
}
