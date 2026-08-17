namespace LightflowStudio;

/// <summary>A monitor's usable (taskbar-excluded) area, in the same coordinate space as the window bounds being validated.</summary>
internal readonly record struct ScreenWorkArea(double Left, double Top, double Width, double Height);

/// <summary>
/// Pure, WPF-independent geometry recovery for restored window bounds. Ensures Lightflow never reopens smaller
/// than its declared minimum, and never positioned where no currently connected monitor can meaningfully show
/// it (monitor removal, resolution/DPI changes, or otherwise obsolete saved geometry).
/// </summary>
internal static class WorkspaceWindowPlacement
{
    internal const double MinimumVisibleWidth = 120;
    internal const double MinimumVisibleHeight = 60;

    public static WorkspaceWindowState Clamp(WorkspaceWindowState saved, IReadOnlyList<ScreenWorkArea> workAreas,
        double minWidth, double minHeight)
    {
        var width = Math.Max(minWidth, saved.Width);
        var height = Math.Max(minHeight, saved.Height);

        if (workAreas.Count == 0)
            return new WorkspaceWindowState { Width = width, Height = height, Left = 0, Top = 0, IsMaximized = saved.IsMaximized };

        if (workAreas.Any(area => IsSufficientlyVisible(saved.Left, saved.Top, width, height, area)))
            return saved with { Width = width, Height = height };

        // No connected monitor shows enough of the saved position: recover onto the primary work area, centered.
        var primary = workAreas[0];
        width = Math.Min(width, primary.Width);
        height = Math.Min(height, primary.Height);
        return new WorkspaceWindowState
        {
            Width = width,
            Height = height,
            Left = primary.Left + Math.Max(0, (primary.Width - width) / 2),
            Top = primary.Top + Math.Max(0, (primary.Height - height) / 2),
            IsMaximized = saved.IsMaximized
        };
    }

    private static bool IsSufficientlyVisible(double left, double top, double width, double height, ScreenWorkArea area)
    {
        var overlapWidth = Math.Min(left + width, area.Left + area.Width) - Math.Max(left, area.Left);
        var overlapHeight = Math.Min(top + height, area.Top + area.Height) - Math.Max(top, area.Top);
        return overlapWidth >= MinimumVisibleWidth && overlapHeight >= MinimumVisibleHeight;
    }
}
