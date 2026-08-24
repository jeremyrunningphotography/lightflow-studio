using System.Globalization;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LightflowStudio;

internal enum ThumbnailGenerationStatus
{
    Succeeded,
    Current,
    SourceChanged,
    AssetNotFound,
    RootUnavailable,
    SourceMissing,
    Unsupported,
    GeneratorUnavailable,
    InvalidOutput,
    Failed
}

internal enum ThumbnailPriority { Background, Normal, Visible }

internal sealed record ThumbnailRequest(Guid AssetId, bool ForceRefresh = false,
    ThumbnailPriority Priority = ThumbnailPriority.Normal);

internal sealed record ThumbnailGenerationResult(ThumbnailGenerationStatus Status,
    string? ThumbnailPath = null, string? Diagnostic = null)
{
    public bool Succeeded => Status is ThumbnailGenerationStatus.Succeeded or ThumbnailGenerationStatus.Current;
}

internal sealed record ThumbnailRenderResult(ThumbnailGenerationStatus Status, string? Diagnostic = null);
internal sealed record ThumbnailColorRender(string VisualIdentity, IReadOnlyList<string> OrderedLutPaths)
{
    public static ThumbnailColorRender Original { get; } = new(PreviewVisualIdentity.Original, []);
}

internal sealed record ThumbnailGenerationActivityChanged(Guid AssetId, bool IsGenerating);
internal interface IThumbnailGenerationActivity
{
    event EventHandler<ThumbnailGenerationActivityChanged>? Changed;
    IDisposable Begin(Guid assetId);
    bool IsGenerating(Guid assetId);
}

internal sealed class ThumbnailGenerationActivity : IThumbnailGenerationActivity
{
    private readonly object _sync = new();
    private readonly Dictionary<Guid, int> _active = [];
    public event EventHandler<ThumbnailGenerationActivityChanged>? Changed;
    public bool IsGenerating(Guid assetId) { lock (_sync) return _active.ContainsKey(assetId); }
    public IDisposable Begin(Guid assetId)
    {
        lock (_sync)
        {
            _active.TryGetValue(assetId, out var count);
            _active[assetId] = count + 1;
            if (count == 0) Changed?.Invoke(this, new(assetId, true));
        }
        return new Lease(this, assetId);
    }
    private void End(Guid assetId)
    {
        lock (_sync)
        {
            if (!_active.TryGetValue(assetId, out var count)) return;
            if (count > 1) _active[assetId] = count - 1;
            else { _active.Remove(assetId); Changed?.Invoke(this, new(assetId, false)); }
        }
    }
    private sealed class Lease(ThumbnailGenerationActivity owner, Guid assetId) : IDisposable
    {
        private ThumbnailGenerationActivity? _owner = owner;
        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.End(assetId);
    }
}

internal interface IThumbnailRenderer
{
    Task<ThumbnailRenderResult> RenderAsync(string sourcePath, string mediaType, TimeSpan videoPosition,
        string destinationPath, CancellationToken cancellationToken = default);
    Task<ThumbnailRenderResult> RenderAsync(string sourcePath, string mediaType, TimeSpan videoPosition,
        string destinationPath, ThumbnailColorRender color, CancellationToken cancellationToken = default) =>
        RenderAsync(sourcePath, mediaType, videoPosition, destinationPath, cancellationToken);
}

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
            return new(ThumbnailGenerationStatus.Failed, $"The image thumbnail could not be generated: {exception.Message}");
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
        Transform? transform = orientation switch
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
        return transform is null ? source : new TransformedBitmap(source, transform);
    }

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
                    result.StandardError.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim()
                        ?? "FFmpeg could not decode a representative video frame.");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException
            or System.ComponentModel.Win32Exception)
        {
            return new(ThumbnailGenerationStatus.GeneratorUnavailable,
                $"FFmpeg could not be started: {exception.Message}");
        }
    }
}

internal interface IThumbnailGenerationService : IDisposable
{
    Task<ThumbnailGenerationResult> GenerateAsync(ThumbnailRequest request,
        CancellationToken cancellationToken = default);
}

