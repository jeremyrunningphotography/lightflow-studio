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
            "home" => "M1,7 L8,1 L15,7 M3,6 L3,15 L13,15 L13,6 M6,15 L6,10 L10,10 L10,15",
            "desktop" => "M1,2 L15,2 L15,12 L1,12 Z M5,15 L11,15 M8,12 L8,15",
            "documents" => "M3,1 L10,1 L14,5 L14,15 L3,15 Z M10,1 L10,5 L14,5 M6,8 L11,8 M6,11 L10,11",
            "downloads" => "M8,1 L8,11 M4,7 L8,11 L12,7 M2,11 L2,15 L14,15 L14,11",
            "disk" => "M3,2 L13,2 L16,9 L16,14 L0,14 L0,9 Z M0,9 L16,9 M3,12 L5,12",
            "external" => "M3,1 L13,1 L13,15 L3,15 Z M6,12 L10,12 M8,4 L8,9 M6,6 L8,4 L10,6",
            "share" => "M1,2 L6,2 L8,4 L15,4 L15,11 L1,11 Z M8,11 L8,15 M3,15 L13,15",
            "server" => "M1,1 L15,1 L15,6 L1,6 Z M1,10 L15,10 L15,15 L1,15 Z M4,3 L4,4 M4,12 L4,13",
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
        Classes={ "lf-icon",kind is "folder" or "home" or "desktop" or "documents" or "downloads" or "disk" or "external" or "share" or "server" ? "location" : "navigation",kind=="folder" ? "folder" : "outline",active ? "active" : "inactive" }
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
