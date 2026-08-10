using LightflowStudio;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class EncodingFrameProbeWindowTests
{
    [Fact]
    public void SeparateBoundaries_ProduceTwoSmallAbsoluteEndWindows()
    {
        var range = new MediaRange(TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(25));

        Assert.Equal("298%302,1498%1502", EncodingFrameProbeWindow.For(range));
    }

    [Fact]
    public void NearbyBoundaries_MergeAndClampToSource()
    {
        var range = new MediaRange(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3));

        Assert.Equal("0%5", EncodingFrameProbeWindow.For(range));
    }

    [Theory]
    [InlineData(true, "7%10")]
    [InlineData(false, "0%4")]
    public void SingleBoundary_ProducesOnlyRequiredWindow(bool startOnly, string expected)
    {
        var range = new MediaRange(TimeSpan.FromSeconds(10),
            startOnly ? TimeSpan.FromSeconds(9) : null,
            startOnly ? null : TimeSpan.FromSeconds(2));

        Assert.Equal(expected, EncodingFrameProbeWindow.For(range));
    }

    [Fact]
    public void NonZeroSourceStart_ProducesContainerTimelineIntervals()
    {
        var range = new MediaRange(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(9));

        Assert.Equal("12%15", EncodingFrameProbeWindow.For(range, TimeSpan.FromSeconds(5)));
    }
}
