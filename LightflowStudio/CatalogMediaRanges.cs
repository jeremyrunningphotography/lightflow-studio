using Microsoft.Data.Sqlite;

namespace LightflowStudio;

internal interface IMediaRangeStore
{
    Task<MediaRange?> RestoreAsync(Guid assetId, CancellationToken cancellationToken = default);
    Task SaveAsync(Guid assetId, MediaRange? range, CancellationToken cancellationToken = default);
}

/// <summary>
/// Catalog-backed durable user range intent. The separate table and (Kind, Ordinal) identity deliberately
/// preserve a straightforward path to multiple named/ordered subclips while #133 exposes one primary range.
/// </summary>
internal sealed class CatalogMediaRangeStore(Func<CatalogDatabaseSession?> session, Func<DateTimeOffset>? utcNow = null)
    : IMediaRangeStore
{
    private readonly Func<DateTimeOffset> _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);

    public Task<MediaRange?> RestoreAsync(Guid assetId, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            using var connection = RequireSession().OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT SourceDurationTicks, InTicks, OutTicks
                FROM MediaAssetRanges WHERE AssetId=$asset AND Kind='primary' AND Ordinal=0;
                """;
            command.Parameters.AddWithValue("$asset", assetId.ToString("D"));
            using var reader = command.ExecuteReader();
            if (!reader.Read()) return null;
            try
            {
                var range = new MediaRange(
                    TimeSpan.FromTicks(reader.GetInt64(0)),
                    reader.IsDBNull(1) ? null : TimeSpan.FromTicks(reader.GetInt64(1)),
                    reader.IsDBNull(2) ? null : TimeSpan.FromTicks(reader.GetInt64(2)));
                return range.Validate().Count == 0 && !range.IsFullSource ? range : null;
            }
            catch (ArgumentOutOfRangeException) { return null; }
        }, cancellationToken);

    public Task SaveAsync(Guid assetId, MediaRange? range, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            if (range is { } invalid && (invalid.IsFullSource || invalid.Validate().Count != 0))
                throw new ArgumentException("The saved media range must contain at least one valid boundary.", nameof(range));
            using var connection = RequireSession().OpenConnection();
            using var transaction = connection.BeginTransaction();
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.Parameters.AddWithValue("$asset", assetId.ToString("D"));
            if (range is null)
                command.CommandText = "DELETE FROM MediaAssetRanges WHERE AssetId=$asset AND Kind='primary' AND Ordinal=0;";
            else
            {
                var now = _utcNow().UtcDateTime.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
                command.CommandText = """
                    INSERT INTO MediaAssetRanges
                        (RangeId,AssetId,Kind,Ordinal,InTicks,OutTicks,SourceDurationTicks,CreatedUtc,UpdatedUtc)
                    VALUES ($range,$asset,'primary',0,$in,$out,$duration,$now,$now)
                    ON CONFLICT(AssetId,Kind,Ordinal) DO UPDATE SET
                        InTicks=excluded.InTicks, OutTicks=excluded.OutTicks,
                        SourceDurationTicks=excluded.SourceDurationTicks, UpdatedUtc=excluded.UpdatedUtc;
                    """;
                command.Parameters.AddWithValue("$range", Guid.NewGuid().ToString("D"));
                command.Parameters.AddWithValue("$in", range.In?.Ticks ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("$out", range.Out?.Ticks ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("$duration", range.SourceDuration.Ticks);
                command.Parameters.AddWithValue("$now", now);
            }
            command.ExecuteNonQuery();
            transaction.Commit();
        }, cancellationToken);

    private CatalogDatabaseSession RequireSession() => session() ??
        throw new InvalidOperationException("The Catalog is unavailable.");
}
