using Xunit;

namespace LightflowStudio.Tests;

public sealed class BrowserSelectionActionTests
{
    [Theory]
    [InlineData(1, 0, false)]
    [InlineData(1, 1, true)]
    [InlineData(1, 3, true)]
    [InlineData(3, 0, false)]
    [InlineData(3, 1, true)]
    [InlineData(3, 3, true)]
    public void ExportMenuUsesAnySelectedVideosProjectedSubclipPresence(int videoCount, int subclipCount, bool menu)
    {
        var tiles = Enumerable.Range(0, videoCount).Select(index => Tile($"clip{index}.mov", MediaTypeCategory.Video)).ToArray();
        tiles[^1].SetAssetState(new BrowserAssetQueryState(subclipCount > 0 ? BrowserAssetState.Subclips : BrowserAssetState.None,
            false, false, subclipCount));
        var state = BrowserSelectionActions.Evaluate(tiles);
        Assert.True(state.CanExport);
        Assert.Equal(menu, state.ShowExportMenu);
        if (menu)
        {
            foreach (var tile in tiles) tile.SetAssetState(BrowserAssetState.Subclips);
            Assert.True(BrowserSelectionActions.Evaluate(tiles).ShowExportMenu);
        }
    }

    [Fact]
    public void ExportMenuTracksFirstAdditionLastDeletionAndCurrentSelectionThroughSharedProjection()
    {
        var model = new BrowserGridModel();
        var root = Guid.NewGuid();
        var assetId = Guid.NewGuid();
        model.Populate([new MediaFolderEntry(root, "clip.mov", "CLIP.MOV", "clip.mov", false,
            new MediaTypeClassification(MediaTypeCategory.Video), 1, DateTimeOffset.UnixEpoch)]);
        model.ApplyAssetIdentities([new(assetId, "clip.mov", CatalogReconciliationItemStatus.New)]);
        model.SelectSingle(0);
        Assert.False(BrowserSelectionActions.Evaluate(model.SelectedTilesInBrowserOrder).ShowExportMenu);
        model.ApplyAssetStateFlag(assetId, BrowserAssetState.Subclips, true);
        Assert.True(BrowserSelectionActions.Evaluate(model.SelectedTilesInBrowserOrder).ShowExportMenu);
        model.ApplyAssetStateFlag(assetId, BrowserAssetState.Color, true);
        Assert.True(BrowserSelectionActions.Evaluate(model.SelectedTilesInBrowserOrder).ShowExportMenu);
        model.ApplyAssetStateFlag(assetId, BrowserAssetState.Subclips, false);
        Assert.False(BrowserSelectionActions.Evaluate(model.SelectedTilesInBrowserOrder).ShowExportMenu);
        model.ApplyAssetStateFlag(assetId, BrowserAssetState.Subclips, true);
        Assert.True(BrowserSelectionActions.Evaluate(model.SelectedTilesInBrowserOrder).ShowExportMenu);
        Assert.False(BrowserSelectionActions.Evaluate([]).ShowExportMenu);
        Assert.False(BrowserSelectionActions.Evaluate([Tile("other.mov", MediaTypeCategory.Video)]).ShowExportMenu);
    }

    [Theory]
    [InlineData((int)MediaTypeCategory.StillImage)]
    [InlineData((int)MediaTypeCategory.RawImage)]
    [InlineData((int)MediaTypeCategory.Audio)]
    [InlineData((int)MediaTypeCategory.Unknown)]
    public void SavedSubclipsDoNotMakeUnsupportedSelectionsExportable(int category)
    {
        var video = Tile("clip.mov", MediaTypeCategory.Video);
        video.SetAssetState(BrowserAssetState.Subclips);
        var other = Tile("other", (MediaTypeCategory)category);
        other.SetAssetState(BrowserAssetState.Subclips);
        Assert.False(BrowserSelectionActions.Evaluate([other]).ShowExportMenu);
        var mixed = BrowserSelectionActions.Evaluate([video, other]);
        Assert.False(mixed.CanExport);
        Assert.False(mixed.ShowExportMenu);
        var unidentified = Tile("unknown.mov", MediaTypeCategory.Video, identified: false);
        unidentified.SetAssetState(BrowserAssetState.Subclips);
        Assert.False(BrowserSelectionActions.Evaluate([unidentified]).ShowExportMenu);
        Assert.False(BrowserSelectionActions.Evaluate([video, unidentified]).CanExport);
    }

