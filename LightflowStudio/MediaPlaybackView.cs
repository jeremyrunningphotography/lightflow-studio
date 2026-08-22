using System.Windows.Controls;

namespace LightflowStudio;

internal sealed class MediaPlaybackView : ContentControl, IDisposable
{
    private MediaPlaybackPresentation? _presentation;

    public MediaPlaybackView(IMediaPlaybackService playback)
    {
        ArgumentNullException.ThrowIfNull(playback);
        _presentation = playback.CreatePresentation();
        Content = _presentation.Surface;
        Focusable = true;
    }

    public Task<MediaDecodedFrame> CaptureFrameAsync(CancellationToken token = default) =>
        (_presentation ?? throw new ObjectDisposedException(nameof(MediaPlaybackView))).CaptureFrameAsync(token);

    public void Dispose()
    {
        Content = null;
        Interlocked.Exchange(ref _presentation, null)?.Dispose();
    }
}
