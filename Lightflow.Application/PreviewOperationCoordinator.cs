using System.IO;

namespace LightflowStudio;

public interface IPreviewOperationCoordinator : IDisposable
{
    Task<IDisposable> EnterOperationAsync(CancellationToken cancellationToken = default);
    Task<IDisposable> EnterMaintenanceAsync(CancellationToken cancellationToken = default);
}

public sealed class PreviewOperationCoordinator : IPreviewOperationCoordinator
{
    private readonly SemaphoreSlim _turnstile = new(1, 1);
    private readonly SemaphoreSlim _readerGate = new(1, 1);
    private readonly SemaphoreSlim _resource = new(1, 1);
    private int _readers;
    private bool _disposed;

    public async Task<IDisposable> EnterOperationAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _turnstile.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _readerGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_readers == 0) await _resource.WaitAsync(cancellationToken).ConfigureAwait(false);
                _readers++;
            }
            finally { _readerGate.Release(); }
        }
        finally { _turnstile.Release(); }
        return new Lease(ReleaseOperation);
    }

    public async Task<IDisposable> EnterMaintenanceAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _turnstile.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { await _resource.WaitAsync(cancellationToken).ConfigureAwait(false); }
        catch { _turnstile.Release(); throw; }
        return new Lease(() => { _resource.Release(); _turnstile.Release(); });
    }

    private void ReleaseOperation()
    {
        _readerGate.Wait();
        try
        {
            _readers--;
            if (_readers == 0) _resource.Release();
        }
        finally { _readerGate.Release(); }
    }

    public void Dispose()
    {
        _disposed = true;
        _turnstile.Dispose();
        _readerGate.Dispose();
        _resource.Dispose();
    }

    private sealed class Lease(Action release) : IDisposable
    {
        private Action? _release = release;
        public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
    }
}

