using LightflowStudio;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class DependencyHealthCheckTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"LightflowStudio-health-{Guid.NewGuid():N}");
    public DependencyHealthCheckTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task RunAsync_ReportsReadyWhenToolsAndNvencEncodersAreAvailable()
    {
        var ffmpeg = CreateExecutable("ffmpeg.exe"); var ffprobe = CreateExecutable("ffprobe.exe");
        var report = await DependencyHealthCheck.RunAsync(ffmpeg, ffprobe, (executable, arguments, _) => Task.FromResult(arguments.Contains("-c:v") ? (0, "", "") : (0, $"{Path.GetFileName(executable)} version 1.0", "")));
        Assert.True(report.IsReady); Assert.All(report.Items, item => Assert.True(item.IsReady)); Assert.Equal(4, report.Items.Count);
        Assert.Equal("Everything needed for export is ready.", report.Summary);
        Assert.All(report.Items, item => Assert.DoesNotContain("encoding", item.Summary, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RunAsync_ReportsEveryMissingRequirementWithoutStartingProcesses()
    {
        var processStarted = false;
        var report = await DependencyHealthCheck.RunAsync(null, null, (_, _, _) => { processStarted = true; return Task.FromResult((0, "", "")); });
        Assert.False(processStarted); Assert.False(report.IsReady); Assert.Equal(4, report.Items.Count); Assert.All(report.Items, item => Assert.False(item.IsReady));
    }

    [Fact]
    public async Task RunAsync_IdentifiesAnFfmpegBuildWithoutHevcNvenc()
    {
        var ffmpeg = CreateExecutable("ffmpeg.exe"); var ffprobe = CreateExecutable("ffprobe.exe");
        var report = await DependencyHealthCheck.RunAsync(ffmpeg, ffprobe, (_, arguments, _) => Task.FromResult(arguments.Contains("hevc_nvenc") ? (1, "", "No capable device found") : (0, "version 1.0", "")));
        Assert.False(report.IsReady); Assert.True(report.Items.Single(item => item.Name == "H.264 NVIDIA encoder").IsReady); Assert.False(report.Items.Single(item => item.Name == "HEVC NVIDIA encoder").IsReady); Assert.Contains("1 item", report.Summary);
    }

    [Fact]
    public async Task RunAsync_ReportsAnExecutableThatCannotStart()
    {
        var ffmpeg = CreateExecutable("ffmpeg.exe"); var ffprobe = CreateExecutable("ffprobe.exe");
        var report = await DependencyHealthCheck.RunAsync(ffmpeg, ffprobe, (executable, _, _) => executable == ffprobe ? throw new IOException("blocked") : Task.FromResult((0, "h264_nvenc hevc_nvenc", "")));
        Assert.False(report.Items.Single(item => item.Name == "FFprobe").IsReady); Assert.Contains("could not be started", report.Items.Single(item => item.Name == "FFprobe").Summary);
    }

    [Fact]
    public async Task RunAsync_ProvidesResolutionGuidanceForEveryRequirement()
    {
        var report = await DependencyHealthCheck.RunAsync(null, null);

        Assert.All(report.Items, item => Assert.False(string.IsNullOrWhiteSpace(item.Resolution)));
        Assert.Contains("Open Settings", report.Items.Single(item => item.Name == "FFmpeg").Resolution);
        Assert.Contains("NVIDIA", report.Items.Single(item => item.Name == "H.264 NVIDIA encoder").Resolution);
    }
    private string CreateExecutable(string name) { var path = Path.Combine(_root, name); File.WriteAllText(path, "test"); return path; }
    public void Dispose() => Directory.Delete(_root, recursive: true);
}
