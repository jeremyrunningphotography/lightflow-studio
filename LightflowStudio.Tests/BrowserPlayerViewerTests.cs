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

    [Theory]
    [InlineData(10d, 20d, 5d, 5d, 20d)]
    [InlineData(10d, 20d, 20d, 20d, null)]
    [InlineData(10d, 20d, 30d, 30d, null)]
    public void SetIn_NewBoundaryWinsAndOnlyClearsConflictingOut(
        double currentIn, double currentOut, double requestedIn, double expectedIn, double? expectedOut)
    {
        var duration = TimeSpan.FromSeconds(60);
        var current = new MediaRange(duration, TimeSpan.FromSeconds(currentIn), TimeSpan.FromSeconds(currentOut));

        var result = ReviewRangeBoundaryPolicy.SetIn(duration, current, TimeSpan.FromSeconds(requestedIn));

        Assert.Equal(TimeSpan.FromSeconds(expectedIn), result.In);
        Assert.Equal(expectedOut is null ? null : TimeSpan.FromSeconds(expectedOut.Value), result.Out);
        Assert.Empty(result.Validate());
    }

    [Theory]
    [InlineData(10d, 20d, 30d, 10d, 30d)]
    [InlineData(10d, 20d, 10d, null, 10d)]
    [InlineData(10d, 20d, 5d, null, 5d)]
    public void SetOut_NewBoundaryWinsAndOnlyClearsConflictingIn(
        double currentIn, double currentOut, double requestedOut, double? expectedIn, double expectedOut)
    {
        var duration = TimeSpan.FromSeconds(60);
        var current = new MediaRange(duration, TimeSpan.FromSeconds(currentIn), TimeSpan.FromSeconds(currentOut));

        var result = ReviewRangeBoundaryPolicy.SetOut(duration, current, TimeSpan.FromSeconds(requestedOut));

        Assert.Equal(expectedIn is null ? null : TimeSpan.FromSeconds(expectedIn.Value), result.In);
        Assert.Equal(TimeSpan.FromSeconds(expectedOut), result.Out);
        Assert.Empty(result.Validate());
    }

    [Fact]
    public void TimelineWithNoBoundaries_SelectsFullSourceWithoutMarkers()
    {
        var presentation = PlayerRangeTimelinePresentation.For(null, TimeSpan.FromSeconds(100));

        Assert.True(presentation.HasSelectedSpan);
        Assert.True(presentation.HasProportions);
        Assert.False(presentation.ShowBoundaries);
        Assert.Equal(0, presentation.StartFraction);
        Assert.Equal(1, presentation.WidthFraction);

        var explicitFullSourceValue = PlayerRangeTimelinePresentation.For(
            new MediaRange(TimeSpan.FromSeconds(100)), TimeSpan.FromSeconds(100));
        Assert.Equal(presentation, explicitFullSourceValue);
    }

    [Theory]
    [InlineData(null, 70d, 0d, .7)]
    [InlineData(20d, null, .2, .8)]
    [InlineData(20d, 70d, .2, .5)]
    public void TimelineWithSavedBoundaries_ProjectsEffectiveSpanAndShowsMarkers(
        double? rangeIn, double? rangeOut, double expectedStart, double expectedWidth)
    {
        var duration = TimeSpan.FromSeconds(100);
        var range = new MediaRange(duration,
            rangeIn is null ? null : TimeSpan.FromSeconds(rangeIn.Value),
            rangeOut is null ? null : TimeSpan.FromSeconds(rangeOut.Value));

        var presentation = PlayerRangeTimelinePresentation.For(range, duration);

        Assert.True(presentation.HasSelectedSpan);
        Assert.True(presentation.HasProportions);
        Assert.True(presentation.ShowBoundaries);
        Assert.Equal(expectedStart, presentation.StartFraction, 6);
        Assert.Equal(expectedWidth, presentation.WidthFraction, 6);
    }

    [Theory]
    [InlineData(true, true, false, true)]
    [InlineData(true, false, true, true)]
    [InlineData(true, true, true, true)]
    [InlineData(true, false, false, false)]
    [InlineData(false, true, true, false)]
    public void SubclipEligibility_UsesOneTypedPolicyForTargetIntentAndSourceCompletion(
        bool catalogVideo, bool hasIn, bool hasOut, bool expected)
    {
        var duration = TimeSpan.FromSeconds(60);
        var range = hasIn || hasOut
            ? new MediaRange(duration, hasIn ? TimeSpan.FromSeconds(10) : null,
                hasOut ? TimeSpan.FromSeconds(20) : null)
            : null;

        var result = SubclipCreationEligibility.Evaluate(catalogVideo, range, duration);

        Assert.Equal(expected, result.CanCreate);
        Assert.Equal(expected, result.MaterializedRange is not null);
    }

    [Fact]
    public void SubclipEligibility_RejectsUnavailableDurationAndMaterializesExactSourceBounds()
    {
        var inOnly = new MediaRange(TimeSpan.FromSeconds(60), In: TimeSpan.FromTicks(123456789));
        var unavailable = SubclipCreationEligibility.Evaluate(true, inOnly, null);
        var invalid = SubclipCreationEligibility.Evaluate(true, inOnly, TimeSpan.Zero);
        var valid = SubclipCreationEligibility.Evaluate(true, inOnly, TimeSpan.FromTicks(987654321));

        Assert.False(unavailable.CanCreate);
        Assert.False(invalid.CanCreate);
        Assert.Equal((inOnly.In, TimeSpan.FromTicks(987654321)),
            (valid.MaterializedRange?.In, valid.MaterializedRange?.Out));
    }
}
