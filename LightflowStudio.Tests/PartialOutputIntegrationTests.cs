using System.Diagnostics;
using LightflowStudio;
using Xunit;

namespace LightflowStudio.Tests;

[Collection("Flyleaf playback integration")]
public sealed class PartialOutputIntegrationTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("lightflow-partial-integration-").FullName;

    [Theory]
    [InlineData(OutputContainer.Mp4, ".mp4", "mpeg4")]
    [InlineData(OutputContainer.Mov, ".mov", "mpeg4")]
    [InlineData(OutputContainer.Mkv, ".mkv", "ffv1")]
    internal void ExplicitMuxer_CreatesValidPartialThatFinalizesToPlayableMedia(
        OutputContainer container, string extension, string codec)
    {
        var dependencies = PlaybackDependencyLocator.FindSharedLibraries()
            ?? throw new InvalidOperationException("Run scripts/Get-PlaybackDependencies.ps1 before integration tests.");
        var ffmpeg = Path.Combine(dependencies, "ffmpeg.exe");
        var ffprobe = Path.Combine(dependencies, "ffprobe.exe");
        var final = Path.Combine(_root, "encoded" + extension);
        var lifecycle = new EncodingOutputLifecycle(final);
        lifecycle.Prepare();

        Run(ffmpeg, ["-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi", "-i",
            "testsrc2=size=160x90:rate=10:duration=1", "-c:v", codec,
            "-f", FfmpegCommandBuilder.OutputMuxer(container), lifecycle.PartialPath]);

        Assert.True(File.Exists(lifecycle.PartialPath));
        Assert.False(File.Exists(final));
        var probe = Run(ffprobe, FfmpegCommandBuilder.ProbeOutput(lifecycle.PartialPath));
        Assert.True(EncodedOutputValidator.TryValidate(probe, TimeSpan.FromSeconds(1), false, out var error), error);

        lifecycle.FinalizeValidatedOutput();

        Assert.False(File.Exists(lifecycle.PartialPath));
        Assert.True(File.Exists(final));
        Assert.Contains("\"codec_type\": \"video\"", Run(ffprobe, FfmpegCommandBuilder.ProbeOutput(final)));
    }

    private static string Run(string executable, IReadOnlyList<string> arguments)
    {
        var info = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var process = Process.Start(info) ?? throw new InvalidOperationException($"Could not start {executable}.");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException(stderr);
        return stdout;
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }
}
