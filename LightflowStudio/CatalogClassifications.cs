using Microsoft.Data.Sqlite;

namespace LightflowStudio;

internal enum AssetFlag { Rejected = -1, Unflagged = 0, Picked = 1 }
internal enum AssetColorLabel { Red = 1, Yellow = 2, Green = 3, Blue = 4, Purple = 5 }

internal sealed record AssetClassification(Guid AssetId, int Rating, AssetFlag Flag,
    AssetColorLabel? ColorLabel, IReadOnlyList<string> Keywords, long Revision = 0)
{
    public static AssetClassification Empty(Guid assetId) => new(assetId, 0, AssetFlag.Unflagged, null, []);
}

internal interface IAssetClassificationStore
{
    Task<IReadOnlyDictionary<Guid, AssetClassification>> GetAsync(IReadOnlyCollection<Guid> assetIds,
        CancellationToken cancellationToken = default);
    Task SaveAsync(AssetClassification classification, CancellationToken cancellationToken = default);
}

internal sealed class CatalogAssetClassificationStore(Func<CatalogDatabaseSession?> session,
    Func<DateTimeOffset>? utcNow = null) : IAssetClassificationStore
{
    private const int QueryBatchSize = 400;
    private readonly Func<DateTimeOffset> _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);

    public Task<IReadOnlyDictionary<Guid, AssetClassification>> GetAsync(IReadOnlyCollection<Guid> assetIds,
        CancellationToken cancellationToken = default) => Task.Run<IReadOnlyDictionary<Guid, AssetClassification>>(() =>
    {
        var result = assetIds.Distinct().ToDictionary(id => id, AssetClassification.Empty);
        using var connection = RequireSession().OpenConnection();
        foreach (var batch in result.Keys.ToArray().Chunk(QueryBatchSize))
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var command = connection.CreateCommand();
            var names = batch.Select((id, i) => { var name = $"$asset{i}"; command.Parameters.AddWithValue(name, id.ToString("D")); return name; }).ToArray();
            command.CommandText = $"SELECT AssetId,Rating,Flag,ColorLabel,Revision FROM MediaAssetClassifications WHERE AssetId IN ({string.Join(',', names)});";
            using var reader = command.ExecuteReader();
            while (reader.Read() && Guid.TryParse(reader.GetString(0), out var id))
                result[id] = new(id, reader.GetInt32(1), (AssetFlag)reader.GetInt32(2),
                    reader.IsDBNull(3) ? null : (AssetColorLabel)reader.GetInt32(3), [], reader.GetInt64(4));
            reader.Close();
            command.CommandText = $"SELECT AssetId,Keyword FROM MediaAssetKeywords WHERE AssetId IN ({string.Join(',', names)}) ORDER BY AssetId,Ordinal;";
            using var keywordReader = command.ExecuteReader();
            while (keywordReader.Read() && Guid.TryParse(keywordReader.GetString(0), out var id))
                result[id] = result[id] with { Keywords = [.. result[id].Keywords, keywordReader.GetString(1)] };
        }
        return result;
    }, cancellationToken);

    public Task SaveAsync(AssetClassification classification, CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        if (classification.Rating is < 0 or > 5) throw new ArgumentOutOfRangeException(nameof(classification));
        if (!Enum.IsDefined(classification.Flag) || classification.ColorLabel is { } label && !Enum.IsDefined(label))
            throw new ArgumentOutOfRangeException(nameof(classification));
        var keywords = classification.Keywords.Select(value => value.Trim()).Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        using var connection = RequireSession().OpenConnection();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        var now = _utcNow().UtcDateTime.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
        command.CommandText = """
            INSERT INTO MediaAssetClassifications (AssetId,Rating,Flag,ColorLabel,Revision,CreatedUtc,UpdatedUtc)
            SELECT AssetId,$rating,$flag,$label,1,$now,$now FROM MediaAssets WHERE AssetId=$asset
            ON CONFLICT(AssetId) DO UPDATE SET Rating=excluded.Rating,Flag=excluded.Flag,
                ColorLabel=excluded.ColorLabel,Revision=Revision+1,UpdatedUtc=excluded.UpdatedUtc;
            """;
        command.Parameters.AddWithValue("$asset", classification.AssetId.ToString("D"));
        command.Parameters.AddWithValue("$rating", classification.Rating);
        command.Parameters.AddWithValue("$flag", (int)classification.Flag);
        command.Parameters.AddWithValue("$label", classification.ColorLabel is { } value ? (int)value : DBNull.Value);
        command.Parameters.AddWithValue("$now", now);
        if (command.ExecuteNonQuery() != 1) throw new InvalidOperationException("Catalog asset does not exist.");
        command.CommandText = "DELETE FROM MediaAssetKeywords WHERE AssetId=$asset;";
        command.ExecuteNonQuery();
        command.CommandText = "INSERT INTO MediaAssetKeywords (AssetId,Keyword,Ordinal,CreatedUtc) VALUES ($asset,$keyword,$ordinal,$now);";
        command.Parameters.Add("$keyword", SqliteType.Text);
        command.Parameters.Add("$ordinal", SqliteType.Integer);
        for (var index = 0; index < keywords.Length; index++)
        {
            command.Parameters["$keyword"].Value = keywords[index];
            command.Parameters["$ordinal"].Value = index;
            command.ExecuteNonQuery();
        }
        transaction.Commit();
    }, cancellationToken);

    private CatalogDatabaseSession RequireSession() => session() ?? throw new InvalidOperationException("The Catalog is unavailable.");
}
