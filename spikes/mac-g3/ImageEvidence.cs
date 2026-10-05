using System.Security.Cryptography;
using System.Text.Json;
using SkiaSharp;

namespace Lightflow.G3;

internal static class ImageEvidence
{
    internal static void CreateFixtures(string root)
    {
        Directory.CreateDirectory(root);
        for (var i = 0; i < 32; i++)
        {
            var path = Path.Combine(root, $"thumb-{i:D2}.png");
            if (File.Exists(path)) continue;
            using var bitmap = new SKBitmap(320, 200);
            using var canvas = new SKCanvas(bitmap);
            canvas.Clear(new SKColor((byte)(i * 7), (byte)(80 + i * 3), (byte)(170 - i * 3)));
            using var paint = new SKPaint { Color = SKColors.Orange };
            canvas.DrawRect(20 + i * 3, 20, 60, 100, paint);
            using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
            File.WriteAllBytes(path, data.ToArray());
        }
    }

    internal static object Run(string root)
    {
        CreateFixtures(root);
        using var source = new SKBitmap(3, 2);
        var colors = new[] { SKColors.Red, SKColors.Green, SKColors.Blue, SKColors.Yellow, SKColors.Magenta, SKColors.Cyan };
        for (var i = 0; i < colors.Length; i++) source.SetPixel(i % 3, i / 3, colors[i]);
        var results = new List<object>();
        foreach (var format in new[] { SKEncodedImageFormat.Png, SKEncodedImageFormat.Jpeg, SKEncodedImageFormat.Webp })
        {
            using var bytes = source.Encode(format, 100);
            var path = Path.Combine(root, $"vector.{format.ToString().ToLowerInvariant()}");
            File.WriteAllBytes(path, bytes.ToArray());
            using var codec = SKCodec.Create(path);
            using var bitmap = SKBitmap.Decode(codec);
            results.Add(new { test = "decode", format = format.ToString(), width = bitmap.Width, height = bitmap.Height,
                alpha = bitmap.AlphaType.ToString(), sha256 = Convert.ToHexString(SHA256.HashData(bytes.ToArray())) });
        }
        // Tiny uncompressed indexed GIF fixture; explicit known bytes, no external corpus.
        var gif = Convert.FromBase64String("R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7");
        File.WriteAllBytes(Path.Combine(root, "vector.gif"), gif);
        using (var codec = SKCodec.Create(new MemoryStream(gif)))
            results.Add(new { test = "gif", decoded = codec is not null, frames = codec?.FrameCount });
        foreach (var (name, bytes) in NativeGapFixtures())
        {
            File.WriteAllBytes(Path.Combine(root, name), bytes);
            using var codec = SKCodec.Create(new MemoryStream(bytes));
            results.Add(new { test = "format-gap", fixture = name, decoded = codec is not null,
                width = codec?.Info.Width, height = codec?.Info.Height,
                sha256 = Convert.ToHexString(SHA256.HashData(bytes)) });
        }
        using var jpeg = source.Encode(SKEncodedImageFormat.Jpeg, 100);
        // Orientation metadata read and a discrete pixel-coordinate transform are tested separately
        // from JPEG's lossy pixel values. Expected arrays are literal independent vectors.
        int[][] expected = [ [0,1,2,3,4,5], [2,1,0,5,4,3], [5,4,3,2,1,0], [3,4,5,0,1,2],
            [0,3,1,4,2,5], [3,0,4,1,5,2], [5,2,4,1,3,0], [2,5,1,4,0,3] ];
        for (var orientation = 1; orientation <= 8; orientation++)
        {
            // Little-endian TIFF IFD with a single SHORT orientation tag inside EXIF APP1.
            byte[] payload = [69,120,105,102,0,0,73,73,42,0,8,0,0,0,1,0,18,1,3,0,1,0,0,0,(byte)orientation,0,0,0,0,0,0,0];
            var raw = jpeg.ToArray();
            byte[] tagged = [.. raw[..2], 255,225,0,(byte)(payload.Length + 2), .. payload, .. raw[2..]];
            var path = Path.Combine(root, $"orientation-{orientation}.jpg"); File.WriteAllBytes(path, tagged);
            using var codec = SKCodec.Create(path);
            if ((int)codec.EncodedOrigin != orientation) throw new InvalidOperationException("EXIF metadata mismatch");
            using var transformed = Orient(source, codec.EncodedOrigin);
            var actual = Enumerable.Range(0, transformed.Width * transformed.Height)
                .Select(i => Array.IndexOf(colors, transformed.GetPixel(i % transformed.Width, i / transformed.Width))).ToArray();
            if (!actual.SequenceEqual(expected[orientation - 1])) throw new InvalidOperationException($"Orientation {orientation} mismatch");
            results.Add(new { test = "orientation", orientation, width = transformed.Width, height = transformed.Height,
                pixels = actual, sha256 = Convert.ToHexString(SHA256.HashData(tagged)), passed = true });
        }
        using (var linear = SKColorSpace.CreateSrgbLinear())
        using (var target = SKColorSpace.CreateSrgb())
        using (var input = new SKBitmap(new SKImageInfo(1, 1, SKColorType.Rgba8888, SKAlphaType.Unpremul, linear)))
        {
            // SKColor-based SetPixel is itself color-aware. Write a literal linear-light
            // sample to the RGBA buffer so the expected sRGB conversion is independent.
            System.Runtime.InteropServices.Marshal.Copy(new byte[] { 128,128,128,128 }, 0, input.GetPixels(), 4);
            using var encoded = input.Encode(SKEncodedImageFormat.Png, 100);
            File.WriteAllBytes(Path.Combine(root, "linear-profile-alpha.png"), encoded.ToArray());
            using var codec = SKCodec.Create(new MemoryStream(encoded.ToArray()));
            using var output = new SKBitmap(new SKImageInfo(1, 1, SKColorType.Rgba8888, SKAlphaType.Unpremul, target));
            var result = codec.GetPixels(output.Info, output.GetPixels());
            var pixel = output.GetPixel(0, 0);
            var chunks = new List<string>();
            var png = encoded.ToArray();
            for (var offset = 8; offset + 12 <= png.Length;)
            {
                var length = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(offset, 4));
                chunks.Add(System.Text.Encoding.ASCII.GetString(png, offset + 4, 4));
                offset += length + 12;
            }
            results.Add(new { test = "linear-profile-to-srgb", red = pixel.Red, alpha = pixel.Alpha, expectedRed = 188,
                sourceIsSrgb = codec.Info.ColorSpace?.IsSrgb,
                pngChunks = chunks,
                passed = result == SKCodecResult.Success && Math.Abs(pixel.Red - 188) <= 1 && pixel.Alpha == 128,
                limitation = "A failed encoded round-trip may be fixture profile loss; inspect PNG chunks before attributing decoder behavior." });
            using var converted = new SKBitmap(new SKImageInfo(1, 1, SKColorType.Rgba8888, SKAlphaType.Unpremul, target));
            using var canvas = new SKCanvas(converted);
            canvas.Clear(SKColors.Transparent);
            canvas.DrawBitmap(input, 0, 0);
            var rendered = converted.GetPixel(0, 0);
            results.Add(new { test = "linear-to-srgb-canvas", red = rendered.Red, alpha = rendered.Alpha,
                expectedRed = 188, passed = Math.Abs(rendered.Red - 188) <= 1 && rendered.Alpha == 128 });
        }
        using (var invalid = SKCodec.Create(new MemoryStream(new byte[] { 0,1,2,3 })))
            results.Add(new { test = "malformed", rejected = invalid is null });
        var lockPath = Path.Combine(root, "lifetime.png");
        File.Copy(Path.Combine(root, "thumb-00.png"), lockPath, true);
        using (var decoded = SKBitmap.Decode(lockPath))
        {
            File.Delete(lockPath);
            results.Add(new { test = "source-file-release", deleted = !File.Exists(lockPath), pixelStillReadable = decoded.GetPixel(0, 0).Alpha == 255 });
        }
        return results;
    }

    private static IEnumerable<(string Name, byte[] Bytes)> NativeGapFixtures()
    {
        using var bmp = new MemoryStream();
        using (var writer = new BinaryWriter(bmp, System.Text.Encoding.ASCII, true))
        {
            writer.Write((ushort)0x4D42); writer.Write(62); writer.Write(0); writer.Write(54);
            writer.Write(40); writer.Write(2); writer.Write(1); writer.Write((ushort)1); writer.Write((ushort)24);
            writer.Write(0); writer.Write(8); writer.Write(2835); writer.Write(2835); writer.Write(0); writer.Write(0);
            writer.Write(new byte[] { 0,0,255,0,255,0,0,0 });
        }
        yield return ("vector.bmp", bmp.ToArray());
        using var tiff = new MemoryStream();
        using (var writer = new BinaryWriter(tiff, System.Text.Encoding.ASCII, true))
        {
            writer.Write((ushort)0x4949); writer.Write((ushort)42); writer.Write(8); writer.Write((ushort)9);
            foreach (var (tag, type, count, value) in new[] { (256,3,1,2), (257,3,1,1), (258,3,3,122),
                (259,3,1,1), (262,3,1,2), (273,4,1,128), (277,3,1,3), (278,4,1,1), (279,4,1,6) })
            { writer.Write((ushort)tag); writer.Write((ushort)type); writer.Write(count); writer.Write(value); }
            writer.Write(0); writer.Write((ushort)8); writer.Write((ushort)8); writer.Write((ushort)8);
            writer.Write(new byte[] { 255,0,0,0,255,0 });
        }
        yield return ("vector.tiff", tiff.ToArray());
    }

    internal static SKBitmap Orient(SKBitmap source, SKEncodedOrigin origin)
    {
        var swaps = (int)origin >= 5;
        var output = new SKBitmap(swaps ? source.Height : source.Width, swaps ? source.Width : source.Height);
        for (var y = 0; y < source.Height; y++) for (var x = 0; x < source.Width; x++)
        {
            var (dx, dy) = (int)origin switch
            {
                2 => (source.Width - 1 - x, y), 3 => (source.Width - 1 - x, source.Height - 1 - y),
                4 => (x, source.Height - 1 - y), 5 => (y, x), 6 => (source.Height - 1 - y, x),
                7 => (source.Height - 1 - y, source.Width - 1 - x), 8 => (y, source.Width - 1 - x), _ => (x, y)
            };
            output.SetPixel(dx, dy, source.GetPixel(x, y));
        }
        return output;
    }
}
