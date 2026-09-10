using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FlyleafLib;
using FlyleafLib.Controls.WPF;
using FlyleafLib.MediaFramework.MediaRenderer;
using FlyleafLib.MediaPlayer;

namespace LightflowStudio;

internal sealed class FlyleafPlaybackBackend : IMediaPlaybackBackend
{
    private static readonly object EngineLock = new();
    private static bool _engineStarted;
    private readonly string _ffmpegPath;
    private readonly FfmpegAudioPlayback _audio;
    private readonly Dispatcher _dispatcher;
    private readonly IVideoPostProcessorFactory? _postProcessorFactory;
    private readonly LightflowColorPostProcessorFactory? _colorPostProcessorFactory;
    private readonly VideoProcessors? _videoProcessor;
    private Window? _offscreenWindow;
    private FlyleafHost? _host;
    private CancellationTokenSource _pending = new();
    private Player? _player;
    private bool _disposed;
    private int _desiredVolume = 100;
    private bool _desiredMute;
    private string? _sourcePath;
    private int? _audioStreamIndex;
    private int _suppressPresentationEvents;
    private PlaybackReviewOptions _reviewOptions = new();
    public event EventHandler? Ended;

    public FlyleafPlaybackBackend(
        string? ffmpegPath = null,
        Func<IPlaybackAudioOutput>? createAudioOutput = null,
        IVideoPostProcessorFactory? postProcessorFactory = null,
        VideoProcessors? videoProcessor = null)
    {
        _ffmpegPath = ffmpegPath ?? PlaybackDependencyLocator.FindSharedLibraries()
            ?? throw new DirectoryNotFoundException("Bundled playback libraries were not found.");
        _audio = new FfmpegAudioPlayback(_ffmpegPath, createAudioOutput);
        _dispatcher = Dispatcher.CurrentDispatcher;
        _colorPostProcessorFactory = postProcessorFactory is null ? new LightflowColorPostProcessorFactory() : null;
        _postProcessorFactory = postProcessorFactory ?? _colorPostProcessorFactory;
        _videoProcessor = videoProcessor;
        StartEngine(_ffmpegPath, _dispatcher);
    }

    public event EventHandler<MediaPresentationTimestamp>? FramePresented;
    public event EventHandler<MediaPlaybackError>? Failed;

    /// <summary>
    /// Backed by <see cref="_desiredVolume"/>/<see cref="_desiredMute"/> rather than reading straight through to
    /// <c>_player.Audio</c> so a volume/mute choice survives across <see cref="OpenAsync"/> calls (a fresh
    /// <see cref="Player"/> is created per source — see <see cref="CreatePlayer"/> — and would otherwise reset
    /// to its own default volume every time a different asset is opened). Reapplied to the live player, when one
    /// exists, immediately on every set and again right after each <see cref="OpenAsync"/> completes.
    /// </summary>
    public int Volume
    {
        get => _desiredVolume;
        set
        {
            _desiredVolume = Math.Clamp(value, 0, 100);
            _audio.Volume = _desiredVolume;
        }
    }

    public bool Mute
    {
        get => _desiredMute;
        set
        {
            _desiredMute = value;
            _audio.Mute = _desiredMute;
        }
    }

    public void SetColorPipeline(PlayerColorPipeline? pipeline, bool bypass)
    {
        _colorPostProcessorFactory?.SetPipeline(pipeline, bypass);
        if (_player is not null) RequestRender();
    }

    public FrameworkElement CreatePresentationSurface()
    {
        return RunOnUi(() =>
        {
            CloseOffscreenWindow();
            ReleaseHost(_host);
            _host = new FlyleafHost { Player = _player };
            return _host;
        });
    }

