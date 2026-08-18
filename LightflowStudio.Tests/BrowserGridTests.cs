using LightflowStudio;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class BrowserGridTests
{
    [Fact]
    public void PresentableCategories_IsExactlyTheSetIsPresentableAccepts()
    {
        // The single authoritative source for "does the Browser show this category" — a category that's
        // presentable but missing from this list (or vice versa) would leave the quick-filter toolbar
        // (built from PresentableCategories) silently out of sync with what the grid actually shows.
        var rootId = Guid.NewGuid();
        foreach (var category in Enum.GetValues<MediaTypeCategory>())
        {
            var isPresentable = BrowserGridModel.IsPresentable(File(rootId, "a", category));
            Assert.Equal(BrowserGridModel.PresentableCategories.Contains(category), isPresentable);
        }
    }

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

        // Default query is Name-ascending, so this also verifies the filtered set sorts correctly by default.
        Assert.Equal(["clip.mp4", "photo.jpg", "raw.cr2"], model.Tiles.Select(tile => tile.Name));
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
    public void ComputeColumns_EveryTileReservesTrailingMarginIncludingTheLastInARow()
    {
        // Regression: the tile Border's Margin="0,0,12,12" trails every tile, including the last one in a
        // row — WPF measures that as part of each tile's layout footprint regardless of position. The old
        // formula assumed spacing sat only between tiles (no trailing margin on the last), which overcounted
        // columns at boundary widths: at 348px, 2 tiles need 2*(168+12)=360px and do not fit, but the old
        // formula returned 2 anyway. That produced a tile the row's panel could not actually place on the
        // line — an orphaned/misaligned partial row, most visible at narrow (near-minimum-window) widths.
        Assert.Equal(1, BrowserGridLayout.ComputeColumns(348));
        Assert.Equal(1, BrowserGridLayout.ComputeColumns(359));
        Assert.Equal(2, BrowserGridLayout.ComputeColumns(360));
        Assert.Equal(2, BrowserGridLayout.ComputeColumns(361));
        Assert.Equal(2, BrowserGridLayout.ComputeColumns(539));
        Assert.Equal(3, BrowserGridLayout.ComputeColumns(540));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(100)]
    [InlineData(167)]
    [InlineData(168)]
    [InlineData(179)]
    [InlineData(180)]
    [InlineData(181)]
    [InlineData(347)]
    [InlineData(348)]
    [InlineData(349)]
    [InlineData(359)]
    [InlineData(360)]
    [InlineData(361)]
    [InlineData(528)] // approx. media-canvas width at the app's declared MinWidth with the default Locations pane
    [InlineData(600)]
    [InlineData(788)] // approx. media-canvas width at the app's declared MinWidth with the Locations pane at its own minimum
    [InlineData(1000)]
    [InlineData(4000)]
    public void ComputeColumns_NeverClaimsMoreTilesFitThanTheirFootprintAllows(double availableWidth)
    {
        var columns = BrowserGridLayout.ComputeColumns(availableWidth);
        var footprintNeeded = columns * (BrowserGridLayout.TileWidth + BrowserGridLayout.TileSpacing);

        // A single column is always allowed even if it technically overflows narrow width, so at least one
        // tile is always shown; for any larger column count the full row must actually fit.
        Assert.True(columns == 1 || footprintNeeded <= availableWidth,
            $"{columns} columns at width {availableWidth} would need {footprintNeeded}px and overflow.");
    }

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
    public void SetColumns_RepeatedNarrowToWideResizeReflowsWithoutLossDuplicationOrReordering()
    {
        // Models continuously resizing the application window (and dragging the Locations splitter) between
        // narrow and wide: every intermediate column count driven by ComputeColumns at realistic widths,
        // including the app's declared-minimum-size-equivalent canvas width, down to a single column.
        var model = new BrowserGridModel();
        var rootId = Guid.NewGuid();
        var entries = Enumerable.Range(0, 17).Select(i => Image(rootId, $"{i:D2}.jpg")).ToArray();
        model.Populate(entries);

        double[] widths = [1200, 788, 348, 168, 528, 4000, 359, 1000, 180, 600, 0];
        foreach (var width in widths)
        {
            model.SetColumns(BrowserGridLayout.ComputeColumns(width));

            Assert.Equal(17, model.Tiles.Count);
            Assert.Equal(entries.Select(e => e.Name), model.Tiles.Select(t => t.Name));
            Assert.Equal(Enumerable.Range(0, 17), model.Tiles.Select(t => t.Index));
            Assert.Equal(17, model.Rows.Sum(row => row.Tiles.Count));
            Assert.Equal(model.Tiles, model.Rows.SelectMany(row => row.Tiles));
            Assert.All(model.Rows.Take(model.Rows.Count - 1),
                row => Assert.Equal(BrowserGridLayout.ComputeColumns(width), row.Tiles.Count));
        }
    }

    [Fact]
    public void SetColumns_LocationsPaneResizeAtApplicationMinimumWidthReflowsCleanly()
    {
        // Approximate media-canvas widths at the app's declared MinWidth=1120, before (default ~280px pane)
        // and after dragging the Locations splitter to its own minimum (220px pane) — the exact scenario
        // from the reported bug.
        var model = new BrowserGridModel();
        var rootId = Guid.NewGuid();
        var entries = Enumerable.Range(0, 9).Select(i => Image(rootId, $"{i}.jpg")).ToArray();
        model.Populate(entries);

        model.SetColumns(BrowserGridLayout.ComputeColumns(528));
        var afterDefaultPane = model.Rows.SelectMany(row => row.Tiles).ToArray();
        Assert.Equal(entries.Select(e => e.Name), afterDefaultPane.Select(t => t.Name));

        model.SetColumns(BrowserGridLayout.ComputeColumns(788));
        var afterNarrowedPane = model.Rows.SelectMany(row => row.Tiles).ToArray();
        Assert.Equal(entries.Select(e => e.Name), afterNarrowedPane.Select(t => t.Name));
        Assert.Equal(afterDefaultPane.OrderBy(t => t.Index), afterNarrowedPane.OrderBy(t => t.Index));
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

    [Fact]
    public void DerivedWorkProjection_MetadataLookupMirrorsThumbnailLookupIncludingTheNotNeededCase()
    {
        var freshlyProbed = Guid.NewGuid();
        var alreadyCurrentFromPriorSession = Guid.NewGuid();
        var failed = Guid.NewGuid();
        var alreadyApplied = Guid.NewGuid();
        var results = new[]
        {
            new DerivedWorkItemResult(freshlyProbed, DerivedWorkItemOutcome.Generated, DerivedWorkComponentOutcome.Succeeded, DerivedWorkComponentOutcome.NotNeeded),
            new DerivedWorkItemResult(alreadyCurrentFromPriorSession, DerivedWorkItemOutcome.Current, DerivedWorkComponentOutcome.NotNeeded, DerivedWorkComponentOutcome.NotNeeded),
            new DerivedWorkItemResult(failed, DerivedWorkItemOutcome.Failed, DerivedWorkComponentOutcome.Failed, DerivedWorkComponentOutcome.NotNeeded),
            new DerivedWorkItemResult(alreadyApplied, DerivedWorkItemOutcome.Current, DerivedWorkComponentOutcome.NotNeeded, DerivedWorkComponentOutcome.NotNeeded),
        };

        var pending = BrowserDerivedWorkProjection.AssetsNeedingMetadataLookup(results, assetId => assetId == alreadyApplied);

        Assert.Equal([freshlyProbed, alreadyCurrentFromPriorSession], pending);
    }

    [Fact]
    public void SetQuery_FiltersToTheChosenMediaCategory()
    {
        var model = new BrowserGridModel();
        var rootId = Guid.NewGuid();
        model.Populate([Image(rootId, "a.jpg"), Video(rootId, "b.mp4")]);

        model.SetQuery(BrowserQuery.Default with { Filters = [BrowserFilterPredicate.ForMediaType(MediaTypeCategory.Video)] });

        Assert.Equal(["b.mp4"], model.Tiles.Select(t => t.Name));
        Assert.Equal(2, model.TotalCount);
        Assert.Equal(1, model.VisibleCount);
    }

    [Fact]
    public void SetQuery_ReassignsIndexToPositionsWithinTheNewVisibleListOnly()
    {
        var model = new BrowserGridModel();
        var rootId = Guid.NewGuid();
        model.Populate([Image(rootId, "a.jpg"), Video(rootId, "b.mp4"), Image(rootId, "c.jpg")]);

        model.SetQuery(BrowserQuery.Default with { Filters = [BrowserFilterPredicate.ForMediaType(MediaTypeCategory.StillImage)] });

        Assert.Equal([0, 1], model.Tiles.Select(t => t.Index));
    }

    [Fact]
    public void SetQuery_AlwaysResetsTheSelectionAnchorEvenForAQueryWithEquivalentContent()
    {
        // BrowserQuery.Filters is a list, so record equality on BrowserQuery only catches reference-identical
        // predicate collections, not equivalent ones (see BrowserGridModel.SetQuery) — every call recomputes
        // and resets the anchor, including one that happens to describe the same intent as before.
        var model = new BrowserGridModel();
        var rootId = Guid.NewGuid();
        model.Populate(Enumerable.Range(0, 4).Select(i => Image(rootId, $"{i}.jpg")).ToArray());
        model.SelectSingle(1);

        model.SetQuery(BrowserQuery.Default);
        model.SelectRange(3);

        // With the anchor cleared, SelectRange(3) with no anchor treats 3 as its own anchor and selects only
        // index 3, not a 1..3 span.
        Assert.Equal([false, false, false, true], model.Tiles.Select(t => t.IsSelected));
    }

    [Fact]
    public void SetQuery_NeverDeselectsItemsThatRemainVisible()
    {
        var model = new BrowserGridModel();
        var rootId = Guid.NewGuid();
        model.Populate([Image(rootId, "a.jpg"), Video(rootId, "b.mp4")]);
        model.SelectSingle(0);

        model.SetQuery(BrowserQuery.Default with { SearchText = "a" });

        Assert.True(model.Tiles.Single().IsSelected);
    }

    [Fact]
    public void SetQuery_DoesNotClearSelectionOnItemsHiddenByTheNewFilterTheyRemainSelectedIfShownAgain()
    {
        // Filtering never silently drops a selection just because the item is temporarily out of view —
        // only an explicit action (Select All, Clear, folder navigation) changes what is selected.
        var model = new BrowserGridModel();
        var rootId = Guid.NewGuid();
        model.Populate([Image(rootId, "a.jpg"), Video(rootId, "b.mp4")]);
        model.SelectAll();

        model.SetQuery(BrowserQuery.Default with { Filters = [BrowserFilterPredicate.ForMediaType(MediaTypeCategory.Video)] });
        Assert.Equal(2, model.SelectedKeys.Count);

        model.SetQuery(BrowserQuery.Default);
        Assert.True(model.Tiles.Single(t => t.Name == "a.jpg").IsSelected);
    }

    [Fact]
    public void SelectAll_ReplacesTheEntireSelectionWithOnlyWhatTheCurrentFilterShows()
    {
        // Select All is a deliberate replace action (standard Ctrl+A semantics): unlike an implicit filter
        // change, it intentionally drops a prior selection outside the now-visible set.
        var model = new BrowserGridModel();
        var rootId = Guid.NewGuid();
        var image = Image(rootId, "a.jpg");
        model.Populate([image, Video(rootId, "b.mp4")]);
        model.SelectAll();
        Assert.Equal(2, model.SelectedKeys.Count);

        model.SetQuery(BrowserQuery.Default with { Filters = [BrowserFilterPredicate.ForMediaType(MediaTypeCategory.Video)] });
        model.SelectAll();

        Assert.Single(model.SelectedKeys);
        Assert.DoesNotContain(image.RelativePathKey, model.SelectedKeys);
        Assert.True(model.Tiles.Single(t => t.Name == "b.mp4").IsSelected);
    }

    [Fact]
    public void Populate_PreservesTheCurrentQueryAcrossANonDestructiveRefresh()
    {
        var model = new BrowserGridModel();
        var rootId = Guid.NewGuid();
        model.Populate([Image(rootId, "a.jpg"), Video(rootId, "b.mp4")]);
        model.SetQuery(BrowserQuery.Default with { Filters = [BrowserFilterPredicate.ForMediaType(MediaTypeCategory.Video)] });

        model.Populate([Image(rootId, "a.jpg"), Video(rootId, "b.mp4"), Video(rootId, "c.mp4")]);

        Assert.Equal(["b.mp4", "c.mp4"], model.Tiles.Select(t => t.Name));
    }

    [Fact]
    public void ApplyMetadata_UpdatesOnlyTheMatchingAssetAndReportsWhetherASortableValueChanged()
    {
        var model = new BrowserGridModel();
        var rootId = Guid.NewGuid();
        var entry = Image(rootId, "a.jpg");
        model.Populate([entry]);
        var assetId = Guid.NewGuid();
        model.ApplyAssetIdentities([new(assetId, entry.RelativePath, CatalogReconciliationItemStatus.New)]);

        var firstApply = model.ApplyMetadata(assetId, new DateTime(2024, 1, 1), null);
        var redundantApply = model.ApplyMetadata(assetId, new DateTime(2024, 1, 1), null);

        Assert.True(firstApply);
        Assert.False(redundantApply);
        Assert.Equal(new DateTime(2024, 1, 1), model.Tiles.Single().CaptureDate);
    }

    [Fact]
    public void ApplyMetadata_ForAssetNoLongerPresentIsIgnoredRatherThanThrowing()
    {
        var model = new BrowserGridModel();
        model.Populate([]);

        var exception = Record.Exception(() => model.ApplyMetadata(Guid.NewGuid(), DateTime.UtcNow, 5.0));

        Assert.Null(exception);
    }

    [Fact]
    public void HasMetadataApplied_ReflectsAppliedStateForKnownAssetsOnly()
    {
        var model = new BrowserGridModel();
        var rootId = Guid.NewGuid();
        var entry = Image(rootId, "a.jpg");
        model.Populate([entry]);
        var assetId = Guid.NewGuid();
        model.ApplyAssetIdentities([new(assetId, entry.RelativePath, CatalogReconciliationItemStatus.New)]);

        Assert.False(model.HasMetadataApplied(assetId));
        model.ApplyMetadata(assetId, null, null);
        Assert.True(model.HasMetadataApplied(assetId));
        Assert.False(model.HasMetadataApplied(Guid.NewGuid()));
    }

    [Fact]
    public void SelectedTotalSizeBytes_SumsSelectedItemsRegardlessOfWhatTheCurrentFilterShows()
    {
        var model = new BrowserGridModel();
        var rootId = Guid.NewGuid();
        model.Populate([SizedImage(rootId, "a.jpg", 1000), SizedFile(rootId, "b.mp4", MediaTypeCategory.Video, 2000)]);
        model.SelectAll();

        model.SetQuery(BrowserQuery.Default with { Filters = [BrowserFilterPredicate.ForMediaType(MediaTypeCategory.Video)] });

        Assert.Equal(3000, model.SelectedTotalSizeBytes);
    }

    [Fact]
    public void ReapplyQuery_RecomputesOrderWhenUnderlyingMetadataChangedWithoutTheQueryItselfChanging()
    {
        var model = new BrowserGridModel();
        var rootId = Guid.NewGuid();
        var entryA = Image(rootId, "a.jpg");
        var entryB = Image(rootId, "b.jpg");
        model.Populate([entryA, entryB]);
        var assetA = Guid.NewGuid();
        var assetB = Guid.NewGuid();
        model.ApplyAssetIdentities([new(assetA, entryA.RelativePath, CatalogReconciliationItemStatus.New),
            new(assetB, entryB.RelativePath, CatalogReconciliationItemStatus.New)]);
        model.SetQuery(BrowserQuery.Default with { SortMode = BrowserSortMode.CaptureDate });
        // Both currently missing a capture date: falls back to name order.
        Assert.Equal(["a.jpg", "b.jpg"], model.Tiles.Select(t => t.Name));

        model.ApplyMetadata(assetB, new DateTime(2020, 1, 1), null);
        model.ReapplyQuery();

        Assert.Equal(["b.jpg", "a.jpg"], model.Tiles.Select(t => t.Name));
    }

    private static MediaFolderEntry Directory(Guid rootId, string name) =>
        new(rootId, name, name.ToUpperInvariant(), name, true, MediaTypeClassification.Unknown, null, DateTimeOffset.UtcNow);

    private static MediaFolderEntry Image(Guid rootId, string name) => File(rootId, name, MediaTypeCategory.StillImage);

    private static MediaFolderEntry Video(Guid rootId, string name) => File(rootId, name, MediaTypeCategory.Video);

    private static MediaFolderEntry File(Guid rootId, string name, MediaTypeCategory category) =>
        new(rootId, name, name.ToUpperInvariant(), name, false, new(category), 10, DateTimeOffset.UtcNow);

    private static MediaFolderEntry SizedImage(Guid rootId, string name, long sizeBytes) =>
        SizedFile(rootId, name, MediaTypeCategory.StillImage, sizeBytes);

    private static MediaFolderEntry SizedFile(Guid rootId, string name, MediaTypeCategory category, long sizeBytes) =>
        new(rootId, name, name.ToUpperInvariant(), name, false, new(category), sizeBytes, DateTimeOffset.UtcNow);
}

public sealed class StringEmptyToVisibilityConverterTests
{
    private readonly StringEmptyToVisibilityConverter _converter = new();

    [Fact]
    public void Convert_IsVisibleForNull() =>
        Assert.Equal(System.Windows.Visibility.Visible, _converter.Convert(null, typeof(System.Windows.Visibility), null, null!));

    [Fact]
    public void Convert_IsVisibleForEmptyString() =>
        Assert.Equal(System.Windows.Visibility.Visible, _converter.Convert("", typeof(System.Windows.Visibility), null, null!));

    [Fact]
    public void Convert_IsCollapsedWhenTextIsPresent() =>
        Assert.Equal(System.Windows.Visibility.Collapsed, _converter.Convert("sunset.jpg", typeof(System.Windows.Visibility), null, null!));

    [Fact]
    public void ConvertBack_IsNotSupported() =>
        Assert.Throws<NotSupportedException>(() => _converter.ConvertBack(null, typeof(string), null, null!));
}
