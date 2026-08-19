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

/// <summary>
/// #124 (revised): the fast, purely Catalog-derived half of a navigation's outcome — location, effective
/// recursive mode, and the full stored root list — surfaced via <see cref="BrowserNavigationSession.EffectiveScopeDetermined"/>
/// well before the (potentially slow) media discovery <see cref="BrowserNavigationSession"/> runs afterward.
/// </summary>
internal sealed record BrowserEffectiveScope(BrowserLocation Location, BrowserScopeMode Mode,
    IReadOnlyList<BrowserRecursiveRoot> RecursiveRoots);

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
    bool CanGoUp,
    IDerivedWorkBatch? DerivedWork = null,
    // #124: the flattened, files-only candidate set across Location and every descendant folder, populated
    // only while Mode is BrowserScopeMode.IncludeSubfolders. Null in direct-folder mode, where Entries (this
    // folder's own direct listing) is already the grid's candidate set. Kept separate from Entries rather
    // than replacing it, because Entries also feeds the Locations tree's direct-child folder listing —
    // recursive scope is a media-canvas concern, not a navigation-tree concern, so the tree always reflects
    // direct children regardless of scope mode.
    IReadOnlyList<MediaFolderEntry>? RecursiveMediaEntries = null,
    // #124 (revised): the effective mode actually used for this commit — derived live from RecursiveRoots
    // against Location, never a manually toggled field. See BrowserRecursiveRootLogic.IsEffectivelyRecursive.
    BrowserScopeMode Mode = BrowserScopeMode.DirectFolder,
    // Every stored Catalog recursive root, as of this navigation — reused by MainWindow to sync Locations
    // tree iconography without a second Catalog round-trip. Empty for states committed before any root list
    // was ever fetched (e.g. an early RootNotFound/RootUnavailable failure).
    IReadOnlyList<BrowserRecursiveRoot>? RecursiveRoots = null)
{
    public static BrowserFolderState Initial { get; } = new(null, BrowserFolderStatus.Empty, [],
        "Choose a storage location to begin browsing.", false, false, false);
}

/// <summary>
/// A plain, synchronous <see cref="IProgress{T}"/> that invokes its callback directly on the reporting
/// thread. Deliberately not <see cref="System.Progress{T}"/>, which posts to the <see cref="System.Threading.SynchronizationContext"/>
/// captured at construction — asynchronously, via the thread pool, when none is present (as in a unit test or
/// any call already off a UI-owning context) — making exactly when a report is observed non-deterministic.
/// <see cref="BrowserNavigationSession"/> only needs the report to reach <see cref="BrowserNavigationSession.RaiseRecursiveScopeProgress"/>
/// promptly and in order; callers that must marshal onward to a UI thread (see <c>MainWindow</c>) do so
/// explicitly at the point they consume the resulting event, exactly like every other cross-thread signal in
/// this codebase already does.
/// </summary>
internal sealed class SynchronousProgress<T>(Action<T> callback) : IProgress<T>
{
    public void Report(T value) => callback(value);
}

