using LightflowStudio;
using Microsoft.Data.Sqlite;
using Xunit;

namespace LightflowStudio.Tests;

/// <summary>
/// #124 (revised): coverage for the durable, Catalog-backed recursive-root model that replaced the earlier
/// continuous-outline design — pure inheritance/normalization logic (<see cref="BrowserRecursiveRootLogic"/>),
/// the service that enforces the four normalization rules over it (<see cref="BrowserRecursiveRootService"/>),
/// and real SQLite persistence/migration behavior (<see cref="CatalogBrowserRecursiveRootRepository"/>).
/// </summary>
public sealed class BrowserRecursiveRootLogicTests
{
    [Fact]
    public void IsEffectivelyRecursive_TrueForTheRootItself()
    {
        var rootId = Guid.NewGuid();
        var roots = new[] { new BrowserRecursiveRoot(Guid.NewGuid(), rootId, "2026") };

        Assert.True(BrowserRecursiveRootLogic.IsEffectivelyRecursive(roots, rootId, "2026"));
    }

    [Fact]
    public void IsEffectivelyRecursive_TrueForADeepDescendant()
    {
        var rootId = Guid.NewGuid();
        var roots = new[] { new BrowserRecursiveRoot(Guid.NewGuid(), rootId, "2026") };

        Assert.True(BrowserRecursiveRootLogic.IsEffectivelyRecursive(roots, rootId, "2026/August/Wedding"));
    }

    [Fact]
    public void IsEffectivelyRecursive_FalseForASiblingFolder()
    {
        var rootId = Guid.NewGuid();
        var roots = new[] { new BrowserRecursiveRoot(Guid.NewGuid(), rootId, "2026") };

        Assert.False(BrowserRecursiveRootLogic.IsEffectivelyRecursive(roots, rootId, "2025"));
    }

    [Fact]
    public void IsEffectivelyRecursive_FalseForTheSameRelativeFolderUnderADifferentMediaRoot()
    {
        var roots = new[] { new BrowserRecursiveRoot(Guid.NewGuid(), Guid.NewGuid(), "2026") };

        Assert.False(BrowserRecursiveRootLogic.IsEffectivelyRecursive(roots, Guid.NewGuid(), "2026"));
    }

    [Fact]
    public void IsEffectivelyRecursive_TrueAtAMediaRootsOwnTopLevelWhenItIsItselfTheStoredRoot()
    {
        var rootId = Guid.NewGuid();
        var roots = new[] { new BrowserRecursiveRoot(Guid.NewGuid(), rootId, "") };

        Assert.True(BrowserRecursiveRootLogic.IsEffectivelyRecursive(roots, rootId, ""));
        Assert.True(BrowserRecursiveRootLogic.IsEffectivelyRecursive(roots, rootId, "Anything/Anywhere"));
    }

    [Fact]
    public void IsEffectivelyRecursive_IsSegmentAwareAndNeverTreatsTripAsAnAncestorOfTrips()
    {
        var rootId = Guid.NewGuid();
        var roots = new[] { new BrowserRecursiveRoot(Guid.NewGuid(), rootId, "Trip") };

        Assert.False(BrowserRecursiveRootLogic.IsEffectivelyRecursive(roots, rootId, "Trips"));
    }

    [Fact]
    public void GoverningRoots_FindsTheAncestorRootThatCoversADescendantFolder()
    {
        var rootId = Guid.NewGuid();
        var root = new BrowserRecursiveRoot(Guid.NewGuid(), rootId, "2026");

        var governing = BrowserRecursiveRootLogic.GoverningRoots([root], rootId, "2026/August/Wedding");

        Assert.Equal([root], governing);
    }

    [Fact]
    public void GoverningRoots_IsEmptyWhenNothingCoversTheFolder()
    {
        var rootId = Guid.NewGuid();
        var root = new BrowserRecursiveRoot(Guid.NewGuid(), rootId, "2026");

        Assert.Empty(BrowserRecursiveRootLogic.GoverningRoots([root], rootId, "2025"));
    }

