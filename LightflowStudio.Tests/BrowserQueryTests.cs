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

        var result = BrowserQueryEngine.Apply(model.Tiles, BrowserQuery.Default with { MediaFilter = MediaTypeCategory.Video });

        Assert.Equal(["c.mp4"], result.Select(t => t.Name));
    }

    [Fact]
    public void Apply_NullMediaFilterMeansEveryPresentableCategory()
    {
        var model = new BrowserGridModel();
        var rootId = Guid.NewGuid();
        model.Populate([Entry(rootId, "a.jpg", MediaTypeCategory.StillImage), Entry(rootId, "b.cr2", MediaTypeCategory.RawImage),
            Entry(rootId, "c.mp4", MediaTypeCategory.Video)]);

        var result = BrowserQueryEngine.Apply(model.Tiles, BrowserQuery.Default with { MediaFilter = null });

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

        var result = BrowserQueryEngine.Apply(model.Tiles,
            BrowserQuery.Default with { MediaFilter = MediaTypeCategory.Video, SearchText = "beach" });

        Assert.Equal(["beach.mp4"], result.Select(t => t.Name));
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
