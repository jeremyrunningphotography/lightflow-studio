using LightflowStudio;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class BrowserQueryTests
{
    [Fact]
    public void Apply_DefaultQuerySortsByNameAscendingCaseInsensitively()
    {
        var tiles = Tiles(("banana.jpg", 1), ("Apple.jpg", 2), ("cherry.jpg", 3));

        var result = BrowserQueryEngine.Apply(tiles, BrowserQuery.Default);

        Assert.Equal(["Apple.jpg", "banana.jpg", "cherry.jpg"], result.Select(t => t.Name));
    }

    [Fact]
    public void Apply_MediaFilterRestrictsToOneCategory()
    {
        var model = new BrowserGridModel();
        var rootId = Guid.NewGuid();
        model.Populate([Entry(rootId, "a.jpg", MediaTypeCategory.StillImage), Entry(rootId, "b.cr2", MediaTypeCategory.RawImage),
            Entry(rootId, "c.mp4", MediaTypeCategory.Video)]);

        var result = BrowserQueryEngine.Apply(model.Tiles,
            BrowserQuery.Default with { Filters = [BrowserFilterPredicate.ForMediaType(MediaTypeCategory.Video)] });

        Assert.Equal(["c.mp4"], result.Select(t => t.Name));
    }

    [Fact]
    public void Apply_NoActiveFiltersMeansEveryPresentableCategory()
    {
        var model = new BrowserGridModel();
        var rootId = Guid.NewGuid();
        model.Populate([Entry(rootId, "a.jpg", MediaTypeCategory.StillImage), Entry(rootId, "b.cr2", MediaTypeCategory.RawImage),
            Entry(rootId, "c.mp4", MediaTypeCategory.Video)]);

        var result = BrowserQueryEngine.Apply(model.Tiles, BrowserQuery.Default);

        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void Apply_SearchMatchesFilenameCaseInsensitively()
    {
        var tiles = Tiles(("Sunset-Beach.jpg", 1), ("mountain.jpg", 2));

        var result = BrowserQueryEngine.Apply(tiles, BrowserQuery.Default with { SearchText = "beach" });

        Assert.Equal(["Sunset-Beach.jpg"], result.Select(t => t.Name));
    }

    [Fact]
    public void Apply_SearchAlsoMatchesRelativePathNotJustFilename()
    {
        var model = new BrowserGridModel();
        var rootId = Guid.NewGuid();
        model.Populate([new MediaFolderEntry(rootId, "Iceland/img1.jpg", "ICELAND/IMG1.JPG", "img1.jpg", false,
            new(MediaTypeCategory.StillImage), 10, DateTimeOffset.UtcNow)]);

        var result = BrowserQueryEngine.Apply(model.Tiles, BrowserQuery.Default with { SearchText = "iceland" });

        Assert.Single(result);
    }

    [Fact]
    public void Apply_BlankOrWhitespaceSearchTextMatchesEverything()
    {
        var tiles = Tiles(("a.jpg", 1), ("b.jpg", 2));

        var result = BrowserQueryEngine.Apply(tiles, BrowserQuery.Default with { SearchText = "   " });

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void Apply_SearchAndFilterCombine()
    {
        var model = new BrowserGridModel();
        var rootId = Guid.NewGuid();
        model.Populate([Entry(rootId, "beach.jpg", MediaTypeCategory.StillImage), Entry(rootId, "beach.mp4", MediaTypeCategory.Video),
            Entry(rootId, "mountain.mp4", MediaTypeCategory.Video)]);

        var result = BrowserQueryEngine.Apply(model.Tiles, BrowserQuery.Default with
        {
            Filters = [BrowserFilterPredicate.ForMediaType(MediaTypeCategory.Video)],
            SearchText = "beach"
        });

        Assert.Equal(["beach.mp4"], result.Select(t => t.Name));
    }

    [Fact]
    public void Apply_StackingTwoMediaTypePredicatesOrsWithinTheSameField()
    {
        // Mockup-driven faceted-search semantics: predicates for the SAME field are alternative values of
        // one facet and OR together (checking "Video" and "Images" means either is acceptable), unlike the
        // AND-across-different-fields composition used when facets differ.
        var model = new BrowserGridModel();
        var rootId = Guid.NewGuid();
        model.Populate([
            Entry(rootId, "a.jpg", MediaTypeCategory.StillImage),
            Entry(rootId, "b.mp4", MediaTypeCategory.Video),
            Entry(rootId, "c.raw", MediaTypeCategory.RawImage)
        ]);

        var result = BrowserQueryEngine.Apply(model.Tiles, BrowserQuery.Default with
        {
            Filters =
            [
                BrowserFilterPredicate.ForMediaType(MediaTypeCategory.Video),
                BrowserFilterPredicate.ForMediaType(MediaTypeCategory.StillImage)
            ]
        });

        Assert.Equal(["a.jpg", "b.mp4"], result.Select(t => t.Name));
    }

    [Fact]
    public void Apply_EveryIndividualMediaTypeExplicitlyActiveYieldsTheSameTilesAsNoFilterAtAll()
    {
        // The two states are results-equivalent but must stay distinct selections in the query itself —
        // Filters here still literally holds three predicates, not an empty/normalized list.
        var model = new BrowserGridModel();
        var rootId = Guid.NewGuid();
        model.Populate([
            Entry(rootId, "a.jpg", MediaTypeCategory.StillImage),
            Entry(rootId, "b.mp4", MediaTypeCategory.Video),
            Entry(rootId, "c.raw", MediaTypeCategory.RawImage)
        ]);
        var query = BrowserQuery.Default with
        {
            Filters = [.. BrowserGridModel.PresentableCategories.Select(BrowserFilterPredicate.ForMediaType)]
        };

        var result = BrowserQueryEngine.Apply(model.Tiles, query);

        Assert.Equal(3, query.Filters.Count);
        Assert.Equal(BrowserQueryEngine.Apply(model.Tiles, BrowserQuery.Default).Select(t => t.Name), result.Select(t => t.Name));
    }

    [Fact]
    public void Apply_SortDescendingReversesTheEntireOrderIncludingTieBreakers()
    {
        var tiles = Tiles(("a.jpg", 1), ("b.jpg", 2), ("c.jpg", 3));

        var result = BrowserQueryEngine.Apply(tiles, BrowserQuery.Default with { SortDescending = true });

        Assert.Equal(["c.jpg", "b.jpg", "a.jpg"], result.Select(t => t.Name));
    }

    [Fact]
    public void Apply_SortByFileSizeOrdersSmallestToLargestAscending()
    {
        var model = new BrowserGridModel();
        var rootId = Guid.NewGuid();
        model.Populate([SizedEntry(rootId, "big.jpg", 3000), SizedEntry(rootId, "small.jpg", 100), SizedEntry(rootId, "medium.jpg", 500)]);

        var result = BrowserQueryEngine.Apply(model.Tiles, BrowserQuery.Default with { SortMode = BrowserSortMode.FileSize });

        Assert.Equal(["small.jpg", "medium.jpg", "big.jpg"], result.Select(t => t.Name));
    }

    [Fact]
    public void Apply_SortByModifiedDateUsesFilesystemTimestampWithoutAnyProbe()
    {
        var model = new BrowserGridModel();
        var rootId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        model.Populate([
            new MediaFolderEntry(rootId, "newest.jpg", "NEWEST.JPG", "newest.jpg", false, new(MediaTypeCategory.StillImage), 10, now),
            new MediaFolderEntry(rootId, "oldest.jpg", "OLDEST.JPG", "oldest.jpg", false, new(MediaTypeCategory.StillImage), 10, now.AddDays(-5)),
            new MediaFolderEntry(rootId, "middle.jpg", "MIDDLE.JPG", "middle.jpg", false, new(MediaTypeCategory.StillImage), 10, now.AddDays(-2)),
        ]);

        var result = BrowserQueryEngine.Apply(model.Tiles, BrowserQuery.Default with { SortMode = BrowserSortMode.ModifiedDate });

        Assert.Equal(["oldest.jpg", "middle.jpg", "newest.jpg"], result.Select(t => t.Name));
    }

    [Fact]
    public void Apply_SortByMediaTypeGroupsStillImagesThenRawThenVideo()
    {
        var model = new BrowserGridModel();
        var rootId = Guid.NewGuid();
        model.Populate([Entry(rootId, "v.mp4", MediaTypeCategory.Video), Entry(rootId, "i.jpg", MediaTypeCategory.StillImage),
            Entry(rootId, "r.cr2", MediaTypeCategory.RawImage)]);

        var result = BrowserQueryEngine.Apply(model.Tiles, BrowserQuery.Default with { SortMode = BrowserSortMode.MediaType });

        Assert.Equal([MediaTypeCategory.StillImage, MediaTypeCategory.RawImage, MediaTypeCategory.Video], result.Select(t => t.Category));
    }

    [Fact]
    public void Apply_ItemsMissingCaptureDateSortToTheEndRegardlessOfDirection()
    {
        var model = new BrowserGridModel();
        var rootId = Guid.NewGuid();
        model.Populate([Entry(rootId, "known.jpg", MediaTypeCategory.StillImage), Entry(rootId, "unknown.jpg", MediaTypeCategory.StillImage)]);
        var known = model.Tiles.Single(t => t.Name == "known.jpg");
        known.ApplyMetadata(new DateTime(2024, 3, 15), null);

        var ascending = BrowserQueryEngine.Apply(model.Tiles, BrowserQuery.Default with { SortMode = BrowserSortMode.CaptureDate });
        Assert.Equal(["known.jpg", "unknown.jpg"], ascending.Select(t => t.Name));

        var descending = BrowserQueryEngine.Apply(model.Tiles, BrowserQuery.Default with { SortMode = BrowserSortMode.CaptureDate, SortDescending = true });
        Assert.Equal(["known.jpg", "unknown.jpg"], descending.Select(t => t.Name));
    }

    [Fact]
    public void Apply_SortByCaptureDateOrdersEarliestToLatestAmongItemsThatHaveIt()
    {
        var model = new BrowserGridModel();
        var rootId = Guid.NewGuid();
        model.Populate([Entry(rootId, "a.jpg", MediaTypeCategory.StillImage), Entry(rootId, "b.jpg", MediaTypeCategory.StillImage)]);
        model.Tiles.Single(t => t.Name == "a.jpg").ApplyMetadata(new DateTime(2024, 6, 1), null);
        model.Tiles.Single(t => t.Name == "b.jpg").ApplyMetadata(new DateTime(2024, 1, 1), null);

        var result = BrowserQueryEngine.Apply(model.Tiles, BrowserQuery.Default with { SortMode = BrowserSortMode.CaptureDate });

        Assert.Equal(["b.jpg", "a.jpg"], result.Select(t => t.Name));
    }

    [Fact]
    public void Apply_SortByDurationOrdersShortestToLongestAndExcludesNonVideoWithoutADuration()
    {
        var model = new BrowserGridModel();
        var rootId = Guid.NewGuid();
        model.Populate([Entry(rootId, "long.mp4", MediaTypeCategory.Video), Entry(rootId, "short.mp4", MediaTypeCategory.Video),
            Entry(rootId, "photo.jpg", MediaTypeCategory.StillImage)]);
        model.Tiles.Single(t => t.Name == "long.mp4").ApplyMetadata(null, 120.0);
        model.Tiles.Single(t => t.Name == "short.mp4").ApplyMetadata(null, 5.0);

        var result = BrowserQueryEngine.Apply(model.Tiles, BrowserQuery.Default with { SortMode = BrowserSortMode.Duration });

        Assert.Equal(["short.mp4", "long.mp4", "photo.jpg"], result.Select(t => t.Name));
    }

    [Theory]
    [InlineData("2024:03:15 10:30:00", 2024, 3, 15, 10, 30, 0)]
    [InlineData("2024:12:01", 2024, 12, 1, 0, 0, 0)]
    public void ParseExifCaptureDate_ParsesStandardExifDateFormats(string raw, int year, int month, int day, int hour, int minute, int second)
    {
        var parsed = BrowserQueryEngine.ParseExifCaptureDate(raw);

        Assert.Equal(new DateTime(year, month, day, hour, minute, second), parsed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a date")]
    [InlineData("2024-03-15")]
    public void ParseExifCaptureDate_ReturnsNullForMissingOrUnrecognizedText(string? raw) =>
        Assert.Null(BrowserQueryEngine.ParseExifCaptureDate(raw));

    [Fact]
    public void ExtractSortableMetadata_ReadsCaptureDateFromImageMetadataJson()
    {
        var json = """{"kind":"Image","fileSizeBytes":10,"image":{"format":"JPEG","width":100,"height":100,"capturedAt":"2024:03:15 10:30:00"}}""";

        var (captureDate, duration) = BrowserQueryEngine.ExtractSortableMetadata(json);

        Assert.Equal(new DateTime(2024, 3, 15, 10, 30, 0), captureDate);
        Assert.Null(duration);
    }

    [Fact]
    public void ExtractSortableMetadata_ReadsDurationFromVideoMetadataJson()
    {
        var json = """{"kind":"Video","fileSizeBytes":10,"durationSeconds":42.5}""";

        var (captureDate, duration) = BrowserQueryEngine.ExtractSortableMetadata(json);

        Assert.Null(captureDate);
        Assert.Equal(42.5, duration);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{}")]
    public void ExtractSortableMetadata_ReturnsNothingRatherThanThrowingForMissingOrMalformedJson(string? json)
    {
        var (captureDate, duration) = BrowserQueryEngine.ExtractSortableMetadata(json);

        Assert.Null(captureDate);
        Assert.Null(duration);
    }

    [Fact]
    public void Apply_MetadataFacetsUseOnlyHydratedNormalizedPreviewValues()
    {
        var tiles = Tiles(("r5.mp4", 1), ("other.mp4", 2), ("pending.mp4", 3));
        tiles[0].ApplyMetadata(new BrowserTechnicalMetadata(new DateTime(2025, 6, 10), 45,
            "Canon", "EOS R5 Mark II", "RF28-70mm F2 L USM", 8192, 4320, 59.94));
        tiles[1].ApplyMetadata(new BrowserTechnicalMetadata(new DateTime(2023, 1, 1), 12,
            "Sony", "ILCE-7SM3", "FE 24-70mm F2.8 GM", 3840, 2160, 29.97));
        var query = BrowserQuery.Default with { Filters =
        [
            BrowserFilterPredicate.ForText(BrowserFilterField.Camera, "Canon EOS R5 Mark II"),
            BrowserFilterPredicate.ForText(BrowserFilterField.Lens, "RF28-70mm F2 L USM"),
            BrowserFilterPredicate.ForDateRange(new DateTime(2025, 1, 1), new DateTime(2025, 12, 31)),
            BrowserFilterPredicate.ForMinimum(BrowserFilterField.Duration, 30),
            BrowserFilterPredicate.ForResolution(8192, 4320),
            BrowserFilterPredicate.ForMinimum(BrowserFilterField.FrameRate, 59.94)
        ] };

        var result = BrowserQueryEngine.Apply(tiles, query);

        Assert.Equal(["r5.mp4"], result.Select(tile => tile.Name));
        Assert.False(tiles[2].MetadataApplied);
    }

    [Fact]
    public void Apply_LightflowStateFacetsDistinguishHydratedFalseFromUnavailable()
    {
        var tiles = Tiles(("colored.mp4", 1), ("original.mp4", 2), ("pending.mp4", 3));
        tiles[0].SetAssetState(new BrowserAssetQueryState(
            BrowserAssetState.Color | BrowserAssetState.ReviewRange | BrowserAssetState.Subclips, true, true, 2));
        tiles[1].SetAssetState(new BrowserAssetQueryState(BrowserAssetState.None, false, false, 0));

        var colored = BrowserQueryEngine.Apply(tiles, BrowserQuery.Default with { Filters =
        [
            BrowserFilterPredicate.ForState(BrowserFilterField.ColorState, true),
            BrowserFilterPredicate.ForState(BrowserFilterField.CameraLutState, true),
            BrowserFilterPredicate.ForState(BrowserFilterField.CreativeLutState, true),
            BrowserFilterPredicate.ForState(BrowserFilterField.ReviewRangeState, true),
            BrowserFilterPredicate.ForState(BrowserFilterField.SubclipState, true)
        ] });
        var original = BrowserQueryEngine.Apply(tiles, BrowserQuery.Default with { Filters =
        [
            BrowserFilterPredicate.ForState(BrowserFilterField.ColorState, false),
            BrowserFilterPredicate.ForState(BrowserFilterField.ReviewRangeState, false),
            BrowserFilterPredicate.ForState(BrowserFilterField.SubclipState, false)
        ] });

        Assert.Equal(["colored.mp4"], colored.Select(tile => tile.Name));
        Assert.Equal(["original.mp4"], original.Select(tile => tile.Name));
    }

    [Fact]
    public void AdvancedFilterContext_AppliesMediaTypeAndSearchButIgnoresEveryAdvancedPredicate()
    {
        var model = new BrowserGridModel();
        var rootId = Guid.NewGuid();
        model.Populate([
            Entry(rootId, "trip-canon.mp4", MediaTypeCategory.Video),
            Entry(rootId, "trip-sony.mp4", MediaTypeCategory.Video),
            Entry(rootId, "other-canon.mp4", MediaTypeCategory.Video),
            Entry(rootId, "trip-photo.jpg", MediaTypeCategory.StillImage)
        ]);
        model.Tiles.Single(tile => tile.Name == "trip-canon.mp4").ApplyMetadata(
            new BrowserTechnicalMetadata(null, 60, "Canon", "R5", null, 3840, 2160, 59.94));
        model.Tiles.Single(tile => tile.Name == "trip-sony.mp4").ApplyMetadata(
            new BrowserTechnicalMetadata(null, 60, "Sony", "FX3", null, 1920, 1080, 23.976));
        model.Tiles.Single(tile => tile.Name == "other-canon.mp4").ApplyMetadata(
            new BrowserTechnicalMetadata(null, 60, "Canon", "R5", null, 3840, 2160, 59.94));
        var query = BrowserQuery.Default with
        {
            SearchText = "trip",
            Filters =
            [
                BrowserFilterPredicate.ForMediaType(MediaTypeCategory.Video),
                BrowserFilterPredicate.ForText(BrowserFilterField.Camera, "Canon R5")
            ]
        };

        model.SetQuery(query);
        var context = model.AdvancedFilterContextTiles;
        var filtered = model.Tiles;

        Assert.Equal(["trip-canon.mp4", "trip-sony.mp4"], context.Select(tile => tile.Name));
        Assert.Equal(["trip-canon.mp4"], filtered.Select(tile => tile.Name));
    }

    [Fact]
    public void ExtractMetadataProjectsCameraLensResolutionAndFrameRateFromNormalizedPayload()
    {
        var json = """{"kind":"Video","durationSeconds":42.5,"video":{"codec":"h264","width":3840,"height":2160,"frameRate":23.976}}""";

        var metadata = BrowserQueryEngine.ExtractMetadata(json);

        Assert.Equal(3840, metadata.PixelWidth);
        Assert.Equal(2160, metadata.PixelHeight);
        Assert.Equal(23.976, metadata.FrameRate);
    }

    private static IReadOnlyList<BrowserGridTile> Tiles(params (string Name, int Index)[] items)
    {
        var rootId = Guid.NewGuid();
        return items.Select(item => new BrowserGridTile(Entry(rootId, item.Name, MediaTypeCategory.StillImage), item.Index)).ToArray();
    }

    private static MediaFolderEntry Entry(Guid rootId, string name, MediaTypeCategory category) =>
        new(rootId, name, name.ToUpperInvariant(), name, false, new(category), 10, DateTimeOffset.UtcNow);

    private static MediaFolderEntry SizedEntry(Guid rootId, string name, long sizeBytes) =>
        new(rootId, name, name.ToUpperInvariant(), name, false, new(MediaTypeCategory.StillImage), sizeBytes, DateTimeOffset.UtcNow);
}

public sealed class BrowserFilterPredicateTests
{
    [Fact]
    public void Label_IsImagesForStillImageCategory() => AssertLabel(MediaTypeCategory.StillImage, "Images");

    [Fact]
    public void Label_IsRawForRawImageCategory() => AssertLabel(MediaTypeCategory.RawImage, "RAW");

    [Fact]
    public void Label_IsVideoForVideoCategory() => AssertLabel(MediaTypeCategory.Video, "Video");

    private static void AssertLabel(MediaTypeCategory category, string expectedLabel) =>
        Assert.Equal(expectedLabel, BrowserFilterPredicate.ForMediaType(category).Label);

    [Fact]
    public void RemoveAutomationLabel_NamesTheSpecificFilterBeingRemoved() =>
        Assert.Equal("Remove Video filter", BrowserFilterPredicate.ForMediaType(MediaTypeCategory.Video).RemoveAutomationLabel);

    [Fact]
    public void OriginalColorLabel_RemainsUnderstandableAsAStandaloneChip() =>
        Assert.Equal("Original color", BrowserFilterPredicate.ForState(BrowserFilterField.ColorState, false).Label);

    [Fact]
    public void TwoPredicatesForTheSameConditionAreStructurallyEqual()
    {
        // Equality (not reference identity) is what WithFilterAdded/WithFilterRemoved rely on, and what
        // would let a future Smart Collection compare/deduplicate saved query intent.
        var first = BrowserFilterPredicate.ForMediaType(MediaTypeCategory.Video);
        var second = BrowserFilterPredicate.ForMediaType(MediaTypeCategory.Video);

        Assert.Equal(first, second);
        Assert.True(first == second);
    }

    [Fact]
    public void Matches_RestrictsToTheExactMediaTypeCategory()
    {
        var predicate = BrowserFilterPredicate.ForMediaType(MediaTypeCategory.Video);
        var video = new BrowserGridTile(Entry(Guid.NewGuid(), "a.mp4", MediaTypeCategory.Video), 0);
        var image = new BrowserGridTile(Entry(Guid.NewGuid(), "b.jpg", MediaTypeCategory.StillImage), 1);

        Assert.True(predicate.Matches(video));
        Assert.False(predicate.Matches(image));
    }

    private static MediaFolderEntry Entry(Guid rootId, string name, MediaTypeCategory category) =>
        new(rootId, name, name.ToUpperInvariant(), name, false, new(category), 10, DateTimeOffset.UtcNow);
}

public sealed class BrowserQueryFilterMutationTests
{
    [Fact]
    public void WithFilterAdded_AppendsANewPredicate()
    {
        var predicate = BrowserFilterPredicate.ForMediaType(MediaTypeCategory.Video);

        var query = BrowserQuery.Default.WithFilterAdded(predicate);

        Assert.Equal([predicate], query.Filters);
    }

    [Fact]
    public void WithFilterAdded_IsIdempotentForAnAlreadyActivePredicate()
    {
        var predicate = BrowserFilterPredicate.ForMediaType(MediaTypeCategory.Video);
        var query = BrowserQuery.Default.WithFilterAdded(predicate);

        var reapplied = query.WithFilterAdded(predicate);

        Assert.Equal([predicate], reapplied.Filters);
    }

    [Fact]
    public void WithFilterAdded_SupportsStackingDistinctPredicates()
    {
        var video = BrowserFilterPredicate.ForMediaType(MediaTypeCategory.Video);
        var images = BrowserFilterPredicate.ForMediaType(MediaTypeCategory.StillImage);

        var query = BrowserQuery.Default.WithFilterAdded(video).WithFilterAdded(images);

        Assert.Equal([video, images], query.Filters);
    }

    [Fact]
    public void WithFilterRemoved_DropsExactlyTheGivenPredicate()
    {
        var video = BrowserFilterPredicate.ForMediaType(MediaTypeCategory.Video);
        var images = BrowserFilterPredicate.ForMediaType(MediaTypeCategory.StillImage);
        var query = BrowserQuery.Default.WithFilterAdded(video).WithFilterAdded(images);

        var result = query.WithFilterRemoved(video);

        Assert.Equal([images], result.Filters);
    }

    [Fact]
    public void WithFilterRemoved_IsANoOpForAPredicateThatIsNotActive() =>
        Assert.Same(BrowserQuery.Default, BrowserQuery.Default.WithFilterRemoved(BrowserFilterPredicate.ForMediaType(MediaTypeCategory.Video)));

    [Fact]
    public void DefaultQuery_HasNoActiveFilters() =>
        Assert.Empty(BrowserQuery.Default.Filters);

    [Fact]
    public void WithoutField_ClearsEveryPredicateForThatFieldOnly()
    {
        var video = BrowserFilterPredicate.ForMediaType(MediaTypeCategory.Video);
        var other = new BrowserFilterPredicate { Field = (BrowserFilterField)999 };
        var query = BrowserQuery.Default.WithFilterAdded(video).WithFilterAdded(other);

        var result = query.WithoutField(BrowserFilterField.MediaType);

        Assert.Equal([other], result.Filters);
    }

    [Fact]
    public void WithoutField_IsANoOpWhenTheFieldHasNoActivePredicates() =>
        Assert.Same(BrowserQuery.Default, BrowserQuery.Default.WithoutField(BrowserFilterField.MediaType));
}
