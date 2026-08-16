using System.IO;

namespace LightflowStudio;

internal sealed record BrowserLocation(
    Guid RootId,
    string RootName,
    string RootPath,
    string RelativeFolder)
{
    public string AbsolutePath => string.IsNullOrEmpty(RelativeFolder)
        ? RootPath
        : MediaPathSemantics.ResolveContained(RootPath, RelativeFolder);
    public string DisplayPath => AbsolutePath;
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
    CatalogUnavailable,
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
        "Choose a storage location to begin browsing.", false, false, false);
}

/// <summary>
/// UI-independent filesystem-first navigation session. Absolute paths are resolved to stable logical roots
/// before the existing authoritative discovery and contained enumeration services run.
/// </summary>
internal sealed class BrowserNavigationSession(
    IMediaRootService roots,
    IBrowserLocationResolver locations,
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

    public Task<BrowserFolderState?> NavigateToPathAsync(string absoluteFolder,
        CancellationToken cancellationToken = default) =>
        NavigateResolvedAsync(absoluteFolder, NavigationKind.New, cancellationToken);

    public async Task<BrowserFolderState?> NavigateToRootAsync(Guid rootId,
        CancellationToken cancellationToken = default)
    {
        var operation = Begin(cancellationToken);
        try
        {
            var root = await roots.GetAsync(rootId, operation.Request.Token).ConfigureAwait(false);
            operation.Request.Token.ThrowIfCancellationRequested();
            if (root is null)
                return CommitFailure(operation.Generation, BrowserFolderStatus.RootNotFound,
                    "This managed location no longer exists.");
            if (root.Availability != MediaRootAvailability.Online || string.IsNullOrWhiteSpace(root.PhysicalPath))
                return CommitFailure(operation.Generation, BrowserFolderStatus.RootUnavailable, root.Diagnostic ??
                    "This managed location is not available on this computer.");
            var resolution = await locations.ResolveAsync(root.PhysicalPath, operation.Request.Token).ConfigureAwait(false);
            operation.Request.Token.ThrowIfCancellationRequested();
            if (!resolution.Succeeded)
                return CommitFailure(operation.Generation, Map(resolution.Status), resolution.Diagnostic);
            var location = new BrowserLocation(resolution.RootId!.Value, resolution.RootName!, resolution.RootPath!,
                resolution.RelativeFolder);
            return await LoadAndCommitAsync(operation, location, NavigationKind.New, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (operation.Request.IsCancellationRequested)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return null;
        }
    }

    public Task<BrowserFolderState?> NavigateToFolderAsync(MediaFolderEntry entry,
        CancellationToken cancellationToken = default)
    {
        if (!entry.IsDirectory) throw new ArgumentException("Browser navigation requires a folder entry.", nameof(entry));
        var current = State.Location;
        if (current is null || current.RootId != entry.RootId)
            throw new ArgumentException("The folder does not belong to the current Browser location.", nameof(entry));
        return NavigateResolvedAsync(MediaPathSemantics.ResolveContained(current.RootPath, entry.RelativePath),
            NavigationKind.New, cancellationToken);
    }

    public Task<BrowserFolderState?> BackAsync(CancellationToken cancellationToken = default)
    {
        BrowserLocation? target;
        lock (_sync) target = _back.Count == 0 ? null : _back[^1];
        return target is null ? Task.FromResult<BrowserFolderState?>(State)
            : NavigateKnownAsync(target, NavigationKind.Back, cancellationToken);
    }

    public Task<BrowserFolderState?> ForwardAsync(CancellationToken cancellationToken = default)
    {
        BrowserLocation? target;
        lock (_sync) target = _forward.Count == 0 ? null : _forward[^1];
        return target is null ? Task.FromResult<BrowserFolderState?>(State)
            : NavigateKnownAsync(target, NavigationKind.Forward, cancellationToken);
    }

    public Task<BrowserFolderState?> UpAsync(CancellationToken cancellationToken = default)
    {
        var current = State.Location;
        if (current is null) return Task.FromResult<BrowserFolderState?>(State);
        var parent = ParentPath(current.AbsolutePath);
        return parent is null ? Task.FromResult<BrowserFolderState?>(State)
            : NavigateResolvedAsync(parent, NavigationKind.New, cancellationToken);
    }

    public Task<BrowserFolderState?> RefreshAsync(CancellationToken cancellationToken = default)
    {
        var current = State.Location;
        return current is null ? Task.FromResult<BrowserFolderState?>(State)
            : NavigateResolvedAsync(current.AbsolutePath, NavigationKind.Refresh, cancellationToken);
    }

    private async Task<BrowserFolderState?> NavigateResolvedAsync(string absoluteFolder,
        NavigationKind kind, CancellationToken cancellationToken)
    {
        var operation = Begin(cancellationToken);
        try
        {
            var resolution = await locations.ResolveAsync(absoluteFolder, operation.Request.Token).ConfigureAwait(false);
            operation.Request.Token.ThrowIfCancellationRequested();
            if (!resolution.Succeeded)
                return CommitFailure(operation.Generation, Map(resolution.Status), resolution.Diagnostic);
            var location = new BrowserLocation(resolution.RootId!.Value, resolution.RootName!, resolution.RootPath!,
                resolution.RelativeFolder);
            return await LoadAndCommitAsync(operation, location, kind, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (operation.Request.IsCancellationRequested)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return null;
        }
    }

    private async Task<BrowserFolderState?> NavigateKnownAsync(BrowserLocation location,
        NavigationKind kind, CancellationToken cancellationToken)
    {
        var operation = Begin(cancellationToken);
        try { return await LoadAndCommitAsync(operation, location, kind, cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (operation.Request.IsCancellationRequested)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return null;
        }
    }

    private async Task<BrowserFolderState?> LoadAndCommitAsync(Operation operation, BrowserLocation location,
        NavigationKind kind, CancellationToken callerToken)
    {
        try
        {
            var root = await roots.GetAsync(location.RootId, operation.Request.Token).ConfigureAwait(false);
            operation.Request.Token.ThrowIfCancellationRequested();
            if (root is null)
                return Commit(operation.Generation, location, kind, BrowserFolderStatus.RootNotFound, [],
                    "This location's Catalog identity no longer exists.");
            if (root.Availability != MediaRootAvailability.Online)
                return Commit(operation.Generation, location, kind, BrowserFolderStatus.RootUnavailable, [], root.Diagnostic ??
                    "This storage location is currently unavailable.");
            location = location with { RootName = root.DisplayName, RootPath = root.PhysicalPath! };

            var authoritative = await discovery.RefreshAsync(
                new(location.RootId, EmptyToNull(location.RelativeFolder)), DerivedWorkPriority.Visible,
                operation.Request.Token, operation.Request.Token).ConfigureAwait(false);
            operation.Request.Token.ThrowIfCancellationRequested();
            if (!authoritative.Reconciliation.Succeeded)
                return Commit(operation.Generation, location, kind, Map(authoritative.Reconciliation.Status), [],
                    authoritative.Diagnostic ?? authoritative.Reconciliation.Diagnostic);

            var listing = await folders.EnumerateAsync(
                new(location.RootId, EmptyToNull(location.RelativeFolder)), operation.Request.Token).ConfigureAwait(false);
            operation.Request.Token.ThrowIfCancellationRequested();
            return Commit(operation.Generation, location with { RelativeFolder = listing.RelativeFolder }, kind,
                listing.Succeeded
                    ? listing.Entries.Count == 0 ? BrowserFolderStatus.Empty : BrowserFolderStatus.Ready
                    : Map(listing.Status), listing.Entries, listing.Diagnostic);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            callerToken.ThrowIfCancellationRequested();
            return Commit(operation.Generation, location, kind, BrowserFolderStatus.Failed, [],
                $"Lightflow could not open this folder: {exception.Message}");
        }
    }

    private Operation Begin(CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _activeRequest?.Cancel();
            _activeRequest?.Dispose();
            _activeRequest = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            return new(_activeRequest, ++_generation);
        }
    }

    private BrowserFolderState? Commit(long generation, BrowserLocation location, NavigationKind kind,
        BrowserFolderStatus status, IReadOnlyList<MediaFolderEntry> entries, string? diagnostic)
    {
        lock (_sync)
        {
            if (_disposed || generation != _generation) return null;
            if (status is not (BrowserFolderStatus.Ready or BrowserFolderStatus.Empty))
                return new(location, status, [], diagnostic, State.CanGoBack, State.CanGoForward,
                    State.CanGoUp);
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
                ParentPath(location.AbsolutePath) is not null);
            return State;
        }
    }

    private BrowserFolderState? CommitFailure(long generation, BrowserFolderStatus status, string? diagnostic)
    {
        lock (_sync)
        {
            if (_disposed || generation != _generation) return null;
            return new(State.Location, status, [], diagnostic, State.CanGoBack, State.CanGoForward, State.CanGoUp);
        }
    }

    private static string? EmptyToNull(string value) => value.Length == 0 ? null : value;

    private static string? ParentPath(string path)
    {
        try
        {
            if (string.Equals(MediaPathSemantics.NormalizeRootPath(path),
                    BrowserLocationResolver.NaturalAnchor(path), StringComparison.OrdinalIgnoreCase)) return null;
            return Directory.GetParent(path)?.FullName;
        }
        catch (ArgumentException) { return null; }
    }

    private static BrowserFolderStatus Map(BrowserLocationResolutionStatus status) => status switch
    {
        BrowserLocationResolutionStatus.CatalogUnavailable => BrowserFolderStatus.CatalogUnavailable,
        BrowserLocationResolutionStatus.FolderUnavailable => BrowserFolderStatus.FolderUnavailable,
        BrowserLocationResolutionStatus.InvalidPath => BrowserFolderStatus.InvalidPath,
        _ => BrowserFolderStatus.Failed
    };

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

    private sealed record Operation(CancellationTokenSource Request, long Generation);
    private enum NavigationKind { New, Back, Forward, Refresh }
}
