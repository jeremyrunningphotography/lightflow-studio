namespace LightflowStudio;

/// <summary>
/// #124: the Browser scope abstraction. A location (root + relative folder, from #107/#108) can be browsed as
/// direct children only, or as that folder plus every descendant folder. Deliberately just an enum today —
/// #124 only needs "folder, direct" and "folder, include descendants" — but every consumer (the navigation
/// session, the recursive discovery service, monitoring relevance) is written against this type rather than a
/// bare bool, so a later scope kind (Favorites, Collections, Smart Collections, All Media, Recent — see #72)
/// can extend the same seam without redesigning the boundary between scope and <see cref="BrowserQuery"/>.
/// </summary>
internal enum BrowserScopeMode { DirectFolder, IncludeSubfolders }

/// <summary>
/// Pure scope-membership logic, kept separate from any particular consumer so both the recursive discovery
/// walk and Browser monitoring relevance (#101's <see cref="IMediaRootMonitoringService.FolderRefreshed"/>)
/// use one authoritative answer to "is this folder inside that base scope".
/// </summary>
internal static class BrowserScope
{
    /// <summary>
    /// True when <paramref name="candidateRelativeFolder"/> falls within the recursive scope rooted at
    /// <paramref name="baseRelativeFolder"/> — equal to it, a descendant of it, or the base scope is the
    /// Media Root itself (in which case every folder is inside it). A null candidate represents an
    /// ambiguous/root-level monitoring event (e.g. a watcher reconnect or overflow, which #101 already
    /// queues with a null relative folder); treated as relevant, since watcher events are hints only and an
    /// authoritative refresh in that case is the safe default. Malformed folder text is likewise treated as
    /// relevant rather than silently ignored, for the same reason.
    /// </summary>
    public static bool IsWithinFolderScope(string? candidateRelativeFolder, string baseRelativeFolder)
    {
        if (candidateRelativeFolder is null || baseRelativeFolder.Length == 0) return true;
        // An empty string is this codebase's convention for "the Media Root folder itself" (see
        // BrowserLocation.RelativeFolder) — meaningfully different from null (ambiguous/ancestor event) and
        // never itself inside a non-root base scope, so it must be rejected explicitly rather than falling
        // through to NormalizeRelativePath, which rejects empty paths for an unrelated reason (enforcing
        // well-formed non-empty relative file paths) and would otherwise be caught by the defensive fallback below.
        if (candidateRelativeFolder.Length == 0) return false;
        string candidate, @base;
        try
        {
            candidate = MediaPathSemantics.NormalizeRelativePath(candidateRelativeFolder);
            @base = MediaPathSemantics.NormalizeRelativePath(baseRelativeFolder);
        }
        catch (ArgumentException) { return true; }
        return string.Equals(candidate, @base, StringComparison.OrdinalIgnoreCase) ||
            candidate.StartsWith(@base + "/", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// The outcome of recursively discovering/reconciling one base folder and all of its descendant folders.
/// Shaped to slot directly into <see cref="BrowserFolderState"/> alongside the existing direct-folder path:
/// <see cref="MediaEntries"/> is the flattened, already-filtered-to-files candidate set for the grid, and
/// <see cref="DerivedWork"/> is a single <see cref="IDerivedWorkBatch"/> (see
/// <see cref="AggregateDerivedWorkBatch"/>) so #108's existing grid/thumbnail/metadata hydration code needs
/// no recursive-specific branch at all.
/// </summary>
internal sealed record RecursiveScopeResult(
    CatalogReconciliationStatus Status,
    Guid RootId,
    string RelativeFolder,
    IReadOnlyList<MediaFolderEntry> MediaEntries,
    IDerivedWorkBatch? DerivedWork,
    string? Diagnostic = null)
{
    public bool Succeeded => Status == CatalogReconciliationStatus.Succeeded;
}

/// <summary>
/// Honest, in-progress folder counts for one recursive walk: how many folders have been identified so far
/// (<see cref="FoldersDiscovered"/> — the denominator, itself still growing as the walk proceeds) and how many
/// of those have finished their own enumerate+reconcile pass (<see cref="FoldersVisited"/> — the numerator).
/// <see cref="FoldersVisited"/> never exceeds <see cref="FoldersDiscovered"/>; both are monotonically
/// increasing for a given walk. This is deliberately not a final total — a true total is unknowable without a
/// separate traversal pass this service does not perform — so it represents "done vs. currently known", the
/// same honest denominator a caller like <c>MainWindow</c> can present as determinate progress once it has
/// enough data to be meaningful.
/// </summary>
internal readonly record struct RecursiveScopeProgress(int FoldersDiscovered, int FoldersVisited);

internal interface IRecursiveMediaDiscoveryService
{
    Task<RecursiveScopeResult> DiscoverAsync(MediaFolderEnumerationRequest baseRequest,
        DerivedWorkPriority priority = DerivedWorkPriority.Background,
        CancellationToken enumerationToken = default,
        CancellationToken derivedWorkToken = default,
        IProgress<RecursiveScopeProgress>? progress = null);
}

/// <summary>
/// Service-layer (WPF-independent) recursive scope resolution for #124's Include Subfolders. Reuses the
/// existing #98-#100 pipeline exactly as the single-folder <see cref="BrowserNavigationSession"/> path does —
/// one <see cref="IMediaFolderEnumerator.EnumerateAsync"/> + one <see cref="IMediaDiscoveryRefreshService.RefreshAsync"/>
/// call per visited folder, so every asset identity, reconciliation, and derived-work outcome is produced by
/// exactly the same code a direct-folder view would use for that same folder. There is no second
/// reconciliation strategy, no parallel thumbnail/metadata pipeline, and no Browser-owned filesystem walk:
/// this is the one place recursion itself lives, and it lives in the service layer, not in WPF.
/// </summary>
internal sealed class RecursiveMediaDiscoveryService(
    IMediaFolderEnumerator folders,
    IMediaDiscoveryRefreshService discovery,
    int maximumConcurrentFolders = 4,
    int maximumFolders = 20000) : IRecursiveMediaDiscoveryService
{
    public async Task<RecursiveScopeResult> DiscoverAsync(MediaFolderEnumerationRequest baseRequest,
        DerivedWorkPriority priority = DerivedWorkPriority.Background,
        CancellationToken enumerationToken = default,
        CancellationToken derivedWorkToken = default,
        IProgress<RecursiveScopeProgress>? progress = null)
    {
        var baseFolder = NormalizeFolder(baseRequest.RelativeFolder);

        var baseListing = await folders.EnumerateAsync(new(baseRequest.RootId, baseFolder), enumerationToken)
            .ConfigureAwait(false);
        if (!baseListing.Succeeded)
            return new(Map(baseListing.Status), baseRequest.RootId, baseFolder, [], null, baseListing.Diagnostic);

        var baseRefresh = await discovery.RefreshAsync(new(baseRequest.RootId, baseFolder), priority,
            enumerationToken, derivedWorkToken).ConfigureAwait(false);
        if (!baseRefresh.Reconciliation.Succeeded)
            return new(baseRefresh.Reconciliation.Status, baseRequest.RootId, baseFolder, [], null,
                baseRefresh.Diagnostic ?? baseRefresh.Reconciliation.Diagnostic);

        var state = new WalkState(Math.Max(1, maximumFolders));
        state.TryReserveFolder();
        state.AddFolder(baseListing, baseRefresh);
        state.MarkVisited();
        progress?.Report(state.Snapshot());

        using var gate = new SemaphoreSlim(Math.Max(1, maximumConcurrentFolders));
        var subfolders = baseListing.Entries.Where(entry => entry.IsDirectory).Select(entry => entry.RelativePath);
        await Task.WhenAll(subfolders.Select(subfolder =>
                VisitAsync(baseRequest.RootId, subfolder, priority, gate, state, enumerationToken, derivedWorkToken, progress)))
            .ConfigureAwait(false);

        var diagnostic = CombineDiagnostics(state.Diagnostics);
        var reconciliation = new CatalogReconciliationResult(CatalogReconciliationStatus.Succeeded,
            baseRequest.RootId, baseFolder, state.ReconciliationItems, state.UnsupportedCount, diagnostic);
        var batches = state.DerivedWorkBatches;
        var derivedWork = batches.Count == 0 ? null : new AggregateDerivedWorkBatch(reconciliation, batches);
        return new(CatalogReconciliationStatus.Succeeded, baseRequest.RootId, baseFolder, state.MediaEntries,
            derivedWork, diagnostic);
    }

    /// <summary>
    /// Visits one descendant folder: enumerate + reconcile it exactly like the top-level call, fold its
    /// contribution into the shared <paramref name="state"/>, then fan out to its own subfolders. Bounded to
    /// at most <paramref name="gate"/>'s permit count concurrent enumerate/reconcile pairs in flight at once —
    /// deliberately not one unbounded task per descendant folder — while still letting the (cheap, I/O-idle)
    /// task tree itself grow to match however many folders exist, which is what actually lets a large tree
    /// make forward progress instead of serializing folder-by-folder. A folder that individually fails
    /// (access denied, transient I/O) is skipped with a recorded diagnostic rather than failing the whole
    /// scope — consistent with "offline/unavailable roots remain non-destructive". Real cancellation
    /// (distinct from a per-folder business failure) is never caught here: it propagates through
    /// <see cref="Task.WhenAll(IEnumerable{Task})"/> exactly like the single-folder path already handles it,
    /// so <see cref="BrowserNavigationSession"/>'s existing generation/latest-wins machinery needs no
    /// recursive-specific branch.
    /// </summary>
    private async Task VisitAsync(Guid rootId, string relativeFolder, DerivedWorkPriority priority,
        SemaphoreSlim gate, WalkState state, CancellationToken enumerationToken, CancellationToken derivedWorkToken,
        IProgress<RecursiveScopeProgress>? progress)
    {
        enumerationToken.ThrowIfCancellationRequested();
        if (!state.TryReserveFolder()) return;
        // Reserving this folder just grew the known denominator (FoldersDiscovered) even before it has been
        // visited — report promptly so a caller's determinate progress reflects newly discovered folders as
        // soon as they are known, not only when folders finish.
        progress?.Report(state.Snapshot());

        MediaFolderEnumerationResult listing;
        MediaDiscoveryRefreshResult refresh;
        await gate.WaitAsync(enumerationToken).ConfigureAwait(false);
        try
        {
            listing = await folders.EnumerateAsync(new(rootId, relativeFolder), enumerationToken).ConfigureAwait(false);
            if (!listing.Succeeded)
            {
                state.AddDiagnostic($"{relativeFolder}: {listing.Diagnostic ?? listing.Status.ToString()}");
                return;
            }
            refresh = await discovery.RefreshAsync(new(rootId, relativeFolder), priority, enumerationToken,
                derivedWorkToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
            // Runs on every exit from the try above, including a thrown cancellation: if this folder's
            // attempt was actually canceled, the whole walk is unwinding anyway (see the class doc — real
            // cancellation propagates out of DiscoverAsync entirely rather than being caught here), so no
            // caller ever observes this last, technically-premature "visited" report before the operation
            // aborts. On an ordinary (non-canceled) exit — success or a recorded per-folder failure above —
            // this correctly counts the folder's own enumerate+reconcile attempt as done.
            state.MarkVisited();
            progress?.Report(state.Snapshot());
        }

        if (!refresh.Reconciliation.Succeeded)
        {
            state.AddDiagnostic($"{relativeFolder}: " +
                (refresh.Diagnostic ?? refresh.Reconciliation.Diagnostic ?? refresh.Reconciliation.Status.ToString()));
            return;
        }
        state.AddFolder(listing, refresh);

        var subfolders = listing.Entries.Where(entry => entry.IsDirectory).Select(entry => entry.RelativePath);
        await Task.WhenAll(subfolders.Select(subfolder =>
                VisitAsync(rootId, subfolder, priority, gate, state, enumerationToken, derivedWorkToken, progress)))
            .ConfigureAwait(false);
    }

    private static string NormalizeFolder(string? relativeFolder) =>
        string.IsNullOrWhiteSpace(relativeFolder) || relativeFolder.Trim() == "."
            ? ""
            : MediaPathSemantics.NormalizeRelativePath(relativeFolder);

    private static string? CombineDiagnostics(IReadOnlyList<string> diagnostics) =>
        diagnostics.Count == 0 ? null : string.Join(" ", diagnostics);

    private static CatalogReconciliationStatus Map(MediaFolderEnumerationStatus status) => status switch
    {
        MediaFolderEnumerationStatus.RootNotFound => CatalogReconciliationStatus.RootNotFound,
        MediaFolderEnumerationStatus.RootUnavailable => CatalogReconciliationStatus.RootUnavailable,
        MediaFolderEnumerationStatus.FolderNotFound or MediaFolderEnumerationStatus.FolderUnavailable or
            MediaFolderEnumerationStatus.AccessDenied or MediaFolderEnumerationStatus.LinkedPathRejected =>
            CatalogReconciliationStatus.FolderUnavailable,
        MediaFolderEnumerationStatus.InvalidPath => CatalogReconciliationStatus.InvalidRequest,
        _ => CatalogReconciliationStatus.EnumerationFailed
    };

    /// <summary>Thread-safe aggregation across every visited folder, plus the total-folder safety valve that bounds pathologically large/looping trees.</summary>
    private sealed class WalkState(int maximumFolders)
    {
        private readonly object _sync = new();
        private readonly List<MediaFolderEntry> _mediaEntries = [];
        private readonly List<CatalogReconciliationItem> _reconciliationItems = [];
        private readonly List<IDerivedWorkBatch> _derivedWorkBatches = [];
        private readonly List<string> _diagnostics = [];
        private int _unsupportedCount;
        private int _reservedFolders;
        private int _visitedFolders;
        private bool _truncated;

        public IReadOnlyList<MediaFolderEntry> MediaEntries { get { lock (_sync) return _mediaEntries.ToArray(); } }
        public IReadOnlyList<CatalogReconciliationItem> ReconciliationItems { get { lock (_sync) return _reconciliationItems.ToArray(); } }
        public IReadOnlyList<IDerivedWorkBatch> DerivedWorkBatches { get { lock (_sync) return _derivedWorkBatches.ToArray(); } }
        public IReadOnlyList<string> Diagnostics { get { lock (_sync) return _diagnostics.ToArray(); } }
        public int UnsupportedCount { get { lock (_sync) return _unsupportedCount; } }

        /// <summary>Marks one reserved folder's own enumerate+reconcile attempt as finished (success or a recorded per-folder failure).</summary>
        public void MarkVisited() { lock (_sync) _visitedFolders++; }

        /// <summary>A consistent, same-instant snapshot of both progress counters — see <see cref="RecursiveScopeProgress"/>.</summary>
        public RecursiveScopeProgress Snapshot() { lock (_sync) return new(_reservedFolders, _visitedFolders); }

        /// <summary>
        /// Reserves one slot toward the safety cap on total visited folders. Once the cap is reached, further
        /// folders (and therefore their descendants) are simply never visited — a bounded, graceful
        /// truncation for pathologically large trees rather than an unbounded walk, noted once as a
        /// diagnostic rather than treated as an error.
        /// </summary>
        public bool TryReserveFolder()
        {
            lock (_sync)
            {
                if (_reservedFolders >= maximumFolders)
                {
                    if (!_truncated)
                    {
                        _truncated = true;
                        _diagnostics.Add(
                            $"This location has more than {maximumFolders} folders; some descendant media was not scanned.");
                    }
                    return false;
                }
                _reservedFolders++;
                return true;
            }
        }

        public void AddFolder(MediaFolderEnumerationResult listing, MediaDiscoveryRefreshResult refresh)
        {
            lock (_sync)
            {
                _mediaEntries.AddRange(listing.Entries.Where(entry => !entry.IsDirectory));
                _reconciliationItems.AddRange(refresh.Reconciliation.Items);
                _unsupportedCount += refresh.Reconciliation.UnsupportedCount;
                if (refresh.DerivedWork is { } batch) _derivedWorkBatches.Add(batch);
                if (listing.Diagnostic is { } diagnostic) _diagnostics.Add(diagnostic);
            }
        }

        public void AddDiagnostic(string diagnostic) { lock (_sync) _diagnostics.Add(diagnostic); }
    }
}

/// <summary>
/// Combines one <see cref="IDerivedWorkBatch"/> per visited folder (see <see cref="RecursiveMediaDiscoveryService"/>)
/// into the single batch #108's grid/thumbnail/metadata hydration already expects. This is the "aggregate/compound
/// derived-work representation" #124 calls for: it lives at this service/model boundary rather than as
/// recursive-specific branching scattered through <c>MainWindow</c>. Progress/results are recomputed from the
/// live children on every access (matching <see cref="DerivedWorkBatch"/>'s own snapshot-under-lock convention)
/// rather than duplicated/cached, so it can never drift from what the children actually report.
/// </summary>
internal sealed class AggregateDerivedWorkBatch : IDerivedWorkBatch
{
    private readonly IReadOnlyList<IDerivedWorkBatch> _children;
    private readonly Lazy<Task<DerivedWorkProgress>> _completion;

    public AggregateDerivedWorkBatch(CatalogReconciliationResult reconciliation, IReadOnlyList<IDerivedWorkBatch> children)
    {
        Reconciliation = reconciliation;
        _children = children;
        foreach (var child in _children) child.ProgressChanged += OnChildProgressChanged;
        _completion = new(async () =>
        {
            await Task.WhenAll(_children.Select(child => child.Completion)).ConfigureAwait(false);
            foreach (var child in _children) child.ProgressChanged -= OnChildProgressChanged;
            var final = Aggregate();
            RaiseProgressChanged(final);
            return final;
        });
    }

    public Guid BatchId { get; } = Guid.NewGuid();
    public CatalogReconciliationResult Reconciliation { get; }
    public DerivedWorkProgress Progress => Aggregate();
    public IReadOnlyList<DerivedWorkItemResult> Results => [.. _children.SelectMany(child => child.Results)];
    public Task<DerivedWorkProgress> Completion => _completion.Value;
    public event EventHandler<DerivedWorkProgress>? ProgressChanged;

    /// <summary>Cancels every child batch. Each child cancels through its own scheduler exactly as a single-folder batch would.</summary>
    public void Cancel() { foreach (var child in _children) child.Cancel(); }

    private void OnChildProgressChanged(object? sender, DerivedWorkProgress progress) => RaiseProgressChanged(Aggregate());

    private void RaiseProgressChanged(DerivedWorkProgress progress)
    {
        if (ProgressChanged is not { } handlers) return;
        foreach (EventHandler<DerivedWorkProgress> handler in handlers.GetInvocationList())
        {
            try { handler(this, progress); }
            catch { }
        }
    }

    private DerivedWorkProgress Aggregate()
    {
        if (_children.Count == 0) return new(BatchId, DerivedWorkBatchStatus.Completed, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        var snapshots = _children.Select(child => child.Progress).ToArray();
        var status = snapshots.Any(progress => progress.Status == DerivedWorkBatchStatus.Running)
            ? DerivedWorkBatchStatus.Running
            : snapshots.All(progress => progress.Status == DerivedWorkBatchStatus.Completed)
                ? DerivedWorkBatchStatus.Completed
                : DerivedWorkBatchStatus.Canceled;
        return new(BatchId, status,
            snapshots.Sum(progress => progress.Total), snapshots.Sum(progress => progress.Pending),
            snapshots.Sum(progress => progress.Running), snapshots.Sum(progress => progress.Completed),
            snapshots.Sum(progress => progress.Generated), snapshots.Sum(progress => progress.Current),
            snapshots.Sum(progress => progress.Failed), snapshots.Sum(progress => progress.Skipped),
            snapshots.Sum(progress => progress.Canceled));
    }
}
