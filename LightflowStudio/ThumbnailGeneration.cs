using System.Globalization;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LightflowStudio;

internal sealed class CompositeThumbnailRenderer(IImageThumbnailRenderer images, IVideoThumbnailRenderer videos)
    : IThumbnailRenderer
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".png", ".tif", ".tiff", ".bmp", ".gif", ".wdp", ".jxr" };

    public Task<ThumbnailRenderResult> RenderAsync(string sourcePath, string mediaType, TimeSpan videoPosition,
        string destinationPath, CancellationToken cancellationToken = default)
    {
        if (string.Equals(mediaType, "image", StringComparison.OrdinalIgnoreCase) ||
            ImageExtensions.Contains(Path.GetExtension(sourcePath)))
            return images.RenderAsync(sourcePath, destinationPath, cancellationToken);
        if (string.Equals(mediaType, "video", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(mediaType, "unknown", StringComparison.OrdinalIgnoreCase))
            return videos.RenderAsync(sourcePath, videoPosition, destinationPath, cancellationToken);
        return Task.FromResult(new ThumbnailRenderResult(ThumbnailGenerationStatus.Unsupported,
            "The asset is not a supported image or video."));
    }

    public Task<ThumbnailRenderResult> RenderAsync(string sourcePath, string mediaType, TimeSpan videoPosition,
        string destinationPath, ThumbnailColorRender color, CancellationToken cancellationToken = default)
    {
        if (string.Equals(mediaType, "video", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(mediaType, "unknown", StringComparison.OrdinalIgnoreCase))
            return videos.RenderAsync(sourcePath, videoPosition, destinationPath, color.OrderedLutPaths, cancellationToken);
        return RenderAsync(sourcePath, mediaType, videoPosition, destinationPath, cancellationToken);
    }
}

internal interface IImageThumbnailRenderer
{
    Task<ThumbnailRenderResult> RenderAsync(string sourcePath, string destinationPath,
        CancellationToken cancellationToken = default);
}

internal sealed class WicImageThumbnailRenderer(int maximumPixelDimension = 512) : IImageThumbnailRenderer
{
    public Task<ThumbnailRenderResult> RenderAsync(string sourcePath, string destinationPath,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => Render(sourcePath, destinationPath, cancellationToken), cancellationToken);

    private ThumbnailRenderResult Render(string sourcePath, string destinationPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 64 * 1024, FileOptions.SequentialScan);
            var decoder = BitmapDecoder.Create(source, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            if (decoder.Frames.Count == 0)
                return new(ThumbnailGenerationStatus.InvalidOutput, "The image contains no decodable frames.");
            cancellationToken.ThrowIfCancellationRequested();
            BitmapSource bitmap = decoder.Frames[0];
            bitmap = ApplyOrientation(bitmap, ReadOrientation(decoder.Frames[0].Metadata as BitmapMetadata));
            var scale = Math.Min(1d, Math.Min((double)maximumPixelDimension / bitmap.PixelWidth,
                (double)maximumPixelDimension / bitmap.PixelHeight));
            if (scale < 1d) bitmap = new TransformedBitmap(bitmap, new ScaleTransform(scale, scale));
            cancellationToken.ThrowIfCancellationRequested();
            var encoder = new JpegBitmapEncoder { QualityLevel = 85 };
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var output = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 64 * 1024, FileOptions.WriteThrough);
            encoder.Save(output);
            output.Flush(flushToDisk: true);
            return new(ThumbnailGenerationStatus.Succeeded);
        }
        catch (FileFormatException exception)
        {
            return new(ThumbnailGenerationStatus.InvalidOutput, $"The image is malformed: {exception.Message}");
        }
        catch (NotSupportedException exception)
        {
            return new(ThumbnailGenerationStatus.Unsupported, $"The image format is unsupported: {exception.Message}");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new(ThumbnailGenerationStatus.Failed, $"The image Preview could not be generated: {exception.Message}");
        }
    }

    /// <summary>Internal (not private) so <see cref="PlayerViewerHost"/> can reuse the identical EXIF-orientation read for its full-resolution Viewer decode rather than a second copy of this parsing.</summary>
    internal static int ReadOrientation(BitmapMetadata? metadata)
    {
        try
        {
            if (metadata?.ContainsQuery("/app1/ifd/{ushort=274}") != true) return 1;
            return Convert.ToInt32(metadata.GetQuery("/app1/ifd/{ushort=274}"), CultureInfo.InvariantCulture);
        }
        catch (Exception exception) when (exception is NotSupportedException or ArgumentException or FormatException) { return 1; }
    }

    internal static BitmapSource ApplyOrientation(BitmapSource source, int orientation)
    {
        var transform = OrientationTransform(orientation);
        return transform is null ? source : new TransformedBitmap(source, transform);
    }

    internal static (int Width, int Height) DisplayDimensions(int width, int height, int orientation)
    {
        var bounds = new System.Windows.Rect(0, 0, width, height);
        bounds = OrientationTransform(orientation)?.TransformBounds(bounds) ?? bounds;
        return ((int)Math.Round(bounds.Width), (int)Math.Round(bounds.Height));
    }

    private static Transform? OrientationTransform(int orientation) => orientation switch
        {
            2 => new ScaleTransform(-1, 1),
            3 => new RotateTransform(180),
            4 => new ScaleTransform(1, -1),
            5 => Group(new ScaleTransform(-1, 1), new RotateTransform(90)),
            6 => new RotateTransform(90),
            7 => Group(new ScaleTransform(-1, 1), new RotateTransform(270)),
            8 => new RotateTransform(270),
            _ => null
        };

    private static TransformGroup Group(params Transform[] transforms)
    {
        var group = new TransformGroup();
        foreach (var transform in transforms) group.Children.Add(transform);
        return group;
    }
}

