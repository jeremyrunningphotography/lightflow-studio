using System.Diagnostics;
using System.IO;

namespace LightflowStudio;

internal interface IEncoderCapabilityProbe
{
    Task<(bool Available, string Diagnostic)> ProbeAsync(string ffmpeg, string encoder, CancellationToken token);
}

internal sealed class FfmpegEncoderCapabilityProbe : IEncoderCapabilityProbe
{
    public async Task<(bool Available, string Diagnostic)> ProbeAsync(string ffmpeg, string encoder, CancellationToken token)
    {
        try
        {
            var start = new ProcessStartInfo(ffmpeg)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            foreach (var argument in new[] { "-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i",
                         "color=size=256x256:rate=1", "-frames:v", "1", "-c:v", encoder, "-f", "null", "-" })
                start.ArgumentList.Add(argument);
            using var process = Process.Start(start) ?? throw new InvalidOperationException($"Could not start {ffmpeg}.");
            var stdout = process.StandardOutput.ReadToEndAsync(token);
            var stderr = process.StandardError.ReadToEndAsync(token);
            await process.WaitForExitAsync(token).ConfigureAwait(false);
            var detail = string.Join(' ', (await stderr.ConfigureAwait(false) + await stdout.ConfigureAwait(false))
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Take(2));
            return (process.ExitCode == 0, process.ExitCode == 0 ? "Execution probe succeeded." : detail);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        { return (false, exception.Message); }
    }
}

internal sealed class EncoderCapabilityService
{
    private readonly string? _ffmpeg;
    private readonly IEncoderCapabilityProbe _probe;
    private readonly object _sync = new();
    private Task<IReadOnlyList<EncoderCapability>>? _cached;

    public EncoderCapabilityService(string? ffmpeg, IEncoderCapabilityProbe probe)
    { _ffmpeg = ffmpeg; _probe = probe; }

    public Task<IReadOnlyList<EncoderCapability>> GetAsync(CancellationToken token = default)
    {
        lock (_sync) return _cached ??= DetectAsync(token);
    }

    private async Task<IReadOnlyList<EncoderCapability>> DetectAsync(CancellationToken token)
    {
        var results = new List<EncoderCapability>();
        if (string.IsNullOrWhiteSpace(_ffmpeg))
            results.Add(new(EncoderBackend.NvidiaNvenc, EncoderCapabilityState.ImplementedButUnavailable, "FFmpeg is unavailable."));
        else
        {
            var h264 = await _probe.ProbeAsync(_ffmpeg, "h264_nvenc", token).ConfigureAwait(false);
            var hevc = await _probe.ProbeAsync(_ffmpeg, "hevc_nvenc", token).ConfigureAwait(false);
            var available = h264.Available && hevc.Available;
            results.Add(new(EncoderBackend.NvidiaNvenc,
                available ? EncoderCapabilityState.ImplementedAndAvailable : EncoderCapabilityState.ImplementedButUnavailable,
                available ? "H.264 and HEVC NVENC execution probes succeeded." : $"H.264: {h264.Diagnostic} HEVC: {hevc.Diagnostic}"));
        }
        results.Add(new(EncoderBackend.Cpu, EncoderCapabilityState.NotImplemented, "CPU encoding is not implemented."));
        results.Add(new(EncoderBackend.AmdAmf, EncoderCapabilityState.NotImplemented, "AMD AMF encoding is not implemented."));
        results.Add(new(EncoderBackend.IntelQuickSync, EncoderCapabilityState.NotImplemented, "Intel Quick Sync encoding is not implemented."));
        return results;
    }
}
