using System.Windows.Media.Imaging;
using LightflowStudio;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class FrameScreengrabTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"LightflowStudio-Screengrab-{Guid.NewGuid():N}");

    [Fact]
    public async Task SaveAsync_WritesNativeDimensionsAndNeverOverwritesSamePosition()
    {
        var service = new FrameScreengrabService(() => _folder);
        var frame = new MediaDecodedFrame(new(TimeSpan.FromTicks(12_345_678)), 2, 1, 8,
            [0, 0, 255, 255, 0, 255, 0, 255]);

        var first = await service.SaveAsync(@"D:\Media\My Clip.mp4", frame);
        var second = await service.SaveAsync(@"D:\Media\My Clip.mp4", frame);

        Assert.NotEqual(first.Path, second.Path);
        Assert.EndsWith("-001.png", second.Path);
        Assert.Equal((2, 1), (first.Width, first.Height));
        using var stream = File.OpenRead(first.Path);
        var decoded = new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
        Assert.Equal((2, 1), (decoded.PixelWidth, decoded.PixelHeight));
        var converted = new FormatConvertedBitmap(decoded, System.Windows.Media.PixelFormats.Bgra32, null, 0);
        var pixels = new byte[8];
        converted.CopyPixels(pixels, 8, 0);
        Assert.Equal(frame.BgraPixels, pixels);
    }

    [Fact]
    public void BuildFileStem_UsesSourceNameAndOneReadableDisplayedTimestamp()
    {
        var position = TimeSpan.FromTicks(12_345_678);

        var stem = FrameScreengrabService.BuildFileStem(@"D:\Media\My Clip.mp4", position);

        Assert.Equal("My Clip_00-00-01.234", stem);
        Assert.DoesNotContain("_t", stem);
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }
}
