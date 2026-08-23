using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace LightflowStudio;

internal enum LutDimension { OneDimensional, ThreeDimensional }
internal enum LutResourceAvailability { Available, Invalid }

internal sealed record LutValidationResult(
    bool IsValid,
    LutDimension? Dimension = null,
    int? Size = null,
    string? Diagnostic = null);

internal sealed record ManagedLutResource(
    Guid LutId,
    string DisplayName,
    string OriginalFileName,
    string ContentSha256,
    LutDimension Dimension,
    int Size,
    LutResourceAvailability Availability,
    string? Diagnostic = null);

internal enum LutImportStatus { Imported, DuplicateContent, Invalid, Failed }
internal sealed record LutImportResult(LutImportStatus Status, ManagedLutResource? Resource = null, string? Diagnostic = null)
{
    public bool Succeeded => Status is LutImportStatus.Imported or LutImportStatus.DuplicateContent;
}

internal enum LutRemovalStatus { Removed, NotFound, Assigned, Failed }
internal sealed record LutRemovalResult(LutRemovalStatus Status, string? Diagnostic = null)
{
    public bool Succeeded => Status == LutRemovalStatus.Removed;
}

internal static class CubeLutValidator
{
    public const int MaximumBytes = 16 * 1024 * 1024;

    public static LutValidationResult Validate(ReadOnlySpan<byte> content)
    {
        if (content.IsEmpty) return Invalid("The LUT file is empty.");
        if (content.Length > MaximumBytes) return Invalid("The LUT file is larger than the supported 16 MB limit.");
        string text;
        try { text = new UTF8Encoding(false, true).GetString(content); }
        catch (DecoderFallbackException) { return Invalid("The LUT file is not valid UTF-8 text."); }

        LutDimension? dimension = null;
        int size = 0;
        var values = 0L;
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim().TrimEnd('\r');
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var comment = line.IndexOf('#');
            if (comment >= 0) line = line[..comment].Trim();
            if (line.Length == 0) continue;
            var fields = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (fields[0].Equals("TITLE", StringComparison.OrdinalIgnoreCase)) continue;
            if (fields[0].Equals("DOMAIN_MIN", StringComparison.OrdinalIgnoreCase)
                || fields[0].Equals("DOMAIN_MAX", StringComparison.OrdinalIgnoreCase))
            {
                if (fields.Length != 4 || !fields.Skip(1).All(IsFiniteNumber))
                    return Invalid($"{fields[0]} must contain three finite numbers.");
                continue;
            }
            if (fields[0].Equals("LUT_1D_SIZE", StringComparison.OrdinalIgnoreCase)
                || fields[0].Equals("LUT_3D_SIZE", StringComparison.OrdinalIgnoreCase))
            {
                if (fields[0].Equals("LUT_1D_SIZE", StringComparison.OrdinalIgnoreCase))
                    return Invalid("1D .cube LUTs are not supported by Lightflow's current LUT rendering contract; import a 3D LUT.");
                if (dimension is not null || fields.Length != 2 || !int.TryParse(fields[1], NumberStyles.None,
                        CultureInfo.InvariantCulture, out size))
                    return Invalid("The LUT must contain exactly one valid LUT_1D_SIZE or LUT_3D_SIZE declaration.");
                dimension = LutDimension.ThreeDimensional;
                const int maximum = 256;
                if (size is < 2 || size > maximum)
                    return Invalid($"The declared LUT size must be between 2 and {maximum}.");
                continue;
            }
            if (!ThreeFiniteNumbers(fields)) return Invalid("Each LUT data row must contain three finite numbers.");
            values++;
        }

        if (dimension is null) return Invalid("The LUT is missing a LUT_1D_SIZE or LUT_3D_SIZE declaration.");
        var expected = dimension == LutDimension.OneDimensional ? size : checked((long)size * size * size);
        if (values != expected) return Invalid($"The LUT declares {expected:N0} data rows but contains {values:N0}.");
        return new(true, dimension, size);
    }

    private static bool ThreeFiniteNumbers(string[] fields) => fields.Length == 3 && fields.All(IsFiniteNumber);
    private static bool IsFiniteNumber(string field) =>
        double.TryParse(field, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value);
    private static LutValidationResult Invalid(string diagnostic) => new(false, Diagnostic: diagnostic);
}