internal sealed class ThumbnailGenerationService : IThumbnailGenerationService
{
    internal const int CurrentGeneratorVersion = 1;
    private readonly IMediaAssetService _assets;
    private readonly IPreviewStoreService _previews;
    private readonly ILightflowStorageLocations _locations;
    private readonly IThumbnailRenderer _renderer;
    private readonly IDerivedMediaMetadataService? _metadata;
    private readonly IDisposable? _ownedDependency;
    private readonly PriorityAsyncGate _gate;
    private readonly IPreviewOperationCoordinator? _operations;
    private readonly IAssetColorStore? _colors;
    private readonly ILutLibraryCache? _lutCache;
    private readonly IThumbnailGenerationActivity? _activity;

    public ThumbnailGenerationService(IMediaAssetService assets, IPreviewStoreService previews,
        ILightflowStorageLocations locations, IThumbnailRenderer renderer,
        IDerivedMediaMetadataService? metadata = null, int maximumConcurrency = 2,
        IDisposable? ownedDependency = null, IPreviewOperationCoordinator? operations = null,
        IAssetColorStore? colors = null, ILutLibraryCache? lutCache = null,
        IThumbnailGenerationActivity? activity = null)
    {
        _assets = assets;
        _previews = previews;
        _locations = locations;
        _renderer = renderer;
        _metadata = metadata;
        _ownedDependency = ownedDependency;
        _gate = new(maximumConcurrency);
        _operations = operations;
        _colors = colors;
        _lutCache = lutCache;
        _activity = activity;
    }

