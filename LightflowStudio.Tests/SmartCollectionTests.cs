using LightflowStudio;
using Microsoft.Data.Sqlite;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class SmartCollectionTests : IAsyncLifetime
{
    private readonly string _directory = Path.Combine(AppContext.BaseDirectory, "smart-tests", Guid.NewGuid().ToString("N"));
    private LightflowStorageCoordinator _storage = null!;
    private MediaRootInfo _root = null!;
    private string Media => Path.Combine(_directory, "media");
    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(Path.Combine(Media, "photos", "child"));
        _storage = (await LightflowStorageCoordinator.StartAsync(Path.Combine(_directory, "app"))).Coordinator!;
        await _storage.MediaMonitoring!.DisposeAsync();
        _root = (await _storage.MediaRoots.CreateAsync("Source", Media)).Root!;
    }
    public async Task DisposeAsync()
    {
        await _storage.DisposeAsync();
        SqliteConnection.ClearAllPools();
        Directory.Delete(_directory, true);
    }
    private SmartCollectionSource Folder(bool recursive = false) => new(SmartCollectionSourceKind.Folder, _root.RootId, "photos", IncludeSubfolders: recursive);
    private async Task<MediaAsset> Add(string relative)
    {
        var path = Path.Combine(Media, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "fixture");
        return (await _storage.MediaAssets.CreateAsync(_root.RootId, relative, "image")).Asset!.Asset;
    }

    [Fact]
    public async Task DefinitionSurvivesRestartAndLocationIsIndependentFromSource()
    {
        var set = await _storage.Collections.CreateSetAsync("Organization");
        var query = BrowserQueryIntent.Capture(new() { SearchText = "trip", SortDescending = true,
            Filters = [BrowserFilterPredicate.ForRating(BrowserNumberComparison.GreaterThanOrEqual, 4)] });
        var saved = await _storage.SmartCollections.SaveSmartCollectionAsync("Favorites", set.CollectionSetId, Folder(true), query);
        await _storage.DisposeAsync();
        _storage = (await LightflowStorageCoordinator.StartAsync(Path.Combine(_directory, "app"))).Coordinator!;
        await _storage.MediaMonitoring!.DisposeAsync();
        var reopened = (await _storage.SmartCollections.GetSmartCollectionAsync(saved.SmartCollectionId))!;
        Assert.Equal(saved.Source, reopened.Source);
        Assert.Equal(set.CollectionSetId, reopened.Organization.ParentCollectionSetId);
        Assert.Equal(query.Serialize(), reopened.Query.Serialize());
        Assert.DoesNotContain(Media, reopened.Query.Serialize());
        Assert.False(reopened.Query.ToQuery().SortDescending);
        Assert.Empty(await _storage.Collections.ListMembershipsAsync(saved.SmartCollectionId));
    }

    [Fact]
    public async Task RequiredSourceAndSmartSourceAreRejectedWithoutPartialOrganization()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _storage.SmartCollections.SaveSmartCollectionAsync("Invalid", null, new(SmartCollectionSourceKind.Folder), new()));
        var smart = await _storage.SmartCollections.SaveSmartCollectionAsync("First", null, Folder(), new());
        await Assert.ThrowsAsync<ArgumentException>(() => _storage.SmartCollections.SaveSmartCollectionAsync("Cycle", null,
            new(SmartCollectionSourceKind.Collection, CollectionId: smart.SmartCollectionId), new()));
        Assert.Single(await _storage.Collections.ListCollectionsAsync());
        Assert.Throws<ArgumentException>(() => (Folder() with { RelativeFolder = "../outside" }).Validate());
    }

    [Fact]
    public async Task EditIsAtomicRevisionCheckedAndSupportsHierarchyRenameMoveDelete()
    {
        var smart = await _storage.SmartCollections.SaveSmartCollectionAsync("First", null, Folder(), new());
        var set = await _storage.Collections.CreateSetAsync("Destination");
        var source = await _storage.Collections.CreateCollectionAsync("Source");
        var updated = await _storage.SmartCollections.SaveSmartCollectionAsync("Edited", set.CollectionSetId,
            new(SmartCollectionSourceKind.Collection, CollectionId: source.CollectionId), new() { MatchMode = BrowserMatchMode.Any },
            smart.SmartCollectionId, smart.Organization.Revision);
        await Assert.ThrowsAsync<CollectionConcurrencyException>(() => _storage.SmartCollections.SaveSmartCollectionAsync("Stale", null,
            Folder(true), new(), smart.SmartCollectionId, smart.Organization.Revision));
        Assert.Equal("Edited", (await _storage.SmartCollections.GetSmartCollectionAsync(smart.SmartCollectionId))!.Organization.Name);
        set = (await _storage.Collections.GetSetAsync(set.CollectionSetId))!;
        await Assert.ThrowsAsync<CollectionNotEmptyException>(() => _storage.Collections.DeleteSetAsync(set.CollectionSetId, set.Revision));
        var moved = await _storage.Collections.ReparentCollectionAsync(updated.SmartCollectionId, updated.Organization.Revision, null);
        var renamed = await _storage.Collections.RenameCollectionAsync(moved.CollectionId, moved.Revision, "Renamed");
        Assert.True(renamed.IsSmartCollection);
        await _storage.Collections.DeleteCollectionAsync(renamed.CollectionId, renamed.Revision);
        Assert.Null(await _storage.SmartCollections.GetSmartCollectionAsync(renamed.CollectionId));
    }

    [Fact]
    public async Task NoManualMembershipAndStaticSourceMembershipPublishesChanges()
    {
        var asset = await Add("photos/one.jpg");
        var source = await _storage.Collections.CreateCollectionAsync("Static Source");
        var smart = await _storage.SmartCollections.SaveSmartCollectionAsync("Computed", null,
            new(SmartCollectionSourceKind.Collection, CollectionId: source.CollectionId), new());
        await Assert.ThrowsAsync<ArgumentException>(() => _storage.Collections.AddMembershipAsync(smart.SmartCollectionId, asset.AssetId));
        var changes = new List<Guid>();
        _storage.SmartCollections.MembershipChanged += (_, id) => changes.Add(id);
        var membership = await _storage.Collections.AddMembershipAsync(source.CollectionId, asset.AssetId);
        var scopes = new BrowserCollectionScopeService(_storage.Collections, _storage.MediaAssets, _storage.MediaRoots, _storage.MediaTypes, () => null);
        Assert.Single((await scopes.LoadAsync(source.CollectionId)).Entries);
        await _storage.Collections.RemoveMembershipAsync(source.CollectionId, asset.AssetId, membership.Membership.Revision);
        Assert.Empty((await scopes.LoadAsync(source.CollectionId)).Entries);
        Assert.Equal([source.CollectionId, source.CollectionId], changes);
        Assert.Empty(await _storage.Collections.ListMembershipsAsync(smart.SmartCollectionId));
        Assert.False(BrowserCollectionMembershipInteraction.CanDrop(new([asset.AssetId]), new(smart.Organization)));
        Assert.True(BrowserCollectionInteraction.CanDrop(new(smart.Organization), new(await _storage.Collections.CreateSetAsync("Target"))));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task SavedFolderRecursionOverridesOrdinaryScopeWithoutMutatingItAndShowsKnownContentBeforeScan(bool recursive)
    {
        await Add("photos/one.jpg"); await Add("photos/child/two.jpg");
        // Deliberately configure the opposite ordinary Browser mode.
        if (!recursive) await _storage.BrowserRecursiveRoots.EnableAsync(_root.RootId, "photos");
        var gate = new GateDiscovery(new MediaDiscoveryRefreshService(_storage.CatalogReconciliation, () => null));
        using var navigation = new BrowserNavigationSession(_storage.MediaRoots, _storage.BrowserLocations, gate,
            _storage.MediaFolders, _storage.BrowserRecursiveRoots, assets: _storage.MediaAssets, mediaTypes: _storage.MediaTypes);
        var known = new TaskCompletionSource<BrowserFolderState>(TaskCreationOptions.RunContinuationsAsynchronously);
        navigation.KnownContentAvailable += (_, state) => known.TrySetResult(state);
        var work = navigation.NavigateSourceAsync(_root.RootId, "photos", recursive);
        var state = await known.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(work.IsCompleted);
        Assert.True(navigation.WorkingGeneration > 0);
        Assert.Equal(recursive ? 2 : 1, (state.RecursiveMediaEntries ?? state.Entries).Count);
        gate.Release.TrySetResult();
        await work.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(0, navigation.WorkingGeneration);
        Assert.Equal(!recursive, (await _storage.BrowserRecursiveRoots.ListAsync()).Count > 0);
    }

    [Fact]
    public async Task CatalogMutationBarrierHoldsSmartDefinitionWrites()
    {
        var barrier = await _storage.Mutations.QuiesceAsync();
        var saving = _storage.SmartCollections.SaveSmartCollectionAsync("Wait", null, Folder(), new());
        Assert.False(saving.IsCompleted);
        barrier.Dispose();
        var saved = await saving.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.NotNull(await _storage.SmartCollections.GetSmartCollectionAsync(saved.SmartCollectionId));
    }

    [Fact]
    public void DefiningQueryReevaluatesMetadataAndViewQueryDoesNotRewriteIt()
    {
        var model = new BrowserGridModel(); var root = Guid.NewGuid(); var first = Guid.NewGuid(); var second = Guid.NewGuid();
        model.Populate([new(root, "first.jpg", "FIRST.JPG", "first.jpg", false, new(MediaTypeCategory.StillImage), 1, DateTimeOffset.UtcNow, AssetId: first),
            new(root, "second.jpg", "SECOND.JPG", "second.jpg", false, new(MediaTypeCategory.StillImage), 1, DateTimeOffset.UtcNow, AssetId: second)]);
        var intent = new BrowserQueryIntent { Filters = [BrowserFilterPredicate.ForRating(BrowserNumberComparison.GreaterThanOrEqual, 4)] };
        var json = intent.Serialize();
        model.SetDefiningQuery(intent.ToQuery());
        Assert.Empty(model.Tiles); // pending authored state is not a fabricated rating
        model.ApplyClassification(AssetClassification.Empty(first) with { Rating = 5 });
        model.ReapplyQuery();
        Assert.Single(model.Tiles);
        model.SetQuery(new() { SearchText = "second" });
        Assert.Empty(model.Tiles); Assert.Equal(1, model.TotalCount);
        model.ApplyClassification(AssetClassification.Empty(second) with { Rating = 4 }); model.ReapplyQuery();
        Assert.Equal(second, Assert.Single(model.Tiles).AssetId);
        Assert.Equal(json, intent.Serialize()); Assert.Equal(2, model.TotalCount);
        model.ApplyClassification(AssetClassification.Empty(second)); model.ReapplyQuery(); Assert.Empty(model.Tiles);
    }

    [Fact]
    public void VersioningAndAllAnyUseTheSharedPredicateContract()
    {
        var grid = new BrowserGridModel(); var root = Guid.NewGuid();
        grid.Populate([new(root, "photo.jpg", "PHOTO.JPG", "photo.jpg", false, new(MediaTypeCategory.StillImage), 1, DateTimeOffset.UtcNow),
            new(root, "video.mp4", "VIDEO.MP4", "video.mp4", false, new(MediaTypeCategory.Video), 1, DateTimeOffset.UtcNow)]);
        var query = new BrowserQueryIntent { Filters = [BrowserFilterPredicate.ForMediaType(MediaTypeCategory.Video), BrowserFilterPredicate.ForText(BrowserFilterField.Camera, "Missing")] };
        Assert.Empty(BrowserQueryEngine.Apply(grid.Tiles, query.ToQuery()));
        Assert.Single(BrowserQueryEngine.Apply(grid.Tiles, (query with { MatchMode = BrowserMatchMode.Any }).ToQuery()));
        Assert.Equal(query.Serialize(), BrowserQueryIntent.Deserialize(query.Serialize()).Serialize());
        Assert.Throws<ArgumentException>(() => BrowserQueryIntent.Deserialize("{\"Version\":99}"));
        Assert.ThrowsAny<Exception>(() => BrowserQueryIntent.Deserialize("{\"Version\":1,\"Filters\":[{\"Field\":\"Unsupported\"}]}"));
        Assert.Throws<ArgumentException>(() => new BrowserQueryIntent { Filters = [new() { Field = (BrowserFilterField)999 }] }.Serialize());
        var node = new BrowserCollectionNode(new MediaCollection(Guid.NewGuid(), null, "Smart", 0, 1, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow) { IsSmartCollection = true });
        Assert.True(node.IsSmartCollection); Assert.False(node.IsCollection); Assert.False(node.IsSet);
    }

    [Fact]
    public async Task BackupRecoveryRestoresDefinitionAndSourceAfterEdits()
    {
        var saved = await _storage.SmartCollections.SaveSmartCollectionAsync("Backup", null, Folder(true), new() { SearchText = "jpg" });
        var locations = _storage.Locations;
        await _storage.DisposeAsync();
        var recovery = new SqliteCatalogRecoveryService(locations);
        var backup = await recovery.CreateBackupAsync(locations.CatalogDatabasePath, CatalogBackupKind.Automatic);
        Assert.True(backup.Succeeded);
        var opened = await new CatalogDatabaseService(locations).OpenExistingAsync();
        var service = new CatalogCollectionOrganizationService(() => opened.Session);
        await service.SaveSmartCollectionAsync("Changed", null, Folder(), new(), saved.SmartCollectionId, saved.Organization.Revision);
        await opened.Session!.DisposeAsync();
        var restore = await recovery.BeginRestoreAsync(backup.Backup!.Path);
        Assert.True((await restore.Transaction!.CommitAsync()).Succeeded);
        _storage = (await LightflowStorageCoordinator.StartAsync(Path.Combine(_directory, "app"))).Coordinator!;
        await _storage.MediaMonitoring!.DisposeAsync();
        var restored = (await _storage.SmartCollections.GetSmartCollectionAsync(saved.SmartCollectionId))!;
        Assert.Equal("Backup", restored.Organization.Name);
        Assert.Equal(saved.Source, restored.Source);
        Assert.Equal(saved.Query.Serialize(), restored.Query.Serialize());
    }

    [Fact]
    public async Task FolderIdentitySurvivesRemappingAndUnavailableSourcesKeepDefinitionsAndKnownAssets()
    {
        var asset = await Add("photos/one.jpg");
        var saved = await _storage.SmartCollections.SaveSmartCollectionAsync("Portable", null, Folder(), new());
        var remapped = Path.Combine(_directory, "remapped");
        Directory.Move(Media, remapped);
        Assert.True((await _storage.MediaRoots.RemapAsync(_root.RootId, remapped)).Succeeded);
        using var navigation = new BrowserNavigationSession(_storage.MediaRoots, _storage.BrowserLocations,
            new MediaDiscoveryRefreshService(_storage.CatalogReconciliation, () => null), _storage.MediaFolders,
            _storage.BrowserRecursiveRoots, assets: _storage.MediaAssets, mediaTypes: _storage.MediaTypes);
        var result = await navigation.NavigateSourceAsync(_root.RootId, "photos", false);
        Assert.Equal(Path.Combine(remapped, "photos"), result!.Location!.AbsolutePath);
        Assert.Equal(saved.Source, (await _storage.SmartCollections.GetSmartCollectionAsync(saved.SmartCollectionId))!.Source);
        Directory.Move(remapped, Media);
        var unavailable = await navigation.NavigateSourceAsync(_root.RootId, "photos", false);
        Assert.Equal(BrowserFolderStatus.RootUnavailable, unavailable!.Status);
        Assert.Equal(asset.AssetId, Assert.Single(await _storage.MediaAssets.ListScopeAsync(_root.RootId, "photos", false)).AssetId);
        Assert.NotNull(await _storage.SmartCollections.GetSmartCollectionAsync(saved.SmartCollectionId));
        Assert.Equal(0, navigation.WorkingGeneration);
    }

    [Fact]
    public async Task MigrationPreservesOrdinaryCollectionsAndAddsSmartDefinitionStorage()
    {
        var locations = LightflowStorageLocations.Create(Path.Combine(_directory, "migration"));
        var old = await new CatalogDatabaseService(locations, null, CatalogMigrations.All.Take(18).ToArray()).CreateNewAsync();
        var id = Guid.NewGuid();
        using (var connection = old.Session!.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "INSERT INTO Collections VALUES($id,NULL,'Legacy',0,1,$now,$now);";
            command.Parameters.AddWithValue("$id", id.ToString("D"));
            command.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O")); command.ExecuteNonQuery();
        }
        await old.Session!.DisposeAsync();
        var current = await new CatalogDatabaseService(locations, new SqliteCatalogRecoveryService(locations)).OpenExistingAsync();
        Assert.NotNull(current.Session);
        var service = new CatalogCollectionOrganizationService(() => current.Session);
        var legacy = (await service.GetCollectionAsync(id))!;
        Assert.False(legacy.IsSmartCollection); Assert.Equal("Legacy", legacy.Name);
        var smart = await service.SaveSmartCollectionAsync("New", null, new(SmartCollectionSourceKind.Collection, CollectionId: id), new());
        Assert.True(smart.Organization.IsSmartCollection);
        await current.Session!.DisposeAsync();
    }

    [Fact]
    public void LargeCandidateUniverseRemainsVirtualizedAndKeepsViewQueryIndependent()
    {
        var root = Guid.NewGuid(); var grid = new BrowserGridModel();
        grid.Populate(Enumerable.Range(0, 10000).Select(i => new MediaFolderEntry(root, $"photo{i:D5}.jpg", $"PHOTO{i:D5}.JPG",
            $"photo{i:D5}.jpg", false, new(MediaTypeCategory.StillImage), 10, DateTimeOffset.UtcNow)).ToArray());
        grid.SetDefiningQuery(new() { SearchText = "photo0" });
        grid.SetQuery(new() { SearchText = "000", SortDescending = true });
        Assert.Equal(10000, grid.TotalCount);
        Assert.True(grid.VisibleCount < grid.TotalCount);
        Assert.Equal("photo0", grid.DefiningQuery!.SearchText);
        Assert.All(grid.Tiles, tile => Assert.Contains("000", tile.Name));
    }

    private sealed class GateDiscovery(IMediaDiscoveryRefreshService inner) : IMediaDiscoveryRefreshService
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<MediaDiscoveryRefreshResult> RefreshAsync(MediaFolderEnumerationRequest request,
            DerivedWorkPriority priority = DerivedWorkPriority.Background, CancellationToken cancellationToken = default,
            CancellationToken derivedWorkCancellationToken = default)
        {
            Entered.TrySetResult(); await Release.Task.WaitAsync(cancellationToken);
            return await inner.RefreshAsync(request, priority, cancellationToken, derivedWorkCancellationToken);
        }
    }
}
