using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace LightflowStudio;

internal enum MediaAssetSourceStatus { Unknown, Available, Missing }
internal enum MediaAssetOperationStatus { Succeeded, AlreadyExists, RootNotFound, RootUnavailable, SourceMissing, NotFound, Failed }

internal sealed record SourceFingerprint(int Version, string Value);

internal sealed record MediaAsset(
    Guid AssetId,
    Guid RootId,
    string RelativePath,
    string RelativePathKey,
    string MediaType,
    long FileSizeBytes,
    long LastWriteUtcTicks,
    SourceFingerprint? Fingerprint,
    MediaAssetSourceStatus SourceStatus,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc,
    DateTimeOffset? LastSeenUtc);

internal sealed record MediaAssetResolution(
    MediaAsset Asset,
    MediaRootAvailability RootAvailability,
    string? PhysicalPath,
    bool SourceExists,
    string? Diagnostic = null);

internal sealed record MediaAssetOperationResult(
    MediaAssetOperationStatus Status,
    MediaAssetResolution? Asset = null,
    string? Diagnostic = null)
{
    public bool Succeeded => Status == MediaAssetOperationStatus.Succeeded;
}

internal interface ISourceFingerprintService
{
    Task<SourceFingerprint> CreateAsync(string path, CancellationToken cancellationToken = default);
}

/// <summary>
/// Version 1 hashes bounded samples, not the complete source. It detects many ordinary changes cheaply but is not
/// cryptographic proof that two media files are byte-for-byte identical.
/// </summary>
internal sealed class SampledSourceFingerprintService : ISourceFingerprintService
{
    public const int CurrentVersion = 1;
    internal const int SampleBytes = 64 * 1024;
    private static readonly byte[] Domain = Encoding.ASCII.GetBytes("Lightflow.SourceFingerprint.v1\0");

    public Task<SourceFingerprint> CreateAsync(string path, CancellationToken cancellationToken = default) =>
        Task.Run(() => Create(path, cancellationToken), cancellationToken);

    private static SourceFingerprint Create(string path, CancellationToken cancellationToken)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, SampleBytes, FileOptions.SequentialScan);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Domain);
        Span<byte> length = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64LittleEndian(length, stream.Length);
        hash.AppendData(length);

        var buffer = new byte[SampleBytes];
        var headLength = ReadAtMost(stream, buffer, Math.Min(SampleBytes, stream.Length), cancellationToken);
        hash.AppendData(buffer, 0, headLength);
        if (stream.Length > SampleBytes)
        {
            stream.Position = Math.Max(SampleBytes, stream.Length - SampleBytes);
            var tailLength = ReadAtMost(stream, buffer, Math.Min(SampleBytes, stream.Length - stream.Position), cancellationToken);
            hash.AppendData(buffer, 0, tailLength);
        }

        return new(CurrentVersion, Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant());
    }

    private static int ReadAtMost(Stream stream, byte[] buffer, long count, CancellationToken cancellationToken)
    {
        var total = 0;
        while (total < count)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = stream.Read(buffer, total, (int)Math.Min(buffer.Length - total, count - total));
            if (read == 0) break;
            total += read;
        }
        return total;
    }
}

internal interface IMediaAssetRepository
{
    Task<MediaAsset?> GetAsync(Guid assetId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MediaAsset>> ListAsync(CancellationToken cancellationToken = default);
    Task<MediaAsset?> FindAsync(Guid rootId, string relativePathKey, CancellationToken cancellationToken = default);
    Task<MediaAssetOperationStatus> CreateAsync(MediaAsset asset, CancellationToken cancellationToken = default);
    Task<bool> UpdateObservationAsync(Guid assetId, long size, long lastWriteUtcTicks,
        SourceFingerprint fingerprint, DateTimeOffset observedUtc, CancellationToken cancellationToken = default);
    Task<bool> MarkMissingAsync(Guid assetId, DateTimeOffset observedUtc, CancellationToken cancellationToken = default);
    Task<int> MarkMissingAsync(IReadOnlyCollection<Guid> assetIds, DateTimeOffset observedUtc,
        CancellationToken cancellationToken = default);
    Task<MediaAssetOperationStatus> RelocateAsync(Guid assetId, Guid rootId, string relativePath,
        DateTimeOffset observedUtc, CancellationToken cancellationToken = default);
}

internal sealed class CatalogMediaAssetRepository(Func<CatalogDatabaseSession?> session) : IMediaAssetRepository
{
    public Task<MediaAsset?> GetAsync(Guid assetId, CancellationToken cancellationToken = default) => RunAsync(() =>
    {
        using var connection = RequireSession().OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = SelectSql + " WHERE AssetId=$asset;";
        command.Parameters.AddWithValue("$asset", assetId.ToString("D"));
        return ReadOne(command);
    }, cancellationToken);

