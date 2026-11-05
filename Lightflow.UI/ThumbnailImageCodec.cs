using SkiaSharp;
using LightflowStudio;

namespace Lightflow.UI;

/// <summary>SDR sRGB, first-frame thumbnail adapter using the production Avalonia Skia dependency.</summary>
public sealed class ThumbnailImageCodec : IThumbnailImageCodec
{
    public bool Supports(string extension) => extension.ToLowerInvariant() is ".jpg" or ".jpeg" or ".png" or ".webp" or ".bmp" or ".gif";
    public bool Validate(string path)
    {
        try
        {
            using var codec = SKCodec.Create(path);
            if (codec is null) return false;
            var info = codec.Info; using var color = info.ColorSpace;
            if (info.Width <= 0 || info.Height <= 0 || Math.Max(info.Width, info.Height) > 512) return false;
            using var bitmap = new SKBitmap(info);
            return codec.GetPixels(bitmap.Info, bitmap.GetPixels()) == SKCodecResult.Success;
        }
        catch (IOException) { return false; }
    }
    public ThumbnailRenderResult Render(string source, string destination, IReadOnlyList<string> paths, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var codec = SKCodec.Create(source);
        if (codec is null) return new(ThumbnailGenerationStatus.Unsupported, "The image format is unavailable in this decoder.");
        // Preserve the encoded profile on decoded pixels; the sRGB destination canvas performs conversion.
        // Request decoder subsampling first. Some codecs still decode at full size; this is measured, not assumed bounded.
        var sourceInfo = codec.Info;
        using var sourceColor = sourceInfo.ColorSpace;
        var scale = Math.Min(1d, 512d / Math.Max(sourceInfo.Width, sourceInfo.Height));
        var size = codec.GetScaledDimensions((float)scale);
        if ((long)size.Width * size.Height > 64L * 1024 * 1024) return new(ThumbnailGenerationStatus.Unsupported, "This decoder cannot provide a safely bounded thumbnail for this image size.", PreviewFailureReason.CodecUnavailable);
        using var srgb = SKColorSpace.CreateSrgb();
        using var decoded = new SKBitmap(new SKImageInfo(size.Width, size.Height, SKColorType.Rgba8888, SKAlphaType.Premul, sourceColor ?? srgb));
        var result = codec.GetPixels(decoded.Info, decoded.GetPixels());
        token.ThrowIfCancellationRequested();
        if (result != SKCodecResult.Success) return new(ThumbnailGenerationStatus.InvalidOutput, $"Image decoding failed ({result}).");
        var swap = codec.EncodedOrigin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var w = swap ? decoded.Height : decoded.Width; var h = swap ? decoded.Width : decoded.Height;
        var fit = Math.Min(1d, 512d / Math.Max(w, h));
        using var output = new SKBitmap(new SKImageInfo(Math.Max(1, (int)Math.Round(w * fit)), Math.Max(1, (int)Math.Round(h * fit)), SKColorType.Rgba8888, SKAlphaType.Opaque, srgb));
        using (var canvas = new SKCanvas(output))
        {
            // Alpha is explicitly composited on the Browser's near-black image well before JPEG encoding.
            canvas.Clear(new SKColor(5, 7, 10)); canvas.Scale((float)fit);
            switch (codec.EncodedOrigin)
            {
                case SKEncodedOrigin.TopRight: canvas.Translate(w, 0); canvas.Scale(-1, 1); break;
                case SKEncodedOrigin.BottomRight: canvas.Translate(w, h); canvas.RotateDegrees(180); break;
                case SKEncodedOrigin.BottomLeft: canvas.Translate(0, h); canvas.Scale(1, -1); break;
                case SKEncodedOrigin.LeftTop: canvas.RotateDegrees(90); canvas.Scale(1, -1); break;
                case SKEncodedOrigin.RightTop: canvas.Translate(w, 0); canvas.RotateDegrees(90); break;
                case SKEncodedOrigin.RightBottom: canvas.Translate(w, h); canvas.RotateDegrees(90); canvas.Scale(-1, 1); break;
                case SKEncodedOrigin.LeftBottom: canvas.Translate(0, h); canvas.RotateDegrees(-90); break;
            }
            using var paint = new SKPaint { FilterQuality = SKFilterQuality.High }; canvas.DrawBitmap(decoded, 0, 0, paint);
        }
        foreach (var path in paths)
        {
            token.ThrowIfCancellationRequested();
            var lut = CubeLutData.Load(path);
            for (int y = 0; y < output.Height; y++)
            {
                token.ThrowIfCancellationRequested();
                for (int x = 0; x < output.Width; x++)
                {
                    var p = output.GetPixel(x, y); var v = Sample(lut, new(p.Red / 255f, p.Green / 255f, p.Blue / 255f));
                    output.SetPixel(x, y, new SKColor(Byte(v.X), Byte(v.Y), Byte(v.Z)));
                }
            }
        }
        token.ThrowIfCancellationRequested();
        using var image = SKImage.FromBitmap(output); using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, 85);
        using var stream = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        encoded.SaveTo(stream); stream.Flush(true);
        return new(ThumbnailGenerationStatus.Succeeded);
    }
    private static byte Byte(float x) => (byte)Math.Clamp((int)Math.Round(x * 255), 0, 255);
    private static System.Numerics.Vector3 Sample(CubeLutData lut, System.Numerics.Vector3 value)
    {
        var v = System.Numerics.Vector3.Clamp((value - lut.DomainMin) / (lut.DomainMax - lut.DomainMin), System.Numerics.Vector3.Zero, System.Numerics.Vector3.One) * (lut.Size - 1);
        var x = (int)v.X; var y = (int)v.Y; var z = (int)v.Z; var x1 = Math.Min(x + 1, lut.Size - 1); var y1 = Math.Min(y + 1, lut.Size - 1); var z1 = Math.Min(z + 1, lut.Size - 1);
        System.Numerics.Vector3 At(int r, int g, int b) { var i = ((b * lut.Size + g) * lut.Size + r) * 4; return new(lut.Samples[i], lut.Samples[i + 1], lut.Samples[i + 2]); }
        var a = System.Numerics.Vector3.Lerp(At(x, y, z), At(x1, y, z), v.X - x); var b = System.Numerics.Vector3.Lerp(At(x, y1, z), At(x1, y1, z), v.X - x);
        var c = System.Numerics.Vector3.Lerp(At(x, y, z1), At(x1, y, z1), v.X - x); var d = System.Numerics.Vector3.Lerp(At(x, y1, z1), At(x1, y1, z1), v.X - x);
        return System.Numerics.Vector3.Lerp(System.Numerics.Vector3.Lerp(a, b, v.Y - y), System.Numerics.Vector3.Lerp(c, d, v.Y - y), v.Z - z);
    }
}
