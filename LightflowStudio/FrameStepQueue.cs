namespace LightflowStudio;

/// <summary>
/// Coalesces rapid frame-step input into a bounded net direction and applies one service operation at a time.
/// Reset starts a new session generation: pending intent and results/errors from the old generation are ignored.
/// This class owns only input policy; decoding and presentation behavior belong to the playback implementation.
/// </summary>
internal sealed class FrameStepQueue
{
    private const int MaxPendingMagnitude = 20;
    private readonly object _gate = new();
    private int _pending;
    private long _generation;
    private long _activeDrainGeneration = -1;

    internal int PendingDelta { get { lock (_gate) return _pending; } }
    internal bool IsDraining { get { lock (_gate) return _activeDrainGeneration == _generation; } }

    public void Reset()
    {
        lock (_gate)
        {
            _generation++;
            _pending = 0;
        }
    }

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

                try
                {
                    if (stepForward) await service.StepForwardAsync().ConfigureAwait(true);
                    else await service.StepBackwardAsync().ConfigureAwait(true);
                }
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
            lock (_gate)
            {
                if (_activeDrainGeneration == generation) _activeDrainGeneration = -1;
            }
        }
    }
}
