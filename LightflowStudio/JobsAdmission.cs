namespace LightflowStudio;

/// <summary>Application-wide admission only; capability adapters retain execution and recovery.</summary>
internal sealed class JobsAdmission(int maximum, bool paused = false)
{
    private readonly object _sync = new();
    private readonly List<Request> _waiting = [];
    private readonly Dictionary<Guid, string?> _active = [];
    private int _maximum = EncodingJobConcurrency.Validate(maximum);
    private bool _paused = paused;
    public event Action? Changed;
    public int Maximum { get { lock (_sync) return _maximum; } set { lock (_sync) _maximum = EncodingJobConcurrency.Validate(value); Pump(); } }
    public bool IsPaused { get { lock (_sync) return _paused; } set { lock (_sync) _paused = value; Pump(); } }
    public bool HasWork { get { lock (_sync) return _active.Count > 0 || _waiting.Any(item => !item.Held); } }

    public void Submit(Guid id, Action<IDisposable> start, bool held = false, string? lane = null, bool pump = true)
    {
        lock (_sync)
        {
            if (_active.ContainsKey(id) || _waiting.Any(item => item.Id == id)) return;
            _waiting.Add(new(id, start, held, lane));
        }
        if (pump) Pump();
    }

    public bool Hold(Guid id, bool held)
    {
        lock (_sync)
        {
            var index = _waiting.FindIndex(item => item.Id == id);
            if (index < 0) return !_active.ContainsKey(id);
            _waiting[index] = _waiting[index] with { Held = held };
        }
        // Resume is pumped explicitly after the owning adapter has published its state.
        return true;
    }

    public void Remove(Guid id) { lock (_sync) _waiting.RemoveAll(item => item.Id == id); }
    public void Swap(Guid first, Guid second)
    {
        lock (_sync)
        {
            var a = _waiting.FindIndex(item => item.Id == first);
            var b = _waiting.FindIndex(item => item.Id == second);
            if (a >= 0 && b >= 0) (_waiting[a], _waiting[b]) = (_waiting[b], _waiting[a]);
        }
    }

    public async Task<IDisposable> AcquireAsync(Guid id, CancellationToken token, string? lane = null)
    {
        var ready = new TaskCompletionSource<IDisposable>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = token.Register(() => { Remove(id); ready.TrySetCanceled(token); });
        if (!token.IsCancellationRequested)
            Submit(id, lease => { if (!ready.TrySetResult(lease)) lease.Dispose(); }, lane: lane);
        try { return await ready.Task.ConfigureAwait(false); }
        finally { Remove(id); }
    }

    public void Pump()
    {
        List<(Request Request, IDisposable Lease)> starts = [];
        lock (_sync)
        {
            while (!_paused && _active.Count < _maximum)
            {
                var index = _waiting.FindIndex(item => !item.Held &&
                    (item.Lane is null || !_active.Values.Contains(item.Lane)));
                if (index < 0) break;
                var request = _waiting[index];
                _waiting.RemoveAt(index);
                _active.Add(request.Id, request.Lane);
                starts.Add((request, new Lease(this, request.Id)));
            }
        }
        foreach (var (request, lease) in starts)
        {
            try { request.Start(lease); }
            catch { lease.Dispose(); throw; }
        }
        Changed?.Invoke();
    }

    private sealed record Request(Guid Id, Action<IDisposable> Start, bool Held, string? Lane);
    private sealed class Lease(JobsAdmission owner, Guid id) : IDisposable
    {
        private int _disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            lock (owner._sync) owner._active.Remove(id);
            owner.Pump();
        }
    }
}
