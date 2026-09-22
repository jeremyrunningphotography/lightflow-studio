namespace LightflowStudio;

internal interface ICatalogMutationParticipant
{
    CatalogMutationLifecycle Mutations { get; }
}

/// <summary>Counts complete logical operations, not connections or transactions. Reads remain independent.</summary>
internal sealed class CatalogMutationLifecycle : IDisposable
{
    private readonly object _sync = new();
    private readonly AsyncLocal<Admission?> _current = new();
    private TaskCompletionSource _changed = Signal();
    private int _active;
    private bool _closed;
    private bool _disposed;
    private long _generation;

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal static CatalogMutationLifecycle From(object participant) =>
        (participant as ICatalogMutationParticipant)?.Mutations
        ?? throw new InvalidOperationException("A Catalog writer must expose its owning mutation lifecycle.");

    // A HTTP callback does not inherit the sending operation's ExecutionContext.
    // Capture a bounded capability, never a boolean "ignore quiescence" override.
    internal Func<Func<Task>, Task> CaptureContinuation()
    {
        var parent = _current.Value ?? throw new InvalidOperationException("No accepted Catalog operation owns this continuation.");
        return async operation =>
        {
            Admission child;
            lock (_sync)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (parent.Released) throw new InvalidOperationException("The owning Catalog operation has completed.");
                _active++;
                child = new Admission(this);
            }
            using (child)
            {
                var previous = _current.Value;
                _current.Value = child;
                try { await operation().ConfigureAwait(false); }
                finally { _current.Value = previous; }
            }
        };
    }
    private void Changed()
    {
        var previous = _changed;
        _changed = Signal();
        previous.TrySetResult();
    }

    public async Task RunAsync(Func<Task> operation, CancellationToken token = default)
    {
        using var admission = await AdmitAsync(token);
        var previous = _current.Value;
        _current.Value = admission;
        try { await operation().ConfigureAwait(false); }
        finally { _current.Value = previous; }
    }

    public async Task<T> RunAsync<T>(Func<Task<T>> operation, CancellationToken token = default)
    {
        using var admission = await AdmitAsync(token);
        var previous = _current.Value;
        _current.Value = admission;
        try { return await operation().ConfigureAwait(false); }
        finally { _current.Value = previous; }
    }

    private async Task<Admission> AdmitAsync(CancellationToken token)
    {
        while (true)
        {
            token.ThrowIfCancellationRequested();
            Task changed;
            lock (_sync)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                // A nested operation is continuation of work accepted before admission closed.
                // Every nested lease is counted independently, so detached continuations cannot
                // disappear from the drain when their parent completes.
                if (!_closed || _current.Value is { Released: false } inherited && inherited.Owner == this)
                {
                    _active++;
                    return new Admission(this);
                }
                changed = _changed.Task;
            }
            await changed.WaitAsync(token).ConfigureAwait(false);
        }
    }

    public async Task<Quiescence> QuiesceAsync(CancellationToken token = default)
    {
        long generation;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_closed) throw new InvalidOperationException("Catalog quiescence is already owned.");
            if (_current.Value is { Released: false })
                throw new InvalidOperationException("A Catalog mutation cannot wait for itself to drain.");
            _closed = true;
            generation = ++_generation;
        }
        try
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();
                Task changed;
                lock (_sync)
                {
                    ObjectDisposedException.ThrowIf(_disposed, this);
                    if (_active == 0) return new Quiescence(this, generation);
                    changed = _changed.Task;
                }
                await changed.WaitAsync(token).ConfigureAwait(false);
            }
        }
        catch { Reopen(generation); throw; }
    }

    private void Reopen(long generation)
    {
        lock (_sync)
        {
            if (_disposed || generation != _generation) return;
            _closed = false;
            Changed();
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _disposed = _closed = true;
            Changed(); // Wake queued admissions/drains; none may remain stranded.
        }
    }

    private sealed class Admission(CatalogMutationLifecycle owner) : IDisposable
    {
        public CatalogMutationLifecycle Owner => owner;
        public bool Released { get; private set; }
        public void Dispose()
        {
            lock (owner._sync)
            {
                if (Released) return;
                Released = true;
                owner._active--;
                owner.Changed();
            }
        }
    }

    internal sealed class Quiescence(CatalogMutationLifecycle owner, long generation) : IDisposable
    {
        private int _finished;
        public void CompleteShutdown()
        {
            if (Interlocked.Exchange(ref _finished, 1) == 0) owner.Dispose();
        }
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _finished, 1) == 0) owner.Reopen(generation);
        }
    }
}
