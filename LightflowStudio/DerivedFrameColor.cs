namespace LightflowStudio;

/// <summary>The shared assigned Color boundary for derived imagery: Camera, then Creative.</summary>
internal static class DerivedFrameColor
{
    internal static async Task<ThumbnailColorRender> ResolveAsync(IAssetColorStore? colors,
        ILutLibraryCache? luts, Guid assetId, CancellationToken token)
    {
        if (colors is null || luts is null) return ThumbnailColorRender.Original;
        var intent = await colors.GetAsync(assetId, token).ConfigureAwait(false);
        if (!intent.HasColor) return ThumbnailColorRender.Original;
        var paths = new List<string>(2);
        if (intent.Camera is { } camera) paths.Add(luts.ResolvePath(ColorLutStage.Camera, camera.LutId));
        if (intent.Creative is { } creative) paths.Add(luts.ResolvePath(ColorLutStage.Creative, creative.LutId));
        return new(intent.ColorIdentity, paths);
    }
}
