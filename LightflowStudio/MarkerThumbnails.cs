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
    private readonly PriorityAsyncGate _gate = new(2);
    public async Task<string?> GetAsync(TimelineMarker marker, CancellationToken token)
    {
        using var operation = operations is null ? null : await operations.EnterOperationAsync(token).ConfigureAwait(false);
        using var lease = await _gate.EnterAsync(ThumbnailPriority.Visible, token).ConfigureAwait(false);
        var observed = await assets.ObserveAsync(marker.AssetId, token).ConfigureAwait(false);
        if (!observed.Succeeded || observed.Asset?.PhysicalPath is null || observed.Asset.Asset.Fingerprint is null) return null;
        var identity = Identity(observed.Asset.Asset, marker.Position);
        var directory = Path.Combine(locations().PreviewsDirectory, "previews", "markers", marker.AssetId.ToString("N"));
        var path = Path.Combine(directory, identity + ".jpg");
        if (File.Exists(path) && ThumbnailGenerationService.IsValidThumbnail(path)) return path;
        Directory.CreateDirectory(directory);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".lightflow";
        try
        {
            var result = await renderer.RenderAsync(observed.Asset.PhysicalPath, observed.Asset.Asset.MediaType,
                marker.Position, temporary, token).ConfigureAwait(false);
            if (result.Status != ThumbnailGenerationStatus.Succeeded || !ThumbnailGenerationService.IsValidThumbnail(temporary)) return null;
            var verified = await assets.ObserveAsync(marker.AssetId, token).ConfigureAwait(false);
            if (!verified.Succeeded || verified.Asset?.Asset.Fingerprint is null || Identity(verified.Asset.Asset, marker.Position) != identity) return null;
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
