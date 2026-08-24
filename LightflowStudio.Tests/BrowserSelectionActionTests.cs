using Xunit;

namespace LightflowStudio.Tests;

public sealed class BrowserSelectionActionTests
{
    [Fact]
    public void Empty_selection_keeps_actions_visible_but_disabled()
    {
        var state = BrowserSelectionActions.Evaluate([]);
        Assert.False(state.HasSelection);
        Assert.False(state.CanExport);
        Assert.False(state.CanRegenerateThumbnails);
        Assert.False(state.CanRename);
        Assert.False(state.CanAssignCameraLut);
    }

    [Fact]
    public void Video_selection_enables_existing_video_capabilities_but_not_unimplemented_rename()
    {
        var state = BrowserSelectionActions.Evaluate([Tile("clip.mov", MediaTypeCategory.Video)]);
        Assert.True(state.CanExport);
        Assert.True(state.CanRegenerateThumbnails);
        Assert.True(state.CanAssignCameraLut);
        Assert.True(state.CanAssignCreativeLut);
        Assert.False(state.CanRename);
    }

    [Fact]
    public void Mixed_selection_disables_export_and_color_but_keeps_supported_thumbnail_regeneration()
    {
        var state = BrowserSelectionActions.Evaluate([
            Tile("clip.mov", MediaTypeCategory.Video), Tile("still.jpg", MediaTypeCategory.StillImage)]);
        Assert.False(state.CanExport);
        Assert.True(state.CanRegenerateThumbnails);
        Assert.False(state.CanAssignCameraLut);
    }

    [Fact]
    public void Raw_or_unidentified_selection_does_not_claim_unsupported_capabilities()
    {
        var raw = BrowserSelectionActions.Evaluate([Tile("raw.dng", MediaTypeCategory.RawImage)]);
        var unidentified = BrowserSelectionActions.Evaluate([Tile("clip.mov", MediaTypeCategory.Video, identified: false)]);
        Assert.False(raw.CanRegenerateThumbnails);
        Assert.False(unidentified.CanExport);
    }

    [Fact]
    public void Right_click_preserves_selected_batch_and_replaces_for_unselected_tile()
    {
        Assert.False(BrowserSelectionActions.ShouldReplaceSelectionOnRightClick(tileIsSelected: true));
        Assert.True(BrowserSelectionActions.ShouldReplaceSelectionOnRightClick(tileIsSelected: false));
    }

    [Fact]
    public void Lut_picker_reflects_no_lut_shared_and_mixed_durable_assignments()
    {
        var lutId = Guid.NewGuid();
        var resource = new ManagedLutResource(lutId, "Log to Rec.709", "technical.cube", new string('a', 64),
            LutDimension.ThreeDimensional, 33, LutResourceAvailability.Available);
        var assigned = new ColorLutReference(lutId, resource.DisplayName, resource.ContentSha256,
            LutResourceAvailability.Available);

        var neutral = BrowserLutActionPicker.Build("Camera", [resource]);
        var original = BrowserLutActionPicker.Present(ColorLutStage.Camera, [resource],
            [new(Guid.NewGuid(), null, null, "original")]);
        var shared = BrowserLutActionPicker.Present(ColorLutStage.Camera, [resource],
            [new(Guid.NewGuid(), assigned, null, "one"), new(Guid.NewGuid(), assigned, null, "two")]);
        var mixed = BrowserLutActionPicker.Present(ColorLutStage.Camera, [resource],
            [new(Guid.NewGuid(), assigned, null, "one"), new(Guid.NewGuid(), null, null, "original")]);

        Assert.Equal("Camera LUT…", neutral[0].Label);
        Assert.False(neutral[0].IsAction);
        Assert.Equal("No LUT", original.Options[original.SelectedIndex].Label);
        Assert.Equal(lutId, shared.Options[shared.SelectedIndex].LutId);
        Assert.Equal("Mixed", mixed.Options[mixed.SelectedIndex].Label);
        Assert.False(mixed.Options[mixed.SelectedIndex].IsAction);
    }

    [Fact]
    public void Lut_color_is_all_or_nothing_when_any_selected_source_is_unavailable()
    {
        var state = BrowserSelectionActions.Evaluate([
            Tile("one.mov", MediaTypeCategory.Video), Tile("two.mov", MediaTypeCategory.Video)]);

        Assert.True(BrowserSelectionActions.CanAssignLutColor(state, [true, true]));
        Assert.False(BrowserSelectionActions.CanAssignLutColor(state, [true, false]));
        Assert.False(BrowserSelectionActions.CanAssignLutColor(state, [true]));
    }

    private static BrowserGridTile Tile(string name, MediaTypeCategory category, bool identified = true)
    {
        var root = Guid.NewGuid();
        var tile = new BrowserGridTile(new MediaFolderEntry(root, name, name.ToUpperInvariant(), name,
            false, new MediaTypeClassification(category), 1, DateTimeOffset.UnixEpoch), 0);
        if (identified) tile.SetAssetId(Guid.NewGuid());
        return tile;
    }
}
