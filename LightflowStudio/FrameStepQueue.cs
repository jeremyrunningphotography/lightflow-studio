namespace LightflowStudio;

/// <summary>
/// Serializes Previous/Next Frame requests against one <see cref="IMediaPlaybackService"/> so rapid repeated
/// clicks/key presses can never issue a new <c>StepForwardAsync</c>/<c>StepBackwardAsync</c> call before the
/// previous one has genuinely finished.
///
/// <para><b>Root cause this fixes:</b> before this class existed, each click called
/// <see cref="PlaybackFrameStep.RunAsync"/> independently. <see cref="MediaPlaybackService"/>'s own "latest
/// generation wins" operation model cancels a still-in-flight step's C# <em>wait</em> for the next request —
/// but <c>FlyleafPlaybackBackend.StepForwardAsync</c> calls <c>player.ShowFrameNext()</c> as a fire-and-forget
/// native request and only <em>waits</em> for its completion signal (a <c>CurTime</c> <c>PropertyChanged</c>
/// event); cancelling that wait does not tell Flyleaf's decoder to abandon the still-in-flight native
/// decode — <c>FlyleafPlaybackBackend.CancelPending</c>'s token is wired only into <c>OpenAsync</c>, never into
/// stepping. So a second rapid click could reach and re-enter <c>player.ShowFrameNext()</c> while the
/// <em>first</em> click's native decode was still genuinely in progress: two overlapping
/// <c>ShowFrameNext()</c> requests against the same decoder, a usage pattern the native engine was never
/// designed for and which produced the reported hang/crash under rapid clicking. Serializing at this layer —
/// never starting a new step until <see cref="PlaybackFrameStep.RunAsync"/> for the previous one has actually
/// returned — guarantees Flyleaf only ever receives one <c>ShowFrameNext()</c> request at a time, regardless
/// of how fast the user clicks.</para>
///
/// <para>One instance per player session (<c>TrimEditorWindow</c>/<c>PlayerViewerHost</c> each own one for
/// their control's lifetime, reused across every asset they open). Call <see cref="Reset"/> whenever the
/// underlying session changes (a new asset opens, the current one closes) so a still-draining backlog from the
/// <em>previous</em> session is discarded rather than continuing to apply steps to a service that may now hold
/// a different source or none at all. <see cref="Reset"/> only invalidates <em>future</em> loop iterations — a
/// single step already genuinely in flight when it is called keeps running to completion (there is no way to
/// abort the underlying native decode; see the root-cause note above), and its result is discarded rather than
/// applied or reported once it settles.</para>
///
/// <para>Requests accumulate as a single net pending delta (clamped to <see cref="MaxPendingMagnitude"/>, so a
/// stuck key or extreme rapid-fire input cannot grow an unbounded backlog) rather than queuing every individual
/// click: +1/+1/-1 nets to +1 pending, one further forward step, not three separately queued operations. This
/// preserves repeated-click intent — the net movement still happens — while keeping the worst-case backlog
/// small and bounded.</para>
///
/// <para><b>Confirmed and fixed residual risk:</b> the "never start step N+1 until step N has returned"
/// guarantee is only as good as what "returned" means — an earlier revision of <see cref="PlaybackFrameStep"/>
/// applied the same short external boundary timeout to <em>both</em> directions, and this class's own
/// serialization could not protect against a call that "returned" via premature external cancellation while
/// the native decode it triggered was still genuinely unsettled. That was confirmed to be exactly what
/// happened for backward stepping specifically: rapid Previous Frame still crashed after this class first
/// shipped, even though rapid Next Frame was fixed. Removing that external timeout from backward stepping
/// (see <see cref="PlaybackFrameStep"/>'s own doc comment) then exposed a second, deeper issue: backward
/// reconstruction's own internal forward-decode steps could land within roughly the last few frames of a
/// source, where <c>ShowFrameNext()</c> genuinely cannot decode any further at all (proven by directly polling
/// the engine's position with no dependency on its completion event — see
/// <c>FlyleafPlaybackBackend.TryBoundedStepForwardAsync</c>'s doc comment) — leaving reconstruction to hang for
/// its internal steps' own 10-second fallback with no bound at this layer. That inner layer now bounds each
/// individual internal step and settles for the closest already-confirmed predecessor instead of waiting.
/// This class's own serialization contract — genuinely never starting step N+1 before step N's call returns —
/// was correct throughout both fixes; the defect was in what "returned" was allowed to mean, at two different
/// layers, not in this class.</para>
/// </summary>
internal sealed class FrameStepQueue
{
    private const int MaxPendingMagnitude = 20;
    private readonly object _gate = new();
    private int _pending;
    private long _generation;

