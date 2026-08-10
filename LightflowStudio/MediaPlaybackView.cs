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

    public void Dispose()
    {
        Content = null;
        Interlocked.Exchange(ref _presentation, null)?.Dispose();
    }
}
