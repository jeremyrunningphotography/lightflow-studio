using Microsoft.Data.Sqlite;

namespace LightflowStudio;

internal interface IAssetCopyDataService
{
    Task CloneAsync(Guid sourceAssetId, MediaAsset destination, CancellationToken cancellationToken = default);
}

/// <summary>Clones AssetId-owned durable intent while keeping the copied asset independently mutable.</summary>
internal sealed class AssetCopyDataService(Func<CatalogDatabaseSession?> session, IPreviewStoreService? previews)
    : IAssetCopyDataService
{
    public async Task CloneAsync(Guid sourceAssetId, MediaAsset destination,
        CancellationToken cancellationToken = default)
    {
        await Task.Run(() => CloneCatalog(sourceAssetId, destination.AssetId, cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        if (previews is null) return;
        var source = await previews.GetAsync(sourceAssetId, cancellationToken).ConfigureAwait(false);
        if (source?.MetadataProbeVersion is not { } version ||
            source.MetadataJson is null && source.RawMetadataJson is null)
            return;
        var identity = new PreviewSourceIdentity(destination.FileSizeBytes, destination.LastWriteUtcTicks,
            destination.Fingerprint?.Version ?? source.Source.FingerprintVersion,
            destination.Fingerprint?.Value ?? source.Source.Fingerprint);
        await previews.ObserveSourceAsync(destination.AssetId, identity, cancellationToken).ConfigureAwait(false);
        await previews.SetMetadataAsync(destination.AssetId, new(version, source.MetadataState,
            PayloadJson: source.MetadataJson, RawPayloadJson: source.RawMetadataJson), cancellationToken)
            .ConfigureAwait(false);
    }

    private void CloneCatalog(Guid source, Guid destination, CancellationToken cancellationToken)
    {
        using var connection = (session() ?? throw new InvalidOperationException("The Catalog is unavailable.")).OpenConnection();
        using var transaction = connection.BeginTransaction();
        var oldId = source.ToString("D");
        var newId = destination.ToString("D");
        var now = DateTimeOffset.UtcNow.UtcDateTime.ToString("O");

        CopyRows(connection, transaction, "MediaAssetRanges", "RangeId,AssetId,Kind,Ordinal,InTicks,OutTicks,SourceDurationTicks,CreatedUtc,UpdatedUtc",
            "SELECT RangeId,Kind,Ordinal,InTicks,OutTicks,SourceDurationTicks FROM MediaAssetRanges WHERE AssetId=$source ORDER BY Ordinal",
            reader => [Guid.NewGuid().ToString("D"), newId, reader.GetString(1), reader.GetInt64(2), Db(reader, 3), Db(reader, 4), reader.GetInt64(5), now, now], oldId, cancellationToken);
        CopyRows(connection, transaction, "Subclips", "SubclipId,AssetId,Name,Ordinal,InTicks,OutTicks,SourceDurationTicks,Revision,CreatedUtc,UpdatedUtc",
            "SELECT SubclipId,Name,Ordinal,InTicks,OutTicks,SourceDurationTicks,Revision FROM Subclips WHERE AssetId=$source ORDER BY Ordinal",
            reader => [Guid.NewGuid().ToString("D"), newId, reader.GetString(1), reader.GetInt64(2), reader.GetInt64(3), reader.GetInt64(4), reader.GetInt64(5), reader.GetInt64(6), now, now], oldId, cancellationToken);
        Execute(connection, transaction, "INSERT INTO MediaAssetColor (AssetId,ColorEnabled,CameraLutId,CreativeLutId,CreatedUtc,UpdatedUtc) SELECT $destination,ColorEnabled,CameraLutId,CreativeLutId,$now,$now FROM MediaAssetColor WHERE AssetId=$source", oldId, newId, now);
        Execute(connection, transaction, "INSERT INTO MediaAssetPreferredFrames (AssetId,PositionTicks,Revision,CreatedUtc,UpdatedUtc) SELECT $destination,PositionTicks,Revision,$now,$now FROM MediaAssetPreferredFrames WHERE AssetId=$source", oldId, newId, now);
        Execute(connection, transaction, "INSERT INTO MediaAssetClassifications (AssetId,Rating,Flag,ColorLabel,Revision,CreatedUtc,UpdatedUtc) SELECT $destination,Rating,Flag,ColorLabel,Revision,$now,$now FROM MediaAssetClassifications WHERE AssetId=$source", oldId, newId, now);
        Execute(connection, transaction, "INSERT INTO MediaAssetKeywords (AssetId,Keyword,Ordinal,CreatedUtc) SELECT $destination,Keyword,Ordinal,$now FROM MediaAssetKeywords WHERE AssetId=$source", oldId, newId, now);

        using (var memberships = connection.CreateCommand())
        {
            memberships.Transaction = transaction;
            memberships.CommandText = "SELECT CollectionId FROM CollectionAssets WHERE AssetId=$source ORDER BY CollectionId";
            memberships.Parameters.AddWithValue("$source", oldId);
            var collectionIds = new List<string>();
            using (var reader = memberships.ExecuteReader()) while (reader.Read()) collectionIds.Add(reader.GetString(0));
            foreach (var collectionId in collectionIds)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var insert = connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText = "INSERT INTO CollectionAssets (CollectionId,AssetId,Ordinal,Revision,CreatedUtc,UpdatedUtc) SELECT $collection,$destination,COALESCE(MAX(Ordinal),-1)+1,1,$now,$now FROM CollectionAssets WHERE CollectionId=$collection";
                insert.Parameters.AddWithValue("$collection", collectionId);
                insert.Parameters.AddWithValue("$destination", newId);
                insert.Parameters.AddWithValue("$now", now);
                insert.ExecuteNonQuery();
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        transaction.Commit();
    }

    private static object Db(SqliteDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? DBNull.Value : reader.GetValue(ordinal);

    private static void Execute(SqliteConnection connection, SqliteTransaction transaction, string sql,
        string source, string destination, string now)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction; command.CommandText = sql;
        command.Parameters.AddWithValue("$source", source); command.Parameters.AddWithValue("$destination", destination);
        command.Parameters.AddWithValue("$now", now); command.ExecuteNonQuery();
    }

    private static void CopyRows(SqliteConnection connection, SqliteTransaction transaction, string table,
        string columns, string select, Func<SqliteDataReader, object[]> map, string source,
        CancellationToken cancellationToken)
    {
        using var query = connection.CreateCommand(); query.Transaction = transaction; query.CommandText = select;
        query.Parameters.AddWithValue("$source", source);
        var rows = new List<object[]>(); using (var reader = query.ExecuteReader()) while (reader.Read()) rows.Add(map(reader));
        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var insert = connection.CreateCommand(); insert.Transaction = transaction;
            var names = Enumerable.Range(0, row.Length).Select(i => $"$p{i}").ToArray();
            insert.CommandText = $"INSERT INTO {table} ({columns}) VALUES ({string.Join(',', names)})";
            for (var i = 0; i < row.Length; i++) insert.Parameters.AddWithValue(names[i], row[i]);
            insert.ExecuteNonQuery();
        }
    }
}