    public async Task<ThumbnailGenerationResult> GenerateAsync(ThumbnailRequest request,
        CancellationToken cancellationToken = default)
    {
        using var operationLease = _operations is null ? null :
            await _operations.EnterOperationAsync(cancellationToken).ConfigureAwait(false);
        var observed = await _assets.ObserveAsync(request.AssetId, cancellationToken).ConfigureAwait(false);
        if (observed.Status == MediaAssetOperationStatus.NotFound)
            return new(ThumbnailGenerationStatus.AssetNotFound, Diagnostic: observed.Diagnostic);
        if (observed.Status == MediaAssetOperationStatus.RootUnavailable)
            return await RetainOfflineAsync(request.AssetId, PreviewSourceAvailability.Unavailable,
                ThumbnailGenerationStatus.RootUnavailable, observed.Diagnostic, cancellationToken).ConfigureAwait(false);
        if (observed.Status == MediaAssetOperationStatus.SourceMissing)
            return await RetainOfflineAsync(request.AssetId, PreviewSourceAvailability.Missing,
                ThumbnailGenerationStatus.SourceMissing, observed.Diagnostic, cancellationToken).ConfigureAwait(false);
        if (!observed.Succeeded || observed.Asset?.PhysicalPath is null || observed.Asset.Asset.Fingerprint is null)
            return new(ThumbnailGenerationStatus.Failed,
                Diagnostic: observed.Diagnostic ?? "The media source could not be observed.");

        var asset = observed.Asset.Asset;
        var source = SourceIdentity(asset);
        var preview = await _previews.ObserveSourceAsync(request.AssetId, source, cancellationToken).ConfigureAwait(false);
        ThumbnailColorRender color;
        try { color = await ResolveColorAsync(request.AssetId, cancellationToken).ConfigureAwait(false); }
        catch (Exception exception) when (exception is FileNotFoundException or InvalidDataException or KeyNotFoundException)
        {
            var failedIdentity = _colors is null ? PreviewVisualIdentity.Original
                : (await _colors.GetAsync(request.AssetId, cancellationToken).ConfigureAwait(false)).ColorIdentity;
            await RecordFailureAsync(request.AssetId, preview, failedIdentity, cancellationToken).ConfigureAwait(false);
            return new(ThumbnailGenerationStatus.Failed, ResolveExisting(preview.ThumbnailRelativePath), exception.Message);
        }
        if (!request.ForceRefresh && preview.ThumbnailState == PreviewComponentState.Current &&
            preview.ThumbnailGeneratorVersion == CurrentGeneratorVersion &&
            string.Equals(preview.ThumbnailVisualIdentity ?? PreviewVisualIdentity.Original, color.VisualIdentity, StringComparison.Ordinal) &&
            ResolveExisting(preview.ThumbnailRelativePath) is { } existing && IsValidThumbnail(existing))
            return new(ThumbnailGenerationStatus.Current, existing);

        using var activity = _activity?.Begin(request.AssetId);
        using var lease = await _gate.EnterAsync(request.Priority, cancellationToken).ConfigureAwait(false);
        var finalPath = _previews.GetArtifactPath(request.AssetId, PreviewArtifactKind.Thumbnail,
            CurrentGeneratorVersion, source, "jpg", color.VisualIdentity);
        Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);
        var temporaryPath = finalPath + $".{Guid.NewGuid():N}.lightflow";
        try
        {
            var position = await RepresentativePositionAsync(request.AssetId, asset.MediaType, cancellationToken)
                .ConfigureAwait(false);
            var rendered = await _renderer.RenderAsync(observed.Asset.PhysicalPath, asset.MediaType, position,
                temporaryPath, color, cancellationToken).ConfigureAwait(false);
            if (rendered.Status != ThumbnailGenerationStatus.Succeeded)
            {
                await RecordFailureAsync(request.AssetId, preview, color.VisualIdentity, cancellationToken).ConfigureAwait(false);
                return new(rendered.Status, ResolveExisting(preview.ThumbnailRelativePath), rendered.Diagnostic);
            }
            if (!IsValidThumbnail(temporaryPath))
            {
                await RecordFailureAsync(request.AssetId, preview, color.VisualIdentity, cancellationToken).ConfigureAwait(false);
                return new(ThumbnailGenerationStatus.InvalidOutput, ResolveExisting(preview.ThumbnailRelativePath),
                    "The generated thumbnail was not a valid image.");
            }

            var verified = await _assets.ObserveAsync(request.AssetId, cancellationToken).ConfigureAwait(false);
            if (verified.Status == MediaAssetOperationStatus.RootUnavailable)
                return await RetainOfflineAsync(request.AssetId, PreviewSourceAvailability.Unavailable,
                    ThumbnailGenerationStatus.RootUnavailable, verified.Diagnostic, cancellationToken).ConfigureAwait(false);
            if (verified.Status == MediaAssetOperationStatus.SourceMissing)
                return await RetainOfflineAsync(request.AssetId, PreviewSourceAvailability.Missing,
                    ThumbnailGenerationStatus.SourceMissing, verified.Diagnostic, cancellationToken).ConfigureAwait(false);
            if (verified.Status == MediaAssetOperationStatus.NotFound)
                return new(ThumbnailGenerationStatus.AssetNotFound, Diagnostic: verified.Diagnostic);
            if (!verified.Succeeded || verified.Asset?.Asset.Fingerprint is null)
                return new(ThumbnailGenerationStatus.Failed, ResolveExisting(preview.ThumbnailRelativePath),
                    verified.Diagnostic ?? "The source could not be verified after thumbnail generation.");

            var verifiedSource = SourceIdentity(verified.Asset.Asset);
            await _previews.ObserveSourceAsync(request.AssetId, verifiedSource, cancellationToken).ConfigureAwait(false);
            if (!SameSourceIdentity(source, verifiedSource))
                return new(ThumbnailGenerationStatus.SourceChanged, ResolveExisting(preview.ThumbnailRelativePath),
                    "The source changed while its thumbnail was being generated. Retry after the file is stable.");

            var verifiedColorIdentity = await CurrentVisualIdentityAsync(request.AssetId, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(color.VisualIdentity, verifiedColorIdentity, StringComparison.Ordinal))
                return new(ThumbnailGenerationStatus.SourceChanged, ResolveExisting(preview.ThumbnailRelativePath),
                    "Color changed while its thumbnail was being generated. Newer work will replace it.");

            File.Move(temporaryPath, finalPath, overwrite: true);
            if (!string.Equals(color.VisualIdentity,
                    await CurrentVisualIdentityAsync(request.AssetId, cancellationToken).ConfigureAwait(false),
                    StringComparison.Ordinal))
            {
                try { File.Delete(finalPath); } catch { }
                return new(ThumbnailGenerationStatus.SourceChanged, ResolveExisting(preview.ThumbnailRelativePath),
                    "Color changed before its thumbnail could be committed. Newer work will replace it.");
            }
            var relative = Path.GetRelativePath(_locations.PreviewsDirectory, finalPath).Replace('\\', '/');
            await _previews.SetArtifactAsync(request.AssetId, PreviewArtifactKind.Thumbnail,
                new(CurrentGeneratorVersion, PreviewComponentState.Current, relative, VisualIdentity: color.VisualIdentity), cancellationToken).ConfigureAwait(false);
            return new(ThumbnailGenerationStatus.Succeeded, finalPath);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            await RecordFailureAsync(request.AssetId, preview, color.VisualIdentity, CancellationToken.None).ConfigureAwait(false);
            return new(ThumbnailGenerationStatus.Failed, ResolveExisting(preview.ThumbnailRelativePath), exception.Message);
        }
        finally { try { File.Delete(temporaryPath); } catch { } }
    }

    private async Task<TimeSpan> RepresentativePositionAsync(Guid assetId, string mediaType,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(mediaType, "video", StringComparison.OrdinalIgnoreCase) || _metadata is null)
            return TimeSpan.FromSeconds(1);
        var result = await _metadata.ProbeAsync(assetId, cancellationToken: cancellationToken).ConfigureAwait(false);
        var duration = result.Metadata?.DurationSeconds;
        if (duration is null or <= 0) return TimeSpan.FromSeconds(1);
        var seconds = duration < 1 ? duration.Value / 2 : Math.Clamp(duration.Value * 0.1, 1, 30);
        return TimeSpan.FromSeconds(seconds);
    }

    private async Task RecordFailureAsync(Guid assetId, PreviewRecord preview, string? visualIdentity, CancellationToken cancellationToken) =>
        await _previews.SetArtifactAsync(assetId, PreviewArtifactKind.Thumbnail,
            new(CurrentGeneratorVersion, PreviewComponentState.Failed, preview.ThumbnailRelativePath, VisualIdentity: visualIdentity), cancellationToken)
            .ConfigureAwait(false);

    private async Task<ThumbnailColorRender> ResolveColorAsync(Guid assetId, CancellationToken cancellationToken)
    {
        if (_colors is null || _lutCache is null) return ThumbnailColorRender.Original;
        var intent = await _colors.GetAsync(assetId, cancellationToken).ConfigureAwait(false);
        if (!intent.HasColor) return ThumbnailColorRender.Original;
        var paths = new List<string>(2);
        if (intent.Camera is { } camera) paths.Add(_lutCache.ResolvePath(ColorLutStage.Camera, camera.LutId));
        if (intent.Creative is { } creative) paths.Add(_lutCache.ResolvePath(ColorLutStage.Creative, creative.LutId));
        return new(intent.ColorIdentity, paths);
    }

    private async Task<string> CurrentVisualIdentityAsync(Guid assetId, CancellationToken cancellationToken)
    {
        if (_colors is null) return PreviewVisualIdentity.Original;
        var intent = await _colors.GetAsync(assetId, cancellationToken).ConfigureAwait(false);
        return intent.HasColor ? intent.ColorIdentity : PreviewVisualIdentity.Original;
    }

    private async Task<ThumbnailGenerationResult> RetainOfflineAsync(Guid assetId,
        PreviewSourceAvailability availability, ThumbnailGenerationStatus status, string? diagnostic,
        CancellationToken cancellationToken)
    {
        var existing = await _previews.GetAsync(assetId, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
            await _previews.SetSourceAvailabilityAsync(assetId, availability, cancellationToken).ConfigureAwait(false);
        return new(status, ResolveExisting(existing?.ThumbnailRelativePath), diagnostic);
    }

    private string? ResolveExisting(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return null;
        try
        {
            var path = MediaPathSemantics.ResolveContained(_locations.PreviewsDirectory, relativePath);
            return File.Exists(path) ? path : null;
        }
        catch (ArgumentException) { return null; }
    }

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

    private static PreviewSourceIdentity SourceIdentity(MediaAsset asset) => new(asset.FileSizeBytes,
        asset.LastWriteUtcTicks, asset.Fingerprint!.Version, asset.Fingerprint.Value);
    private static bool SameSourceIdentity(PreviewSourceIdentity left, PreviewSourceIdentity right) =>
        left.FileSizeBytes == right.FileSizeBytes && left.LastWriteUtcTicks == right.LastWriteUtcTicks &&
        left.FingerprintVersion == right.FingerprintVersion &&
        string.Equals(left.Fingerprint, right.Fingerprint, StringComparison.OrdinalIgnoreCase);

    public void Dispose()
    {
        _gate.Dispose();
        _ownedDependency?.Dispose();
    }
}

