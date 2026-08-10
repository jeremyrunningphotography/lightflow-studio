using LightflowStudio;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class TrimIndicatorTests
{
    [Fact]
    public void ActiveRange_UsesProportionalStartAndWidth()
    {
        var range = new MediaRange(TimeSpan.FromSeconds(100), TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(70));
        var indicator = TrimIndicatorPresentation.For(range, TimeSpan.FromSeconds(100));
        Assert.True(indicator.HasActiveTrim);
        Assert.True(indicator.HasProportions);
        Assert.Equal(.2, indicator.StartFraction, 6);
        Assert.Equal(.5, indicator.WidthFraction, 6);
    }

    [Fact]
    public void UntrimmedAndUnavailableDuration_AreNeutralAndGraceful()
    {
        var unknown = TrimIndicatorPresentation.For(null, null);
        Assert.False(unknown.HasActiveTrim);
        Assert.False(unknown.HasProportions);
        var known = TrimIndicatorPresentation.For(null, TimeSpan.FromSeconds(10));
        Assert.False(known.HasActiveTrim);
        Assert.True(known.HasProportions);
        Assert.Equal(1, known.WidthFraction);
    }

    [Fact]
    public void BatchOptionTreatsRestoredTrimAsOrdinaryActiveTrim()
    {
        var option = new BatchFileOption("video.mp4", "video.mp4", 10);
        option.ApplyTrim(new(TimeSpan.FromSeconds(100), TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(70)));
        Assert.True(option.HasActiveTrim);
        Assert.Equal("Edit Trim", option.TrimActionText);
        Assert.True(option.TrimIndicatorHasProportions);
    }
}
