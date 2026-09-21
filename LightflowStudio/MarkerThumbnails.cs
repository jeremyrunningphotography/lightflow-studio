using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace LightflowStudio;

internal interface IMarkerThumbnailService : IDisposable
{
    Task<string?> GetAsync(TimelineMarker marker, CancellationToken token);
}

/// <summary>Rebuildable position-frame cache under Previews; no marker schema or durable image identity.</summary>
internal sealed class MarkerThumbnailService(IMediaAssetService assets, Func<ILightflowStorageLocations> locations,
    IThumbnailRenderer renderer, IPreviewOperationCoordinator? operations = null) : IMarkerThumbnailService
{
    private readonly PositionFrameService _frames = new(assets, locations, renderer, operations);
    public async Task<string?> GetAsync(TimelineMarker marker, CancellationToken token) =>
        await _frames.GetAsync(await _frames.PrepareAsync(marker.AssetId, token), marker.Position, token);
    internal static string Identity(MediaAsset source, TimeSpan position) => PositionFrameService.Identity(source, position);
    public void Dispose() => _frames.Dispose();
}

internal interface IPositionFrameService : IDisposable
{
    Task<MediaAssetResolution?> PrepareAsync(Guid assetId, CancellationToken token);
    Task<string?> GetAsync(MediaAssetResolution? source, TimeSpan position, CancellationToken token);
}

/// <summary>Shared Original-color position frames. Effective presentation changes must version Identity and renderer together.</summary>
internal sealed class PositionFrameService(IMediaAssetService assets, Func<ILightflowStorageLocations> locations,
    IThumbnailRenderer renderer, IPreviewOperationCoordinator? operations = null) : IPositionFrameService
{
    private readonly PriorityAsyncGate _gate = new(2);
    public async Task<MediaAssetResolution?> PrepareAsync(Guid assetId, CancellationToken token) =>
        (await assets.ObserveAsync(assetId, token).ConfigureAwait(false)).Asset;

    public async Task<string?> GetAsync(MediaAssetResolution? source, TimeSpan position, CancellationToken token)
    {
        if (source?.Asset.Fingerprint is null || position < TimeSpan.Zero) return null;
        using var operation = operations is null ? null : await operations.EnterOperationAsync(token).ConfigureAwait(false);
        using var lease = await _gate.EnterAsync(ThumbnailPriority.Visible, token).ConfigureAwait(false);
        var identity = Identity(source.Asset, position);
        // Preserve the marker cache namespace so existing exact-position frames remain reusable.
        var directory = Path.Combine(locations().PreviewsDirectory, "previews", "markers", source.Asset.AssetId.ToString("N"));
        var path = Path.Combine(directory, identity + ".jpg");
        if (File.Exists(path) && ThumbnailGenerationService.IsValidThumbnail(path)) return path;
        if (!source.SourceExists || source.RootAvailability != MediaRootAvailability.Online || source.PhysicalPath is null) return null;
        Directory.CreateDirectory(directory);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".lightflow";
        try
        {
            var result = await renderer.RenderAsync(source.PhysicalPath, source.Asset.MediaType,
                position, temporary, token).ConfigureAwait(false);
            if (result.Status != ThumbnailGenerationStatus.Succeeded || !ThumbnailGenerationService.IsValidThumbnail(temporary)) return null;
            var verified = await assets.ObserveAsync(source.Asset.AssetId, token).ConfigureAwait(false);
            if (!verified.Succeeded || verified.Asset?.Asset.Fingerprint is null || Identity(verified.Asset.Asset, position) != identity) return null;
            token.ThrowIfCancellationRequested();
            File.Move(temporary, path, overwrite: true);
            return path;
        }
        finally { try { File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }
    internal static string Identity(MediaAsset source, TimeSpan position) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        $"1|{source.FileSizeBytes}|{source.LastWriteUtcTicks}|{source.Fingerprint?.Version}|{source.Fingerprint?.Value}|{position.Ticks}")));
    public void Dispose() => _gate.Dispose();
}
