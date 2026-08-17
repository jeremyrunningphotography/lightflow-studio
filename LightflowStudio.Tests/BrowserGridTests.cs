using LightflowStudio;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class BrowserGridTests
{
    [Fact]
    public void Populate_ExcludesDirectoriesAndNeverCreatesFolderTiles()
    {
        var model = new BrowserGridModel();
        var rootId = Guid.NewGuid();
        var entries = new[]
        {
            Directory(rootId, "Trips"),
            Image(rootId, "a.jpg"),
            Video(rootId, "b.mp4"),
        };

        model.Populate(entries);

        Assert.Equal(2, model.Tiles.Count);
        Assert.DoesNotContain(model.Tiles, tile => tile.Name == "Trips");
        Assert.Contains(model.Tiles, tile => tile.Name == "a.jpg");
        Assert.Contains(model.Tiles, tile => tile.Name == "b.mp4");
    }

    [Fact]
    public void Populate_SupportedStillImageAppears() => AssertAppears(MediaTypeCategory.StillImage, appears: true);

    [Fact]
    public void Populate_SupportedRawImageAppears() => AssertAppears(MediaTypeCategory.RawImage, appears: true);

    [Fact]
    public void Populate_SupportedVideoAppears() => AssertAppears(MediaTypeCategory.Video, appears: true);

    [Fact]
    public void Populate_StandaloneAudioDoesNotAppear() => AssertAppears(MediaTypeCategory.Audio, appears: false);

    [Fact]
    public void Populate_UnknownUnsupportedFileDoesNotAppear() => AssertAppears(MediaTypeCategory.Unknown, appears: false);

    private static void AssertAppears(MediaTypeCategory category, bool appears)
    {
        var model = new BrowserGridModel();
        var rootId = Guid.NewGuid();
        model.Populate([File(rootId, "item.bin", category)]);

        if (appears)
        {
            var tile = Assert.Single(model.Tiles);
            Assert.Equal(category, tile.Category);
            Assert.False(string.IsNullOrEmpty(tile.CategoryGlyph));
        }
        else Assert.Empty(model.Tiles);
    }

    [Fact]
    public void Populate_MixedFolderProducesTilesOnlyForSupportedImageRawAndVideoMedia()
    {
        // Lightflow's Browser is a media browser, not a general-purpose file browser: audio, sidecars/
        // documents/archives/executables (Unknown), and folders must never reach the central canvas.
        var model = new BrowserGridModel();
        var rootId = Guid.NewGuid();
        var entries = new[]
        {
            Directory(rootId, "Subfolder"),
            File(rootId, "photo.jpg", MediaTypeCategory.StillImage),
            File(rootId, "raw.cr2", MediaTypeCategory.RawImage),
            File(rootId, "clip.mp4", MediaTypeCategory.Video),
            File(rootId, "track.mp3", MediaTypeCategory.Audio),
            File(rootId, "notes.txt", MediaTypeCategory.Unknown),
            File(rootId, "clip.lrf", MediaTypeCategory.Unknown),
        };

        model.Populate(entries);

        Assert.Equal(["photo.jpg", "raw.cr2", "clip.mp4"], model.Tiles.Select(tile => tile.Name));
    }

    [Fact]
    public void Populate_IndexIsContiguousOverTheFilteredMediaSetOnly()
    {
        // Selection/shift-range math operates on tile.Index, which must count only presentable media —
        // skipped folders/audio/unsupported entries must not leave gaps or shift later indices.
        var model = new BrowserGridModel();
        var rootId = Guid.NewGuid();
        var entries = new[]
        {
            File(rootId, "a.jpg", MediaTypeCategory.StillImage),
            File(rootId, "skip1.mp3", MediaTypeCategory.Audio),
            Directory(rootId, "skip2"),
            File(rootId, "b.mp4", MediaTypeCategory.Video),
            File(rootId, "skip3.txt", MediaTypeCategory.Unknown),
            File(rootId, "c.cr2", MediaTypeCategory.RawImage),
        };

        model.Populate(entries);

        Assert.Equal(["a.jpg", "b.mp4", "c.cr2"], model.Tiles.Select(tile => tile.Name));
        Assert.Equal([0, 1, 2], model.Tiles.Select(tile => tile.Index));
    }

    [Fact]
    public void SelectRange_OperatesOnlyOverTheFilteredMediaSet()
    {
        // A shift-range selection must span only the presentable tiles at their filtered indices, not the
        // raw folder-entry positions that included excluded audio/unsupported/folder items.
        var model = new BrowserGridModel();
        var rootId = Guid.NewGuid();
        var entries = new[]
        {
            File(rootId, "a.jpg", MediaTypeCategory.StillImage),
            File(rootId, "skip.mp3", MediaTypeCategory.Audio),
            File(rootId, "b.mp4", MediaTypeCategory.Video),
            File(rootId, "c.cr2", MediaTypeCategory.RawImage),
        };
        model.Populate(entries);
        model.SelectSingle(0);

        model.SelectRange(2);

        Assert.Equal([true, true, true], model.Tiles.Select(tile => tile.IsSelected));
    }

    [Fact]
    public void Populate_AssignsDeterministicIndexMatchingEnumerationOrder()
    {
        var model = new BrowserGridModel();
        var rootId = Guid.NewGuid();
        model.Populate([Image(rootId, "a.jpg"), Image(rootId, "b.jpg"), Image(rootId, "c.jpg")]);

        Assert.Equal(["a.jpg", "b.jpg", "c.jpg"], model.Tiles.Select(tile => tile.Name));
        Assert.Equal([0, 1, 2], model.Tiles.Select(tile => tile.Index));
    }

    [Fact]
    public void Populate_ReusesExistingTileInstanceByKeySoThumbnailsSurviveNonDestructiveRefresh()
    {
        var model = new BrowserGridModel();
        var rootId = Guid.NewGuid();
        var entry = Image(rootId, "a.jpg");
        model.Populate([entry]);
        var assetId = Guid.NewGuid();
        model.ApplyAssetIdentities([new(assetId, entry.RelativePath, CatalogReconciliationItemStatus.New)]);
        model.ApplyThumbnail(assetId, @"C:\previews\a.jpg");
        var originalTile = model.Tiles.Single();

        model.Populate([Image(rootId, "a.jpg"), Image(rootId, "b.jpg")]);

        var refreshedTile = model.Tiles.Single(tile => tile.Name == "a.jpg");
        Assert.Same(originalTile, refreshedTile);
        Assert.Equal(@"C:\previews\a.jpg", refreshedTile.ThumbnailPath);
        Assert.True(refreshedTile.HasThumbnail);
    }

    [Fact]
    public void ApplyThumbnail_UpdatesOnlyTheMatchingAssetInPlace()
    {
        var model = new BrowserGridModel();
        var rootId = Guid.NewGuid();
        var entryA = Image(rootId, "a.jpg");
        var entryB = Image(rootId, "b.jpg");
        model.Populate([entryA, entryB]);
        var assetA = Guid.NewGuid();
        var assetB = Guid.NewGuid();
        model.ApplyAssetIdentities(
        [
            new(assetA, entryA.RelativePath, CatalogReconciliationItemStatus.New),
            new(assetB, entryB.RelativePath, CatalogReconciliationItemStatus.New),
        ]);

        model.ApplyThumbnail(assetA, @"C:\previews\a.jpg");

        var tileA = model.Tiles.Single(tile => tile.Name == "a.jpg");
        var tileB = model.Tiles.Single(tile => tile.Name == "b.jpg");
        Assert.True(tileA.HasThumbnail);
        Assert.False(tileB.HasThumbnail);
    }

    [Fact]
    public void ApplyThumbnail_ForAssetNoLongerPresentIsIgnoredRatherThanThrowing()
    {
        var model = new BrowserGridModel();
        model.Populate([]);

        var exception = Record.Exception(() => model.ApplyThumbnail(Guid.NewGuid(), @"C:\previews\gone.jpg"));

        Assert.Null(exception);
    }

    [Fact]
    public void HasThumbnail_ReflectsAppliedStateForKnownAssetsOnly()
    {
        var model = new BrowserGridModel();
        var rootId = Guid.NewGuid();
        var entry = Image(rootId, "a.jpg");
        model.Populate([entry]);
        var assetId = Guid.NewGuid();
        model.ApplyAssetIdentities([new(assetId, entry.RelativePath, CatalogReconciliationItemStatus.New)]);

        Assert.False(model.HasThumbnail(assetId));
        model.ApplyThumbnail(assetId, @"C:\previews\a.jpg");
        Assert.True(model.HasThumbnail(assetId));
        Assert.False(model.HasThumbnail(Guid.NewGuid()));
    }

    [Fact]
    public void SelectSingle_ReplacesSelectionAndOnlyTouchesChangedTiles()
    {
        var model = new BrowserGridModel();
        var rootId = Guid.NewGuid();
        model.Populate([Image(rootId, "a.jpg"), Image(rootId, "b.jpg"), Image(rootId, "c.jpg")]);
        model.SelectSingle(0);

        model.SelectSingle(1);

        Assert.Equal([false, true, false], model.Tiles.Select(tile => tile.IsSelected));
    }

    [Fact]
    public void ToggleCtrl_AddsAndRemovesIndividualItemsWithoutClearingOthers()
    {
        var model = new BrowserGridModel();
        var rootId = Guid.NewGuid();
        model.Populate([Image(rootId, "a.jpg"), Image(rootId, "b.jpg"), Image(rootId, "c.jpg")]);
        model.SelectSingle(0);

        model.ToggleCtrl(2);
        Assert.Equal([true, false, true], model.Tiles.Select(tile => tile.IsSelected));

        model.ToggleCtrl(0);
        Assert.Equal([false, false, true], model.Tiles.Select(tile => tile.IsSelected));
    }

    [Fact]
    public void SelectRange_SelectsContiguousSpanFromAnchorAndDoesNotMoveTheAnchor()
    {
        var model = new BrowserGridModel();
        var rootId = Guid.NewGuid();
        model.Populate(Enumerable.Range(0, 6).Select(i => Image(rootId, $"{i}.jpg")).ToArray());
        model.SelectSingle(1);

        model.SelectRange(4);
        Assert.Equal([false, true, true, true, true, false], model.Tiles.Select(tile => tile.IsSelected));

        model.SelectRange(0);
        Assert.Equal([true, true, false, false, false, false], model.Tiles.Select(tile => tile.IsSelected));
    }

    [Fact]
    public void SelectAll_SelectsEveryTile()
    {
        var model = new BrowserGridModel();
        var rootId = Guid.NewGuid();
        model.Populate([Image(rootId, "a.jpg"), Image(rootId, "b.jpg")]);

        model.SelectAll();

        Assert.All(model.Tiles, tile => Assert.True(tile.IsSelected));
    }

    [Fact]
    public void ClearSelection_DeselectsEverything()
    {
        var model = new BrowserGridModel();
        var rootId = Guid.NewGuid();
        model.Populate([Image(rootId, "a.jpg"), Image(rootId, "b.jpg")]);
        model.SelectAll();

        model.ClearSelection();

        Assert.All(model.Tiles, tile => Assert.False(tile.IsSelected));
    }

    [Fact]
    public void Selection_SurvivesNonDestructiveRefreshWhenTheSelectedItemRemains()
    {
        var model = new BrowserGridModel();
        var rootId = Guid.NewGuid();
        model.Populate([Image(rootId, "a.jpg"), Image(rootId, "b.jpg")]);
        model.SelectSingle(0);

        model.Populate([Image(rootId, "a.jpg"), Image(rootId, "b.jpg"), Image(rootId, "c.jpg")]);

        Assert.True(model.Tiles.Single(tile => tile.Name == "a.jpg").IsSelected);
    }

    [Fact]
    public void Selection_DropsKeysForItemsRemovedByRefresh()
    {
        var model = new BrowserGridModel();
        var rootId = Guid.NewGuid();
        model.Populate([Image(rootId, "a.jpg"), Image(rootId, "b.jpg")]);
        model.SelectSingle(1);

        model.Populate([Image(rootId, "a.jpg")]);

        Assert.Empty(model.SelectedKeys);
    }

    [Fact]
    public void ApplyThumbnail_NeverChangesSelectionState()
    {
        var model = new BrowserGridModel();
        var rootId = Guid.NewGuid();
        var entry = Image(rootId, "a.jpg");
        model.Populate([entry]);
        model.SelectSingle(0);
        var assetId = Guid.NewGuid();
        model.ApplyAssetIdentities([new(assetId, entry.RelativePath, CatalogReconciliationItemStatus.New)]);

        model.ApplyThumbnail(assetId, @"C:\previews\a.jpg");

        Assert.True(model.Tiles.Single().IsSelected);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(150, 1)]
    [InlineData(168, 1)]
    [InlineData(360, 2)]
    [InlineData(720, 4)]
    public void ComputeColumns_FitsAsManyTilesAsAvailableWidthAllowsWithoutOverflow(double width, int expected) =>
        Assert.Equal(expected, BrowserGridLayout.ComputeColumns(width));

    [Fact]
    public void BuildRows_GroupsTilesIntoFixedWidthRowsInOrder()
    {
        var tiles = Enumerable.Range(0, 7).Select(i => new BrowserGridTile(Image(Guid.NewGuid(), $"{i}.jpg"), i)).ToArray();

        var rows = BrowserGridLayout.BuildRows(tiles, 3);

        Assert.Equal(3, rows.Count);
        Assert.Equal(3, rows[0].Count);
        Assert.Equal(3, rows[1].Count);
        Assert.Single(rows[2]);
        Assert.Equal(tiles, rows.SelectMany(row => row));
    }

    [Fact]
    public void SetColumns_ReflowsRowsWithoutLosingOrRecreatingTiles()
    {
        var model = new BrowserGridModel();
        var rootId = Guid.NewGuid();
        model.Populate(Enumerable.Range(0, 6).Select(i => Image(rootId, $"{i}.jpg")).ToArray());
        model.SetColumns(3);
        var firstPassTiles = model.Rows.SelectMany(row => row.Tiles).ToArray();

        model.SetColumns(2);

        Assert.Equal(3, model.Rows.Count);
        var secondPassTiles = model.Rows.SelectMany(row => row.Tiles).ToArray();
        Assert.Equal(firstPassTiles.OrderBy(t => t.Index), secondPassTiles.OrderBy(t => t.Index));
    }

    [Fact]
    public void SetColumns_SameValueIsANoOpAndDoesNotRebuildRows()
    {
        var model = new BrowserGridModel();
        var rootId = Guid.NewGuid();
        model.Populate(Enumerable.Range(0, 4).Select(i => Image(rootId, $"{i}.jpg")).ToArray());
        model.SetColumns(2);
        var rowsBefore = model.Rows.ToArray();

        model.SetColumns(2);

        Assert.Equal(rowsBefore, model.Rows.ToArray());
    }

    [Fact]
    public void LargeFolder_PopulatesAndReflowsWithoutLosingItemsOrOrder()
    {
        var model = new BrowserGridModel();
        var rootId = Guid.NewGuid();
        var entries = Enumerable.Range(0, 4000).Select(i => Image(rootId, $"img-{i:D5}.jpg")).ToArray();

        model.Populate(entries);
        model.SetColumns(6);

        Assert.Equal(4000, model.Tiles.Count);
        Assert.Equal(Enumerable.Range(0, 4000), model.Tiles.Select(tile => tile.Index));
        Assert.Equal(4000, model.Rows.Sum(row => row.Tiles.Count));
    }

    [Fact]
    public void Populate_EmptyFolderProducesNoTilesOrRows()
    {
        var model = new BrowserGridModel();

        model.Populate([]);

        Assert.Empty(model.Tiles);
        Assert.Empty(model.Rows);
    }

    [Fact]
    public void DerivedWorkProjection_IncludesFreshlyGeneratedAndFreshlyVerifiedThumbnails()
    {
        var generated = Guid.NewGuid();
        var current = Guid.NewGuid();
        var results = new[]
        {
            new DerivedWorkItemResult(generated, DerivedWorkItemOutcome.Generated, DerivedWorkComponentOutcome.NotNeeded, DerivedWorkComponentOutcome.Succeeded),
            new DerivedWorkItemResult(current, DerivedWorkItemOutcome.Current, DerivedWorkComponentOutcome.NotNeeded, DerivedWorkComponentOutcome.Current),
        };

        var pending = BrowserDerivedWorkProjection.AssetsNeedingThumbnailLookup(results, _ => false);

        Assert.Equal([generated, current], pending);
    }

    [Fact]
    public void DerivedWorkProjection_IncludesAlreadyCurrentThumbnailsTheSchedulerSkippedRegenerating()
    {
        // Regression: on a previously-viewed folder the scheduler recognizes the cached thumbnail is
        // already current and never calls the generator at all, reporting the thumbnail component as
        // NotNeeded rather than Succeeded/Current. The grid must still resolve that already-existing
        // cached path instead of leaving the tile on its placeholder forever.
        var alreadyCurrentFromPriorSession = Guid.NewGuid();
        var results = new[]
        {
            new DerivedWorkItemResult(alreadyCurrentFromPriorSession, DerivedWorkItemOutcome.Current,
                DerivedWorkComponentOutcome.NotNeeded, DerivedWorkComponentOutcome.NotNeeded),
        };

        var pending = BrowserDerivedWorkProjection.AssetsNeedingThumbnailLookup(results, _ => false);

        Assert.Equal([alreadyCurrentFromPriorSession], pending);
    }

    [Fact]
    public void DerivedWorkProjection_ExcludesAssetsWithNoViableThumbnailAndAlreadyAppliedAssets()
    {
        var failed = Guid.NewGuid();
        var skippedUnavailable = Guid.NewGuid();
        var canceled = Guid.NewGuid();
        var alreadyApplied = Guid.NewGuid();
        var results = new[]
        {
            new DerivedWorkItemResult(failed, DerivedWorkItemOutcome.Failed, DerivedWorkComponentOutcome.NotNeeded, DerivedWorkComponentOutcome.Failed),
            new DerivedWorkItemResult(skippedUnavailable, DerivedWorkItemOutcome.SkippedUnavailable, DerivedWorkComponentOutcome.SkippedUnavailable, DerivedWorkComponentOutcome.SkippedUnavailable),
            new DerivedWorkItemResult(canceled, DerivedWorkItemOutcome.Canceled, DerivedWorkComponentOutcome.Canceled, DerivedWorkComponentOutcome.Canceled),
            new DerivedWorkItemResult(alreadyApplied, DerivedWorkItemOutcome.Current, DerivedWorkComponentOutcome.NotNeeded, DerivedWorkComponentOutcome.Current),
        };

        var pending = BrowserDerivedWorkProjection.AssetsNeedingThumbnailLookup(results, assetId => assetId == alreadyApplied);

        Assert.Empty(pending);
    }

    private static MediaFolderEntry Directory(Guid rootId, string name) =>
        new(rootId, name, name.ToUpperInvariant(), name, true, MediaTypeClassification.Unknown, null, DateTimeOffset.UtcNow);

    private static MediaFolderEntry Image(Guid rootId, string name) => File(rootId, name, MediaTypeCategory.StillImage);

    private static MediaFolderEntry Video(Guid rootId, string name) => File(rootId, name, MediaTypeCategory.Video);

    private static MediaFolderEntry File(Guid rootId, string name, MediaTypeCategory category) =>
        new(rootId, name, name.ToUpperInvariant(), name, false, new(category), 10, DateTimeOffset.UtcNow);
}