internal interface IVideoThumbnailRenderer
{
    Task<ThumbnailRenderResult> RenderAsync(string sourcePath, TimeSpan position, string destinationPath,
        CancellationToken cancellationToken = default);
    Task<ThumbnailRenderResult> RenderAsync(string sourcePath, TimeSpan position, string destinationPath,
        IReadOnlyList<string> orderedLutPaths, CancellationToken cancellationToken = default) =>
        RenderAsync(sourcePath, position, destinationPath, cancellationToken);
}

internal sealed class FfmpegVideoThumbnailRenderer(string? executable, IProbeProcessRunner runner,
    int maximumPixelDimension = 512) : IVideoThumbnailRenderer
{
    public async Task<ThumbnailRenderResult> RenderAsync(string sourcePath, TimeSpan position, string destinationPath,
        CancellationToken cancellationToken = default)
        => await RenderAsync(sourcePath, position, destinationPath, [], cancellationToken).ConfigureAwait(false);

    public async Task<ThumbnailRenderResult> RenderAsync(string sourcePath, TimeSpan position, string destinationPath,
        IReadOnlyList<string> orderedLutPaths, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
            return new(ThumbnailGenerationStatus.GeneratorUnavailable, "FFmpeg is unavailable.");
        try
        {
            var result = await runner.RunAsync(executable,
                FfmpegCommandBuilder.ExtractThumbnail(sourcePath, position, maximumPixelDimension, destinationPath, orderedLutPaths),
                cancellationToken).ConfigureAwait(false);
            return result.ExitCode == 0
                ? new(ThumbnailGenerationStatus.Succeeded)
                : new(ThumbnailGenerationStatus.InvalidOutput,
                    result.StandardError, PreviewFailure.ClassifyDecoder(result.StandardError));
        }
        catch (OperationCanceledException) { throw; }
        catch (TimeoutException exception)
        {
            return new(ThumbnailGenerationStatus.Failed, exception.Message, PreviewFailureReason.TimedOut);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException
            or System.ComponentModel.Win32Exception)
        {
            return new(ThumbnailGenerationStatus.GeneratorUnavailable,
                $"FFmpeg could not be started: {exception.Message}");
        }
    }
}

