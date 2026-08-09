namespace LightflowStudio;

using System.IO;

internal sealed class MediaPlaybackService : IMediaPlaybackService
{
    private readonly IMediaPlaybackBackend _backend;
    private readonly SemaphoreSlim _operations = new(1, 1);
    private CancellationTokenSource? _latestOperation;
    private long _generation;
    private bool _disposed;

    public MediaPlaybackService(IMediaPlaybackBackend backend)
    {
        _backend = backend;
        _backend.FramePresented += Backend_FramePresented;
        _backend.Failed += Backend_Failed;
    }

    public MediaPlaybackSnapshot Snapshot { get; private set; } = new(MediaPlaybackState.Empty, null, null, null);
    public MediaPlaybackSourceInfo? SourceInfo { get; private set; }
    public event EventHandler<MediaPlaybackSnapshot>? StateChanged;
    public event EventHandler<MediaPresentationTimestamp>? FramePresented;

    internal System.Windows.FrameworkElement CreatePresentationSurface() => _backend.CreatePresentationSurface();

    public Task OpenAsync(string sourcePath, CancellationToken token = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        var fullPath = Path.GetFullPath(sourcePath);
        var operation = BeginLatestOperation(token);
        _backend.CancelPending();
        return RunOpenAsync(fullPath, operation.Generation, operation.Token);
    }

    public Task SeekAsync(TimeSpan position, CancellationToken token = default)
    {
        var operation = BeginLatestOperation(token);
        _backend.CancelPending();
        return RunPositionOperationAsync(position, operation.Generation, operation.Token);
    }

    public async Task CloseAsync(CancellationToken token = default)
    {
        var operation = BeginLatestOperation(token);
        _backend.CancelPending();
        await _operations.WaitAsync(operation.Token).ConfigureAwait(false);
        try
        {
            await _backend.CloseAsync(operation.Token).ConfigureAwait(false);
            if (IsCurrent(operation.Generation))
            {
                SourceInfo = null;
                Publish(new(MediaPlaybackState.Empty, null, null, null));
            }
        }
        finally { _operations.Release(); }
    }

    public Task PlayAsync(CancellationToken token = default) => RunStateOperationAsync(
        MediaPlaybackState.Playing, _backend.PlayAsync, token);

    public Task PauseAsync(CancellationToken token = default) => RunStateOperationAsync(
        MediaPlaybackState.Paused, _backend.PauseAsync, token);

    public Task StepForwardAsync(CancellationToken token = default) => RunStepAsync(_backend.StepForwardAsync, token);

    public Task StepBackwardAsync(CancellationToken token = default) => RunStepAsync(_backend.StepBackwardAsync, token);

    public async Task<MediaDecodedFrame> GetFrameAsync(TimeSpan position, CancellationToken token = default)
    {
        ThrowIfDisposed();
        return await _backend.GetFrameAsync(position, token).ConfigureAwait(false);
    }

    private async Task RunOpenAsync(string sourcePath, long generation, CancellationToken token)
    {
        if (IsCurrent(generation)) SourceInfo = null;
        PublishIfCurrent(generation, new(MediaPlaybackState.Loading, sourcePath, null, null));
        await _operations.WaitAsync(token).ConfigureAwait(false);
        try
        {
            await _backend.CloseAsync(CancellationToken.None).ConfigureAwait(false);
            var opened = await _backend.OpenAsync(sourcePath, token).ConfigureAwait(false);
            if (!IsCurrent(generation)) return;
            SourceInfo = opened.Source;
            Publish(new(MediaPlaybackState.Paused, sourcePath, opened.FirstFrame, opened.Source.Duration));
        }
        catch (OperationCanceledException) when (!IsCurrent(generation) || token.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (IsCurrent(generation)) PublishFailure(sourcePath, exception);
        }
        finally { _operations.Release(); }
    }

    private async Task RunPositionOperationAsync(TimeSpan position, long generation, CancellationToken token)
    {
        EnsureLoaded();
        var resume = Snapshot.State == MediaPlaybackState.Playing;
        PublishIfCurrent(generation, Snapshot with { State = MediaPlaybackState.Seeking, Error = null });
        await _operations.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var timestamp = await _backend.SeekAsync(position, token).ConfigureAwait(false);
            if (!IsCurrent(generation)) return;
            Publish(Snapshot with { State = resume ? MediaPlaybackState.Playing : MediaPlaybackState.Paused, DisplayedTimestamp = timestamp });
            if (resume) await _backend.PlayAsync(token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!IsCurrent(generation) || token.IsCancellationRequested) { }
        finally { _operations.Release(); }
    }

    private async Task RunStateOperationAsync(MediaPlaybackState state, Func<CancellationToken, Task> action, CancellationToken token)
    {
        EnsureLoaded();
        await action(token).ConfigureAwait(false);
        Publish(Snapshot with { State = state, Error = null });
    }

    private async Task RunStepAsync(Func<CancellationToken, Task<MediaPresentationTimestamp>> action, CancellationToken token)
    {
        EnsureLoaded();
        await _backend.PauseAsync(token).ConfigureAwait(false);
        var timestamp = await action(token).ConfigureAwait(false);
        Publish(Snapshot with { State = MediaPlaybackState.Paused, DisplayedTimestamp = timestamp, Error = null });
    }

    private (long Generation, CancellationToken Token) BeginLatestOperation(CancellationToken callerToken)
    {
        ThrowIfDisposed();
        var replacement = CancellationTokenSource.CreateLinkedTokenSource(callerToken);
        var previous = Interlocked.Exchange(ref _latestOperation, replacement);
        previous?.Cancel();
        previous?.Dispose();
        return (Interlocked.Increment(ref _generation), replacement.Token);
    }

    private bool IsCurrent(long generation) => generation == Interlocked.Read(ref _generation);
    private void PublishIfCurrent(long generation, MediaPlaybackSnapshot snapshot) { if (IsCurrent(generation)) Publish(snapshot); }

    private void Backend_FramePresented(object? sender, MediaPresentationTimestamp timestamp)
    {
        if (_disposed || Snapshot.State is MediaPlaybackState.Empty or MediaPlaybackState.Loading) return;
        Publish(Snapshot with { DisplayedTimestamp = timestamp });
        FramePresented?.Invoke(this, timestamp);
    }

    private void Backend_Failed(object? sender, MediaPlaybackError error)
    {
        if (!_disposed) Publish(Snapshot with { State = MediaPlaybackState.Failed, Error = error });
    }

    private void PublishFailure(string sourcePath, Exception exception) => Publish(new(
        MediaPlaybackState.Failed,
        sourcePath,
        null,
        null,
        new(MediaPlaybackErrorKind.OperationFailed, "The video could not be opened for preview.", exception.Message)));

    private void Publish(MediaPlaybackSnapshot snapshot)
    {
        Snapshot = snapshot;
        StateChanged?.Invoke(this, snapshot);
    }

    private void EnsureLoaded()
    {
        ThrowIfDisposed();
        if (SourceInfo is null) throw new InvalidOperationException("No playback source is loaded.");
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _latestOperation?.Cancel();
        _backend.FramePresented -= Backend_FramePresented;
        _backend.Failed -= Backend_Failed;
        await _backend.DisposeAsync().ConfigureAwait(false);
        _latestOperation?.Dispose();
        _operations.Dispose();
        Publish(new(MediaPlaybackState.Disposed, null, null, null));
    }
}
