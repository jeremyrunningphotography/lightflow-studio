using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LightflowStudio;

// Presentation only. App owns shutdown; MainWindow owns workspace restoration/readiness.
internal sealed class StartupSplash : Window
{
    private readonly System.Windows.Controls.TextBlock _status = new()
    {
        Text = "Starting Lightflow…",
        FontSize = 12,
        TextAlignment = TextAlignment.Center,
        HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
        VerticalAlignment = System.Windows.VerticalAlignment.Bottom,
        Margin = new Thickness(16, 0, 16, 12),
        IsHitTestVisible = false
    };

    // Reserve the existing footer only for exceptional status; do not paint over the artwork tagline.
    private readonly System.Windows.Controls.Border _validationBackdrop = new()
    {
        Height = 54, VerticalAlignment = VerticalAlignment.Bottom,
        Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(8, 8, 10)),
        Visibility = Visibility.Collapsed, IsHitTestVisible = false
    };
    private readonly System.Windows.Controls.TextBlock _support = new()
    {
        FontSize = 11, TextAlignment = TextAlignment.Center,
        VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(8, 0, 8, 12),
        Visibility = Visibility.Collapsed, IsHitTestVisible = false
    };
    private readonly System.Windows.Controls.ProgressBar _progress = new()
    {
        IsIndeterminate = true, Height = 2, VerticalAlignment = VerticalAlignment.Bottom,
        Margin = new Thickness(24, 0, 24, 5), Visibility = Visibility.Collapsed, IsHitTestVisible = false
    };
    internal void SetProgress(string message)
    {
        _status.Text = message; _status.Margin = new Thickness(8, 0, 8, 12);
        _validationBackdrop.Visibility = _support.Visibility = _progress.Visibility = Visibility.Collapsed;
    }
    internal void SetValidation(StartupValidationProgress message)
    {
        _status.Text = message.Primary; _status.Margin = new Thickness(8, 0, 8, 29);
        _support.Text = message.Supporting;
        _validationBackdrop.Visibility = _support.Visibility = _progress.Visibility = Visibility.Visible;
    }

    internal void CaptureValidation(string directory)
    {
        var content = (System.Windows.Controls.Grid)Content;
        content.Measure(new System.Windows.Size(Width, Height)); content.Arrange(new Rect(0, 0, Width, Height)); content.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(Width), (int)Math.Ceiling(Height), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(content);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        System.IO.Directory.CreateDirectory(directory);
        var store = _status.Text.Contains("Preview", StringComparison.Ordinal) ? "Previews" : "Catalog";
        using var file = System.IO.File.Create(System.IO.Path.Combine(directory, $"{Environment.ProcessId}-{store}.png"));
        encoder.Save(file);
    }

    internal StartupSplash()
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(8, 8, 10));
        Width = 440;
        Height = Width * 480 / 680;
        var artwork = new System.Windows.Controls.Image
        {
            // Hands-on refinement: trim only empty canvas in presentation. The approved embedded
            // source remains unchanged, with the mark, glow, wordmark and tagline inside this viewport.
            Source = new CroppedBitmap(new BitmapImage(new Uri("pack://application:,,,/LightflowStudio;component/Assets/Branding/lightflow-splash-1280x720.png")),
                new Int32Rect(300, 110, 680, 480)),
            Stretch = Stretch.Uniform,
            IsHitTestVisible = false
        };
        _status.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "MutedTextBrush");
        var content = new System.Windows.Controls.Grid();
        content.Children.Add(artwork);
        content.Children.Add(_validationBackdrop);
        content.Children.Add(_status);
        _support.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "MutedTextBrush");
        _progress.SetResourceReference(System.Windows.Controls.ProgressBar.ForegroundProperty, "BrandGradient");
        content.Children.Add(_support);
        content.Children.Add(_progress);
        Content = content;
        SourceInitialized += (_, _) =>
        {
            // Position the HWND on the cursor's display before measuring that display's WPF DPI.
            var area = System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Cursor.Position).WorkingArea;
            var handle = new WindowInteropHelper(this).Handle;
            SetWindowPos(handle, 0, area.Left, area.Top, 0, 0, 0x0015); // NOSIZE | NOZORDER | NOACTIVATE
            var scale = VisualTreeHelper.GetDpi(this);
            Width = Math.Min(440, Math.Min(area.Width / scale.DpiScaleX * 0.8,
                area.Height / scale.DpiScaleY * 0.8 * 680 / 480));
            Height = Width * 480 / 680;
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