    [Fact]
    public void RedundantDescendants_FindsExistingRootsThatANewAncestorRootWouldAbsorb()
    {
        var rootId = Guid.NewGuid();
        var descendant = new BrowserRecursiveRoot(Guid.NewGuid(), rootId, "2026/August");
        var unrelated = new BrowserRecursiveRoot(Guid.NewGuid(), rootId, "2025/Fair");

        var redundant = BrowserRecursiveRootLogic.RedundantDescendants([descendant, unrelated], rootId, "2026");

        Assert.Equal([descendant], redundant);
    }

    [Fact]
    public void RedundantDescendants_IncludesAnExactDuplicateOfTheNewRootItself()
    {
        var rootId = Guid.NewGuid();
        var existing = new BrowserRecursiveRoot(Guid.NewGuid(), rootId, "2026");

        Assert.Equal([existing], BrowserRecursiveRootLogic.RedundantDescendants([existing], rootId, "2026"));
    }
}

public sealed class BrowserRecursiveRootServiceTests
{
    [Fact]
    public async Task EnableAsync_CreatesANewRoot()
    {
        var service = new BrowserRecursiveRootService(new InMemoryRepository());
        var rootId = Guid.NewGuid();

        await service.EnableAsync(rootId, "2026");

        var stored = Assert.Single(await service.ListAsync());
        Assert.Equal(rootId, stored.RootId);
        Assert.Equal("2026", stored.RelativeFolder);
    }

    [Fact]
    public async Task EnableAsync_AtAMediaRootsOwnTopLevelUsesAnEmptyRelativeFolder()
    {
        var service = new BrowserRecursiveRootService(new InMemoryRepository());
        var rootId = Guid.NewGuid();

        await service.EnableAsync(rootId, "");

        Assert.Equal("", Assert.Single(await service.ListAsync()).RelativeFolder);
    }

    [Fact]
    public async Task EnableAsync_NormalizationRule1_ReEnablingAnAlreadyCoveredDescendantIsANoOp()
    {
        var service = new BrowserRecursiveRootService(new InMemoryRepository());
        var rootId = Guid.NewGuid();
        await service.EnableAsync(rootId, "2026");

        await service.EnableAsync(rootId, "2026/August");

        var roots = await service.ListAsync();
        var stored = Assert.Single(roots);
        Assert.Equal("2026", stored.RelativeFolder); // no redundant child root created
    }

    [Fact]
    public async Task EnableAsync_NormalizationRule2_EstablishingAnAncestorRemovesRedundantDescendantRoots()
    {
        var service = new BrowserRecursiveRootService(new InMemoryRepository());
        var rootId = Guid.NewGuid();
        await service.EnableAsync(rootId, "2026/August");
        await service.EnableAsync(rootId, "2026/December");

        await service.EnableAsync(rootId, "2026");

        var stored = Assert.Single(await service.ListAsync());
        Assert.Equal("2026", stored.RelativeFolder);
    }

    [Fact]
    public async Task EnableAsync_NormalizationRule3_DisjointRootsStayIndependent()
    {
        var service = new BrowserRecursiveRootService(new InMemoryRepository());
        var rootId = Guid.NewGuid();
        await service.EnableAsync(rootId, "2025/Fair");

        await service.EnableAsync(rootId, "2026");

        var roots = await service.ListAsync();
        Assert.Equal(2, roots.Count);
        Assert.Contains(roots, root => root.RelativeFolder == "2025/Fair");
        Assert.Contains(roots, root => root.RelativeFolder == "2026");
    }

    [Fact]
    public async Task DisableAsync_RemovesTheRootWhenTheFolderIsTheRootItself()
    {
        var service = new BrowserRecursiveRootService(new InMemoryRepository());
        var rootId = Guid.NewGuid();
        await service.EnableAsync(rootId, "2026");

        await service.DisableAsync(rootId, "2026");

        Assert.Empty(await service.ListAsync());
    }

