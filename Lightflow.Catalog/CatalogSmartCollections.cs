using Microsoft.Data.Sqlite;
namespace LightflowStudio;
public partial class CatalogCollectionOrganizationStore
{
    public Task<CatalogSmartCollectionDefinition?> GetSmartCollectionAsync(Guid id, CancellationToken token = default) =>
        RunReadAsync(() =>
        {
            using var connection = RequireSession().OpenConnection();
            using var transaction = connection.BeginTransaction(deferred: true);
            var organization = ReadCollection(connection, transaction, id);
            if (organization?.IsSmartCollection != true) return null;
            return ReadSmartDefinition(connection, transaction, organization);
        }, token);

    public Task<CatalogSmartCollectionDefinition> SaveSmartCollectionAsync(string name, Guid? parent,
        SmartCollectionSource source, ICatalogSavedQuery query, Guid? id = null, long? expectedRevision = null,
        CancellationToken token = default) => MutateAsync((connection, transaction) =>
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(query);
        name = NormalizeName(name, nameof(name));
        source = source.Validate();
        var json = query.Serialize();
        EnsureParentExists(connection, transaction, parent);
        if (source.CollectionId is { } sourceId && ReadCollection(connection, transaction, sourceId) is not { IsSmartCollection: false })
            throw new ArgumentException("Choose an existing static Collection Source.");
        var now = FormatUtc(_utcNow());
        var identity = id ?? Guid.NewGuid();
        if (id is null)
        {
            Execute(connection, transaction, """
                INSERT INTO Collections(CollectionId,ParentCollectionSetId,Name,Ordinal,Revision,CreatedUtc,UpdatedUtc,IsSmartCollection)
                VALUES($id,$parent,$name,$ordinal,1,$now,$now,1);
                """, ("$id", identity.ToString("D")), ("$parent", Db(parent)), ("$name", name),
                ("$ordinal", NextHierarchyOrdinal(connection, transaction, parent)), ("$now", now));
        }
        else
        {
            var current = ReadCollection(connection, transaction, identity);
            if (current is not { IsSmartCollection: true } || current.Revision != expectedRevision) throw Changed("Smart Collection", "edit");
            Execute(connection, transaction, """
                UPDATE Collections SET Name=$name,ParentCollectionSetId=$parent,Ordinal=$ordinal,Revision=Revision+1,UpdatedUtc=$now
                WHERE CollectionId=$id;
                """, ("$id", identity.ToString("D")), ("$parent", Db(parent)), ("$name", name),
                ("$ordinal", current.ParentCollectionSetId == parent ? current.Ordinal : NextHierarchyOrdinal(connection, transaction, parent)), ("$now", now));
            if (current.ParentCollectionSetId != parent) NormalizeHierarchyOrdinals(connection, transaction, current.ParentCollectionSetId, now);
        }
        Execute(connection, transaction, """
            INSERT INTO SmartCollectionDefinitions(SmartCollectionId,SourceKind,RootId,RelativeFolder,SourceCollectionId,IncludeSubfolders,QueryJson)
            VALUES($id,$kind,$root,$folder,$collection,$recursive,$query)
            ON CONFLICT(SmartCollectionId) DO UPDATE SET SourceKind=excluded.SourceKind,RootId=excluded.RootId,
                RelativeFolder=excluded.RelativeFolder,SourceCollectionId=excluded.SourceCollectionId,
                IncludeSubfolders=excluded.IncludeSubfolders,QueryJson=excluded.QueryJson;
            """, ("$id", identity.ToString("D")), ("$kind", (int)source.Kind), ("$root", Db(source.RootId)),
            ("$folder", (object?)source.RelativeFolder ?? DBNull.Value), ("$collection", Db(source.CollectionId)),
            ("$recursive", source.IncludeSubfolders ? 1 : 0), ("$query", json));
        return ReadSmartDefinition(connection, transaction, ReadCollection(connection, transaction, identity)!);
    }, token);

    private CatalogSmartCollectionDefinition ReadSmartDefinition(SqliteConnection connection, SqliteTransaction? transaction, MediaCollection organization)
    {
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "SELECT SourceKind,RootId,RelativeFolder,SourceCollectionId,IncludeSubfolders,QueryJson FROM SmartCollectionDefinitions WHERE SmartCollectionId=$id;";
        command.Parameters.AddWithValue("$id", organization.CollectionId.ToString("D"));
        using var reader = command.ExecuteReader();
        if (!reader.Read()) throw new InvalidOperationException("The Smart Collection definition is missing.");
        return new(organization, new((SmartCollectionSourceKind)reader.GetInt32(0), NullableGuid(reader, 1),
            reader.IsDBNull(2) ? null : reader.GetString(2), NullableGuid(reader, 3), reader.GetBoolean(4)), (queryReader ?? throw new NotSupportedException("A qualified saved-query codec is required."))(reader.GetString(5)));
    }
}
