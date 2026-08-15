using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace LightflowStudio;

internal enum PreviewComponentState { Missing, Current, Stale, Failed }
internal enum PreviewSourceAvailability { Unknown, Available, Missing, Unavailable }
internal enum PreviewArtifactKind { Thumbnail, StandardPreview }

internal sealed record PreviewSourceIdentity
{
    public PreviewSourceIdentity(long fileSizeBytes, long lastWriteUtcTicks, int fingerprintVersion, string fingerprint)
    {
        if (fileSizeBytes < 0) throw new ArgumentOutOfRangeException(nameof(fileSizeBytes));
        if (lastWriteUtcTicks < 0) throw new ArgumentOutOfRangeException(nameof(lastWriteUtcTicks));
        if (fingerprintVersion <= 0) throw new ArgumentOutOfRangeException(nameof(fingerprintVersion));
        ArgumentException.ThrowIfNullOrWhiteSpace(fingerprint);
        if (fingerprint.Any(character => !Uri.IsHexDigit(character)))
            throw new ArgumentException("Preview source fingerprints must be hexadecimal.", nameof(fingerprint));
        FileSizeBytes = fileSizeBytes;
        LastWriteUtcTicks = lastWriteUtcTicks;
        FingerprintVersion = fingerprintVersion;
        Fingerprint = fingerprint;
    }

    public long FileSizeBytes { get; }
    public long LastWriteUtcTicks { get; }
    public int FingerprintVersion { get; }
    public string Fingerprint { get; }
}

internal sealed record PreviewRecord(
    Guid AssetId,
    PreviewSourceIdentity Source,
    PreviewSourceAvailability SourceAvailability,
    int? MetadataProbeVersion,
    PreviewComponentState MetadataState,
    string? MetadataJson,
    string? RawMetadataJson,
    int? ThumbnailGeneratorVersion,
    PreviewComponentState ThumbnailState,
    string? ThumbnailRelativePath,
    int? StandardPreviewGeneratorVersion,
    PreviewComponentState StandardPreviewState,
    string? StandardPreviewRelativePath,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc);

internal sealed record PreviewComponentUpdate(
    int GeneratorVersion,
    PreviewComponentState State,
    string? RelativePath = null,
    string? PayloadJson = null,
    string? RawPayloadJson = null);

internal interface IPreviewStoreService : IAsyncDisposable
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<PreviewRecord?> GetAsync(Guid assetId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PreviewRecord>> ListAsync(CancellationToken cancellationToken = default);
    Task<PreviewRecord> ObserveSourceAsync(Guid assetId, PreviewSourceIdentity source,
        CancellationToken cancellationToken = default);
    Task<PreviewRecord?> SetSourceAvailabilityAsync(Guid assetId, PreviewSourceAvailability availability,
        CancellationToken cancellationToken = default);
    Task<PreviewRecord?> SetMetadataAsync(Guid assetId, PreviewComponentUpdate update,
        CancellationToken cancellationToken = default);
    Task<PreviewRecord?> ClearMetadataAsync(Guid assetId, CancellationToken cancellationToken = default);
    Task<PreviewRecord?> SetArtifactAsync(Guid assetId, PreviewArtifactKind kind, PreviewComponentUpdate update,
        CancellationToken cancellationToken = default);
    Task<PreviewRecord?> ClearArtifactAsync(Guid assetId, PreviewArtifactKind kind,
        CancellationToken cancellationToken = default);
    Task ClearAllAsync(CancellationToken cancellationToken = default);
    string GetArtifactPath(Guid assetId, PreviewArtifactKind kind, int generatorVersion,
        PreviewSourceIdentity source, string extension);
}

internal sealed class PreviewStoreService : IPreviewStoreService
{
    internal const int SchemaVersion = 1;
    internal const int SqliteApplicationId = 0x4C465052; // LFPR
    private readonly ILightflowStorageLocations _locations;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _initialized;
    private bool _disposed;

    public PreviewStoreService(ILightflowStorageLocations locations) => _locations = locations;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            await Task.Run(() => EnsureInitialized(cancellationToken), cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public Task<PreviewRecord?> GetAsync(Guid assetId, CancellationToken cancellationToken = default) =>
        RunAsync(connection => Read(connection, assetId), cancellationToken);

    public Task<IReadOnlyList<PreviewRecord>> ListAsync(CancellationToken cancellationToken = default) =>
        RunAsync<IReadOnlyList<PreviewRecord>>(connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = SelectSql + " ORDER BY AssetId;";
            using var reader = command.ExecuteReader();
            var records = new List<PreviewRecord>();
            while (reader.Read()) records.Add(Read(reader));
            return records;
        }, cancellationToken);

