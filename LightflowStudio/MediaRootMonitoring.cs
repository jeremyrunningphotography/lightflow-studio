using System.IO;

namespace LightflowStudio;

internal enum MediaRootChangeKind { Created, Changed, Deleted, Renamed, Error, Overflow }
internal enum MediaRootMonitorStatus { Running, Degraded, Stopped }

internal sealed record MediaRootChange(Guid RootId, MediaRootChangeKind Kind, string? Path = null,
    string? OldPath = null, string? Diagnostic = null);

internal sealed record MediaRootMonitorState(MediaRootMonitorStatus Status, int WatchedRoots,
    string? Diagnostic = null);

internal interface IMediaRootWatcher : IDisposable { void Start(); }

internal interface IMediaRootWatcherFactory
{
    IMediaRootWatcher Create(MediaRootInfo root, Action<MediaRootChange> publish);
}

internal sealed class FileSystemMediaRootWatcherFactory : IMediaRootWatcherFactory
{
    public IMediaRootWatcher Create(MediaRootInfo root, Action<MediaRootChange> publish) =>
        new FileSystemMediaRootWatcher(root, publish);

    private sealed class FileSystemMediaRootWatcher : IMediaRootWatcher
    {
        private readonly Guid _rootId;
        private readonly FileSystemWatcher _watcher;
        private readonly Action<MediaRootChange> _publish;

        public FileSystemMediaRootWatcher(MediaRootInfo root, Action<MediaRootChange> publish)
        {
            if (root.PhysicalPath is null) throw new ArgumentException("An online Media Root requires a physical path.");
            _rootId = root.RootId;
            _publish = publish;
            _watcher = new(root.PhysicalPath)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
                InternalBufferSize = 32 * 1024
            };
            _watcher.Created += (_, e) => Publish(MediaRootChangeKind.Created, e.FullPath);
            _watcher.Changed += (_, e) => Publish(MediaRootChangeKind.Changed, e.FullPath);
            _watcher.Deleted += (_, e) => Publish(MediaRootChangeKind.Deleted, e.FullPath);
            _watcher.Renamed += (_, e) => _publish(new(_rootId, MediaRootChangeKind.Renamed, e.FullPath, e.OldFullPath));
            _watcher.Error += (_, e) => _publish(new(_rootId,
                e.GetException() is InternalBufferOverflowException ? MediaRootChangeKind.Overflow : MediaRootChangeKind.Error,
                Diagnostic: e.GetException()?.Message));
        }

        public void Start() => _watcher.EnableRaisingEvents = true;
        public void Dispose() => _watcher.Dispose();
        private void Publish(MediaRootChangeKind kind, string path) => _publish(new(_rootId, kind, path));
    }
}

internal interface IMediaRootMonitoringService : IAsyncDisposable
{
    MediaRootMonitorState State { get; }
    event EventHandler<MediaRootMonitorState>? StateChanged;
    Task StartAsync(CancellationToken cancellationToken = default);
    Task SynchronizeAsync(CancellationToken cancellationToken = default);
    Task FlushAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// File-system notifications are hints only. This service coalesces them into calls to the
/// authoritative discovery/reconciliation pipeline and never mutates Catalog state directly.
/// </summary>
internal sealed class MediaRootMonitoringService : IMediaRootMonitoringService
{
    private readonly IMediaRootService _roots;
    private readonly IMediaDiscoveryRefreshService _refresh;
    private readonly IMediaRootWatcherFactory _factory;
    private readonly TimeSpan _debounce;
    private readonly int _maximumPending;
    private readonly object _sync = new();
    private readonly Dictionary<Guid, WatchRegistration> _watchers = [];
    private readonly Dictionary<Guid, string?> _observedRoots = [];
    private readonly Dictionary<RefreshKey, DateTimeOffset> _pending = [];
    private readonly SemaphoreSlim _signal = new(0);
    private readonly CancellationTokenSource _shutdown = new();
    private Task? _worker;
    private bool _disposed;

    public MediaRootMonitoringService(IMediaRootService roots, IMediaDiscoveryRefreshService refresh,
        IMediaRootWatcherFactory? factory = null, TimeSpan? debounce = null, int maximumPending = 4096)
    {
        _roots = roots;
        _refresh = refresh;
        _factory = factory ?? new FileSystemMediaRootWatcherFactory();
        _debounce = debounce ?? TimeSpan.FromMilliseconds(600);
        _maximumPending = maximumPending > 0 ? maximumPending : throw new ArgumentOutOfRangeException(nameof(maximumPending));
    }

