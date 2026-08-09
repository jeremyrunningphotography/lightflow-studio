namespace LightflowStudio;

internal sealed class MediaPlaybackCoordinator : IAsyncDisposable
{
    private readonly SemaphoreSlim _ownership = new(1, 1);
    private readonly Func<IMediaPlaybackService> _serviceFactory;
    private IMediaPlaybackService? _service;
    private Guid? _owner;

    public MediaPlaybackCoordinator(Func<IMediaPlaybackService> serviceFactory) => _serviceFactory = serviceFactory;

    public async Task<MediaPlaybackLease> AcquireAsync(Guid owner, CancellationToken token = default)
    {
        await _ownership.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (_owner != owner && _service is not null) await _service.CloseAsync(token).ConfigureAwait(false);
            _service ??= _serviceFactory();
            _owner = owner;
            return new(this, owner, _service);
        }
        finally { _ownership.Release(); }
    }

    private async ValueTask ReleaseAsync(Guid owner)
    {
        await _ownership.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_owner != owner || _service is null) return;
            await _service.CloseAsync().ConfigureAwait(false);
            _owner = null;
        }
        finally { _ownership.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        if (_service is not null) await _service.DisposeAsync().ConfigureAwait(false);
        _ownership.Dispose();
    }

    internal sealed class MediaPlaybackLease : IAsyncDisposable
    {
        private readonly MediaPlaybackCoordinator _coordinator;
        private readonly Guid _owner;
        private int _released;

        internal MediaPlaybackLease(MediaPlaybackCoordinator coordinator, Guid owner, IMediaPlaybackService service)
        {
            _coordinator = coordinator;
            _owner = owner;
            Service = service;
        }

        public IMediaPlaybackService Service { get; }
        public ValueTask DisposeAsync() => Interlocked.Exchange(ref _released, 1) == 0
            ? _coordinator.ReleaseAsync(_owner)
            : ValueTask.CompletedTask;
    }
}
