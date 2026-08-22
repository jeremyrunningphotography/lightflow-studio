using System.Windows;

namespace LightflowStudio;

internal enum MediaPlaybackState
{
    Empty,
    Loading,
    Paused,
    Playing,
    Seeking,
    Ended,
    Failed,
    Disposed
}

internal enum MediaPlaybackErrorKind
{
    SourceUnavailable,
    InvalidOrCorruptMedia,
    VideoDecodeUnavailable,
    AudioUnavailable,
    DecoderInitializationFailed,
    OperationFailed
}

internal sealed record MediaPresentationTimestamp(TimeSpan Position, bool IsDecodedPresentationTimestamp = true);

internal sealed record MediaPlaybackError(MediaPlaybackErrorKind Kind, string Message, string? Diagnostic = null);

internal sealed record MediaAudioStreamInfo(int Index, string? Language, string? Title, int Channels, bool IsDefault);

internal sealed record MediaPlaybackOpenMetrics(
    TimeSpan SourceOpen,
    TimeSpan FirstFrameSettle,
    TimeSpan Total);

internal sealed record MediaPlaybackSourceInfo(
    string SourcePath,
    TimeSpan Duration,
    TimeSpan StartTimestamp,
    int Width,
    int Height,
    IReadOnlyList<MediaAudioStreamInfo> AudioStreams,
    int? SelectedAudioStreamIndex,
    bool UsesHardwareDecode)
{
    public MediaPlaybackOpenMetrics? OpenMetrics { get; init; }
}

internal sealed record MediaPlaybackSnapshot(
    MediaPlaybackState State,
    string? SourcePath,
    MediaPresentationTimestamp? DisplayedTimestamp,
    TimeSpan? Duration,
    MediaPlaybackError? Error = null);

internal sealed record MediaDecodedFrame(
    MediaPresentationTimestamp Timestamp,
    int Width,
    int Height,
    int Stride,
    byte[] BgraPixels);

internal sealed class MediaPlaybackPresentation : IDisposable
{
    private readonly Action<FrameworkElement> _release;
    private readonly Func<CancellationToken, Task<MediaDecodedFrame>> _captureFrame;
    private FrameworkElement? _surface;

    public MediaPlaybackPresentation(
        FrameworkElement surface,
        Action<FrameworkElement> release,
        Func<CancellationToken, Task<MediaDecodedFrame>> captureFrame)
    {
        _surface = surface;
        _release = release;
        _captureFrame = captureFrame;
    }

    public FrameworkElement Surface => _surface ?? throw new ObjectDisposedException(nameof(MediaPlaybackPresentation));

    public Task<MediaDecodedFrame> CaptureFrameAsync(CancellationToken token = default)
    {
        ObjectDisposedException.ThrowIf(_surface is null, this);
        return _captureFrame(token);
    }

    public void Dispose()
    {
        var surface = Interlocked.Exchange(ref _surface, null);
        if (surface is not null) _release(surface);
    }
}

internal interface IMediaPlaybackService : IAsyncDisposable
{
    MediaPlaybackSnapshot Snapshot { get; }
    MediaPlaybackSourceInfo? SourceInfo { get; }
    event EventHandler<MediaPlaybackSnapshot>? StateChanged;
    event EventHandler<MediaPresentationTimestamp>? FramePresented;

    /// <summary>0-100. Persists across sources opened through the same service (matches how a physical volume
    /// control behaves) rather than resetting on every <see cref="OpenAsync"/>.</summary>
    int Volume { get; set; }
    bool Mute { get; set; }

    MediaPlaybackPresentation CreatePresentation();
    Task OpenAsync(string sourcePath, CancellationToken token = default);
    Task CloseAsync(CancellationToken token = default);
    Task PlayAsync(CancellationToken token = default);
    Task PauseAsync(CancellationToken token = default);
    Task SeekAsync(TimeSpan position, CancellationToken token = default);
    Task StepForwardAsync(CancellationToken token = default);
    Task StepBackwardAsync(CancellationToken token = default);
    Task<MediaDecodedFrame> GetFrameAsync(TimeSpan position, CancellationToken token = default);

}

internal sealed record PlaybackBackendOpened(MediaPlaybackSourceInfo Source, MediaPresentationTimestamp FirstFrame);

internal interface IMediaPlaybackBackend : IAsyncDisposable
{
    event EventHandler<MediaPresentationTimestamp>? FramePresented;
    event EventHandler<MediaPlaybackError>? Failed;

    /// <summary>0-100. Readable/settable even with no source open (applied to the next-opened source), so a
    /// user's volume choice survives switching between assets.</summary>
    int Volume { get; set; }
    bool Mute { get; set; }

    FrameworkElement CreatePresentationSurface();
    void ReleasePresentationSurface(FrameworkElement surface);
    void CancelPending();
    Task<PlaybackBackendOpened> OpenAsync(string sourcePath, CancellationToken token);
    Task CloseAsync(CancellationToken token);
    Task PlayAsync(CancellationToken token);
    Task PauseAsync(CancellationToken token);
    Task<MediaPresentationTimestamp> SeekAsync(TimeSpan position, CancellationToken token);
    Task<MediaPresentationTimestamp> StepForwardAsync(CancellationToken token);
    Task<MediaPresentationTimestamp> StepBackwardAsync(CancellationToken token);
    Task<MediaDecodedFrame> GetFrameAsync(TimeSpan position, CancellationToken token);
    Task<MediaDecodedFrame> CapturePresentedFrameAsync(CancellationToken token);
}