    public MediaRootMonitorState State { get; private set; } = new(MediaRootMonitorStatus.Stopped, 0);
    public event EventHandler<MediaRootMonitorState>? StateChanged;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await SynchronizeAsync(cancellationToken).ConfigureAwait(false);
        lock (_sync) _worker ??= Task.Run(WorkerAsync);
    }

    public async Task SynchronizeAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        IReadOnlyList<MediaRootInfo> roots;
        try { roots = await _roots.ListAsync(cancellationToken).ConfigureAwait(false); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            SetState(MediaRootMonitorStatus.Degraded, $"Media Root monitoring could not be synchronized: {exception.Message}");
            return;
        }

        lock (_sync)
        {
            string? synchronizationDiagnostic = null;
            var online = roots.Where(root => root.Availability == MediaRootAvailability.Online && root.PhysicalPath is not null)
                .ToDictionary(root => root.RootId);
            foreach (var obsolete in _watchers.Where(pair => !online.TryGetValue(pair.Key, out var root) ||
                         !SamePath(pair.Value.Path, root.PhysicalPath!)).Select(pair => pair.Key).ToArray())
            {
                _watchers[obsolete].Watcher.Dispose();
                _watchers.Remove(obsolete);
            }
            foreach (var root in online.Values.Where(root => !_watchers.ContainsKey(root.RootId)))
            {
                try
                {
                    var isReconnectOrRemap = _observedRoots.TryGetValue(root.RootId, out var previousPath);
                    var watcher = _factory.Create(root, Publish);
                    watcher.Start();
                    _watchers.Add(root.RootId, new(root.PhysicalPath!, watcher));
                    if (isReconnectOrRemap && !SameNullablePath(previousPath, root.PhysicalPath))
                        QueueLocked(new(root.RootId, null), immediate: true);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
                {
                    synchronizationDiagnostic = $"Media Root ‘{root.DisplayName}’ could not be monitored: {exception.Message}";
                }
            }
            foreach (var root in roots) _observedRoots[root.RootId] =
                root.Availability == MediaRootAvailability.Online ? root.PhysicalPath : null;
            SetStateLocked(synchronizationDiagnostic is null ? MediaRootMonitorStatus.Running : MediaRootMonitorStatus.Degraded,
                synchronizationDiagnostic);
        }
    }

    private void Publish(MediaRootChange change)
    {
        lock (_sync)
        {
            if (_disposed || !_watchers.TryGetValue(change.RootId, out var registration)) return;
            if (change.Kind is MediaRootChangeKind.Error or MediaRootChangeKind.Overflow)
            {
                QueueLocked(new(change.RootId, null));
                SetStateLocked(MediaRootMonitorStatus.Degraded,
                    change.Diagnostic ?? "A filesystem notification was lost; an authoritative refresh was requested.");
                return;
            }

            var folders = new HashSet<string?>(StringComparer.OrdinalIgnoreCase);
            string? oldFolder = null;
            if (!TryFolder(registration.Path, change.Path, out var folder) ||
                change.Kind == MediaRootChangeKind.Renamed && !TryFolder(registration.Path, change.OldPath, out oldFolder))
            {
                QueueLocked(new(change.RootId, null));
                return;
            }
            folders.Add(folder);
            if (change.Kind == MediaRootChangeKind.Renamed) folders.Add(oldFolder);
            foreach (var value in folders) QueueLocked(new(change.RootId, value));
        }
    }

