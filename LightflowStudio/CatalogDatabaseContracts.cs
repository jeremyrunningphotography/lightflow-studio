namespace LightflowStudio;

internal enum CatalogOpenStatus
{
    Ready,
    Created,
    AlreadyExists,
    MissingExpectedCatalog,
    StorageUnavailable,
    Unreadable,
    Corrupt,
    NotLightflowCatalog,
    UnsupportedFutureSchema,
    MigrationBackupRequired,
    MigrationBackupFailed,
    MigrationFailed
}

internal sealed record CatalogRuntimePolicy(
    bool ForeignKeysEnabled,
    string JournalMode,
    int SynchronousLevel,
    int BusyTimeoutMilliseconds);

internal sealed record CatalogIdentity(
    Guid CatalogId,
    string ApplicationIdentity,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc);

internal sealed record CatalogOpenResult(
    CatalogOpenStatus Status,
    CatalogDatabaseSession? Session = null,
    string? Diagnostic = null,
    int? SchemaVersion = null)
{
    public bool IsSuccess => Status is CatalogOpenStatus.Ready or CatalogOpenStatus.Created;
}

/// <summary>
/// Provider-neutral seam for #83's required SQLite-aware pre-migration backup.
/// </summary>
internal interface ICatalogMigrationBackup
{
    Task<CatalogMigrationBackupResult> PrepareForMigrationAsync(
        string catalogDatabasePath,
        int currentSchemaVersion,
        int targetSchemaVersion,
        CancellationToken cancellationToken);
}

internal sealed record CatalogMigrationBackupResult(bool Succeeded, string? Diagnostic = null)
{
    public static CatalogMigrationBackupResult Success() => new(true);
    public static CatalogMigrationBackupResult Failure(string diagnostic) => new(false, diagnostic);
}

internal sealed class CatalogDatabaseSession : IAsyncDisposable
{
    private readonly CatalogSqliteConnectionFactory _connections;
    private int _disposed;

    internal CatalogDatabaseSession(
        string databasePath,
        int schemaVersion,
        CatalogIdentity identity,
        CatalogRuntimePolicy runtimePolicy,
        CatalogSqliteConnectionFactory connections)
    {
        DatabasePath = databasePath;
        SchemaVersion = schemaVersion;
        Identity = identity;
        RuntimePolicy = runtimePolicy;
        _connections = connections;
    }

    public string DatabasePath { get; }
    public int SchemaVersion { get; }
    public CatalogIdentity Identity { get; }
    public CatalogRuntimePolicy RuntimePolicy { get; }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
            _connections.ClearPool();
        return ValueTask.CompletedTask;
    }
}
