using LightflowStudio;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class EncodingRangeResolverTests
{
    [Fact]
    public void Resolve_TranslatesNormalizedCfrRangeAndMakesOutFrameInclusive()
    {
        var requested = new MediaRange(TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        var result = EncodingRangeResolver.Resolve(requested, TimeSpan.FromSeconds(5), Frames(5, 5.5, 6, 6.5, 7, 7.5, 8, 8.5));

        Assert.Equal(TimeSpan.FromSeconds(6), result.AbsoluteIn);
        Assert.Equal(TimeSpan.FromSeconds(7.5), result.ExclusiveOut);
        Assert.Equal(TimeSpan.FromSeconds(1.5), result.EffectiveDuration);
    }

    [Fact]
    public void Resolve_UsesActualNextVfrTimestamp()
    {
        var requested = new MediaRange(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(.05), TimeSpan.FromSeconds(.31));
        var result = EncodingRangeResolver.Resolve(requested, TimeSpan.Zero, Frames(0, .05, .12, .31, .48, .80));

        Assert.Equal(TimeSpan.FromSeconds(.43), result.EffectiveDuration);
        Assert.Equal(TimeSpan.FromSeconds(.48), result.ExclusiveOut);
    }

    [Fact]
    public void Resolve_AllowsStartOnlyEndOnlyAndVeryShortRanges()
    {
        var frames = Frames(0, .1, .2, .3, .4);
        Assert.Equal(.3, EncodingRangeResolver.Resolve(new(TimeSpan.FromSeconds(.5), TimeSpan.FromSeconds(.2)), TimeSpan.Zero, frames).EffectiveDuration.TotalSeconds, 3);
        Assert.Equal(.3, EncodingRangeResolver.Resolve(new(TimeSpan.FromSeconds(.5), null, TimeSpan.FromSeconds(.2)), TimeSpan.Zero, frames).EffectiveDuration.TotalSeconds, 3);
        Assert.Equal(.2, EncodingRangeResolver.Resolve(new(TimeSpan.FromSeconds(.5), TimeSpan.FromSeconds(.2), TimeSpan.FromSeconds(.3)), TimeSpan.Zero, frames).EffectiveDuration.TotalSeconds, 3);
    }

    [Fact]
    public void Resolve_RejectsStaleOrInvalidPersistedBoundary()
    {
        Assert.Throws<ArgumentException>(() => EncodingRangeResolver.Resolve(
            new(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(.15), TimeSpan.FromSeconds(.3)), TimeSpan.Zero, Frames(0, .1, .2, .3)));
    }

    [Fact]
    public void Resolve_AcceptsPacketPresentationTimestamps()
    {
        var requested = new MediaRange(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(.1), TimeSpan.FromSeconds(.3));
        const string packets = """{"packets":[{"pts_time":"0.0"},{"pts_time":"0.1"},{"pts_time":"0.3"},{"pts_time":"0.45"}]}""";

        Assert.Equal(TimeSpan.FromSeconds(.35), EncodingRangeResolver.Resolve(requested, TimeSpan.Zero, packets).EffectiveDuration);
    }

    private static string Frames(params double[] values) =>
        "{\"frames\":[" + string.Join(',', values.Select(value => $"{{\"best_effort_timestamp_time\":\"{value}\"}}")) + "]}";
}