internal interface IManagedLutLibrary
{
    Task<IReadOnlyList<ManagedLutResource>> ListAsync(CancellationToken cancellationToken = default);
    Task<LutImportResult> ImportAsync(string sourcePath, CancellationToken cancellationToken = default);
    Task<ManagedLutResource?> RenameAsync(Guid lutId, string displayName, CancellationToken cancellationToken = default);
    Task<LutRemovalResult> RemoveAsync(Guid lutId, CancellationToken cancellationToken = default);
    Task<string> MaterializeAsync(Guid lutId, CancellationToken cancellationToken = default);
}

internal sealed class CatalogManagedLutLibrary(
    Func<CatalogDatabaseSession?> session,
    string materializationDirectory,
    Func<DateTimeOffset>? utcNow = null) : IManagedLutLibrary
{
    private readonly Func<DateTimeOffset> _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);

    public Task<IReadOnlyList<ManagedLutResource>> ListAsync(CancellationToken cancellationToken = default) => Task.Run<IReadOnlyList<ManagedLutResource>>(() =>
    {
        using var connection = RequireSession().OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT LutId,DisplayName,OriginalFileName,ContentSha256,LutKind,LutSize,CubeContent
            FROM LutResources ORDER BY DisplayName COLLATE NOCASE, LutId;
            """;
        using var reader = command.ExecuteReader();
        var resources = new List<ManagedLutResource>();
        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            resources.Add(ReadResource(reader, validateContent: true));
        }
        return resources;
    }, cancellationToken);

    public async Task<LutImportResult> ImportAsync(string sourcePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !sourcePath.EndsWith(".cube", StringComparison.OrdinalIgnoreCase))
            return new(LutImportStatus.Invalid, Diagnostic: "Choose a .cube LUT file.");
        byte[] content;
        try { content = await File.ReadAllBytesAsync(sourcePath, cancellationToken).ConfigureAwait(false); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        { return new(LutImportStatus.Failed, Diagnostic: exception.Message); }
        var validation = CubeLutValidator.Validate(content);
        if (!validation.IsValid) return new(LutImportStatus.Invalid, Diagnostic: validation.Diagnostic);
        var hash = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
        var displayName = LutCatalog.MakeDisplayName(sourcePath);
        var fileName = Path.GetFileName(sourcePath);
        return await Task.Run(() =>
        {
            using var connection = RequireSession().OpenConnection();
            using (var existing = connection.CreateCommand())
            {
                existing.CommandText = """
                    SELECT LutId,DisplayName,OriginalFileName,ContentSha256,LutKind,LutSize,CubeContent
                    FROM LutResources WHERE ContentSha256=$hash;
                    """;
                existing.Parameters.AddWithValue("$hash", hash);
                using var reader = existing.ExecuteReader();
                if (reader.Read()) return new LutImportResult(LutImportStatus.DuplicateContent, ReadResource(reader, true));
            }
            var id = Guid.NewGuid();
            var now = FormatUtc(_utcNow());
            using var insert = connection.CreateCommand();
            insert.CommandText = """
                INSERT INTO LutResources
                    (LutId,DisplayName,OriginalFileName,ContentSha256,CubeContent,LutKind,LutSize,CreatedUtc,UpdatedUtc)
                VALUES ($id,$name,$file,$hash,$content,$kind,$size,$now,$now);
                """;
            insert.Parameters.AddWithValue("$id", id.ToString("D"));
            insert.Parameters.AddWithValue("$name", displayName);
            insert.Parameters.AddWithValue("$file", fileName);
            insert.Parameters.AddWithValue("$hash", hash);
            insert.Parameters.AddWithValue("$content", content);
            insert.Parameters.AddWithValue("$kind", validation.Dimension == LutDimension.OneDimensional ? "1d" : "3d");
            insert.Parameters.AddWithValue("$size", validation.Size!.Value);
            insert.Parameters.AddWithValue("$now", now);
            insert.ExecuteNonQuery();
            return new LutImportResult(LutImportStatus.Imported,
                new(id, displayName, fileName, hash, validation.Dimension!.Value, validation.Size.Value,
                    LutResourceAvailability.Available));
        }, cancellationToken).ConfigureAwait(false);
    }

    public Task<ManagedLutResource?> RenameAsync(Guid lutId, string displayName,
        CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        displayName = displayName?.Trim() ?? "";
        if (displayName.Length == 0) throw new ArgumentException("A LUT display name is required.", nameof(displayName));
        using var connection = RequireSession().OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE LutResources SET DisplayName=$name,UpdatedUtc=$now WHERE LutId=$id;";
        command.Parameters.AddWithValue("$name", displayName);
        command.Parameters.AddWithValue("$now", FormatUtc(_utcNow()));
        command.Parameters.AddWithValue("$id", lutId.ToString("D"));
        if (command.ExecuteNonQuery() == 0) return null;
        return ReadById(connection, lutId);
    }, cancellationToken);

    public Task<LutRemovalResult> RemoveAsync(Guid lutId, CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        using var connection = RequireSession().OpenConnection();
        using (var assigned = connection.CreateCommand())
        {
            assigned.CommandText = "SELECT count(*) FROM MediaAssetColor WHERE CameraLutId=$id OR CreativeLutId=$id;";
            assigned.Parameters.AddWithValue("$id", lutId.ToString("D"));
            if (Convert.ToInt64(assigned.ExecuteScalar()) > 0)
                return new LutRemovalResult(LutRemovalStatus.Assigned,
                    "This LUT is assigned to one or more Catalog assets. Clear those assignments before removing it.");
        }
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM LutResources WHERE LutId=$id;";
        command.Parameters.AddWithValue("$id", lutId.ToString("D"));
        return command.ExecuteNonQuery() == 1
            ? new(LutRemovalStatus.Removed)
            : new(LutRemovalStatus.NotFound, "The LUT is no longer in the managed library.");
    }, cancellationToken);

    public Task<string> MaterializeAsync(Guid lutId, CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        using var connection = RequireSession().OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT ContentSha256,CubeContent FROM LutResources WHERE LutId=$id;";
        command.Parameters.AddWithValue("$id", lutId.ToString("D"));
        using var reader = command.ExecuteReader();
        if (!reader.Read()) throw new FileNotFoundException("The managed LUT resource no longer exists.");
        var hash = reader.GetString(0);
        var content = (byte[])reader[1];
        var validation = CubeLutValidator.Validate(content);
        if (!validation.IsValid || !string.Equals(hash, Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant(),
                StringComparison.Ordinal))
            throw new InvalidDataException("The managed LUT resource failed integrity validation.");
        Directory.CreateDirectory(materializationDirectory);
        var path = Path.Combine(materializationDirectory, $"{lutId:D}-{hash[..12]}.cube");
        if (!File.Exists(path) || !File.ReadAllBytes(path).AsSpan().SequenceEqual(content))
        {
            var temporary = path + $".{Guid.NewGuid():N}.tmp";
            try { File.WriteAllBytes(temporary, content); File.Move(temporary, path, true); }
            finally { try { File.Delete(temporary); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { } }
        }
        return path;
    }, cancellationToken);

    private static ManagedLutResource ReadById(SqliteConnection connection, Guid lutId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT LutId,DisplayName,OriginalFileName,ContentSha256,LutKind,LutSize,CubeContent
            FROM LutResources WHERE LutId=$id;
            """;
        command.Parameters.AddWithValue("$id", lutId.ToString("D"));
        using var reader = command.ExecuteReader();
        if (!reader.Read()) throw new InvalidOperationException("The LUT resource disappeared during the operation.");
        return ReadResource(reader, true);
    }

    private static ManagedLutResource ReadResource(SqliteDataReader reader, bool validateContent)
    {
        var dimension = reader.GetString(4) == "1d" ? LutDimension.OneDimensional : LutDimension.ThreeDimensional;
        var availability = LutResourceAvailability.Available;
        string? diagnostic = null;
        if (validateContent)
        {
            var content = (byte[])reader[6];
            var validation = CubeLutValidator.Validate(content);
            var actualHash = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
            if (!validation.IsValid || !string.Equals(actualHash, reader.GetString(3), StringComparison.Ordinal)
                || validation.Dimension != dimension || validation.Size != reader.GetInt32(5))
            { availability = LutResourceAvailability.Invalid; diagnostic = validation.Diagnostic ?? "Content integrity validation failed."; }
        }
        return new(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(2), reader.GetString(3),
            dimension, reader.GetInt32(5), availability, diagnostic);
    }

    private CatalogDatabaseSession RequireSession() => session() ?? throw new InvalidOperationException("The Catalog is unavailable.");
    private static string FormatUtc(DateTimeOffset value) => value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
}