    [Fact]
    public void VisualIndexRequiresIdentifiedVideoSelectionAndSupportsMultipleVideos()
    {
        var video = Tile("clip.mov", MediaTypeCategory.Video);
        Assert.True(BrowserSelectionActions.Evaluate([video]).CanCreateVisualIndex);
        Assert.True(BrowserSelectionActions.Evaluate([video, Tile("second.mp4", MediaTypeCategory.Video)]).CanCreateVisualIndex);
        Assert.False(BrowserSelectionActions.Evaluate([]).CanCreateVisualIndex);
        Assert.False(BrowserSelectionActions.Evaluate([video, Tile("photo.jpg", MediaTypeCategory.StillImage)]).CanCreateVisualIndex);
        Assert.False(BrowserSelectionActions.Evaluate([Tile("audio.wav", MediaTypeCategory.Audio)]).CanCreateVisualIndex);
        Assert.False(BrowserSelectionActions.Evaluate([Tile("unknown", MediaTypeCategory.Unknown)]).CanCreateVisualIndex);
        Assert.False(BrowserSelectionActions.Evaluate([Tile("clip.mov", MediaTypeCategory.Video, identified: false)]).CanCreateVisualIndex);
    }
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
    public void RegenerateWithoutSelection_UsesAuthoritativeScopeAndConfirmsOnlyAboveFifty()
    {
        var scope = Enumerable.Range(0, 51).Select(_ => Guid.NewGuid()).ToArray();
        Assert.Equal(scope, BrowserThumbnailRegeneration.ResolveTargets([], 0, scope));
        Assert.False(BrowserThumbnailRegeneration.RequiresConfirmation(0, 50));
        Assert.True(BrowserThumbnailRegeneration.RequiresConfirmation(0, 51));
        Assert.False(BrowserThumbnailRegeneration.RequiresConfirmation(1, 100));
        Assert.Equal("Regenerate Previews", BrowserThumbnailRegeneration.ProductLabel(0, false));
    }

    [Fact]
    public void RegenerateWithSelection_PrefersSelectedApplicableAssets()
    {
        var selected = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var scope = Enumerable.Range(0, 60).Select(_ => Guid.NewGuid()).ToArray();
        Assert.Equal(selected, BrowserThumbnailRegeneration.ResolveTargets(selected, selected.Length, scope));
        Assert.Equal("Regenerate Preview", BrowserThumbnailRegeneration.ProductLabel(1, true));
        Assert.Equal("Regenerate Previews", BrowserThumbnailRegeneration.ProductLabel(2, true));
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
        var noSelection = BrowserLutActionPicker.Present(ColorLutStage.Camera, [resource], []);
        var single = BrowserLutActionPicker.Present(ColorLutStage.Camera, [resource],
            [new(Guid.NewGuid(), assigned, null, "one")]);
        var shared = BrowserLutActionPicker.Present(ColorLutStage.Camera, [resource],
            [new(Guid.NewGuid(), assigned, null, "one"), new(Guid.NewGuid(), assigned, null, "two")]);
        var mixed = BrowserLutActionPicker.Present(ColorLutStage.Camera, [resource],
            [new(Guid.NewGuid(), assigned, null, "one"), new(Guid.NewGuid(), null, null, "original")]);

        Assert.Equal("Camera LUT…", neutral[0].Label);
        Assert.False(neutral[0].IsAction);
        Assert.Equal("No LUT", noSelection.Options[noSelection.SelectedIndex].Label);
        Assert.False(noSelection.Options[noSelection.SelectedIndex].IsAction);
        Assert.Equal("No LUT", original.Options[original.SelectedIndex].Label);
        Assert.Equal(lutId, single.Options[single.SelectedIndex].LutId);
        Assert.Equal(resource.DisplayName, single.Options[single.SelectedIndex].Label);
        Assert.Equal(lutId, shared.Options[shared.SelectedIndex].LutId);
        Assert.Equal(resource.DisplayName, shared.Options[shared.SelectedIndex].Label);
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
