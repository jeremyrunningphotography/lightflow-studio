using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Controls.Shapes;

namespace Lightflow.UI;

/// <summary>Presentation values aligned with WPF Themes/LightflowShell.xaml. No Browser state owner.</summary>
public static class BrowserPresentation
{
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
        Classes={ "lf-icon",kind=="folder" ? "folder" : "navigation",active ? "active" : "inactive" }
    };
}

/// <summary>Optional view preferences only, independent of Catalog and navigation persistence.</summary>
public sealed record BrowserLayout(double NavigationWidth = 280, double RightWidth = 360, bool RightOpen = false,
    ShellWindowGeometry? Window = null, int Version = 3, bool Zoomed = false)
{
    public BrowserLayout Validated() => this with
    {
        NavigationWidth=double.IsFinite(NavigationWidth) ? Math.Clamp(NavigationWidth,180,520) : 280,
        RightWidth=double.IsFinite(RightWidth) ? Math.Clamp(RightWidth,200,600) : 360,
        Window=Window is { IsValid:true } ? Window : null,
        Version=3
    };
}
