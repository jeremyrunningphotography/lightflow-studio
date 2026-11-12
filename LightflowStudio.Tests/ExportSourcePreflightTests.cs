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
    public void Dispose() => Directory.Delete(_root, true);
}
