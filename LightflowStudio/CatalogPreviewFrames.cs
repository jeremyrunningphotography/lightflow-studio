using Microsoft.Data.Sqlite;

namespace LightflowStudio;

internal sealed record PreferredPreviewFrame(
    Guid AssetId,
    TimeSpan Position,
    long Revision,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc);

internal interface IPreferredPreviewFrameStore
{
    Task<PreferredPreviewFrame?> GetAsync(Guid assetId, CancellationToken cancellationToken = default);
    Task<PreferredPreviewFrame> SetAsync(Guid assetId, MediaPresentationTimestamp timestamp,
        TimeSpan sourceDuration, CancellationToken cancellationToken = default);
    Task ResetAsync(Guid assetId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Catalog-owned user intent for #205. Only the authoritative decoded source timestamp is durable; Preview
/// pixels remain rebuildable and continue through the ordinary thumbnail pipeline.
/// </summary>
internal sealed class CatalogPreferredPreviewFrameStore(Func<CatalogDatabaseSession?> session) : IPreferredPreviewFrameStore
{
    public Task<PreferredPreviewFrame?> GetAsync(Guid assetId, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            using var connection = RequireSession().OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT PositionTicks,Revision,CreatedUtc,UpdatedUtc FROM MediaAssetPreferredFrames WHERE AssetId=$asset;";
            command.Parameters.AddWithValue("$asset", assetId.ToString("D"));
            using var reader = command.ExecuteReader();
            return reader.Read() ? Read(assetId, reader) : null;
        }, cancellationToken);

    public Task<PreferredPreviewFrame> SetAsync(Guid assetId, MediaPresentationTimestamp timestamp,
        TimeSpan sourceDuration, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(timestamp);
        if (!timestamp.IsDecodedPresentationTimestamp)
            throw new ArgumentException("The preferred Preview frame must use a decoded presentation timestamp.", nameof(timestamp));
        if (sourceDuration <= TimeSpan.Zero || timestamp.Position < TimeSpan.Zero || timestamp.Position > sourceDuration)
            throw new ArgumentOutOfRangeException(nameof(timestamp), "The preferred Preview timestamp must be within the source duration.");

        return Task.Run(() =>
        {
            var now = DateTime.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
            using var connection = RequireSession().OpenConnection();
            using var transaction = connection.BeginTransaction();
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO MediaAssetPreferredFrames (AssetId,PositionTicks,Revision,CreatedUtc,UpdatedUtc)
                SELECT AssetId,$position,1,$now,$now FROM MediaAssets
                WHERE AssetId=$asset AND lower(MediaType)='video'
                ON CONFLICT(AssetId) DO UPDATE SET
                    PositionTicks=excluded.PositionTicks,
                    Revision=MediaAssetPreferredFrames.Revision+1,
                    UpdatedUtc=excluded.UpdatedUtc;
                """;
            command.Parameters.AddWithValue("$asset", assetId.ToString("D"));
            command.Parameters.AddWithValue("$position", timestamp.Position.Ticks);
            command.Parameters.AddWithValue("$now", now);
            if (command.ExecuteNonQuery() != 1)
                throw new InvalidOperationException("Only a Catalog video can have a preferred Preview frame.");
            transaction.Commit();
            return GetRequired(assetId, connection);
        }, cancellationToken);
    }

    public Task ResetAsync(Guid assetId, CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        using var connection = RequireSession().OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM MediaAssetPreferredFrames WHERE AssetId=$asset;";
        command.Parameters.AddWithValue("$asset", assetId.ToString("D"));
        command.ExecuteNonQuery();
    }, cancellationToken);

    private static PreferredPreviewFrame GetRequired(Guid assetId, SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT PositionTicks,Revision,CreatedUtc,UpdatedUtc FROM MediaAssetPreferredFrames WHERE AssetId=$asset;";
        command.Parameters.AddWithValue("$asset", assetId.ToString("D"));
        using var reader = command.ExecuteReader();
        return reader.Read() ? Read(assetId, reader) : throw new InvalidOperationException("The preferred Preview frame was not committed.");
    }

    private static PreferredPreviewFrame Read(Guid assetId, SqliteDataReader reader) => new(
        assetId,
        TimeSpan.FromTicks(reader.GetInt64(0)),
        reader.GetInt64(1),
        DateTimeOffset.Parse(reader.GetString(2), null, System.Globalization.DateTimeStyles.RoundtripKind),
        DateTimeOffset.Parse(reader.GetString(3), null, System.Globalization.DateTimeStyles.RoundtripKind));

    private CatalogDatabaseSession RequireSession() => session() ??
        throw new InvalidOperationException("The Catalog is unavailable.");
}
