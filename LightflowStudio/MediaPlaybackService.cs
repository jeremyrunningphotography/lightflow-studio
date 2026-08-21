namespace LightflowStudio;

using System.IO;

internal sealed class MediaPlaybackService : IMediaPlaybackService
{
    private readonly IMediaPlaybackBackend _backend;
    private readonly SemaphoreSlim _operations = new(1, 1);
    private readonly object _lifecycle = new();
    private CancellationTokenSource _sourceLifetime = new();
    private CancellationTokenSource? _latestOperation;
    private long _sourceGeneration;
    private long _operationGeneration;
    private long _activeBackendOperation;
    private int _suppressActiveBackendFrames;
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

    public int Volume { get => _backend.Volume; set => _backend.Volume = value; }
    public bool Mute { get => _backend.Mute; set => _backend.Mute = value; }

    public MediaPlaybackPresentation CreatePresentation() =>
        new(_backend.CreatePresentationSurface(), _backend.ReleasePresentationSurface);

    public Task OpenAsync(string sourcePath, CancellationToken token = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        var operation = BeginSourceTransition(token);
        var fullPath = Path.GetFullPath(sourcePath);
        Publish(new(MediaPlaybackState.Loading, fullPath, null, null));
        return RunOpenAsync(fullPath, operation);
    }

    public Task SeekAsync(TimeSpan position, CancellationToken token = default)
    {
        EnsureLoaded();
        var resume = Snapshot.State == MediaPlaybackState.Playing;
        var operation = BeginSourceOperation(token);
        Publish(Snapshot with { State = MediaPlaybackState.Seeking, Error = null });
        return RunTimestampOperationAsync(
            operation,
            backendToken => _backend.SeekAsync(position, backendToken),
            timestamp => Snapshot with
            {
                State = resume ? MediaPlaybackState.Playing : MediaPlaybackState.Paused,
                DisplayedTimestamp = timestamp,
                Error = null
            },
            resume);
    }

    public async Task CloseAsync(CancellationToken token = default)
    {
        var operation = BeginSourceTransition(token);
        Publish(new(MediaPlaybackState.Empty, null, null, null));
        await RunSerializedAsync(operation, _backend.CloseAsync).ConfigureAwait(false);
    }

    public Task PlayAsync(CancellationToken token = default) => RunStateOperationAsync(
        MediaPlaybackState.Playing, _backend.PlayAsync, token);

    public Task PauseAsync(CancellationToken token = default) => RunStateOperationAsync(
        MediaPlaybackState.Paused, _backend.PauseAsync, token);

    public Task StepForwardAsync(CancellationToken token = default) => RunStepAsync(_backend.StepForwardAsync, token);

    public Task StepBackwardAsync(CancellationToken token = default) => RunStepAsync(_backend.StepBackwardAsync, token);

    public async Task<MediaDecodedFrame> GetFrameAsync(TimeSpan position, CancellationToken token = default)
    {
        EnsureLoaded();
        var operation = BeginSourceOperation(token);
        await _operations.WaitAsync(operation.Token).ConfigureAwait(false);
        try
        {
            EnsureCurrent(operation);
            ActivateBackendOperation(operation, suppressFrames: true);
            var frame = await _backend.GetFrameAsync(position, operation.Token).ConfigureAwait(false);
            EnsureCurrent(operation);
            return frame;
        }
        finally
        {
            Volatile.Write(ref _suppressActiveBackendFrames, 0);
            _operations.Release();
        }
    }

    private async Task RunOpenAsync(string sourcePath, PlaybackOperation operation)
    {
        await _operations.WaitAsync(operation.Token).ConfigureAwait(false);
        try
        {
            EnsureCurrent(operation);
            ActivateBackendOperation(operation);
            await _backend.CloseAsync(CancellationToken.None).ConfigureAwait(false);
            EnsureCurrent(operation);
            var opened = await _backend.OpenAsync(sourcePath, operation.Token).ConfigureAwait(false);
            EnsureCurrent(operation);
            SourceInfo = opened.Source;
            Publish(new(MediaPlaybackState.Paused, sourcePath, opened.FirstFrame, opened.Source.Duration));
        }
        catch (OperationCanceledException) when (!IsCurrent(operation) || operation.Token.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (IsCurrent(operation)) PublishFailure(sourcePath, exception);
        }
        finally { _operations.Release(); }
    }

    private async Task RunStateOperationAsync(MediaPlaybackState state, Func<CancellationToken, Task> action, CancellationToken token)
    {
        EnsureLoaded();
        var operation = BeginSourceOperation(token);
        await RunSerializedAsync(operation, action).ConfigureAwait(false);
        if (IsCurrent(operation)) Publish(Snapshot with { State = state, Error = null });
    }

    private Task RunStepAsync(Func<CancellationToken, Task<MediaPresentationTimestamp>> action, CancellationToken token)
    {
        EnsureLoaded();
        var operation = BeginSourceOperation(token);
        return RunTimestampOperationAsync(
            operation,
            async backendToken =>
            {
                await _backend.PauseAsync(backendToken).ConfigureAwait(false);
                return await action(backendToken).ConfigureAwait(false);
            },
            timestamp => Snapshot with
            {
                State = MediaPlaybackState.Paused,
                DisplayedTimestamp = timestamp,
                Error = null
            });
    }