    [Fact]
    public async Task DisableAsync_NormalizationRule4_FromAnInheritedDescendantRemovesTheGoverningAncestorRoot()
    {
        var service = new BrowserRecursiveRootService(new InMemoryRepository());
        var rootId = Guid.NewGuid();
        await service.EnableAsync(rootId, "2026");

        await service.DisableAsync(rootId, "2026/August/Wedding");

        Assert.Empty(await service.ListAsync());
    }

    [Fact]
    public async Task DisableAsync_FromAnInheritedDescendantAlsoRestoresEveryOtherDescendantOfTheRemovedRoot()
    {
        // Disabling from Wedding (2026/August/Wedding) must remove the *whole* 2026 root — January and
        // December (siblings of August, never individually toggled) must stop being recursive too.
        var service = new BrowserRecursiveRootService(new InMemoryRepository());
        var rootId = Guid.NewGuid();
        await service.EnableAsync(rootId, "2026");

        await service.DisableAsync(rootId, "2026/August/Wedding");

        var roots = await service.ListAsync();
        Assert.False(BrowserRecursiveRootLogic.IsEffectivelyRecursive(roots, rootId, "2026/December"));
        Assert.False(BrowserRecursiveRootLogic.IsEffectivelyRecursive(roots, rootId, "2026/January"));
    }

    [Fact]
    public async Task DisableAsync_NeverCreatesAPerFolderOffOverrideWhenNothingGovernsTheFolder()
    {
        var repository = new InMemoryRepository();
        var service = new BrowserRecursiveRootService(repository);
        var rootId = Guid.NewGuid();

        await service.DisableAsync(rootId, "2026/August");

        Assert.Empty(await service.ListAsync());
        Assert.Empty(repository.DeleteCalls); // a true no-op: not even an empty delete round-trip
    }

    [Fact]
    public async Task DisableAsync_OnlyRemovesTheRootGoverningTheGivenFolderNeverAnUnrelatedDisjointRoot()
    {
        var service = new BrowserRecursiveRootService(new InMemoryRepository());
        var rootId = Guid.NewGuid();
        await service.EnableAsync(rootId, "2025/Fair");
        await service.EnableAsync(rootId, "2026");

        await service.DisableAsync(rootId, "2026/August");

        var stored = Assert.Single(await service.ListAsync());
        Assert.Equal("2025/Fair", stored.RelativeFolder);
    }

    [Fact]
    public async Task EnableThenDisableThenEnable_LeavesExactlyOneRootRatherThanAccumulatingStaleRows()
    {
        var service = new BrowserRecursiveRootService(new InMemoryRepository());
        var rootId = Guid.NewGuid();
        await service.EnableAsync(rootId, "2026");
        await service.DisableAsync(rootId, "2026");

        await service.EnableAsync(rootId, "2026");

        Assert.Single(await service.ListAsync());
    }

