using Lightflow.Application;
using System.IO;
using Microsoft.Data.Sqlite;
namespace LightflowStudio;
public sealed class CatalogMediaAssetRepository(Func<CatalogDatabaseSession?> session, CatalogMutationLifecycle? mutations = null) : IMediaAssetRepository, ICatalogMutationParticipant
{
    public CatalogMutationLifecycle Mutations => mutations ?? RequireSession().Mutations;
    public Task<IReadOnlyList<MediaAsset>> ListScopeAsync(Guid rootId, string folder, bool recursive,
        CancellationToken cancellationToken = default) => RunAsync<IReadOnlyList<MediaAsset>>(() =>
    {
        using var timing = BrowserPerformance.Measure("catalog.scope");
        var prefix = string.IsNullOrEmpty(folder) ? "" : CatalogRelativePaths.RelativePathKey(folder) + "/";
        using var connection = RequireSession().OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = SelectSql + " WHERE RootId=$root" +
            (prefix.Length == 0 ? "" : " AND RelativePathKey >= $prefix AND RelativePathKey < $end") + """
             AND ($recursive=1 OR instr(substr(RelativePathKey, length($prefix)+1), '/')=0)
             ORDER BY RelativePathKey;
            """;
        command.Parameters.AddWithValue("$root", rootId.ToString("D"));
        command.Parameters.AddWithValue("$prefix", prefix);
        command.Parameters.AddWithValue("$end", prefix.Length == 0 ? "" : prefix[..^1] + "0");
        command.Parameters.AddWithValue("$recursive", recursive ? 1 : 0);
        using var reader = command.ExecuteReader();
        var result = new List<MediaAsset>();
        while (reader.Read()) { cancellationToken.ThrowIfCancellationRequested(); result.Add(Read(reader)); }
        return result;
    }, cancellationToken);
    public Task<MediaAsset?> GetAsync(Guid assetId, CancellationToken cancellationToken = default) => RunAsync(() =>
    {
        using var connection = RequireSession().OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = SelectSql + " WHERE AssetId=$asset;";
        command.Parameters.AddWithValue("$asset", assetId.ToString("D"));
        return ReadOne(command);
    }, cancellationToken);

