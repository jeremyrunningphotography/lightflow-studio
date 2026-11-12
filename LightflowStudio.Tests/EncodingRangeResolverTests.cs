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

    [Theory]
    [InlineData(0, .2, .16, .04)]
    [InlineData(5, .2, .16, .04)]
    [InlineData(0, .12, .08, .04)]
    public void Resolve_BoundedDecodedOutPreservesInclusiveFrameIntent(double start, double end, double last, double step)
    {
        var range = new MediaRange(TimeSpan.FromSeconds(end), null, TimeSpan.FromSeconds(end));
        var frames = "{\"frames\":[" + string.Join(',', new[] { start, start + last - step, start + last }
            .Distinct().Select(value => $"{{\"best_effort_timestamp_time\":\"{value}\",\"duration_time\":\"{step}\"}}")) + "]}";
        var result = EncodingRangeResolver.Resolve(range, TimeSpan.FromSeconds(start), frames,
            new(TimeSpan.FromSeconds(end), TimeSpan.FromSeconds(last + step), "1/1000000"));
        Assert.Equal(TimeSpan.FromSeconds(start + end), result.ExclusiveOut);
        Assert.NotNull(result.BoundaryDiagnostic);
        Assert.Equal(range, result.RequestedRange);
    }

    [Theory]
    [InlineData(.2, .1, .1)] // 50 ms cap
    [InlineData(.2, .16, .02)] // local interval
    [InlineData(.2, .22, .04)] // would select a later frame
    public void Resolve_RejectsUnprovenEofCorrection(double end, double last, double step)
        => Assert.Throws<ArgumentException>(() => EncodingRangeResolver.Resolve(
            new(TimeSpan.FromSeconds(end), null, TimeSpan.FromSeconds(end)), TimeSpan.Zero,
            Frames(0, last - step, last)));

    [Fact]
    public void Resolve_InteriorCorrectionUsesNearestEarlierFrameAndActualVfrSuccessor()
    {
        var result = EncodingRangeResolver.Resolve(new(TimeSpan.FromSeconds(1), null, TimeSpan.FromSeconds(.105)),
            TimeSpan.Zero, Frames(0, .08, .14, .18));
        Assert.Equal(TimeSpan.FromSeconds(.14), result.ExclusiveOut);
        Assert.NotNull(result.BoundaryDiagnostic);
    }

    [Fact]
    public void Resolve_AmbiguousMidpointRejectedAndExactPreferred()
    {
        Assert.Throws<ArgumentException>(() => EncodingRangeResolver.Resolve(
            new(TimeSpan.FromSeconds(1), null, TimeSpan.FromSeconds(.1)), TimeSpan.Zero, Frames(0, .08, .12)));
        Assert.Null(EncodingRangeResolver.Resolve(new(TimeSpan.FromSeconds(1), null, TimeSpan.FromSeconds(.08)),
            TimeSpan.Zero, Frames(0, .08, .12)).BoundaryDiagnostic);
    }

    [Fact]
    public void Resolve_PacketsCannotAuthorizeCorrection()
        => Assert.Throws<ArgumentException>(() => EncodingRangeResolver.Resolve(
            new(TimeSpan.FromSeconds(.2), null, TimeSpan.FromSeconds(.2)), TimeSpan.Zero,
            """{"packets":[{"pts_time":"0"},{"pts_time":"0.12"},{"pts_time":"0.16"}]}"""));

    [Fact]
    public void Resolve_DoesNotSkipHighFrameRateSuccessorWithinRoundingTolerance()
    {
        var result = EncodingRangeResolver.Resolve(new(TimeSpan.FromSeconds(.01), null, TimeSpan.FromSeconds(.003)),
            TimeSpan.Zero, Frames(0, .003, .004, .005));
        Assert.Equal(TimeSpan.FromSeconds(.004), result.ExclusiveOut);
    }

    private static string Frames(params double[] values) =>
        "{\"frames\":[" + string.Join(',', values.Select(value => $"{{\"best_effort_timestamp_time\":\"{value}\"}}")) + "]}";
}