    public Task<IReadOnlyList<MediaAsset>> ListAsync(CancellationToken cancellationToken = default) => RunAsync<IReadOnlyList<MediaAsset>>(() =>
    {
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

    public Task<MediaAssetOperationStatus> CreateAsync(MediaAsset asset, CancellationToken cancellationToken = default) => RunAsync(() =>
    {
        using var connection = RequireSession().OpenConnection();
        using var transaction = connection.BeginTransaction();
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
    }, cancellationToken);

    public Task<bool> UpdateObservationAsync(Guid assetId, long size, long lastWriteUtcTicks,
        SourceFingerprint fingerprint, DateTimeOffset observedUtc, CancellationToken cancellationToken = default) => RunAsync(() =>
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
    }, cancellationToken);

    public Task<bool> MarkMissingAsync(Guid assetId, DateTimeOffset observedUtc, CancellationToken cancellationToken = default) => RunAsync(() =>
    {
        using var connection = RequireSession().OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE MediaAssets SET SourceStatus='missing', UpdatedUtc=$now WHERE AssetId=$asset;";
        command.Parameters.AddWithValue("$now", Timestamp(observedUtc));
        command.Parameters.AddWithValue("$asset", assetId.ToString("D"));
        return command.ExecuteNonQuery() == 1;
    }, cancellationToken);

    public Task<int> MarkMissingAsync(IReadOnlyCollection<Guid> assetIds, DateTimeOffset observedUtc,
        CancellationToken cancellationToken = default) => RunAsync(() =>
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
    }, cancellationToken);