internal enum ColorLutStage { Camera, Creative }
internal sealed record ColorLutReference(Guid LutId, string DisplayName, string ContentSha256,
    LutResourceAvailability Availability, string? Diagnostic = null);
internal sealed record AssetColorIntent(Guid AssetId, ColorLutReference? Camera, ColorLutReference? Creative,
    string ColorIdentity)
{
    public IReadOnlyList<ColorLutReference> OrderedPipeline => new[] { Camera, Creative }.OfType<ColorLutReference>().ToArray();
    public bool HasColor => Camera is not null || Creative is not null;
}
internal sealed record ColorAssignmentChange(Guid AssetId, Guid? CameraLutId, Guid? CreativeLutId);

internal interface IAssetColorStore
{
    Task<AssetColorIntent> GetAsync(Guid assetId, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<Guid, AssetColorIntent>> GetAsync(IReadOnlyCollection<Guid> assetIds,
        CancellationToken cancellationToken = default);
    Task SetStageAsync(IReadOnlyCollection<Guid> assetIds, ColorLutStage stage, Guid? lutId,
        CancellationToken cancellationToken = default);
    Task SetAsync(IReadOnlyCollection<ColorAssignmentChange> changes, CancellationToken cancellationToken = default);
}

internal sealed class CatalogAssetColorStore(Func<CatalogDatabaseSession?> session, Func<DateTimeOffset>? utcNow = null)
    : IAssetColorStore
{
    private readonly Func<DateTimeOffset> _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);

