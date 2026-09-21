using System.Diagnostics;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Xunit;

namespace LightflowStudio.Tests;

[Collection("STA dispatcher tests")]
public sealed class VideoRotationPixelTests
{
    [Fact]
    public async Task CachedPresentation_ReloadsLowerRevisionAfterRecovery_AndKeepsOriginalPixels()
    {
        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();
            var store = new TestRotations(new(90));
            var original = BitmapSource.Create(4, 2, 96, 96, PixelFormats.Bgra32, null, new byte[32], 16);
            original.Freeze();
            var preview = new OrientedPreviewImage { AssetId = store.Id, Source = original };
            OrientedPreviewImage.SetStore(preview, store);
            var window = new Window { Content = preview, Width = 100, Height = 100, Left = -32000, ShowActivated = false, ShowInTaskbar = false };
            window.Show();
            try
            {
                await Task.Delay(20);
                Assert.Equal(2, ((BitmapSource)preview.Source).PixelWidth);
                store.Update(new(180), 2);
                await Task.Delay(20);
                Assert.Equal(4, ((BitmapSource)preview.Source).PixelWidth);
                store.Restore(new(90), 1);
                await Task.Delay(20);
                Assert.Equal(2, ((BitmapSource)preview.Source).PixelWidth);
                store.Update(default, 2);
                await Task.Delay(20);
                Assert.Same(original, preview.Source);
            }
            finally { window.Content = null; window.Close(); }
        });
    }

    [Fact]
    public async Task SourceMatrixAndAuthoredRotation_AgreeAcrossPreviewPlayerAndBakedExport()
    {
        var root = Path.Combine(Path.GetTempPath(), "rotation-pixels-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var dependencies = PlaybackDependencyLocator.FindSharedLibraries()!;
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "Directory.Build.props"))) repository = repository.Parent;
        var ffmpeg = Path.Combine(repository!.FullName, "artifacts", "ffmpeg", "bin", "ffmpeg.exe");
        var original = Path.Combine(root, "original.mp4");
        Run(ffmpeg, ["-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi", "-i",
            "color=black:size=160x96:rate=25:duration=2,drawbox=x=0:y=0:w=80:h=48:color=red:t=fill,drawbox=x=80:y=0:w=80:h=48:color=lime:t=fill,drawbox=x=0:y=48:w=80:h=48:color=blue:t=fill,drawbox=x=80:y=48:w=80:h=48:color=yellow:t=fill",
            "-c:v", "libopenh264", "-pix_fmt", "yuv420p", original]);
        try
        {
            for (var sourceDegrees = 0; sourceDegrees < 360; sourceDegrees += 90)
            {
                var source = Path.Combine(root, $"source-{sourceDegrees}.mp4");
                Run(ffmpeg, ["-hide_banner", "-loglevel", "error", "-y", "-display_rotation", (-sourceDegrees).ToString(), "-i", original, "-c", "copy", source]);
                var hash = SHA256.HashData(File.ReadAllBytes(source));
                var previewPath = Path.Combine(root, $"preview-{sourceDegrees}.jpg");
                Run(ffmpeg, FfmpegCommandBuilder.ExtractThumbnail(source, TimeSpan.Zero, 160, previewPath));
                await StaDispatcher.RunAsync(async () =>
                {
                    TestWpfApplication.EnsureLoaded();
                    await using var backend = new FlyleafPlaybackBackend(dependencies);
                    await using var service = new MediaPlaybackService(backend);
                    await service.OpenAsync(source);
                    Assert.Equal(sourceDegrees, service.SourceInfo!.SourceRotation.Degrees);
                    using var presentation = service.CreatePresentation();
                    var window = new Window { Content = presentation.Surface, Width = 480, Height = 360,
                        Left = -32000, ShowActivated = false, ShowInTaskbar = false };
                    window.Show();
                    try
                    {
                        var timestamp = service.Snapshot.DisplayedTimestamp;
                        var seeks = backend.NativeSeekCount;
                        for (var adjustment = 0; adjustment < 360; adjustment += 90)
                        {
                            var rotation = new VideoRotation(adjustment);
                            var effective = new VideoRotation(sourceDegrees).Compose(rotation);
                            service.SetVideoRotation(rotation);
                            await Task.Delay(100);
                            var frame = await presentation.CaptureFrameAsync();
                            Assert.Equal(timestamp, frame.Timestamp);
                            Assert.Equal(seeks, backend.NativeSeekCount);
                            Assert.Equal(effective.Dimensions(160, 96), (frame.Width, frame.Height));
                            AssertCorners(frame.BgraPixels, frame.Width, frame.Height, frame.Stride, effective);

                            var store = new TestRotations(rotation);
                            var preview = new OrientedPreviewImage { AssetId = store.Id, Source = PlayerViewerHost.DecodeImage(previewPath) };
                            OrientedPreviewImage.SetStore(preview, store);
                            preview.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
                            await Task.Delay(20);
                            AssertBitmap((BitmapSource)preview.Source, effective);
                            preview.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));

                            var output = Path.Combine(root, $"export-{sourceDegrees}-{adjustment}.mp4");
                            // Exercise the production filter chain with a CPU encoder so this contract does
                            // not require an NVIDIA device on CI. Production encoder selection is tested separately.
                            var encode = FfmpegCommandBuilder.Encode(source, output, null, RecoveryStrategy.Normal,
                                OutputResolution.Source, rotation: rotation);
                            var encodeArgs = new List<string> { "-hide_banner", "-loglevel", "error", "-y", "-i", source };
                            if (encode.IndexOf("-vf") is var filterIndex && filterIndex >= 0)
                                encodeArgs.AddRange(["-vf", encode[filterIndex + 1]]);
                            encodeArgs.AddRange(["-c:v", "libopenh264", output]);
                            Run(ffmpeg, encodeArgs);
                            var outputFrame = output + ".png";
                            Run(ffmpeg, ["-hide_banner", "-loglevel", "error", "-y", "-i", output, "-frames:v", "1", outputFrame]);
                            var bitmap = PlayerViewerHost.DecodeImage(outputFrame);
                            Assert.Equal(effective.Dimensions(160, 96), (bitmap.PixelWidth, bitmap.PixelHeight));
                            AssertBitmap(bitmap, effective);
                        }
                    }
                    finally { window.Content = null; window.Close(); }
                });
                Assert.Equal(hash, SHA256.HashData(File.ReadAllBytes(source)));
            }
        }
        finally { Directory.Delete(root, true); }
    }

    private static void AssertBitmap(BitmapSource source, VideoRotation effective)
    {
        var bitmap = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var stride = bitmap.PixelWidth * 4;
        var pixels = new byte[stride * bitmap.PixelHeight];
        bitmap.CopyPixels(pixels, stride, 0);
        AssertCorners(pixels, bitmap.PixelWidth, bitmap.PixelHeight, stride, effective);
    }
    private static void AssertCorners(byte[] pixels, int width, int height, int stride, VideoRotation effective)
    {
        string Color(int x, int y)
        {
            var at = y * stride + x * 4;
            var b = pixels[at]; var g = pixels[at + 1]; var r = pixels[at + 2];
            return r > 150 && g > 150 ? "Y" : r > g && r > b ? "R" : g > b ? "G" : "B";
        }
        var actual = Color(width / 4, height / 4) + Color(width * 3 / 4, height / 4)
            + Color(width / 4, height * 3 / 4) + Color(width * 3 / 4, height * 3 / 4);
        var expected = effective.Degrees switch { 0 => "RGBY", 90 => "BRYG", 180 => "YBGR", _ => "GYRB" };
        Assert.Equal(expected, actual);
    }
    private static void Run(string executable, IReadOnlyList<string> args)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var error = process.StandardError.ReadToEndAsync(); var output = process.StandardOutput.ReadToEndAsync();
        Assert.True(process.WaitForExit(30000), "FFmpeg did not finish within 30 seconds.");
        Assert.True(process.ExitCode == 0, error.GetAwaiter().GetResult());
        output.GetAwaiter().GetResult();
    }
    private sealed class TestRotations(VideoRotation rotation) : IAssetVideoRotationStore
    {
        public Guid Id { get; } = Guid.NewGuid();
        private VideoRotation _rotation = rotation;
        private long _revision = 1;
        public event EventHandler<IReadOnlyList<AssetVideoRotation>>? Changed;
        public event EventHandler? Invalidated;
        public void Update(VideoRotation value, long revision)
        { _rotation = value; _revision = revision; Changed?.Invoke(this, [new(Id, value, revision)]); }
        public void Restore(VideoRotation value, long revision)
        { _rotation = value; _revision = revision; Invalidated?.Invoke(this, EventArgs.Empty); }
        public Task<IReadOnlyDictionary<Guid, AssetVideoRotation>> GetAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<Guid, AssetVideoRotation>>(new Dictionary<Guid, AssetVideoRotation> { [Id] = new(Id, _rotation, _revision) });
        public Task RotateAsync(IReadOnlyDictionary<Guid, long> revisions, bool right, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
