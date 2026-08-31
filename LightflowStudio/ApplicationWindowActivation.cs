using System.Windows;
using System.Windows.Interop;
using System.Runtime.InteropServices;

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
    private const int ShowRestore = 9;
    private const int ShowMaximized = 3;
    private IntPtr Handle => new WindowInteropHelper(window).Handle;

    public bool IsMinimized => window.WindowState == WindowState.Minimized || IsIconic(Handle);
    public bool IsVisible => window.IsVisible;
    public void Restore()
    {
        window.WindowState = lastNonMinimizedState;
        ShowWindow(Handle, lastNonMinimizedState == WindowState.Maximized ? ShowMaximized : ShowRestore);
    }
    public void Show() => window.Show();
    public bool Activate()
    {
        var activated = window.Activate();
        var foreground = SetForegroundWindow(Handle);
        if (!activated && !foreground) FlashWindow(Handle, invert: true);
        return activated || foreground;
    }
    public bool Focus() => window.Focus();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr windowHandle, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FlashWindow(IntPtr windowHandle, [MarshalAs(UnmanagedType.Bool)] bool invert);
}
