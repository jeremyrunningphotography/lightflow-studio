using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Controls.Shapes;

namespace Lightflow.UI;

/// <summary>Presentation values aligned with WPF Themes/LightflowShell.xaml. No Browser state owner.</summary>
public static class BrowserPresentation
{
    public const string Canvas = "#0B0D11", Surface = "#11141A", Panel = "#171A20", Raised = "#1D2129",
        Divider = "#2B303A", Accent = "#FF9A66", Selection = "#282129", Text = "#D8D8DC";
    public static IBrush Brush(string color) => new SolidColorBrush(Color.Parse(color));
    // WPF's filled-folder silhouette, and font-independent equivalents of its Segoe controls.
    public static Avalonia.Controls.Shapes.Path Icon(string kind, bool active = false) => new()
    {
        Width = 16, Height = 16, Stretch = Stretch.Uniform, IsHitTestVisible = false,
        Data = Geometry.Parse(kind switch
        {
            "folder" => "M1,2 L6,2 L7.5,4 L15,4 L15,12 L1,12 Z",
            "back" => "M10,2 L4,8 L10,14 M4,8 L16,8",
            "forward" => "M6,2 L12,8 L6,14 M0,8 L12,8",
            "up" => "M2,7 L8,1 L14,7 M8,1 L8,15",
            "refresh" => "M13,5 A6,6 0 1 0 14,10 M9,5 L14,5 L14,0",
            "down" => "M2,5 L8,11 L14,5",
            "right" => "M5,2 L11,8 L5,14",
            "scope" => "M1,1 L6,1 L6,6 L1,6 Z M10,10 L15,10 L15,15 L10,15 Z M3,8 L3,13 L8,13",
            "panel" => "M1,1 L15,1 L15,15 L1,15 Z M10,1 L10,15",
            _ => "M2,2 L14,14 M14,2 L2,14"
        }),
        Stroke = Brush(active ? Accent : kind == "folder" ? "#E4B85A" : Text), StrokeThickness = 1.5,
        Fill = kind == "folder" && active ? Brush(Accent) : Brushes.Transparent
    };
}

/// <summary>Only view proportions, independent of Catalog and navigation persistence.</summary>
public sealed record BrowserLayout(double Navigation = .24, double Assets = .52, double Right = .24, bool RightOpen = true)
{
    public BrowserLayout Validated() => double.IsFinite(Navigation) && double.IsFinite(Assets) && double.IsFinite(Right)
        && Navigation >= .02 && Assets >= .1 && Right >= .02 && Navigation + Assets + Right is > .99 and < 1.01
        ? this : new();
}
