using LightflowStudio;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class DerivedWorkSchedulingTests
{
    [Fact]
    public async Task NewAndChangedAssetsScheduleOnlyNeededMetadataAndThumbnails()
    {
        var first = Asset("video");
        var second = Asset("image");
        var assets = new FakeAssets(first, second);
        var previews = new FakePreviews();
        var metadata = new FakeMetadata();
        var thumbnails = new FakeThumbnails();
        await using var scheduler = new DerivedWorkScheduler(assets, previews, metadata, thumbnails);

        var batch = Schedule(scheduler, Reconciliation(
            (first.Asset.AssetId, CatalogReconciliationItemStatus.New),
            (second.Asset.AssetId, CatalogReconciliationItemStatus.Changed)));
        var progress = await batch.Completion;

        Assert.Equal(DerivedWorkBatchStatus.Completed, progress.Status);
        Assert.Equal(2, progress.Generated);
        Assert.Equal(2, metadata.Calls.Count);
        Assert.Equal(2, thumbnails.Calls.Count);
    }

    [Fact]
    public async Task UnchangedCurrentPreviewIsReusedWithoutGeneratorCalls()
    {
        var asset = Asset("video");
        var previews = new FakePreviews(CurrentPreview(asset.Asset));
        var metadata = new FakeMetadata();
        var thumbnails = new FakeThumbnails();
        await using var scheduler = new DerivedWorkScheduler(new FakeAssets(asset), previews, metadata, thumbnails);

        var batch = Schedule(scheduler, Reconciliation(
            (asset.Asset.AssetId, CatalogReconciliationItemStatus.Unchanged)));
        var progress = await batch.Completion;

        Assert.Equal(1, progress.Current);
        Assert.Empty(metadata.Calls);
        Assert.Empty(thumbnails.Calls);
    }

    [Theory]
    [InlineData(null, "persisted-color-b")]
    [InlineData("persisted-color-a", "persisted-color-b")]
    public async Task FreshBrowserLoad_SchedulesPersistedVisualIdentityMismatch(
        string? cachedVisualIdentity, string committedVisualIdentity)
    {
        var asset = Asset("video");
        var preview = CurrentPreview(asset.Asset) with
        {
            ThumbnailVisualIdentity = cachedVisualIdentity ?? PreviewVisualIdentity.Original
        };
        var thumbnails = new FakeThumbnails();
        var colors = new FakeColors(new(asset.Asset.AssetId,
            new(Guid.NewGuid(), "Persisted Camera", new string('a', 64), LutResourceAvailability.Available),
            null, committedVisualIdentity));
        await using var scheduler = new DerivedWorkScheduler(new FakeAssets(asset), new FakePreviews(preview),
            new FakeMetadata(), thumbnails, colors: colors);

        var batch = Schedule(scheduler, Reconciliation(
            (asset.Asset.AssetId, CatalogReconciliationItemStatus.Unchanged)), DerivedWorkPriority.Visible);
        await batch.Completion;

        var call = Assert.Single(thumbnails.Calls);
        Assert.Equal(asset.Asset.AssetId, call.AssetId);
        Assert.Equal(ThumbnailPriority.Visible, call.Priority);
    }

    [Fact]
    public async Task RepeatedRefreshesShareOneInFlightAssetWorkItem()
    {
        var asset = Asset("audio");
        var metadata = new FakeMetadata { Block = true };
        await using var scheduler = new DerivedWorkScheduler(new FakeAssets(asset), new FakePreviews(), metadata,
            new FakeThumbnails(), maximumConcurrency: 1);
        var result = Reconciliation((asset.Asset.AssetId, CatalogReconciliationItemStatus.New));

        var first = Schedule(scheduler, result);
        await metadata.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var repeated = Schedule(scheduler, result, DerivedWorkPriority.Visible);
        metadata.Release.TrySetResult();
        await Task.WhenAll(first.Completion, repeated.Completion);

        Assert.Single(metadata.Calls);
        Assert.Equal(DerivedWorkBatchStatus.Completed, first.Progress.Status);
        Assert.Equal(DerivedWorkBatchStatus.Completed, repeated.Progress.Status);
    }

    [Fact]
    public async Task VisibleQueuedWorkRunsBeforeEarlierBackgroundWork()
    {
        var blocker = Asset("audio");
        var background = Asset("audio");
        var visible = Asset("audio");
        var metadata = new FakeMetadata { BlockingAsset = blocker.Asset.AssetId };
        await using var scheduler = new DerivedWorkScheduler(new FakeAssets(blocker, background, visible),
            new FakePreviews(), metadata, new FakeThumbnails(), maximumConcurrency: 1);

        var running = Schedule(scheduler, Reconciliation((blocker.Asset.AssetId, CatalogReconciliationItemStatus.New)));
        await metadata.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var queuedBackground = Schedule(scheduler, Reconciliation(
            (background.Asset.AssetId, CatalogReconciliationItemStatus.New)), DerivedWorkPriority.Background);
        var queuedVisible = Schedule(scheduler, Reconciliation(
            (visible.Asset.AssetId, CatalogReconciliationItemStatus.New)), DerivedWorkPriority.Visible);
        metadata.Release.TrySetResult();
        await Task.WhenAll(running.Completion, queuedBackground.Completion, queuedVisible.Completion);

        Assert.Equal(new[] { blocker.Asset.AssetId, visible.Asset.AssetId, background.Asset.AssetId }, metadata.Calls);
    }

    [Fact]
    public async Task CanceledQueuedBatchDoesNotRunOrBlockLaterWork()
    {
        var blocker = Asset("audio");
        var canceled = Asset("audio");
        var later = Asset("audio");
        var metadata = new FakeMetadata { BlockingAsset = blocker.Asset.AssetId };
        await using var scheduler = new DerivedWorkScheduler(new FakeAssets(blocker, canceled, later),
            new FakePreviews(), metadata, new FakeThumbnails(), maximumConcurrency: 1);
        var running = Schedule(scheduler, Reconciliation((blocker.Asset.AssetId, CatalogReconciliationItemStatus.New)));
        await metadata.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var cancellation = new CancellationTokenSource();
        var canceledBatch = Schedule(scheduler, Reconciliation(
            (canceled.Asset.AssetId, CatalogReconciliationItemStatus.New)), cancellationToken: cancellation.Token);
        var laterBatch = Schedule(scheduler, Reconciliation((later.Asset.AssetId, CatalogReconciliationItemStatus.New)));

        cancellation.Cancel();
        metadata.Release.TrySetResult();
        await Task.WhenAll(running.Completion, canceledBatch.Completion, laterBatch.Completion);

        Assert.Equal(DerivedWorkBatchStatus.Canceled, canceledBatch.Progress.Status);
        Assert.DoesNotContain(canceled.Asset.AssetId, metadata.Calls);
        Assert.Contains(later.Asset.AssetId, metadata.Calls);
    }

    [Fact]
    public async Task SchedulerBoundsConcurrentAssetsWithoutUnboundedStarts()
    {
        var assets = Enumerable.Range(0, 6).Select(_ => Asset("audio")).ToArray();
        var metadata = new ConcurrencyMetadata(expectedMaximum: 2);
        await using var scheduler = new DerivedWorkScheduler(new FakeAssets(assets), new FakePreviews(), metadata,
            new FakeThumbnails(), maximumConcurrency: 2);

        var batch = Schedule(scheduler, Reconciliation(assets.Select(asset =>
            (asset.Asset.AssetId, CatalogReconciliationItemStatus.New)).ToArray()));
        await metadata.LimitReached.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(2, metadata.MaximumObserved);
        Assert.Equal(2, metadata.StartedCount);
        metadata.Release.TrySetResult();
        await batch.Completion;
        Assert.True(metadata.MaximumObserved <= 2);
    }

    [Fact]
    public async Task IndividualFailureIsIsolatedAndRetryable()
    {
        var failing = Asset("audio");
        var healthy = Asset("audio");
        var metadata = new FakeMetadata { FailingAsset = failing.Asset.AssetId };
        await using var scheduler = new DerivedWorkScheduler(new FakeAssets(failing, healthy),
            new FakePreviews(), metadata, new FakeThumbnails());

        var first = Schedule(scheduler, Reconciliation(
            (failing.Asset.AssetId, CatalogReconciliationItemStatus.New),
            (healthy.Asset.AssetId, CatalogReconciliationItemStatus.New)));
        var firstProgress = await first.Completion;
        metadata.FailingAsset = null;
        var retry = Schedule(scheduler, Reconciliation(
            (failing.Asset.AssetId, CatalogReconciliationItemStatus.Unchanged)));
        var retryProgress = await retry.Completion;

        Assert.Equal(1, firstProgress.Failed);
        Assert.Equal(1, firstProgress.Generated);
        Assert.Equal(1, retryProgress.Generated);
        Assert.Equal(2, metadata.Calls.Count(id => id == failing.Asset.AssetId));
    }

    [Fact]
    public async Task SourceChangedFailureDoesNotPreventThumbnailOrOtherAssets()
    {
        var changed = Asset("video");
        var healthy = Asset("video");
        var metadata = new FakeMetadata { SourceChangedAsset = changed.Asset.AssetId };
        var thumbnails = new FakeThumbnails();
        await using var scheduler = new DerivedWorkScheduler(new FakeAssets(changed, healthy),
            new FakePreviews(), metadata, thumbnails);

        var batch = Schedule(scheduler, Reconciliation(
            (changed.Asset.AssetId, CatalogReconciliationItemStatus.Changed),
            (healthy.Asset.AssetId, CatalogReconciliationItemStatus.New)));
        var progress = await batch.Completion;

        Assert.Equal(1, progress.Failed);
        Assert.Equal(1, progress.Generated);
        Assert.Contains(changed.Asset.AssetId, thumbnails.Calls.Select(call => call.AssetId));
        Assert.Contains(healthy.Asset.AssetId, thumbnails.Calls.Select(call => call.AssetId));
    }

    [Fact]
    public async Task UnexpectedMetadataExceptionDoesNotBlockThumbnailGeneration()
    {
        var asset = Asset("video");
        var metadata = new FakeMetadata { ThrowingAsset = asset.Asset.AssetId };
        var thumbnails = new FakeThumbnails();
        await using var scheduler = new DerivedWorkScheduler(new FakeAssets(asset), new FakePreviews(),
            metadata, thumbnails);

        var batch = Schedule(scheduler, Reconciliation(
            (asset.Asset.AssetId, CatalogReconciliationItemStatus.New)));
        var progress = await batch.Completion;

        Assert.Equal(1, progress.Failed);
        Assert.Single(thumbnails.Calls);
        Assert.Equal(DerivedWorkItemOutcome.PartialFailure, Assert.Single(batch.Results).Outcome);
    }

    [Fact]
    public async Task MissingAndOfflineAssetsRetainPreviewsWithoutGenerators()
    {
        var missing = Asset("video", MediaAssetSourceStatus.Missing, exists: false);
        var offline = Asset("video", availability: MediaRootAvailability.Unavailable, exists: false);
        var previews = new FakePreviews(CurrentPreview(missing.Asset), CurrentPreview(offline.Asset));
        var metadata = new FakeMetadata();
        var thumbnails = new FakeThumbnails();
        await using var scheduler = new DerivedWorkScheduler(new FakeAssets(missing, offline), previews,
            metadata, thumbnails);

        var batch = Schedule(scheduler, Reconciliation(
            (missing.Asset.AssetId, CatalogReconciliationItemStatus.Missing),
            (offline.Asset.AssetId, CatalogReconciliationItemStatus.Unchanged)));
        var progress = await batch.Completion;

        Assert.Equal(2, progress.Skipped);
        Assert.Empty(metadata.Calls);
        Assert.Empty(thumbnails.Calls);
        Assert.Equal(2, previews.Records.Count);
        Assert.All(previews.Records.Values, record => Assert.Equal(PreviewComponentState.Current, record.ThumbnailState));
    }

    [Fact]
    public async Task SchedulingReturnsBeforeBlockedDerivedWorkCompletesAndReportsProgress()
    {
        var asset = Asset("audio");
        var metadata = new FakeMetadata { Block = true };
        await using var scheduler = new DerivedWorkScheduler(new FakeAssets(asset), new FakePreviews(), metadata,
            new FakeThumbnails(), maximumConcurrency: 1);

        var reconciliation = Reconciliation((asset.Asset.AssetId, CatalogReconciliationItemStatus.New));
        var discovery = new MediaDiscoveryRefreshService(new FakeReconciliation(reconciliation), () => scheduler);
        var refreshed = await discovery.RefreshAsync(new(reconciliation.RootId));
        var batch = refreshed.DerivedWork!;
        await metadata.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Same(reconciliation, refreshed.Reconciliation);
        Assert.False(batch.Completion.IsCompleted);
        Assert.Equal(1, batch.Progress.Running);
        Assert.Equal(0, batch.Progress.Completed);
        metadata.Release.TrySetResult();
        Assert.Equal(DerivedWorkBatchStatus.Completed, (await batch.Completion).Status);
    }

    [Fact]
    public async Task SchedulerReplacementAfterReconciliationReturnsCatalogSuccessAndLaterRefreshCanRetry()
    {
        var asset = Asset("audio");
        var reconciliation = Reconciliation((asset.Asset.AssetId, CatalogReconciliationItemStatus.New));
        var originalMetadata = new FakeMetadata();
        var replacementMetadata = new FakeMetadata();
        await using var original = new DerivedWorkScheduler(new FakeAssets(asset), new FakePreviews(),
            originalMetadata, new FakeThumbnails());
        await using var replacement = new DerivedWorkScheduler(new FakeAssets(asset), new FakePreviews(),
            replacementMetadata, new FakeThumbnails());
        IDerivedWorkScheduler current = original;
        var schedulerSelected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowSubmission = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var selections = 0;
        var discovery = new MediaDiscoveryRefreshService(new FakeReconciliation(reconciliation), () =>
        {
            var selected = Volatile.Read(ref current);
            if (Interlocked.Increment(ref selections) == 1)
            {
                schedulerSelected.TrySetResult();
                allowSubmission.Task.GetAwaiter().GetResult();
            }
            return selected;
        });

        var racingRefresh = Task.Run(async () => await discovery.RefreshAsync(new(reconciliation.RootId)));
        await schedulerSelected.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await original.DisposeAsync();
        Volatile.Write(ref current, replacement);
        allowSubmission.TrySetResult();

        var raced = await racingRefresh;
        Assert.Same(reconciliation, raced.Reconciliation);
        Assert.True(raced.Reconciliation.Succeeded);
        Assert.Null(raced.DerivedWork);
        Assert.Contains("storage", raced.Diagnostic, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(originalMetadata.Calls);

        var retried = await discovery.RefreshAsync(new(reconciliation.RootId));
        Assert.NotNull(retried.DerivedWork);
        Assert.Equal(DerivedWorkBatchStatus.Completed, (await retried.DerivedWork.Completion).Status);
        Assert.Single(replacementMetadata.Calls);
    }

    private static CatalogReconciliationResult Reconciliation(
        params (Guid AssetId, CatalogReconciliationItemStatus Status)[] items) =>
        new(CatalogReconciliationStatus.Succeeded, Guid.NewGuid(), "",
            items.Select(item => new CatalogReconciliationItem(item.AssetId, $"{item.AssetId:N}.media", item.Status)).ToArray());

    private static IDerivedWorkBatch Schedule(DerivedWorkScheduler scheduler,
        CatalogReconciliationResult reconciliation,
        DerivedWorkPriority priority = DerivedWorkPriority.Background,
        CancellationToken cancellationToken = default)
    {
        var result = scheduler.TrySchedule(reconciliation, priority, cancellationToken);
        Assert.True(result.Accepted, result.Diagnostic);
        return result.Batch!;
    }

    private static MediaAssetResolution Asset(string mediaType,
        MediaAssetSourceStatus sourceStatus = MediaAssetSourceStatus.Available,
        MediaRootAvailability availability = MediaRootAvailability.Online, bool exists = true)
    {
        var assetId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var asset = new MediaAsset(assetId, Guid.NewGuid(), $"{assetId:N}.media", assetId.ToString("N"),
            mediaType, 10, now.UtcTicks, new(1, Convert.ToHexString(assetId.ToByteArray()).ToLowerInvariant()),
            sourceStatus, now, now, now);
        return new(asset, availability, exists ? $"C:\\media\\{assetId:N}.media" : null, exists);
    }

    private static PreviewRecord CurrentPreview(MediaAsset asset)
    {
        var source = new PreviewSourceIdentity(asset.FileSizeBytes, asset.LastWriteUtcTicks,
            asset.Fingerprint!.Version, asset.Fingerprint.Value);
        return new(asset.AssetId, source, PreviewSourceAvailability.Available,
            DerivedMediaMetadataService.CurrentProbeVersion, PreviewComponentState.Current, "{}", "{}",
            ThumbnailGenerationService.CurrentGeneratorVersion, PreviewComponentState.Current, "thumbnail.jpg",
            null, PreviewComponentState.Missing, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
    }

    private sealed class FakeReconciliation(CatalogReconciliationResult result) : ICatalogReconciliationService
    {
        public Task<CatalogReconciliationResult> ReconcileAsync(MediaFolderEnumerationRequest request,
            CancellationToken cancellationToken = default) => Task.FromResult(result);
    }

    private sealed class FakeAssets(params MediaAssetResolution[] assets) : IMediaAssetService
    {
        private readonly Dictionary<Guid, MediaAssetResolution> _assets = assets.ToDictionary(asset => asset.Asset.AssetId);
        public Task<MediaAssetResolution?> GetAsync(Guid assetId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_assets.GetValueOrDefault(assetId));
        public Task<IReadOnlyList<MediaAsset>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MediaAsset>>(_assets.Values.Select(value => value.Asset).ToArray());
        public Task<MediaAssetOperationResult> CreateAsync(Guid rootId, string relativePath, string mediaType = "unknown", CancellationToken cancellationToken = default) => throw new InvalidOperationException("The scheduler must not create Catalog assets.");
        public Task<MediaAssetResolution?> FindAsync(Guid rootId, string relativePath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MediaAssetOperationResult> ObserveAsync(Guid assetId, CancellationToken cancellationToken = default) => throw new InvalidOperationException("The scheduler must leave source observation to existing generators.");
        public Task<int> MarkMissingAsync(IReadOnlyCollection<Guid> assetIds, CancellationToken cancellationToken = default) => throw new InvalidOperationException("The scheduler must not mutate missing Catalog state.");
    }

    private sealed class FakePreviews(params PreviewRecord[] records) : IPreviewStoreService
    {
        public Dictionary<Guid, PreviewRecord> Records { get; } = records.ToDictionary(record => record.AssetId);
        public Task<PreviewRecord?> GetAsync(Guid assetId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Records.GetValueOrDefault(assetId));
        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<PreviewRecord>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<PreviewRecord>>(Records.Values.ToArray());
        public Task<PreviewRecord> ObserveSourceAsync(Guid assetId, PreviewSourceIdentity source, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PreviewRecord?> SetSourceAvailabilityAsync(Guid assetId, PreviewSourceAvailability availability, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PreviewRecord?> SetMetadataAsync(Guid assetId, PreviewComponentUpdate update, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PreviewRecord?> ClearMetadataAsync(Guid assetId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PreviewRecord?> SetArtifactAsync(Guid assetId, PreviewArtifactKind kind, PreviewComponentUpdate update, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PreviewRecord?> ClearArtifactAsync(Guid assetId, PreviewArtifactKind kind, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ClearAllAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public string GetArtifactPath(Guid assetId, PreviewArtifactKind kind, int generatorVersion, PreviewSourceIdentity source, string extension) => throw new NotSupportedException();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeMetadata : IDerivedMediaMetadataService
    {
        public List<Guid> Calls { get; } = [];
        public Guid? BlockingAsset { get; set; }
        public Guid? FailingAsset { get; set; }
        public Guid? SourceChangedAsset { get; set; }
        public Guid? ThrowingAsset { get; set; }
        public bool Block { get; set; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<DerivedMetadataResult> ProbeAsync(Guid assetId, bool forceRefresh = false,
            CancellationToken cancellationToken = default)
        {
            lock (Calls) Calls.Add(assetId);
            if (Block || BlockingAsset == assetId)
            {
                Started.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);
            }
            if (FailingAsset == assetId) return new(DerivedMetadataStatus.Failed, Diagnostic: "probe failed");
            if (SourceChangedAsset == assetId) return new(DerivedMetadataStatus.SourceChanged, Diagnostic: "source changed");
            if (ThrowingAsset == assetId) throw new InvalidOperationException("unexpected probe exception");
            return new(DerivedMetadataStatus.Succeeded);
        }
        public void Dispose() { }
    }

    private sealed class FakeThumbnails : IThumbnailGenerationService
    {
        public List<ThumbnailRequest> Calls { get; } = [];
        public Task<ThumbnailGenerationResult> GenerateAsync(ThumbnailRequest request,
            CancellationToken cancellationToken = default)
        {
            lock (Calls) Calls.Add(request);
            return Task.FromResult(new ThumbnailGenerationResult(ThumbnailGenerationStatus.Succeeded));
        }
        public void Dispose() { }
    }

    private sealed class FakeColors(AssetColorIntent intent) : IAssetColorStore
    {
        public Task<AssetColorIntent> GetAsync(Guid assetId, CancellationToken cancellationToken = default) =>
            Task.FromResult(intent);
        public Task<IReadOnlyDictionary<Guid, AssetColorIntent>> GetAsync(IReadOnlyCollection<Guid> assetIds,
            CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyDictionary<Guid, AssetColorIntent>>(
                assetIds.ToDictionary(id => id, _ => intent));
        public Task SetStageAsync(IReadOnlyCollection<Guid> assetIds, ColorLutStage stage, Guid? lutId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SetAsync(IReadOnlyCollection<ColorAssignmentChange> changes,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class ConcurrencyMetadata(int expectedMaximum) : IDerivedMediaMetadataService
    {
        private int _active;
        private int _maximum;
        private int _started;
        public int MaximumObserved => Volatile.Read(ref _maximum);
        public int StartedCount => Volatile.Read(ref _started);
        public TaskCompletionSource LimitReached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<DerivedMetadataResult> ProbeAsync(Guid assetId, bool forceRefresh = false,
            CancellationToken cancellationToken = default)
        {
            var active = Interlocked.Increment(ref _active);
            Interlocked.Increment(ref _started);
            while (true)
            {
                var current = Volatile.Read(ref _maximum);
                if (active <= current || Interlocked.CompareExchange(ref _maximum, active, current) == current) break;
            }
            if (active == expectedMaximum) LimitReached.TrySetResult();
            try { await Release.Task.WaitAsync(cancellationToken); }
            finally { Interlocked.Decrement(ref _active); }
            return new(DerivedMetadataStatus.Succeeded);
        }
        public void Dispose() { }
    }
}
