using System.Diagnostics;
using System.Globalization;
using LightflowStudio;
using Xunit;

namespace LightflowStudio.Tests;

[Collection("Flyleaf playback integration")]
public sealed class RangeEncodingIntegrationTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("lightflow-range-integration-").FullName;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ResolvedRange_ProducesPlayableVideoAndAlignedAudio(bool vfr)
    {
        var dependencies = PlaybackDependencyLocator.FindSharedLibraries()
            ?? throw new InvalidOperationException("Run scripts/Get-PlaybackDependencies.ps1 before integration tests.");
        var ffmpeg = Path.Combine(dependencies, "ffmpeg.exe");
        var ffprobe = Path.Combine(dependencies, "ffprobe.exe");
        var source = Path.Combine(_root, $"source-{vfr}.mkv");
        var output = Path.Combine(_root, $"trimmed-{vfr}.mkv");
        GenerateFixture(ffmpeg, source, vfr);

        var metadataJson = Run(ffprobe, FfmpegCommandBuilder.ProbeMetadata(source));
        Assert.True(MediaMetadataParser.TryParse(metadataJson, new FileInfo(source).Length, out var metadata));
        var allFrameJson = Run(ffprobe, FfmpegCommandBuilder.ProbeVideoFrames(source));
        var timestamps = EncodingRangeResolver.ParseFrameTimestamps(allFrameJson)
            .Select(value => value - metadata.StartTimestamp).ToList();
        var requested = new MediaRange(TimeSpan.FromSeconds(metadata.DurationSeconds), timestamps[2], timestamps[6]);
        var packetJson = Run(ffprobe, FfmpegCommandBuilder.ProbeVideoPackets(source, requested, metadata.StartTimestamp));
        var resolved = EncodingRangeResolver.Resolve(requested, metadata.StartTimestamp, packetJson);

        Run(ffmpeg, ["-hide_banner", "-loglevel", "error", "-y", "-ss", Seconds(requested.EffectiveIn), "-copyts", "-i", source, "-map", "0:v:0", "-map", "0:a:0",
            "-vf", $"trim=start={Seconds(resolved.AbsoluteIn)}:end={Seconds(resolved.ExclusiveOut)},setpts=PTS-STARTPTS",
            "-af", $"atrim=start={Seconds(resolved.AbsoluteIn)}:end={Seconds(resolved.ExclusiveOut)},asetpts=PTS-STARTPTS",
            "-c:v", "ffv1", "-c:a", "pcm_s16le", output]);
        var outputProbe = Run(ffprobe, FfmpegCommandBuilder.ProbeOutput(output));

        Assert.True(EncodedOutputValidator.TryValidate(outputProbe, resolved.EffectiveDuration, true, out var error), error);
        Assert.Contains("\"codec_name\": \"ffv1\"", outputProbe);
        var outputPts = EncodingRangeResolver.ParseFrameTimestamps(Run(ffprobe, FfmpegCommandBuilder.ProbeVideoFrames(output)));
        Assert.True(outputPts.Count >= 5);
        Assert.True(Math.Abs((outputPts[^1] - outputPts[0] - (timestamps[6] - timestamps[2])).TotalMilliseconds) < 80);
    }

    private static void GenerateFixture(string ffmpeg, string output, bool vfr)
    {
        var args = new List<string> { "-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi", "-i", "testsrc2=size=160x90:rate=20:duration=2", "-f", "lavfi", "-i", "sine=frequency=440:duration=2" };
        if (vfr) args.AddRange(["-filter:v", "setpts=if(lt(N\\,10)\\,N/(20*TB)\\,(0.5+(N-10)/7)/TB)", "-fps_mode", "vfr"]);
        args.AddRange(["-c:v", "ffv1", "-c:a", "pcm_s16le", "-shortest", "-output_ts_offset", "5", output]);
        Run(ffmpeg, args);
    }

    private static string Run(string executable, IReadOnlyList<string> arguments)
    {
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var process = Process.Start(info) ?? throw new InvalidOperationException($"Could not start {executable}.");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException(stderr);
        return stdout;
    }

    private static string Seconds(TimeSpan value) => value.TotalSeconds.ToString("0.#########", CultureInfo.InvariantCulture);
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }
}
