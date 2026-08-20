namespace LightflowStudio;

internal enum TimelinePointerTarget
{
    Track,
    PlayheadThumb,
    TrimMarker
}

internal static class TimelineSeek
{
    public static bool ShouldSeek(TimelinePointerTarget target) => target == TimelinePointerTarget.Track;

    public static TimeSpan PositionFromCoordinate(double x, double usableWidth, TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero || !double.IsFinite(usableWidth) || usableWidth <= 0) return TimeSpan.Zero;
        var boundedX = double.IsFinite(x) ? Math.Clamp(x, 0, usableWidth) : 0;
        return TimeSpan.FromTicks((long)Math.Round(duration.Ticks * boundedX / usableWidth));
    }
}

/// <summary>
/// Shared by every UI consumer of <see cref="IMediaPlaybackService.StepForwardAsync"/>/<see cref="IMediaPlaybackService.StepBackwardAsync"/>
/// (<c>TrimEditorWindow</c>, <c>PlayerViewerHost</c>) so the boundary-responsiveness bound is defined exactly
/// once. At the last decoded frame, the engine's own wait for a timestamp change that will never come only
/// gives up after its own internal 10-second timeout; a bounded caller-supplied token limits this instead.
/// <see cref="MediaPlaybackService"/>'s "latest generation wins" operation-cancellation handling already
/// treats a caller token cancelling as equivalent to being superseded by a newer request and completes quietly
/// rather than throwing, so swallowing <see cref="OperationCanceledException"/> here reflects that existing
/// contract rather than adding new boundary-detection logic. Other exceptions propagate for the caller's own
/// status/message handling — this only owns the boundary-timeout concern, not general error presentation.
/// <see cref="BoundaryTimeout"/> applies to every step, not only ones that actually hit a boundary: backward
/// stepping over VFR source can legitimately re-seek and decode forward through up to ~2000 frames per attempt
/// across up to 8 doubling-window attempts (see <c>FlyleafPlaybackBackend.StepBackwardAsync</c>), so this is
/// deliberately more generous than the minimum needed to fix the boundary case alone — halving the previous
/// unbounded-up-to-10-second worst case rather than capping tight enough to risk truncating a legitimate, if
/// slow, reconstruction on a large/high-bitrate or network-hosted source.
/// </summary>
internal static class PlaybackFrameStep
{
    private static readonly TimeSpan BoundaryTimeout = TimeSpan.FromSeconds(5);

    public static async Task RunAsync(IMediaPlaybackService service, bool forward)
    {
        using var boundaryGuard = new CancellationTokenSource(BoundaryTimeout);
        try
        {
            if (forward) await service.StepForwardAsync(boundaryGuard.Token).ConfigureAwait(true);
            else await service.StepBackwardAsync(boundaryGuard.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) { }
    }
}
