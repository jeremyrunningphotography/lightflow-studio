using System.Diagnostics;
using System.IO;

namespace LightflowStudio;

internal enum DependencyHealth { Ready, NeedsAttention }

internal sealed record DependencyCheckItem(string Name, DependencyHealth Health, string Summary, string Detail, string Resolution)
{
    public bool IsReady => Health == DependencyHealth.Ready;
}

internal sealed record DependencyHealthReport(IReadOnlyList<DependencyCheckItem> Items)
{
    public bool IsReady => Items.All(item => item.IsReady);
    public string Summary => IsReady ? "Everything needed for encoding is ready." : $"{Items.Count(item => !item.IsReady)} item{(Items.Count(item => !item.IsReady) == 1 ? "" : "s")} need attention.";
}

internal static class DependencyHealthCheck
{
    public static async Task<DependencyHealthReport> RunAsync(string? ffmpeg, string? ffprobe,
        Func<string, IReadOnlyList<string>, CancellationToken, Task<(int ExitCode, string StdOut, string StdErr)>>? run = null,
        CancellationToken token = default)
    {
        run ??= RunProcessAsync;
        var items = new List<DependencyCheckItem>();
        var ffmpegReady = await CheckExecutableAsync("FFmpeg", ffmpeg, ["-hide_banner", "-version"], "FFmpeg is available.", "FFmpeg could not be started.", "Open Settings and choose a working ffmpeg.exe, or reinstall Lightflow Studio to restore the bundled copy.", run, token);
        items.Add(ffmpegReady);
        items.Add(await CheckExecutableAsync("FFprobe", ffprobe, ["-hide_banner", "-version"], "Media file details are available.", "FFprobe is missing or could not be started.", "Place ffprobe.exe beside the selected FFmpeg executable, or reinstall Lightflow Studio to restore the bundled copy.", run, token));

        if (!ffmpegReady.IsReady || string.IsNullOrWhiteSpace(ffmpeg))
        {
            items.Add(EncoderUnavailable("H.264 NVIDIA encoder"));
            items.Add(EncoderUnavailable("HEVC NVIDIA encoder"));
            return new(items);
        }

        items.Add(await CheckEncoderAsync(ffmpeg, "h264_nvenc", "H.264 NVIDIA encoder", run, token));
        items.Add(await CheckEncoderAsync(ffmpeg, "hevc_nvenc", "HEVC NVIDIA encoder", run, token));
        return new(items);
    }

    private static async Task<DependencyCheckItem> CheckExecutableAsync(string name, string? path, IReadOnlyList<string> arguments,
        string readySummary, string unavailableSummary, string resolution,
        Func<string, IReadOnlyList<string>, CancellationToken, Task<(int ExitCode, string StdOut, string StdErr)>> run, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return new(name, DependencyHealth.NeedsAttention, unavailableSummary, path ?? "No executable was found.", resolution);
        try
        {
            var result = await run(path, arguments, token);
            return result.ExitCode == 0
                ? new(name, DependencyHealth.Ready, readySummary, FirstLine(result.StdOut + result.StdErr, path), "No action is needed.")
                : new(name, DependencyHealth.NeedsAttention, unavailableSummary, $"The check exited with code {result.ExitCode}.", resolution);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return new(name, DependencyHealth.NeedsAttention, unavailableSummary, ex.Message, resolution);
        }
    }

    private static async Task<DependencyCheckItem> CheckEncoderAsync(string ffmpeg, string encoder, string name,
        Func<string, IReadOnlyList<string>, CancellationToken, Task<(int ExitCode, string StdOut, string StdErr)>> run, CancellationToken token)
    {
        try
        {
            var result = await run(ffmpeg, ["-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i", "color=size=256x256:rate=1", "-frames:v", "1", "-c:v", encoder, "-f", "null", "-"], token);
            return result.ExitCode == 0
                ? new(name, DependencyHealth.Ready, "Available for encoding.", $"{encoder} successfully started.", "No action is needed.")
                : new(name, DependencyHealth.NeedsAttention, "Could not start on this computer.", FirstLine(result.StdErr + result.StdOut, "The encoder test failed."), "Install a current NVIDIA graphics driver and confirm that this computer has a supported NVIDIA GPU.");
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return new(name, DependencyHealth.NeedsAttention, "Could not start on this computer.", ex.Message, "Install a current NVIDIA graphics driver and confirm that this computer has a supported NVIDIA GPU.");
        }
    }
    private static DependencyCheckItem EncoderUnavailable(string name) => new(name, DependencyHealth.NeedsAttention, "Not available in the selected FFmpeg build.", "The selected FFmpeg build could not be tested for this encoder.", "Use an FFmpeg build that includes NVIDIA NVENC support and install a current NVIDIA graphics driver.");
    private static string FirstLine(string output, string fallback) => output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? fallback;

    private static async Task<(int ExitCode, string StdOut, string StdErr)> RunProcessAsync(string executable, IReadOnlyList<string> arguments, CancellationToken token)
    {
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var process = Process.Start(info) ?? throw new InvalidOperationException($"Could not start {executable}.");
        var stdout = process.StandardOutput.ReadToEndAsync(token);
        var stderr = process.StandardError.ReadToEndAsync(token);
        await process.WaitForExitAsync(token);
        return (process.ExitCode, await stdout, await stderr);
    }
}