    /// <summary>
    /// The generation a drain loop is currently running for, or -1 if none is active. Generation-scoped
    /// (rather than a plain bool) so that a request arriving for a <em>new</em> generation right after
    /// <see cref="Reset"/> always starts its own loop immediately, even while the previous generation's loop is
    /// still mid-await on its one already-in-flight step and has not yet noticed it is stale — a plain bool
    /// would report "already draining" for that stale loop and silently drop the new request until the next
    /// unrelated click (see this class's own regression tests for the exact scenario).
    /// </summary>
    private long _activeDrainGeneration = -1;

    /// <summary>Not-yet-applied net forward(positive)/backward(negative) requests. Exposed for tests only.</summary>
    internal int PendingDelta { get { lock (_gate) return _pending; } }

    /// <summary>True while a drain loop is actively applying requests for the current generation. Exposed for tests only.</summary>
    internal bool IsDraining { get { lock (_gate) return _activeDrainGeneration == _generation; } }

    /// <summary>
    /// Discards any not-yet-applied requests and starts a new generation: the previous generation's drain loop
    /// (if any) stops applying further steps and discards/never-reports the result of whichever single step
    /// was already genuinely in flight when this was called, without needing to have "noticed" the reset first.
    /// </summary>
    public void Reset()
    {
        lock (_gate)
        {
            _generation++;
            _pending = 0;
        }
    }

    /// <summary>
    /// Queues one step in the requested direction and returns immediately — this never awaits a backend call
    /// itself, so rapid repeated calls stay cheap and responsive regardless of how slowly the engine can
    /// actually process them. Starts a drain loop for the current generation only if one is not already
    /// running for it; a running loop for this same generation will pick up this request on its next iteration.
    /// <paramref name="onError"/> is invoked (marshaled back to this method's calling context, since
    /// <see cref="RequestStep"/> is always called from the UI thread) only for a failure that is still current
    /// by the time it is caught — a failure belonging to a generation <see cref="Reset"/> has since superseded
    /// is discarded rather than reported, so it can never describe an asset the caller has already moved on from.
    /// Every internal <c>lock</c> is a defensive-correctness measure, not a substitute for this: both
    /// <see cref="RequestStep"/> and every <see cref="DrainAsync"/> continuation (via <c>ConfigureAwait(true)</c>)
    /// are expected to run only on the caller's original thread — today, always the WPF UI thread, since both
    /// current consumers call this exclusively from button/keyboard handlers. A caller violating that (e.g. a
    /// future background-thread-driven step) is outside what this class's ownership handoff was designed for.
    /// </summary>
    public void RequestStep(IMediaPlaybackService service, bool forward, Action<Exception> onError)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(onError);
        long generation;
        bool startDrain;
        lock (_gate)
        {
            _pending = Math.Clamp(_pending + (forward ? 1 : -1), -MaxPendingMagnitude, MaxPendingMagnitude);
            generation = _generation;
            startDrain = _activeDrainGeneration != generation;
            if (startDrain) _activeDrainGeneration = generation;
        }
        if (startDrain) _ = DrainAsync(service, generation, onError);
    }

    private async Task DrainAsync(IMediaPlaybackService service, long generation, Action<Exception> onError)
    {
        try
        {
            while (true)
            {
                bool stepForward;
                lock (_gate)
                {
                    if (generation != _generation || _pending == 0) return;
                    stepForward = _pending > 0;
                    _pending += stepForward ? -1 : 1;
                }

                try { await PlaybackFrameStep.RunAsync(service, stepForward).ConfigureAwait(true); }
                catch (Exception exception)
                {
                    bool stillCurrent;
                    lock (_gate) { stillCurrent = generation == _generation; }
                    if (stillCurrent) onError(exception);
                }
            }
        }
        finally
        {
            // Only clears ownership if this loop still owns it — a generation this loop no longer represents
            // (Reset already moved on and a new loop already claimed _activeDrainGeneration) must never have
            // its ownership stolen out from under it by this now-stale loop's own cleanup.
            lock (_gate) { if (_activeDrainGeneration == generation) _activeDrainGeneration = -1; }
        }
    }
}
