using Xunit;

namespace LightflowStudio.Tests;

public sealed class BrowserRecursiveScopeTests
{
    [Fact]
    public async Task DiscoverAsync_IncludesBaseAndDescendantMediaButExcludesDirectoriesThemselves()
    {
        var rootId = Guid.NewGuid();
        var folders = new MapFolders(new()
        {
            [""] = Listing(rootId, "", Dir(rootId, "A"), FileEntry(rootId, "root.mp4")),
            ["A"] = Listing(rootId, "A", Dir(rootId, "A/B"), FileEntry(rootId, "A/clip.mp4")),
            ["A/B"] = Listing(rootId, "A/B", FileEntry(rootId, "A/B/deep.mp4")),
        });
        var discovery = new MapDiscovery(rootId);
        var service = new RecursiveMediaDiscoveryService(folders, discovery);

        var result = await service.DiscoverAsync(new(rootId, null));

        Assert.True(result.Succeeded);
        Assert.Equal(["A/B/deep.mp4", "A/clip.mp4", "root.mp4"],
            result.MediaEntries.Select(entry => entry.RelativePath).OrderBy(path => path, StringComparer.Ordinal));
        Assert.DoesNotContain(result.MediaEntries, entry => entry.IsDirectory);
    }

    [Fact]
    public async Task DiscoverAsync_ReconcilesEveryVisitedFolderExactlyOnceThroughTheExistingDiscoveryPipeline()
    {
        // Proves there is no second/duplicate reconciliation path: each folder is enumerated once for the
        // candidate listing and reconciled exactly once through the same IMediaDiscoveryRefreshService a
        // direct-folder view already uses.
        var rootId = Guid.NewGuid();
        var folders = new MapFolders(new()
        {
            [""] = Listing(rootId, "", Dir(rootId, "A"), Dir(rootId, "B")),
            ["A"] = Listing(rootId, "A", FileEntry(rootId, "A/clip.mp4")),
            ["B"] = Listing(rootId, "B", FileEntry(rootId, "B/clip.mp4")),
        });
        var discovery = new MapDiscovery(rootId);
        var service = new RecursiveMediaDiscoveryService(folders, discovery);

        var result = await service.DiscoverAsync(new(rootId, null));

        Assert.True(result.Succeeded);
        Assert.Equal(["", "A", "B"], discovery.Requests.OrderBy(folder => folder, StringComparer.Ordinal));
        Assert.Equal(discovery.Requests.Count, discovery.Requests.Distinct().Count());
    }

    [Fact]
    public async Task DiscoverAsync_AggregatesReconciliationItemsFromEveryVisitedFolder()
    {
        var rootId = Guid.NewGuid();
        var baseAsset = Guid.NewGuid();
        var childAsset = Guid.NewGuid();
        var folders = new MapFolders(new()
        {
            [""] = Listing(rootId, "", Dir(rootId, "A"), FileEntry(rootId, "root.mp4")),
            ["A"] = Listing(rootId, "A", FileEntry(rootId, "A/clip.mp4")),
        });
        var discovery = new MapDiscovery(rootId, new()
        {
            [""] = Refresh(rootId, "", Item(baseAsset, "root.mp4")),
            ["A"] = Refresh(rootId, "A", Item(childAsset, "A/clip.mp4")),
        });
        var service = new RecursiveMediaDiscoveryService(folders, discovery);

        var result = await service.DiscoverAsync(new(rootId, null));

        Assert.True(result.Succeeded);
        // Consumed by BrowserGridModel.ApplyAssetIdentities exactly like a single folder's reconciliation.
        var derivedWork = Assert.IsType<AggregateDerivedWorkBatch>(result.DerivedWork);
        Assert.Equal(new[] { baseAsset, childAsset }.OrderBy(id => id),
            derivedWork.Reconciliation.Items.Select(item => item.AssetId).OrderBy(id => id));
    }

