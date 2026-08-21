using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FlyleafLib;
using FlyleafLib.Controls.WPF;
using FlyleafLib.MediaPlayer;

namespace LightflowStudio;

internal sealed class FlyleafPlaybackBackend : IMediaPlaybackBackend
{
    private static readonly object EngineLock = new();
    private static bool _engineStarted;
    private readonly string _ffmpegPath;
    private readonly Dispatcher _dispatcher;
    private Window? _offscreenWindow;
    private FlyleafHost? _host;
    private CancellationTokenSource _pending = new();
    private Player? _player;
    private bool _disposed;

    public FlyleafPlaybackBackend(string? ffmpegPath = null)
    {
        _ffmpegPath = ffmpegPath ?? PlaybackDependencyLocator.FindSharedLibraries()
            ?? throw new DirectoryNotFoundException("Bundled playback libraries were not found.");
        _dispatcher = Dispatcher.CurrentDispatcher;
        StartEngine(_ffmpegPath, _dispatcher);
    }

    public event EventHandler<MediaPresentationTimestamp>? FramePresented;
    public event EventHandler<MediaPlaybackError>? Failed;

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
        ThrowIfDisposed();
        if (!File.Exists(sourcePath)) throw new FileNotFoundException("The playback source does not exist.", sourcePath);
        await ClosePlayerAsync().ConfigureAwait(false);

