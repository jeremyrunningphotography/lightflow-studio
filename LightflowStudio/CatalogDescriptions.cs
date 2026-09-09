using Microsoft.Data.Sqlite;

namespace LightflowStudio;

// Authored Catalog values. Never populated from or folded into derived/source metadata.
internal enum AssetDescriptionField { Title, Description, Notes, CreatorOverride, CreditOverride }
internal sealed record AssetDescription(Guid AssetId, string? Title = null, string? Description = null,
    string? Notes = null, string? CreatorOverride = null, string? CreditOverride = null, long Revision = 0)
{
    public string? Get(AssetDescriptionField field) => field switch
    {
        AssetDescriptionField.Title => Title,
        AssetDescriptionField.Description => Description,
        AssetDescriptionField.Notes => Notes,
        AssetDescriptionField.CreatorOverride => CreatorOverride,
        AssetDescriptionField.CreditOverride => CreditOverride,
        _ => throw new ArgumentOutOfRangeException(nameof(field))
    };
}

// Omitted fields are untouched; a present null is an explicit Clear.
internal sealed record AssetDescriptionPatch(IReadOnlyDictionary<AssetDescriptionField, string?> Values)
{
    public void Validate()
    {
        foreach (var (field, value) in Values)
        {
            if (!Enum.IsDefined(field)) throw new ArgumentOutOfRangeException(nameof(Values));
            if (value is null) continue;
            if (value.Length == 0) throw new ArgumentException("Enter a value to Set, or choose Clear.");
            if (value.Contains('\0')) throw new ArgumentException("Descriptions cannot contain a null character.");
            if (field is not (AssetDescriptionField.Description or AssetDescriptionField.Notes) && value.IndexOfAny(['\r', '\n']) >= 0)
                throw new ArgumentException("Title, creator override, and credit override must be single-line text.");
        }
    }
}

internal sealed class AssetDescriptionConflictException() : InvalidOperationException(
    "The selected Catalog data changed or an asset is no longer available. Reload values before applying again.");

internal interface IAssetDescriptionStore
{
    Task<IReadOnlyDictionary<Guid, AssetDescription>> GetAsync(IReadOnlyCollection<Guid> assetIds,
        CancellationToken cancellationToken = default);
    Task ApplyAsync(IReadOnlyDictionary<Guid, long> expectedRevisions, AssetDescriptionPatch patch,
        CancellationToken cancellationToken = default);
}

internal sealed class CatalogAssetDescriptionStore(Func<CatalogDatabaseSession?> session) : IAssetDescriptionStore
{
    public Task<IReadOnlyDictionary<Guid, AssetDescription>> GetAsync(IReadOnlyCollection<Guid> assetIds,
        CancellationToken cancellationToken = default)
    {
        var ids = assetIds.Distinct().ToArray();
        return Task.Run<IReadOnlyDictionary<Guid, AssetDescription>>(() =>
        {
            var result = new Dictionary<Guid, AssetDescription>();
            using var connection = RequireSession().OpenConnection();
            // A coherent snapshot includes all batches and revisions used for an optimistic bulk write.
            using var transaction = connection.BeginTransaction(deferred: true);
            foreach (var batch in ids.Chunk(400))
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                var names = batch.Select((id, i) =>
                {
                    var name = $"$a{i}";
                    command.Parameters.AddWithValue(name, id.ToString("D"));
                    return name;
                });
                command.CommandText = $"""
                    SELECT a.AssetId,d.Title,d.Description,d.Notes,d.CreatorOverride,d.CreditOverride,COALESCE(d.Revision,0)
                    FROM MediaAssets a LEFT JOIN MediaAssetDescriptions d ON a.AssetId=d.AssetId
                    WHERE a.AssetId IN ({string.Join(',', names)});
                    """;
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    var id = Guid.Parse(reader.GetString(0));
                    string? Value(int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
                    result.Add(id, new(id, Value(1), Value(2), Value(3), Value(4), Value(5), reader.GetInt64(6)));
                }
            }
            transaction.Commit();
            return result;
        }, cancellationToken);
    }

    public Task ApplyAsync(IReadOnlyDictionary<Guid, long> expectedRevisions, AssetDescriptionPatch patch,
        CancellationToken cancellationToken = default)
    {
        // Capture caller-owned collections before crossing the asynchronous boundary.
        var expected = expectedRevisions.ToArray();
        var values = new AssetDescriptionPatch(patch.Values.ToDictionary(p => p.Key, p => p.Value));
        values.Validate();
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (values.Values.Count == 0 || expected.Length == 0) return;
            using var connection = RequireSession().OpenConnection();
            using var transaction = connection.BeginTransaction();
            var now = DateTime.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
            foreach (var (id, revision) in expected)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.Parameters.AddWithValue("$asset", id.ToString("D"));
                command.Parameters.AddWithValue("$revision", revision);
                command.Parameters.AddWithValue("$now", now);
                command.CommandText = """
                    SELECT COALESCE(d.Revision,0) FROM MediaAssets a
                    LEFT JOIN MediaAssetDescriptions d ON a.AssetId=d.AssetId WHERE a.AssetId=$asset;
                    """;
                if (command.ExecuteScalar() is not long actual || actual != revision)
                    throw new AssetDescriptionConflictException();
                command.CommandText = """
                    INSERT INTO MediaAssetDescriptions(AssetId,Revision,CreatedUtc,UpdatedUtc)
                    VALUES($asset,1,$now,$now) ON CONFLICT(AssetId) DO NOTHING;
                    """;
                command.ExecuteNonQuery();
                var assignments = new List<string>();
                foreach (var (field, value) in values.Values)
                {
                    // Validated enum names are the fixed schema identifiers, never user-supplied SQL.
                    assignments.Add($"{field}=${field}");
                    command.Parameters.AddWithValue($"${field}", (object?)value ?? DBNull.Value);
                }
                command.CommandText = $"UPDATE MediaAssetDescriptions SET {string.Join(',', assignments)},Revision=$revision+1,UpdatedUtc=$now WHERE AssetId=$asset;";
                if (command.ExecuteNonQuery() != 1) throw new AssetDescriptionConflictException();
            }
            cancellationToken.ThrowIfCancellationRequested();
            transaction.Commit();
        }, cancellationToken);
    }

    private CatalogDatabaseSession RequireSession() => session() ?? throw new InvalidOperationException("The Catalog is unavailable.");
}
