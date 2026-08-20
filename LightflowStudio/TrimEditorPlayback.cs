namespace LightflowStudio;

internal enum TrimBoundary { In, Out }

/// <summary>Adds trim-boundary seeking to the shared <see cref="MediaPlaybackLeaseSession"/> lease wrapper — see that class for the acquire/open/dispose mechanics this reuses unchanged.</summary>
internal sealed class TrimEditorPlayback(MediaPlaybackCoordinator coordinator) : MediaPlaybackLeaseSession(coordinator)
{
    public async Task<MediaPresentationTimestamp?> SeekToInitialPositionAsync(
        TrimSelection selection, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(selection);
        var service = Service ?? throw new InvalidOperationException("Open the playback source before setting its initial position.");
        if (selection.InitialPlaybackPosition <= TimeSpan.Zero) return service.Snapshot.DisplayedTimestamp;
        await service.SeekAsync(selection.InitialPlaybackPosition, token).ConfigureAwait(false);
        return service.Snapshot.DisplayedTimestamp;
    }

    public async Task<MediaPresentationTimestamp?> SeekToBoundaryAsync(
        TrimSelection selection, TrimBoundary boundary, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(selection);
        var service = Service ?? throw new InvalidOperationException("Open the playback source before seeking to a trim boundary.");
        var position = boundary == TrimBoundary.In ? selection.In : selection.Out;
        await service.SeekAsync(position, token).ConfigureAwait(false);
        return service.Snapshot.DisplayedTimestamp;
    }
}
