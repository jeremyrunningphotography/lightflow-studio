using LightflowStudio;
using Microsoft.Data.Sqlite;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class CatalogCollectionsTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"lightflow-collections-{Guid.NewGuid():N}");

    [Fact]
    public async Task RoundTrip_PreservesSeparateNestedSetsCollectionsAndStableIdsAcrossRestart()
    {
        await using var fixture = await Fixture.CreateAsync(_root);
        var top = await fixture.Collections.CreateSetAsync("  Oregon Country Fair  ");
        var year = await fixture.Collections.CreateSetAsync("2026", top.CollectionSetId);
        var picks = await fixture.Collections.CreateCollectionAsync("Imagerium Favorites", year.CollectionSetId);
        await fixture.ReopenAsync();

        var reopenedTop = Assert.Single(await fixture.Collections.ListSetsAsync());
        var reopenedYear = Assert.Single(await fixture.Collections.ListSetsAsync(reopenedTop.CollectionSetId));
        var reopenedCollection = Assert.Single(await fixture.Collections.ListCollectionsAsync(reopenedYear.CollectionSetId));

        Assert.Equal(top.CollectionSetId, reopenedTop.CollectionSetId);
        Assert.Equal("Oregon Country Fair", reopenedTop.Name);
        Assert.Equal(year.CollectionSetId, reopenedYear.CollectionSetId);
        Assert.Equal(picks.CollectionId, reopenedCollection.CollectionId);
        Assert.Empty(await fixture.Collections.ListCollectionsAsync());
    }

    [Fact]
    public async Task SetHierarchy_RejectsSelfAndDescendantCyclesAndRequiresLeafDelete()
    {
        await using var fixture = await Fixture.CreateAsync(_root);
        var parent = await fixture.Collections.CreateSetAsync("Parent");
        var child = await fixture.Collections.CreateSetAsync("Child", parent.CollectionSetId);
        var collection = await fixture.Collections.CreateCollectionAsync("Picks", child.CollectionSetId);

        await Assert.ThrowsAsync<CollectionHierarchyException>(() => fixture.Collections.ReparentSetAsync(
            parent.CollectionSetId, parent.Revision, child.CollectionSetId));
        await Assert.ThrowsAsync<CollectionHierarchyException>(() => fixture.Collections.ReparentSetAsync(
            parent.CollectionSetId, parent.Revision, parent.CollectionSetId));
        await Assert.ThrowsAsync<CollectionNotEmptyException>(() => fixture.Collections.DeleteSetAsync(
            child.CollectionSetId, child.Revision));

        await fixture.Collections.DeleteCollectionAsync(collection.CollectionId, collection.Revision);
        child = Assert.Single(await fixture.Collections.ListSetsAsync(parent.CollectionSetId));
        await fixture.Collections.DeleteSetAsync(child.CollectionSetId, child.Revision);
        Assert.Empty(await fixture.Collections.ListSetsAsync(parent.CollectionSetId));
    }

    [Fact]
    public async Task Membership_IsUniqueManyToManyOrderedAndIndependentOfSourceAvailability()
    {
        await using var fixture = await Fixture.CreateAsync(_root);
        var firstAsset = fixture.InsertAsset("one.mp4");
        var secondAsset = fixture.InsertAsset("two.mp4");
        var firstCollection = await fixture.Collections.CreateCollectionAsync("First");
        var secondCollection = await fixture.Collections.CreateCollectionAsync("Second");

        var first = await fixture.Collections.AddMembershipAsync(firstCollection.CollectionId, firstAsset);
        var duplicate = await fixture.Collections.AddMembershipAsync(firstCollection.CollectionId, firstAsset);
        var second = await fixture.Collections.AddMembershipAsync(firstCollection.CollectionId, secondAsset);
        await fixture.Collections.AddMembershipAsync(secondCollection.CollectionId, firstAsset);
        fixture.Execute("UPDATE MediaAssets SET SourceStatus='missing' WHERE AssetId=$asset;", ("$asset", firstAsset.ToString("D")));
        fixture.Execute("UPDATE MediaRootMappings SET PhysicalPath='X:\\remapped';");
        await fixture.ReopenAsync();

        Assert.True(first.Created);
        Assert.False(duplicate.Created);
        Assert.Equal(first.Membership, duplicate.Membership);
        Assert.Equal([firstAsset, secondAsset], (await fixture.Collections.ListMembershipsAsync(firstCollection.CollectionId)).Select(x => x.AssetId));
        Assert.Equal(firstAsset, Assert.Single(await fixture.Collections.ListMembershipsAsync(secondCollection.CollectionId)).AssetId);

        var reordered = await fixture.Collections.ReorderMembershipsAsync(firstCollection.CollectionId,
            [new(secondAsset, second.Membership.Revision), new(firstAsset, first.Membership.Revision)]);
        Assert.Equal([secondAsset, firstAsset], reordered.Select(x => x.AssetId));
        Assert.Equal([0, 1], reordered.Select(x => x.Ordinal));
    }

    [Fact]
    public async Task Reorder_IsCompleteRevisionCheckedAndRollsBackOnMismatch()
    {
        await using var fixture = await Fixture.CreateAsync(_root);
        var first = await fixture.Collections.CreateCollectionAsync("First");
        var second = await fixture.Collections.CreateCollectionAsync("Second");
        var third = await fixture.Collections.CreateCollectionAsync("Third");

        await fixture.Collections.ReorderHierarchyAsync(null,
            [Hierarchy(third), Hierarchy(first), Hierarchy(second)]);
        var reordered = await fixture.Collections.ListCollectionsAsync();
        Assert.Equal([third.CollectionId, first.CollectionId, second.CollectionId], reordered.Select(x => x.CollectionId));

        await Assert.ThrowsAsync<CollectionConcurrencyException>(() => fixture.Collections.ReorderHierarchyAsync(null,
            reordered.Select((item, index) => new CollectionHierarchyOrder(CollectionHierarchyItemKind.Collection,
                item.CollectionId, index == 1 ? item.Revision - 1 : item.Revision)).Reverse().ToArray()));
        Assert.Equal([third.CollectionId, first.CollectionId, second.CollectionId],
            (await fixture.Collections.ListCollectionsAsync()).Select(x => x.CollectionId));
    }

    [Fact]
    public async Task SetAndCollectionSiblingOrder_IsOneDenseMixedSequence()
    {
        await using var fixture = await Fixture.CreateAsync(_root);
        var firstSet = await fixture.Collections.CreateSetAsync("Set one");
        var secondSet = await fixture.Collections.CreateSetAsync("Set two");
        var firstCollection = await fixture.Collections.CreateCollectionAsync("Collection one");
        var secondCollection = await fixture.Collections.CreateCollectionAsync("Collection two");

        await fixture.Collections.ReorderHierarchyAsync(null,
            [Hierarchy(firstCollection), Hierarchy(secondSet), Hierarchy(secondCollection), Hierarchy(firstSet)]);
        var sets = await fixture.Collections.ListSetsAsync();
        var collections = await fixture.Collections.ListCollectionsAsync();

        Assert.Equal([secondSet.CollectionSetId, firstSet.CollectionSetId], sets.Select(item => item.CollectionSetId));
        Assert.Equal([1, 3], sets.Select(item => item.Ordinal));
        Assert.Equal([firstCollection.CollectionId, secondCollection.CollectionId], collections.Select(item => item.CollectionId));
        Assert.Equal([0, 2], collections.Select(item => item.Ordinal));
    }

    [Fact]
    public async Task MixedOrder_ReparentExactPositionAndRestartPreserveTopLevelAndNestedSequences()
    {
        await using var fixture = await Fixture.CreateAsync(_root);
        var container = await fixture.Collections.CreateSetAsync("Container");
        var nestedSet = await fixture.Collections.CreateSetAsync("Nested Set", container.CollectionSetId);
        var moved = await fixture.Collections.CreateCollectionAsync("Moved", container.CollectionSetId);
        var nestedLast = await fixture.Collections.CreateCollectionAsync("Nested Last", container.CollectionSetId);
        await fixture.Collections.ReorderHierarchyAsync(container.CollectionSetId,
            [Hierarchy(moved), Hierarchy(nestedSet), Hierarchy(nestedLast)]);
        moved = (await fixture.Collections.ListCollectionsAsync(container.CollectionSetId)).Single(item => item.CollectionId == moved.CollectionId);
        var rootCollection = await fixture.Collections.CreateCollectionAsync("Root Collection");

        moved = await fixture.Collections.ReparentCollectionAsync(moved.CollectionId, moved.Revision, null);
        container = Assert.Single(await fixture.Collections.ListSetsAsync());
        rootCollection = (await fixture.Collections.ListCollectionsAsync()).Single(item => item.CollectionId == rootCollection.CollectionId);
        await fixture.Collections.ReorderHierarchyAsync(null,
            [Hierarchy(moved), Hierarchy(container), Hierarchy(rootCollection)]);
        await fixture.ReopenAsync();

        Assert.Equal(0, Assert.Single(await fixture.Collections.ListCollectionsAsync(),
            item => item.CollectionId == moved.CollectionId).Ordinal);
        Assert.Equal(1, Assert.Single(await fixture.Collections.ListSetsAsync()).Ordinal);
        Assert.Equal(2, Assert.Single(await fixture.Collections.ListCollectionsAsync(),
            item => item.CollectionId == rootCollection.CollectionId).Ordinal);
        Assert.Equal(0, Assert.Single(await fixture.Collections.ListSetsAsync(container.CollectionSetId)).Ordinal);
        Assert.Equal(1, Assert.Single(await fixture.Collections.ListCollectionsAsync(container.CollectionSetId)).Ordinal);
    }

    [Fact]
    public async Task DirectReparent_DefaultsToAppendInDestinationMixedChildOrderForBothKinds()
    {
        await using var fixture = await Fixture.CreateAsync(_root);
        var destination = await fixture.Collections.CreateSetAsync("Destination");
        await fixture.Collections.CreateCollectionAsync("Existing collection", destination.CollectionSetId);
        await fixture.Collections.CreateSetAsync("Existing set", destination.CollectionSetId);
        var movedCollection = await fixture.Collections.CreateCollectionAsync("Moved collection");
        var movedSet = await fixture.Collections.CreateSetAsync("Moved set");

        movedCollection = await fixture.Collections.ReparentCollectionAsync(movedCollection.CollectionId,
            movedCollection.Revision, destination.CollectionSetId);
        movedSet = (await fixture.Collections.ListSetsAsync()).Single(item => item.CollectionSetId == movedSet.CollectionSetId);
        movedSet = await fixture.Collections.ReparentSetAsync(movedSet.CollectionSetId,
            movedSet.Revision, destination.CollectionSetId);

        Assert.Equal(2, movedCollection.Ordinal);
        Assert.Equal(3, movedSet.Ordinal);
        Assert.Equal([0, 2], (await fixture.Collections.ListCollectionsAsync(destination.CollectionSetId))
            .Select(item => item.Ordinal));
        Assert.Equal([1, 3], (await fixture.Collections.ListSetsAsync(destination.CollectionSetId))
            .Select(item => item.Ordinal));
    }

    [Fact]
    public async Task ReparentAndDelete_CompactOnlyAffectedSiblingOrderAndNeverDeleteAssets()
    {
        await using var fixture = await Fixture.CreateAsync(_root);
        var asset = fixture.InsertAsset("clip.mp4");
        var set = await fixture.Collections.CreateSetAsync("Set");
        var first = await fixture.Collections.CreateCollectionAsync("First");
        var moved = await fixture.Collections.CreateCollectionAsync("Moved");
        var last = await fixture.Collections.CreateCollectionAsync("Last");
        await fixture.Collections.AddMembershipAsync(moved.CollectionId, asset);

        moved = await fixture.Collections.ReparentCollectionAsync(moved.CollectionId, moved.Revision, set.CollectionSetId);
        Assert.Equal([1, 2], (await fixture.Collections.ListCollectionsAsync()).Select(x => x.Ordinal));
        Assert.Equal(0, moved.Ordinal);
        await fixture.Collections.DeleteCollectionAsync(moved.CollectionId, moved.Revision);

        Assert.Empty(await fixture.Collections.ListMembershipsAsync(moved.CollectionId));
        Assert.Equal(1L, fixture.Scalar("SELECT count(*) FROM MediaAssets WHERE AssetId=$asset;", ("$asset", asset.ToString("D"))));
        Assert.Equal([first.CollectionId, last.CollectionId], (await fixture.Collections.ListCollectionsAsync()).Select(x => x.CollectionId));
    }

    [Fact]
    public async Task AssetDelete_IsRestrictedWhilePreciousMembershipExists()
    {
        await using var fixture = await Fixture.CreateAsync(_root);
        var asset = fixture.InsertAsset("clip.mp4");
        var collection = await fixture.Collections.CreateCollectionAsync("Keep");
        await fixture.Collections.AddMembershipAsync(collection.CollectionId, asset);

        Assert.Throws<SqliteException>(() => fixture.Execute("DELETE FROM MediaAssets WHERE AssetId=$asset;",
            ("$asset", asset.ToString("D"))));
        Assert.Single(await fixture.Collections.ListMembershipsAsync(collection.CollectionId));
    }

    [Fact]
    public async Task BulkMembershipMutation_IsAtomicWhenAnyAssetOrRevisionIsInvalid()
    {
        await using var fixture = await Fixture.CreateAsync(_root);
        var first = fixture.InsertAsset("one.mp4");
        var second = fixture.InsertAsset("two.mp4");
        var collection = await fixture.Collections.CreateCollectionAsync("Atomic");

        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Collections.AddMembershipsAsync(
            collection.CollectionId, [first, Guid.NewGuid()]));
        Assert.Empty(await fixture.Collections.ListMembershipsAsync(collection.CollectionId));

        var added = await fixture.Collections.AddMembershipsAsync(collection.CollectionId, [first, second]);
        await Assert.ThrowsAsync<CollectionConcurrencyException>(() => fixture.Collections.RemoveMembershipsAsync(
            collection.CollectionId,
            [new(first, added[0].Membership.Revision), new(second, added[1].Membership.Revision + 1)]));
        Assert.Equal([first, second],
            (await fixture.Collections.ListMembershipsAsync(collection.CollectionId)).Select(item => item.AssetId));
    }

    [Fact]
    public async Task BackupRestore_PreservesOrganizationAndLeavesPreviewStorageIndependent()
    {
        var locations = LightflowStorageLocations.Create(_root);
        var opened = await new CatalogDatabaseService(locations).CreateNewAsync();
        var session = opened.Session!;
        var collections = new CatalogCollectionOrganizationService(() => session);
        var original = await collections.CreateCollectionAsync("Backup state");
        await session.DisposeAsync();
        var recovery = new SqliteCatalogRecoveryService(locations);
        var backup = await recovery.CreateBackupAsync(locations.CatalogDatabasePath, CatalogBackupKind.Automatic);
        session = (await new CatalogDatabaseService(locations).OpenExistingAsync()).Session!;
        collections = new(() => session);
        var renamed = await collections.RenameCollectionAsync(original.CollectionId, original.Revision, "Current state");
        Directory.CreateDirectory(locations.PreviewsDirectory);
        var previewProbe = Path.Combine(locations.PreviewsDirectory, "independent.preview");
        await File.WriteAllTextAsync(previewProbe, "keep");
        await session.DisposeAsync();

        var installation = await recovery.BeginRestoreAsync(backup.Backup!.Path);
        Assert.True((await installation.Transaction!.CommitAsync()).Succeeded);
        session = (await new CatalogDatabaseService(locations, recovery).OpenExistingAsync()).Session!;
        collections = new(() => session);

        Assert.Equal("Backup state", Assert.Single(await collections.ListCollectionsAsync()).Name);
        Assert.True(File.Exists(previewProbe));
        Assert.NotEqual(renamed.Name, Assert.Single(await collections.ListCollectionsAsync()).Name);
        await session.DisposeAsync();
    }

    [Fact]
    public async Task CatalogRelocation_PreservesOrganizationThroughCoordinatorServiceClosure()
    {
        var started = await LightflowStorageCoordinator.StartAsync(_root);
        await using var coordinator = started.Coordinator!;
        var collection = await coordinator.Collections.CreateCollectionAsync("Relocate me");

        var relocated = await coordinator.RelocateCatalogAsync(Path.Combine(_root, "relocated-catalog"));

        Assert.True(relocated.Succeeded, relocated.Diagnostic);
        Assert.Equal(collection.CollectionId, Assert.Single(await coordinator.Collections.ListCollectionsAsync()).CollectionId);
        Assert.Equal(Path.GetFullPath(Path.Combine(_root, "relocated-catalog")),
            Path.GetFullPath(coordinator.Locations.CatalogDirectory));
    }

    [Fact]
    public async Task MigrationFromVersionNine_RequiresBackupAndCreatesOrganizationSchemaAtomically()
    {
        var locations = LightflowStorageLocations.Create(_root);
        var versionNine = await new CatalogDatabaseService(locations, null, CatalogMigrations.All.Take(9).ToArray()).CreateNewAsync();
        await versionNine.Session!.DisposeAsync();
        var backup = new RecordingBackup();

        var migrated = await new CatalogDatabaseService(locations, backup).OpenExistingAsync();

        Assert.Equal([(9, 11)], backup.Requests);
        Assert.Equal(11, migrated.SchemaVersion);
        Assert.Equal(3L, Scalar(migrated.Session!, "SELECT count(*) FROM sqlite_master WHERE type='table' AND name IN ('CollectionSets','Collections','CollectionAssets');"));
        await migrated.Session!.DisposeAsync();
    }

    [Fact]
    public async Task MigrationFromVersionTen_PreservesSeparateOrdersAsDeterministicMixedOrder()
    {
        var locations = LightflowStorageLocations.Create(_root);
        var versionTen = await new CatalogDatabaseService(locations, null, CatalogMigrations.All.Take(10).ToArray()).CreateNewAsync();
        var session = versionTen.Session!;
        var rootSetA = Guid.NewGuid();
        var rootSetB = Guid.NewGuid();
        var nestedSet = Guid.NewGuid();
        var rootCollectionA = Guid.NewGuid();
        var rootCollectionB = Guid.NewGuid();
        var nestedCollection = Guid.NewGuid();
        var now = DateTime.UtcNow.ToString("O");
        Execute(session, """
            INSERT INTO CollectionSets VALUES ($setA,NULL,'Set A',0,1,$now,$now);
            INSERT INTO CollectionSets VALUES ($setB,NULL,'Set B',1,1,$now,$now);
            INSERT INTO CollectionSets VALUES ($nestedSet,$setA,'Nested Set',0,1,$now,$now);
            INSERT INTO Collections VALUES ($collectionA,NULL,'Collection A',0,1,$now,$now);
            INSERT INTO Collections VALUES ($collectionB,NULL,'Collection B',1,1,$now,$now);
            INSERT INTO Collections VALUES ($nestedCollection,$setA,'Nested Collection',0,1,$now,$now);
            """, ("$setA", rootSetA.ToString("D")), ("$setB", rootSetB.ToString("D")),
            ("$nestedSet", nestedSet.ToString("D")), ("$collectionA", rootCollectionA.ToString("D")),
            ("$collectionB", rootCollectionB.ToString("D")), ("$nestedCollection", nestedCollection.ToString("D")),
            ("$now", now));
        await session.DisposeAsync();
        var backup = new RecordingBackup();

        var migrated = await new CatalogDatabaseService(locations, backup).OpenExistingAsync();
        var collections = new CatalogCollectionOrganizationService(() => migrated.Session);

        Assert.Equal([(10, 11)], backup.Requests);
        Assert.Equal([0, 1], (await collections.ListSetsAsync()).Select(item => item.Ordinal));
        Assert.Equal([2, 3], (await collections.ListCollectionsAsync()).Select(item => item.Ordinal));
        Assert.Equal(0, Assert.Single(await collections.ListSetsAsync(rootSetA)).Ordinal);
        Assert.Equal(1, Assert.Single(await collections.ListCollectionsAsync(rootSetA)).Ordinal);
        Assert.Throws<SqliteException>(() => Execute(migrated.Session!,
            "UPDATE Collections SET Ordinal=0 WHERE CollectionId=$id;", ("$id", rootCollectionA.ToString("D"))));
        await migrated.Session!.DisposeAsync();
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly LightflowStorageLocations _locations;
        private CatalogDatabaseSession _session = null!;
        private readonly Guid _rootId = Guid.NewGuid();
        public ICollectionOrganizationService Collections { get; private set; } = null!;

        private Fixture(string root) => _locations = LightflowStorageLocations.Create(root);
        public static async Task<Fixture> CreateAsync(string root)
        {
            var fixture = new Fixture(root);
            fixture._session = (await new CatalogDatabaseService(fixture._locations).CreateNewAsync()).Session!;
            fixture.Execute("""
                INSERT INTO MediaRoots (RootId,DisplayName,SourceStatus,CreatedUtc,UpdatedUtc)
                VALUES ($id,'Media','online',$now,$now);
                INSERT INTO MediaRootMappings (MappingId,RootId,MachineId,PhysicalPath,SourceStatus,CreatedUtc,UpdatedUtc)
                VALUES ($mapping,$id,'machine','X:\\media','online',$now,$now);
                """, ("$id", fixture._rootId.ToString("D")), ("$mapping", Guid.NewGuid().ToString("D")),
                ("$now", DateTime.UtcNow.ToString("O")));
            fixture.Collections = new CatalogCollectionOrganizationService(() => fixture._session);
            return fixture;
        }

        public Guid InsertAsset(string relativePath)
        {
            var id = Guid.NewGuid();
            Execute("""
                INSERT INTO MediaAssets
                    (AssetId,RootId,RelativePath,RelativePathKey,MediaType,FileSizeBytes,LastWriteUtcTicks,
                     SourceStatus,CreatedUtc,UpdatedUtc)
                VALUES ($id,$root,$path,$key,'video',1,1,'available',$now,$now);
                """, ("$id", id.ToString("D")), ("$root", _rootId.ToString("D")), ("$path", relativePath),
                ("$key", relativePath.ToUpperInvariant()), ("$now", DateTime.UtcNow.ToString("O")));
            return id;
        }

        public async Task ReopenAsync()
        {
            await _session.DisposeAsync();
            _session = (await new CatalogDatabaseService(_locations).OpenExistingAsync()).Session!;
            Collections = new CatalogCollectionOrganizationService(() => _session);
        }

        public void Execute(string sql, params (string Name, object Value)[] parameters)
        {
            using var connection = _session.OpenConnection();
            using var command = connection.CreateCommand(); command.CommandText = sql;
            foreach (var parameter in parameters) command.Parameters.AddWithValue(parameter.Name, parameter.Value);
            command.ExecuteNonQuery();
        }

        public long Scalar(string sql, params (string Name, object Value)[] parameters)
        {
            using var connection = _session.OpenConnection();
            using var command = connection.CreateCommand(); command.CommandText = sql;
            foreach (var parameter in parameters) command.Parameters.AddWithValue(parameter.Name, parameter.Value);
            return Convert.ToInt64(command.ExecuteScalar());
        }

        public async ValueTask DisposeAsync() => await _session.DisposeAsync();
    }

    private sealed class RecordingBackup : ICatalogMigrationBackup
    {
        public List<(int From, int To)> Requests { get; } = [];
        public Task<CatalogMigrationBackupResult> PrepareForMigrationAsync(string catalogDatabasePath,
            int currentSchemaVersion, int targetSchemaVersion, CancellationToken cancellationToken)
        {
            Requests.Add((currentSchemaVersion, targetSchemaVersion));
            return Task.FromResult(CatalogMigrationBackupResult.Success());
        }
    }

    private static CollectionHierarchyOrder Hierarchy(CollectionSet item) => new(
        CollectionHierarchyItemKind.Set, item.CollectionSetId, item.Revision);

    private static CollectionHierarchyOrder Hierarchy(MediaCollection item) => new(
        CollectionHierarchyItemKind.Collection, item.CollectionId, item.Revision);

    private static object? Scalar(CatalogDatabaseSession session, string sql)
    {
        using var connection = session.OpenConnection();
        using var command = connection.CreateCommand(); command.CommandText = sql;
        return command.ExecuteScalar();
    }

    private static void Execute(CatalogDatabaseSession session, string sql,
        params (string Name, object Value)[] parameters)
    {
        using var connection = session.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var parameter in parameters) command.Parameters.AddWithValue(parameter.Name, parameter.Value);
        command.ExecuteNonQuery();
    }

    public Task InitializeAsync() { Directory.CreateDirectory(_root); return Task.CompletedTask; }
    public Task DisposeAsync() { SqliteConnection.ClearAllPools(); try { Directory.Delete(_root, true); } catch { } return Task.CompletedTask; }
}
