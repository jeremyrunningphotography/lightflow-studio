namespace LightflowStudio;

/// <summary>Bounded, keyed derived work. Subscribers share decoding and can promote queued work.</summary>
internal sealed class DerivedFrameDemands(int concurrency = 2) : IDisposable
{
    private readonly object _sync = new();
    private readonly Dictionary<string, Entry> _entries = [];
    private int _running;
    private bool _disposed;

    internal async Task<string?> RequestAsync(string key, ThumbnailPriority priority,
        Func<CancellationToken, Task<string?>> render, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Entry entry;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_entries.TryGetValue(key, out entry!))
                _entries.Add(key, entry = new(render));
            entry.Subscribers++;
            if (priority == ThumbnailPriority.Visible) entry.Visible++;
            Pump();
        }
        try
        {
            try { return await entry.Completion.Task.WaitAsync(token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (!token.IsCancellationRequested && !_disposed)
            {
                // A new subscriber can arrive while abandoned work is stopping. Wait for its
                // removal, then request afresh; never start a second decoder for the same key.
                return await RequestAsync(key, priority, render, token).ConfigureAwait(false);
            }
        }
        finally
        {
            lock (_sync)
            {
                entry.Subscribers--;
                if (priority == ThumbnailPriority.Visible) entry.Visible--;
                if (entry.Subscribers == 0 && !entry.Completion.Task.IsCompleted) entry.Cancellation.Cancel();
            }
        }
    }

    private void Pump()
    {
        while (!_disposed && _running < concurrency)
        {
            var next = _entries.FirstOrDefault(pair => !pair.Value.Started && pair.Value.Visible > 0);
            if (next.Value is null) next = _entries.FirstOrDefault(pair => !pair.Value.Started);
            if (next.Value is null) return;
            next.Value.Started = true;
            _running++;
            _ = Task.Run(() => RunAsync(next.Key, next.Value));
        }
    }

    private async Task RunAsync(string key, Entry entry)
    {
        string? result = null;
        Exception? error = null;
        try
        {
            entry.Cancellation.Token.ThrowIfCancellationRequested();
            result = await entry.Render(entry.Cancellation.Token).ConfigureAwait(false);
        }
        catch (Exception exception) { error = exception; }
        lock (_sync)
        {
            _entries.Remove(key);
            _running--;
            if (error is OperationCanceledException) entry.Completion.TrySetCanceled();
            else if (error is not null) entry.Completion.TrySetException(error);
            else entry.Completion.TrySetResult(result);
            entry.Cancellation.Dispose();
            Pump();
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _disposed = true;
            foreach (var entry in _entries.Values)
            {
                entry.Cancellation.Cancel();
                if (!entry.Started) entry.Completion.TrySetCanceled();
            }
        }
    }

    private sealed class Entry(Func<CancellationToken, Task<string?>> render)
    {
        internal readonly Func<CancellationToken, Task<string?>> Render = render;
        internal readonly CancellationTokenSource Cancellation = new();
        internal readonly TaskCompletionSource<string?> Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool Started;
        internal int Subscribers;
        internal int Visible;
    }
}
