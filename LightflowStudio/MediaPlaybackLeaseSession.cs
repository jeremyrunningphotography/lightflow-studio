namespace LightflowStudio;

/// <summary>
/// The shared thin lease wrapper around the single global <see cref="MediaPlaybackCoordinator"/>: own owner
/// identity, lazily acquire/reuse one lease, open a source through it, release on dispose. <see cref="TrimEditorPlayback"/>
/// derives from this to add trim-boundary seeking; <see cref="PlayerViewerHost"/> uses it directly, since
/// reviewing media in the Browser has no In/Out concept to add. Two consumer-specific owners of one shared
/// coordinator, not two playback engines — every source transfer still goes through the same
/// <see cref="MediaPlaybackCoordinator.AcquireAsync"/>/lease mechanism every other consumer already uses.
/// </summary>
internal class MediaPlaybackLeaseSession : IAsyncDisposable
{
    private readonly MediaPlaybackCoordinator _coordinator;
    private readonly Guid _owner = Guid.NewGuid();
    private MediaPlaybackCoordinator.MediaPlaybackLease? _lease;

    public MediaPlaybackLeaseSession(MediaPlaybackCoordinator coordinator) => _coordinator = coordinator;

    public IMediaPlaybackService? Service => _lease?.Service;

    public async Task<IMediaPlaybackService> OpenAsync(string sourcePath, CancellationToken token = default,
        Action<IMediaPlaybackService>? configureBeforeOpen = null)
    {
        _lease ??= await _coordinator.AcquireAsync(_owner, token).ConfigureAwait(false);
        configureBeforeOpen?.Invoke(_lease.Service);
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
