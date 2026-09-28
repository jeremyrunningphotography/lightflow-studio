using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace LightflowStudio;

/// <summary>Cached Preview pixels must not retain a file handle or WPF's URI image cache.</summary>
public sealed class CachedPreviewImageConverter : IValueConverter
{
    public static CachedPreviewImageConverter Instance { get; } = new();

    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string { Length: > 0 } path) return null;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            return decoder.Frames.Count == 0 ? null : DetachedBitmap.Copy(decoder.Frames[0]);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or
            NotSupportedException or FileFormatException)
        {
            // The rebuildable file may disappear during maintenance. Keep normal placeholder behavior.
            return null;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        DependencyProperty.UnsetValue;
}
