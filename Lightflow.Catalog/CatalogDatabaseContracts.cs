namespace LightflowStudio;

public enum CatalogOpenStatus
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

public sealed record CatalogRuntimePolicy(
    bool ForeignKeysEnabled,
    string JournalMode,
    int SynchronousLevel,
    int BusyTimeoutMilliseconds);

public sealed record CatalogOpenResult(
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
public interface ICatalogMigrationBackup
{
    Task<CatalogMigrationBackupResult> PrepareForMigrationAsync(
        string catalogDatabasePath,
        int currentSchemaVersion,
        int targetSchemaVersion,
        CancellationToken cancellationToken);
}

public sealed record CatalogMigrationBackupResult(bool Succeeded, string? Diagnostic = null)
{
    public static CatalogMigrationBackupResult Success() => new(true);
    public static CatalogMigrationBackupResult Failure(string diagnostic) => new(false, diagnostic);
}

public sealed class CatalogDatabaseSession : IAsyncDisposable
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
    internal string ResolvedDatabasePath => _connections.DatabasePath;
    public int SchemaVersion { get; }
    public CatalogIdentity Identity { get; }
    public CatalogRuntimePolicy RuntimePolicy { get; }
    internal CatalogMutationLifecycle Mutations { get; set; } = new();
    internal Action<string>? RequireOwnership { get; set; }
    internal Action<string>? ReadinessFailed { get; set; }

    internal Microsoft.Data.Sqlite.SqliteConnection OpenConnection()
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        RequireOwnership?.Invoke(ResolvedDatabasePath);
        _connections.Publish();
        try { return _connections.OpenConnection(); }
        catch (Microsoft.Data.Sqlite.SqliteException error) when (error.SqliteErrorCode is 10 or 11 or 14 or 26)
        { ReadinessFailed?.Invoke(error.Message); throw; }
    }

    internal void CloseBeforeActivation() => _connections.CloseUnpublishedPool(CatalogStorageBoundary.AfterCatalogValidation_SQLiteClosed);
    internal void MarkPublished() => _connections.Publish();

    public async ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref _disposed, 1);
        await _connections.CloseAndDrainAsync().ConfigureAwait(false);
    }
}
