using System.Windows.Media.Imaging;

namespace LightflowStudio;

/// <summary>Publishes immutable pixels, without a WIC decoder or its stream/thread lifetime.</summary>
internal static class DetachedBitmap
{
    public static BitmapSource Copy(BitmapSource source)
    {
        // Freeze/Clone alone is insufficient: BitmapFrame (and transforms over it) can retain
        // a decoder whose FreezeCore still checks its owning dispatcher. CopyPixels is safe
        // for an already frozen cross-thread source, or on the decoding thread before publication.
        var stride = checked((source.PixelWidth * source.Format.BitsPerPixel + 7) / 8);
        var pixels = new byte[checked(stride * source.PixelHeight)];
        source.CopyPixels(pixels, stride, 0);
        var result = BitmapSource.Create(source.PixelWidth, source.PixelHeight,
            source.DpiX, source.DpiY, source.Format, source.Palette, pixels, stride);
        result.Freeze();
        return result;
    }
}
