using System.IO;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
// UseWindowsForms is enabled project-wide (for OpenFileDialog elsewhere), which brings a global
// System.Windows.Forms using into scope alongside System.Windows.Controls/System.Windows.Input — both
// declare UserControl/KeyEventArgs, so this file resolves them explicitly rather than ambiguously,
// mirroring MainWindow.xaml.cs's existing fully-qualified System.Windows.Input.KeyEventArgs convention.
using UserControl = System.Windows.Controls.UserControl;

namespace LightflowStudio;

/// <summary>
/// Lightflow's integrated Player/Viewer presentation content (#110). Deliberately independent of
/// <c>MainWindow</c> and Browser chrome: it takes only a <see cref="MediaPlaybackCoordinator"/> (the same
/// global playback engine every other consumer reuses) and a host-agnostic <see cref="PlayerViewerAsset"/> —
/// nothing Browser-specific — so a future floating/new-window host (#112) can embed this exact control rather
/// than reimplementing playback wiring. Video plays through the shared #53 engine via
/// <see cref="MediaPlaybackLeaseSession"/>/<see cref="MediaPlaybackView"/> — the same lease-wrapper
/// <c>TrimEditorPlayback</c> derives from for trim-boundary seeking, used here directly since Browser review
/// has no In/Out concept to add; still images decode directly through WIC, reusing
/// <see cref="WicImageThumbnailRenderer"/>'s existing EXIF-orientation handling rather than a second image path.
/// </summary>
public partial class PlayerViewerHost : UserControl
{
    private readonly MediaPlaybackCoordinator _coordinator;
    private long _generation;
    private MediaPlaybackLeaseSession? _playback;
    private IMediaPlaybackService? _service;
    private MediaPlaybackView? _mediaView;
    private PlayerViewerAsset? _currentAsset;
    private bool _updatingPosition;
    private bool _updatingVolume;
    private readonly FrameStepQueue _frameStepQueue = new();

    internal PlayerViewerHost(MediaPlaybackCoordinator coordinator)
    {
        _coordinator = coordinator;
        InitializeComponent();
        // See FreezeOverlay's own XAML comment and FrameStepQueue.BeforeBackwardStepAsync's doc comment: VFR
        // backward reconstruction visibly moves the live render surface through however many intermediate
        // frames it takes to relocate the predecessor, since it runs on the same Player the surface renders.
        // Freezing on the frame in place before it starts (awaited — genuinely completes before reconstruction
        // can move anything) and un-freezing only once the whole queued burst has settled keeps that invisible.
        _frameStepQueue.BeforeBackwardStepAsync = FreezeCurrentFrameAsync;
        _frameStepQueue.DrainCompleted += UnfreezeCurrentFrame;
    }

    /// <summary>Raised by the Back button or Esc. The host decides what "back" means (for the Browser, returning to Grid presentation at its preserved context).</summary>
    public event EventHandler? BackRequested;

    internal PlayerViewerAsset? CurrentAsset => _currentAsset;

