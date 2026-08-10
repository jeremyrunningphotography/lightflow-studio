using System.Windows.Controls;

namespace LightflowStudio;

internal sealed class MediaPlaybackView : ContentControl
{
    public MediaPlaybackView(IMediaPlaybackService playback)
    {
        ArgumentNullException.ThrowIfNull(playback);
        Content = playback.CreatePresentationSurface();
        Focusable = true;
    }
}
