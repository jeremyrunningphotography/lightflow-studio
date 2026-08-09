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

internal sealed record MediaPlaybackSourceInfo(
    string SourcePath,
    TimeSpan Duration,
    TimeSpan StartTimestamp,
    int Width,
    int Height,
    IReadOnlyList<MediaAudioStreamInfo> AudioStreams,
    int? SelectedAudioStreamIndex,
    bool UsesHardwareDecode);

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

internal interface IMediaPlaybackService : IAsyncDisposable
{
    MediaPlaybackSnapshot Snapshot { get; }
    MediaPlaybackSourceInfo? SourceInfo { get; }
    event EventHandler<MediaPlaybackSnapshot>? StateChanged;
    event EventHandler<MediaPresentationTimestamp>? FramePresented;

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
    FrameworkElement CreatePresentationSurface();
    void CancelPending();
    Task<PlaybackBackendOpened> OpenAsync(string sourcePath, CancellationToken token);
    Task CloseAsync(CancellationToken token);
    Task PlayAsync(CancellationToken token);
    Task PauseAsync(CancellationToken token);
    Task<MediaPresentationTimestamp> SeekAsync(TimeSpan position, CancellationToken token);
    Task<MediaPresentationTimestamp> StepForwardAsync(CancellationToken token);
    Task<MediaPresentationTimestamp> StepBackwardAsync(CancellationToken token);
    Task<MediaDecodedFrame> GetFrameAsync(TimeSpan position, CancellationToken token);
}
