using System.Text.Json;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class FfprobeTechnicalMetadataTests
{
    [Theory]
    [InlineData("yuv420p", null, 8, "4:2:0")]
    [InlineData("yuvj420p", null, 8, "4:2:0")]
    [InlineData("yuv420p10le", null, 10, "4:2:0")]
    [InlineData("yuv422p10le", null, 10, "4:2:2")]
    [InlineData("yuv444p10le", null, 10, "4:4:4")]
    [InlineData("nv12", null, 8, "4:2:0")]
    [InlineData("p010le", null, 10, "4:2:0")]
    [InlineData("gbrp", null, 8, null)]
    [InlineData("gray", null, 8, null)]
    [InlineData("rgb565le", null, null, null)]
    [InlineData("pal8", null, null, null)]
    [InlineData("cuda", null, null, null)]
    [InlineData("future_yuv420p10le", null, null, null)]
    [InlineData(null, null, null, null)]
    [InlineData("yuv420p10le", "12", 12, "4:2:0")]
    [InlineData("unknown", "8", 8, null)]
    [InlineData("yuv420p10le", "0", 10, "4:2:0")]
    [InlineData("yuv420p10le", "-1", 10, "4:2:0")]
    [InlineData("yuv420p10le", "N/A", 10, "4:2:0")]
    [InlineData("unknown", "999", null, null)]
    public void DepthAndChromaUseOnlyExplicitVideoDepthOrExactDescriptors(string? format,
        string? explicitDepth, int? depth, string? chroma)
    {
        var json = JsonSerializer.Serialize(new { streams = new object[] {
            new { codec_type = "video", codec_name = "hevc", profile = "Main 10", width = 16, height = 9,
                pix_fmt = format, bits_per_raw_sample = explicitDepth,
                color_space = "bt709", color_transfer = "smpte2084", color_primaries = "bt2020" },
            new { codec_type = "audio", bits_per_raw_sample = "24" } },
            format = new { BitDepth = "24", tags = new { encoder = "Camera name", bits_per_raw_sample = "16" } } });
        var video = FfprobeMetadataNormalizer.Normalize(json, 1).Metadata!.Video!;
        Assert.Equal(depth, video.BitDepth);
        Assert.Equal(chroma, video.ChromaSubsampling);
        Assert.Equal("bt709", video.ColorMatrix);
        Assert.Equal("smpte2084", video.ColorTransfer);
        Assert.Equal("bt2020", video.ColorPrimaries);
    }

    [Fact]
    public void AttachedJpegCannotSupplyVideoDepth()
    {
        var result = FfprobeMetadataNormalizer.Normalize("""
            {"streams":[
             {"codec_type":"video","codec_name":"mjpeg","pix_fmt":"yuvj420p","bits_per_raw_sample":"8","disposition":{"attached_pic":1}},
             {"codec_type":"video","codec_name":"hevc","pix_fmt":"yuv422p10le","disposition":{"attached_pic":0}}]}
            """, 10);
        Assert.Equal("hevc", result.Metadata!.Video!.Codec);
        Assert.Equal(10, result.Metadata.Video.BitDepth);
    }

    [Fact]
    public void LegacySerializedMatrixRemainsReadableOfflineWithoutInventedChroma()
    {
        var legacy = """{"codec":"hevc","width":1920,"height":1080,"colorSpace":"bt709","colorTransfer":"arib-std-b67","colorPrimaries":"bt2020"}""";
        var video = JsonSerializer.Deserialize<DerivedVideoMetadata>(legacy, DerivedMetadataJson.Options)!;
        Assert.Equal("bt709", video.ColorMatrix);
        Assert.Null(video.ChromaSubsampling);
        var serialized = JsonSerializer.Serialize(video, DerivedMetadataJson.Options);
        Assert.Contains("\"colorSpace\":\"bt709\"", serialized);
        Assert.DoesNotContain("colorMatrix", serialized);
    }

    [Fact]
    public async Task PinnedProbeReturnsSelectedTagsAndSourceRotationInOneInvocation()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Directory.Build.props"))) root = root.Parent;
        var tools = Path.Combine(root!.FullName, "artifacts", "ffmpeg", "bin");
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(root.FullName, "dependencies", "ffmpeg.json")));
        Assert.Equal(FfprobePixelFormats.DescriptorVersion, manifest.RootElement.GetProperty("version").GetString());
        var folder = Path.Combine(root.FullName, ".cache", "metadata-test-" + Guid.NewGuid());
        Directory.CreateDirectory(folder);
        var original = Path.Combine(folder, "original.mov");
        var rotated = Path.Combine(folder, "rotated.mov");
        var runner = new ProbeProcessRunner();
        var encode = await runner.RunAsync(Path.Combine(tools, "ffmpeg.exe"), ["-v", "error", "-f", "lavfi",
            "-i", "testsrc2=size=160x90:rate=10:duration=1", "-c:v", "libopenh264", "-pix_fmt", "yuv420p",
            "-metadata", "creation_time=2026-09-24T12:00:00Z", "-metadata", "comment=omit this unrelated tag",
            "-timecode", "01:02:03:04", original]);
        Assert.True(encode.ExitCode == 0, encode.StandardError);
        var remux = await runner.RunAsync(Path.Combine(tools, "ffmpeg.exe"), ["-v", "error", "-display_rotation", "90",
            "-i", original, "-c", "copy", "-metadata", "creation_time=2026-09-24T12:00:00Z",
            "-metadata:s:v:0", "creation_time=2026-09-24T12:00:00Z", rotated]);
        Assert.True(remux.ExitCode == 0, remux.StandardError);
        var counting = new CountingRunner();
        var result = await new FfprobeMediaMetadataReader(Path.Combine(tools, "ffprobe.exe"), counting)
            .ProbeAsync(rotated, "video", new FileInfo(rotated).Length);
        Assert.Equal(1, counting.Calls);
        Assert.Equal(DerivedMetadataStatus.Succeeded, result.Status);
        Assert.Equal(8, result.Metadata!.Video!.BitDepth);
        Assert.Equal("4:2:0", result.Metadata.Video.ChromaSubsampling);
        Assert.Equal(new MediaAspectRatio(9, 16), result.Metadata.Video.SourceDisplayAspectRatio);
        Assert.Equal(new MediaAspectRatio(16, 9), result.Metadata.Video.SourceDisplayAspectRatio!.Value.Rotate(new(90)));
        using var raw = JsonDocument.Parse(result.RawMetadata!);
        var formatTags = raw.RootElement.GetProperty("format").GetProperty("tags");
        Assert.True(formatTags.TryGetProperty("creation_time", out _));
        Assert.True(formatTags.TryGetProperty("encoder", out _));
        Assert.False(formatTags.TryGetProperty("comment", out _));
        var stream = raw.RootElement.GetProperty("streams")[0];
        Assert.True(stream.GetProperty("tags").TryGetProperty("timecode", out _));
        Assert.True(stream.GetProperty("side_data_list")[0].TryGetProperty("displaymatrix", out _));
        Assert.True(stream.GetProperty("side_data_list")[0].TryGetProperty("rotation", out _));
        Assert.True(stream.TryGetProperty("sample_aspect_ratio", out _));
        Directory.Delete(folder, true);
    }

    private sealed class CountingRunner : IProbeProcessRunner
    {
        public int Calls;
        public Task<ProbeProcessResult> RunAsync(string executable, IReadOnlyList<string> arguments,
            CancellationToken cancellationToken = default)
        { Calls++; return new ProbeProcessRunner().RunAsync(executable, arguments, cancellationToken); }
    }
}
