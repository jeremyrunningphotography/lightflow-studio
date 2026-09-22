using System.Globalization;
using Microsoft.Data.Sqlite;

namespace LightflowStudio;

internal sealed record TimelineMarker(Guid MarkerId, Guid AssetId, TimeSpan Position, string Name,
    long Revision, DateTimeOffset CreatedUtc, DateTimeOffset UpdatedUtc)
{
    public string DisplayName => string.IsNullOrEmpty(Name) ? "Marker" : Name;
    public string ChoiceLabel => $"{DisplayName} · {PositionLabel}";
    public string PositionLabel => Position.ToString("c", CultureInfo.InvariantCulture);
}
internal sealed record MarkerCreateResult(TimelineMarker Marker, bool Created);
internal sealed record MarkerSummary(int Count) { public bool HasMarkers => Count > 0; }
internal sealed class MarkerConcurrencyException() : InvalidOperationException("The marker changed. Reload it before editing.");
internal interface IMarkerService
{
    Task<IReadOnlyList<TimelineMarker>> ListAsync(Guid assetId, CancellationToken token = default);
    Task<IReadOnlyDictionary<Guid, MarkerSummary>> SummariesAsync(IReadOnlyCollection<Guid> assets, CancellationToken token = default);
    Task<MarkerCreateResult> CreateAsync(Guid assetId, TimeSpan position, CancellationToken token = default);
    Task RenameAsync(Guid markerId, long revision, string name, CancellationToken token = default);
    Task DeleteAsync(Guid markerId, long revision, CancellationToken token = default);
}

internal static class MarkerNavigation
{
    public static TimelineMarker? Previous(IEnumerable<TimelineMarker> markers, TimeSpan position) =>
        markers.Where(m => m.Position < position).OrderByDescending(m => m.Position).FirstOrDefault();
    public static TimelineMarker? Next(IEnumerable<TimelineMarker> markers, TimeSpan position) =>
        markers.Where(m => m.Position > position).OrderBy(m => m.Position).FirstOrDefault();
    public static double Fraction(TimeSpan position, TimeSpan duration) => duration.Ticks <= 0 ? 0 :
        Math.Clamp((double)position.Ticks / duration.Ticks, 0, 1);
}

/// <summary>Catalog-only point annotations. No source file or destination identity is involved.</summary>
internal sealed class CatalogMarkerService(Func<CatalogDatabaseSession?> session) : IMarkerService
{
    private CatalogDatabaseSession Session => session() ?? throw new InvalidOperationException("The Catalog is unavailable.");
    public Task<IReadOnlyList<TimelineMarker>> ListAsync(Guid assetId, CancellationToken token = default) =>
        Task.Run<IReadOnlyList<TimelineMarker>>(() =>
        {
            using var connection = Session.OpenConnection();
            return Read(connection, null, assetId);
        }, token);

    public Task<IReadOnlyDictionary<Guid, MarkerSummary>> SummariesAsync(IReadOnlyCollection<Guid> assets, CancellationToken token = default) =>
        Task.Run<IReadOnlyDictionary<Guid, MarkerSummary>>(() =>
        {
            using var connection = Session.OpenConnection();
            var result = assets.Distinct().ToDictionary(id => id, _ => new MarkerSummary(0));
            foreach (var batch in result.Keys.ToArray().Chunk(128))
            {
                token.ThrowIfCancellationRequested();
                using var command = connection.CreateCommand();
                var parameters = batch.Select((id, index) => { var key = "$a" + index; command.Parameters.AddWithValue(key, id.ToString("D")); return key; });
                command.CommandText = $"SELECT AssetId,COUNT(*) FROM TimelineMarkers WHERE AssetId IN ({string.Join(',', parameters)}) GROUP BY AssetId";
                using var reader = command.ExecuteReader();
                while (reader.Read()) result[Guid.Parse(reader.GetString(0))] = new(reader.GetInt32(1));
            }
            return result;
        }, token);

    public Task<MarkerCreateResult> CreateAsync(Guid assetId, TimeSpan position, CancellationToken token = default)
    {
        return Session.Mutations.RunAsync<MarkerCreateResult>(() => {
        if (position < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(position));
        return Task.Run(() =>
        {
            using var connection = Session.OpenConnection();
            using var transaction = connection.BeginTransaction(deferred: false);
            var existing = Read(connection, transaction, assetId).FirstOrDefault(m => m.Position == position);
            if (existing is not null) { transaction.Commit(); return new MarkerCreateResult(existing, false); }
            var now = DateTimeOffset.UtcNow;
            var marker = new TimelineMarker(Guid.NewGuid(), assetId, position, "", 1, now, now);
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "INSERT INTO TimelineMarkers VALUES ($id,$asset,$position,'',1,$now,$now)";
            command.Parameters.AddWithValue("$id", marker.MarkerId.ToString("D"));
            command.Parameters.AddWithValue("$asset", assetId.ToString("D"));
            command.Parameters.AddWithValue("$position", position.Ticks);
            command.Parameters.AddWithValue("$now", now.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
            command.ExecuteNonQuery();
            transaction.Commit();
            return new MarkerCreateResult(marker, true);
        }, token);
    }, token);
    }

    public Task RenameAsync(Guid markerId, long revision, string name, CancellationToken token = default) =>
        MutateAsync(markerId, revision, name?.Trim() ?? "", token);
    public Task DeleteAsync(Guid markerId, long revision, CancellationToken token = default) =>
        MutateAsync(markerId, revision, null, token);
    private Task MutateAsync(Guid markerId, long revision, string? name, CancellationToken token) {
        return Session.Mutations.RunAsync(() => { return Task.Run(() =>
    {
        using var connection = Session.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = name is null ? "DELETE FROM TimelineMarkers WHERE MarkerId=$id AND Revision=$revision" :
            "UPDATE TimelineMarkers SET Name=$name,Revision=Revision+1,UpdatedUtc=$now WHERE MarkerId=$id AND Revision=$revision";
        command.Parameters.AddWithValue("$id", markerId.ToString("D"));
        command.Parameters.AddWithValue("$revision", revision);
        if (name is not null)
        {
            command.Parameters.AddWithValue("$name", name);
            command.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        }
        if (command.ExecuteNonQuery() != 1) throw new MarkerConcurrencyException();
    }, token); }, token);
    }

    private static List<TimelineMarker> Read(SqliteConnection connection, SqliteTransaction? transaction, Guid assetId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT MarkerId,AssetId,PositionTicks,Name,Revision,CreatedUtc,UpdatedUtc FROM TimelineMarkers WHERE AssetId=$asset ORDER BY PositionTicks,MarkerId";
        command.Parameters.AddWithValue("$asset", assetId.ToString("D"));
        using var reader = command.ExecuteReader();
        var result = new List<TimelineMarker>();
        while (reader.Read()) result.Add(new(Guid.Parse(reader.GetString(0)), Guid.Parse(reader.GetString(1)),
            TimeSpan.FromTicks(reader.GetInt64(2)), reader.GetString(3), reader.GetInt64(4),
            DateTimeOffset.Parse(reader.GetString(5), CultureInfo.InvariantCulture), DateTimeOffset.Parse(reader.GetString(6), CultureInfo.InvariantCulture)));
        return result;
    }
}