    public Task<PreviewRecord> ObserveSourceAsync(Guid assetId, PreviewSourceIdentity source,
        CancellationToken cancellationToken = default) => RunAsync(connection =>
    {
        if (assetId == Guid.Empty) throw new ArgumentException("AssetId must be a durable non-empty identifier.", nameof(assetId));
        var now = Utc(DateTimeOffset.UtcNow);
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO PreviewRecords
                (AssetId,FileSizeBytes,LastWriteUtcTicks,FingerprintVersion,SourceFingerprint,
                 SourceAvailability,MetadataState,ThumbnailState,StandardPreviewState,CreatedUtc,UpdatedUtc)
            VALUES ($asset,$size,$write,$fingerprintVersion,$fingerprint,'available','missing','missing','missing',$now,$now)
            ON CONFLICT(AssetId) DO UPDATE SET
                MetadataState=CASE WHEN (FileSizeBytes<>excluded.FileSizeBytes OR LastWriteUtcTicks<>excluded.LastWriteUtcTicks
                    OR FingerprintVersion<>excluded.FingerprintVersion OR SourceFingerprint<>excluded.SourceFingerprint)
                    AND MetadataState<>'missing' THEN 'stale' ELSE MetadataState END,
                ThumbnailState=CASE WHEN (FileSizeBytes<>excluded.FileSizeBytes OR LastWriteUtcTicks<>excluded.LastWriteUtcTicks
                    OR FingerprintVersion<>excluded.FingerprintVersion OR SourceFingerprint<>excluded.SourceFingerprint)
                    AND ThumbnailState<>'missing' THEN 'stale' ELSE ThumbnailState END,
                StandardPreviewState=CASE WHEN (FileSizeBytes<>excluded.FileSizeBytes OR LastWriteUtcTicks<>excluded.LastWriteUtcTicks
                    OR FingerprintVersion<>excluded.FingerprintVersion OR SourceFingerprint<>excluded.SourceFingerprint)
                    AND StandardPreviewState<>'missing' THEN 'stale' ELSE StandardPreviewState END,
                FileSizeBytes=excluded.FileSizeBytes,LastWriteUtcTicks=excluded.LastWriteUtcTicks,
                FingerprintVersion=excluded.FingerprintVersion,SourceFingerprint=excluded.SourceFingerprint,
                SourceAvailability='available',UpdatedUtc=excluded.UpdatedUtc;
            """;
        AddSource(command, assetId, source, now);
        command.ExecuteNonQuery();
        return Read(connection, assetId)!;
    }, cancellationToken);

    public Task<PreviewRecord?> SetSourceAvailabilityAsync(Guid assetId, PreviewSourceAvailability availability,
        CancellationToken cancellationToken = default) => RunAsync(connection =>
    {
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE PreviewRecords SET SourceAvailability=$status,UpdatedUtc=$now WHERE AssetId=$asset;";
        command.Parameters.AddWithValue("$status", Availability(availability));
        command.Parameters.AddWithValue("$now", Utc(DateTimeOffset.UtcNow));
        command.Parameters.AddWithValue("$asset", assetId.ToString("D"));
        command.ExecuteNonQuery();
        return Read(connection, assetId);
    }, cancellationToken);

    public Task<PreviewRecord?> SetMetadataAsync(Guid assetId, PreviewComponentUpdate update,
        CancellationToken cancellationToken = default) => RunAsync(connection =>
    {
        ValidateUpdate(update);
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE PreviewRecords SET MetadataProbeVersion=$version,MetadataState=$state,
                MetadataJson=$payload,RawMetadataJson=$raw,UpdatedUtc=$now WHERE AssetId=$asset;
            """;
        AddUpdate(command, assetId, update);
        command.Parameters.AddWithValue("$payload", (object?)update.PayloadJson ?? DBNull.Value);
        command.Parameters.AddWithValue("$raw", (object?)update.RawPayloadJson ?? DBNull.Value);
        command.ExecuteNonQuery();
        return Read(connection, assetId);
    }, cancellationToken);

