namespace LightflowStudio;

internal enum DerivedWorkPriority { Background, Normal, Visible }
internal enum DerivedWorkBatchStatus { Running, Completed, Canceled }
internal enum DerivedWorkItemOutcome { Generated, Current, PartialFailure, Failed, SkippedUnavailable, Canceled }
internal enum DerivedWorkComponentOutcome { NotNeeded, Succeeded, Current, SkippedUnavailable, Failed, Canceled }

internal sealed record DerivedWorkItemResult(
    Guid AssetId,
    DerivedWorkItemOutcome Outcome,
    DerivedWorkComponentOutcome Metadata,
    DerivedWorkComponentOutcome Thumbnail,
    string? Diagnostic = null);

internal sealed record DerivedWorkProgress(
    Guid BatchId,
    DerivedWorkBatchStatus Status,
    int Total,
    int Pending,
    int Running,
    int Completed,
    int Generated,
    int Current,
    int Failed,
    int Skipped,
    int Canceled);

internal interface IDerivedWorkBatch
{
    Guid BatchId { get; }
    CatalogReconciliationResult Reconciliation { get; }
    DerivedWorkProgress Progress { get; }
    IReadOnlyList<DerivedWorkItemResult> Results { get; }
    Task<DerivedWorkProgress> Completion { get; }
    event EventHandler<DerivedWorkProgress>? ProgressChanged;
    void Cancel();
}

internal interface IDerivedWorkScheduler : IAsyncDisposable
{
    DerivedWorkSchedulingResult TrySchedule(CatalogReconciliationResult reconciliation,
        DerivedWorkPriority priority = DerivedWorkPriority.Background,
        CancellationToken cancellationToken = default);
}

internal enum DerivedWorkSchedulingStatus { Accepted, SchedulerUnavailable }

internal sealed record DerivedWorkSchedulingResult(
    DerivedWorkSchedulingStatus Status,
    IDerivedWorkBatch? Batch = null,
    string? Diagnostic = null)
{
    public bool Accepted => Status == DerivedWorkSchedulingStatus.Accepted && Batch is not null;
}

internal sealed record MediaDiscoveryRefreshResult(
    CatalogReconciliationResult Reconciliation,
    IDerivedWorkBatch? DerivedWork,
    string? Diagnostic = null);

internal interface IMediaDiscoveryRefreshService
{
    Task<MediaDiscoveryRefreshResult> RefreshAsync(MediaFolderEnumerationRequest request,
        DerivedWorkPriority priority = DerivedWorkPriority.Background,
        CancellationToken cancellationToken = default,
        CancellationToken derivedWorkCancellationToken = default);
}

internal sealed class MediaDiscoveryRefreshService(
    ICatalogReconciliationService reconciliation,
    Func<IDerivedWorkScheduler?> scheduler) : IMediaDiscoveryRefreshService
{
    public async Task<MediaDiscoveryRefreshResult> RefreshAsync(MediaFolderEnumerationRequest request,
        DerivedWorkPriority priority = DerivedWorkPriority.Background,
        CancellationToken cancellationToken = default,
        CancellationToken derivedWorkCancellationToken = default)
    {
        var result = await reconciliation.ReconcileAsync(request, cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded) return new(result, null, result.Diagnostic);
        var activeScheduler = scheduler();
        if (activeScheduler is null)
            return new(result, null, "Preview storage is unavailable; Catalog reconciliation completed without derived work.");
        var scheduled = activeScheduler.TrySchedule(result, priority, derivedWorkCancellationToken);
        return scheduled.Accepted
            ? new(result, scheduled.Batch)
            : new(result, null, scheduled.Diagnostic ??
                "Preview work was not scheduled because storage lifecycle is changing. Refresh again later.");
    }
}