    public FrameworkElement GetInputSurface(FrameworkElement surface)
    {
        if (surface is not FlyleafHost host) return surface;
        // Only consumers that claim the native input seam replace Flyleaf's default bindings.
        // Trim and other presentation consumers keep their existing behavior.
        host.KeyBindings = AvailableWindows.None;
        host.MouseBindings = AvailableWindows.None;
        host.OpenOnDrop = AvailableWindows.None;
        host.ToggleFullScreenOnDoubleClick = AvailableWindows.None;
        return host.Surface ?? surface;
    }

    public void SetViewport(ViewerViewport viewport) => RunOnUi(() =>
    {
        if (_player is not { } player) return;
        player.Config.Video.Zoom = viewport.Zoom * 100;
        player.Config.Video.PanXOffset = viewport.PanX;
        player.Config.Video.PanYOffset = viewport.PanY;
        player.RequestRender();
    });

    public bool SetPresentationOverlay(FrameworkElement surface, FrameworkElement? content) => RunOnUi(() =>
    {
        if (surface is not FlyleafHost host) return false;
        // Flyleaf's content seam uses its existing transparent overlay HWND above the renderer.
        host.Content = content;
        // WPF layered child HWNDs can lose their compositor channel when their native
        // video parent is resized/reparented. This applies only to the tiny WPF overlay;
        // Flyleaf's D3D video renderer remains hardware accelerated.
        if (host.Overlay is { } overlay && PresentationSource.FromVisual(overlay) is System.Windows.Interop.HwndSource source)
            source.CompositionTarget.RenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
        return true;
    });

    public async Task SetReviewOptionsAsync(PlaybackReviewOptions options, CancellationToken token = default)
    {
        options.Validate();
        var player = RequirePlayer();
        var (playing, position) = RunOnUi(() => (player.IsPlaying, TimeSpan.FromTicks(player.CurTime)));
        if (options == _reviewOptions) return;
        var cadenceChanged = options.FrameDivisor != _reviewOptions.FrameDivisor || options.TargetRate != _reviewOptions.TargetRate;
        if (!cadenceChanged)
        {
            _reviewOptions = options;
            // Flyleaf already supports live speed changes; do not seek/pause the source for this.
            RunOnUi(() => { player.Speed = options.Speed; if (playing) ConfigureCadence(player, true); });
            if (playing && _sourcePath is { } path && _audioStreamIndex is { } index)
                await _audio.StartAsync(path, index, RunOnUi(() => TimeSpan.FromTicks(player.CurTime)), token, options.Speed).ConfigureAwait(false);
            return;
        }
        await StopPlaybackAsync(player, token).ConfigureAwait(false);
        _reviewOptions = options;
        RunOnUi(() => { player.Speed = options.Speed; ConfigureCadence(player, playing); });
        await SeekPlayerAsync(player, position, token).ConfigureAwait(false);
        if (playing) await PlayAsync(token).ConfigureAwait(false);
    }

    public void ReleasePresentationSurface(FrameworkElement surface)
    {
        if (surface is not FlyleafHost host) return;
        RunOnUi(() =>
        {
            if (!ReferenceEquals(host, _host)) return;
            CloseOffscreenWindow();
            ReleaseHost(host);
            _host = null;
        });
    }

