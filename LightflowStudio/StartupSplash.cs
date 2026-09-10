using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LightflowStudio;

// Presentation only. App owns shutdown; MainWindow owns workspace restoration/readiness.
internal sealed class StartupSplash : Window
{
    internal StartupSplash()
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(8, 8, 10));
        Width = 640;
        Height = 360;
        Content = new System.Windows.Controls.Image
        {
            Source = new BitmapImage(new Uri("pack://application:,,,/LightflowStudio;component/Assets/Branding/lightflow-splash-1280x720.png")),
            Stretch = Stretch.Uniform,
            IsHitTestVisible = false
        };
        SourceInitialized += (_, _) =>
        {
            // Position the HWND on the cursor's display before measuring that display's WPF DPI.
            var area = System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Cursor.Position).WorkingArea;
            var handle = new WindowInteropHelper(this).Handle;
            SetWindowPos(handle, 0, area.Left, area.Top, 0, 0, 0x0015); // NOSIZE | NOZORDER | NOACTIVATE
            var scale = VisualTreeHelper.GetDpi(this);
            Width = Math.Min(640, area.Width / scale.DpiScaleX * 0.8);
            Height = Width * 9 / 16;
            SetWindowPos(handle, 0, area.Left + (area.Width - (int)(Width * scale.DpiScaleX)) / 2,
                area.Top + (area.Height - (int)(Height * scale.DpiScaleY)) / 2, 0, 0, 0x0015);
        };
    }

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(nint window, nint insertAfter, int x, int y, int cx, int cy, uint flags);
}

internal static class StartupWindowPresentation
{
    // DWM cloaking keeps the real HWND, Loaded/layout, and native Player surface alive without
    // exposing default chrome or content. Opacity alone cannot hide a hosted native video HWND.
    internal static void SetCloaked(Window window, bool cloaked)
    {
        var value = cloaked ? 1 : 0;
        Marshal.ThrowExceptionForHR(DwmSetWindowAttribute(new WindowInteropHelper(window).Handle, 13, ref value, sizeof(int)));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);
}
