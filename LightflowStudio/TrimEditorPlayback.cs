namespace LightflowStudio;

internal enum TrimBoundary { In, Out }

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

    public async Task<MediaPresentationTimestamp?> SeekToInitialPositionAsync(
        TrimSelection selection, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(selection);
        if (_lease is null) throw new InvalidOperationException("Open the playback source before setting its initial position.");
        if (selection.InitialPlaybackPosition <= TimeSpan.Zero) return _lease.Service.Snapshot.DisplayedTimestamp;
        await _lease.Service.SeekAsync(selection.InitialPlaybackPosition, token).ConfigureAwait(false);
        return _lease.Service.Snapshot.DisplayedTimestamp;
    }

    public async Task<MediaPresentationTimestamp?> SeekToBoundaryAsync(
        TrimSelection selection, TrimBoundary boundary, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(selection);
        if (_lease is null) throw new InvalidOperationException("Open the playback source before seeking to a trim boundary.");
        var position = boundary == TrimBoundary.In ? selection.In : selection.Out;
        await _lease.Service.SeekAsync(position, token).ConfigureAwait(false);
        return _lease.Service.Snapshot.DisplayedTimestamp;
    }

    public async ValueTask DisposeAsync()
    {
        if (_lease is null) return;
        await _lease.DisposeAsync().ConfigureAwait(false);
        _lease = null;
    }
}
