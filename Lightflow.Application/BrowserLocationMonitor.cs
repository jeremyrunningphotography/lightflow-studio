namespace Lightflow.Application;

/// <summary>OS notification hints only; the current native snapshot remains authoritative.</summary>
public interface IBrowserLocationChangeSource : IDisposable
{
    event Action? Changed;
}

/// <summary>Owns an event subscription and serial, debounced, cancellable reconciliation. No polling.</summary>
public sealed class BrowserLocationMonitor : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly IBrowserLocationChangeSource _source;
    private readonly Func<CancellationToken, Task> _reconcile;
    private readonly TimeSpan _debounce;
    private CancellationTokenSource? _pending;
    private Task _tail = Task.CompletedTask;
    private bool _disposed;
    private long? _readyEpoch;

    public BrowserLocationMonitor(IBrowserLocationChangeSource source, Func<CancellationToken, Task> reconcile,
        TimeSpan? debounce = null)
    {
        _source=source; _reconcile=reconcile; _debounce=debounce ?? TimeSpan.FromMilliseconds(250);
        if(_debounce<TimeSpan.Zero)throw new ArgumentOutOfRangeException(nameof(debounce));
        _source.Changed+=RequestUpdate;
    }
    public void RequestAfterOpen(long epoch)
    {
        lock(_gate)
        {
            if(_disposed || _readyEpoch==epoch)return;
            _readyEpoch=epoch; RequestUpdate();
        }
    }
    public void RequestUpdate()
    {
        lock(_gate)
        {
            if(_disposed)return;
            _pending?.Cancel();
            var cancellation=_pending=new(); var previous=_tail;
            _tail=Task.Run(async()=>
            {
                try
                {
                    await Task.Delay(_debounce,cancellation.Token).ConfigureAwait(false);
                    await previous.WaitAsync(cancellation.Token).ConfigureAwait(false);
                    cancellation.Token.ThrowIfCancellationRequested();
                    await _reconcile(cancellation.Token).WaitAsync(cancellation.Token).ConfigureAwait(false);
                }
                catch(OperationCanceledException) when(cancellation.IsCancellationRequested) { }
                catch(Exception error) { Console.Error.WriteLine($"Locations notification reconciliation failed: {error}"); }
                finally
                {
                    lock(_gate) { if(ReferenceEquals(_pending,cancellation))_pending=null; cancellation.Dispose(); }
                }
            });
        }
    }
    public Task WhenIdleAsync() { lock(_gate)return _tail; }
    public async ValueTask DisposeAsync()
    {
        Task tail; bool stop;
        lock(_gate)
        {
            stop=!_disposed; _disposed=true;
            if(stop) { _source.Changed-=RequestUpdate; _pending?.Cancel(); }
            tail=_tail;
        }
        // Native disposal may wait for a callback; never hold the managed gate here.
        if(stop)_source.Dispose();
        await tail.ConfigureAwait(false);
    }
}
