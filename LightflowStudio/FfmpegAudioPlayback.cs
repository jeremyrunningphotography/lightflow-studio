using System.Diagnostics;
using System.IO;
using NAudio.Wave;

namespace LightflowStudio;

/// <summary>
/// Audio-only companion for the shared playback backend. FFmpeg decodes the selected embedded stream from
/// the authoritative video position; Windows output owns only bounded PCM buffering and never media timing.
/// </summary>
internal sealed class FfmpegAudioPlayback : IAsyncDisposable
{
    private const int SampleRate = 48_000;
    private const int Channels = 2;
    private readonly string _ffmpeg;
    private readonly Func<IPlaybackAudioOutput> _createOutput;
    private CancellationTokenSource? _runCancellation;
    private Task? _pump;
    private Process? _process;
    private IPlaybackAudioOutput? _output;
    private int _volume = 100;
    private bool _mute;

    public FfmpegAudioPlayback(string ffmpegDirectory, Func<IPlaybackAudioOutput>? createOutput = null)
    {
        _ffmpeg = Path.Combine(ffmpegDirectory, "ffmpeg.exe");
        if (!File.Exists(_ffmpeg)) throw new FileNotFoundException("Bundled playback FFmpeg was not found.", _ffmpeg);
        _createOutput = createOutput ?? (() => new WaveOutPlaybackAudioOutput(SampleRate, Channels));
    }

    public int Volume
    {
        get => _volume;
        set { _volume = Math.Clamp(value, 0, 100); ApplyVolume(); }
    }

    public bool Mute
    {
        get => _mute;
        set { _mute = value; ApplyVolume(); }
    }

    internal int? ActiveProcessId
    {
        get
        {
            var process = _process;
            if (process is null) return null;
            try { return process.HasExited ? null : process.Id; }
            catch (InvalidOperationException) { return null; }
        }
    }

    public async Task StartAsync(string sourcePath, int streamIndex, TimeSpan position, CancellationToken token)
    {
        await StopAsync().ConfigureAwait(false);
        token.ThrowIfCancellationRequested();

        var output = _createOutput();
        _output = output;
        ApplyVolume();
        var start = new ProcessStartInfo(_ffmpeg)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in new[]
        {
            "-hide_banner", "-loglevel", "error", "-nostdin",
            "-ss", position.TotalSeconds.ToString("0.#######", System.Globalization.CultureInfo.InvariantCulture),
            "-i", sourcePath, "-map", $"0:{streamIndex}", "-vn", "-sn", "-dn",
            "-ac", Channels.ToString(), "-ar", SampleRate.ToString(), "-f", "s16le", "pipe:1"
        }) start.ArgumentList.Add(argument);

        Process process;
        try
        {
            process = Process.Start(start) ?? throw new InvalidOperationException("Bundled FFmpeg audio decode could not be started.");
        }
        catch
        {
            _output = null;
            output.Dispose();
            throw;
        }
        _process = process;
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        _runCancellation = cancellation;
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _pump = PumpAsync(process, output, ready, cancellation.Token);
        try
        {
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(3), token).ConfigureAwait(false);
            output.Play();
        }
        catch
        {
            await StopAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async Task StopAsync()
    {
        var cancellation = Interlocked.Exchange(ref _runCancellation, null);
        var process = Interlocked.Exchange(ref _process, null);
        var pump = Interlocked.Exchange(ref _pump, null);
        var output = Interlocked.Exchange(ref _output, null);
        cancellation?.Cancel();
        output?.Stop();
        if (process is { HasExited: false })
        {
            try { process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
        }
        if (pump is not null)
        {
            try { await pump.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            catch (IOException) when (cancellation?.IsCancellationRequested == true) { }
        }
        process?.Dispose();
        cancellation?.Dispose();
        output?.Dispose();
    }

    private static async Task PumpAsync(
        Process process,
        IPlaybackAudioOutput output,
        TaskCompletionSource ready,
        CancellationToken token)
    {
        var buffer = new byte[16 * 1024];
        // Always consume stderr while PCM is flowing. Although FFmpeg is intentionally quiet, leaving the
        // redirected pipe unread can deadlock a long-running decoder if it emits enough diagnostics.
        var stderr = process.StandardError.ReadToEndAsync();
        while (true)
        {
            while (output.BufferedDuration > TimeSpan.FromMilliseconds(500))
                await Task.Delay(10, token).ConfigureAwait(false);
            var read = await process.StandardOutput.BaseStream.ReadAsync(buffer, token).ConfigureAwait(false);
            if (read == 0) break;
            output.AddSamples(buffer, 0, read);
            ready.TrySetResult();
        }
        if (!ready.Task.IsCompleted)
        {
            var error = await stderr.ConfigureAwait(false);
            ready.TrySetException(new InvalidDataException(
                string.IsNullOrWhiteSpace(error) ? "The selected audio stream produced no samples." : error.Trim()));
        }
    }

    private void ApplyVolume()
    {
        if (_output is { } output) output.Volume = _mute ? 0 : _volume / 100f;
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);
}

internal interface IPlaybackAudioOutput : IDisposable
{
    TimeSpan BufferedDuration { get; }
    float Volume { get; set; }
    void AddSamples(byte[] buffer, int offset, int count);
    void Play();
    void Stop();
}

internal sealed class WaveOutPlaybackAudioOutput : IPlaybackAudioOutput
{
    private readonly BufferedWaveProvider _buffer = new(new WaveFormat(48_000, 16, 2))
    {
        BufferDuration = TimeSpan.FromSeconds(1),
        DiscardOnBufferOverflow = false,
        ReadFully = true
    };
    private readonly WaveOutEvent _device = new() { DesiredLatency = 50, NumberOfBuffers = 3 };

    public WaveOutPlaybackAudioOutput(int sampleRate, int channels)
    {
        if (sampleRate != 48_000 || channels != 2) throw new ArgumentOutOfRangeException(nameof(sampleRate));
        _device.Init(_buffer);
    }

    public TimeSpan BufferedDuration => _buffer.BufferedDuration;
    public float Volume { get => _device.Volume; set => _device.Volume = value; }
    public void AddSamples(byte[] buffer, int offset, int count) => _buffer.AddSamples(buffer, offset, count);
    public void Play() => _device.Play();
    public void Stop() => _device.Stop();
    public void Dispose() => _device.Dispose();
}
