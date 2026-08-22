namespace LightflowStudio;

/// <summary>
/// Which content the Browser's central workspace area currently presents. The surrounding shell (Locations
/// tree, navigation bar, query toolbar, status bar) stays intact across both — only this one region's content
/// changes. See <see cref="PlayerViewerHost"/> for the reusable presentation content itself.
/// </summary>
internal enum BrowserPresentationMode { Grid, PlayerViewer }

/// <summary>
/// Which presentation the opened asset needs: video plays through the shared #53 playback engine; a still
/// image is decoded and shown directly. Exhaustive over <see cref="BrowserGridModel.PresentableCategories"/>.
/// </summary>
internal enum MediaPresentationKind { Video, Image }

/// <summary>
/// The one host-agnostic classification <see cref="PlayerViewerHost"/> needs to decide how to present an
/// asset. Deliberately the same three-category split <see cref="BrowserGridModel.IsPresentable"/> already
/// enforces for grid admission, so a future non-Browser host (e.g. #112's floating window) never needs a
/// second classification of what Lightflow can present.
/// </summary>
internal static class MediaPresentationClassification
{
    public static MediaPresentationKind KindFor(MediaTypeCategory category) => category switch
    {
        MediaTypeCategory.StillImage or MediaTypeCategory.RawImage => MediaPresentationKind.Image,
        MediaTypeCategory.Video => MediaPresentationKind.Video,
        _ => throw new ArgumentOutOfRangeException(nameof(category), category, "Not a presentable Browser media category.")
    };
}

/// <summary>
/// The minimal, host-agnostic description of one asset to open into <see cref="PlayerViewerHost"/> — deliberately
/// independent of <see cref="BrowserGridTile"/> (a WPF-bound, selection/thumbnail-carrying Browser grid concept)
/// so the reusable Player/Viewer content never depends on Browser-specific types. Any future host (a floating
/// window per #112, or a filmstrip-driven multi-asset review per #111) constructs this same shape.
/// </summary>
internal sealed record PlayerViewerAsset(Guid RootId, string RelativePath, string Key, string Name, MediaPresentationKind Kind, Guid? AssetId = null);

internal static class ReviewRangePlaybackPolicy
{
    public static bool ShouldArmOutBoundary(MediaRange? range, TimeSpan playhead) =>
        range is not null && playhead <= range.EffectiveOut;

    public static bool HasReachedArmedOutBoundary(MediaRange? range, bool armed, TimeSpan displayed) =>
        armed && range is not null && displayed >= range.EffectiveOut;
}

internal sealed record PlayerRangeTimelinePresentation(
    bool HasSelectedSpan,
    bool HasProportions,
    bool ShowBoundaries,
    double StartFraction,
    double WidthFraction)
{
    public static PlayerRangeTimelinePresentation For(MediaRange? range, TimeSpan? knownDuration)
    {
        if (range is null || range.IsFullSource)
        {
            var hasDuration = knownDuration > TimeSpan.Zero;
            return new(hasDuration, hasDuration, false, 0, hasDuration ? 1 : 0);
        }

        var projected = TrimIndicatorPresentation.For(range, knownDuration);
        return new(projected.HasActiveTrim, projected.HasProportions, true,
            projected.StartFraction, projected.WidthFraction);
    }
}