internal sealed class ThumbnailGenerationService : SharedThumbnailGenerationService
{
    public ThumbnailGenerationService(IMediaAssetService assets, IPreviewStoreService previews,
        ILightflowStorageLocations locations, IThumbnailRenderer renderer,
        IDerivedMediaMetadataService? metadata = null, int maximumConcurrency = 2,
        IDisposable? ownedDependency = null, IPreviewOperationCoordinator? operations = null,
        IAssetColorStore? colors = null, ILutLibraryCache? lutCache = null,
        IThumbnailGenerationActivity? activity = null, IPreferredPreviewFrameStore? preferredFrames = null, TimeProvider? time = null)
        : base(assets, previews, locations, renderer, metadata, maximumConcurrency, ownedDependency, operations,
            new WindowsThumbnailColorSource(colors,lutCache), IsValidThumbnail,
            message => new ActivityLogFile(locations.ActivityLogPath).TryAppend(message), activity, preferredFrames,time,ApplicationDataProfile.GuardAccess) { }

    internal static bool IsValidThumbnail(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.Read, 64 * 1024, FileOptions.SequentialScan);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            return decoder.Frames.Count > 0 && decoder.Frames[0].PixelWidth > 0 && decoder.Frames[0].PixelHeight > 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FileFormatException
            or NotSupportedException)
        { return false; }
    }

}

internal sealed class WindowsThumbnailColorSource(IAssetColorStore? colors, ILutLibraryCache? luts) : IThumbnailColorSource
{
    public Task<ThumbnailColorRender> ResolveAsync(Guid id, CancellationToken token) => DerivedFrameColor.ResolveAsync(colors,luts,id,token);
    public async Task<string> IdentityAsync(Guid id, CancellationToken token)
    {
        if (colors is null) return PreviewVisualIdentity.Original;
        var intent = await colors.GetAsync(id,token).ConfigureAwait(false);
        return intent.HasColor ? intent.ColorIdentity : PreviewVisualIdentity.Original;
    }
}

internal static class ThumbnailGenerationFactory
{
    public static IThumbnailGenerationService Create(IMediaAssetService assets, IPreviewStoreService previews,
        ILightflowStorageLocations locations, AppSettings settings, string? applicationDirectory = null,
        int maximumConcurrency = 2, IPreviewOperationCoordinator? operations = null)
        => Create(assets, previews, locations, settings, applicationDirectory, maximumConcurrency, operations, null, null, null, null);

    public static IThumbnailGenerationService Create(IMediaAssetService assets, IPreviewStoreService previews,
        ILightflowStorageLocations locations, AppSettings settings, string? applicationDirectory,
        int maximumConcurrency, IPreviewOperationCoordinator? operations, IAssetColorStore? colors,
        ILutLibraryCache? lutCache, IThumbnailGenerationActivity? activity,
        IPreferredPreviewFrameStore? preferredFrames = null)
    {
        applicationDirectory ??= AppContext.BaseDirectory;
        var configuredDirectory = string.IsNullOrWhiteSpace(settings.FfmpegPath)
            ? null : Path.GetDirectoryName(settings.FfmpegPath);
        var configuredFfmpeg = configuredDirectory is null ? null : Path.Combine(configuredDirectory, "ffmpeg.exe");
        var ffmpeg = ExecutableLocator.Find("ffmpeg.exe", Path.Combine(applicationDirectory, "ffmpeg", "bin", "ffmpeg.exe"),
            configured: configuredFfmpeg ?? settings.FfmpegPath);
        // Thumbnail generation holds the shared Preview lease across its complete lifecycle, including this
        // internal metadata lookup. Giving the nested service the same coordinator could deadlock behind a
        // waiting maintenance writer while the outer thumbnail operation still owns the shared resource.
        var metadata = DerivedMediaMetadataFactory.Create(assets, previews, settings, applicationDirectory,
            maximumConcurrency);
        var renderer = new CompositeThumbnailRenderer(new WicImageThumbnailRenderer(),
            new FfmpegVideoThumbnailRenderer(ffmpeg, new ProbeProcessRunner()));
        return new ThumbnailGenerationService(assets, previews, locations, renderer, metadata,
            maximumConcurrency, metadata, operations, colors, lutCache, activity, preferredFrames);
    }
}