        var player = RunOnUi(CreatePlayer);
        _player = player;
        RunOnUi(() => { if (_host is not null) _host.Player = player; });
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _pending.Token);
        var opened = await WaitForOpenAsync(player, sourcePath, linked.Token).ConfigureAwait(false);
        if (!opened.Success)
        {
            var error = new MediaPlaybackError(MediaPlaybackErrorKind.InvalidOrCorruptMedia,
                "The video could not be decoded for preview.", opened.Error);
            Failed?.Invoke(this, error);
            throw new InvalidDataException(opened.Error ?? error.Message);
        }

        var first = await SeekPlayerAsync(player, TimeSpan.Zero, linked.Token).ConfigureAwait(false);
        var audioStreams = (player.Audio.Streams ?? [])
            .Select(stream => new MediaAudioStreamInfo(
                stream.StreamIndex,
                stream.Language?.OriginalInput,
                stream.Title,
                stream.Channels,
                stream.StreamIndex == player.Audio.StreamIndex))
            .ToList();
        var selectedVideo = player.Video.Streams?.FirstOrDefault(stream => stream.StreamIndex == player.Video.StreamIndex);
        var info = new MediaPlaybackSourceInfo(
            sourcePath,
            TimeSpan.FromTicks(player.Duration),
            TimeSpan.FromTicks(Math.Max(0, selectedVideo?.StartTime ?? 0)),
            player.Video.Width,
            player.Video.Height,
            audioStreams,
            player.Audio.StreamIndex >= 0 ? player.Audio.StreamIndex : null,
            player.Video.VideoAcceleration);
        return new(info, first);
    }

    public async Task CloseAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        await ClosePlayerAsync().ConfigureAwait(false);
    }

    public Task PlayAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        RequirePlayer().Play();
        return Task.CompletedTask;
    }

    public Task PauseAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        RequirePlayer().Pause();
        return Task.CompletedTask;
    }

    public Task<MediaPresentationTimestamp> SeekAsync(TimeSpan position, CancellationToken token) =>
        SeekPlayerAsync(RequirePlayer(), position, token);

    public async Task<MediaPresentationTimestamp> StepForwardAsync(CancellationToken token)
    {
        var player = RequirePlayer();
        player.Pause();
        var before = player.CurTime;
        player.ShowFrameNext();
        return await WaitForTimestampChangeAsync(player, before, token).ConfigureAwait(false);
    }

    /// <summary>
    /// A single internal forward step used while reconstructing a backward-step predecessor (both the main
    /// search loop below and <see cref="SettleOnKnownTimestampAsync"/>), bounded to
    /// <see cref="InternalReconstructionStepTimeout"/> rather than <see cref="StepForwardAsync"/>'s own
    /// unbounded-by-default contract. This exists because of a proven, position-dependent engine limitation:
    /// <c>ShowFrameNext()</c> can genuinely fail to decode any further at all — not merely fail to raise its
    /// completion event — within roughly the last few frames of a source (confirmed empirically two ways:
    /// identical <c>SeekAsync</c>-then-<c>StepForwardAsync</c> calls succeed in milliseconds everywhere else in
    /// a real clip and reliably stall only in that trailing window regardless of total clip length; and
    /// directly polling <c>Player.CurTime</c> for 4+ seconds after issuing <c>ShowFrameNext()</c> there, with no
    /// dependency on <c>PropertyChanged</c> at all, shows the position never moves — ruling out a missed
    /// notification and confirming the decode itself does not advance). Backward reconstruction searches
    /// forward from an earlier seek point toward the current position and can therefore reach that trailing
    /// window even when the *current* position is not near the end at all. Direct forward stepping already has
    /// an equivalent external bound (<c>PlaybackFrameStep</c>'s boundary timeout); this gives the
    /// reconstruction's own internal steps the same protection without needing a single overall wall-clock
    /// budget for the whole (potentially legitimately multi-second) reconstruction — see
    /// <c>PlaybackFrameStep</c>'s own doc comment for why that would be wrong. Returns <see langword="null"/>
    /// (never throws for its own bound firing) so the caller can treat this as "the engine cannot decode any
    /// further from here" and settle for the closest already-confirmed predecessor rather than aborting the
    /// whole backward step — see the main search loop below for why retrying with a different window cannot
    /// help (the wall is inherent to approaching the target position, not to the window's starting point).
    /// </summary>
    private async Task<long?> TryBoundedStepForwardAsync(CancellationToken token)
    {
        using var stepGuard = CancellationTokenSource.CreateLinkedTokenSource(token);
        stepGuard.CancelAfter(InternalReconstructionStepTimeout);
        try
        {
            var timestamp = await StepForwardAsync(stepGuard.Token).ConfigureAwait(false);
            return timestamp.Position.Ticks;
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            // Our own bound fired, not the caller's token — the caller's session is still fine.
            return null;
        }
    }

    /// <summary>
    /// Generous relative to the low-hundreds-of-milliseconds a normal internal step takes even in a slow
    /// environment, but far short of <see cref="WaitForTimestampChangeAsync"/>'s own internal 10-second
    /// fallback — cuts a trailing-window stall down from 10 seconds to 2 per affected internal step, letting
    /// reconstruction settle for its closest confirmed predecessor promptly rather than hanging.
    /// </summary>
    private static readonly TimeSpan InternalReconstructionStepTimeout = TimeSpan.FromSeconds(2);

    public async Task<MediaPresentationTimestamp> StepBackwardAsync(CancellationToken token)
    {
        var player = RequirePlayer();
        player.Pause();
        var original = player.CurTime;
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
                var next = await TryBoundedStepForwardAsync(token).ConfigureAwait(false);
                // The engine cannot decode any further from `current` — settle for the closest predecessor
                // already confirmed via genuine decoded steps (or the seek landing itself) rather than
                // discarding it: a larger window's final approach would hit the exact same wall, since the
                // wall is inherent to how close we are to `original`, not to where the window started.
                if (next is null) break;
                if (next.Value >= original) break;
                predecessor = next.Value;
                current = next.Value;
            }

            if (predecessor is null) continue;
            return await SettleOnKnownTimestampAsync(predecessor.Value, token).ConfigureAwait(false);
        }

        throw new InvalidOperationException("The previous decoded frame could not be located reliably.");
    }

    public async Task<MediaDecodedFrame> GetFrameAsync(TimeSpan position, CancellationToken token)
    {
        var player = RequirePlayer();
        var wasPlaying = player.IsPlaying;
        var restore = TimeSpan.FromTicks(player.CurTime);
        player.Pause();
        try
        {
            var timestamp = await SeekPlayerAsync(player, position, token).ConfigureAwait(false);
            RunOnUi(EnsureOffscreenSurface);
            await Task.Delay(50, token).ConfigureAwait(false);
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
                if (wasPlaying) player.Play();
            }
        }
    }

    private Player CreatePlayer()
    {
        var config = new Config();
        config.Player.AutoPlay = false;
        config.Player.SeekAccurate = true;
        config.Player.UICurTime = UIRefreshType.PerFrame;
        config.Video.VideoAcceleration = true;
        var player = new Player(config);
        player.PropertyChanged += Player_PropertyChanged;
        return player;
    }

    private async Task ClosePlayerAsync()
    {
        var player = Interlocked.Exchange(ref _player, null);
        if (player is null) return;
        player.PropertyChanged -= Player_PropertyChanged;
        await Task.Run(() => player.Dispose()).ConfigureAwait(false);
        RunOnUi(() => { if (ReferenceEquals(_host?.Player, player)) _host.Player = null; });
    }

    private void Player_PropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(Player.CurTime) || sender is not Player player || !ReferenceEquals(player, _player)) return;
        FramePresented?.Invoke(this, Timestamp(player.CurTime));
    }

    private static async Task<OpenCompletedArgs> WaitForOpenAsync(Player player, string sourcePath, CancellationToken token)
    {
        var completion = new TaskCompletionSource<OpenCompletedArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<OpenCompletedArgs>? handler = null;
        handler = (_, args) => completion.TrySetResult(args);
        player.OpenCompleted += handler;
        using var registration = token.Register(() => completion.TrySetCanceled(token));
        try
        {
            player.OpenAsync(sourcePath, defaultSubtitles: false);
            return await completion.Task.ConfigureAwait(false);
        }
        finally { player.OpenCompleted -= handler; }
    }

    private static async Task<MediaPresentationTimestamp> SeekPlayerAsync(Player player, TimeSpan position, CancellationToken token)
    {
        var clamped = Math.Clamp(position.TotalMilliseconds, 0, TimeSpan.FromTicks(player.Duration).TotalMilliseconds);
        var completion = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<int>? handler = null;
        handler = (_, result) => completion.TrySetResult(result);
        player.SeekCompleted += handler;
        using var registration = token.Register(() => completion.TrySetCanceled(token));
        try
        {
            player.SeekAccurate((int)Math.Min(int.MaxValue, clamped));
            var result = await completion.Task.ConfigureAwait(false);
            if (result < 0) throw new InvalidOperationException("The playback seek failed.");
            return Timestamp(player.CurTime);
        }
        finally { player.SeekCompleted -= handler; }
    }

    private static async Task<MediaPresentationTimestamp> WaitForTimestampChangeAsync(Player player, long before, CancellationToken token)
    {
        var completion = new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);
        PropertyChangedEventHandler? handler = null;
        handler = (_, args) =>
        {
            if (args.PropertyName == nameof(Player.CurTime) && player.CurTime != before) completion.TrySetResult(player.CurTime);
        };
        player.PropertyChanged += handler;
        using var registration = token.Register(() => completion.TrySetCanceled(token));
        try
        {
            if (player.CurTime != before) return Timestamp(player.CurTime);
            return Timestamp(await completion.Task.WaitAsync(TimeSpan.FromSeconds(10), token).ConfigureAwait(false));
        }
        finally { player.PropertyChanged -= handler; }
    }

    private async Task<MediaPresentationTimestamp> SettleOnKnownTimestampAsync(long targetTicks, CancellationToken token)
    {
        var player = RequirePlayer();
        var timestamp = await SeekPlayerAsync(player, TimeSpan.FromTicks(targetTicks), token).ConfigureAwait(false);
        for (var frames = 0; timestamp.Position.Ticks < targetTicks && frames < 120; frames++)
        {
            var next = await TryBoundedStepForwardAsync(token).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Playback could not settle on the preceding decoded timestamp.");
            timestamp = Timestamp(next);
        }
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

    private void EnsureOffscreenSurface()
    {
        _host ??= new FlyleafHost { Player = _player };
        if (_host.IsLoaded || _offscreenWindow is not null) return;
        _offscreenWindow = new Window
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
        await ClosePlayerAsync().ConfigureAwait(false);
        RunOnUi(() =>
        {
            CloseOffscreenWindow();
            ReleaseHost(_host);
            _host = null;
        });
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