    public Task<PreviewRecord?> SetArtifactAsync(Guid assetId, PreviewArtifactKind kind, PreviewComponentUpdate update,
        CancellationToken cancellationToken = default) => RunAsync(connection =>
    {
        ValidateUpdate(update);
        if (update.State == PreviewComponentState.Current && string.IsNullOrWhiteSpace(update.RelativePath))
            throw new ArgumentException("A current artifact requires a relative path.", nameof(update));
        var prefix = kind == PreviewArtifactKind.Thumbnail ? "Thumbnail" : "StandardPreview";
        using var command = connection.CreateCommand();
        command.CommandText = $"UPDATE PreviewRecords SET {prefix}GeneratorVersion=$version,{prefix}State=$state,{prefix}RelativePath=$path,UpdatedUtc=$now WHERE AssetId=$asset;";
        AddUpdate(command, assetId, update);
        command.Parameters.AddWithValue("$path", string.IsNullOrWhiteSpace(update.RelativePath)
            ? DBNull.Value : NormalizeArtifactRelativePath(update.RelativePath));
        command.ExecuteNonQuery();
        return Read(connection, assetId);
    }, cancellationToken);

    public Task<PreviewRecord?> ClearMetadataAsync(Guid assetId, CancellationToken cancellationToken = default) =>
        RunAsync(connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE PreviewRecords SET MetadataProbeVersion=NULL,MetadataState='missing',MetadataJson=NULL,RawMetadataJson=NULL,UpdatedUtc=$now WHERE AssetId=$asset;";
            command.Parameters.AddWithValue("$now", Utc(DateTimeOffset.UtcNow));
            command.Parameters.AddWithValue("$asset", assetId.ToString("D"));
            command.ExecuteNonQuery();
            return Read(connection, assetId);
        }, cancellationToken);

    public Task<PreviewRecord?> ClearArtifactAsync(Guid assetId, PreviewArtifactKind kind,
        CancellationToken cancellationToken = default) => RunAsync(connection =>
    {
        var prefix = kind == PreviewArtifactKind.Thumbnail ? "Thumbnail" : "StandardPreview";
        using var command = connection.CreateCommand();
        command.CommandText = $"UPDATE PreviewRecords SET {prefix}GeneratorVersion=NULL,{prefix}State='missing',{prefix}RelativePath=NULL,UpdatedUtc=$now WHERE AssetId=$asset;";
        command.Parameters.AddWithValue("$now", Utc(DateTimeOffset.UtcNow));
        command.Parameters.AddWithValue("$asset", assetId.ToString("D"));
        command.ExecuteNonQuery();
        return Read(connection, assetId);
    }, cancellationToken);

    public Task ClearAllAsync(CancellationToken cancellationToken = default) => RunAsync(connection =>
    {
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM PreviewRecords;";
        command.ExecuteNonQuery();
        transaction.Commit();
        try
        {
            command.Transaction = null;
            command.CommandText = "VACUUM; PRAGMA wal_checkpoint(TRUNCATE);";
            command.ExecuteNonQuery();
        }
        catch (SqliteException) { }
        return true;
    }, cancellationToken);

    public string GetArtifactPath(Guid assetId, PreviewArtifactKind kind, int generatorVersion,
        PreviewSourceIdentity source, string extension)
    {
        if (generatorVersion <= 0) throw new ArgumentOutOfRangeException(nameof(generatorVersion));
        extension = extension.Trim().TrimStart('.').ToLowerInvariant();
        if (extension.Length == 0 || extension.Any(character => !char.IsAsciiLetterOrDigit(character)))
            throw new ArgumentException("Use a simple alphanumeric file extension.", nameof(extension));
        var id = assetId.ToString("N");
        var material = Encoding.UTF8.GetBytes($"{source.FileSizeBytes}:{source.LastWriteUtcTicks}:{source.FingerprintVersion}:{source.Fingerprint.ToLowerInvariant()}");
        var sourceKey = Convert.ToHexString(SHA256.HashData(material)).ToLowerInvariant()[..32];
        var root = kind == PreviewArtifactKind.Thumbnail
            ? _locations.ThumbnailCacheDirectory
            : _locations.StandardPreviewCacheDirectory;
        return Path.Combine(root, id[..2], id.Substring(2, 2),
            $"{id}-g{generatorVersion}-f{source.FingerprintVersion}-{sourceKey}.{extension}");
    }

    private async Task<T> RunAsync<T>(Func<SqliteConnection, T> operation, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return await Task.Run(() =>
            {
                EnsureInitialized(cancellationToken);
                using var connection = OpenConnection();
                return operation(connection);
            }, cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    private void EnsureInitialized(CancellationToken cancellationToken)
    {
        if (_initialized) return;
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(_locations.PreviewsDirectory);
        if (File.Exists(_locations.PreviewsDatabasePath))
        {
            using var existing = OpenReadOnlyConnection();
            ValidateDatabase(existing);
            _initialized = true;
            return;
        }
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        Execute(connection, transaction, $"PRAGMA application_id={SqliteApplicationId};");
        Execute(connection, transaction, """
            CREATE TABLE IF NOT EXISTS PreviewStoreInfo (
                SingletonId INTEGER NOT NULL PRIMARY KEY CHECK(SingletonId=1),
                ApplicationIdentity TEXT NOT NULL,CreatedUtc TEXT NOT NULL,UpdatedUtc TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS PreviewRecords (
                AssetId TEXT NOT NULL PRIMARY KEY CHECK(length(AssetId)=36),
                FileSizeBytes INTEGER NOT NULL CHECK(FileSizeBytes>=0),
                LastWriteUtcTicks INTEGER NOT NULL CHECK(LastWriteUtcTicks>=0),
                FingerprintVersion INTEGER NOT NULL CHECK(FingerprintVersion>0),
                SourceFingerprint TEXT NOT NULL CHECK(length(SourceFingerprint)>0),
                SourceAvailability TEXT NOT NULL CHECK(SourceAvailability IN ('unknown','available','missing','unavailable')),
                MetadataProbeVersion INTEGER NULL,MetadataState TEXT NOT NULL CHECK(MetadataState IN ('missing','current','stale','failed')),
                MetadataJson TEXT NULL,RawMetadataJson TEXT NULL,
                ThumbnailGeneratorVersion INTEGER NULL,ThumbnailState TEXT NOT NULL CHECK(ThumbnailState IN ('missing','current','stale','failed')),ThumbnailRelativePath TEXT NULL,
                StandardPreviewGeneratorVersion INTEGER NULL,StandardPreviewState TEXT NOT NULL CHECK(StandardPreviewState IN ('missing','current','stale','failed')),StandardPreviewRelativePath TEXT NULL,
                CreatedUtc TEXT NOT NULL,UpdatedUtc TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS IX_PreviewRecords_MetadataState ON PreviewRecords(MetadataState);
            CREATE INDEX IF NOT EXISTS IX_PreviewRecords_ThumbnailState ON PreviewRecords(ThumbnailState);
            """);
        var now = Utc(DateTimeOffset.UtcNow);
        using (var info = connection.CreateCommand())
        {
            info.Transaction = transaction;
            info.CommandText = "INSERT OR IGNORE INTO PreviewStoreInfo VALUES(1,'LightflowStudio.Previews',$now,$now);";
            info.Parameters.AddWithValue("$now", now);
            info.ExecuteNonQuery();
        }
        Execute(connection, transaction, $"PRAGMA user_version={SchemaVersion};");
        transaction.Commit();
        ValidateDatabase(connection);
        _initialized = true;
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = _locations.PreviewsDatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
            DefaultTimeout = 5
        }.ToString());
        connection.Open();
        using var policy = connection.CreateCommand();
        policy.CommandText = $"PRAGMA journal_mode={(IsUnc(_locations.PreviewsDatabasePath) ? "DELETE" : "WAL")}; PRAGMA synchronous=NORMAL; PRAGMA busy_timeout=5000;";
        policy.ExecuteNonQuery();
        return connection;
    }

    private SqliteConnection OpenReadOnlyConnection()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = _locations.PreviewsDatabasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
            DefaultTimeout = 5
        }.ToString());
        connection.Open();
        return connection;
    }

    private static void ValidateDatabase(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA quick_check;";
        if (!string.Equals(Convert.ToString(command.ExecuteScalar()), "ok", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The Preview database failed its integrity check. It may be safely rebuilt.");
        command.CommandText = "PRAGMA application_id;";
        if (Convert.ToInt32(command.ExecuteScalar()) != SqliteApplicationId)
            throw new InvalidDataException("The configured Preview database is not a Lightflow Preview store.");
        command.CommandText = "PRAGMA user_version;";
        if (Convert.ToInt32(command.ExecuteScalar()) != SchemaVersion)
            throw new InvalidDataException("The Preview database schema is unsupported and may be safely rebuilt.");
        command.CommandText = "SELECT ApplicationIdentity FROM PreviewStoreInfo WHERE SingletonId=1;";
        if (!string.Equals(Convert.ToString(command.ExecuteScalar()), "LightflowStudio.Previews", StringComparison.Ordinal))
            throw new InvalidDataException("The Preview database identity metadata is incomplete and may be safely rebuilt.");
    }

    private static PreviewRecord? Read(SqliteConnection connection, Guid assetId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = SelectSql + " WHERE AssetId=$asset;";
        command.Parameters.AddWithValue("$asset", assetId.ToString("D"));
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;
        return Read(reader);
    }

    private static PreviewRecord Read(SqliteDataReader reader) =>
        new(Guid.Parse(reader.GetString(0)), new(reader.GetInt64(1), reader.GetInt64(2), reader.GetInt32(3), reader.GetString(4)),
            ParseAvailability(reader.GetString(5)), NullableInt(reader, 6), ParseState(reader.GetString(7)), NullableString(reader, 8), NullableString(reader, 9),
            NullableInt(reader, 10), ParseState(reader.GetString(11)), NullableString(reader, 12), NullableInt(reader, 13), ParseState(reader.GetString(14)), NullableString(reader, 15),
            DateTimeOffset.Parse(reader.GetString(16), CultureInfo.InvariantCulture), DateTimeOffset.Parse(reader.GetString(17), CultureInfo.InvariantCulture));

    private const string SelectSql = """
        SELECT AssetId,FileSizeBytes,LastWriteUtcTicks,FingerprintVersion,SourceFingerprint,SourceAvailability,
            MetadataProbeVersion,MetadataState,MetadataJson,RawMetadataJson,
            ThumbnailGeneratorVersion,ThumbnailState,ThumbnailRelativePath,
            StandardPreviewGeneratorVersion,StandardPreviewState,StandardPreviewRelativePath,CreatedUtc,UpdatedUtc
        FROM PreviewRecords
        """;

    private static void AddSource(SqliteCommand command, Guid assetId, PreviewSourceIdentity source, string now)
    {
        command.Parameters.AddWithValue("$asset", assetId.ToString("D")); command.Parameters.AddWithValue("$size", source.FileSizeBytes);
        command.Parameters.AddWithValue("$write", source.LastWriteUtcTicks); command.Parameters.AddWithValue("$fingerprintVersion", source.FingerprintVersion);
        command.Parameters.AddWithValue("$fingerprint", source.Fingerprint); command.Parameters.AddWithValue("$now", now);
    }
    private static void AddUpdate(SqliteCommand command, Guid assetId, PreviewComponentUpdate update)
    {
        command.Parameters.AddWithValue("$asset", assetId.ToString("D")); command.Parameters.AddWithValue("$version", update.GeneratorVersion);
        command.Parameters.AddWithValue("$state", State(update.State)); command.Parameters.AddWithValue("$now", Utc(DateTimeOffset.UtcNow));
    }
    private static void ValidateUpdate(PreviewComponentUpdate update) { if (update.GeneratorVersion <= 0) throw new ArgumentOutOfRangeException(nameof(update)); }
    private static string NormalizeArtifactRelativePath(string path)
    {
        if (Path.IsPathFullyQualified(path)) throw new ArgumentException("Artifact paths must be relative.", nameof(path));
        var normalized = path.Replace('\\', '/').Trim('/');
        if (normalized.Length == 0 || normalized.Split('/').Any(segment => segment is "." or ".." || segment.Length == 0))
            throw new ArgumentException("Artifact paths must stay within the Preview store.", nameof(path));
        return normalized;
    }
    private static int? NullableInt(SqliteDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);
    private static string? NullableString(SqliteDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    private static string Utc(DateTimeOffset value) => value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
    private static string State(PreviewComponentState state) => state.ToString().ToLowerInvariant();
    private static PreviewComponentState ParseState(string value) => Enum.Parse<PreviewComponentState>(value, true);
    private static string Availability(PreviewSourceAvailability value) => value.ToString().ToLowerInvariant();
    private static PreviewSourceAvailability ParseAvailability(string value) => Enum.Parse<PreviewSourceAvailability>(value, true);
    private static bool IsUnc(string path) => path.StartsWith(@"\\", StringComparison.Ordinal) ||
        path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase);
    private static void Execute(SqliteConnection connection, SqliteTransaction transaction, string sql)
    { using var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = sql; command.ExecuteNonQuery(); }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_initialized && File.Exists(_locations.PreviewsDatabasePath))
            {
                try
                {
                    using var connection = OpenConnection();
                    using var checkpoint = connection.CreateCommand();
                    checkpoint.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
                    checkpoint.ExecuteNonQuery();
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SqliteException) { }
            }
            _disposed = true;
        }
        finally { _gate.Release(); _gate.Dispose(); }
    }
}