    /// <summary>
    /// Opens one asset, releasing whatever was previously open first. Safe to call repeatedly for rapid
    /// Browser ↔ Player transitions or fast successive opens: a generation token guards every awaited step so
    /// only the most recently requested open can ever publish UI state, matching the same latest-request-wins
    /// discipline the underlying playback engine already applies to its own operations.
    /// </summary>
    internal async Task OpenAsync(PlayerViewerAsset asset, MediaPathResolution resolution, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(asset);
        var generation = ++_generation;
        try { await ReleaseCurrentAsync().ConfigureAwait(true); }
        catch (Exception exception)
        {
            // Best-effort teardown of whatever was previously open; a failure releasing the OLD asset must
            // not prevent attempting to open the NEW one, and must not fault this call unobserved (MainWindow
            // invokes it fire-and-forget from the tile double-click/Enter handler) — unfiltered, matching
            // MainWindow.RunBrowserNavigationAsync's own catch-all convention for a fire-and-forget UI entry
            // point, since an unanticipated exception type here must still surface as a status message rather
            // than propagate as a silent unobserved task fault.
            SetStatus(exception.Message);
        }
        if (generation != _generation) return;

        _currentAsset = asset;
        AssetNameText.Text = asset.Name;
        SetStatus("Loading…");

        if (resolution.PhysicalPath is null || !resolution.Exists)
        {
            SetStatus(resolution.Diagnostic ?? "This file is unavailable.");
            Focus();
            return;
        }

        try
        {
            if (asset.Kind == MediaPresentationKind.Video)
                await OpenVideoAsync(resolution.PhysicalPath, generation, token).ConfigureAwait(true);
            else
                await OpenImageAsync(resolution.PhysicalPath, generation, token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            // Unfiltered: a genuinely unanticipated exception (e.g. a native/Direct3D presentation-surface
            // failure inside OpenVideoAsync, not just the anticipated IOException/InvalidOperationException/
            // etc. cases) must still surface as a status message here rather than propagate unobserved out of
            // this fire-and-forget call — OpenVideoAsync/OpenImageAsync already guarantee the lease/state
            // they touched is released before any exception reaches this catch.
            if (generation != _generation) return;
            SetStatus(exception.Message);
        }
        finally
        {
            if (generation == _generation) Focus();
        }
    }

    /// <summary>
    /// Releases playback and clears presentation. Safe to call whether or not anything is currently open.
    /// Generation-guarded exactly like <see cref="OpenAsync"/>: <see cref="ReleaseCurrentAsync"/>'s actual
    /// resource teardown always runs unconditionally, but a stale/superseded Close's UI-state clearing (asset
    /// name, status) is skipped if a newer Open has already published a different asset's state by the time
    /// this Close's own await settles — otherwise a slow-to-tear-down Close from a lease-theft recovery
    /// (<see cref="HandleStateChanged"/>) could clobber a newly-opened asset's header/status after the fact.
    /// </summary>
    internal async Task CloseAsync()
    {
        var generation = ++_generation;
        await ReleaseCurrentAsync().ConfigureAwait(true);
        if (generation != _generation) return;
        _currentAsset = null;
        AssetNameText.Text = "";
        SetStatus(null);
    }

    private async Task OpenVideoAsync(string absolutePath, long generation, CancellationToken token)
    {
        var playback = new MediaPlaybackLeaseSession(_coordinator);
        var service = await playback.OpenAsync(absolutePath, token).ConfigureAwait(true);
        if (generation != _generation) { await playback.DisposeAsync().ConfigureAwait(true); return; }

        _playback = playback;
        _service = service;
        service.StateChanged += Playback_StateChanged;
        try
        {
            if (service.SourceInfo is not { } info || info.Duration <= TimeSpan.Zero || service.Snapshot.State == MediaPlaybackState.Failed)
                throw new InvalidOperationException(service.Snapshot.Error?.Message ?? "The video could not be decoded for preview.");

            _mediaView = new MediaPlaybackView(service);
            VideoHost.Children.Add(_mediaView);
            PositionSlider.Maximum = info.Duration.TotalMilliseconds;
            DurationText.Text = FormatTimestamp(info.Duration);
            UpdateFromSnapshot(service.Snapshot);
            SetTransportEnabled(true);
            SetAudioControlsEnabled(info.AudioStreams.Count > 0);
            UpdateAudioControlsFromService();
            TransportBar.Visibility = Visibility.Visible;
            SetStatus(null);
        }
        catch
        {
            // Any failure after acquiring the lease — an invalid/failed source, or a presentation-surface
            // construction failure inside `new MediaPlaybackView(service)` — must release it before
            // propagating, whatever the exception type. Otherwise _service stays assigned to a session
            // nothing further can use, and Space (guarded only on "_service is not null", unlike StepAsync
            // which also checks PositionSlider.IsEnabled) would still reach it despite every transport
            // control being visibly disabled.
            service.StateChanged -= Playback_StateChanged;
            _service = null;
            _playback = null;
            await playback.DisposeAsync().ConfigureAwait(true);
            throw;
        }
    }

    private async Task OpenImageAsync(string absolutePath, long generation, CancellationToken token)
    {
        var bitmap = await Task.Run(() => DecodeImage(absolutePath), token).ConfigureAwait(true);
        if (generation != _generation) return;
        ImageSurface.Source = bitmap;
        ImageSurface.Visibility = Visibility.Visible;
        SetStatus(null);
    }

    private static BitmapSource DecodeImage(string absolutePath)
    {
        using var stream = new FileStream(absolutePath, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, 64 * 1024, FileOptions.SequentialScan);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        if (decoder.Frames.Count == 0) throw new InvalidOperationException("The image contains no decodable frames.");
        var frame = decoder.Frames[0];
        var orientation = WicImageThumbnailRenderer.ReadOrientation(frame.Metadata as BitmapMetadata);
        BitmapSource bitmap = WicImageThumbnailRenderer.ApplyOrientation(frame, orientation);
        bitmap.Freeze();
        return bitmap;
    }

    private async Task ReleaseCurrentAsync()
    {
        // Invalidates the frame-step backlog before releasing _service — no further queued steps are applied
        // or reported to a service that may now hold a different source or none at all. This does not itself
        // wait for a step already genuinely in flight; that one native decode keeps running regardless (see
        // FrameStepQueue's own doc comment — there is no way to abort it), and MediaPlaybackService's existing
        // cancel-on-close/generation handling governs what happens when the close below reaches it.
        _frameStepQueue.Reset();
        var mediaView = _mediaView;
        _mediaView = null;
        VideoHost.Children.Clear();
        if (_service is not null) _service.StateChanged -= Playback_StateChanged;
        _service = null;
        var playback = _playback;
        _playback = null;
        mediaView?.Dispose();
        if (playback is not null) await playback.DisposeAsync().ConfigureAwait(true);

        ImageSurface.Source = null;
        ImageSurface.Visibility = Visibility.Collapsed;
        TransportBar.Visibility = Visibility.Collapsed;
        SetTransportEnabled(false);
        SetAudioControlsEnabled(false);
        UnfreezeCurrentFrame();
    }

    private void SetStatus(string? message)
    {
        StatusText.Text = message ?? "";
        StatusText.Visibility = string.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void SetTransportEnabled(bool enabled)
    {
        PositionSlider.IsEnabled = enabled;
        PreviousFrameButton.IsEnabled = enabled;
        NextFrameButton.IsEnabled = enabled;
        PlayPauseButton.IsEnabled = enabled;
    }

    /// <summary>
    /// Volume/mute are gated separately from the rest of the transport: a video-only source (no audio stream)
    /// keeps Play/seek/frame-step usable while these stay disabled rather than controlling nothing meaningfully.
    /// </summary>
    private void SetAudioControlsEnabled(bool enabled)
    {
        MuteButton.IsEnabled = enabled;
        VolumeSlider.IsEnabled = enabled;
    }

    /// <summary>Reflects the shared playback session's current volume/mute — called once per open, since the
    /// session (and therefore its volume/mute) persists across whichever asset is currently showing.</summary>
    private void UpdateAudioControlsFromService()
    {
        if (_service is null) return;
        _updatingVolume = true;
        VolumeSlider.Value = _service.Volume;
        _updatingVolume = false;
        UpdateMuteIcon(_service.Mute);
    }

    private void UpdateMuteIcon(bool muted)
    {
        MuteIcon.Text = muted ? "" : "";
        MuteButton.ToolTip = muted ? "Unmute" : "Mute";
    }

    private void Playback_StateChanged(object? sender, MediaPlaybackSnapshot snapshot) =>
        Dispatcher.BeginInvoke(() => HandleStateChanged(snapshot));

    /// <summary>
    /// <see cref="MediaPlaybackCoordinator"/> allows only one active session at a time: a different consumer
    /// acquiring the lease (e.g. opening <c>TrimEditorWindow</c> while this control's video is still open)
    /// forcibly closes this control's source out from under it. <see cref="ReleaseCurrentAsync"/> always
    /// unsubscribes this handler before <em>our own</em> close, so an <see cref="MediaPlaybackState.Empty"/>
    /// snapshot arriving here while <see cref="_service"/> is still assigned can only mean that external
    /// takeover, never our own intentional close — silently leaving the transport enabled over a source that
    /// no longer exists would be worse than returning to Grid, which is what actually happened.
    /// </summary>
    private void HandleStateChanged(MediaPlaybackSnapshot snapshot)
    {
        if (snapshot.State == MediaPlaybackState.Empty && _service is not null)
        {
            _ = CloseAsync();
            BackRequested?.Invoke(this, EventArgs.Empty);
            return;
        }
        UpdateFromSnapshot(snapshot);
    }

    private void UpdateFromSnapshot(MediaPlaybackSnapshot snapshot)
    {
        if (snapshot.DisplayedTimestamp is { } timestamp)
        {
            _updatingPosition = true;
            PositionSlider.Value = Math.Clamp(timestamp.Position.TotalMilliseconds, PositionSlider.Minimum, PositionSlider.Maximum);
            _updatingPosition = false;
            CurrentTimeText.Text = FormatTimestamp(timestamp.Position);
        }
        PlayPauseButton.Content = snapshot.State == MediaPlaybackState.Playing ? "Pause" : "Play";
        // A failure arriving mid-session (not just at open — see OpenVideoAsync's own upfront check) must
        // disable transport the same way: Play/seek/frame-step against an already-failed session would
        // otherwise still be reachable merely because the buttons were enabled before the failure happened.
        if (snapshot.State == MediaPlaybackState.Failed)
        {
            SetStatus(snapshot.Error?.Message ?? "Playback failed.");
            SetTransportEnabled(false);
        }
    }

    private async void PositionSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingPosition || _service is null || !PositionSlider.IsEnabled) return;
        try { await _service.SeekAsync(TimeSpan.FromMilliseconds(e.NewValue)); }
        catch (OperationCanceledException) { }
        catch (Exception exception) { SetStatus(exception.Message); }
    }

