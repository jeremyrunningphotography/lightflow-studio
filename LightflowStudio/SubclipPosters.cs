using System.Security.Cryptography;
using System.Text;
using System.IO;

namespace LightflowStudio;

internal sealed record SubclipPosterResult(string? Path, string? Diagnostic = null)
{
    public bool Succeeded => Path is not null;
}

internal interface ISubclipPosterService : IDisposable
{
    Task<SubclipPosterResult> GetAsync(Subclip subclip, CancellationToken cancellationToken = default);
    void Remove(Guid assetId, Guid subclipId);
}

/// <summary>
/// Bounded, rebuildable Preview work for Subclip posters. Cache identity follows the observed source plus the
/// stable Subclip identity and authoritative In timestamp; names, order, and Catalog revisions are deliberately
/// excluded because none changes the pixels.
/// </summary>
internal sealed class SubclipPosterService(
    IMediaAssetService assets,
    Func<ILightflowStorageLocations> locations,
    IThumbnailRenderer renderer,
    int maximumConcurrency = 2,
    IPreviewOperationCoordinator? operations = null) : ISubclipPosterService
{
    internal const int GeneratorVersion = 1;
    private readonly PriorityAsyncGate _gate = new(maximumConcurrency);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, byte> _deleted = new();

    public async Task<SubclipPosterResult> GetAsync(Subclip subclip, CancellationToken cancellationToken = default)
    {
        if (_deleted.ContainsKey(subclip.SubclipId)) return new(null, "The Subclip was deleted.");
        using var operationLease = operations is null ? null :
            await operations.EnterOperationAsync(cancellationToken).ConfigureAwait(false);
        using var lease = await _gate.EnterAsync(ThumbnailPriority.Visible, cancellationToken).ConfigureAwait(false);
        var observed = await assets.ObserveAsync(subclip.AssetId, cancellationToken).ConfigureAwait(false);
        if (!observed.Succeeded || observed.Asset?.PhysicalPath is null || observed.Asset.Asset.Fingerprint is null)
            return new(null, observed.Diagnostic ?? "The source is unavailable.");

        var source = observed.Asset.Asset;
        var directory = DirectoryFor(subclip.AssetId);
        var identity = CacheIdentity(source, subclip);
        var finalPath = Path.Combine(directory, $"{subclip.SubclipId:N}-{identity}.jpg");
        if (File.Exists(finalPath) && ThumbnailGenerationService.IsValidThumbnail(finalPath))
            return new(finalPath);

        Directory.CreateDirectory(directory);
        var temporaryPath = finalPath + $".{Guid.NewGuid():N}.lightflow";
        try
        {
            var rendered = await renderer.RenderAsync(observed.Asset.PhysicalPath, source.MediaType, subclip.In,
                temporaryPath, cancellationToken).ConfigureAwait(false);
            if (rendered.Status != ThumbnailGenerationStatus.Succeeded ||
                !ThumbnailGenerationService.IsValidThumbnail(temporaryPath))
                return new(null, rendered.Diagnostic ?? "The poster frame could not be decoded.");

            var verified = await assets.ObserveAsync(subclip.AssetId, cancellationToken).ConfigureAwait(false);
            if (!verified.Succeeded || verified.Asset?.Asset.Fingerprint is null ||
                CacheIdentity(verified.Asset.Asset, subclip) != identity)
                return new(null, "The source changed while its poster was generated.");
            if (_deleted.ContainsKey(subclip.SubclipId)) return new(null, "The Subclip was deleted.");

            File.Move(temporaryPath, finalPath, overwrite: true);
            return new(finalPath);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new(null, exception.Message);
        }
        finally { try { File.Delete(temporaryPath); } catch { } }
    }

    public void Remove(Guid assetId, Guid subclipId)
    {
        _deleted.TryAdd(subclipId, 0);
        var directory = DirectoryFor(assetId);
        if (!Directory.Exists(directory)) return;
        foreach (var path in Directory.EnumerateFiles(directory, $"{subclipId:N}-*.jpg"))
            try { File.Delete(path); } catch { }
    }

    private string DirectoryFor(Guid assetId) =>
        Path.Combine(locations().PreviewsDirectory, "previews", "subclips", assetId.ToString("N"));

    internal static string CacheIdentity(MediaAsset source, Subclip subclip)
    {
        var value = $"{GeneratorVersion}|{source.FileSizeBytes}|{source.LastWriteUtcTicks}|" +
                    $"{source.Fingerprint!.Version}|{source.Fingerprint.Value}|{subclip.In.Ticks}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..20].ToLowerInvariant();
    }

    public void Dispose() => _gate.Dispose();
}

internal static class SubclipPosterFactory
{
    public static ISubclipPosterService Create(IMediaAssetService assets, Func<ILightflowStorageLocations> locations,
        AppSettings settings, string? applicationDirectory = null, int maximumConcurrency = 2,
        IPreviewOperationCoordinator? operations = null)
    {
        applicationDirectory ??= AppContext.BaseDirectory;
        var configuredDirectory = string.IsNullOrWhiteSpace(settings.FfmpegPath)
            ? null : Path.GetDirectoryName(settings.FfmpegPath);
        var configuredFfmpeg = configuredDirectory is null ? null : Path.Combine(configuredDirectory, "ffmpeg.exe");
        var ffmpeg = ExecutableLocator.Find("ffmpeg.exe",
            Path.Combine(applicationDirectory, "ffmpeg", "bin", "ffmpeg.exe"),
            configured: configuredFfmpeg ?? settings.FfmpegPath);
        return new SubclipPosterService(assets, locations,
            new CompositeThumbnailRenderer(new WicImageThumbnailRenderer(),
                new FfmpegVideoThumbnailRenderer(ffmpeg, new ProbeProcessRunner())), maximumConcurrency, operations);
    }
}
