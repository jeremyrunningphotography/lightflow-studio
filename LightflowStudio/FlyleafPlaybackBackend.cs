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
    private readonly FlyleafHost _host;
    private readonly Dispatcher _dispatcher;
    private Window? _offscreenWindow;
    private CancellationTokenSource _pending = new();
    private Player? _player;
    private bool _disposed;

    public FlyleafPlaybackBackend(string? ffmpegPath = null)
    {
        _ffmpegPath = ffmpegPath ?? PlaybackDependencyLocator.FindSharedLibraries()
            ?? throw new DirectoryNotFoundException("Bundled playback libraries were not found.");
        _dispatcher = Dispatcher.CurrentDispatcher;
        StartEngine(_ffmpegPath, _dispatcher);
        _host = RunOnUi(() => new FlyleafHost());
    }

    public event EventHandler<MediaPresentationTimestamp>? FramePresented;
    public event EventHandler<MediaPlaybackError>? Failed;

    public FrameworkElement CreatePresentationSurface()
    {
        RunOnUi(() =>
        {
            if (_offscreenWindow is null) return;
            _offscreenWindow.Content = null;
            _offscreenWindow.Close();
            _offscreenWindow = null;
        });
        return _host;
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
        RunOnUi(() => _host.Player = player);
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
                var next = (await StepForwardAsync(token).ConfigureAwait(false)).Position.Ticks;
                if (next >= original) break;
                predecessor = next;
                current = next;
            }

            if (predecessor is null) continue;
            return await SettleOnKnownTimestampAsync(player, predecessor.Value, token).ConfigureAwait(false);
        }

        throw new InvalidOperationException("The previous decoded frame could not be located reliably.");
    }

    public async Task<MediaDecodedFrame> GetFrameAsync(TimeSpan position, CancellationToken token)
    {
        var player = RequirePlayer();
        var wasPlaying = player.IsPlaying;
        var restore = TimeSpan.FromTicks(player.CurTime);
        player.Pause();
        var timestamp = await SeekPlayerAsync(player, position, token).ConfigureAwait(false);
        RunOnUi(EnsureOffscreenSurface);
        await Task.Delay(50, token).ConfigureAwait(false);
        var bitmap = RunOnUi(() => player.TakeSnapshotToBitmapSource()
            ?? throw new InvalidOperationException("No decoded video frame is available."));
        var converted = EnsureBgra32(bitmap);
        var stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);
        var frame = new MediaDecodedFrame(timestamp, converted.PixelWidth, converted.PixelHeight, stride, pixels);
        await SeekPlayerAsync(player, restore, CancellationToken.None).ConfigureAwait(false);
        if (wasPlaying) player.Play();
        return frame;
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
        RunOnUi(() => { if (ReferenceEquals(_host.Player, player)) _host.Player = null; });
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

    private static async Task<MediaPresentationTimestamp> SettleOnKnownTimestampAsync(Player player, long targetTicks, CancellationToken token)
    {
        var timestamp = await SeekPlayerAsync(player, TimeSpan.FromTicks(targetTicks), token).ConfigureAwait(false);
        for (var frames = 0; timestamp.Position.Ticks < targetTicks && frames < 120; frames++)
            timestamp = await new FlyleafStepForward(player).RunAsync(token).ConfigureAwait(false);
        if (timestamp.Position.Ticks != targetTicks)
            throw new InvalidOperationException("Playback could not settle on the preceding decoded timestamp.");
        return timestamp;
    }

    private sealed class FlyleafStepForward(Player player)
    {
        public async Task<MediaPresentationTimestamp> RunAsync(CancellationToken token)
        {
            var before = player.CurTime;
            player.ShowFrameNext();
            return await WaitForTimestampChangeAsync(player, before, token).ConfigureAwait(false);
        }
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
            if (_offscreenWindow is not null)
            {
                _offscreenWindow.Content = null;
                _offscreenWindow.Close();
                _offscreenWindow = null;
            }
            _host.Dispose();
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