internal static class ThumbnailGenerationFactory
{
    public static IThumbnailGenerationService Create(IMediaAssetService assets, IPreviewStoreService previews,
        ILightflowStorageLocations locations, AppSettings settings, string? applicationDirectory = null,
        int maximumConcurrency = 2, IPreviewOperationCoordinator? operations = null)
        => Create(assets, previews, locations, settings, applicationDirectory, maximumConcurrency, operations, null, null, null);

    public static IThumbnailGenerationService Create(IMediaAssetService assets, IPreviewStoreService previews,
        ILightflowStorageLocations locations, AppSettings settings, string? applicationDirectory,
        int maximumConcurrency, IPreviewOperationCoordinator? operations, IAssetColorStore? colors,
        ILutLibraryCache? lutCache, IThumbnailGenerationActivity? activity)
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
            maximumConcurrency, metadata, operations, colors, lutCache, activity);
    }
}

internal sealed class PriorityAsyncGate : IDisposable
{
    private readonly object _sync = new();
    private readonly int _maximum;
    private readonly Queue<Waiter> _visible = new();
    private readonly Queue<Waiter> _normal = new();
    private readonly Queue<Waiter> _background = new();
    private readonly Action? _beforeWaiterAssignment;
    private int _active;
    private bool _disposed;

