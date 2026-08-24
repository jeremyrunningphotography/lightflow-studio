using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace LightflowStudio;

internal sealed class EncodingJobExecutor
{
    private readonly string _ffmpeg;
    private readonly string _ffprobe;
    private readonly string? _identityCacheDirectory;
    private readonly IEncodingLutResourceStore _colorResources;
    private readonly Action<string>? _diagnostic;
    private readonly ConcurrentDictionary<Guid, Process> _processes = [];

    public EncodingJobExecutor(string ffmpeg, string ffprobe, string? identityCacheDirectory = null,
        IEncodingLutResourceStore? colorResources = null, Action<string>? diagnostic = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ffmpeg);
        ArgumentException.ThrowIfNullOrWhiteSpace(ffprobe);
        _ffmpeg = ffmpeg;
        _ffprobe = ffprobe;
        _identityCacheDirectory = identityCacheDirectory;
        _colorResources = colorResources ?? new EncodingLutResourceStore(EncodingLutResourceStore.DefaultDirectory);
        _diagnostic = diagnostic;
    }

    public int ActiveProcessCount => _processes.Count;

    public async Task<JobItemResult<EncodingItemResult>> ExecuteAsync(
        JobPlanItem item, EncodingJobOptions options, IProgress<double> progress, CancellationToken token)
    {
        var input = item.Definition.SourceIdentity;
        var output = item.OutputPaths.Single();
        var duration = item.Definition.MediaRange?.EffectiveDuration.TotalSeconds ?? 0;
        var lifecycle = new EncodingOutputLifecycle(output, input, _identityCacheDirectory);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            lifecycle.Prepare();
            var colorLuts = options.ColorMode == EncodingColorMode.Assigned
                            && item.Definition.AssignedColor is { ColorEnabled: true } color
                ? color.OrderedPipeline.Select(_colorResources.Resolve).ToArray()
                : [];
            var manualLut = options.ColorMode == EncodingColorMode.OriginalOrManual ? options.LutPath : null;
            var args = FfmpegCommandBuilder.Encode(input, lifecycle.PartialPath, manualLut,
                options.Recovery, options.Resolution, options.DetailedOutput, options.Encoding,
                item.Definition.ResolvedRange, colorLuts);
            var exit = await RunFfmpegAsync(item.Definition.Id, args, duration, progress, token).ConfigureAwait(false);
            var data = new EncodingItemResult(exit,
                item.Definition.ResolvedRange?.RequestedRange.SourceDuration ?? item.Definition.MediaRange?.SourceDuration,
                item.Definition.ResolvedRange?.RequestedRange, item.Definition.MediaRange?.EffectiveDuration);
            if (exit != 0)
                return Failed(item, $"FFmpeg exited with code {exit}.", data, lifecycle.CleanupFailedAttempt());

            var validation = await CaptureAsync(_ffprobe, FfmpegCommandBuilder.ProbeOutput(lifecycle.PartialPath), token).ConfigureAwait(false);
            var expectsAudio = options.Recovery != RecoveryStrategy.VideoOnly
                               && options.Encoding.AudioMode != AudioEncodingMode.None
                               && item.Definition.SourceHasAudio != false;
            var validationError = "FFprobe could not open the exported file.";
            if (validation.ExitCode != 0 || !EncodedOutputValidator.TryValidate(validation.StandardOutput,
                    item.Definition.MediaRange?.EffectiveDuration ?? TimeSpan.FromSeconds(duration), expectsAudio,
                    out validationError))
                return Failed(item, validationError, data, lifecycle.CleanupFailedAttempt());

            lifecycle.FinalizeValidatedOutput();
            try
            {
                EncodingOutputIdentityStore.Save(output, EncodingOutputIdentity.Create(item.Definition, options), _identityCacheDirectory);
                return new(item.Definition.Id, JobState.Completed, item.OutputPaths, [], [], data);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return new(item.Definition.Id, JobState.CompletedWithWarnings, item.OutputPaths,
                    [$"The output is valid, but its resume identity could not be saved: {exception.Message}"], [], data);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            var warning = lifecycle.CleanupFailedAttempt();
            return new(item.Definition.Id, JobState.Cancelled, item.OutputPaths,
                warning is null ? [] : [warning], [], default);
        }
        catch (Exception exception)
        {
            var warning = lifecycle.CleanupFailedAttempt();
            return Failed(item, exception.Message, default, warning);
        }
    }

    public void TerminateAll()
    {
        foreach (var process in _processes.Values) Terminate(process);
    }

    private async Task<int> RunFfmpegAsync(Guid itemId, IReadOnlyList<string> args, double duration,
        IProgress<double> progress, CancellationToken token)
    {
        using var process = Start(_ffmpeg, args);
        if (!_processes.TryAdd(itemId, process)) throw new InvalidOperationException($"Item {itemId} already has an active process.");
        using var registration = token.Register(() => Terminate(process));
        try
        {
            var errors = new StringBuilder();
            var errorTask = Task.Run(async () =>
            {
                while (await process.StandardError.ReadLineAsync(CancellationToken.None).ConfigureAwait(false) is { } line)
                {
                    errors.AppendLine(line);
                    _diagnostic?.Invoke($"[FFmpeg] {line}");
                }
            });
            while (await process.StandardOutput.ReadLineAsync(token).ConfigureAwait(false) is { } line)
                if (FfmpegProgressParser.TryParsePercent(line, duration, out var percent)) progress.Report(percent);
            await process.WaitForExitAsync(token).ConfigureAwait(false);
            await errorTask.ConfigureAwait(false);
            progress.Report(100);
            if (process.ExitCode != 0 && errors.Length > 0) _diagnostic?.Invoke(errors.ToString());
            return process.ExitCode;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            Terminate(process);
            try { await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
            throw;
        }
        finally { _processes.TryRemove(itemId, out _); }
    }

    private static JobItemResult<EncodingItemResult> Failed(JobPlanItem item, string error,
        EncodingItemResult? data, string? cleanupWarning) => new(item.Definition.Id, JobState.Failed,
        item.OutputPaths, cleanupWarning is null ? [] : [cleanupWarning], [error], data);

    private static Process Start(string executable, IEnumerable<string> args)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var argument in args) start.ArgumentList.Add(argument);
        return Process.Start(start) ?? throw new InvalidOperationException($"Could not start {executable}.");
    }

    private static async Task<(int ExitCode, string StandardOutput)> CaptureAsync(
        string executable, IEnumerable<string> args, CancellationToken token)
    {
        using var process = Start(executable, args);
        var output = process.StandardOutput.ReadToEndAsync(token);
        var error = process.StandardError.ReadToEndAsync(token);
        using var registration = token.Register(() => Terminate(process));
        await process.WaitForExitAsync(token).ConfigureAwait(false);
        await error.ConfigureAwait(false);
        return (process.ExitCode, await output.ConfigureAwait(false));
    }

    private static void Terminate(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception) { }
    }
}
