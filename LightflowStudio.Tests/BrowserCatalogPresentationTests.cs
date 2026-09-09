using LightflowStudio;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class BrowserCatalogPresentationTests : IAsyncLifetime
{
    [Fact]
    public void RegeneratedThumbnailReplacesProvisionalCachedThumbnailOnce()
    {
        var id = Guid.NewGuid();
        DerivedWorkItemResult[] results = [new(id, DerivedWorkItemOutcome.Generated,
            DerivedWorkComponentOutcome.NotNeeded, DerivedWorkComponentOutcome.Succeeded)];
        Assert.Equal(id, Assert.Single(BrowserDerivedWorkProjection.AssetsNeedingThumbnailLookup(results,
            _ => true, new HashSet<Guid>())));
        Assert.Empty(BrowserDerivedWorkProjection.AssetsNeedingThumbnailLookup(results, _ => true, new HashSet<Guid> { id }));
    }
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "lightflow-known-" + Guid.NewGuid().ToString("N"));
    private LightflowStorageCoordinator _storage = null!;
    private MediaRootInfo _root = null!;
    private string Media => Path.Combine(_directory, "media");

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(Path.Combine(Media, "known"));
        Directory.CreateDirectory(Path.Combine(Media, "other"));
        _storage = (await LightflowStorageCoordinator.StartAsync(Path.Combine(_directory, "app"))).Coordinator!;
        await _storage.MediaMonitoring!.DisposeAsync();
        _root = (await _storage.MediaRoots.CreateAsync("Test", Media)).Root!;
    }

    public async Task DisposeAsync()
    {
        await _storage.DisposeAsync();
        Directory.Delete(_directory, true);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task KnownEntriesPrecedeReconciliationAndShareScopeAndSelection(bool recursive)
    {
        var kept = await Add("known/keep.jpg");
        await Add("known/remove.jpg");
        await Add("known/sub/child.jpg");
        await Add("known-other/outside.jpg");
        if (recursive) await _storage.BrowserRecursiveRoots.EnableAsync(_root.RootId, "known");
        File.Delete(Path.Combine(Media, "known/remove.jpg"));
        File.WriteAllText(Path.Combine(Media, "known/new.jpg"), "new");
        var gate = new GateDiscovery(new MediaDiscoveryRefreshService(_storage.CatalogReconciliation, () => null));
        using var navigation = Session(gate);
        var presented = new TaskCompletionSource<BrowserFolderState>(TaskCreationOptions.RunContinuationsAsynchronously);
        navigation.KnownContentAvailable += (_, state) => presented.TrySetResult(state);
        var loading = navigation.NavigateToPathAsync(Path.Combine(Media, "known"));
        var initial = await presented.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(loading.IsCompleted);
        Assert.True(initial.IsRevalidating);
        var grid = new BrowserGridModel();
        grid.Populate(initial.RecursiveMediaEntries ?? initial.Entries);
        Assert.Equal(recursive ? 3 : 2, grid.TotalCount);
        Assert.Contains(grid.Tiles, tile => tile.Name == "remove.jpg");
        grid.SelectSingle(grid.Tiles.ToList().FindIndex(tile => tile.AssetId == kept.AssetId));
        var prior = grid.Tiles.Single(tile => tile.AssetId == kept.AssetId);
        var query = BrowserQuery.Default with { SortDescending = true, SearchText = "jpg" };
        grid.SetQuery(query);
        gate.Release.TrySetResult();
        var final = (await loading.WaitAsync(TimeSpan.FromSeconds(10)))!;
        Assert.False(final.IsRevalidating);
        Assert.NotNull(final.Reconciliation);
        grid.InvalidateChangedAssets(final.Reconciliation.Items);
        grid.Populate(final.RecursiveMediaEntries ?? final.Entries);
        grid.ApplyAssetIdentities(final.Reconciliation.Items);
        Assert.DoesNotContain(grid.Tiles, tile => tile.Name == "remove.jpg");
        Assert.Contains(grid.Tiles, tile => tile.Name == "new.jpg");
        Assert.Same(prior, grid.Tiles.Single(tile => tile.AssetId == kept.AssetId));
        Assert.Single(grid.SelectedKeys);
        Assert.Equal(query, grid.Query);
        Assert.Equal(grid.Tiles.Select(tile => tile.Name).OrderDescending(), grid.Tiles.Select(tile => tile.Name));
        Assert.False(final.CanGoBack); // provisional + final is one navigation, not two history entries
    }

    [Fact]
    public async Task SupersededReconciliationCannotReplaceNewerNavigationEvenIfProviderIgnoresCancellation()
    {
        await Add("known/photo.jpg");
        var gate = new GateDiscovery(new MediaDiscoveryRefreshService(_storage.CatalogReconciliation, () => null));
        using var navigation = Session(gate);
        BrowserFolderState? initial = null;
        navigation.KnownContentAvailable += (_, state) => initial = state;
        var obsolete = navigation.NavigateToPathAsync(Path.Combine(Media, "known"));
        await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var current = await navigation.NavigateToPathAsync(Path.Combine(Media, "other"));
        Assert.False(navigation.IsCurrent(initial!));
        gate.Release.TrySetResult();
        Assert.Null(await obsolete.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Same(current, navigation.State);
        Assert.Equal("known", navigation.BackTarget!.RelativeFolder);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationAndDisposalInvalidateProvisionalCallbacks(bool dispose)
    {
        await Add("known/photo.jpg");
        var gate = new GateDiscovery(new MediaDiscoveryRefreshService(_storage.CatalogReconciliation, () => null));
        using var navigation = Session(gate);
        using var cancellation = new CancellationTokenSource();
        var pending = navigation.NavigateToPathAsync(Path.Combine(Media, "known"), cancellation.Token);
        await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var initial = navigation.State;
        if (dispose) navigation.Dispose(); else cancellation.Cancel();
        Assert.False(navigation.IsCurrent(initial));
        gate.Release.TrySetResult();
        if (dispose) Assert.Null(await pending.WaitAsync(TimeSpan.FromSeconds(10)));
        else await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }

    [Fact]
    public async Task EmptyAndUnavailableFoldersDoNotFabricateKnownContent()
    {
        using var navigation = Session(new MediaDiscoveryRefreshService(_storage.CatalogReconciliation, () => null));
        var count = 0;
        navigation.KnownContentAvailable += (_, _) => count++;
        Assert.Equal(BrowserFolderStatus.Empty, (await navigation.NavigateToPathAsync(Path.Combine(Media, "known")))!.Status);
        Directory.Move(Media, Path.Combine(_directory, "offline"));
        Assert.Equal(BrowserFolderStatus.RootUnavailable, (await navigation.NavigateToRootAsync(_root.RootId))!.Status);
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task CachedPreviewRequiresMatchingAssetSourceIdentity()
    {
        var asset = await Add("known/photo.jpg");
        var source = new PreviewSourceIdentity(asset.FileSizeBytes, asset.LastWriteUtcTicks,
            asset.Fingerprint!.Version, asset.Fingerprint.Value);
        var preview = await _storage.Previews!.ObserveSourceAsync(asset.AssetId, source);
        Assert.True(BrowserPreviewReuse.Matches(asset, preview));
        Assert.False(BrowserPreviewReuse.Matches(asset with { AssetId = Guid.NewGuid() }, preview));
        Assert.False(BrowserPreviewReuse.Matches(asset with { Fingerprint = new(1, new string('0', 64)) }, preview));
        Assert.False(BrowserPreviewReuse.Matches(asset with { SourceStatus = MediaAssetSourceStatus.Missing }, preview));
    }

    [Fact]
    public async Task ScopedCatalogQueryUsesLogicalBoundariesAndHandlesUnicodeAndSqlWildcards()
    {
        var expected = await Add("known%_/😀.jpg");
        var child = await Add("known%_/sub/child.jpg");
        await Add("known%_-other/outside.jpg");
        var top = await Add("😀.jpg");
        var direct = await _storage.MediaAssets.ListScopeAsync(_root.RootId, "KNOWN%_", false);
        Assert.Equal(expected.AssetId, Assert.Single(direct).AssetId);
        var recursive = await _storage.MediaAssets.ListScopeAsync(_root.RootId, "known%_", true);
        Assert.Equal(new[] { expected.AssetId, child.AssetId }.Order(), recursive.Select(asset => asset.AssetId).Order());
        Assert.Equal(top.AssetId, Assert.Single(await _storage.MediaAssets.ListScopeAsync(_root.RootId, "", false)).AssetId);
        Assert.Empty(await _storage.MediaAssets.ListScopeAsync(Guid.NewGuid(), "", true));
    }

    [Fact]
    public async Task FailedValidationRetainsUnverifiedSnapshotButRetiresQueuedInitialPresentation()
    {
        await Add("known/photo.jpg");
        using var navigation = Session(new FailedDiscovery());
        BrowserFolderState? initial = null;
        navigation.KnownContentAvailable += (_, state) => initial = state;
        var final = await navigation.NavigateToPathAsync(Path.Combine(Media, "known"));
        Assert.Equal(BrowserFolderStatus.FolderUnavailable, final!.Status);
        Assert.True(navigation.State.IsRevalidating);
        Assert.Same(initial, navigation.State);
        Assert.False(navigation.CanPresentKnownContent(initial!));
    }

    [Fact]
    public async Task ChangedFingerprintInvalidatesThumbnailEvenWithUnchangedSizeAndTimestamp()
    {
        var asset = await Add("known/photo.jpg");
        using var navigation = Session(new MediaDiscoveryRefreshService(_storage.CatalogReconciliation, () => null));
        var grid = new BrowserGridModel();
        grid.Populate(BrowserCatalogScope.Entries([asset], _storage.MediaTypes));
        grid.ApplyThumbnail(asset.AssetId, "old-preview.jpg");
        grid.SelectSingle(0);
        File.WriteAllText(Path.Combine(Media, asset.RelativePath), "BBBB");
        File.SetLastWriteTimeUtc(Path.Combine(Media, asset.RelativePath), new DateTime(asset.LastWriteUtcTicks, DateTimeKind.Utc));
        var final = (await navigation.NavigateToPathAsync(Path.Combine(Media, "known")))!;
        Assert.Equal(CatalogReconciliationItemStatus.Changed, Assert.Single(final.Reconciliation!.Items).Status);
        grid.InvalidateChangedAssets(final.Reconciliation.Items);
        grid.Populate(final.Entries);
        grid.ApplyAssetIdentities(final.Reconciliation.Items);
        Assert.False(Assert.Single(grid.Tiles).HasThumbnail);
        Assert.Single(grid.SelectedKeys);
    }

    private BrowserNavigationSession Session(IMediaDiscoveryRefreshService discovery) => new(
        _storage.MediaRoots, _storage.BrowserLocations, discovery, _storage.MediaFolders, _storage.BrowserRecursiveRoots,
        assets: _storage.MediaAssets, mediaTypes: _storage.MediaTypes);

    private async Task<MediaAsset> Add(string relative)
    {
        var path = Path.Combine(Media, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "AAAA");
        return (await _storage.MediaAssets.CreateAsync(_root.RootId, relative, "image")).Asset!.Asset;
    }

    private sealed class GateDiscovery(IMediaDiscoveryRefreshService inner) : IMediaDiscoveryRefreshService
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<MediaDiscoveryRefreshResult> RefreshAsync(MediaFolderEnumerationRequest request,
            DerivedWorkPriority priority = DerivedWorkPriority.Background, CancellationToken cancellationToken = default,
            CancellationToken derivedWorkCancellationToken = default)
        {
            if (request.RelativeFolder == "known")
            {
                Entered.TrySetResult();
                await Release.Task;
            }
            return await inner.RefreshAsync(request, priority); // deliberately ignore cancellation
        }
    }

    private sealed class FailedDiscovery : IMediaDiscoveryRefreshService
    {
        public Task<MediaDiscoveryRefreshResult> RefreshAsync(MediaFolderEnumerationRequest request,
            DerivedWorkPriority priority = DerivedWorkPriority.Background, CancellationToken cancellationToken = default,
            CancellationToken derivedWorkCancellationToken = default) => Task.FromResult(new MediaDiscoveryRefreshResult(
                new(CatalogReconciliationStatus.FolderUnavailable, request.RootId, request.RelativeFolder ?? "", []), null));
    }
}
