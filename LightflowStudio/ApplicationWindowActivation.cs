using System.Windows;

namespace LightflowStudio;

internal interface IApplicationWindow
{
    bool IsMinimized { get; }
    bool IsVisible { get; }
    void Restore();
    void Show();
    bool Activate();
    bool Focus();
}

internal static class ApplicationWindowActivation
{
    public static void RestoreAndActivate(IApplicationWindow window)
    {
        if (window.IsMinimized) window.Restore();
        if (!window.IsVisible) window.Show();
        window.Activate();
        window.Focus();
    }
}

internal sealed class WpfApplicationWindow(Window window, WindowState lastNonMinimizedState) : IApplicationWindow
{
    public bool IsMinimized => window.WindowState == WindowState.Minimized;
    public bool IsVisible => window.IsVisible;
    public void Restore() => window.WindowState = lastNonMinimizedState;
    public void Show() => window.Show();
    public bool Activate() => window.Activate();
    public bool Focus() => window.Focus();
}