    public void CancelPending()
    {
        var replacement = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _pending, replacement);
        previous.Cancel();
        previous.Dispose();
    }

    public async Task<PlaybackBackendOpened> OpenAsync(string sourcePath, CancellationToken token)
    {
        var totalTimer = Stopwatch.StartNew();
        ThrowIfDisposed();
        if (!File.Exists(sourcePath)) throw new FileNotFoundException("The playback source does not exist.", sourcePath);
        await _audio.StopAsync().ConfigureAwait(false);
        _sourcePath = null;
        _audioStreamIndex = null;
        await ClosePlayerAsync().ConfigureAwait(false);

        _reviewOptions = new();
        _cadencePrepared = false;
        var player = RunOnUi(CreatePlayer);
        _player = player;
        RunOnUi(() => { if (_host is not null) _host.Player = player; });
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _pending.Token);
        var sourceOpenTimer = Stopwatch.StartNew();
        var opened = await WaitForOpenAsync(player, sourcePath, linked.Token).ConfigureAwait(false);
        sourceOpenTimer.Stop();
        if (!opened.Success)
        {
            var error = new MediaPlaybackError(MediaPlaybackErrorKind.InvalidOrCorruptMedia,
                "The video could not be decoded for preview.", opened.Error);
            Failed?.Invoke(this, error);
            throw new InvalidDataException(opened.Error ?? error.Message);
        }

        var firstFrameTimer = Stopwatch.StartNew();
        var first = await SeekPlayerAsync(player, TimeSpan.Zero, linked.Token).ConfigureAwait(false);
        firstFrameTimer.Stop();
        var selectedAudio = player.Audio.Streams?.FirstOrDefault();
        var audioStreams = (player.Audio.Streams ?? [])
            .Select(stream => new MediaAudioStreamInfo(
                stream.StreamIndex,
                stream.Language?.OriginalInput,
                stream.Title,
                stream.Channels,
                stream.StreamIndex == selectedAudio?.StreamIndex))
            .ToList();
        _sourcePath = sourcePath;
        _audioStreamIndex = selectedAudio?.StreamIndex;
        var selectedVideo = player.Video.Streams?.FirstOrDefault(stream => stream.StreamIndex == player.Video.StreamIndex);
        totalTimer.Stop();
        var info = new MediaPlaybackSourceInfo(
            sourcePath,
            TimeSpan.FromTicks(player.Duration),
            TimeSpan.FromTicks(Math.Max(0, selectedVideo?.StartTime ?? 0)),
            (int)(selectedVideo?.Width ?? 0),
            (int)(selectedVideo?.Height ?? 0),
            audioStreams,
            _audioStreamIndex,
            player.Video.VideoAcceleration)
        {
            OpenMetrics = new(sourceOpenTimer.Elapsed, firstFrameTimer.Elapsed, totalTimer.Elapsed),
            FrameRate = selectedVideo?.FPS ?? 0
        };
        Trace.WriteLine(
            $"Playback open {Path.GetFileName(sourcePath)}: source={sourceOpenTimer.Elapsed.TotalMilliseconds:n0}ms, " +
            $"first-frame={firstFrameTimer.Elapsed.TotalMilliseconds:n0}ms, total={totalTimer.Elapsed.TotalMilliseconds:n0}ms");
        return new(info, first);
    }

    public async Task CloseAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        await _audio.StopAsync().ConfigureAwait(false);
        _sourcePath = null;
        _audioStreamIndex = null;
        await ClosePlayerAsync().ConfigureAwait(false);
    }

    public async Task PlayAsync(CancellationToken token)
    {
        var resumeTimer = Stopwatch.StartNew();
        token.ThrowIfCancellationRequested();
        var player = RequirePlayer();
        if (!_cadencePrepared && (_reviewOptions.FrameDivisor > 1 || _reviewOptions.TargetRate is not null))
        {
            var position = RunOnUi(() => TimeSpan.FromTicks(player.CurTime));
            RunOnUi(() => ConfigureCadence(player, preview: true));
            await SeekPlayerAsync(player, position, token).ConfigureAwait(false);
        }
        if (_sourcePath is { } sourcePath && _audioStreamIndex is { } streamIndex)
        {
            try
            {
                var position = RunOnUi(() => TimeSpan.FromTicks(player.CurTime));
                await _audio.StartAsync(sourcePath, streamIndex, position, token, _reviewOptions.Speed).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception exception)
            {
                Failed?.Invoke(this, new MediaPlaybackError(
                    MediaPlaybackErrorKind.AudioUnavailable,
                    "Audio output could not be started; video remains available.",
                    exception.Message));
            }
        }
        token.ThrowIfCancellationRequested();
        RunOnUi(player.Play);
        if (resumeTimer.ElapsedMilliseconds >= 150)
            App.ActivityLog?.TryAppend($"[Player timing] Playback/audio restart: {resumeTimer.ElapsedMilliseconds}ms.");
    }

    public async Task PauseAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var player = RequirePlayer();
        var position = RunOnUi(() => { player.Pause(); return TimeSpan.FromTicks(player.CurTime); });
        await _audio.StopAsync().ConfigureAwait(false);
        if (RunOnUi(() => player.Config.Video.MaxOutputFps != double.MaxValue || player.Config.Video.FrameSelection is not null))
        {
            RunOnUi(() => ConfigureCadence(player, preview: false));
            await SeekPlayerAsync(player, position, token).ConfigureAwait(false);
        }
    }

    private bool _cadencePrepared;
    public bool HasEnded => RunOnUi(() => _player?.Status == Status.Ended);
    internal int NativeSeekCount { get; private set; }
    private async Task StopPlaybackAsync(Player player, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        RunOnUi(player.Pause);
        await _audio.StopAsync().ConfigureAwait(false);
    }

    private void ConfigureCadence(Player player, bool preview)
    {
        _cadencePrepared = preview;
        player.Config.Video.FrameSelection = preview && _reviewOptions.FrameDivisor == 1 && _reviewOptions.TargetRate is { } rate
            ? new RationalCadenceSelector(rate).Select : null;
        player.Config.Video.MaxOutputFps = preview
            ? _reviewOptions.OutputThreshold(player.VideoDecoder.VideoStream.FPS) : double.MaxValue;
        // The pinned config property has no change notification. Refresh the decoder's derived selector
        // while paused, then flush queued frames by seeking. Player.Speed/the elapsed-time clock is unchanged.
        player.VideoDecoder.Speed = _reviewOptions.Speed == 1 ? 0.5 : 1;
        player.VideoDecoder.Speed = _reviewOptions.Speed;
    }

    public async Task<MediaPresentationTimestamp> SeekAsync(TimeSpan position, CancellationToken token)
        => await PrepareSeekAsync(position, false, token).ConfigureAwait(false);

    public async Task<MediaPresentationTimestamp> PrepareSeekAsync(TimeSpan position, bool resume, CancellationToken token)
    {
        var timer = Stopwatch.StartNew();
        var player = RequirePlayer();
        await StopPlaybackAsync(player, token).ConfigureAwait(false);
        var stopMilliseconds = timer.ElapsedMilliseconds;
        RunOnUi(() => ConfigureCadence(player, resume));
        var timestamp = await SeekPlayerAsync(player, position, token).ConfigureAwait(false);
        if (timer.ElapsedMilliseconds >= 150)
            App.ActivityLog?.TryAppend($"[Player timing] Seek to {position.TotalSeconds:0.000}s: stop={stopMilliseconds}ms, cadence/native seek={timer.ElapsedMilliseconds - stopMilliseconds}ms, resume={resume}.");
        return timestamp;
    }

    /// <summary>
    /// Every method in this class that mutates the native <see cref="Player"/> (Pause, Play, SeekAccurate,
    /// ShowFrameNext, OpenAsync) marshals that call onto <see cref="_dispatcher"/> — the thread that created the
    /// Player (and, for hardware-accelerated sources, its D3D11 device) via <see cref="CreatePlayer"/> — via
    /// <see cref="RunOnUi{T}"/>, rather than calling it directly wherever the surrounding async method happens to
    /// be executing. This is required, not defensive style: every async method in this class awaits with
    /// <c>ConfigureAwait(false)</c> (as does every caller up through <c>MediaPlaybackService</c>), so by the time
    /// execution reaches a native call past the method's first <see langword="await"/>, it is running on
    /// whichever arbitrary thread-pool thread the prior continuation happened to resume on — not necessarily the
    /// dispatcher thread, and not necessarily the *same* thread as the previous native call in the same logical
    /// operation. Proven root cause of a reproducible access violation (two sequential, fully-serialized Previous
    /// Frame clicks against a real, hardware-decoded source with a live D3D11 render surface attached — see
    /// FlyleafPlaybackIntegrationTests' hardware-decode regression test): Windows Error Reporting captured
    /// <c>System.AccessViolationException</c> inside Flyleaf's own demuxer read thread
    /// (<c>Flyleaf.FFmpeg.Raw.av_read_frame</c> via <c>Demuxer.RunInternal</c>/<c>RunThreadBase.Run</c>),
    /// consistent with a native call issued from a thread other than the one the Player/decoder/renderer was
    /// created on racing that background thread's own use of the same native context — a hazard specific to this
    /// process's own calling pattern, not something <c>FrameStepQueue</c>'s C#-level serialization (which was
    /// already correct) could prevent, since the two clicks in the repro were already fully sequential at the C#
    /// await level. A short experimental settle delay after Pause() (before the next native call) was tested and
    /// did not reliably prevent the crash, ruling out "just needs more time" as the explanation and confirming
    /// this is a thread-affinity defect, not a timing one.
    /// </summary>
    public async Task<MediaPresentationTimestamp> StepForwardAsync(CancellationToken token)
    {
        await _audio.StopAsync().ConfigureAwait(false);
        var player = RequirePlayer();
        token.ThrowIfCancellationRequested();
        // Flyleaf's ShowFrameNext is synchronous: it either calls UpdateCurTime before returning or leaves
        // CurTime unchanged at the end boundary. Reading after that same dispatcher call avoids the obsolete
        // ten-second wait without abandoning native work or guessing a nominal frame duration.
        return RunOnUi(() =>
        {
            player.Pause();
            player.ShowFrameNext();
            return Timestamp(player.CurTime);
        });
    }

    public async Task<MediaPresentationTimestamp> StepBackwardAsync(CancellationToken token)
    {
        await _audio.StopAsync().ConfigureAwait(false);
        var player = RequirePlayer();
        // See StepForwardAsync's doc comment above: every native Player call is marshaled onto the dispatcher thread.
        Interlocked.Increment(ref _suppressPresentationEvents);
        try
        {
            var original = RunOnUi(() => { player.Pause(); return player.CurTime; });
            if (original <= 0) return Timestamp(0);

            // Flyleaf's built-in ShowFramePrev maps timestamps through nominal FPS. That is not
            // authoritative for VFR, so reconstruct the immediate predecessor from decoded PTS.
            var window = TimeSpan.FromSeconds(1);
            for (var attempt = 0; attempt < 8; attempt++, window += window)
            {
                token.ThrowIfCancellationRequested();
                var start = TimeSpan.FromTicks(Math.Max(0, original - window.Ticks));
                var current = (await SeekPlayerAsync(player, start, token).ConfigureAwait(false)).Position.Ticks;
                long? predecessor = current < original ? current : null;
                for (var frames = 0; frames < 2000 && current < original; frames++)
                {
                    var next = await StepForwardAsync(token).ConfigureAwait(false);
                    if (next.Position.Ticks >= original || next.Position.Ticks == current) break;
                    predecessor = next.Position.Ticks;
                    current = next.Position.Ticks;
                }

                if (predecessor is null) continue;
                return await SettleOnKnownTimestampAsync(predecessor.Value, token).ConfigureAwait(false);
            }

            throw new InvalidOperationException("The previous decoded frame could not be located reliably.");
        }
        finally { Interlocked.Decrement(ref _suppressPresentationEvents); }
    }

    public async Task<MediaDecodedFrame> GetFrameAsync(TimeSpan position, CancellationToken token)
    {
        var player = RequirePlayer();
        // See StepForwardAsync's doc comment: native calls always marshaled onto the dispatcher thread.
        var (wasPlaying, restore) = RunOnUi(() => { var playing = player.IsPlaying; var position = TimeSpan.FromTicks(player.CurTime); player.Pause(); return (playing, position); });
        try
        {
            // Attaching a FlyleafHost initializes/resets the renderer. Doing that after SeekCompleted can
            // discard the decoded frame which completed the seek, leaving snapshot extraction to race a
            // replacement render. Make renderer readiness a prerequisite of the seek instead: ShowOneFrame
            // installs its decoded RendererFrame synchronously before Flyleaf raises SeekCompleted.
            await EnsureOffscreenSurfaceAsync(token).ConfigureAwait(false);
            var timestamp = await SeekPlayerAsync(player, position, token).ConfigureAwait(false);
            var bitmap = RunOnUi(() => player.TakeSnapshotToBitmapSource()
                ?? throw new InvalidOperationException("No decoded video frame is available."));
            var converted = EnsureBgra32(bitmap);
            var stride = converted.PixelWidth * 4;
            var pixels = new byte[stride * converted.PixelHeight];
            converted.CopyPixels(pixels, stride, 0);
            return new(timestamp, converted.PixelWidth, converted.PixelHeight, stride, pixels);
        }
        finally
        {
            // A source transition cancels this token before it can acquire the service's
            // serialization gate. Never restore or resume an obsolete player after that point.
            if (!token.IsCancellationRequested && ReferenceEquals(player, _player))
            {
                await SeekPlayerAsync(player, restore, token).ConfigureAwait(false);
                if (wasPlaying) RunOnUi(() => player.Play());
            }
        }
    }

    public Task<MediaDecodedFrame> CapturePresentedFrameAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var player = RequirePlayer();
        return Task.FromResult(RunOnUi(() =>
        {
            var bitmap = player.TakeSnapshotToBitmapSource()
                ?? throw new InvalidOperationException("No presented video frame is available.");
            var converted = EnsureBgra32(bitmap);
            var stride = converted.PixelWidth * 4;
            var pixels = new byte[stride * converted.PixelHeight];
            converted.CopyPixels(pixels, stride, 0);
            return new MediaDecodedFrame(
                Timestamp(player.CurTime), converted.PixelWidth, converted.PixelHeight, stride, pixels);
        }));
    }

    private Player CreatePlayer()
    {
        var config = new Config();
        config.Player.AutoPlay = false;
        config.Player.SeekAccurate = true;
        config.Player.UICurTime = UIRefreshType.PerFrame;
        config.Video.VideoAcceleration = true;
        config.Video.MaxOutputFps = double.MaxValue;
        config.Video.PostProcessorFactory = _postProcessorFactory;
        if (_videoProcessor is not null) config.Video.VideoProcessor = _videoProcessor.Value;
        // Flyleaf remains the video/PTS engine. Its audio decoder/output path is disabled because the shared
        // backend's bounded FFmpeg/WaveOut companion owns the selected audio stream.
        config.Audio.Enabled = false;
        var player = new Player(config);
        player.PropertyChanged += Player_PropertyChanged;
        return player;
    }

    internal void RequestRender() => RunOnUi(() => RequirePlayer().RequestRender());

    internal VideoProcessors ActiveVideoProcessor => RunOnUi(() => RequirePlayer().Renderer.VideoProcessor);
    internal (double Width, double Height) RenderedViewport => RunOnUi(() =>
        ((double)RequirePlayer().Renderer.Viewport.Width, (double)RequirePlayer().Renderer.Viewport.Height));

    private async Task ClosePlayerAsync()
    {
        var player = Interlocked.Exchange(ref _player, null);
        if (player is null) return;
        // Detach every WPF-owned reference before yielding. Application.Exit currently waits synchronously on
        // the UI thread, so a continuation which calls Dispatcher.Invoke after player.Dispose has completed
        // deadlocks against that blocked dispatcher and leaves the process alive with no visible windows.
        RunOnUi(() =>
        {
            player.PropertyChanged -= Player_PropertyChanged;
            if (ReferenceEquals(_host?.Player, player)) _host.Player = null;
        });
        await Task.Run(() => player.Dispose()).ConfigureAwait(false);
    }

    private void Player_PropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(Player.Status) && sender is Player endedPlayer &&
            ReferenceEquals(endedPlayer, _player) && endedPlayer.Status == FlyleafLib.MediaPlayer.Status.Ended)
        {
            Ended?.Invoke(this, EventArgs.Empty);
            return;
        }
        if (args.PropertyName != nameof(Player.CurTime) || sender is not Player player ||
            !ReferenceEquals(player, _player) || Volatile.Read(ref _suppressPresentationEvents) > 0) return;
        FramePresented?.Invoke(this, Timestamp(player.CurTime));
    }

    private async Task<OpenCompletedArgs> WaitForOpenAsync(Player player, string sourcePath, CancellationToken token)
    {
        var completion = new TaskCompletionSource<OpenCompletedArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<OpenCompletedArgs>? handler = null;
        handler = (_, args) => completion.TrySetResult(args);
        player.OpenCompleted += handler;
        using var registration = token.Register(() => completion.TrySetCanceled(token));
        try
        {
            // Every native Player call must run on the dispatcher thread that created it (see StepBackwardAsync's
            // own doc comment for the proven access-violation this fixes): once this method's own await
            // completes, .ConfigureAwait(false) resumes on an arbitrary threadpool thread, so RunOnUi is required
            // even though this call itself starts on the thread OpenAsync was invoked from.
            RunOnUi(() => player.OpenAsync(sourcePath, defaultSubtitles: false));
            return await completion.Task.ConfigureAwait(false);
        }
        finally { player.OpenCompleted -= handler; }
    }

    private async Task<MediaPresentationTimestamp> SeekPlayerAsync(Player player, TimeSpan position, CancellationToken token)
    {
        NativeSeekCount++;
        var clamped = Math.Clamp(position.TotalMilliseconds, 0, TimeSpan.FromTicks(player.Duration).TotalMilliseconds);
        var completion = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<int>? handler = null;
        handler = (_, result) => completion.TrySetResult(result);
        player.SeekCompleted += handler;
        using var registration = token.Register(() => completion.TrySetCanceled(token));
        try
        {
            // See StepBackwardAsync's doc comment: every native Player call must run on the dispatcher thread
            // that created the Player, never on whatever threadpool thread a prior .ConfigureAwait(false)
            // continuation happens to resume on.
            RunOnUi(() => player.SeekAccurateFromKeyframe((int)Math.Min(int.MaxValue, clamped)));
            var result = await completion.Task.ConfigureAwait(false);
            if (result < 0) throw new InvalidOperationException("The playback seek failed.");
            return RunOnUi(() => Timestamp(player.CurTime));
        }
        finally { player.SeekCompleted -= handler; }
    }

    private async Task<MediaPresentationTimestamp> SettleOnKnownTimestampAsync(long targetTicks, CancellationToken token)
    {
        var player = RequirePlayer();
        var timestamp = await SeekPlayerAsync(player, TimeSpan.FromTicks(targetTicks), token).ConfigureAwait(false);
        for (var frames = 0; timestamp.Position.Ticks < targetTicks && frames < 120; frames++)
            timestamp = await StepForwardAsync(token).ConfigureAwait(false);
        if (timestamp.Position.Ticks != targetTicks)
            throw new InvalidOperationException("Playback could not settle on the preceding decoded timestamp.");
        return timestamp;
    }

    private static BitmapSource EnsureBgra32(BitmapSource source)
    {
        if (source.Format == System.Windows.Media.PixelFormats.Bgra32) return source;
        var converted = new FormatConvertedBitmap(source, System.Windows.Media.PixelFormats.Bgra32, null, 0);
        converted.Freeze();
        return converted;
    }

    private static MediaPresentationTimestamp Timestamp(long ticks) => new(TimeSpan.FromTicks(Math.Max(0, ticks)));

    private async Task EnsureOffscreenSurfaceAsync(CancellationToken token)
    {
        TaskCompletionSource? loaded = null;
        RoutedEventHandler? loadedHandler = null;
        RunOnUi(() =>
        {
            _host ??= new FlyleafHost { Player = _player };
            if (_host.IsLoaded) return;

            loaded = new(TaskCreationOptions.RunContinuationsAsynchronously);
            loadedHandler = (_, _) => loaded.TrySetResult();
            _host.Loaded += loadedHandler;
            _offscreenWindow ??= new Window
            {
                Content = _host,
                Width = 2,
                Height = 2,
                Left = -32000,
                Top = -32000,
                ShowActivated = false,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None
            };
            _offscreenWindow.Show();
            if (_host.IsLoaded) loaded.TrySetResult();
        });

        if (loaded is null) return;
        try { await loaded.Task.WaitAsync(token).ConfigureAwait(false); }
        finally
        {
            RunOnUi(() =>
            {
                if (_host is not null && loadedHandler is not null)
                    _host.Loaded -= loadedHandler;
            });
        }
    }

    private void CloseOffscreenWindow()
    {
        if (_offscreenWindow is null) return;
        _offscreenWindow.Content = null;
        _offscreenWindow.Close();
        _offscreenWindow = null;
    }

    private static void ReleaseHost(FlyleafHost? host)
    {
        if (host is null) return;
        host.Player = null;
        host.Dispose();
    }
    private Player RequirePlayer() => _player ?? throw new InvalidOperationException("No playback source is loaded.");
    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private static void StartEngine(string ffmpegPath, Dispatcher dispatcher)
    {
        lock (EngineLock)
        {
            if (_engineStarted) return;
            var config = new EngineConfig
            {
                FFmpegPath = ffmpegPath,
                FFmpegLoadProfile = Flyleaf.FFmpeg.LoadProfile.Main,
                UIRefresh = false,
                KeepDisplayActive = false,
                LogLevel = LogLevel.Warn,
                FFmpegLogLevel = Flyleaf.FFmpeg.LogLevel.Warn
            };
            if (dispatcher.CheckAccess()) Engine.Start(config);
            else dispatcher.Invoke(() => Engine.Start(config));
            _engineStarted = true;
        }
    }

    private T RunOnUi<T>(Func<T> action)
    {
        return _dispatcher.CheckAccess() ? action() : _dispatcher.Invoke(action);
    }

    private void RunOnUi(Action action) => RunOnUi(() => { action(); return true; });

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        CancelPending();
        // Start native-player disposal and release the remaining WPF presentation objects while this method is
        // still executing on its caller's dispatcher. No continuation below may require that dispatcher: WPF
        // invokes Application.Exit on it, and App synchronously drains the app-owned services there.
        var playerDisposal = ClosePlayerAsync();
        RunOnUi(() =>
        {
            CloseOffscreenWindow();
            ReleaseHost(_host);
            _host = null;
        });
        await _audio.DisposeAsync().ConfigureAwait(false);
        await playerDisposal.ConfigureAwait(false);
        _pending.Dispose();
    }
}

internal static class PlaybackDependencyLocator
{
    public static string? FindSharedLibraries(string? baseDirectory = null)
    {
        baseDirectory ??= AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDirectory, "playback", "ffmpeg", "bin"),
            Path.Combine(baseDirectory, "ffmpeg", "bin"),
            Path.GetFullPath(Path.Combine(baseDirectory, "..", "..", "..", "..", "artifacts", "playback", "ffmpeg", "bin")),
            Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "playback", "ffmpeg", "bin")
        };
        return candidates.FirstOrDefault(IsValid);
    }

    public static bool IsValid(string path) => Directory.Exists(path)
        && Directory.EnumerateFiles(path, "avcodec-*.dll").Any()
        && Directory.EnumerateFiles(path, "avformat-*.dll").Any()
        && Directory.EnumerateFiles(path, "avutil-*.dll").Any();
}
