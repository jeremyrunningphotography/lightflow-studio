using Microsoft.Data.Sqlite;

namespace LightflowStudio;

[Flags]
internal enum BrowserAssetState
{
    None = 0,
    ReviewRange = 1,
    Color = 2
}

internal static class BrowserAssetStateRevisionPolicy
{
    /// <summary>A read may apply unless this specific asset changed after the read began.</summary>
    public static bool CanApply(long readRevision, long? assetChangedAt) =>
        assetChangedAt is null || assetChangedAt <= readRevision;
}

internal interface IBrowserAssetStateStore
{
    Task<IReadOnlyDictionary<Guid, BrowserAssetState>> GetAsync(
        IReadOnlyCollection<Guid> assetIds, CancellationToken cancellationToken = default);
}

/// <summary>
/// Projects durable, user-authored Catalog facts into the small state vocabulary consumed by Browser tiles.
/// The projection is deliberately separate from Preview data and thumbnail generation. Additional Catalog
/// attributes can extend <see cref="BrowserAssetState"/> without teaching the grid how those facts are stored.
/// </summary>
internal sealed class CatalogBrowserAssetStateStore(Func<CatalogDatabaseSession?> session) : IBrowserAssetStateStore
{
    private const int QueryBatchSize = 500;

    public Task<IReadOnlyDictionary<Guid, BrowserAssetState>> GetAsync(
        IReadOnlyCollection<Guid> assetIds, CancellationToken cancellationToken = default) =>
        Task.Run<IReadOnlyDictionary<Guid, BrowserAssetState>>(() =>
        {
            var states = assetIds.Distinct().ToDictionary(assetId => assetId, _ => BrowserAssetState.None);
            if (states.Count == 0) return states;

            using var connection = RequireSession().OpenConnection();
            foreach (var batch in states.Keys.ToArray().Chunk(QueryBatchSize))
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var command = connection.CreateCommand();
                var parameters = new string[batch.Length];
                for (var index = 0; index < batch.Length; index++)
                {
                    parameters[index] = $"$asset{index}";
                    command.Parameters.AddWithValue(parameters[index], batch[index].ToString("D"));
                }
                command.CommandText = $"""
                    SELECT DISTINCT AssetId
                    FROM MediaAssetRanges
                    WHERE Kind='primary' AND Ordinal=0 AND AssetId IN ({string.Join(',', parameters)});
                    """;
                using var reader = command.ExecuteReader();
                while (reader.Read() && Guid.TryParse(reader.GetString(0), out var assetId))
                    states[assetId] |= BrowserAssetState.ReviewRange;
                reader.Close();

                command.Parameters.Clear();
                parameters = batch.Select((id, index) =>
                { var name = $"$color{index}"; command.Parameters.AddWithValue(name, id.ToString("D")); return name; }).ToArray();
                command.CommandText = $"SELECT AssetId FROM MediaAssetColor WHERE (CameraLutId IS NOT NULL OR CreativeLutId IS NOT NULL) AND AssetId IN ({string.Join(',', parameters)});";
                using var colorReader = command.ExecuteReader();
                while (colorReader.Read() && Guid.TryParse(colorReader.GetString(0), out var colorAssetId))
                    states[colorAssetId] |= BrowserAssetState.Color;
            }
            return states;
        }, cancellationToken);

    private CatalogDatabaseSession RequireSession() => session() ??
        throw new InvalidOperationException("The Catalog is unavailable.");
}