    private async void PositionSlider_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var target = IsInsideSliderThumb(e.OriginalSource as DependencyObject)
            ? TimelinePointerTarget.PlayheadThumb
            : TimelinePointerTarget.Track;
        if (!TimelineSeek.ShouldSeek(target) || _service is null || !PositionSlider.IsEnabled) return;
        e.Handled = true;
        var position = TimelineSeek.PositionFromCoordinate(
            e.GetPosition(PositionSlider).X, PositionSlider.ActualWidth, TimeSpan.FromMilliseconds(PositionSlider.Maximum));
        _updatingPosition = true;
        PositionSlider.Value = position.TotalMilliseconds;
        _updatingPosition = false;
        try { await _service.SeekAsync(position); }
        catch (OperationCanceledException) { }
        catch (Exception exception) { SetStatus(exception.Message); }
    }

    private static bool IsInsideSliderThumb(DependencyObject? element)
    {
        while (element is not null)
        {
            if (element is Thumb) return true;
            element = VisualTreeHelper.GetParent(element);
        }
        return false;
    }

    /// <summary>
    /// Called when the host stops being the visible content (e.g. the Browser tab loses focus to another
    /// workspace) without actually closing the Player/Viewer — switching tabs is not "leaving" the Browser
    /// per #110's context-preservation requirement, so this pauses rather than releasing the playback lease.
    /// A no-op for an image Viewer or when nothing is currently playing.
    /// </summary>
    internal async Task PauseIfPlayingAsync()
    {
        if (_service?.Snapshot.State != MediaPlaybackState.Playing) return;
        try { await _service.PauseAsync(); }
        catch (OperationCanceledException) { }
        catch (Exception exception) { SetStatus(exception.Message); }
    }

    private async void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        if (_service is null) return;
        try
        {
            if (_service.Snapshot.State == MediaPlaybackState.Playing) await _service.PauseAsync();
            else await _service.PlayAsync();
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) { SetStatus(exception.Message); }
    }

    private void PreviousFrame_Click(object sender, RoutedEventArgs e) => RequestStep(forward: false);
    private void NextFrame_Click(object sender, RoutedEventArgs e) => RequestStep(forward: true);

    private void MuteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_service is null) return;
        _service.Mute = !_service.Mute;
        UpdateMuteIcon(_service.Mute);
    }

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingVolume || _service is null) return;
        _service.Volume = (int)e.NewValue;
    }

    /// <summary>
    /// Mirrors <see cref="PositionSlider_PreviewMouseLeftButtonDown"/>'s own click-to-set behavior: the
    /// PlaybackTimelineSlider style's track RepeatButtons only nudge by Slider.DecreaseLarge/IncreaseLarge on a
    /// click (the ordinary WPF Slider default), not jump straight to the clicked position — expected desktop
    /// volume-slider behavior is the latter, so this intercepts a track click (never a thumb click, which must
    /// still start an ordinary drag) and sets Value directly from the click's X position.
    /// </summary>
    private void VolumeSlider_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (IsInsideSliderThumb(e.OriginalSource as DependencyObject) || _service is null || !VolumeSlider.IsEnabled) return;
        e.Handled = true;
        VolumeSlider.Value = SliderClickToSet.ValueFromCoordinate(
            e.GetPosition(VolumeSlider).X, VolumeSlider.ActualWidth, VolumeSlider.Minimum, VolumeSlider.Maximum);
    }

    /// <summary>
    /// Queues one frame step through <see cref="_frameStepQueue"/> rather than calling
    /// <see cref="PlaybackFrameStep"/> directly — see <see cref="FrameStepQueue"/>'s own doc comment for why
    /// rapid repeated requests must never reach the engine concurrently/overlapping. Returns immediately; the
    /// queue's own drain loop applies steps one at a time, so this stays safe and responsive no matter how
    /// fast the button is clicked or the keyboard shortcut repeated.
    /// </summary>
    private void RequestStep(bool forward)
    {
        if (_service is null || !PositionSlider.IsEnabled) return;
        _frameStepQueue.RequestStep(_service, forward, exception => SetStatus(exception.Message));
    }

    /// <summary>
    /// Captures whatever the live surface currently shows and covers it with that still image — see
    /// FreezeOverlay's own XAML comment for why an Image overlay, not a Flyleaf-level suppression, is the
    /// mechanism used. Idempotent: a second backward step queued while the first is still reconstructing must
    /// not re-capture (that would freeze on an already-intermediate position instead of the frame the user
    /// actually saw before the burst began), so this only captures when the overlay isn't already showing one.
    /// A capture failure is swallowed rather than surfaced as a status message — see FrameStepQueue's own doc
    /// comment: losing the visual freeze for one click is a cosmetic regression, not a reason to block the
    /// step itself, which FrameStepQueue guarantees still runs regardless of this outcome.
    /// </summary>
    private async Task FreezeCurrentFrameAsync()
    {
        if (_service is null || FreezeOverlay.Visibility == Visibility.Visible) return;
        try
        {
            var frame = await _service.SnapshotCurrentFrameAsync().ConfigureAwait(true);
            FreezeOverlay.Source = ToBitmapSource(frame);
            FreezeOverlay.Visibility = Visibility.Visible;
        }
        catch (OperationCanceledException) { }
    }

    private void UnfreezeCurrentFrame()
    {
        if (FreezeOverlay.Visibility == Visibility.Collapsed) return;
        FreezeOverlay.Visibility = Visibility.Collapsed;
        FreezeOverlay.Source = null;
    }

    private static BitmapSource ToBitmapSource(MediaDecodedFrame frame)
    {
        var bitmap = BitmapSource.Create(frame.Width, frame.Height, 96, 96, PixelFormats.Bgra32, null, frame.BgraPixels, frame.Stride);
        bitmap.Freeze();
        return bitmap;
    }

    private void BackButton_Click(object sender, RoutedEventArgs e) => BackRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Owns its own keyboard shortcuts rather than relying on the host window, so any future host (#112's
    /// floating window included) gets identical behavior for free. Frame-step uses <c>,</c>/<c>.</c>, not
    /// Left/Right — #111 reserves the arrow keys for filmstrip asset-to-asset navigation, and this control
    /// must not stake a conflicting claim on them ahead of that work.
    /// </summary>
    private void PlayerViewerHost_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                e.Handled = true;
                BackRequested?.Invoke(this, EventArgs.Empty);
                return;
            case Key.Space when _service is not null && PositionSlider.IsEnabled:
                e.Handled = true;
                PlayPause_Click(this, new RoutedEventArgs());
                return;
            case Key.OemComma when _service is not null && PositionSlider.IsEnabled:
                e.Handled = true;
                RequestStep(forward: false);
                return;
            case Key.OemPeriod when _service is not null && PositionSlider.IsEnabled:
                e.Handled = true;
                RequestStep(forward: true);
                return;
        }
    }

    private static string FormatTimestamp(TimeSpan value)
    {
        var hours = (int)value.TotalHours;
        return $"{hours:00}:{value.Minutes:00}:{value.Seconds:00}.{value.Milliseconds:000}";
    }
}