    private async Task RunTimestampOperationAsync(
        PlaybackOperation operation,
        Func<CancellationToken, Task<MediaPresentationTimestamp>> action,
        Func<MediaPresentationTimestamp, MediaPlaybackSnapshot> completedSnapshot,
        bool resumePlayback = false)
    {
        await _operations.WaitAsync(operation.Token).ConfigureAwait(false);
        try
        {
            EnsureCurrent(operation);
            ActivateBackendOperation(operation);
            var timestamp = await action(operation.Token).ConfigureAwait(false);
            EnsureCurrent(operation);
            if (resumePlayback)
            {
                await _backend.PlayAsync(operation.Token).ConfigureAwait(false);
                EnsureCurrent(operation);
            }
            Publish(completedSnapshot(timestamp));
        }
        catch (OperationCanceledException) when (!IsCurrent(operation) || operation.Token.IsCancellationRequested) { }
        finally { _operations.Release(); }
    }

    private async Task RunSerializedAsync(PlaybackOperation operation, Func<CancellationToken, Task> action)
    {
        await _operations.WaitAsync(operation.Token).ConfigureAwait(false);
        try
        {
            EnsureCurrent(operation);
            ActivateBackendOperation(operation);
            await action(operation.Token).ConfigureAwait(false);
            EnsureCurrent(operation);
        }
        catch (OperationCanceledException) when (!IsCurrent(operation) || operation.Token.IsCancellationRequested) { }
        finally { _operations.Release(); }
    }

    private PlaybackOperation BeginSourceTransition(CancellationToken callerToken)
    {
        lock (_lifecycle)
        {
            ThrowIfDisposed();
            var oldSource = _sourceLifetime;
            var oldOperation = _latestOperation;
            _sourceLifetime = new CancellationTokenSource();
            SourceInfo = null;
            oldOperation?.Cancel();
            oldSource.Cancel();
            _backend.CancelPending();
            var sourceGeneration = ++_sourceGeneration;
            var operationGeneration = ++_operationGeneration;
            var linked = CancellationTokenSource.CreateLinkedTokenSource(callerToken, _sourceLifetime.Token);
            _latestOperation = linked;
            oldOperation?.Dispose();
            oldSource.Dispose();
            return new(sourceGeneration, operationGeneration, linked.Token);
        }
    }

    private PlaybackOperation BeginSourceOperation(CancellationToken callerToken)
    {
        lock (_lifecycle)
        {
            ThrowIfDisposed();
            if (SourceInfo is null) throw new InvalidOperationException("No playback source is loaded.");
            var oldOperation = _latestOperation;
            var linked = CancellationTokenSource.CreateLinkedTokenSource(callerToken, _sourceLifetime.Token);
            _latestOperation = linked;
            oldOperation?.Cancel();
            _backend.CancelPending();
            var operation = new PlaybackOperation(_sourceGeneration, ++_operationGeneration, linked.Token);
            oldOperation?.Dispose();
            return operation;
        }
    }

    private bool IsCurrent(PlaybackOperation operation) =>
        operation.SourceGeneration == Interlocked.Read(ref _sourceGeneration) &&
        operation.OperationGeneration == Interlocked.Read(ref _operationGeneration) &&
        !operation.Token.IsCancellationRequested;

    private void EnsureCurrent(PlaybackOperation operation)
    {
        if (!IsCurrent(operation)) throw new OperationCanceledException(operation.Token);
    }

    private void ActivateBackendOperation(PlaybackOperation operation, bool suppressFrames = false)
    {
        Volatile.Write(ref _activeBackendOperation, operation.OperationGeneration);
        Volatile.Write(ref _suppressActiveBackendFrames, suppressFrames ? 1 : 0);
    }

    private void Backend_FramePresented(object? sender, MediaPresentationTimestamp timestamp)
    {
        if (_disposed ||
            Snapshot.State is MediaPlaybackState.Empty or MediaPlaybackState.Loading ||
            Volatile.Read(ref _suppressActiveBackendFrames) != 0 ||
            Volatile.Read(ref _activeBackendOperation) != Interlocked.Read(ref _operationGeneration)) return;
        Publish(Snapshot with { DisplayedTimestamp = timestamp });
        FramePresented?.Invoke(this, timestamp);
    }

    private void Backend_Failed(object? sender, MediaPlaybackError error)
    {
        if (!_disposed && Snapshot.State is not (MediaPlaybackState.Empty or MediaPlaybackState.Loading))
            Publish(Snapshot with { State = MediaPlaybackState.Failed, Error = error });
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
        lock (_lifecycle)
        {
            _latestOperation?.Cancel();
            _sourceLifetime.Cancel();
            _backend.CancelPending();
        }
        _backend.FramePresented -= Backend_FramePresented;
        _backend.Failed -= Backend_Failed;
        await _backend.DisposeAsync().ConfigureAwait(false);
        _latestOperation?.Dispose();
        _sourceLifetime.Dispose();
        _operations.Dispose();
        Publish(new(MediaPlaybackState.Disposed, null, null, null));
    }

    private sealed record PlaybackOperation(
        long SourceGeneration,
        long OperationGeneration,
        CancellationToken Token);
}
