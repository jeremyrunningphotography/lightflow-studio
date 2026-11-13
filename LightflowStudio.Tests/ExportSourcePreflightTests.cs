using Xunit;

namespace LightflowStudio.Tests;

public sealed class ExportSourcePreflightTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("export-source-probe-").FullName;
    private EncodingHandoffInput Input()
    {
        var path = Path.Combine(_root, "source.mp4");
        File.WriteAllText(path, "fixture");
        return new(Guid.NewGuid(), Guid.NewGuid(), path, "source", 7, null, SourceIdentity: TrimSourceIdentity.Read(path));
    }

    [Fact]
    public async Task ProbeFailureRetainsActionableDiagnostic()
    {
        var service = new ExportSourcePreflight("ffprobe", new Runner(new(1, "", "permission denied")));
        var probe = await service.ProbeAsync(Input());
        Assert.Null(probe.Metadata);
        Assert.Contains("permission denied", probe.Diagnostic);
    }

    [Fact]
    public async Task ProbeSelectsActualDjiVideoBeforeLaterMjpegAndRetainsEofEvidence()
    {
        const string json = """{"streams":[{"codec_type":"video","codec_name":"hevc","width":3840,"height":2160,"avg_frame_rate":"60000/1001","time_base":"1/60000","start_time":"0","duration":"15.782433"},{"codec_type":"video","codec_name":"mjpeg","width":960,"height":540,"avg_frame_rate":"0/0"}],"format":{"format_name":"mov,mp4,m4a,3gp,3g2,mj2","duration":"15.786667","start_time":"0"}}""";
        var probe = await new ExportSourcePreflight("ffprobe", new Runner(new(0, json, ""))).ProbeAsync(Input());
        Assert.NotNull(probe.Metadata);
        Assert.Equal("hevc", probe.Metadata.VideoCodec);
        Assert.Equal(TimeSpan.FromSeconds(15.786667), probe.Metadata.EofEvidence!.SourceDuration);
        Assert.Equal("1/60000", probe.Metadata.EofEvidence.VideoTimeBase);
    }

    [Fact]
    public async Task ChangedIdentityIsRejectedBeforeProbing()
    {
        var input = Input();
        File.AppendAllText(input.SourcePath, "changed");
        var probe = await new ExportSourcePreflight("ffprobe", new Runner(new(0, "", ""))).ProbeAsync(input);
        Assert.Null(probe.Metadata);
        Assert.Contains("changed", probe.Diagnostic);
    }

    [Fact]
    public async Task IncompleteMetadataIsFailureRatherThanGuessedFromExtension()
    {
        var probe = await new ExportSourcePreflight("ffprobe", new Runner(new(0,
            """{"streams":[{"codec_type":"video","width":1920,"height":1080}],"format":{"format_name":"mp4"}}""", ""))).ProbeAsync(Input());
        Assert.Null(probe.Metadata);
        Assert.Contains("did not identify", probe.Diagnostic);
    }

    private sealed class Runner(ProbeProcessResult result) : IProbeProcessRunner
    {
        public Task<ProbeProcessResult> RunAsync(string executable, IReadOnlyList<string> arguments, CancellationToken cancellationToken = default)
            => Task.FromResult(result);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TerminalOptimizationRetainsDecodedProofAndFallsBackWhenInsufficient(bool fallback)
    {
        var input = Input() with { InitialTrim = new(TimeSpan.FromSeconds(.2), TimeSpan.FromSeconds(.04), TimeSpan.FromSeconds(.2)) };
        var runner = new EofRunner(fallback);
        var diagnostics = new List<string>();
        var service = new ExportSourcePreflight("ffprobe", runner, diagnostics.Add);
        var probe = new ExportSourceProbe(new(256, 256, 25, .2, 7, "hevc", false, Container: "mp4",
            EofEvidence: new(TimeSpan.FromSeconds(.2), TimeSpan.FromSeconds(.2), "1/1000")), input.SourceIdentity);
        var resolved = await service.ResolveAsync(input, probe);
        Assert.True(resolved!.RetainsSourceEof);
        Assert.Equal(TimeSpan.FromSeconds(.04), resolved.AbsoluteIn);
        Assert.Equal(TimeSpan.FromSeconds(.2), resolved.ExclusiveOut);
        Assert.Equal(2, runner.FrameCalls);
        Assert.Contains(diagnostics, detail => detail.Contains("Saved exclusive source EOF"));
        Assert.Equal(fallback, diagnostics.Any(detail => detail.Contains("full boundary probe")));
    }

    private sealed class EofRunner(bool fallback) : IProbeProcessRunner
    {
        public int FrameCalls { get; private set; }
        public Task<ProbeProcessResult> RunAsync(string executable, IReadOnlyList<string> arguments, CancellationToken cancellationToken = default)
        {
            if (arguments.Contains("-show_packets")) return Task.FromResult(new ProbeProcessResult(0,
                """{"packets":[{"pts_time":"0.04"}]}""", ""));
            FrameCalls++;
            if (fallback && FrameCalls == 1) return Task.FromResult(new ProbeProcessResult(0, """{"frames":[]}""", ""));
            var frames = FrameCalls == 1
                ? """{"frames":[{"best_effort_timestamp_time":"0.16","duration_time":"0.04"}]}"""
                : """{"frames":[{"best_effort_timestamp_time":"0.04","duration_time":"0.04"},{"best_effort_timestamp_time":"0.16","duration_time":"0.04"}]}""";
            return Task.FromResult(new ProbeProcessResult(0, frames, ""));
        }
    }
    public void Dispose() => Directory.Delete(_root, true);
}
