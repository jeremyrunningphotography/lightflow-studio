namespace LightflowStudio;

internal sealed record BrowserSelectionActionState(
    int SelectionCount,
    bool CanExport,
    bool CanRegenerateThumbnails,
    bool CanRename,
    bool CanAssignCameraLut,
    bool CanAssignCreativeLut)
{
    public bool HasSelection => SelectionCount > 0;
}

internal static class BrowserSelectionActions
{
    public static BrowserSelectionActionState Evaluate(IReadOnlyList<BrowserGridTile> selection)
    {
        var identified = selection.Count > 0 && selection.All(tile => tile.AssetId is not null);
        var allVideo = identified && selection.All(tile => tile.Category == MediaTypeCategory.Video);
        var thumbnails = identified && selection.All(tile => tile.Category is
            MediaTypeCategory.Video or MediaTypeCategory.StillImage);
        return new(selection.Count,
            CanExport: allVideo,
            CanRegenerateThumbnails: thumbnails,
            CanRename: false,
            CanAssignCameraLut: allVideo,
            CanAssignCreativeLut: allVideo);
    }

    public static bool ShouldReplaceSelectionOnRightClick(bool tileIsSelected) => !tileIsSelected;
}
