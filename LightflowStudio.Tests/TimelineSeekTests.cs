using LightflowStudio;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class TimelineSeekTests
{
    private static readonly TimeSpan Duration = TimeSpan.FromSeconds(100);

    [Fact]
    public void UntrimmedSelection_InitializesAtPlayableBeginning() =>
        Assert.Equal(TimeSpan.Zero, new TrimSelection(Duration).InitialPlaybackPosition);

    [Fact]
    public void InOnlySelection_InitializesAtInPoint()
    {
        var range = new MediaRange(Duration, TimeSpan.FromSeconds(20));
        Assert.Equal(TimeSpan.FromSeconds(20), new TrimSelection(Duration, range).InitialPlaybackPosition);
    }

    [Fact]
    public void InAndOutSelection_InitializesAtInPoint()
    {
        var range = new MediaRange(Duration, TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(70));
        Assert.Equal(TimeSpan.FromSeconds(20), new TrimSelection(Duration, range).InitialPlaybackPosition);
    }

    [Fact]
    public void RestoredRange_UsesTheSameAuthoritativeInTimestamp()
    {
        var restored = new MediaRange(Duration, TimeSpan.FromTicks(123456789), TimeSpan.FromSeconds(80));
        Assert.Equal(restored.EffectiveIn, new TrimSelection(Duration, restored).InitialPlaybackPosition);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(25, 25)]
    [InlineData(50, 50)]
    [InlineData(100, 100)]
    public void CoordinateMapsDirectlyAcrossTimeline(double x, double expectedSeconds) =>
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), TimelineSeek.PositionFromCoordinate(x, 100, Duration));

    [Theory]
    [InlineData(-10, 0)]
    [InlineData(110, 100)]
    [InlineData(double.NaN, 0)]
    public void CoordinateClampsSafelyToMediaBounds(double x, double expectedSeconds) =>
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), TimelineSeek.PositionFromCoordinate(x, 100, Duration));

    [Fact]
    public void InvalidTimelineGeometry_ReturnsPlayableBeginning()
    {
        Assert.Equal(TimeSpan.Zero, TimelineSeek.PositionFromCoordinate(10, 0, Duration));
        Assert.Equal(TimeSpan.Zero, TimelineSeek.PositionFromCoordinate(10, 100, TimeSpan.Zero));
    }

    [Fact]
    public void OrdinaryTrackClickSeeksButPlayheadAndTrimMarkersKeepTheirOwnInteraction()
    {
        Assert.True(TimelineSeek.ShouldSeek(TimelinePointerTarget.Track));
        Assert.False(TimelineSeek.ShouldSeek(TimelinePointerTarget.PlayheadThumb));
        Assert.False(TimelineSeek.ShouldSeek(TimelinePointerTarget.TrimMarker));
    }
}

/// <summary>Backs PlayerViewerHost's VolumeSlider click-to-set behavior — see SliderClickToSet's own doc comment.</summary>
public sealed class SliderClickToSetTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(25, 25)]
    [InlineData(50, 50)]
    [InlineData(100, 100)]
    public void CoordinateMapsDirectlyAcrossTheSliderRange(double x, double expectedValue) =>
        Assert.Equal(expectedValue, SliderClickToSet.ValueFromCoordinate(x, 100, 0, 100));

    [Theory]
    [InlineData(-10, 0)]
    [InlineData(110, 100)]
    [InlineData(double.NaN, 0)]
    public void CoordinateClampsSafelyToTheSliderRange(double x, double expectedValue) =>
        Assert.Equal(expectedValue, SliderClickToSet.ValueFromCoordinate(x, 100, 0, 100));

    [Fact]
    public void NonZeroMinimum_OffsetsTheMappedValue() =>
        Assert.Equal(30, SliderClickToSet.ValueFromCoordinate(50, 100, 10, 50));

    [Fact]
    public void InvalidSliderGeometry_ReturnsMinimum()
    {
        Assert.Equal(0, SliderClickToSet.ValueFromCoordinate(10, 0, 0, 100));
        Assert.Equal(0, SliderClickToSet.ValueFromCoordinate(10, 100, 0, 0));
    }
}
