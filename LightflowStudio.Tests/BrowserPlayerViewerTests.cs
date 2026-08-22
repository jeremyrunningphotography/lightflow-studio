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
        var a = new PlayerViewerAsset(rootId, "Trip/clip.mp4", "trip/clip.mp4", "clip.mp4", MediaPresentationKind.Video);
        var b = new PlayerViewerAsset(rootId, "Trip/clip.mp4", "trip/clip.mp4", "clip.mp4", MediaPresentationKind.Video);

        Assert.Equal(a, b);
        Assert.Equal(MediaPresentationKind.Video, a.Kind);
    }
}