    public Task<MediaAssetOperationStatus> RelocateAsync(Guid assetId, Guid rootId, string relativePath,
        DateTimeOffset observedUtc, CancellationToken cancellationToken = default) => RunAsync(() =>
    {
        var normalized = MediaPathSemantics.NormalizeRelativePath(relativePath);
        using var connection = RequireSession().OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE MediaAssets SET RootId=$root, RelativePath=$path, RelativePathKey=$key,
                SourceStatus='available', UpdatedUtc=$now, LastSeenUtc=$now WHERE AssetId=$asset;
            """;
        command.Parameters.AddWithValue("$root", rootId.ToString("D"));
        command.Parameters.AddWithValue("$path", normalized);
        command.Parameters.AddWithValue("$key", MediaPathSemantics.RelativePathKey(normalized));
        command.Parameters.AddWithValue("$now", Timestamp(observedUtc));
        command.Parameters.AddWithValue("$asset", assetId.ToString("D"));
        try { return command.ExecuteNonQuery() == 1 ? MediaAssetOperationStatus.Succeeded : MediaAssetOperationStatus.NotFound; }
        catch (SqliteException exception) when (exception.SqliteExtendedErrorCode == 2067)
        { return MediaAssetOperationStatus.AlreadyExists; }
    }, cancellationToken);

    private CatalogDatabaseSession RequireSession() => session() ?? throw new InvalidOperationException("The Catalog is unavailable.");

    private static MediaAsset? ReadOne(SqliteCommand command)
    {
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;
        return Read(reader);
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

internal interface IMediaAssetService
{
    Task<MediaAssetOperationResult> CreateAsync(Guid rootId, string relativePath, string mediaType = "unknown",
        CancellationToken cancellationToken = default);
    Task<MediaAssetResolution?> GetAsync(Guid assetId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MediaAsset>> ListAsync(CancellationToken cancellationToken = default);
    Task<MediaAssetResolution?> FindAsync(Guid rootId, string relativePath, CancellationToken cancellationToken = default);
    Task<MediaAssetOperationResult> ObserveAsync(Guid assetId, CancellationToken cancellationToken = default);
    Task<int> MarkMissingAsync(IReadOnlyCollection<Guid> assetIds, CancellationToken cancellationToken = default);
    Task<MediaAssetOperationResult> RelocateAsync(Guid assetId, Guid rootId, string relativePath,
        CancellationToken cancellationToken = default) => Task.FromResult(new MediaAssetOperationResult(
            MediaAssetOperationStatus.Failed, Diagnostic: "Catalog relocation is not supported by this asset provider."));
}

internal sealed class MediaAssetService(IMediaAssetRepository repository, IMediaRootService roots,
    ISourceFingerprintService fingerprints) : IMediaAssetService
{
    public async Task<MediaAssetOperationResult> CreateAsync(Guid rootId, string relativePath, string mediaType = "unknown",
        CancellationToken cancellationToken = default)
    {
        var normalized = MediaPathSemantics.NormalizeRelativePath(relativePath);
        var key = MediaPathSemantics.RelativePathKey(normalized);
        if (await repository.FindAsync(rootId, key, cancellationToken).ConfigureAwait(false) is not null)
            return new(MediaAssetOperationStatus.AlreadyExists, Diagnostic: "An asset already exists at that logical location.");
        MediaPathResolution resolved;
        try { resolved = await roots.ResolveAsync(rootId, normalized, cancellationToken).ConfigureAwait(false); }
        catch (KeyNotFoundException)
        {
            return new(MediaAssetOperationStatus.RootNotFound, Diagnostic: "The Media Root does not exist.");
        }
        if (resolved.RootAvailability != MediaRootAvailability.Online)
            return new(MediaAssetOperationStatus.RootUnavailable, Diagnostic: resolved.Diagnostic);
        if (!resolved.Exists || resolved.PhysicalPath is null)
            return new(MediaAssetOperationStatus.SourceMissing, Diagnostic: resolved.Diagnostic);

        try
        {
            var observation = await ObserveFileAsync(resolved.PhysicalPath, cancellationToken).ConfigureAwait(false);
            var now = DateTimeOffset.UtcNow;
            var asset = new MediaAsset(Guid.NewGuid(), rootId, normalized, key, NormalizeMediaType(mediaType),
                observation.Size, observation.LastWriteUtcTicks, observation.Fingerprint, MediaAssetSourceStatus.Available,
                now, now, now);
            var created = await repository.CreateAsync(asset, cancellationToken).ConfigureAwait(false);
            if (created != MediaAssetOperationStatus.Succeeded)
                return new(created, Diagnostic: "An asset already exists at that logical location.");
            return new(created, new(asset, resolved.RootAvailability, resolved.PhysicalPath, true));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new(MediaAssetOperationStatus.Failed, Diagnostic: $"The source could not be observed: {exception.Message}");
        }
        catch (SqliteException exception)
        {
            return new(MediaAssetOperationStatus.Failed, Diagnostic: $"The asset could not be saved: {exception.Message}");
        }
    }

    public async Task<MediaAssetResolution?> GetAsync(Guid assetId, CancellationToken cancellationToken = default)
    {
        var asset = await repository.GetAsync(assetId, cancellationToken).ConfigureAwait(false);
        return asset is null ? null : await ResolveAsync(asset, cancellationToken).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<MediaAsset>> ListAsync(CancellationToken cancellationToken = default) =>
        repository.ListAsync(cancellationToken);

    public async Task<MediaAssetResolution?> FindAsync(Guid rootId, string relativePath, CancellationToken cancellationToken = default)
    {
        var key = MediaPathSemantics.RelativePathKey(relativePath);
        var asset = await repository.FindAsync(rootId, key, cancellationToken).ConfigureAwait(false);
        return asset is null ? null : await ResolveAsync(asset, cancellationToken).ConfigureAwait(false);
    }

    public async Task<MediaAssetOperationResult> ObserveAsync(Guid assetId, CancellationToken cancellationToken = default)
    {
        var asset = await repository.GetAsync(assetId, cancellationToken).ConfigureAwait(false);
        if (asset is null) return new(MediaAssetOperationStatus.NotFound, Diagnostic: "The asset no longer exists.");
        var resolved = await roots.ResolveAsync(asset.RootId, asset.RelativePath, cancellationToken).ConfigureAwait(false);
        if (resolved.RootAvailability != MediaRootAvailability.Online)
            return new(MediaAssetOperationStatus.RootUnavailable,
                new(asset, resolved.RootAvailability, null, false, resolved.Diagnostic), resolved.Diagnostic);
        if (!resolved.Exists || resolved.PhysicalPath is null)
        {
            if (!await repository.MarkMissingAsync(asset.AssetId, DateTimeOffset.UtcNow, cancellationToken).ConfigureAwait(false))
                return new(MediaAssetOperationStatus.NotFound, Diagnostic: "The asset no longer exists.");
            var missing = (await repository.GetAsync(asset.AssetId, cancellationToken).ConfigureAwait(false))!;
            return new(MediaAssetOperationStatus.SourceMissing,
                new(missing, MediaRootAvailability.Online, resolved.PhysicalPath, false, resolved.Diagnostic), resolved.Diagnostic);
        }

        try
        {
            var observation = await ObserveFileAsync(resolved.PhysicalPath, cancellationToken).ConfigureAwait(false);
            if (!await repository.UpdateObservationAsync(asset.AssetId, observation.Size, observation.LastWriteUtcTicks,
                observation.Fingerprint, DateTimeOffset.UtcNow, cancellationToken).ConfigureAwait(false))
                return new(MediaAssetOperationStatus.NotFound, Diagnostic: "The asset no longer exists.");
            var updated = (await repository.GetAsync(asset.AssetId, cancellationToken).ConfigureAwait(false))!;
            return new(MediaAssetOperationStatus.Succeeded,
                new(updated, MediaRootAvailability.Online, resolved.PhysicalPath, true));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new(MediaAssetOperationStatus.Failed,
                new(asset, MediaRootAvailability.Online, resolved.PhysicalPath, false), exception.Message);
        }
    }

    public Task<int> MarkMissingAsync(IReadOnlyCollection<Guid> assetIds,
        CancellationToken cancellationToken = default) =>
        repository.MarkMissingAsync(assetIds, DateTimeOffset.UtcNow, cancellationToken);

    public async Task<MediaAssetOperationResult> RelocateAsync(Guid assetId, Guid rootId, string relativePath,
        CancellationToken cancellationToken = default)
    {
        var normalized = MediaPathSemantics.NormalizeRelativePath(relativePath);
        var existing = await repository.FindAsync(rootId, MediaPathSemantics.RelativePathKey(normalized), cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null && existing.AssetId != assetId)
            return new(MediaAssetOperationStatus.AlreadyExists, Diagnostic: "Another asset already owns the destination location.");
        var status = await repository.RelocateAsync(assetId, rootId, normalized, DateTimeOffset.UtcNow, cancellationToken)
            .ConfigureAwait(false);
        if (status != MediaAssetOperationStatus.Succeeded) return new(status, Diagnostic: "The Catalog location could not be updated.");
        return await ObserveAsync(assetId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<MediaAssetResolution> ResolveAsync(MediaAsset asset, CancellationToken cancellationToken)
    {
        var resolved = await roots.ResolveAsync(asset.RootId, asset.RelativePath, cancellationToken).ConfigureAwait(false);
        return new(asset, resolved.RootAvailability, resolved.PhysicalPath, resolved.Exists, resolved.Diagnostic);
    }

    private async Task<(long Size, long LastWriteUtcTicks, SourceFingerprint Fingerprint)> ObserveFileAsync(
        string path, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var before = ReadFacts(path);
            var fingerprint = await fingerprints.CreateAsync(path, cancellationToken).ConfigureAwait(false);
            var after = ReadFacts(path);
            if (before == after) return (after.Size, after.LastWriteUtcTicks, fingerprint);
        }
        throw new IOException("The source changed while Lightflow was observing it. Try again after writes finish.");
    }

    private static (long Size, long LastWriteUtcTicks) ReadFacts(string path)
    {
        var info = new FileInfo(path);
        info.Refresh();
        if (!info.Exists) throw new FileNotFoundException("The source is no longer available.", path);
        return (info.Length, info.LastWriteTimeUtc.Ticks);
    }

    private static string NormalizeMediaType(string mediaType) =>
        string.IsNullOrWhiteSpace(mediaType) ? "unknown" : mediaType.Trim().ToLowerInvariant();
}