    // Keep the projection proportional to membership, below SQLite's portable parameter limit.
    // Results are keyed; callers retain their own ordering and unavailable-source semantics.
    public Task<IReadOnlyDictionary<Guid, MediaAsset>> GetManyAsync(IReadOnlyCollection<Guid> assetIds,
        CancellationToken cancellationToken = default) => RunAsync<IReadOnlyDictionary<Guid, MediaAsset>>(() =>
    {
        using var timing = BrowserPerformance.Measure("catalog.assets_by_id");
        var ids = assetIds.Distinct().ToArray();
        var result = new Dictionary<Guid, MediaAsset>(ids.Length);
        if (ids.Length == 0) return result;
        using var connection = RequireSession().OpenConnection();
        foreach (var batch in ids.Chunk(500))
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var batchTiming = BrowserPerformance.Measure("catalog.assets_by_id.batch");
            using var command = connection.CreateCommand();
            var parameters = batch.Select((id, index) =>
            {
                var name = $"$asset{index}";
                command.Parameters.AddWithValue(name, id.ToString("D"));
                return name;
            });
            command.CommandText = SelectSql + $" WHERE AssetId IN ({string.Join(",", parameters)});";
            using var reader = command.ExecuteReader();
            var rows = 0;
            while (reader.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var asset = Read(reader);
                result.Add(asset.AssetId, asset);
                rows++;
            }
            batchTiming?.SetTag("asset_ids", batch.Length);
            batchTiming?.SetTag("materialized_rows", rows);
        }
        timing?.SetTag("materialized_rows", result.Count);
        return result;
    }, cancellationToken);

    public Task<IReadOnlyList<MediaAsset>> ListAsync(CancellationToken cancellationToken = default) => RunAsync<IReadOnlyList<MediaAsset>>(() =>
    {
        StartupDiagnostics.Note("Catalog full asset enumeration requested");
        using var connection = RequireSession().OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = SelectSql + " ORDER BY AssetId;";
        using var reader = command.ExecuteReader();
        var assets = new List<MediaAsset>();
        while (reader.Read()) assets.Add(Read(reader));
        return assets;
    }, cancellationToken);

    public Task<MediaAsset?> FindAsync(Guid rootId, string relativePathKey, CancellationToken cancellationToken = default) => RunAsync(() =>
    {
        using var connection = RequireSession().OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = SelectSql + " WHERE RootId=$root AND RelativePathKey=$key;";
        command.Parameters.AddWithValue("$root", rootId.ToString("D"));
        command.Parameters.AddWithValue("$key", relativePathKey);
        return ReadOne(command);
    }, cancellationToken);

    public Task<MediaAssetOperationStatus> CreateAsync(MediaAsset asset, CancellationToken cancellationToken = default) {
        return RequireSession().Mutations.RunAsync<MediaAssetOperationStatus>(() => { return RunAsync(() =>
    {
        using var connection = RequireSession().OpenConnection();
        using var transaction = connection.BeginTransaction();
        var pathCheck = ValidateWritePaths(connection, transaction, asset.RootId, asset.AssetId, asset.RelativePath);
        if (!pathCheck.IsSafe) return pathCheck.Status == PathIdentityStatus.AmbiguousMapping ? MediaAssetOperationStatus.AlreadyExists : MediaAssetOperationStatus.Failed;
        try
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO MediaAssets
                    (AssetId,RootId,RelativePath,RelativePathKey,MediaType,FileSizeBytes,LastWriteUtcTicks,
                     FingerprintVersion,SourceFingerprint,SourceStatus,CreatedUtc,UpdatedUtc,LastSeenUtc)
                VALUES
                    ($asset,$root,$path,$key,$type,$size,$write,$version,$fingerprint,'available',$created,$updated,$seen);
                """;
            AddAssetParameters(command, asset);
            command.ExecuteNonQuery();
            transaction.Commit();
            return MediaAssetOperationStatus.Succeeded;
        }
        catch (SqliteException exception) when (exception.SqliteExtendedErrorCode == 2067)
        {
            transaction.Rollback();
            return MediaAssetOperationStatus.AlreadyExists;
        }
    }, cancellationToken); }, cancellationToken);
    }

    public Task<bool> UpdateObservationAsync(Guid assetId, long size, long lastWriteUtcTicks,
        SourceFingerprint fingerprint, DateTimeOffset observedUtc, CancellationToken cancellationToken = default) {
        return RequireSession().Mutations.RunAsync<bool>(() => { return RunAsync(() =>
    {
        using var connection = RequireSession().OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE MediaAssets SET FileSizeBytes=$size, LastWriteUtcTicks=$write,
                FingerprintVersion=$version, SourceFingerprint=$fingerprint, SourceStatus='available',
                UpdatedUtc=$now, LastSeenUtc=$now WHERE AssetId=$asset;
            """;
        command.Parameters.AddWithValue("$size", size);
        command.Parameters.AddWithValue("$write", lastWriteUtcTicks);
        command.Parameters.AddWithValue("$version", fingerprint.Version);
        command.Parameters.AddWithValue("$fingerprint", fingerprint.Value);
        command.Parameters.AddWithValue("$now", Timestamp(observedUtc));
        command.Parameters.AddWithValue("$asset", assetId.ToString("D"));
        return command.ExecuteNonQuery() == 1;
    }, cancellationToken); }, cancellationToken);
    }

    public Task<bool> MarkMissingAsync(Guid assetId, DateTimeOffset observedUtc, CancellationToken cancellationToken = default) {
        return RequireSession().Mutations.RunAsync<bool>(() => { return RunAsync(() =>
    {
        using var connection = RequireSession().OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE MediaAssets SET SourceStatus='missing', UpdatedUtc=$now WHERE AssetId=$asset;";
        command.Parameters.AddWithValue("$now", Timestamp(observedUtc));
        command.Parameters.AddWithValue("$asset", assetId.ToString("D"));
        return command.ExecuteNonQuery() == 1;
    }, cancellationToken); }, cancellationToken);
    }

    public Task<int> MarkMissingAsync(IReadOnlyCollection<Guid> assetIds, DateTimeOffset observedUtc,
        CancellationToken cancellationToken = default) {
        return RequireSession().Mutations.RunAsync<int>(() => { return RunAsync(() =>
    {
        if (assetIds.Count == 0) return 0;
        using var connection = RequireSession().OpenConnection();
        using var transaction = connection.BeginTransaction();
        var updated = 0;
        foreach (var assetId in assetIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "UPDATE MediaAssets SET SourceStatus='missing', UpdatedUtc=$now WHERE AssetId=$asset;";
            command.Parameters.AddWithValue("$now", Timestamp(observedUtc));
            command.Parameters.AddWithValue("$asset", assetId.ToString("D"));
            updated += command.ExecuteNonQuery();
        }
        cancellationToken.ThrowIfCancellationRequested();
        transaction.Commit();
        return updated;
    }, cancellationToken); }, cancellationToken);
    }

    public Task<MediaAssetOperationStatus> RelocateAsync(Guid assetId, Guid rootId, string relativePath,
        DateTimeOffset observedUtc, CancellationToken cancellationToken = default) {
        return RequireSession().Mutations.RunAsync<MediaAssetOperationStatus>(() => { return RunAsync(() =>
    {
        var portableCheck = PortablePathIdentityValidator.ValidateRelativePath(relativePath.Replace('\\', '/'), cancellationToken);
        if (!portableCheck.IsSafe) return MediaAssetOperationStatus.Failed;
        var normalized = CatalogRelativePaths.NormalizeRelativePath(relativePath);
        using var connection = RequireSession().OpenConnection();
        using var transaction = connection.BeginTransaction();
        if (!ValidateWritePaths(connection, transaction, rootId, assetId, normalized).IsSafe) return MediaAssetOperationStatus.Failed;
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = """
            UPDATE MediaAssets SET RootId=$root, RelativePath=$path, RelativePathKey=$key,
                SourceStatus='available', UpdatedUtc=$now, LastSeenUtc=$now WHERE AssetId=$asset;
            """;
        command.Parameters.AddWithValue("$root", rootId.ToString("D"));
        command.Parameters.AddWithValue("$path", normalized);
        command.Parameters.AddWithValue("$key", CatalogRelativePaths.RelativePathKey(normalized));
        command.Parameters.AddWithValue("$now", Timestamp(observedUtc));
        command.Parameters.AddWithValue("$asset", assetId.ToString("D"));
        try { var changed = command.ExecuteNonQuery(); transaction.Commit(); return changed == 1 ? MediaAssetOperationStatus.Succeeded : MediaAssetOperationStatus.NotFound; }
        catch (SqliteException exception) when (exception.SqliteExtendedErrorCode == 2067)
        { return MediaAssetOperationStatus.AlreadyExists; }
    }, cancellationToken); }, cancellationToken);
    }

    private CatalogDatabaseSession RequireSession() => session() ?? throw new InvalidOperationException("The Catalog is unavailable.");

    private static MediaAsset? ReadOne(SqliteCommand command)
    {
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;
        return Read(reader);
    }

    private static PathIdentityResult ValidateWritePaths(SqliteConnection connection, SqliteTransaction transaction,
        Guid rootId, Guid assetId, string relativePath)
    {
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "SELECT AssetId,RelativePath,RelativePathKey FROM MediaAssets WHERE RootId=$root AND AssetId<>$asset;";
        command.Parameters.AddWithValue("$root", rootId.ToString("D"));
        command.Parameters.AddWithValue("$asset", assetId.ToString("D"));
        using var reader = command.ExecuteReader();
        var candidates = new List<PathIdentityCandidate> { new(rootId, assetId, relativePath) };
        while (reader.Read()) candidates.Add(new(rootId, Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(2)));
        return PortablePathIdentityValidator.ValidateCandidates(candidates);
    }

    private static MediaAsset Read(SqliteDataReader reader)
    {
        var fingerprint = reader.IsDBNull(7) ? null : new SourceFingerprint(reader.GetInt32(7), reader.GetString(8));
        return new(
            Guid.Parse(reader.GetString(0)), Guid.Parse(reader.GetString(1)), reader.GetString(2), reader.GetString(3),
            reader.GetString(4), reader.GetInt64(5), reader.GetInt64(6), fingerprint, ParseStatus(reader.GetString(9)),
            DateTimeOffset.Parse(reader.GetString(10), System.Globalization.CultureInfo.InvariantCulture),
            DateTimeOffset.Parse(reader.GetString(11), System.Globalization.CultureInfo.InvariantCulture),
            reader.IsDBNull(12) ? null : DateTimeOffset.Parse(reader.GetString(12), System.Globalization.CultureInfo.InvariantCulture));
    }

    private static void AddAssetParameters(SqliteCommand command, MediaAsset asset)
    {
        command.Parameters.AddWithValue("$asset", asset.AssetId.ToString("D"));
        command.Parameters.AddWithValue("$root", asset.RootId.ToString("D"));
        command.Parameters.AddWithValue("$path", asset.RelativePath);
        command.Parameters.AddWithValue("$key", asset.RelativePathKey);
        command.Parameters.AddWithValue("$type", asset.MediaType);
        command.Parameters.AddWithValue("$size", asset.FileSizeBytes);
        command.Parameters.AddWithValue("$write", asset.LastWriteUtcTicks);
        command.Parameters.AddWithValue("$version", asset.Fingerprint?.Version ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$fingerprint", asset.Fingerprint?.Value ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$created", Timestamp(asset.CreatedUtc));
        command.Parameters.AddWithValue("$updated", Timestamp(asset.UpdatedUtc));
        command.Parameters.AddWithValue("$seen", asset.LastSeenUtc is { } seen ? Timestamp(seen) : DBNull.Value);
    }

    private static MediaAssetSourceStatus ParseStatus(string status) => status switch
    {
        "available" => MediaAssetSourceStatus.Available,
        "missing" => MediaAssetSourceStatus.Missing,
        _ => MediaAssetSourceStatus.Unknown
    };

    private static string Timestamp(DateTimeOffset value) => value.UtcDateTime.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
    private static Task<T> RunAsync<T>(Func<T> operation, CancellationToken token) =>
        Task.Run(() => { token.ThrowIfCancellationRequested(); return operation(); }, token);

    private const string SelectSql = """
        SELECT AssetId,RootId,RelativePath,RelativePathKey,MediaType,FileSizeBytes,LastWriteUtcTicks,
               FingerprintVersion,SourceFingerprint,SourceStatus,CreatedUtc,UpdatedUtc,LastSeenUtc
        FROM MediaAssets
        """;
}