    public async Task<AssetColorIntent> GetAsync(Guid assetId, CancellationToken cancellationToken = default) =>
        (await GetAsync([assetId], cancellationToken).ConfigureAwait(false))[assetId];

    public Task<IReadOnlyDictionary<Guid, AssetColorIntent>> GetAsync(IReadOnlyCollection<Guid> assetIds,
        CancellationToken cancellationToken = default) => Task.Run<IReadOnlyDictionary<Guid, AssetColorIntent>>(() =>
    {
        var ids = assetIds.Distinct().ToArray();
        var result = ids.ToDictionary(id => id, Empty);
        if (ids.Length == 0) return result;
        using var connection = RequireSession().OpenConnection();
        foreach (var batch in ids.Chunk(400))
        {
            using var command = connection.CreateCommand();
            var parameters = batch.Select((id, index) =>
            { var name = $"$id{index}"; command.Parameters.AddWithValue(name, id.ToString("D")); return name; }).ToArray();
            command.CommandText = $"""
                SELECT c.AssetId,c.CameraLutId,cam.DisplayName,cam.ContentSha256,cam.CubeContent,cam.LutKind,cam.LutSize,
                       c.CreativeLutId,creative.DisplayName,creative.ContentSha256,creative.CubeContent,creative.LutKind,creative.LutSize
                FROM MediaAssetColor c
                LEFT JOIN LutResources cam ON cam.LutId=c.CameraLutId
                LEFT JOIN LutResources creative ON creative.LutId=c.CreativeLutId
                WHERE c.AssetId IN ({string.Join(',', parameters)});
                """;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var assetId = Guid.Parse(reader.GetString(0));
                var camera = ReadReference(reader, 1);
                var creative = ReadReference(reader, 7);
                result[assetId] = new(assetId, camera, creative, Identity(camera, creative));
            }
        }
        return result;
    }, cancellationToken);

    public Task SetStageAsync(IReadOnlyCollection<Guid> assetIds, ColorLutStage stage, Guid? lutId,
        CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        var ids = assetIds.Distinct().ToArray();
        if (ids.Length == 0) return;
        using var connection = RequireSession().OpenConnection();
        using var transaction = connection.BeginTransaction();
        foreach (var assetId in ids)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureExists(connection, transaction, "MediaAssets", "AssetId", assetId, "Catalog asset");
        }
        if (lutId is Guid resourceId)
            EnsureExists(connection, transaction, "LutResources", "LutId", resourceId,
                stage == ColorLutStage.Camera ? "Camera LUT" : "Creative LUT");
        var now = _utcNow().UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
        foreach (var assetId in ids)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = lutId is null
                ? (stage == ColorLutStage.Camera
                    ? "DELETE FROM MediaAssetColor WHERE AssetId=$asset AND CreativeLutId IS NULL; UPDATE MediaAssetColor SET CameraLutId=NULL,UpdatedUtc=$now WHERE AssetId=$asset;"
                    : "DELETE FROM MediaAssetColor WHERE AssetId=$asset AND CameraLutId IS NULL; UPDATE MediaAssetColor SET CreativeLutId=NULL,UpdatedUtc=$now WHERE AssetId=$asset;")
                : stage == ColorLutStage.Camera ? """
                INSERT INTO MediaAssetColor (AssetId,CameraLutId,CreativeLutId,CreatedUtc,UpdatedUtc)
                VALUES ($asset,$lut,NULL,$now,$now)
                ON CONFLICT(AssetId) DO UPDATE SET CameraLutId=excluded.CameraLutId,UpdatedUtc=excluded.UpdatedUtc;
                """ : """
                INSERT INTO MediaAssetColor (AssetId,CameraLutId,CreativeLutId,CreatedUtc,UpdatedUtc)
                VALUES ($asset,NULL,$lut,$now,$now)
                ON CONFLICT(AssetId) DO UPDATE SET CreativeLutId=excluded.CreativeLutId,UpdatedUtc=excluded.UpdatedUtc;
                """;
            command.Parameters.AddWithValue("$asset", assetId.ToString("D"));
            command.Parameters.AddWithValue("$lut", lutId?.ToString("D") ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("$now", now);
            command.ExecuteNonQuery();
        }
        transaction.Commit();
    }, cancellationToken);

    public Task SetAsync(IReadOnlyCollection<ColorAssignmentChange> changes,
        CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        var normalized = changes.GroupBy(change => change.AssetId).Select(group => group.Last()).ToArray();
        if (normalized.Length == 0) return;
        using var connection = RequireSession().OpenConnection();
        using var transaction = connection.BeginTransaction();
        foreach (var change in normalized)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureExists(connection, transaction, "MediaAssets", "AssetId", change.AssetId, "Catalog asset");
            if (change.CameraLutId is Guid camera) EnsureExists(connection, transaction, "LutResources", "LutId", camera, "Camera LUT");
            if (change.CreativeLutId is Guid creative) EnsureExists(connection, transaction, "LutResources", "LutId", creative, "Creative LUT");
        }
        var now = _utcNow().UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
        foreach (var change in normalized)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.Parameters.AddWithValue("$asset", change.AssetId.ToString("D"));
            if (change.CameraLutId is null && change.CreativeLutId is null)
                command.CommandText = "DELETE FROM MediaAssetColor WHERE AssetId=$asset;";
            else
            {
                command.CommandText = """
                    INSERT INTO MediaAssetColor (AssetId,CameraLutId,CreativeLutId,CreatedUtc,UpdatedUtc)
                    VALUES ($asset,$camera,$creative,$now,$now)
                    ON CONFLICT(AssetId) DO UPDATE SET CameraLutId=excluded.CameraLutId,
                        CreativeLutId=excluded.CreativeLutId,UpdatedUtc=excluded.UpdatedUtc;
                    """;
                command.Parameters.AddWithValue("$camera", change.CameraLutId?.ToString("D") ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("$creative", change.CreativeLutId?.ToString("D") ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("$now", now);
            }
            command.ExecuteNonQuery();
        }
        transaction.Commit();
    }, cancellationToken);

    private static void EnsureExists(SqliteConnection connection, SqliteTransaction transaction, string table,
        string column, Guid id, string label)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT count(*) FROM {table} WHERE {column}=$id;";
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        if (Convert.ToInt64(command.ExecuteScalar()) != 1) throw new InvalidOperationException($"{label} '{id:D}' does not exist.");
    }

    private static ColorLutReference? ReadReference(SqliteDataReader reader, int offset)
    {
        if (reader.IsDBNull(offset)) return null;
        if (reader.IsDBNull(offset + 1) || reader.IsDBNull(offset + 2) || reader.IsDBNull(offset + 3))
            return new(Guid.Parse(reader.GetString(offset)), "Unavailable LUT", "", LutResourceAvailability.Invalid,
                "The assigned LUT resource is unavailable.");
        var content = (byte[])reader[offset + 3];
        var validation = CubeLutValidator.Validate(content);
        var valid = validation.IsValid
            && string.Equals(Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant(), reader.GetString(offset + 2), StringComparison.Ordinal)
            && (validation.Dimension == LutDimension.OneDimensional ? "1d" : "3d") == reader.GetString(offset + 4)
            && validation.Size == reader.GetInt32(offset + 5);
        return new(Guid.Parse(reader.GetString(offset)), reader.GetString(offset + 1), reader.GetString(offset + 2),
            valid ? LutResourceAvailability.Available : LutResourceAvailability.Invalid,
            valid ? null : validation.Diagnostic ?? "The assigned LUT failed integrity validation.");
    }

    private static AssetColorIntent Empty(Guid assetId) => new(assetId, null, null, Identity(null, null));
    private static string Identity(ColorLutReference? camera, ColorLutReference? creative)
    {
        var contract = $"lightflow-color-v1\ncamera:{camera?.LutId:D}:{camera?.ContentSha256}\ncreative:{creative?.LutId:D}:{creative?.ContentSha256}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(contract))).ToLowerInvariant();
    }
    private CatalogDatabaseSession RequireSession() => session() ?? throw new InvalidOperationException("The Catalog is unavailable.");
}