/// <summary>
/// Discovery-specific derived-work queue. Catalog reconciliation is already complete when work is submitted;
/// fixed workers then inspect rebuildable Preview state and invoke the existing metadata/thumbnail services.
/// </summary>
internal sealed class DerivedWorkScheduler : IDerivedWorkScheduler
{
    private readonly IMediaAssetService _assets;
    private readonly IPreviewStoreService _previews;
    private readonly IDerivedMediaMetadataService _metadata;
    private readonly IThumbnailGenerationService _thumbnails;
    private readonly IPreviewOperationCoordinator? _operations;
    private readonly IAssetColorStore? _colors;
    private readonly bool _ownsGenerators;
    private readonly object _sync = new();
    private readonly Queue<QueueEntry> _visible = new();
    private readonly Queue<QueueEntry> _normal = new();
    private readonly Queue<QueueEntry> _background = new();
    private readonly Dictionary<Guid, WorkItem> _work = [];
    private readonly SemaphoreSlim _available = new(0);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task[] _workers;
    private bool _disposed;

    public DerivedWorkScheduler(IMediaAssetService assets, IPreviewStoreService previews,
        IDerivedMediaMetadataService metadata, IThumbnailGenerationService thumbnails,
        int maximumConcurrency = 2, bool ownsGenerators = false,
        IPreviewOperationCoordinator? operations = null, IAssetColorStore? colors = null)
    {
        if (maximumConcurrency <= 0) throw new ArgumentOutOfRangeException(nameof(maximumConcurrency));
        _assets = assets;
        _previews = previews;
        _metadata = metadata;
        _thumbnails = thumbnails;
        _operations = operations;
        _colors = colors;
        _ownsGenerators = ownsGenerators;
        _workers = Enumerable.Range(0, maximumConcurrency)
            .Select(_ => Task.Run(WorkerAsync))
            .ToArray();
    }

    public DerivedWorkSchedulingResult TrySchedule(CatalogReconciliationResult reconciliation,
        DerivedWorkPriority priority = DerivedWorkPriority.Background,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reconciliation);
        var batch = new DerivedWorkBatch(reconciliation, CancelBatch);
        if (!reconciliation.Succeeded)
        {
            batch.CompleteEmpty();
            return new(DerivedWorkSchedulingStatus.Accepted, batch);
        }

        lock (_sync)
        {
            if (_disposed)
                return new(DerivedWorkSchedulingStatus.SchedulerUnavailable, Diagnostic:
                    "Preview work was not scheduled because storage or the scheduler is being replaced or shut down.");
            foreach (var item in reconciliation.Items)
            {
                if (item.Status == CatalogReconciliationItemStatus.Missing)
                {
                    batch.AddImmediate(new(item.AssetId, DerivedWorkItemOutcome.SkippedUnavailable,
                        DerivedWorkComponentOutcome.SkippedUnavailable,
                        DerivedWorkComponentOutcome.SkippedUnavailable,
                        "The source is missing; existing Preview data was retained."));
                    continue;
                }

                batch.AddPending(item.AssetId);
                if (_work.TryGetValue(item.AssetId, out var existing))
                {
                    existing.Batches.Add(batch);
                    if (existing.State == WorkState.Queued && priority > existing.Priority)
                    {
                        existing.Priority = priority;
                        existing.Generation++;
                        Enqueue(existing);
                    }
                    continue;
                }

                var work = new WorkItem(item.AssetId, priority, _shutdown.Token);
                work.Batches.Add(batch);
                _work.Add(item.AssetId, work);
                Enqueue(work);
            }
            batch.Seal();
        }

