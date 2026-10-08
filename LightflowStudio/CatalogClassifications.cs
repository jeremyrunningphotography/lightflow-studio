using Lightflow.Application;
using Lightflow.Domain;
using Microsoft.Data.Sqlite;

namespace LightflowStudio;

internal sealed class CatalogAssetClassificationStore(Func<CatalogDatabaseSession?> session,
    Func<DateTimeOffset>? utcNow = null) : IAssetClassificationStore
{
    private const int QueryBatchSize = 400;
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<CatalogDatabaseSession, SemaphoreSlim> Gates = new();
    public async Task<AssetClassification> UpdateAsync(Guid assetId, Func<AssetClassification, AssetClassification> mutate,
        CancellationToken token = default)
    {
        var capturedSession = RequireSession();
        return await capturedSession.Mutations.RunAsync(async () => {
            var gate = Gates.GetValue(capturedSession, _ => new(1, 1));
            await gate.WaitAsync(token).ConfigureAwait(false);
            try {
                if (!ReferenceEquals(capturedSession, RequireSession())) throw new InvalidOperationException("The Catalog changed.");
                var current = (await GetAsync([assetId], token).ConfigureAwait(false))[assetId];
                var updated = mutate(current);
                if (updated.AssetId != assetId) throw new InvalidOperationException("Classification identity cannot change.");
                if (updated.Rating == current.Rating && updated.Flag == current.Flag && updated.ColorLabel == current.ColorLabel &&
                    updated.Keywords.SequenceEqual(current.Keywords)) return current;
                await SaveCoreAsync(updated, token).ConfigureAwait(false);
                return (await GetAsync([assetId], token).ConfigureAwait(false))[assetId];
            }
            finally { gate.Release(); }
        }, token).ConfigureAwait(false);
    }
    public async Task SaveAsync(AssetClassification classification, CancellationToken cancellationToken = default)
    {
        var capturedSession = RequireSession();
        await capturedSession.Mutations.RunAsync(async () => {
            var gate = Gates.GetValue(capturedSession, _ => new(1, 1));
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try {
                if (!ReferenceEquals(capturedSession, RequireSession())) throw new InvalidOperationException("The Catalog changed.");
                await SaveCoreAsync(classification, cancellationToken).ConfigureAwait(false);
            }
            finally { gate.Release(); }
        }, cancellationToken).ConfigureAwait(false);
    }
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

    private Task SaveCoreAsync(AssetClassification classification, CancellationToken cancellationToken = default) {
        return RequireSession().Mutations.RunAsync(() => { return Task.Run(() =>
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
    }, cancellationToken); }, cancellationToken);
    }

    private CatalogDatabaseSession RequireSession() => session() ?? throw new InvalidOperationException("The Catalog is unavailable.");
}