    public PriorityAsyncGate(int maximum, Action? beforeWaiterAssignment = null)
    {
        if (maximum <= 0) throw new ArgumentOutOfRangeException(nameof(maximum));
        _maximum = maximum;
        _beforeWaiterAssignment = beforeWaiterAssignment;
    }

    public async Task<IDisposable> EnterAsync(ThumbnailPriority priority, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Waiter? waiter = null;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_active < _maximum) _active++;
            else
            {
                waiter = new(cancellationToken);
                Queue(priority).Enqueue(waiter);
            }
        }
        if (waiter is not null) await waiter.Task.ConfigureAwait(false);
        return new Lease(this);
    }

    private void Release()
    {
        lock (_sync)
        {
            _active--;
            while ((Dequeue(_visible) ?? Dequeue(_normal) ?? Dequeue(_background)) is { } next)
            {
                _beforeWaiterAssignment?.Invoke();
                if (!next.TryAssign()) continue;
                _active++;
                break;
            }
        }
    }

    private Queue<Waiter> Queue(ThumbnailPriority priority) => priority switch
    {
        ThumbnailPriority.Visible => _visible,
        ThumbnailPriority.Background => _background,
        _ => _normal
    };

    private static Waiter? Dequeue(Queue<Waiter> queue)
    {
        while (queue.Count > 0)
        {
            var waiter = queue.Dequeue();
            if (!waiter.IsCanceled) return waiter;
            waiter.Dispose();
        }
        return null;
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _disposed = true;
            foreach (var waiter in _visible.Concat(_normal).Concat(_background)) waiter.Cancel();
            _visible.Clear(); _normal.Clear(); _background.Clear();
        }
    }

    private sealed class Lease(PriorityAsyncGate owner) : IDisposable
    {
        private PriorityAsyncGate? _owner = owner;
        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Release();
    }

    private sealed class Waiter : IDisposable
    {
        private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly CancellationTokenRegistration _registration;
        public Waiter(CancellationToken cancellationToken) =>
            _registration = cancellationToken.Register(() => _completion.TrySetCanceled(cancellationToken));
        public Task Task => _completion.Task;
        public bool IsCanceled => _completion.Task.IsCanceled;
        public bool TryAssign()
        {
            var assigned = _completion.TrySetResult();
            _registration.Dispose();
            return assigned;
        }
        public void Cancel() { _completion.TrySetCanceled(); _registration.Dispose(); }
        public void Dispose() => _registration.Dispose();
    }
}