        if (cancellationToken.CanBeCanceled)
            batch.SetCancellation(cancellationToken.Register(static state => ((DerivedWorkBatch)state!).Cancel(), batch));
        return new(DerivedWorkSchedulingStatus.Accepted, batch);
    }

    private void Enqueue(WorkItem item)
    {
        Queue(item.Priority).Enqueue(new(item.AssetId, item.Generation));
        _available.Release();
    }

    private async Task WorkerAsync()
    {
        while (true)
        {
            try { await _available.WaitAsync(_shutdown.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }

            WorkItem? item = null;
            DerivedWorkBatch[] startingBatches = [];
            lock (_sync)
            {
                while (TryDequeue(out var entry))
                {
                    if (!_work.TryGetValue(entry.AssetId, out var candidate) ||
                        candidate.State != WorkState.Queued || candidate.Generation != entry.Generation)
                        continue;
                    candidate.State = WorkState.Running;
                    item = candidate;
                    startingBatches = candidate.Batches.ToArray();
                    break;
                }
            }
            if (item is null) continue;
            foreach (var batch in startingBatches) batch.MarkRunning(item.AssetId);

            DerivedWorkItemResult result;
            try { result = await ProcessAsync(item.AssetId, item.Priority, item.Cancellation.Token).ConfigureAwait(false); }
            catch (OperationCanceledException)
            {
                result = new(item.AssetId, DerivedWorkItemOutcome.Canceled,
                    DerivedWorkComponentOutcome.Canceled, DerivedWorkComponentOutcome.Canceled);
            }
            catch (Exception exception)
            {
                result = new(item.AssetId, DerivedWorkItemOutcome.Failed,
                    DerivedWorkComponentOutcome.Failed, DerivedWorkComponentOutcome.Failed, exception.Message);
            }

            DerivedWorkBatch[] completedBatches;
            lock (_sync) completedBatches = _work.Remove(item.AssetId, out var completed)
                ? completed.Batches.ToArray()
                : [];
            foreach (var batch in completedBatches) batch.Complete(item.AssetId, result);
            item.Cancellation.Dispose();
        }
    }

    private async Task<DerivedWorkItemResult> ProcessAsync(Guid assetId, DerivedWorkPriority priority,
        CancellationToken cancellationToken)
    {
        var resolved = await _assets.GetAsync(assetId, cancellationToken).ConfigureAwait(false);
        if (resolved is null)
            return Failure(assetId, "The Catalog asset no longer exists.");
        if (resolved.Asset.SourceStatus == MediaAssetSourceStatus.Missing ||
            resolved.RootAvailability != MediaRootAvailability.Online || !resolved.SourceExists ||
            resolved.Asset.Fingerprint is null)
            return new(assetId, DerivedWorkItemOutcome.SkippedUnavailable,
                DerivedWorkComponentOutcome.SkippedUnavailable,
                DerivedWorkComponentOutcome.SkippedUnavailable,
                resolved.Diagnostic ?? "The source is currently unavailable; existing Preview data was retained.");

        var source = new PreviewSourceIdentity(resolved.Asset.FileSizeBytes, resolved.Asset.LastWriteUtcTicks,
            resolved.Asset.Fingerprint.Version, resolved.Asset.Fingerprint.Value);
        PreviewRecord? preview;
        using (var previewLease = _operations is null ? null :
            await _operations.EnterOperationAsync(cancellationToken).ConfigureAwait(false))
            preview = await _previews.GetAsync(assetId, cancellationToken).ConfigureAwait(false);
        var sameSource = preview is not null && SameSource(preview.Source, source);
        var needsMetadata = !sameSource || preview!.MetadataState != PreviewComponentState.Current ||
            preview.MetadataProbeVersion != DerivedMediaMetadataService.CurrentProbeVersion;
        var supportsThumbnail = resolved.Asset.MediaType is "image" or "video";
        var visualIdentity = PreviewVisualIdentity.Original;
        if (_colors is not null && string.Equals(resolved.Asset.MediaType, "video", StringComparison.OrdinalIgnoreCase))
        {
            var color = await _colors.GetAsync(assetId, cancellationToken).ConfigureAwait(false);
            visualIdentity = color.HasColor ? color.ColorIdentity : PreviewVisualIdentity.Original;
        }
        var needsThumbnail = supportsThumbnail && (!sameSource || preview!.ThumbnailState != PreviewComponentState.Current ||
            preview.ThumbnailGeneratorVersion != ThumbnailGenerationService.CurrentGeneratorVersion ||
            !string.Equals(preview.ThumbnailVisualIdentity ?? PreviewVisualIdentity.Original, visualIdentity, StringComparison.Ordinal));

        var metadata = DerivedWorkComponentOutcome.NotNeeded;
        var thumbnail = DerivedWorkComponentOutcome.NotNeeded;
        var diagnostics = new List<string>();
        if (needsMetadata)
        {
            try
            {
                var result = await _metadata.ProbeAsync(assetId, cancellationToken: cancellationToken).ConfigureAwait(false);
                metadata = Map(result.Status);
                if (!result.Succeeded && result.Status is not DerivedMetadataStatus.RootUnavailable and not DerivedMetadataStatus.SourceMissing)
                    diagnostics.Add(result.Diagnostic ?? $"Metadata: {result.Status}");
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception)
            {
                metadata = DerivedWorkComponentOutcome.Failed;
                diagnostics.Add($"Metadata: {exception.Message}");
            }
        }
        if (needsThumbnail)
        {
            try
            {
                var result = await _thumbnails.GenerateAsync(new(assetId, Priority: Map(priority)), cancellationToken)
                    .ConfigureAwait(false);
                thumbnail = Map(result.Status);
                if (!result.Succeeded && result.Status is not ThumbnailGenerationStatus.RootUnavailable and not ThumbnailGenerationStatus.SourceMissing)
                    diagnostics.Add(result.Diagnostic ?? $"Preview: {result.Status}");
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception)
            {
                thumbnail = DerivedWorkComponentOutcome.Failed;
                diagnostics.Add($"Preview: {exception.Message}");
            }
        }

        if (metadata == DerivedWorkComponentOutcome.SkippedUnavailable ||
            thumbnail == DerivedWorkComponentOutcome.SkippedUnavailable)
            return new(assetId, DerivedWorkItemOutcome.SkippedUnavailable, metadata, thumbnail,
                diagnostics.Count == 0 ? "The source became unavailable; existing Preview data was retained." : string.Join(" ", diagnostics));

        var attempted = needsMetadata || needsThumbnail;
        var failures = metadata == DerivedWorkComponentOutcome.Failed || thumbnail == DerivedWorkComponentOutcome.Failed;
        var generated = metadata == DerivedWorkComponentOutcome.Succeeded || thumbnail == DerivedWorkComponentOutcome.Succeeded;
        var successes = generated ||
            metadata == DerivedWorkComponentOutcome.Current || thumbnail == DerivedWorkComponentOutcome.Current;
        var outcome = !attempted ? DerivedWorkItemOutcome.Current
            : failures && successes ? DerivedWorkItemOutcome.PartialFailure
            : failures ? DerivedWorkItemOutcome.Failed
            : generated ? DerivedWorkItemOutcome.Generated
            : DerivedWorkItemOutcome.Current;
        return new(assetId, outcome, metadata, thumbnail,
            diagnostics.Count == 0 ? null : string.Join(" ", diagnostics));
    }

    private static ThumbnailPriority Map(DerivedWorkPriority priority) => priority switch
    {
        DerivedWorkPriority.Visible => ThumbnailPriority.Visible,
        DerivedWorkPriority.Normal => ThumbnailPriority.Normal,
        _ => ThumbnailPriority.Background
    };

    private static DerivedWorkItemResult Failure(Guid assetId, string diagnostic) =>
        new(assetId, DerivedWorkItemOutcome.Failed, DerivedWorkComponentOutcome.Failed,
            DerivedWorkComponentOutcome.Failed, diagnostic);

    private static DerivedWorkComponentOutcome Map(DerivedMetadataStatus status) => status switch
    {
        DerivedMetadataStatus.Succeeded => DerivedWorkComponentOutcome.Succeeded,
        DerivedMetadataStatus.Current => DerivedWorkComponentOutcome.Current,
        DerivedMetadataStatus.RootUnavailable or DerivedMetadataStatus.SourceMissing => DerivedWorkComponentOutcome.SkippedUnavailable,
        _ => DerivedWorkComponentOutcome.Failed
    };

    private static DerivedWorkComponentOutcome Map(ThumbnailGenerationStatus status) => status switch
    {
        ThumbnailGenerationStatus.Succeeded => DerivedWorkComponentOutcome.Succeeded,
        ThumbnailGenerationStatus.Current => DerivedWorkComponentOutcome.Current,
        ThumbnailGenerationStatus.RootUnavailable or ThumbnailGenerationStatus.SourceMissing => DerivedWorkComponentOutcome.SkippedUnavailable,
        _ => DerivedWorkComponentOutcome.Failed
    };

    private static bool SameSource(PreviewSourceIdentity left, PreviewSourceIdentity right) =>
        left.FileSizeBytes == right.FileSizeBytes && left.LastWriteUtcTicks == right.LastWriteUtcTicks &&
        left.FingerprintVersion == right.FingerprintVersion &&
        string.Equals(left.Fingerprint, right.Fingerprint, StringComparison.OrdinalIgnoreCase);

    private bool TryDequeue(out QueueEntry entry)
    {
        if (_visible.TryDequeue(out entry) || _normal.TryDequeue(out entry) || _background.TryDequeue(out entry))
            return true;
        entry = default;
        return false;
    }

    private Queue<QueueEntry> Queue(DerivedWorkPriority priority) => priority switch
    {
        DerivedWorkPriority.Visible => _visible,
        DerivedWorkPriority.Normal => _normal,
        _ => _background
    };

    private void CancelBatch(DerivedWorkBatch batch)
    {
        var affected = batch.CancelPending();
        lock (_sync)
        {
            foreach (var assetId in affected)
            {
                if (!_work.TryGetValue(assetId, out var item)) continue;
                item.Batches.Remove(batch);
                if (item.Batches.Count != 0) continue;
                if (item.State == WorkState.Running) item.Cancellation.Cancel();
                else if (_work.Remove(assetId)) item.Cancellation.Dispose();
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        DerivedWorkBatch[] batches;
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            _shutdown.Cancel();
            foreach (var item in _work.Values) item.Cancellation.Cancel();
            batches = _work.Values.SelectMany(item => item.Batches).Distinct().ToArray();
        }
        foreach (var batch in batches) batch.CancelPending();
        _available.Release(_workers.Length);
        try { await Task.WhenAll(_workers).ConfigureAwait(false); } catch (OperationCanceledException) { }
        lock (_sync) _work.Clear();
        if (_ownsGenerators)
        {
            _thumbnails.Dispose();
            _metadata.Dispose();
        }
        _available.Dispose();
        _shutdown.Dispose();
    }

    private enum WorkState { Queued, Running }
    private readonly record struct QueueEntry(Guid AssetId, int Generation);
    private sealed class WorkItem(Guid assetId, DerivedWorkPriority priority, CancellationToken shutdown)
    {
        public Guid AssetId { get; } = assetId;
        public DerivedWorkPriority Priority { get; set; } = priority;
        public int Generation { get; set; }
        public WorkState State { get; set; }
        public HashSet<DerivedWorkBatch> Batches { get; } = [];
        public CancellationTokenSource Cancellation { get; } = CancellationTokenSource.CreateLinkedTokenSource(shutdown);
    }
}

internal sealed class DerivedWorkBatch : IDerivedWorkBatch
{
    private readonly object _sync = new();
    private readonly Action<DerivedWorkBatch> _cancel;
    private readonly HashSet<Guid> _pending = [];
    private readonly HashSet<Guid> _running = [];
    private readonly List<DerivedWorkItemResult> _results = [];
    private readonly TaskCompletionSource<DerivedWorkProgress> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private CancellationTokenRegistration _cancellation;
    private bool _sealed;
    private bool _canceled;

    public DerivedWorkBatch(CatalogReconciliationResult reconciliation, Action<DerivedWorkBatch> cancel)
    {
        Reconciliation = reconciliation;
        _cancel = cancel;
    }

    public Guid BatchId { get; } = Guid.NewGuid();
    public CatalogReconciliationResult Reconciliation { get; }
    public DerivedWorkProgress Progress { get { lock (_sync) return Snapshot(); } }
    public IReadOnlyList<DerivedWorkItemResult> Results { get { lock (_sync) return _results.ToArray(); } }
    public Task<DerivedWorkProgress> Completion => _completion.Task;
    public event EventHandler<DerivedWorkProgress>? ProgressChanged;

    internal void AddPending(Guid assetId) { lock (_sync) _pending.Add(assetId); }
    internal void AddImmediate(DerivedWorkItemResult result) { lock (_sync) _results.Add(result); }
    internal void MarkRunning(Guid assetId)
    {
        lock (_sync)
        {
            if (_canceled || !_pending.Remove(assetId)) return;
            _running.Add(assetId);
            Publish();
        }
    }

    internal void Complete(Guid assetId, DerivedWorkItemResult result)
    {
        lock (_sync)
        {
            if (_canceled || (!_pending.Remove(assetId) && !_running.Remove(assetId))) return;
            _results.Add(result);
            Publish();
            TryFinish();
        }
    }

    internal IReadOnlyList<Guid> CancelPending()
    {
        lock (_sync)
        {
            if (_canceled || _completion.Task.IsCompleted) return [];
            _canceled = true;
            var affected = _pending.Concat(_running).ToArray();
            foreach (var assetId in affected)
                _results.Add(new(assetId, DerivedWorkItemOutcome.Canceled,
                    DerivedWorkComponentOutcome.Canceled, DerivedWorkComponentOutcome.Canceled));
            _pending.Clear();
            _running.Clear();
            Publish();
            Finish();
            return affected;
        }
    }

    internal void Seal() { lock (_sync) { _sealed = true; Publish(); TryFinish(); } }
    internal void CompleteEmpty() { lock (_sync) { _sealed = true; Publish(); TryFinish(); } }
    internal void SetCancellation(CancellationTokenRegistration registration)
    {
        lock (_sync)
        {
            if (_completion.Task.IsCompleted) registration.Dispose();
            else _cancellation = registration;
        }
    }

    public void Cancel() => _cancel(this);

    private void TryFinish()
    {
        if (_sealed && _pending.Count == 0 && _running.Count == 0) Finish();
    }

    private void Finish()
    {
        var progress = Snapshot();
        _cancellation.Unregister();
        _completion.TrySetResult(progress);
    }

    private void Publish()
    {
        var progress = Snapshot();
        if (ProgressChanged is not { } handlers) return;
        foreach (EventHandler<DerivedWorkProgress> handler in handlers.GetInvocationList())
        {
            try { handler(this, progress); }
            catch { }
        }
    }

    private DerivedWorkProgress Snapshot()
    {
        var completed = _results.Count;
        var status = _canceled ? DerivedWorkBatchStatus.Canceled
            : _sealed && _pending.Count == 0 && _running.Count == 0 ? DerivedWorkBatchStatus.Completed
            : DerivedWorkBatchStatus.Running;
        return new(BatchId, status, completed + _pending.Count + _running.Count,
            _pending.Count, _running.Count, completed,
            _results.Count(item => item.Outcome == DerivedWorkItemOutcome.Generated),
            _results.Count(item => item.Outcome == DerivedWorkItemOutcome.Current),
            _results.Count(item => item.Outcome is DerivedWorkItemOutcome.Failed or DerivedWorkItemOutcome.PartialFailure),
            _results.Count(item => item.Outcome == DerivedWorkItemOutcome.SkippedUnavailable),
            _results.Count(item => item.Outcome == DerivedWorkItemOutcome.Canceled));
    }
}
