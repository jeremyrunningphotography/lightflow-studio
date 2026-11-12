using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class ExportNvencTheoryAttribute : TheoryAttribute
{
    public ExportNvencTheoryAttribute()
    {
        if (Environment.GetEnvironmentVariable("LIGHTFLOW_EXPORT_NVENC_ACCEPTANCE") != "1")
            Skip = "Opt-in requires an NVIDIA GPU and task-owned pinned or packaged FFmpeg tools.";
    }
}

public sealed class ExportBoundaryIntegrationTests
{
    [ExportNvencTheory]
    [InlineData(false, 0)]
    [InlineData(false, 5)]
    [InlineData(true, 0)]
    public async Task RealNvencExport_PreservesDecodedFramesThroughSavedEof(bool vfr, int offset)
    {
        var repo = new DirectoryInfo(AppContext.BaseDirectory);
        while (repo is not null && !File.Exists(Path.Combine(repo.FullName, "AGENTS.md"))) repo = repo.Parent;
        Assert.NotNull(repo);
        var tools = Environment.GetEnvironmentVariable("LIGHTFLOW_EXPORT_TEST_TOOLS")
            ?? Path.Combine(repo.FullName, "artifacts", "ffmpeg", "bin");
        var root = Path.Combine(repo.FullName, "artifacts", "export-047", $"{vfr}-{offset}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var source = Path.Combine(root, "source.mp4");
        var output = Path.Combine(root, "export.mp4");
        var ffmpeg = Path.Combine(tools, "ffmpeg.exe");
        var ffprobe = Path.Combine(tools, "ffprobe.exe");
        await Run(ffmpeg, ["-v", "error", "-y", "-f", "lavfi", "-i",
            $"testsrc2=size=256x256:rate={(vfr ? 100 : 25)}:duration=0.2",
            "-vf", vfr ? "select='eq(n,0)+eq(n,3)+eq(n,7)+eq(n,11)+eq(n,15)+eq(n,19)'" : "null",
            "-fps_mode", "vfr", "-output_ts_offset", offset.ToString(CultureInfo.InvariantCulture), "-c:v", "mpeg4", source]);
        var probe = await Run(ffprobe, FfmpegCommandBuilder.ProbeVideoFrames(source));
        var pts = EncodingRangeResolver.ParseFrameTimestamps(probe).Order().ToArray();
        var range = SubclipCreationEligibility.Materialize(new(TimeSpan.FromSeconds(.2), pts[1] - pts[0]));
        Assert.True(MediaMetadataParser.TryParse(await Run(ffprobe, FfmpegCommandBuilder.ProbeMetadata(source)), new FileInfo(source).Length, out var metadata));
        var resolved = EncodingRangeResolver.Resolve(range, TimeSpan.FromSeconds(offset), probe, metadata.EofEvidence);
        Assert.NotNull(resolved.BoundaryDiagnostic);
        await Run(ffmpeg, FfmpegCommandBuilder.Encode(source, output, null, RecoveryStrategy.Normal,
            OutputResolution.Source, encoding: new() { AudioMode = AudioEncodingMode.None, FrameRate = 0 }, trim: resolved));
        var exported = await Run(ffprobe, FfmpegCommandBuilder.ProbeVideoFrames(output));
        var actual = EncodingRangeResolver.ParseFrameTimestamps(exported).Order().ToArray();
        var expected = pts.Skip(1).Select(value => value - pts[1]).ToArray();
        Assert.Equal(expected.Length, actual.Length);
        for (var index = 0; index < expected.Length; index++)
            Assert.InRange(Math.Abs((actual[index] - expected[index]).TotalMilliseconds), 0, 1);
        File.WriteAllText(Path.Combine(root, "evidence.json"), JsonSerializer.Serialize(new
        {
            tools, source, output, range, resolved,
            sourcePts = pts.Select(value => value.TotalSeconds), outputPts = actual.Select(value => value.TotalSeconds)
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static async Task<string> Run(string executable, IReadOnlyList<string> arguments)
    {
        var start = new ProcessStartInfo(executable)
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { process.Kill(true); throw; }
        Assert.True(process.ExitCode == 0, await error);
        return await output;
    }
}
