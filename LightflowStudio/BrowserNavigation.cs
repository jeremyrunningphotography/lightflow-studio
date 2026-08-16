using System.IO;

namespace LightflowStudio;

internal sealed record BrowserLocation(Guid RootId, string RootName, string RelativeFolder)
{
    public string DisplayPath => string.IsNullOrEmpty(RelativeFolder)
        ? RootName
        : $"{RootName}  ›  {RelativeFolder.Replace("/", "  ›  ")}";
}

internal enum BrowserFolderStatus
{
    Ready,
    Empty,
    RootNotFound,
    RootUnavailable,
    FolderNotFound,
    FolderUnavailable,
    AccessDenied,
    InvalidPath,
    Failed
}

internal sealed record BrowserFolderState(
    BrowserLocation? Location,
    BrowserFolderStatus Status,
    IReadOnlyList<MediaFolderEntry> Entries,
    string? Diagnostic,
    bool CanGoBack,
    bool CanGoForward,
    bool CanGoUp)
{
    public static BrowserFolderState Initial { get; } = new(null, BrowserFolderStatus.Empty, [],
        "Choose a Media Root to begin browsing.", false, false, false);
}

/// <summary>
/// UI-independent logical-folder session. Filesystem/Catalog work stays in the existing Media Root,
/// discovery, reconciliation, and enumeration services; only the latest requested location may publish.
/// </summary>
internal sealed class BrowserNavigationSession(
    IMediaRootService roots,
    IMediaDiscoveryRefreshService discovery,
    IMediaFolderEnumerator folders) : IDisposable
{
    private readonly object _sync = new();
    private readonly List<BrowserLocation> _back = [];
    private readonly List<BrowserLocation> _forward = [];
    private CancellationTokenSource? _activeRequest;
    private long _generation;
    private bool _disposed;

    public BrowserFolderState State { get; private set; } = BrowserFolderState.Initial;

    public Task<BrowserFolderState?> NavigateToRootAsync(Guid rootId, CancellationToken cancellationToken = default) =>
        NavigateAsync(new(rootId, "", ""), NavigationKind.New, cancellationToken);

    public Task<BrowserFolderState?> NavigateToFolderAsync(MediaFolderEntry entry,
        CancellationToken cancellationToken = default)
    {
        if (!entry.IsDirectory) throw new ArgumentException("Browser navigation requires a folder entry.", nameof(entry));
        var current = State.Location;
        if (current is null || current.RootId != entry.RootId)
            throw new ArgumentException("The folder does not belong to the current Browser location.", nameof(entry));
        return NavigateAsync(new(entry.RootId, current.RootName, entry.RelativePath), NavigationKind.New, cancellationToken);
    }

    public Task<BrowserFolderState?> BackAsync(CancellationToken cancellationToken = default)
    {
        BrowserLocation? target;
        lock (_sync) target = _back.Count == 0 ? null : _back[^1];
        return target is null ? Task.FromResult<BrowserFolderState?>(State)
            : NavigateAsync(target, NavigationKind.Back, cancellationToken);
    }

    public Task<BrowserFolderState?> ForwardAsync(CancellationToken cancellationToken = default)
    {
        BrowserLocation? target;
        lock (_sync) target = _forward.Count == 0 ? null : _forward[^1];
        return target is null ? Task.FromResult<BrowserFolderState?>(State)
            : NavigateAsync(target, NavigationKind.Forward, cancellationToken);
    }

    public Task<BrowserFolderState?> UpAsync(CancellationToken cancellationToken = default)
    {
        var current = State.Location;
        if (current is null || string.IsNullOrEmpty(current.RelativeFolder))
            return Task.FromResult<BrowserFolderState?>(State);
        var separator = current.RelativeFolder.LastIndexOf('/');
        var parent = separator < 0 ? "" : current.RelativeFolder[..separator];
        return NavigateAsync(current with { RelativeFolder = parent }, NavigationKind.New, cancellationToken);
    }

    public Task<BrowserFolderState?> RefreshAsync(CancellationToken cancellationToken = default)
    {
        var current = State.Location;
        return current is null ? Task.FromResult<BrowserFolderState?>(State)
            : NavigateAsync(current, NavigationKind.Refresh, cancellationToken);
    }

    private async Task<BrowserFolderState?> NavigateAsync(BrowserLocation requested,
        NavigationKind kind, CancellationToken cancellationToken)
    {
        CancellationTokenSource request;
        long generation;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _activeRequest?.Cancel();
            _activeRequest?.Dispose();
            _activeRequest = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            request = _activeRequest;
            generation = ++_generation;
        }

        try
        {
            var root = await roots.GetAsync(requested.RootId, request.Token).ConfigureAwait(false);
            request.Token.ThrowIfCancellationRequested();
            if (root is null)
                return Commit(generation, requested, kind, BrowserFolderStatus.RootNotFound, [],
                    "This Media Root no longer exists.");
            var location = requested with { RootName = root.DisplayName };
            if (root.Availability != MediaRootAvailability.Online)
                return Commit(generation, location, kind, BrowserFolderStatus.RootUnavailable, [],
                    root.Diagnostic ?? (root.Availability == MediaRootAvailability.Unmapped
                        ? "This Media Root is not connected on this computer. Reconnect it in Settings."
                        : "This Media Root is currently unavailable. Check the drive or network connection."));

            var authoritative = await discovery.RefreshAsync(
                new(location.RootId, EmptyToNull(location.RelativeFolder)), DerivedWorkPriority.Visible,
                request.Token, request.Token).ConfigureAwait(false);
            request.Token.ThrowIfCancellationRequested();
            if (!authoritative.Reconciliation.Succeeded)
                return Commit(generation, location, kind, Map(authoritative.Reconciliation.Status), [],
                    authoritative.Diagnostic ?? authoritative.Reconciliation.Diagnostic);

            var listing = await folders.EnumerateAsync(
                new(location.RootId, EmptyToNull(location.RelativeFolder)), request.Token).ConfigureAwait(false);
            request.Token.ThrowIfCancellationRequested();
            return Commit(generation, location with { RelativeFolder = listing.RelativeFolder }, kind,
                listing.Succeeded
                    ? listing.Entries.Count == 0 ? BrowserFolderStatus.Empty : BrowserFolderStatus.Ready
                    : Map(listing.Status), listing.Entries, listing.Diagnostic);
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return Commit(generation, requested, kind, BrowserFolderStatus.Failed, [],
                $"Lightflow could not open this folder: {exception.Message}");
        }
    }

    private BrowserFolderState? Commit(long generation, BrowserLocation location, NavigationKind kind,
        BrowserFolderStatus status, IReadOnlyList<MediaFolderEntry> entries, string? diagnostic)
    {
        lock (_sync)
        {
            if (_disposed || generation != _generation) return null;
            var previous = State.Location;
            switch (kind)
            {
                case NavigationKind.New:
                    if (previous is not null && previous != location) _back.Add(previous);
                    _forward.Clear();
                    break;
                case NavigationKind.Back:
                    if (_back.Count > 0) _back.RemoveAt(_back.Count - 1);
                    if (previous is not null) _forward.Add(previous);
                    break;
                case NavigationKind.Forward:
                    if (_forward.Count > 0) _forward.RemoveAt(_forward.Count - 1);
                    if (previous is not null) _back.Add(previous);
                    break;
            }
            State = new(location, status, entries, diagnostic, _back.Count > 0, _forward.Count > 0,
                !string.IsNullOrEmpty(location.RelativeFolder));
            return State;
        }
    }

    private static string? EmptyToNull(string value) => value.Length == 0 ? null : value;

    private static BrowserFolderStatus Map(MediaFolderEnumerationStatus status) => status switch
    {
        MediaFolderEnumerationStatus.RootNotFound => BrowserFolderStatus.RootNotFound,
        MediaFolderEnumerationStatus.RootUnavailable => BrowserFolderStatus.RootUnavailable,
        MediaFolderEnumerationStatus.FolderNotFound => BrowserFolderStatus.FolderNotFound,
        MediaFolderEnumerationStatus.FolderUnavailable => BrowserFolderStatus.FolderUnavailable,
        MediaFolderEnumerationStatus.AccessDenied => BrowserFolderStatus.AccessDenied,
        MediaFolderEnumerationStatus.InvalidPath or MediaFolderEnumerationStatus.LinkedPathRejected => BrowserFolderStatus.InvalidPath,
        _ => BrowserFolderStatus.Failed
    };

    private static BrowserFolderStatus Map(CatalogReconciliationStatus status) => status switch
    {
        CatalogReconciliationStatus.RootNotFound => BrowserFolderStatus.RootNotFound,
        CatalogReconciliationStatus.RootUnavailable => BrowserFolderStatus.RootUnavailable,
        CatalogReconciliationStatus.FolderUnavailable => BrowserFolderStatus.FolderUnavailable,
        CatalogReconciliationStatus.InvalidRequest => BrowserFolderStatus.InvalidPath,
        _ => BrowserFolderStatus.Failed
    };

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            _generation++;
            _activeRequest?.Cancel();
            _activeRequest?.Dispose();
            _activeRequest = null;
        }
    }

    private enum NavigationKind { New, Back, Forward, Refresh }
}