    [Fact]
    public async Task DiscoverAsync_DescendantFolderFailureIsNonFatalAndSiblingsStillContribute()
    {
        var rootId = Guid.NewGuid();
        var folders = new MapFolders(new()
        {
            [""] = Listing(rootId, "", Dir(rootId, "Locked"), Dir(rootId, "Ok")),
            ["Locked"] = new(MediaFolderEnumerationStatus.AccessDenied, "Locked", [], "Access denied."),
            ["Ok"] = Listing(rootId, "Ok", FileEntry(rootId, "Ok/clip.mp4")),
        });
        var discovery = new MapDiscovery(rootId);
        var service = new RecursiveMediaDiscoveryService(folders, discovery);

        var result = await service.DiscoverAsync(new(rootId, null));

        Assert.True(result.Succeeded);
        Assert.Equal(["Ok/clip.mp4"], result.MediaEntries.Select(entry => entry.RelativePath));
        Assert.Contains("Locked", result.Diagnostic);
    }

    [Fact]
    public async Task DiscoverAsync_DescendantReconciliationFailureIsNonFatalAndSiblingsStillContribute()
    {
        var rootId = Guid.NewGuid();
        var folders = new MapFolders(new()
        {
            [""] = Listing(rootId, "", Dir(rootId, "Bad"), Dir(rootId, "Ok")),
            ["Bad"] = Listing(rootId, "Bad", FileEntry(rootId, "Bad/clip.mp4")),
            ["Ok"] = Listing(rootId, "Ok", FileEntry(rootId, "Ok/clip.mp4")),
        });
        var discovery = new MapDiscovery(rootId, new()
        {
            ["Bad"] = new(new(CatalogReconciliationStatus.Failed, rootId, "Bad", [], Diagnostic: "boom"), null),
        });
        var service = new RecursiveMediaDiscoveryService(folders, discovery);

        var result = await service.DiscoverAsync(new(rootId, null));

        Assert.True(result.Succeeded);
        Assert.Equal(["Ok/clip.mp4"], result.MediaEntries.Select(entry => entry.RelativePath));
    }

    [Fact]
    public async Task DiscoverAsync_BaseFolderEnumerationFailureIsFatalToTheWholeScope()
    {
        var rootId = Guid.NewGuid();
        var folders = new MapFolders(new()
        {
            [""] = new(MediaFolderEnumerationStatus.AccessDenied, "", [], "Access denied."),
        });
        var service = new RecursiveMediaDiscoveryService(folders, new MapDiscovery(rootId));

        var result = await service.DiscoverAsync(new(rootId, null));

        Assert.False(result.Succeeded);
        Assert.Empty(result.MediaEntries);
        Assert.Null(result.DerivedWork);
    }

    [Fact]
    public async Task DiscoverAsync_BaseFolderReconciliationFailureIsFatalToTheWholeScope()
    {
        var rootId = Guid.NewGuid();
        var folders = new MapFolders(new() { [""] = Listing(rootId, "", FileEntry(rootId, "root.mp4")) });
        var discovery = new MapDiscovery(rootId, new()
        {
            [""] = new(new(CatalogReconciliationStatus.Failed, rootId, "", [], Diagnostic: "boom"), null),
        });
        var service = new RecursiveMediaDiscoveryService(folders, discovery);

        var result = await service.DiscoverAsync(new(rootId, null));

        Assert.False(result.Succeeded);
        Assert.Empty(result.MediaEntries);
    }

    [Fact]
    public async Task DiscoverAsync_ManyFoldersUnderALowConcurrencyCapAllCompleteWithoutDeadlockOrLoss()
    {
        // Sanity coverage for the bounded-concurrency fan-out: a wide, two-level tree with more folders than
        // the concurrency cap must still visit and aggregate every folder correctly rather than deadlocking
        // or silently dropping folders once the semaphore is saturated.
        const int width = 40;
        var rootId = Guid.NewGuid();
        var listings = new Dictionary<string, MediaFolderEnumerationResult>
        {
            [""] = Listing(rootId, "", [.. Enumerable.Range(0, width).Select(index => Dir(rootId, $"F{index}"))])
        };
        foreach (var index in Enumerable.Range(0, width))
            listings[$"F{index}"] = Listing(rootId, $"F{index}", FileEntry(rootId, $"F{index}/clip.mp4"));
        var service = new RecursiveMediaDiscoveryService(new MapFolders(listings), new MapDiscovery(rootId),
            maximumConcurrentFolders: 3);

        var result = await service.DiscoverAsync(new(rootId, null));

        Assert.True(result.Succeeded);
        Assert.Equal(width, result.MediaEntries.Count);
    }

