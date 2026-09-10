namespace LightflowStudio;

/// <summary>A captured order of stable identities; Preview is an existing notifying projection, never a loader.</summary>
internal sealed record PlayerReviewItem(PlayerViewerAsset Asset, object? Preview);

internal sealed class PlayerReviewSet(IReadOnlyList<PlayerReviewItem> items, Guid? currentAssetId, bool isSelectionSubset = false)
{
    public bool IsSelectionSubset { get; } = isSelectionSubset;
    public IReadOnlyList<PlayerReviewItem> Items { get; } = items.ToArray();
    public int CurrentIndex { get; private set; } = items.ToList().FindIndex(item => item.Asset.AssetId == currentAssetId);
    private readonly Dictionary<Guid, int> _indices = items.Select((item, index) => (item, index))
        .Where(pair => pair.item.Asset.AssetId is not null).ToDictionary(pair => pair.item.Asset.AssetId!.Value, pair => pair.index);
    public bool CanPrevious => CurrentIndex > 0;
    public bool HasTraversed { get; private set; }
    public bool CanNext => CurrentIndex >= 0 && CurrentIndex < Items.Count - 1;
    public bool Select(Guid assetId)
    {
        if (!_indices.TryGetValue(assetId, out var index) || index == CurrentIndex) return false;
        CurrentIndex = index;
        HasTraversed = true;
        return true;
    }
}

internal static class BrowserPlayerReviewSet
{
    public static PlayerReviewSet Capture(IReadOnlyList<BrowserGridTile> ordered, PlayerViewerAsset current)
    {
        var compatible = ordered.Where(tile => tile.AssetId is not null &&
            tile.Category is MediaTypeCategory.Video or MediaTypeCategory.StillImage or MediaTypeCategory.RawImage).ToArray();
        var selected = compatible.Where(tile => tile.IsSelected).ToArray();
        var candidates = selected.Length > 1 ? selected : compatible;
        var items = candidates.Select(tile => new PlayerReviewItem(new(tile.RootId, tile.RelativePath, tile.Key,
            tile.Name, MediaPresentationClassification.KindFor(tile.Category), tile.AssetId), tile)).ToList();
        // Continuation can restore a current asset absent from today's query/membership results.
        if (current.AssetId is not null && items.All(item => item.Asset.AssetId != current.AssetId))
            items.Add(new(current, null));
        return new(items, current.AssetId, selected.Length > 1);
    }
}