/// <summary>
/// UI-independent filesystem-first navigation session. Absolute paths are resolved to stable logical roots
/// before the existing authoritative discovery and contained enumeration services run.
/// </summary>
internal sealed class BrowserNavigationSession(
    IMediaRootService roots,
    IBrowserLocationResolver locations,
    IMediaDiscoveryRefreshService discovery,
    IMediaFolderEnumerator folders,
    IBrowserRecursiveRootService recursiveRoots,
    IRecursiveMediaDiscoveryService? recursiveDiscovery = null) : IDisposable
{
    private readonly IRecursiveMediaDiscoveryService _recursiveDiscovery =
        recursiveDiscovery ?? new RecursiveMediaDiscoveryService(folders, discovery);
    private readonly object _sync = new();
    private readonly List<BrowserLocation> _back = [];
    private readonly List<BrowserLocation> _forward = [];
    private CancellationTokenSource? _activeRequest;
    private long _generation;
    private bool _disposed;

    public BrowserFolderState State { get; private set; } = BrowserFolderState.Initial;

    /// <summary>
    /// #124 (revised): fires as soon as effective recursive mode for the folder being loaded is known —
    /// immediately after fetching the Catalog's stored recursive roots, before the (potentially slow)
    /// recursive discovery or authoritative reconciliation that follows even begins. A caller's tree
    /// icons/toolbar toggle are presentation derived purely from location + Catalog recursive-root
    /// configuration, never from the eventual media result set, so they should never wait on discovery to
    /// finish — this event is what lets a caller apply that presentation immediately while <see cref="Commit"/>
    /// (and the full <see cref="BrowserFolderState"/> it produces) is still pending. Generation-gated exactly
    /// like <see cref="Commit"/>: a report from a navigation superseded before reaching this point (a different
    /// folder opened, a scope toggle, disposal) is silently dropped rather than reaching subscribers, so a
    /// stale determination can never overwrite a newer one's presentation. Fires for every navigation,
    /// direct or recursive alike.
    /// </summary>
    public event EventHandler<BrowserEffectiveScope>? EffectiveScopeDetermined;

    /// <summary>
    /// #124: fires with the live <see cref="RecursiveScopeProgress"/> of the current recursive walk, while
    /// one is in flight. Generation-gated exactly like <see cref="Commit"/>: a report from a walk that has
    /// since been superseded (mode toggled again, a different folder opened, disposal) is silently dropped
    /// rather than reaching subscribers, so obsolete progress can never update a caller's UI after a newer
    /// request has taken over. Never raised in <see cref="BrowserScopeMode.DirectFolder"/>, since no recursive
    /// walk ever runs there.
    /// </summary>
    public event EventHandler<RecursiveScopeProgress>? RecursiveScopeProgressChanged;

    public BrowserLocation? BackTarget
    {
        get { lock (_sync) return _back.Count == 0 ? null : _back[^1]; }
    }

    public BrowserLocation? ForwardTarget
    {
        get { lock (_sync) return _forward.Count == 0 ? null : _forward[^1]; }
    }

    public BrowserLocation? UpTarget
    {
        get
        {
            var current = State.Location;
            var parent = current is null ? null : ParentPath(current.AbsolutePath);
            return parent is null || current is null ? null : current with
            {
                RelativeFolder = Path.GetRelativePath(current.RootPath, parent) is "." ? "" :
                    MediaPathSemantics.NormalizeRelativePath(Path.GetRelativePath(current.RootPath, parent))
            };
        }
    }

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

    /// <summary>
    /// #124 (revised): establishes or removes a durable Catalog recursive root governing the currently
    /// selected folder — <see cref="IBrowserRecursiveRootService.EnableAsync"/>/<see cref="IBrowserRecursiveRootService.DisableAsync"/>,
    /// never a settable field — then reloads the current folder through the same generation/cancellation
    /// machinery as any other navigation, as a same-folder "refresh" rather than a "New" navigation, so
    /// toggling never pushes a back-stack entry and never disturbs the current <see cref="BrowserQuery"/> the
    /// caller owns separately. Disabling from an inherited descendant removes the governing ancestor root —
    /// see <see cref="IBrowserRecursiveRootService.DisableAsync"/> — never creates a per-folder override. A
    /// no-op when no location is open yet. Any exception from the Catalog operation (e.g. Catalog
    /// unavailable) propagates unchanged, matching every other Catalog-backed navigation failure in this
    /// class — no separate error UI path for this feature.
    /// </summary>
    public Task<BrowserFolderState?> SetIncludeSubfoldersAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        var current = State.Location;
        if (current is null) return Task.FromResult<BrowserFolderState?>(State);
        return ApplyIncludeSubfoldersAsync(current, enabled, cancellationToken);
    }

    private async Task<BrowserFolderState?> ApplyIncludeSubfoldersAsync(BrowserLocation current, bool enabled,
        CancellationToken cancellationToken)
    {
        // Begin() here (rather than only inside the eventual LoadAndCommitAsync) so the Catalog mutation
        // itself immediately supersedes/cancels any still-in-flight prior request — latest-request-wins
        // applies to the whole toggle operation, not just its reload half.
        var operation = Begin(cancellationToken);
        try
        {
            if (enabled)
                await recursiveRoots.EnableAsync(current.RootId, current.RelativeFolder, operation.Request.Token).ConfigureAwait(false);
            else
                await recursiveRoots.DisableAsync(current.RootId, current.RelativeFolder, operation.Request.Token).ConfigureAwait(false);
            return await LoadAndCommitAsync(operation, current, NavigationKind.Refresh, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (operation.Request.IsCancellationRequested)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return null;
        }
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

            // #124 (revised): effective mode is derived live from the Catalog's stored recursive roots against
            // this location — never a manually toggled field. The fetched list is also carried on the
            // committed state (see BrowserFolderState.RecursiveRoots) so MainWindow can sync Locations-tree
            // iconography from the same round-trip rather than querying the Catalog a second time.
            var recursiveRootList = await recursiveRoots.ListAsync(operation.Request.Token).ConfigureAwait(false);
            operation.Request.Token.ThrowIfCancellationRequested();
            var mode = BrowserRecursiveRootLogic.IsEffectivelyRecursive(recursiveRootList, location.RootId, location.RelativeFolder)
                ? BrowserScopeMode.IncludeSubfolders
                : BrowserScopeMode.DirectFolder;
            RaiseEffectiveScopeDetermined(operation.Generation, new(location, mode, recursiveRootList));

            if (mode == BrowserScopeMode.IncludeSubfolders)
            {
                var progressReporter = new SynchronousProgress<RecursiveScopeProgress>(
                    reported => RaiseRecursiveScopeProgress(operation.Generation, reported));
                var recursive = await _recursiveDiscovery.DiscoverAsync(
                    new(location.RootId, EmptyToNull(location.RelativeFolder)), DerivedWorkPriority.Visible,
                    operation.Request.Token, operation.Request.Token, progressReporter).ConfigureAwait(false);
                operation.Request.Token.ThrowIfCancellationRequested();
                if (!recursive.Succeeded)
                    return Commit(operation.Generation, location, kind, Map(recursive.Status), [],
                        recursive.Diagnostic, mode: mode, recursiveRoots: recursiveRootList);

                // Still needed for the Locations tree's direct-child folder listing, which always reflects
                // direct children regardless of scope mode — see BrowserFolderState.RecursiveMediaEntries.
                var directListing = await folders.EnumerateAsync(
                    new(location.RootId, EmptyToNull(location.RelativeFolder)), operation.Request.Token).ConfigureAwait(false);
                operation.Request.Token.ThrowIfCancellationRequested();
                return Commit(operation.Generation, location with { RelativeFolder = recursive.RelativeFolder }, kind,
                    directListing.Succeeded
                        ? recursive.MediaEntries.Count == 0 ? BrowserFolderStatus.Empty : BrowserFolderStatus.Ready
                        : Map(directListing.Status),
                    directListing.Succeeded ? directListing.Entries : [],
                    directListing.Diagnostic ?? recursive.Diagnostic, recursive.DerivedWork,
                    recursive.MediaEntries, mode, recursiveRootList);
            }

            var authoritative = await discovery.RefreshAsync(
                new(location.RootId, EmptyToNull(location.RelativeFolder)), DerivedWorkPriority.Visible,
                operation.Request.Token, operation.Request.Token).ConfigureAwait(false);
            operation.Request.Token.ThrowIfCancellationRequested();
            if (!authoritative.Reconciliation.Succeeded)
                return Commit(operation.Generation, location, kind, Map(authoritative.Reconciliation.Status), [],
                    authoritative.Diagnostic ?? authoritative.Reconciliation.Diagnostic, mode: mode, recursiveRoots: recursiveRootList);

            var listing = await folders.EnumerateAsync(
                new(location.RootId, EmptyToNull(location.RelativeFolder)), operation.Request.Token).ConfigureAwait(false);
            operation.Request.Token.ThrowIfCancellationRequested();
            return Commit(operation.Generation, location with { RelativeFolder = listing.RelativeFolder }, kind,
                listing.Succeeded
                    ? listing.Entries.Count == 0 ? BrowserFolderStatus.Empty : BrowserFolderStatus.Ready
                    : Map(listing.Status), listing.Entries, listing.Diagnostic, authoritative.DerivedWork,
                mode: mode, recursiveRoots: recursiveRootList);
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
        BrowserFolderStatus status, IReadOnlyList<MediaFolderEntry> entries, string? diagnostic,
        IDerivedWorkBatch? derivedWork = null, IReadOnlyList<MediaFolderEntry>? recursiveMediaEntries = null,
        BrowserScopeMode mode = BrowserScopeMode.DirectFolder,
        IReadOnlyList<BrowserRecursiveRoot>? recursiveRoots = null)
    {
        lock (_sync)
        {
            if (_disposed || generation != _generation) return null;
            if (status is not (BrowserFolderStatus.Ready or BrowserFolderStatus.Empty))
                return new(location, status, [], diagnostic, State.CanGoBack, State.CanGoForward,
                    State.CanGoUp, Mode: mode, RecursiveRoots: recursiveRoots);
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
                ParentPath(location.AbsolutePath) is not null, derivedWork, recursiveMediaEntries, mode, recursiveRoots);
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

    /// <summary>Drops an effective-scope determination from any navigation that is no longer the current generation before it can reach subscribers.</summary>
    private void RaiseEffectiveScopeDetermined(long generation, BrowserEffectiveScope scope)
    {
        lock (_sync) { if (_disposed || generation != _generation) return; }
        EffectiveScopeDetermined?.Invoke(this, scope);
    }

    /// <summary>Drops a progress report from any walk that is no longer the current generation before it can reach subscribers.</summary>
    private void RaiseRecursiveScopeProgress(long generation, RecursiveScopeProgress progress)
    {
        lock (_sync) { if (_disposed || generation != _generation) return; }
        RecursiveScopeProgressChanged?.Invoke(this, progress);
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
