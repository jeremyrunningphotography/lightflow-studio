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
    async Task<PositionFrameContext> PrepareContextAsync(Guid assetId, CancellationToken token) =>
        new(await PrepareAsync(assetId, token), ThumbnailColorRender.Original);
    Task<string?> FindCachedAsync(PositionFrameContext context, TimeSpan position, CancellationToken token) => Task.FromResult<string?>(null);
    Task<string?> GetAsync(PositionFrameContext context, TimeSpan position, ThumbnailPriority priority, CancellationToken token) =>
        GetAsync(context.Source, position, token);
    Task<bool> IsCurrentAsync(PositionFrameContext context, CancellationToken token) => Task.FromResult(true);
}

internal sealed record PositionFrameContext(MediaAssetResolution? Source, ThumbnailColorRender Color);

/// <summary>Position-frame cache shared by interactive demand and explicit preparation Jobs.</summary>
internal sealed class PositionFrameService(IMediaAssetService assets, Func<ILightflowStorageLocations> locations,
    IThumbnailRenderer renderer, IPreviewOperationCoordinator? operations = null,
    IAssetColorStore? colors = null, ILutLibraryCache? luts = null) : IPositionFrameService
{
    private readonly DerivedFrameDemands _demands = new(2);
    public async Task<MediaAssetResolution?> PrepareAsync(Guid assetId, CancellationToken token) =>
        (await assets.ObserveAsync(assetId, token).ConfigureAwait(false)).Asset;

    public async Task<PositionFrameContext> PrepareContextAsync(Guid assetId, CancellationToken token) =>
        new(await PrepareAsync(assetId, token).ConfigureAwait(false),
            await DerivedFrameColor.ResolveAsync(colors, luts, assetId, token).ConfigureAwait(false));

    public async Task<bool> IsCurrentAsync(PositionFrameContext context, CancellationToken token)
    {
        if (context.Source is not { } source) return false;
        var current = await PrepareContextAsync(source.Asset.AssetId, token).ConfigureAwait(false);
        return current.Source is { } observed && Identity(source.Asset, TimeSpan.Zero) == Identity(observed.Asset, TimeSpan.Zero)
            && current.Color.VisualIdentity == context.Color.VisualIdentity;
    }

    public async Task<string?> GetAsync(MediaAssetResolution? source, TimeSpan position, CancellationToken token) =>
        await GetAsync(new PositionFrameContext(source, ThumbnailColorRender.Original), position, ThumbnailPriority.Visible, token).ConfigureAwait(false);

    private string CachePath(PositionFrameContext context, TimeSpan position)
    {
        var identity = Identity(context.Source!.Asset, position);
        if (context.Color.VisualIdentity != PreviewVisualIdentity.Original)
            identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity + "|" + context.Color.VisualIdentity)));
        return Path.Combine(locations().PreviewsDirectory, "previews", "markers", context.Source.Asset.AssetId.ToString("N"), identity + ".jpg");
    }

    public async Task<string?> FindCachedAsync(PositionFrameContext context, TimeSpan position, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (context.Source?.Asset.Fingerprint is null || position < TimeSpan.Zero) return null;
        using var operation = operations is null ? null : await operations.EnterOperationAsync(token).ConfigureAwait(false);
        var currentColor = await DerivedFrameColor.ResolveAsync(colors, luts, context.Source.Asset.AssetId, token).ConfigureAwait(false);
        if (currentColor.VisualIdentity != context.Color.VisualIdentity) return null;
        var path = CachePath(context, position);
        return File.Exists(path) && ThumbnailGenerationService.IsValidThumbnail(path) ? path : null;
    }

    public async Task<string?> GetAsync(PositionFrameContext context, TimeSpan position, ThumbnailPriority priority, CancellationToken token)
    {
        if (context.Source?.Asset.Fingerprint is null || position < TimeSpan.Zero) return null;
        var cached = await FindCachedAsync(context, position, token).ConfigureAwait(false);
        if (cached is not null) return cached;
        return await _demands.RequestAsync(CachePath(context, position), priority,
            ct => RenderAsync(context, position, ct), token).ConfigureAwait(false);
    }

    private async Task<string?> RenderAsync(PositionFrameContext context, TimeSpan position, CancellationToken token)
    {
        var source = context.Source;
        if (source?.Asset.Fingerprint is null || position < TimeSpan.Zero) return null;
        using var operation = operations is null ? null : await operations.EnterOperationAsync(token).ConfigureAwait(false);
        var identity = Identity(source.Asset, position);
        // Preserve the marker cache namespace so existing exact-position frames remain reusable.
        var path = CachePath(context, position);
        var directory = Path.GetDirectoryName(path)!;
        if (File.Exists(path) && ThumbnailGenerationService.IsValidThumbnail(path)) return path;
        if (!source.SourceExists || source.RootAvailability != MediaRootAvailability.Online || source.PhysicalPath is null) return null;
        Directory.CreateDirectory(directory);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".lightflow";
        try
        {
            var result = await renderer.RenderAsync(source.PhysicalPath, source.Asset.MediaType,
                position, temporary, context.Color, token).ConfigureAwait(false);
            if (result.Status != ThumbnailGenerationStatus.Succeeded || !ThumbnailGenerationService.IsValidThumbnail(temporary)) return null;
            var verified = await assets.ObserveAsync(source.Asset.AssetId, token).ConfigureAwait(false);
            if (!verified.Succeeded || verified.Asset?.Asset.Fingerprint is null || Identity(verified.Asset.Asset, position) != identity) return null;
            var currentColor = await DerivedFrameColor.ResolveAsync(colors, luts, source.Asset.AssetId, token).ConfigureAwait(false);
            if (currentColor.VisualIdentity != context.Color.VisualIdentity) return null;
            token.ThrowIfCancellationRequested();
            File.Move(temporary, path, overwrite: true);
            return path;
        }
        finally { try { File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }
    internal static string Identity(MediaAsset source, TimeSpan position) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        $"1|{source.FileSizeBytes}|{source.LastWriteUtcTicks}|{source.Fingerprint?.Version}|{source.Fingerprint?.Value}|{position.Ticks}")));
    public void Dispose() => _demands.Dispose();
}
