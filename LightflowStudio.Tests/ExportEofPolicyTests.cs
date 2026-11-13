using System.Globalization;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class ExportEofPolicyTests
{
    [Theory]
    [InlineData(8.256, 493493, 8.241567)]
    [InlineData(15.786667, 945945, 15.782433)]
    public void ActualDjiEof_RetainsSourceEndpointIncludingAudioTail(double eof, long lastPts, double videoEnd)
    {
        var requested = new MediaRange(TimeSpan.FromSeconds(eof), null, TimeSpan.FromSeconds(eof));
        var result = EncodingRangeResolver.Resolve(requested, TimeSpan.Zero, Frames(lastPts),
            new(TimeSpan.FromSeconds(eof), TimeSpan.FromSeconds(videoEnd), "1/60000"));
        Assert.Equal(requested.SourceDuration, result.ExclusiveOut);
        Assert.Equal(requested, result.RequestedRange);
        Assert.Contains("endpoint retained unchanged", result.BoundaryDiagnostic);
        Assert.Contains("final video and audio", result.BoundaryDiagnostic);
    }

    [Theory]
    [InlineData(15.786667, 15.8, "1/60000")] // stream end does not match decoded terminal frame
    [InlineData(15.9, 15.782433, "1/60000")] // excessive EOF tail
    [InlineData(15.76, 15.782433, "1/60000")] // decoded frame extends beyond EOF
    [InlineData(15.786667, 15.782433, "0/0")]
    public void UnsafeOrAmbiguousTerminalEvidenceIsRejected(double eof, double videoEnd, string timeBase)
        => Assert.Throws<ArgumentException>(() => EncodingRangeResolver.Resolve(
            new(TimeSpan.FromSeconds(eof), null, TimeSpan.FromSeconds(eof)), TimeSpan.Zero, Frames(945945),
            new(TimeSpan.FromSeconds(eof), TimeSpan.FromSeconds(videoEnd), timeBase)));

    [Fact]
    public void StaleDurationAndMissingDecodedDurationAreRejected()
    {
        var range = new MediaRange(TimeSpan.FromSeconds(15.786667), null, TimeSpan.FromSeconds(15.786667));
        Assert.Throws<ArgumentException>(() => EncodingRangeResolver.Resolve(range, TimeSpan.Zero, Frames(945945),
            new(TimeSpan.FromSeconds(16), TimeSpan.FromSeconds(15.782433), "1/60000")));
        Assert.Throws<ArgumentException>(() => EncodingRangeResolver.Resolve(range, TimeSpan.Zero,
            """{"frames":[{"best_effort_timestamp_time":"15.765750"}]}""",
            new(range.SourceDuration, TimeSpan.FromSeconds(15.782433), "1/60000")));
    }

    [Fact]
    public void NonzeroStreamStartAndVariableTerminalDurationUseActualTimeBase()
    {
        var range = new MediaRange(TimeSpan.FromSeconds(.2), null, TimeSpan.FromSeconds(.2));
        const string json = """{"frames":[{"best_effort_timestamp":5100,"best_effort_timestamp_time":"5.1","duration":70},{"best_effort_timestamp":5170,"best_effort_timestamp_time":"5.17","duration":20}]}""";
        var result = EncodingRangeResolver.Resolve(range, TimeSpan.FromSeconds(5), json,
            new(range.SourceDuration, TimeSpan.FromSeconds(.19), "1/1000"));
        Assert.Equal(TimeSpan.FromSeconds(5.2), result.ExclusiveOut);
    }

    private static string Frames(long pts) => "{\"frames\":[" + string.Join(',', new[] { pts - 1001, pts }.Select(value =>
        $"{{\"best_effort_timestamp\":{value},\"best_effort_timestamp_time\":\"{(value / 60000d).ToString("F6", CultureInfo.InvariantCulture)}\",\"duration\":1001}}")) + "]}";
}
