using System.IO;
using Microsoft.Data.Sqlite;

namespace LightflowStudio;

internal sealed class CatalogSqliteConnectionFactory
{
    internal const int BusyTimeoutMilliseconds = 5_000;
    internal const int FullSynchronousLevel = 2;

    private readonly string _connectionString;

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
