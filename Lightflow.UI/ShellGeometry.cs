using Avalonia;
using Avalonia.Controls;

namespace Lightflow.UI;

/// <summary>Restored client size in logical units; desktop origin in native pixels. Never fullscreen state.</summary>
public sealed record ShellWindowGeometry(double Width = 1440, double Height = 900, int X = 0, int Y = 0, double Scale = 1)
{
    public bool IsValid => double.IsFinite(Width) && double.IsFinite(Height) && Width is >= 1120 and <= 20000
        && Height is >= 720 and <= 20000 && double.IsFinite(Scale) && Scale is > 0 and <= 8;
}

public sealed record ShellWorkArea(PixelRect Bounds, double Scale = 1)
{
    public double Width => Bounds.Width / Scale;
    public double Height => Bounds.Height / Scale;
}

public static class ShellGeometry
{
    public const double DefaultWidth = 1440, DefaultHeight = 900, MinimumWidth = 1120, MinimumHeight = 720;

    public static ShellWorkArea Select(ShellWindowGeometry? preferred, IReadOnlyList<ShellWorkArea> areas)
    {
        if (areas.Count == 0) return new(new PixelRect(0,0,1920,1080));
        if (preferred is null) return areas[0];
        var rectangle=new PixelRect(preferred.X,preferred.Y,(int)(preferred.Width*preferred.Scale),(int)(preferred.Height*preferred.Scale));
        return areas.OrderByDescending(a=> { var overlap=a.Bounds.Intersect(rectangle); return (long)overlap.Width*overlap.Height; })
            .ThenBy(a=>Math.Pow(a.Bounds.Center.X-rectangle.Center.X,2)+Math.Pow(a.Bounds.Center.Y-rectangle.Center.Y,2)).First();
    }
    public static ShellWindowGeometry FirstRun(ShellWorkArea area)
    {
        var width=Math.Min(DefaultWidth,area.Width); var height=Math.Min(DefaultHeight,area.Height);
        return new(DefaultWidth,DefaultHeight,area.Bounds.X+(int)((area.Width-width)*area.Scale/2),area.Bounds.Y+(int)((area.Height-height)*area.Scale/2),area.Scale);
    }
    public static ShellWindowGeometry Clamp(ShellWindowGeometry preferred, ShellWorkArea area)
    {
        var width=Math.Min(Math.Max(MinimumWidth,preferred.Width),area.Width);
        var height=Math.Min(Math.Max(MinimumHeight,preferred.Height),area.Height);
        return new(width,height,(int)Math.Clamp(preferred.X,area.Bounds.X,Math.Max(area.Bounds.X,area.Bounds.Right-width*area.Scale)),
            (int)Math.Clamp(preferred.Y,area.Bounds.Y,Math.Max(area.Bounds.Y,area.Bounds.Bottom-height*area.Scale)),area.Scale);
    }
    // An OS clamp, DPI change or zoom must not replace the user's unconstrained restored preference.
    public static ShellWindowGeometry Remember(ShellWindowGeometry preferred, ShellWindowGeometry observed,
        ShellWorkArea area, bool sizeChanged, bool positionChanged)
    {
        var clamped=Clamp(preferred,area);
        var width=sizeChanged && Math.Abs(observed.Width-clamped.Width)>1 ? Math.Max(MinimumWidth,observed.Width) : preferred.Width;
        var height=sizeChanged && Math.Abs(observed.Height-clamped.Height)>1 ? Math.Max(MinimumHeight,observed.Height) : preferred.Height;
        return new(width,height,positionChanged && observed.X!=clamped.X ? observed.X : preferred.X,
            positionChanged && observed.Y!=clamped.Y ? observed.Y : preferred.Y,observed.Scale);
    }
}

public sealed record BrowserPaneWidths(double Navigation, double Right, double CenterMinimum)
{
    public static BrowserPaneWidths Fit(double width, BrowserLayout preferred)
    {
        var gap=preferred.RightOpen ? 16 : 8;
        var center=Math.Min(340,Math.Max(0,width-gap-140-(preferred.RightOpen ? 180 : 0)));
        var budget=Math.Max(0,width-gap-center);
        var left=preferred.NavigationWidth;var right=preferred.RightOpen ? preferred.RightWidth : 0;
        void Reduce(ref double value,double minimum) => value=Math.Max(minimum,value-Math.Max(0,left+right-budget));
        Reduce(ref left,180); if(preferred.RightOpen) Reduce(ref right,200);
        Reduce(ref left,140); if(preferred.RightOpen) Reduce(ref right,180);
        if(left+right>budget && left+right>0) { var ratio=budget/(left+right); left*=ratio;right*=ratio; }
        return new(left,right,center);
    }
}

/// <summary>Only ordinary native zoom is restored; fullscreen is never a startup preference.</summary>
public static class ShellWindowState
{
    public static bool RememberZoom(WindowState state, bool previous) => state switch
    {
        WindowState.Maximized => true,
        WindowState.Normal or WindowState.FullScreen => false,
        _ => previous // Minimizing does not discard the preceding normal/zoomed choice.
    };
}
