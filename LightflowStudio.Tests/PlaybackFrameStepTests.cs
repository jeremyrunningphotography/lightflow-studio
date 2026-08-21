using System.Windows;
using Xunit;

namespace LightflowStudio.Tests;

/// <summary>
/// Tests PlaybackFrameStep's direction-specific completion contract directly — the layer that actually failed
/// for backward stepping (see PlaybackFrameStep's own doc comment for the proven root cause: an earlier
/// revision applied the same short external boundary timeout to both directions, and backward stepping's
/// genuinely longer reconstruction routinely exceeded it, causing FrameStepQueue's drain loop to treat a
/// prematurely-cancelled call as "safely returned" and issue the next backward step while the abandoned one's
/// native work was still unsettled).
/// </summary>
public sealed class PlaybackFrameStepTests
{
    [Fact]
    public async Task Backward_IsNotTruncatedByForwardsBoundaryTimeout()
    {
        // Deliberately longer than ForwardBoundaryTimeout (5s) and does not observe cancellation at all —
        // if PlaybackFrameStep still applied an external timeout to backward stepping, this would either
        // throw (if RunAsync awaited with a token this fake actually observed) or, worse, "return" via
        // cancellation while this fake's delay is still running, which is exactly the unsafe premature-return
        // behavior under test. Completing successfully and only after the full delay proves neither happens.
        var service = new DelayedFakeService(stepDelay: TimeSpan.FromSeconds(6));

        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        await PlaybackFrameStep.RunAsync(service, forward: false);

        Assert.True(elapsed.Elapsed >= TimeSpan.FromSeconds(6),
            $"Backward step returned after {elapsed.Elapsed}, before its own 6s delay completed — it was truncated externally.");
        Assert.Equal(1, service.BackwardCalls);
        Assert.False(service.WasCancelled);
    }

    [Fact]
    public async Task Forward_IsStillBoundedByItsOwnBoundaryTimeout()
    {
        // The fix is direction-specific, not a removal of the forward boundary entirely — this pins that
        // forward stepping (which genuinely can wait forever at end-of-clip) still gets cut off promptly,
        // preserving the behavior the original #110 boundary-responsiveness fix established.
        var service = new DelayedFakeService(stepDelay: TimeSpan.FromSeconds(30), observeCancellation: true);

        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        await PlaybackFrameStep.RunAsync(service, forward: true);

        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(10),
            $"Forward step took {elapsed.Elapsed} to return; expected it to be cut off by its own ~5s boundary timeout.");
        Assert.True(service.WasCancelled);
    }

    private sealed class DelayedFakeService(TimeSpan stepDelay, bool observeCancellation = false) : IMediaPlaybackService
    {
        public int ForwardCalls { get; private set; }
        public int BackwardCalls { get; private set; }
        public bool WasCancelled { get; private set; }

        public MediaPlaybackSnapshot Snapshot { get; } = new(MediaPlaybackState.Paused, "clip.mp4", new(TimeSpan.Zero), TimeSpan.FromSeconds(60));
        public int Volume { get; set; } = 100;
        public bool Mute { get; set; }
        public MediaPlaybackSourceInfo? SourceInfo { get; } = new("clip.mp4", TimeSpan.FromSeconds(60), TimeSpan.Zero, 1920, 1080, [], null, false);
        public event EventHandler<MediaPlaybackSnapshot>? StateChanged { add { } remove { } }
        public event EventHandler<MediaPresentationTimestamp>? FramePresented { add { } remove { } }
        public MediaPlaybackPresentation CreatePresentation() => throw new NotSupportedException();
        public Task OpenAsync(string sourcePath, CancellationToken token = default) => throw new NotSupportedException();
        public Task CloseAsync(CancellationToken token = default) => Task.CompletedTask;
        public Task PlayAsync(CancellationToken token = default) => throw new NotSupportedException();
        public Task PauseAsync(CancellationToken token = default) => throw new NotSupportedException();
        public Task SeekAsync(TimeSpan position, CancellationToken token = default) => throw new NotSupportedException();
        public Task<MediaDecodedFrame> GetFrameAsync(TimeSpan position, CancellationToken token = default) => throw new NotSupportedException();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public async Task StepForwardAsync(CancellationToken token = default)
        {
            ForwardCalls++;
            try
            {
                // Mirrors PlaybackFrameStep's contract: only observe the caller's token if this fake was
                // explicitly asked to (forward's real boundary token should cancel this one).
                if (observeCancellation) await Task.Delay(stepDelay, token).ConfigureAwait(false);
                else await Task.Delay(stepDelay).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { WasCancelled = true; throw; }
        }

        public async Task StepBackwardAsync(CancellationToken token = default)
        {
            BackwardCalls++;
            try
            {
                if (observeCancellation) await Task.Delay(stepDelay, token).ConfigureAwait(false);
                else await Task.Delay(stepDelay).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { WasCancelled = true; throw; }
        }
    }
}
