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
/// Generic click-anywhere-to-set support for a plain numeric Slider (see PlayerViewerHost's VolumeSlider) —
/// the PlaybackTimelineSlider style's track RepeatButtons only nudge by Slider.DecreaseLarge/IncreaseLarge on a
/// click (the ordinary WPF Slider default), so a caller intercepts PreviewMouseLeftButtonDown on the track
/// (never the thumb, which must still start an ordinary drag) and uses this to compute the value to jump to.
/// </summary>
internal static class SliderClickToSet
{
    public static double ValueFromCoordinate(double x, double usableWidth, double minimum, double maximum)
    {
        if (maximum <= minimum || !double.IsFinite(usableWidth) || usableWidth <= 0) return minimum;
        var boundedX = double.IsFinite(x) ? Math.Clamp(x, 0, usableWidth) : 0;
        return minimum + (maximum - minimum) * boundedX / usableWidth;
    }
}

/// <summary>
/// Shared by every UI consumer of <see cref="IMediaPlaybackService.StepForwardAsync"/>/<see cref="IMediaPlaybackService.StepBackwardAsync"/>
/// (<c>TrimEditorWindow</c>, <c>PlayerViewerHost</c>) so per-direction step-completion handling is defined
/// exactly once. Forward and backward stepping have genuinely different completion contracts at the backend
/// (<c>FlyleafPlaybackBackend</c>) and are handled differently here — see each direction's own remarks below.
/// <see cref="MediaPlaybackService"/>'s "latest generation wins" operation-cancellation handling already treats
/// a caller token cancelling as equivalent to being superseded by a newer request and completes quietly rather
/// than throwing, so swallowing <see cref="OperationCanceledException"/> here reflects that existing contract
/// rather than adding new boundary-detection logic. Other exceptions propagate for the caller's own
/// status/message handling — this only owns step-completion timing, not general error presentation.
/// </summary>
internal static class PlaybackFrameStep
{
    /// <summary>
    /// At the last decoded frame, <c>FlyleafPlaybackBackend.StepForwardAsync</c> calls <c>ShowFrameNext()</c>
    /// and then waits for a <c>CurTime</c> change that will provably never arrive — nothing internal to the
    /// engine bounds that wait (its own internal fallback only gives up after 10 seconds), so this external
    /// token is what keeps repeated forward stepping at a clip boundary responsive.
    /// </summary>
    private static readonly TimeSpan ForwardBoundaryTimeout = TimeSpan.FromSeconds(5);

    public static async Task RunAsync(IMediaPlaybackService service, bool forward)
    {
        if (forward)
        {
            using var boundaryGuard = new CancellationTokenSource(ForwardBoundaryTimeout);
            try { await service.StepForwardAsync(boundaryGuard.Token).ConfigureAwait(true); }
            catch (OperationCanceledException) { }
            return;
        }

        // Backward stepping (FlyleafPlaybackBackend.StepBackwardAsync) has no equivalent OUTER "wait for an
        // event that will never come" hazard at this layer — VFR-correct reconstruction re-seeks and decodes
        // forward toward the target, already bounded by its own attempt/frame-count limits (up to 8
        // doubling-window attempts, each up to ~2000 forward steps) rather than a wall clock, and settles for
        // its closest confirmed predecessor rather than retrying once the engine's own internal completion wait
        // has genuinely concluded (by timing out) for a decode step it cannot advance any further — see
        // FlyleafPlaybackBackend.StepBackwardAsync's own doc comment for the proven near-end-of-source engine
        // limitation this handles and why every internal step is awaited to its own genuine conclusion rather
        // than abandoned early (an earlier revision abandoned a stuck internal step via an additional short
        // timeout and let reconstruction proceed while that step could still genuinely be running natively —
        // this reproduced a crash from as few as two sequential backward requests; removed). It still throws a
        // genuine InvalidOperationException in the residual case where no predecessor could be found at all. An
        // earlier revision of this method applied the *same* short external timeout uniformly to both
        // directions; that was the proven root cause of a rapid-Previous-Frame crash. A single reconstruction
        // genuinely needing many forward steps routinely took longer than that timeout (confirmed empirically —
        // see FlyleafPlaybackIntegrationTests' own comment on real decode being slow in this environment), so the
        // external token fired mid-reconstruction far more often than at a genuine boundary: cancelling the C#
        // wait for whichever native seek/step was currently in flight, exactly like the original forward-only
        // bug, but now abandoning a multi-operation sequence mid-flight rather than one call — leaving Flyleaf
        // in a non-quiescent state right before the queue's drain loop, seeing that (falsely) "returned" call,
        // issued the next backward step. Passing no additional token here does not remove cancellation safety:
        // MediaPlaybackService links its own session-lifetime token into every operation regardless of what
        // caller token is supplied, so a genuine session close/teardown still cancels an in-flight
        // reconstruction correctly (FrameStepQueue.Reset, called from both hosts' close paths, still discards
        // the result); only the redundant, too-short external wall-clock cutoff is removed.
        try { await service.StepBackwardAsync().ConfigureAwait(true); }
        catch (OperationCanceledException) { }
    }
}