    [Fact]
    public async Task DiscoverAsync_HonorsTheTotalFolderSafetyCapAndNotesTruncationRatherThanFailing()
    {
        var rootId = Guid.NewGuid();
        var listings = new Dictionary<string, MediaFolderEnumerationResult>
        {
            [""] = Listing(rootId, "", Dir(rootId, "A"), Dir(rootId, "B"), Dir(rootId, "C")),
            ["A"] = Listing(rootId, "A", FileEntry(rootId, "A/clip.mp4")),
            ["B"] = Listing(rootId, "B", FileEntry(rootId, "B/clip.mp4")),
            ["C"] = Listing(rootId, "C", FileEntry(rootId, "C/clip.mp4")),
        };
        // Cap of 2 total folders: only the base folder plus one descendant may be visited.
        var service = new RecursiveMediaDiscoveryService(new MapFolders(listings), new MapDiscovery(rootId),
            maximumFolders: 2);

        var result = await service.DiscoverAsync(new(rootId, null));

        Assert.True(result.Succeeded);
        Assert.Single(result.MediaEntries);
        Assert.Contains("more than 2 folders", result.Diagnostic);
    }

    [Fact]
    public async Task DiscoverAsync_PropagatesRealCancellationRatherThanSwallowingItIntoAResultStatus()
    {
        var rootId = Guid.NewGuid();
        using var cancellation = new CancellationTokenSource();
        // Cancel while the base folder is being enumerated, so by the time VisitAsync("A") runs (right after,
        // in DiscoverAsync's fan-out) the token is already canceled and its leading ThrowIfCancellationRequested
        // fires immediately — the same guard a real cancellation mid-walk would hit.
        var folders = new MapFolders(new()
        {
            [""] = Listing(rootId, "", Dir(rootId, "A")),
        }, onEnumerate: folder =>
        {
            if (folder == "") cancellation.Cancel();
        });
        var service = new RecursiveMediaDiscoveryService(folders, new MapDiscovery(rootId));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.DiscoverAsync(new(rootId, null), enumerationToken: cancellation.Token));
    }

    [Fact]
    public async Task DiscoverAsync_DerivedWorkIsNullWhenNoVisitedFolderScheduledAnyWork()
    {
        var rootId = Guid.NewGuid();
        var folders = new MapFolders(new() { [""] = Listing(rootId, "", FileEntry(rootId, "root.mp4")) });
        // Default MapDiscovery already returns DerivedWork: null for every folder (scheduler unavailable).
        var service = new RecursiveMediaDiscoveryService(folders, new MapDiscovery(rootId));

        var result = await service.DiscoverAsync(new(rootId, null));

        Assert.True(result.Succeeded);
        Assert.Null(result.DerivedWork);
    }

    [Theory]
    [InlineData(null, "", true)]
    [InlineData(null, "Trips", true)]
    [InlineData("Trips", "", true)]
    [InlineData("Trips", "Trips", true)]
    [InlineData("Trips/Iceland", "Trips", true)]
    [InlineData("TRIPS/ICELAND", "trips", true)]
    [InlineData("Trips2", "Trips", false)]
    [InlineData("Other", "Trips", false)]
    [InlineData("", "Trips", false)]
    public void IsWithinFolderScope_MatchesEqualAndDescendantFoldersOnly(string? candidate, string baseFolder, bool expected) =>
        Assert.Equal(expected, BrowserScope.IsWithinFolderScope(candidate, baseFolder));

    [Fact]
    public void AggregateDerivedWorkBatch_SumsProgressAcrossChildren()
    {
        var reconciliationA = new CatalogReconciliationResult(CatalogReconciliationStatus.Succeeded, Guid.NewGuid(), "A",
            [new(Guid.NewGuid(), "A/clip.mp4", CatalogReconciliationItemStatus.New)]);
        var reconciliationB = new CatalogReconciliationResult(CatalogReconciliationStatus.Succeeded, Guid.NewGuid(), "B",
            [new(Guid.NewGuid(), "B/clip.mp4", CatalogReconciliationItemStatus.New),
             new(Guid.NewGuid(), "B/other.mp4", CatalogReconciliationItemStatus.New)]);
        var batchA = new DerivedWorkBatch(reconciliationA, static _ => { });
        foreach (var item in reconciliationA.Items)
        {
            batchA.AddPending(item.AssetId);
            batchA.Complete(item.AssetId, new(item.AssetId, DerivedWorkItemOutcome.Current,
                DerivedWorkComponentOutcome.Current, DerivedWorkComponentOutcome.Current));
        }
        batchA.Seal();
        var batchB = new DerivedWorkBatch(reconciliationB, static _ => { });
        foreach (var item in reconciliationB.Items)
        {
            batchB.AddPending(item.AssetId);
            batchB.Complete(item.AssetId, new(item.AssetId, DerivedWorkItemOutcome.Current,
                DerivedWorkComponentOutcome.Current, DerivedWorkComponentOutcome.Current));
        }
        batchB.Seal();

        var combined = new CatalogReconciliationResult(CatalogReconciliationStatus.Succeeded, Guid.NewGuid(), "",
            [.. reconciliationA.Items, .. reconciliationB.Items]);
        var aggregate = new AggregateDerivedWorkBatch(combined, [batchA, batchB]);

        Assert.Equal(3, aggregate.Progress.Total);
        Assert.Equal(3, aggregate.Progress.Completed);
        Assert.Equal(DerivedWorkBatchStatus.Completed, aggregate.Progress.Status);
    }

    [Fact]
    public void AggregateDerivedWorkBatch_StatusIsRunningWhileAnyChildIsStillRunning()
    {
        var reconciliation = new CatalogReconciliationResult(CatalogReconciliationStatus.Succeeded, Guid.NewGuid(), "",
            [new(Guid.NewGuid(), "clip.mp4", CatalogReconciliationItemStatus.New)]);
        var running = new DerivedWorkBatch(reconciliation, static _ => { });
        running.AddPending(reconciliation.Items[0].AssetId);
        running.Seal();
        var completed = new DerivedWorkBatch(reconciliation, static _ => { });
        completed.Seal();

        var aggregate = new AggregateDerivedWorkBatch(reconciliation, [running, completed]);

        Assert.Equal(DerivedWorkBatchStatus.Running, aggregate.Progress.Status);
    }

    [Fact]
    public void AggregateDerivedWorkBatch_ResultsConcatenatesEveryChildsResults()
    {
        var assetA = Guid.NewGuid();
        var assetB = Guid.NewGuid();
        var reconciliationA = new CatalogReconciliationResult(CatalogReconciliationStatus.Succeeded, Guid.NewGuid(), "A",
            [new(assetA, "A/clip.mp4", CatalogReconciliationItemStatus.New)]);
        var reconciliationB = new CatalogReconciliationResult(CatalogReconciliationStatus.Succeeded, Guid.NewGuid(), "B",
            [new(assetB, "B/clip.mp4", CatalogReconciliationItemStatus.New)]);
        var batchA = new DerivedWorkBatch(reconciliationA, static _ => { });
        var batchB = new DerivedWorkBatch(reconciliationB, static _ => { });
        batchA.AddPending(assetA);
        batchA.Complete(assetA, new(assetA, DerivedWorkItemOutcome.Generated,
            DerivedWorkComponentOutcome.Succeeded, DerivedWorkComponentOutcome.Succeeded));
        batchA.Seal();
        batchB.AddPending(assetB);
        batchB.Complete(assetB, new(assetB, DerivedWorkItemOutcome.Current,
            DerivedWorkComponentOutcome.Current, DerivedWorkComponentOutcome.Current));
        batchB.Seal();

        var aggregate = new AggregateDerivedWorkBatch(reconciliationA, [batchA, batchB]);

        Assert.Equal(new[] { assetA, assetB }.OrderBy(id => id),
            aggregate.Results.Select(result => result.AssetId).OrderBy(id => id));
    }

    [Fact]
    public void AggregateDerivedWorkBatch_CancelCancelsEveryChild()
    {
        var reconciliation = new CatalogReconciliationResult(CatalogReconciliationStatus.Succeeded, Guid.NewGuid(), "",
            [new(Guid.NewGuid(), "clip.mp4", CatalogReconciliationItemStatus.New)]);
        var canceledBatches = new List<DerivedWorkBatch>();
        var batchA = new DerivedWorkBatch(reconciliation, batch => canceledBatches.Add(batch));
        var batchB = new DerivedWorkBatch(reconciliation, batch => canceledBatches.Add(batch));
        batchA.Seal();
        batchB.Seal();
        var aggregate = new AggregateDerivedWorkBatch(reconciliation, [batchA, batchB]);

        aggregate.Cancel();

        Assert.Equal(2, canceledBatches.Count);
        Assert.Contains(batchA, canceledBatches);
        Assert.Contains(batchB, canceledBatches);
    }

    [Fact]
    public async Task AggregateDerivedWorkBatch_CompletionWaitsForEveryChild()
    {
        var reconciliation = new CatalogReconciliationResult(CatalogReconciliationStatus.Succeeded, Guid.NewGuid(), "",
            [new(Guid.NewGuid(), "clip.mp4", CatalogReconciliationItemStatus.New),
             new(Guid.NewGuid(), "other.mp4", CatalogReconciliationItemStatus.New)]);
        var slow = new DerivedWorkBatch(reconciliation, static _ => { });
        slow.AddPending(reconciliation.Items[0].AssetId);
        var fast = new DerivedWorkBatch(reconciliation, static _ => { });
        fast.Seal();
        var aggregate = new AggregateDerivedWorkBatch(reconciliation, [slow, fast]);

        var completion = aggregate.Completion;
        await Task.Delay(20);
        Assert.False(completion.IsCompleted, "Completion must not finish while a child batch still has pending work.");

        slow.Complete(reconciliation.Items[0].AssetId, new(reconciliation.Items[0].AssetId,
            DerivedWorkItemOutcome.Current, DerivedWorkComponentOutcome.Current, DerivedWorkComponentOutcome.Current));
        slow.Seal();

        var final = await completion;
        Assert.Equal(DerivedWorkBatchStatus.Completed, final.Status);
    }

    private static MediaFolderEntry Dir(Guid rootId, string relativePath) =>
        new(rootId, relativePath, relativePath.ToUpperInvariant(), Path.GetFileName(relativePath), true,
            MediaTypeClassification.Unknown, null, DateTimeOffset.UtcNow);

    private static MediaFolderEntry FileEntry(Guid rootId, string relativePath) =>
        new(rootId, relativePath, relativePath.ToUpperInvariant(), Path.GetFileName(relativePath), false,
            new(MediaTypeCategory.Video, "mp4"), 10, DateTimeOffset.UtcNow);

    private static MediaFolderEnumerationResult Listing(Guid rootId, string folder, params MediaFolderEntry[] entries) =>
        new(MediaFolderEnumerationStatus.Succeeded, folder, entries);

    private static CatalogReconciliationItem Item(Guid assetId, string relativePath) =>
        new(assetId, relativePath, CatalogReconciliationItemStatus.New);

    private static MediaDiscoveryRefreshResult Refresh(Guid rootId, string folder, params CatalogReconciliationItem[] items)
    {
        var reconciliation = new CatalogReconciliationResult(CatalogReconciliationStatus.Succeeded, rootId, folder, items);
        var batch = new DerivedWorkBatch(reconciliation, static _ => { });
        batch.Seal();
        return new(reconciliation, batch);
    }

    private sealed class MapFolders(Dictionary<string, MediaFolderEnumerationResult> byFolder,
        Action<string>? onEnumerate = null) : IMediaFolderEnumerator
    {
        public Task<MediaFolderEnumerationResult> EnumerateAsync(MediaFolderEnumerationRequest request,
            CancellationToken cancellationToken = default)
        {
            var folder = request.RelativeFolder ?? "";
            onEnumerate?.Invoke(folder);
            return Task.FromResult(byFolder.TryGetValue(folder, out var result)
                ? result
                : new MediaFolderEnumerationResult(MediaFolderEnumerationStatus.Succeeded, folder, []));
        }
    }

    private sealed class MapDiscovery(Guid rootId, Dictionary<string, MediaDiscoveryRefreshResult>? byFolder = null)
        : IMediaDiscoveryRefreshService
    {
        public List<string> Requests { get; } = [];

        public Task<MediaDiscoveryRefreshResult> RefreshAsync(MediaFolderEnumerationRequest request,
            DerivedWorkPriority priority = DerivedWorkPriority.Background, CancellationToken cancellationToken = default,
            CancellationToken derivedWorkCancellationToken = default)
        {
            var folder = request.RelativeFolder ?? "";
            lock (Requests) Requests.Add(folder);
            if (byFolder is not null && byFolder.TryGetValue(folder, out var result)) return Task.FromResult(result);
            return Task.FromResult(new MediaDiscoveryRefreshResult(
                new(CatalogReconciliationStatus.Succeeded, rootId, folder, []), null));
        }
    }
}
