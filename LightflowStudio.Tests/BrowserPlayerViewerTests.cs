using Xunit;

namespace LightflowStudio.Tests;

public sealed class BrowserPlayerViewerTests
{
    [Theory]
    [InlineData(MediaTypeCategory.StillImage, MediaPresentationKind.Image)]
    [InlineData(MediaTypeCategory.RawImage, MediaPresentationKind.Image)]
    [InlineData(MediaTypeCategory.Video, MediaPresentationKind.Video)]
    internal void KindFor_MapsEveryPresentableBrowserCategory(MediaTypeCategory category, MediaPresentationKind expected) =>
        Assert.Equal(expected, MediaPresentationClassification.KindFor(category));

    [Fact]
    public void KindFor_NonPresentableCategoryThrows()
    {
        // BrowserGridModel.IsPresentable never admits Audio/Other/Unsupported into the grid at all, so
        // KindFor should never legitimately see them either — an exhaustive switch with no silent fallback.
        Assert.Throws<ArgumentOutOfRangeException>(() => MediaPresentationClassification.KindFor(MediaTypeCategory.Audio));
    }

    [Fact]
    public void PlayerViewerAsset_IsAHostAgnosticValueRecord()
    {
        var rootId = Guid.NewGuid();
        var assetId = Guid.NewGuid();
        var a = new PlayerViewerAsset(rootId, "Trip/clip.mp4", "trip/clip.mp4", "clip.mp4", MediaPresentationKind.Video, assetId);
        var b = new PlayerViewerAsset(rootId, "Trip/clip.mp4", "trip/clip.mp4", "clip.mp4", MediaPresentationKind.Video, assetId);

        Assert.Equal(a, b);
        Assert.Equal(MediaPresentationKind.Video, a.Kind);
    }

    [Fact]
    public void RangePlayback_ArmsAtOrBeforeOut_AndStopsWhenOutIsReached()
    {
        var range = new MediaRange(TimeSpan.FromSeconds(100), TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(70));

        Assert.True(ReviewRangePlaybackPolicy.ShouldArmOutBoundary(range, TimeSpan.FromSeconds(10)));
        Assert.True(ReviewRangePlaybackPolicy.ShouldArmOutBoundary(range, TimeSpan.FromSeconds(40)));
        Assert.True(ReviewRangePlaybackPolicy.ShouldArmOutBoundary(range, TimeSpan.FromSeconds(70)));
        Assert.False(ReviewRangePlaybackPolicy.HasReachedArmedOutBoundary(range, true, TimeSpan.FromSeconds(69)));
        Assert.True(ReviewRangePlaybackPolicy.HasReachedArmedOutBoundary(range, true, TimeSpan.FromSeconds(70)));
    }

    [Fact]
    public void RangePlayback_AfterOut_RemainsUnconstrained()
    {
        var range = new MediaRange(TimeSpan.FromSeconds(100), TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(70));

        Assert.False(ReviewRangePlaybackPolicy.ShouldArmOutBoundary(range, TimeSpan.FromSeconds(71)));
        Assert.False(ReviewRangePlaybackPolicy.HasReachedArmedOutBoundary(range, false, TimeSpan.FromSeconds(90)));
        Assert.False(ReviewRangePlaybackPolicy.ShouldArmOutBoundary(null, TimeSpan.Zero));
    }
}
