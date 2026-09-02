using System.Globalization;
using Microsoft.Data.Sqlite;

namespace LightflowStudio;

internal sealed record CollectionSet(
    Guid CollectionSetId, Guid? ParentCollectionSetId, string Name, int Ordinal, long Revision,
    DateTimeOffset CreatedUtc, DateTimeOffset UpdatedUtc);

internal sealed record MediaCollection(
    Guid CollectionId, Guid? ParentCollectionSetId, string Name, int Ordinal, long Revision,
    DateTimeOffset CreatedUtc, DateTimeOffset UpdatedUtc);

internal sealed record CollectionMembership(
    Guid CollectionId, Guid AssetId, int Ordinal, long Revision,
    DateTimeOffset CreatedUtc, DateTimeOffset UpdatedUtc);

internal sealed record CollectionOrder(Guid Id, long ExpectedRevision);
internal sealed record CollectionMembershipCreateResult(CollectionMembership Membership, bool Created);

internal sealed class CollectionConcurrencyException(string message) : InvalidOperationException(message);
internal sealed class CollectionHierarchyException(string message) : InvalidOperationException(message);
internal sealed class CollectionNotEmptyException(string message) : InvalidOperationException(message);

internal interface ICollectionOrganizationService
{
    Task<IReadOnlyList<CollectionSet>> ListSetsAsync(Guid? parentCollectionSetId = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MediaCollection>> ListCollectionsAsync(Guid? parentCollectionSetId = null, CancellationToken cancellationToken = default);
    Task<CollectionSet?> GetSetAsync(Guid collectionSetId, CancellationToken cancellationToken = default);
    Task<MediaCollection?> GetCollectionAsync(Guid collectionId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CollectionMembership>> ListMembershipsAsync(Guid collectionId, CancellationToken cancellationToken = default);
    Task<CollectionSet> CreateSetAsync(string name, Guid? parentCollectionSetId = null, CancellationToken cancellationToken = default);
    Task<MediaCollection> CreateCollectionAsync(string name, Guid? parentCollectionSetId = null, CancellationToken cancellationToken = default);
    Task<CollectionSet> RenameSetAsync(Guid collectionSetId, long expectedRevision, string name, CancellationToken cancellationToken = default);
    Task<MediaCollection> RenameCollectionAsync(Guid collectionId, long expectedRevision, string name, CancellationToken cancellationToken = default);
    Task<CollectionSet> ReparentSetAsync(Guid collectionSetId, long expectedRevision, Guid? parentCollectionSetId, CancellationToken cancellationToken = default);
    Task<MediaCollection> ReparentCollectionAsync(Guid collectionId, long expectedRevision, Guid? parentCollectionSetId, CancellationToken cancellationToken = default);
    Task DeleteSetAsync(Guid collectionSetId, long expectedRevision, CancellationToken cancellationToken = default);
    Task DeleteCollectionAsync(Guid collectionId, long expectedRevision, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CollectionSet>> ReorderSetsAsync(Guid? parentCollectionSetId, IReadOnlyList<CollectionOrder> order, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MediaCollection>> ReorderCollectionsAsync(Guid? parentCollectionSetId, IReadOnlyList<CollectionOrder> order, CancellationToken cancellationToken = default);
    Task<CollectionMembershipCreateResult> AddMembershipAsync(Guid collectionId, Guid assetId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CollectionMembershipCreateResult>> AddMembershipsAsync(Guid collectionId, IReadOnlyList<Guid> assetIds, CancellationToken cancellationToken = default);
    Task RemoveMembershipAsync(Guid collectionId, Guid assetId, long expectedRevision, CancellationToken cancellationToken = default);
    Task RemoveMembershipsAsync(Guid collectionId, IReadOnlyList<CollectionOrder> memberships, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CollectionMembership>> ReorderMembershipsAsync(Guid collectionId, IReadOnlyList<CollectionOrder> order, CancellationToken cancellationToken = default);
}

/// <summary>
/// Catalog-only boundary for precious manual Collection organization. Paths, Preview state, Browser state,
/// and saved-query semantics deliberately do not enter these contracts.
/// </summary>
internal sealed class CatalogCollectionOrganizationService(
    Func<CatalogDatabaseSession?> session, Func<DateTimeOffset>? utcNow = null) : ICollectionOrganizationService
{
    private readonly Func<DateTimeOffset> _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    private readonly SemaphoreSlim _mutations = new(1, 1);

    public Task<IReadOnlyList<CollectionSet>> ListSetsAsync(Guid? parentCollectionSetId = null,
        CancellationToken cancellationToken = default) => RunReadAsync<IReadOnlyList<CollectionSet>>(() =>
    {
        using var connection = RequireSession().OpenConnection();
        return ReadSets(connection, null, parentCollectionSetId);
    }, cancellationToken);

    public Task<IReadOnlyList<MediaCollection>> ListCollectionsAsync(Guid? parentCollectionSetId = null,
        CancellationToken cancellationToken = default) => RunReadAsync<IReadOnlyList<MediaCollection>>(() =>
    {
        using var connection = RequireSession().OpenConnection();
        return ReadCollections(connection, null, parentCollectionSetId);
    }, cancellationToken);

    public Task<IReadOnlyList<CollectionMembership>> ListMembershipsAsync(Guid collectionId,
        CancellationToken cancellationToken = default) => RunReadAsync<IReadOnlyList<CollectionMembership>>(() =>
    {
        using var connection = RequireSession().OpenConnection();
        return ReadMemberships(connection, null, collectionId);
    }, cancellationToken);

    public Task<CollectionSet?> GetSetAsync(Guid collectionSetId, CancellationToken cancellationToken = default) =>
        RunReadAsync(() =>
        {
            using var connection = RequireSession().OpenConnection();
            return ReadSet(connection, null, collectionSetId);
        }, cancellationToken);

    public Task<MediaCollection?> GetCollectionAsync(Guid collectionId, CancellationToken cancellationToken = default) =>
        RunReadAsync(() =>
        {
            using var connection = RequireSession().OpenConnection();
            return ReadCollection(connection, null, collectionId);
        }, cancellationToken);

    public Task<CollectionSet> CreateSetAsync(string name, Guid? parentCollectionSetId = null,
        CancellationToken cancellationToken = default) => MutateAsync((connection, transaction) =>
    {
        name = NormalizeName(name, nameof(name));
        EnsureParentExists(connection, transaction, parentCollectionSetId);
        var now = FormatUtc(_utcNow());
        var id = Guid.NewGuid();
        var ordinal = NextOrdinal(connection, transaction, "CollectionSets", "ParentCollectionSetId", parentCollectionSetId);
        Execute(connection, transaction, """
            INSERT INTO CollectionSets
                (CollectionSetId,ParentCollectionSetId,Name,Ordinal,Revision,CreatedUtc,UpdatedUtc)
            VALUES ($id,$parent,$name,$ordinal,1,$now,$now);
            """, ("$id", id.ToString("D")), ("$parent", Db(parentCollectionSetId)), ("$name", name),
            ("$ordinal", ordinal), ("$now", now));
        return ReadSet(connection, transaction, id)!;
    }, cancellationToken);

    public Task<MediaCollection> CreateCollectionAsync(string name, Guid? parentCollectionSetId = null,
        CancellationToken cancellationToken = default) => MutateAsync((connection, transaction) =>
    {
        name = NormalizeName(name, nameof(name));
        EnsureParentExists(connection, transaction, parentCollectionSetId);
        var now = FormatUtc(_utcNow());
        var id = Guid.NewGuid();
        var ordinal = NextOrdinal(connection, transaction, "Collections", "ParentCollectionSetId", parentCollectionSetId);
        Execute(connection, transaction, """
            INSERT INTO Collections
                (CollectionId,ParentCollectionSetId,Name,Ordinal,Revision,CreatedUtc,UpdatedUtc)
            VALUES ($id,$parent,$name,$ordinal,1,$now,$now);
            """, ("$id", id.ToString("D")), ("$parent", Db(parentCollectionSetId)), ("$name", name),
            ("$ordinal", ordinal), ("$now", now));
        return ReadCollection(connection, transaction, id)!;
    }, cancellationToken);

    public Task<CollectionSet> RenameSetAsync(Guid collectionSetId, long expectedRevision, string name,
        CancellationToken cancellationToken = default) => MutateAsync((connection, transaction) =>
    {
        name = NormalizeName(name, nameof(name));
        if (Execute(connection, transaction, """
                UPDATE CollectionSets SET Name=$name,Revision=Revision+1,UpdatedUtc=$now
                WHERE CollectionSetId=$id AND Revision=$revision;
                """, ("$name", name), ("$now", FormatUtc(_utcNow())), ("$id", collectionSetId.ToString("D")),
                ("$revision", expectedRevision)) != 1)
            throw Changed("Collection Set", "rename");
        return ReadSet(connection, transaction, collectionSetId)!;
    }, cancellationToken);

    public Task<MediaCollection> RenameCollectionAsync(Guid collectionId, long expectedRevision, string name,
        CancellationToken cancellationToken = default) => MutateAsync((connection, transaction) =>
    {
        name = NormalizeName(name, nameof(name));
        if (Execute(connection, transaction, """
                UPDATE Collections SET Name=$name,Revision=Revision+1,UpdatedUtc=$now
                WHERE CollectionId=$id AND Revision=$revision;
                """, ("$name", name), ("$now", FormatUtc(_utcNow())), ("$id", collectionId.ToString("D")),
                ("$revision", expectedRevision)) != 1)
            throw Changed("Collection", "rename");
        return ReadCollection(connection, transaction, collectionId)!;
    }, cancellationToken);

    public Task<CollectionSet> ReparentSetAsync(Guid collectionSetId, long expectedRevision,
        Guid? parentCollectionSetId, CancellationToken cancellationToken = default) => MutateAsync((connection, transaction) =>
    {
        var current = ReadSet(connection, transaction, collectionSetId) ?? throw Changed("Collection Set", "move");
        if (current.Revision != expectedRevision) throw Changed("Collection Set", "move");
        if (current.ParentCollectionSetId == parentCollectionSetId) return current;
        EnsureParentExists(connection, transaction, parentCollectionSetId);
        EnsureAcyclic(connection, transaction, collectionSetId, parentCollectionSetId);
        var ordinal = NextOrdinal(connection, transaction, "CollectionSets", "ParentCollectionSetId", parentCollectionSetId);
        if (Execute(connection, transaction, """
                UPDATE CollectionSets SET ParentCollectionSetId=$parent,Ordinal=$ordinal,
                    Revision=Revision+1,UpdatedUtc=$now
                WHERE CollectionSetId=$id AND Revision=$revision;
                """, ("$parent", Db(parentCollectionSetId)), ("$ordinal", ordinal), ("$now", FormatUtc(_utcNow())),
                ("$id", collectionSetId.ToString("D")), ("$revision", expectedRevision)) != 1)
            throw Changed("Collection Set", "move");
        NormalizeOrdinals(connection, transaction, "CollectionSets", "CollectionSetId", "ParentCollectionSetId",
            current.ParentCollectionSetId, FormatUtc(_utcNow()));
        return ReadSet(connection, transaction, collectionSetId)!;
    }, cancellationToken);

    public Task<MediaCollection> ReparentCollectionAsync(Guid collectionId, long expectedRevision,
        Guid? parentCollectionSetId, CancellationToken cancellationToken = default) => MutateAsync((connection, transaction) =>
    {
        var current = ReadCollection(connection, transaction, collectionId) ?? throw Changed("Collection", "move");
        if (current.Revision != expectedRevision) throw Changed("Collection", "move");
        if (current.ParentCollectionSetId == parentCollectionSetId) return current;
        EnsureParentExists(connection, transaction, parentCollectionSetId);
        var ordinal = NextOrdinal(connection, transaction, "Collections", "ParentCollectionSetId", parentCollectionSetId);
        if (Execute(connection, transaction, """
                UPDATE Collections SET ParentCollectionSetId=$parent,Ordinal=$ordinal,
                    Revision=Revision+1,UpdatedUtc=$now
                WHERE CollectionId=$id AND Revision=$revision;
                """, ("$parent", Db(parentCollectionSetId)), ("$ordinal", ordinal), ("$now", FormatUtc(_utcNow())),
                ("$id", collectionId.ToString("D")), ("$revision", expectedRevision)) != 1)
            throw Changed("Collection", "move");
        NormalizeOrdinals(connection, transaction, "Collections", "CollectionId", "ParentCollectionSetId",
            current.ParentCollectionSetId, FormatUtc(_utcNow()));
        return ReadCollection(connection, transaction, collectionId)!;
    }, cancellationToken);

    public Task DeleteSetAsync(Guid collectionSetId, long expectedRevision,
        CancellationToken cancellationToken = default) => MutateAsync<object?>((connection, transaction) =>
    {
        var current = ReadSet(connection, transaction, collectionSetId) ?? throw Changed("Collection Set", "deletion");
        if (current.Revision != expectedRevision) throw Changed("Collection Set", "deletion");
        if (ScalarLong(connection, transaction,
                "SELECT count(*) FROM CollectionSets WHERE ParentCollectionSetId=$id;", ("$id", collectionSetId.ToString("D"))) != 0 ||
            ScalarLong(connection, transaction,
                "SELECT count(*) FROM Collections WHERE ParentCollectionSetId=$id;", ("$id", collectionSetId.ToString("D"))) != 0)
            throw new CollectionNotEmptyException("The Collection Set must be empty before it can be deleted.");
        if (Execute(connection, transaction,
                "DELETE FROM CollectionSets WHERE CollectionSetId=$id AND Revision=$revision;",
                ("$id", collectionSetId.ToString("D")), ("$revision", expectedRevision)) != 1)
            throw Changed("Collection Set", "deletion");
        NormalizeOrdinals(connection, transaction, "CollectionSets", "CollectionSetId", "ParentCollectionSetId",
            current.ParentCollectionSetId, FormatUtc(_utcNow()));
        return null;
    }, cancellationToken);

    public Task DeleteCollectionAsync(Guid collectionId, long expectedRevision,
        CancellationToken cancellationToken = default) => MutateAsync<object?>((connection, transaction) =>
    {
        var current = ReadCollection(connection, transaction, collectionId) ?? throw Changed("Collection", "deletion");
        if (current.Revision != expectedRevision) throw Changed("Collection", "deletion");
        Execute(connection, transaction, "DELETE FROM CollectionAssets WHERE CollectionId=$id;",
            ("$id", collectionId.ToString("D")));
        if (Execute(connection, transaction,
                "DELETE FROM Collections WHERE CollectionId=$id AND Revision=$revision;",
                ("$id", collectionId.ToString("D")), ("$revision", expectedRevision)) != 1)
            throw Changed("Collection", "deletion");
        NormalizeOrdinals(connection, transaction, "Collections", "CollectionId", "ParentCollectionSetId",
            current.ParentCollectionSetId, FormatUtc(_utcNow()));
        return null;
    }, cancellationToken);

    public Task<IReadOnlyList<CollectionSet>> ReorderSetsAsync(Guid? parentCollectionSetId,
        IReadOnlyList<CollectionOrder> order, CancellationToken cancellationToken = default) =>
        ReorderAsync(parentCollectionSetId, order, "CollectionSets", "CollectionSetId", "ParentCollectionSetId",
            ReadSets, cancellationToken);

    public Task<IReadOnlyList<MediaCollection>> ReorderCollectionsAsync(Guid? parentCollectionSetId,
        IReadOnlyList<CollectionOrder> order, CancellationToken cancellationToken = default) =>
        ReorderAsync(parentCollectionSetId, order, "Collections", "CollectionId", "ParentCollectionSetId",
            ReadCollections, cancellationToken);

    public async Task<CollectionMembershipCreateResult> AddMembershipAsync(Guid collectionId, Guid assetId,
        CancellationToken cancellationToken = default) =>
        AssertSingle(await AddMembershipsAsync(collectionId, [assetId], cancellationToken).ConfigureAwait(false));

    public Task<IReadOnlyList<CollectionMembershipCreateResult>> AddMembershipsAsync(Guid collectionId,
        IReadOnlyList<Guid> assetIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assetIds);
        if (assetIds.Select(id => id).Distinct().Count() != assetIds.Count)
            throw new ArgumentException("Assets to add cannot contain duplicate identities.", nameof(assetIds));
        if (assetIds.Count == 0)
            return Task.FromResult<IReadOnlyList<CollectionMembershipCreateResult>>([]);
        return MutateAsync<IReadOnlyList<CollectionMembershipCreateResult>>((connection, transaction) =>
        {
            EnsureCollectionExists(connection, transaction, collectionId);
            var results = new List<CollectionMembershipCreateResult>(assetIds.Count);
            var ordinal = NextMembershipOrdinal(connection, transaction, collectionId);
            foreach (var assetId in assetIds)
            {
                var existing = ReadMembership(connection, transaction, collectionId, assetId);
                if (existing is not null)
                {
                    results.Add(new(existing, false));
                    continue;
                }
                var now = FormatUtc(_utcNow());
                try
                {
                    Execute(connection, transaction, """
                        INSERT INTO CollectionAssets (CollectionId,AssetId,Ordinal,Revision,CreatedUtc,UpdatedUtc)
                        VALUES ($collection,$asset,$ordinal,1,$now,$now);
                        """, ("$collection", collectionId.ToString("D")), ("$asset", assetId.ToString("D")),
                        ("$ordinal", ordinal++), ("$now", now));
                }
                catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
                {
                    throw new ArgumentException("Only existing Catalog assets can be added to a Collection.", nameof(assetIds), exception);
                }
                results.Add(new(ReadMembership(connection, transaction, collectionId, assetId)!, true));
            }
            return results;
        }, cancellationToken);
    }

    public Task RemoveMembershipAsync(Guid collectionId, Guid assetId, long expectedRevision,
        CancellationToken cancellationToken = default) => RemoveMembershipsAsync(collectionId,
        [new(assetId, expectedRevision)], cancellationToken);

    public Task RemoveMembershipsAsync(Guid collectionId, IReadOnlyList<CollectionOrder> memberships,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(memberships);
        ValidateOrder(memberships);
        if (memberships.Count == 0) return Task.CompletedTask;
        return MutateAsync<object?>((connection, transaction) =>
        {
            var current = ReadMemberships(connection, transaction, collectionId)
                .ToDictionary(item => item.AssetId, item => item.Revision);
            if (memberships.Any(item => !current.TryGetValue(item.Id, out var revision) ||
                    revision != item.ExpectedRevision))
                throw Changed("Collection membership", "removal");
            foreach (var membership in memberships)
                if (Execute(connection, transaction, """
                        DELETE FROM CollectionAssets
                        WHERE CollectionId=$collection AND AssetId=$asset AND Revision=$revision;
                        """, ("$collection", collectionId.ToString("D")), ("$asset", membership.Id.ToString("D")),
                        ("$revision", membership.ExpectedRevision)) != 1)
                    throw Changed("Collection membership", "removal");
            NormalizeMembershipOrdinals(connection, transaction, collectionId, FormatUtc(_utcNow()));
            return null;
        }, cancellationToken);
    }

    public Task<IReadOnlyList<CollectionMembership>> ReorderMembershipsAsync(Guid collectionId,
        IReadOnlyList<CollectionOrder> order, CancellationToken cancellationToken = default) =>
        ReorderAsync(collectionId, order, "CollectionAssets", "AssetId", "CollectionId", ReadMemberships,
            cancellationToken);

    private Task<IReadOnlyList<T>> ReorderAsync<T, TScope>(TScope scope, IReadOnlyList<CollectionOrder> order,
        string table, string idColumn, string scopeColumn,
        Func<SqliteConnection, SqliteTransaction?, TScope, List<T>> read, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(order);
        ValidateOrder(order);
        return MutateAsync<IReadOnlyList<T>>((connection, transaction) =>
        {
            var current = read(connection, transaction, scope);
            var currentRows = current.Select(RowIdentity).ToDictionary(item => item.Id, item => item.Revision);
            if (currentRows.Count != order.Count || order.Any(item =>
                    !currentRows.TryGetValue(item.Id, out var revision) || revision != item.ExpectedRevision))
                throw Changed(table == "CollectionAssets" ? "Collection membership" : table[..^1], "reorder");
            var now = FormatUtc(_utcNow());
            ExecuteScope(connection, transaction,
                $"UPDATE {table} SET Ordinal=Ordinal+1000000000 WHERE {ScopePredicate(scopeColumn)};", scopeColumn, scope);
            for (var ordinal = 0; ordinal < order.Count; ordinal++)
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = $"UPDATE {table} SET Ordinal=$ordinal,Revision=Revision+1,UpdatedUtc=$now WHERE {idColumn}=$id AND {ScopePredicate(scopeColumn)};";
                command.Parameters.AddWithValue("$ordinal", ordinal);
                command.Parameters.AddWithValue("$now", now);
                command.Parameters.AddWithValue("$id", order[ordinal].Id.ToString("D"));
                AddScope(command, scopeColumn, scope);
                if (command.ExecuteNonQuery() != 1) throw Changed("Collection order", "reorder");
            }
            return read(connection, transaction, scope);
        }, cancellationToken);
    }

    private async Task<T> MutateAsync<T>(Func<SqliteConnection, SqliteTransaction, T> operation,
        CancellationToken cancellationToken)
    {
        await _mutations.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var connection = RequireSession().OpenConnection();
                using var transaction = connection.BeginTransaction(deferred: false);
                var result = operation(connection, transaction);
                transaction.Commit();
                return result;
            }, cancellationToken).ConfigureAwait(false);
        }
        finally { _mutations.Release(); }
    }

    private static Task<T> RunReadAsync<T>(Func<T> operation, CancellationToken cancellationToken) =>
        Task.Run(() => { cancellationToken.ThrowIfCancellationRequested(); return operation(); }, cancellationToken);

    private static void EnsureParentExists(SqliteConnection connection, SqliteTransaction transaction, Guid? parent)
    {
        if (parent is null) return;
        if (ScalarLong(connection, transaction,
                "SELECT count(*) FROM CollectionSets WHERE CollectionSetId=$id;", ("$id", parent.Value.ToString("D"))) != 1)
            throw new CollectionHierarchyException("The parent Collection Set does not exist.");
    }

    private static void EnsureCollectionExists(SqliteConnection connection, SqliteTransaction transaction, Guid id)
    {
        if (ScalarLong(connection, transaction,
                "SELECT count(*) FROM Collections WHERE CollectionId=$id;", ("$id", id.ToString("D"))) != 1)
            throw new CollectionConcurrencyException("The Collection no longer exists.");
    }

    private static void EnsureAcyclic(SqliteConnection connection, SqliteTransaction transaction, Guid id, Guid? parent)
    {
        if (parent is null) return;
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            WITH RECURSIVE Ancestors(CollectionSetId) AS (
                SELECT $parent
                UNION ALL
                SELECT sets.ParentCollectionSetId
                FROM CollectionSets sets JOIN Ancestors ON sets.CollectionSetId=Ancestors.CollectionSetId
                WHERE sets.ParentCollectionSetId IS NOT NULL
            )
            SELECT count(*) FROM Ancestors WHERE CollectionSetId=$id;
            """;
        command.Parameters.AddWithValue("$parent", parent.Value.ToString("D"));
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        if (Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture) != 0)
            throw new CollectionHierarchyException("A Collection Set cannot be moved inside itself or one of its descendants.");
    }

    private static int NextOrdinal(SqliteConnection connection, SqliteTransaction transaction,
        string table, string parentColumn, Guid? parent)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT coalesce(max(Ordinal)+1,0) FROM {table} WHERE {ScopePredicate(parentColumn)};";
        AddScope(command, parentColumn, parent);
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static int NextMembershipOrdinal(SqliteConnection connection, SqliteTransaction transaction, Guid collectionId) =>
        NextOrdinal(connection, transaction, "CollectionAssets", "CollectionId", collectionId);

    private static void NormalizeOrdinals(SqliteConnection connection, SqliteTransaction transaction,
        string table, string idColumn, string scopeColumn, Guid? scope, string now)
    {
        var ids = ReadIds(connection, transaction, table, idColumn, scopeColumn, scope);
        ExecuteScope(connection, transaction,
            $"UPDATE {table} SET Ordinal=Ordinal+1000000000 WHERE {ScopePredicate(scopeColumn)};", scopeColumn, scope);
        for (var ordinal = 0; ordinal < ids.Count; ordinal++)
            Execute(connection, transaction,
                $"UPDATE {table} SET Ordinal=$ordinal,Revision=Revision+1,UpdatedUtc=$now WHERE {idColumn}=$id;",
                ("$ordinal", ordinal), ("$now", now), ("$id", ids[ordinal].ToString("D")));
    }

    private static void NormalizeMembershipOrdinals(SqliteConnection connection, SqliteTransaction transaction,
        Guid collectionId, string now) => NormalizeOrdinals(connection, transaction, "CollectionAssets", "AssetId",
        "CollectionId", collectionId, now);

    private static List<Guid> ReadIds(SqliteConnection connection, SqliteTransaction transaction, string table,
        string idColumn, string scopeColumn, Guid? scope)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT {idColumn} FROM {table} WHERE {ScopePredicate(scopeColumn)} ORDER BY Ordinal,{idColumn};";
        AddScope(command, scopeColumn, scope);
        using var reader = command.ExecuteReader();
        var ids = new List<Guid>();
        while (reader.Read()) ids.Add(Guid.Parse(reader.GetString(0)));
        return ids;
    }

    private static List<CollectionSet> ReadSets(SqliteConnection connection, SqliteTransaction? transaction, Guid? parent)
    {
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = $"SELECT CollectionSetId,ParentCollectionSetId,Name,Ordinal,Revision,CreatedUtc,UpdatedUtc FROM CollectionSets WHERE {ScopePredicate("ParentCollectionSetId")} ORDER BY Ordinal,CollectionSetId;";
        AddScope(command, "ParentCollectionSetId", parent);
        using var reader = command.ExecuteReader();
        var result = new List<CollectionSet>();
        while (reader.Read()) result.Add(new(Guid.Parse(reader.GetString(0)), NullableGuid(reader, 1), reader.GetString(2),
            reader.GetInt32(3), reader.GetInt64(4), ParseUtc(reader.GetString(5)), ParseUtc(reader.GetString(6))));
        return result;
    }

    private static List<MediaCollection> ReadCollections(SqliteConnection connection, SqliteTransaction? transaction, Guid? parent)
    {
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = $"SELECT CollectionId,ParentCollectionSetId,Name,Ordinal,Revision,CreatedUtc,UpdatedUtc FROM Collections WHERE {ScopePredicate("ParentCollectionSetId")} ORDER BY Ordinal,CollectionId;";
        AddScope(command, "ParentCollectionSetId", parent);
        using var reader = command.ExecuteReader();
        var result = new List<MediaCollection>();
        while (reader.Read()) result.Add(new(Guid.Parse(reader.GetString(0)), NullableGuid(reader, 1), reader.GetString(2),
            reader.GetInt32(3), reader.GetInt64(4), ParseUtc(reader.GetString(5)), ParseUtc(reader.GetString(6))));
        return result;
    }

    private static List<CollectionMembership> ReadMemberships(SqliteConnection connection, SqliteTransaction? transaction, Guid collectionId)
    {
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "SELECT CollectionId,AssetId,Ordinal,Revision,CreatedUtc,UpdatedUtc FROM CollectionAssets WHERE CollectionId=$collection ORDER BY Ordinal,AssetId;";
        command.Parameters.AddWithValue("$collection", collectionId.ToString("D"));
        using var reader = command.ExecuteReader();
        var result = new List<CollectionMembership>();
        while (reader.Read()) result.Add(new(Guid.Parse(reader.GetString(0)), Guid.Parse(reader.GetString(1)),
            reader.GetInt32(2), reader.GetInt64(3), ParseUtc(reader.GetString(4)), ParseUtc(reader.GetString(5))));
        return result;
    }

    private static CollectionSet? ReadSet(SqliteConnection connection, SqliteTransaction? transaction, Guid id) =>
        ReadSingle(connection, transaction,
            "SELECT CollectionSetId,ParentCollectionSetId,Name,Ordinal,Revision,CreatedUtc,UpdatedUtc FROM CollectionSets WHERE CollectionSetId=$id;",
            id, reader => new CollectionSet(Guid.Parse(reader.GetString(0)), NullableGuid(reader, 1), reader.GetString(2),
                reader.GetInt32(3), reader.GetInt64(4), ParseUtc(reader.GetString(5)), ParseUtc(reader.GetString(6))));

    private static MediaCollection? ReadCollection(SqliteConnection connection, SqliteTransaction? transaction, Guid id) =>
        ReadSingle(connection, transaction,
            "SELECT CollectionId,ParentCollectionSetId,Name,Ordinal,Revision,CreatedUtc,UpdatedUtc FROM Collections WHERE CollectionId=$id;",
            id, reader => new MediaCollection(Guid.Parse(reader.GetString(0)), NullableGuid(reader, 1), reader.GetString(2),
                reader.GetInt32(3), reader.GetInt64(4), ParseUtc(reader.GetString(5)), ParseUtc(reader.GetString(6))));

    private static CollectionMembership? ReadMembership(SqliteConnection connection, SqliteTransaction transaction,
        Guid collectionId, Guid assetId)
    {
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "SELECT CollectionId,AssetId,Ordinal,Revision,CreatedUtc,UpdatedUtc FROM CollectionAssets WHERE CollectionId=$collection AND AssetId=$asset;";
        command.Parameters.AddWithValue("$collection", collectionId.ToString("D"));
        command.Parameters.AddWithValue("$asset", assetId.ToString("D"));
        using var reader = command.ExecuteReader();
        return reader.Read() ? new(Guid.Parse(reader.GetString(0)), Guid.Parse(reader.GetString(1)), reader.GetInt32(2),
            reader.GetInt64(3), ParseUtc(reader.GetString(4)), ParseUtc(reader.GetString(5))) : null;
    }

    private static T? ReadSingle<T>(SqliteConnection connection, SqliteTransaction? transaction, string sql,
        Guid id, Func<SqliteDataReader, T> read) where T : class
    {
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = sql; command.Parameters.AddWithValue("$id", id.ToString("D"));
        using var reader = command.ExecuteReader();
        return reader.Read() ? read(reader) : null;
    }

    private static (Guid Id, long Revision) RowIdentity<T>(T row) => row switch
    {
        CollectionSet item => (item.CollectionSetId, item.Revision),
        MediaCollection item => (item.CollectionId, item.Revision),
        CollectionMembership item => (item.AssetId, item.Revision),
        _ => throw new ArgumentException("Unsupported Collection row.", nameof(row))
    };

    private static void ValidateOrder(IReadOnlyList<CollectionOrder> order)
    {
        if (order.Select(item => item.Id).Distinct().Count() != order.Count)
            throw new ArgumentException("Collection order cannot contain duplicate identities.", nameof(order));
    }

    private static T AssertSingle<T>(IReadOnlyList<T> values) => values.Count == 1 ? values[0] :
        throw new InvalidOperationException("The single-item Collection operation returned an unexpected result.");

    private static string ScopePredicate(string column) => $"{column} IS $scope";
    private static void AddScope<T>(SqliteCommand command, string column, T scope) =>
        command.Parameters.AddWithValue("$scope", scope is Guid id ? id.ToString("D") : DBNull.Value);

    private static int ExecuteScope<T>(SqliteConnection connection, SqliteTransaction transaction, string sql,
        string column, T scope)
    {
        using var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = sql;
        AddScope(command, column, scope);
        return command.ExecuteNonQuery();
    }

    private static int Execute(SqliteConnection connection, SqliteTransaction transaction, string sql,
        params (string Name, object Value)[] parameters)
    {
        using var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = sql;
        foreach (var parameter in parameters) command.Parameters.AddWithValue(parameter.Name, parameter.Value);
        return command.ExecuteNonQuery();
    }

    private static long ScalarLong(SqliteConnection connection, SqliteTransaction transaction, string sql,
        params (string Name, object Value)[] parameters)
    {
        using var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = sql;
        foreach (var parameter in parameters) command.Parameters.AddWithValue(parameter.Name, parameter.Value);
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static object Db(Guid? id) => id is null ? DBNull.Value : id.Value.ToString("D");
    private static Guid? NullableGuid(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : Guid.Parse(reader.GetString(ordinal));
    private static DateTimeOffset ParseUtc(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);
    private static string FormatUtc(DateTimeOffset value) => value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
    private static string NormalizeName(string? value, string parameter)
    {
        var name = value?.Trim() ?? "";
        if (name.Length == 0) throw new ArgumentException("A Collection name is required.", parameter);
        return name;
    }
    private static CollectionConcurrencyException Changed(string entity, string operation) =>
        new($"The {entity} changed before the {operation} was committed.");
    private CatalogDatabaseSession RequireSession() => session() ??
        throw new InvalidOperationException("The Catalog is unavailable.");
}
