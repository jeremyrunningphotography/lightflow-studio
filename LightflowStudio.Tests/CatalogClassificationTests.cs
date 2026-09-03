using LightflowStudio;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class CatalogClassificationTests
{
    [Fact]
    public async Task Classification_RoundTripsByStableAssetIdentityAndNormalizesKeywords()
    {
        var root = Path.Combine(Path.GetTempPath(), $"lightflow-classification-{Guid.NewGuid():N}");
        var locations = LightflowStorageLocations.Create(root);
        var session = (await new CatalogDatabaseService(locations).CreateNewAsync()).Session!;
        try
        {
            var rootId = Guid.NewGuid();
            var assetId = Guid.NewGuid();
            using (var connection = session.OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = """
                    INSERT INTO MediaRoots (RootId,DisplayName,SourceStatus,CreatedUtc,UpdatedUtc) VALUES ($root,'Media','online',$now,$now);
                    INSERT INTO MediaAssets (AssetId,RootId,RelativePath,RelativePathKey,MediaType,FileSizeBytes,LastWriteUtcTicks,SourceStatus,CreatedUtc,UpdatedUtc)
                    VALUES ($asset,$root,'clip.mp4','CLIP.MP4','video',1,1,'available',$now,$now);
                    """;
                command.Parameters.AddWithValue("$root", rootId.ToString("D"));
                command.Parameters.AddWithValue("$asset", assetId.ToString("D"));
                command.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O"));
                command.ExecuteNonQuery();
            }
            var store = new CatalogAssetClassificationStore(() => session);
            await store.SaveAsync(new(assetId, 4, AssetFlag.Picked, AssetColorLabel.Blue,
                [" Ceremony ", "favorites", "CEREMONY", ""]));

            var restored = (await store.GetAsync([assetId]))[assetId];

            Assert.Equal(4, restored.Rating);
            Assert.Equal(AssetFlag.Picked, restored.Flag);
            Assert.Equal(AssetColorLabel.Blue, restored.ColorLabel);
            Assert.Equal(["Ceremony", "favorites"], restored.Keywords);
            Assert.Equal(1, restored.Revision);
        }
        finally
        {
            await session.DisposeAsync();
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void DirectRatingClick_TogglesOnlyTheCurrentlySelectedNonzeroRating()
    {
        Assert.Equal(4, AssetClassificationCommandPolicy.SetRating(2, 4, toggleCurrent: true));
        Assert.Equal(0, AssetClassificationCommandPolicy.SetRating(4, 4, toggleCurrent: true));
        Assert.Equal(4, AssetClassificationCommandPolicy.SetRating(4, 4, toggleCurrent: false));
        Assert.Equal(0, AssetClassificationCommandPolicy.SetRating(4, 0, toggleCurrent: false));
    }

    [Theory]
    [InlineData(-1, 1, 0)]
    [InlineData(0, 1, 1)]
    [InlineData(1, 1, 1)]
    [InlineData(1, -1, 0)]
    [InlineData(0, -1, -1)]
    [InlineData(-1, -1, -1)]
    public void FlagShortcut_UsesOrderedClampedTransitions(int current, int delta, int expected) =>
        Assert.Equal((AssetFlag)expected, AssetClassificationCommandPolicy.StepFlag((AssetFlag)current, delta));

    [Fact]
    public void QueryClassificationFacets_UseMinimumRatingAndExactOtherValues()
    {
        var rootId = Guid.NewGuid();
        var assetId = Guid.NewGuid();
        var tile = new BrowserGridTile(new(rootId, "clip.mp4", "CLIP.MP4", "clip.mp4", false,
            new(MediaTypeCategory.Video), 1, DateTimeOffset.UtcNow, AssetId: assetId), 0);
        tile.SetAssetState(new BrowserAssetQueryState(BrowserAssetState.None, false, false, 0,
            new(assetId, 4, AssetFlag.Picked, AssetColorLabel.Blue, ["ceremony"])));

        Assert.True(BrowserFilterPredicate.ForMinimum(BrowserFilterField.Rating, 3).Matches(tile));
        Assert.True(BrowserFilterPredicate.ForText(BrowserFilterField.Flag, "Picked").Matches(tile));
        Assert.True(BrowserFilterPredicate.ForText(BrowserFilterField.ColorLabel, "Blue").Matches(tile));
        Assert.True(BrowserFilterPredicate.ForText(BrowserFilterField.Keyword, "CEREMONY").Matches(tile));
        Assert.False(BrowserFilterPredicate.ForMinimum(BrowserFilterField.Rating, 5).Matches(tile));
    }
}
