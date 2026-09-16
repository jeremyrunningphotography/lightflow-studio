using System.Diagnostics;
using System.Globalization;

namespace LightflowStudio;

// Scoped to a launch, flows through awaited storage workers, and is inert outside startup.
internal sealed class StartupDiagnostics : IDisposable
{
    private static readonly AsyncLocal<StartupDiagnostics?> Current = new();
    private readonly StartupDiagnostics? _previous;
    private readonly Action<string> _write;
    private readonly Action<string>? _progress;
    private readonly Stopwatch _elapsed = Stopwatch.StartNew();
    private volatile bool _disposed;

    public StartupDiagnostics(Action<string> write, Action<string>? progress = null)
    {
        _previous = Current.Value;
        _write = write;
        _progress = progress;
        Current.Value = this;
        Write("launch instrumentation started");
    }

    public static IDisposable? Stage(string name, string? progress = null)
    {
        var current = Current.Value;
        if (current is null || current._disposed) return null;
        current.Write($"{name}: begin");
        if (progress is not null) current._progress?.Invoke(progress);
        return new Timing(current, name);
    }

    public static void Note(string message) => Current.Value?.Write(message);

    private void Write(string message)
    {
        if (!_disposed)
            _write(string.Create(CultureInfo.InvariantCulture,
                $"[Startup pid={Environment.ProcessId} elapsed={_elapsed.Elapsed.TotalMilliseconds:F1}ms] {message}"));
    }

    public void Dispose()
    {
        Write("launch instrumentation ended");
        _disposed = true;
        Current.Value = _previous;
    }

    private sealed class Timing(StartupDiagnostics owner, string name) : IDisposable
    {
        private readonly Stopwatch _timer = Stopwatch.StartNew();
        public void Dispose() => owner.Write(string.Create(CultureInfo.InvariantCulture,
            $"{name}: end duration={_timer.Elapsed.TotalMilliseconds:F1}ms"));
    }
}
