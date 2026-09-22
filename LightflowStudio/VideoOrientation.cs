namespace LightflowStudio;

/// <summary>Clockwise adjustment AFTER the source's display transform. Zero preserves source orientation.</summary>
internal readonly record struct VideoRotation
{
    public int Degrees { get; }
    [System.Text.Json.Serialization.JsonConstructor]
    public VideoRotation(int degrees)
    {
        if (degrees % 90 != 0) throw new ArgumentOutOfRangeException(nameof(degrees));
        Degrees = ((degrees % 360) + 360) % 360;
    }
    public VideoRotation Turn(bool right) => new(Degrees + (right ? 90 : -90));
    public VideoRotation Compose(VideoRotation adjustment) => new(Degrees + adjustment.Degrees);
    public (int Width, int Height) Dimensions(int width, int height) =>
        Degrees is 90 or 270 ? (height, width) : (width, height);
}

internal sealed record AssetVideoRotation(Guid AssetId, VideoRotation Rotation, long Revision = 0);

internal interface IAssetVideoRotationStore
{
    event EventHandler<IReadOnlyList<AssetVideoRotation>>? Changed;
    event EventHandler? Invalidated { add { } remove { } }
    Task<IReadOnlyDictionary<Guid, AssetVideoRotation>> GetAsync(IReadOnlyCollection<Guid> assetIds,
        CancellationToken cancellationToken = default);
    Task RotateAsync(IReadOnlyDictionary<Guid, long> expectedRevisions, bool right,
        CancellationToken cancellationToken = default);
}

internal sealed class CatalogAssetVideoRotationStore(Func<CatalogDatabaseSession?> session) : IAssetVideoRotationStore
{
    public event EventHandler<IReadOnlyList<AssetVideoRotation>>? Changed;
    public event EventHandler? Invalidated;
    internal void NotifyCatalogRestored() => Invalidated?.Invoke(this, EventArgs.Empty);

    public Task<IReadOnlyDictionary<Guid, AssetVideoRotation>> GetAsync(IReadOnlyCollection<Guid> assetIds,
        CancellationToken cancellationToken = default)
    {
        var ids = assetIds.Distinct().ToArray();
        return Task.Run<IReadOnlyDictionary<Guid, AssetVideoRotation>>(() =>
        {
            var result = new Dictionary<Guid, AssetVideoRotation>();
            using var connection = RequireSession().OpenConnection();
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
                }).ToArray();
                command.CommandText = $"""
                    SELECT a.AssetId,COALESCE(r.Degrees,0),COALESCE(r.Revision,0)
                    FROM MediaAssets a LEFT JOIN MediaAssetVideoRotation r ON a.AssetId=r.AssetId
                    WHERE lower(a.MediaType)='video' AND a.AssetId IN ({string.Join(',', names)});
                    """;
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    var id = Guid.Parse(reader.GetString(0));
                    result.Add(id, new(id, new(reader.GetInt32(1)), reader.GetInt64(2)));
                }
            }
            transaction.Commit();
            return result;
        }, cancellationToken);
    }

    public async Task RotateAsync(IReadOnlyDictionary<Guid, long> expectedRevisions, bool right,
        CancellationToken cancellationToken = default)
    {
        await RequireSession().Mutations.RunAsync(async () => {
        var expected = expectedRevisions.ToArray();
        var changed = await Task.Run(() =>
        {
            var result = new List<AssetVideoRotation>();
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
                    SELECT COALESCE(r.Degrees,0),COALESCE(r.Revision,0) FROM MediaAssets a
                    LEFT JOIN MediaAssetVideoRotation r ON a.AssetId=r.AssetId
                    WHERE a.AssetId=$asset AND lower(a.MediaType)='video';
                    """;
                VideoRotation rotation;
                using (var reader = command.ExecuteReader())
                {
                    if (!reader.Read() || reader.GetInt64(1) != revision)
                        throw new InvalidOperationException("The selected video rotation changed. Refresh the selection and try again.");
                    rotation = new VideoRotation(reader.GetInt32(0)).Turn(right);
                }
                command.Parameters.AddWithValue("$degrees", rotation.Degrees);
                command.CommandText = """
                    INSERT INTO MediaAssetVideoRotation(AssetId,Degrees,Revision,CreatedUtc,UpdatedUtc)
                    VALUES($asset,$degrees,$revision+1,$now,$now)
                    ON CONFLICT(AssetId) DO UPDATE SET Degrees=$degrees,Revision=$revision+1,UpdatedUtc=$now;
                    """;
                command.ExecuteNonQuery();
                result.Add(new(id, rotation, revision + 1));
            }
            cancellationToken.ThrowIfCancellationRequested();
            transaction.Commit();
            return result;
        }, cancellationToken).ConfigureAwait(false);
        Changed?.Invoke(this, changed);
    }, cancellationToken);
    }

    private CatalogDatabaseSession RequireSession() => session() ?? throw new InvalidOperationException("The Catalog is unavailable.");
}