    private void QueueLocked(RefreshKey key, bool immediate = false)
    {
        if (_pending.Count >= _maximumPending && !_pending.ContainsKey(key))
        {
            var rootIds = _pending.Keys.Select(item => item.RootId).Append(key.RootId).Distinct().ToArray();
            _pending.Clear();
            foreach (var rootId in rootIds)
                _pending[new(rootId, null)] = DateTimeOffset.UtcNow;
        }
        else _pending[key] = immediate ? DateTimeOffset.MinValue : DateTimeOffset.UtcNow + _debounce;
        try { _signal.Release(); } catch (SemaphoreFullException) { }
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        RefreshKey[] work;
        lock (_sync)
        {
            work = _pending.Keys.OrderBy(key => key.RootId).ThenBy(key => key.RelativeFolder).ToArray();
            _pending.Clear();
        }
        foreach (var key in work)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var result = await _refresh.RefreshAsync(new(key.RootId, key.RelativeFolder), DerivedWorkPriority.Background,
                    cancellationToken, _shutdown.Token).ConfigureAwait(false);
                if (!result.Reconciliation.Succeeded)
                    SetState(MediaRootMonitorStatus.Degraded, result.Diagnostic ??
                        "An authoritative Media Root refresh could not be completed.");
            }
            catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { return; }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                SetState(MediaRootMonitorStatus.Degraded, $"An authoritative Media Root refresh failed: {exception.Message}");
            }
        }
    }

    private async Task WorkerAsync()
    {
        try
        {
            while (true)
            {
                if (!await _signal.WaitAsync(TimeSpan.FromSeconds(5), _shutdown.Token).ConfigureAwait(false))
                {
                    await SynchronizeAsync(_shutdown.Token).ConfigureAwait(false);
                    continue;
                }
                while (true)
                {
                    TimeSpan delay;
                    lock (_sync)
                    {
                        if (_pending.Count == 0) break;
                        var due = _pending.Values.Min();
                        delay = due <= DateTimeOffset.UtcNow ? TimeSpan.Zero : due - DateTimeOffset.UtcNow;
                    }
                    if (delay > TimeSpan.Zero) await Task.Delay(delay, _shutdown.Token).ConfigureAwait(false);
                    await FlushDueAsync(_shutdown.Token).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { }
    }

    private async Task FlushDueAsync(CancellationToken cancellationToken)
    {
        RefreshKey[] work;
        lock (_sync)
        {
            var now = DateTimeOffset.UtcNow;
            work = _pending.Where(pair => pair.Value <= now).Select(pair => pair.Key).ToArray();
            foreach (var key in work) _pending.Remove(key);
        }
        foreach (var key in work)
        {
            try
            {
                var result = await _refresh.RefreshAsync(new(key.RootId, key.RelativeFolder), DerivedWorkPriority.Background,
                    cancellationToken, _shutdown.Token).ConfigureAwait(false);
                if (!result.Reconciliation.Succeeded)
                    SetState(MediaRootMonitorStatus.Degraded, result.Diagnostic ??
                        "An authoritative Media Root refresh could not be completed.");
            }
            catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { return; }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                SetState(MediaRootMonitorStatus.Degraded, $"An authoritative Media Root refresh failed: {exception.Message}");
            }
        }
    }

    private static bool TryFolder(string rootPath, string? eventPath, out string? relativeFolder)
    {
        relativeFolder = null;
        if (string.IsNullOrWhiteSpace(eventPath)) return false;
        try
        {
            var root = MediaPathSemantics.NormalizeRootPath(rootPath);
            var full = Path.GetFullPath(eventPath);
            var relative = Path.GetRelativePath(root, full);
            if (relative == ".") return true;
            var normalized = MediaPathSemantics.NormalizeRelativePath(relative);
            _ = MediaPathSemantics.ResolveContained(root, normalized);
            if (ContainsReparsePoint(root, full)) return false;
            var parent = Path.GetDirectoryName(normalized.Replace('/', Path.DirectorySeparatorChar));
            relativeFolder = string.IsNullOrEmpty(parent) ? null : parent.Replace(Path.DirectorySeparatorChar, '/');
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException) { return false; }
    }

    private static bool ContainsReparsePoint(string root, string path)
    {
        var current = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
        while (!string.IsNullOrEmpty(current) && !SamePath(current, root))
        {
            if (File.Exists(current) || Directory.Exists(current))
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return true;
            }
            current = Path.GetDirectoryName(current);
        }
        return false;
    }

    private void SetState(MediaRootMonitorStatus status, string? diagnostic)
    {
        lock (_sync) SetStateLocked(status, diagnostic);
    }

    private void SetStateLocked(MediaRootMonitorStatus status, string? diagnostic)
    {
        State = new(status, _watchers.Count, diagnostic);
        StateChanged?.Invoke(this, State);
    }

    private static bool SamePath(string left, string right) => string.Equals(
        MediaPathSemantics.NormalizeRootPath(left), MediaPathSemantics.NormalizeRootPath(right),
        StringComparison.OrdinalIgnoreCase);

    private static bool SameNullablePath(string? left, string? right) =>
        left is null || right is null ? left == right : SamePath(left, right);

    public async ValueTask DisposeAsync()
    {
        Task? worker;
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            _shutdown.Cancel();
            foreach (var registration in _watchers.Values) registration.Watcher.Dispose();
            _watchers.Clear();
            _pending.Clear();
            worker = _worker;
            SetStateLocked(MediaRootMonitorStatus.Stopped, null);
        }
        if (worker is not null) await worker.ConfigureAwait(false);
        _shutdown.Dispose();
        _signal.Dispose();
    }

    private sealed record WatchRegistration(string Path, IMediaRootWatcher Watcher);
    private readonly record struct RefreshKey(Guid RootId, string? RelativeFolder);
}
