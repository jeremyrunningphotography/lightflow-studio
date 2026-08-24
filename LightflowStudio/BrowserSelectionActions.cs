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

internal sealed record BrowserLutActionOption(Guid? LutId, string Label, bool IsAction = true);
internal sealed record BrowserLutPickerPresentation(IReadOnlyList<BrowserLutActionOption> Options, int SelectedIndex);

internal static class BrowserLutActionPicker
{
    public static IReadOnlyList<BrowserLutActionOption> Build(string stageName,
        IReadOnlyList<ManagedLutResource> resources)
    {
        var actions = new List<BrowserLutActionOption>
        {
            new(null, $"{stageName} LUT…", IsAction: false),
            new(null, "No LUT")
        };
        actions.AddRange(resources.Select(resource => new BrowserLutActionOption(resource.LutId, resource.DisplayName)));
        return actions;
    }

    public static BrowserLutPickerPresentation Present(ColorLutStage stage,
        IReadOnlyList<ManagedLutResource> resources, IReadOnlyList<AssetColorIntent> intents)
    {
        var stageName = EncodingLutResourceStore.StageName(stage);
        var options = Build(stageName, resources).Skip(1).ToList();
        if (intents.Count == 0)
            return new([new(null, "No LUT", IsAction: false)], 0);
        var references = intents.Select(intent => stage == ColorLutStage.Camera ? intent.Camera : intent.Creative).ToList();
        var distinct = references.Select(reference => reference?.LutId).Distinct().ToList();
        if (distinct.Count > 1)
        {
            options.Insert(0, new(null, "Mixed", IsAction: false));
            return new(options, 0);
        }
        if (distinct[0] is not Guid selected) return new(options, 0);
        var index = options.FindIndex(option => option.LutId == selected);
        if (index >= 0) return new(options, index);
        var assigned = references.First(reference => reference?.LutId == selected)!;
        options.Add(new(selected, $"{assigned.DisplayName} (Unavailable)"));
        return new(options, options.Count - 1);
    }
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

    public static bool CanAssignLutColor(BrowserSelectionActionState state,
        IReadOnlyList<bool> sourceAvailability) =>
        state.CanAssignCameraLut && sourceAvailability.Count == state.SelectionCount && sourceAvailability.All(value => value);
}

internal static class BrowserThumbnailRegeneration
{
    public const int ConfirmationThreshold = 50;
    public static IReadOnlyList<Guid> ResolveTargets(IReadOnlyList<Guid> selectedApplicable,
        int selectionCount, IReadOnlyList<Guid> scopeApplicable) =>
        selectionCount > 0 ? selectedApplicable : scopeApplicable;
    public static bool RequiresConfirmation(int selectionCount, int targetCount) =>
        selectionCount == 0 && targetCount > ConfirmationThreshold;
}
