namespace LightflowStudio;

internal sealed class TrimEditorPlayback : IAsyncDisposable
{
    private readonly MediaPlaybackCoordinator _coordinator;
    private readonly Guid _owner = Guid.NewGuid();
    private MediaPlaybackCoordinator.MediaPlaybackLease? _lease;

    public TrimEditorPlayback(MediaPlaybackCoordinator coordinator) => _coordinator = coordinator;

    public IMediaPlaybackService? Service => _lease?.Service;

    public async Task<IMediaPlaybackService> OpenAsync(string sourcePath, CancellationToken token = default)
    {
        _lease ??= await _coordinator.AcquireAsync(_owner, token).ConfigureAwait(false);
        await _lease.Service.OpenAsync(sourcePath, token).ConfigureAwait(false);
        return _lease.Service;
    }

    public async ValueTask DisposeAsync()
    {
        if (_lease is null) return;
        await _lease.DisposeAsync().ConfigureAwait(false);
        _lease = null;
    }
}