    [Fact]
    public async Task EnableAsync_NormalizesTheFolderPathBeforeStoringIt()
    {
        var service = new BrowserRecursiveRootService(new InMemoryRepository());
        var rootId = Guid.NewGuid();

        await service.EnableAsync(rootId, @"2026\August\");

        Assert.Equal("2026/August", Assert.Single(await service.ListAsync()).RelativeFolder);
    }

    private sealed class InMemoryRepository : IBrowserRecursiveRootRepository
    {
        private readonly List<BrowserRecursiveRoot> _roots = [];
        public List<IReadOnlyCollection<Guid>> DeleteCalls { get; } = [];

        public Task<IReadOnlyList<BrowserRecursiveRoot>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<BrowserRecursiveRoot>>([.. _roots]);

        public Task CreateAsync(Guid rootId, string relativeFolder, CancellationToken cancellationToken = default)
        {
            _roots.Add(new(Guid.NewGuid(), rootId, relativeFolder));
            return Task.CompletedTask;
        }

        public Task<int> DeleteAsync(IReadOnlyCollection<Guid> scopeIds, CancellationToken cancellationToken = default)
        {
            DeleteCalls.Add(scopeIds);
            return Task.FromResult(_roots.RemoveAll(root => scopeIds.Contains(root.ScopeId)));
        }
    }
}

/// <summary>Real-SQLite coverage: identity shape, idempotency, cascade behavior, migration compatibility, and Catalog-unavailable handling.</summary>
public sealed class BrowserRecursiveRootCatalogTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"lightflow-recursive-roots-{Guid.NewGuid():N}");

    [Fact]
    public async Task CreateListDelete_RoundTripsThroughTheRealSqliteSchema()
    {
        var session = await OpenCatalogAsync();
        var rootId = InsertMediaRoot(session, "Library");
        var repository = new CatalogBrowserRecursiveRootRepository(() => session);

        await repository.CreateAsync(rootId, "2026");
        var stored = Assert.Single(await repository.ListAsync());
        Assert.Equal(rootId, stored.RootId);
        Assert.Equal("2026", stored.RelativeFolder);

        await repository.DeleteAsync([stored.ScopeId]);
        Assert.Empty(await repository.ListAsync());
        await session.DisposeAsync();
    }

    [Fact]
    public async Task Create_IsIdempotentAtTheDatabaseLevelForTheSameRootAndFolder()
    {
        var session = await OpenCatalogAsync();
        var rootId = InsertMediaRoot(session, "Library");
        var repository = new CatalogBrowserRecursiveRootRepository(() => session);

        await repository.CreateAsync(rootId, "2026");
        await repository.CreateAsync(rootId, "2026"); // ON CONFLICT(RootId, RelativeFolderKey) DO NOTHING

        Assert.Single(await repository.ListAsync());
        await session.DisposeAsync();
    }

    [Fact]
    public async Task Create_TreatsTheSameFolderAsTheSameRootRegardlessOfCase()
    {
        var session = await OpenCatalogAsync();
        var rootId = InsertMediaRoot(session, "Library");
        var repository = new CatalogBrowserRecursiveRootRepository(() => session);

        await repository.CreateAsync(rootId, "2026/August");
        await repository.CreateAsync(rootId, "2026/AUGUST");

        Assert.Single(await repository.ListAsync());
        await session.DisposeAsync();
    }

    [Fact]
    public async Task CreateAtAMediaRootsOwnTopLevel_PersistsAnEmptyRelativeFolder()
    {
        var session = await OpenCatalogAsync();
        var rootId = InsertMediaRoot(session, "Library");
        var repository = new CatalogBrowserRecursiveRootRepository(() => session);

        await repository.CreateAsync(rootId, "");

        Assert.Equal("", Assert.Single(await repository.ListAsync()).RelativeFolder);
        await session.DisposeAsync();
    }

    [Fact]
    public async Task StoredIdentity_IsRootAgnosticTheSchemaHasNoPhysicalPathColumn()
    {
        var session = await OpenCatalogAsync();
        using var connection = session.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT sql FROM sqlite_master WHERE name = 'BrowserRecursiveScopes';";
        var schema = Convert.ToString(command.ExecuteScalar());

        Assert.NotNull(schema);
        Assert.DoesNotContain("PhysicalPath", schema);
        await session.DisposeAsync();
    }

    [Fact]
    public async Task DeletingTheMediaRoot_CascadesToRemoveItsRecursiveRoots()
    {
        var session = await OpenCatalogAsync();
        var rootId = InsertMediaRoot(session, "Library");
        var repository = new CatalogBrowserRecursiveRootRepository(() => session);
        await repository.CreateAsync(rootId, "2026");

        using (var connection = session.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "DELETE FROM MediaRoots WHERE RootId = $id;";
            command.Parameters.AddWithValue("$id", rootId.ToString("D"));
            command.ExecuteNonQuery();
        }

        Assert.Empty(await repository.ListAsync());
        await session.DisposeAsync();
    }

    [Fact]
    public async Task RemappingTheMediaRootsPhysicalPath_NeverAffectsItsRecursiveRootsSinceIdentityIsRootIdOnly()
    {
        // Media Root remapping only ever touches MediaRootMappings.PhysicalPath — RootId (the only identity
        // BrowserRecursiveScopes references, via its FOREIGN KEY) never changes, so a remap can't orphan a root.
        var session = await OpenCatalogAsync();
        var rootId = InsertMediaRoot(session, "Library");
        var repository = new CatalogBrowserRecursiveRootRepository(() => session);
        await repository.CreateAsync(rootId, "2026");

        InsertMediaRootMapping(session, rootId, @"D:\Remapped\Library");

        var stored = Assert.Single(await repository.ListAsync());
        Assert.Equal(rootId, stored.RootId);
        await session.DisposeAsync();
    }

    [Fact]
    public async Task Repository_ThrowsInvalidOperationExceptionWhenTheCatalogSessionIsUnavailable()
    {
        // The same exception type/message BrowserNavigationSession's existing generic Catalog-failure handling
        // already converts into an honest Browser failure state — no second Catalog-unavailable UI path needed.
        var repository = new CatalogBrowserRecursiveRootRepository(() => null);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => repository.ListAsync());
        Assert.Equal("The Catalog is unavailable.", exception.Message);
    }

    [Fact]
    public async Task ExistingVersion1Catalog_MigratesForwardAndCanImmediatelyStoreARecursiveRoot()
    {
        // Confirms migration 2 (BrowserRecursiveScopes) applies cleanly on top of an already-existing
        // version-1 catalog, exactly like any real user's Catalog created before this feature shipped.
        var locations = LightflowStorageLocations.Create(_root);
        var v1Only = new CatalogDatabaseService(locations, null, [CatalogMigrations.All[0]]);
        var created = await v1Only.CreateNewAsync();
        Assert.Equal(1, created.SchemaVersion);
        await created.Session!.DisposeAsync();

        var migrated = await new CatalogDatabaseService(locations, new AlwaysApproveBackup()).OpenExistingAsync();
        Assert.Equal(CatalogOpenStatus.Ready, migrated.Status);
        Assert.Equal(CatalogMigrations.All[^1].Version, migrated.SchemaVersion);

        var rootId = InsertMediaRoot(migrated.Session!, "Library");
        var repository = new CatalogBrowserRecursiveRootRepository(() => migrated.Session);
        await repository.CreateAsync(rootId, "2026");

        Assert.Single(await repository.ListAsync());
        await migrated.Session!.DisposeAsync();
    }

    private async Task<CatalogDatabaseSession> OpenCatalogAsync()
    {
        var locations = LightflowStorageLocations.Create(_root);
        var result = await new CatalogDatabaseService(locations).CreateNewAsync();
        return result.Session!;
    }

    private static Guid InsertMediaRoot(CatalogDatabaseSession session, string name)
    {
        var id = Guid.NewGuid();
        using var connection = session.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO MediaRoots (RootId, DisplayName, SourceStatus, CreatedUtc, UpdatedUtc)
            VALUES ($id, $name, 'online', $now, $now);
            """;
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
        return id;
    }

    private static void InsertMediaRootMapping(CatalogDatabaseSession session, Guid rootId, string physicalPath)
    {
        using var connection = session.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO MediaRootMappings (MappingId, RootId, MachineId, PhysicalPath, SourceStatus, CreatedUtc, UpdatedUtc)
            VALUES ($mapping, $root, $machine, $path, 'online', $now, $now);
            """;
        command.Parameters.AddWithValue("$mapping", Guid.NewGuid().ToString("D"));
        command.Parameters.AddWithValue("$root", rootId.ToString("D"));
        command.Parameters.AddWithValue("$machine", "test-machine");
        command.Parameters.AddWithValue("$path", physicalPath);
        command.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }

    private sealed class AlwaysApproveBackup : ICatalogMigrationBackup
    {
        public Task<CatalogMigrationBackupResult> PrepareForMigrationAsync(string catalogDatabasePath,
            int currentSchemaVersion, int targetSchemaVersion, CancellationToken cancellationToken) =>
            Task.FromResult(CatalogMigrationBackupResult.Success());
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, true); } catch { }
    }
}
