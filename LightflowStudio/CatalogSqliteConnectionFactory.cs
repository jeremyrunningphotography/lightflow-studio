using System.IO;
using Microsoft.Data.Sqlite;

namespace LightflowStudio;

internal sealed class CatalogSqliteConnectionFactory
{
    internal const int BusyTimeoutMilliseconds = 5_000;
    internal const int FullSynchronousLevel = 2;

    private readonly string _connectionString;
    private int _checkedOut;
    private int _published;

    public CatalogSqliteConnectionFactory(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        DatabasePath = Path.GetFullPath(databasePath);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWrite,
            Cache = SqliteCacheMode.Default,
            Pooling = true,
            ForeignKeys = true,
            DefaultTimeout = BusyTimeoutMilliseconds / 1_000
        }.ToString();
    }

    public string DatabasePath { get; }

    public SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        try
        {
            connection.Open();
            Interlocked.Increment(ref _checkedOut);
            var counted = 1;
            connection.StateChange += (_, args) =>
            {
                if (args.CurrentState == System.Data.ConnectionState.Closed && Interlocked.Exchange(ref counted, 0) != 0)
                    Interlocked.Decrement(ref _checkedOut);
                else if (args.CurrentState == System.Data.ConnectionState.Open && Interlocked.Exchange(ref counted, 1) == 0)
                    Interlocked.Increment(ref _checkedOut);
            };
            ApplyRuntimePolicy(connection);
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    internal static CatalogRuntimePolicy ApplyRuntimePolicy(SqliteConnection connection)
    {
        ExecuteNonQuery(connection, "PRAGMA foreign_keys = ON;");
        ExecuteNonQuery(connection, $"PRAGMA busy_timeout = {BusyTimeoutMilliseconds};");

        var journalMode = Convert.ToString(ExecuteScalar(connection, "PRAGMA journal_mode = WAL;"))
            ?.ToLowerInvariant() ?? string.Empty;
        ExecuteNonQuery(connection, "PRAGMA synchronous = FULL;");

        var policy = new CatalogRuntimePolicy(
            Convert.ToInt32(ExecuteScalar(connection, "PRAGMA foreign_keys;")) == 1,
            journalMode,
            Convert.ToInt32(ExecuteScalar(connection, "PRAGMA synchronous;")),
            Convert.ToInt32(ExecuteScalar(connection, "PRAGMA busy_timeout;")));

        if (!policy.ForeignKeysEnabled || policy.JournalMode != "wal" ||
            policy.SynchronousLevel != FullSynchronousLevel ||
            policy.BusyTimeoutMilliseconds != BusyTimeoutMilliseconds)
        {
            throw new InvalidOperationException("The Catalog SQLite runtime policy could not be applied and verified.");
        }

        return policy;
    }

    public void ClearPool()
    {
        using var connection = new SqliteConnection(_connectionString);
        SqliteConnection.ClearPool(connection);
    }

    internal void Publish() => Interlocked.Exchange(ref _published, 1);

    // Only the unpublished service/activation owner may establish this boundary. ClearPool
    // cannot close checked-out SQL scopes; their using scopes must have returned first.
    internal void CloseUnpublishedPool(CatalogStorageBoundary boundary)
    {
        if (Volatile.Read(ref _published) != 0 || Volatile.Read(ref _checkedOut) != 0)
            throw new CatalogLocationAdmissionException("A closed Catalog boundary requires an unpublished factory with no checked-out SQL scopes.",
                Lightflow.Application.StorageLocationReason.AssessmentFailed);
        var started = System.Diagnostics.Stopwatch.StartNew();
        try { ClearPool(); }
        catch (Exception error)
        {
            throw new CatalogLocationAdmissionException($"The unpublished Catalog pool could not be closed: {error.GetType().Name}.",
                Lightflow.Application.StorageLocationReason.AssessmentFailed);
        }
        StartupDiagnostics.Note($"Catalog closed boundary: phase={boundary}; factory=[{WindowsStorageLocationAssessor.TargetContext(DatabasePath)}]; checkedOut=0; poolClear=completed; elapsedMs={started.Elapsed.TotalMilliseconds:F3}");
    }

    private static void ExecuteNonQuery(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static object? ExecuteScalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }
}
