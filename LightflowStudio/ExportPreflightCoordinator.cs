using System.Diagnostics;
using System.IO;

namespace LightflowStudio;

/// <summary>One dialog-owned run; source facts never overwrite current export settings.</summary>
internal sealed class ExportPreflightCoordinator(ExportSourcePreflight source,
    Func<CancellationToken, Task<EncoderCapability>> hardware,
    Action<string>? diagnostic = null)
{
    internal const int MaximumConcurrentSources = 2;

    public async Task RunAsync(ExportDialogModel model, CancellationToken token = default)
    {
        var generation = model.BeginPreflight();
        var total = Stopwatch.StartNew();
        var updates = new object();
        bool Update(Func<bool> action) { lock (updates) return action(); }
        using var slots = new SemaphoreSlim(MaximumConcurrentSources);
        try
        {
            var encoder = CheckHardwareAsync();
            var files = model.PreflightInputs.Select((input, index) => CheckFileAsync(input, index)).ToArray();
            await Task.WhenAll(files.Append(encoder));
            diagnostic?.Invoke($"Export preflight completed in {total.Elapsed.TotalMilliseconds:F0} ms.");
        }
        finally { if (token.IsCancellationRequested) model.CancelPreflight(generation); }

        async Task CheckHardwareAsync()
        {
            var timer = Stopwatch.StartNew();
            var capability = await Task.Run(() => hardware(token), token);
            if (!token.IsCancellationRequested) Update(() => model.ApplyEncoderCapability(generation, capability));
            diagnostic?.Invoke($"Export hardware execution probes: {timer.Elapsed.TotalMilliseconds:F0} ms.");
        }

        async Task CheckFileAsync(EncodingHandoffInput input, int index)
        {
            await slots.WaitAsync(token);
            try
            {
                if (!Update(() => model.SetChecking(generation, index))) return;
                var timer = Stopwatch.StartNew();
                var probe = await Task.Run(() => source.ProbeAsync(input, token), token);
                if (token.IsCancellationRequested || !Update(() => model.ApplySourceProbe(generation, index, probe))) return;
                diagnostic?.Invoke($"Export source {index + 1} metadata: {timer.Elapsed.TotalMilliseconds:F0} ms.");
                timer.Restart();
                ResolvedMediaRange? range = null;
                string? error = null;
                if (probe.Metadata is not null)
                    try { range = await Task.Run(() => source.ResolveAsync(input, probe, token), token); }
                    catch (Exception ex) when (ex is ArgumentException or InvalidDataException or IOException
                        or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
                    { error = ex.Message; }
                if (!token.IsCancellationRequested) Update(() => model.ApplySourceRange(generation, index, range, error));
                diagnostic?.Invoke($"Export source {index + 1} range: {timer.Elapsed.TotalMilliseconds:F0} ms; {(error is null ? "completed" : "failed")}.");
            }
            finally { slots.Release(); }
        }
    }
}
