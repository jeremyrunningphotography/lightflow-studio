using System.Globalization;
using System.IO;
using Microsoft.Data.Sqlite;

namespace LightflowStudio;

internal sealed class CatalogDatabaseService
{
    internal const string ApplicationIdentity = "LightflowStudio.Catalog";
    internal const int SqliteApplicationId = 0x4C465343; // LFSC

    private readonly ILightflowStorageLocations _storageLocations;
    private readonly ICatalogMigrationBackup? _migrationBackup;
    private readonly IReadOnlyList<CatalogMigration> _migrations;

    public CatalogDatabaseService(
        ILightflowStorageLocations storageLocations,
        ICatalogMigrationBackup? migrationBackup = null)
        : this(storageLocations, migrationBackup, CatalogMigrations.All)
    {
    }

    internal CatalogDatabaseService(
        ILightflowStorageLocations storageLocations,
        ICatalogMigrationBackup? migrationBackup,
        IReadOnlyList<CatalogMigration> migrations)
    {
        _storageLocations = storageLocations ?? throw new ArgumentNullException(nameof(storageLocations));
        _migrationBackup = migrationBackup;
        _migrations = ValidateMigrations(migrations);
    }

    public int CurrentSchemaVersion => _migrations.Count == 0 ? 0 : _migrations[^1].Version;

    public Task<CatalogOpenResult> CreateNewAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => CreateNew(cancellationToken), cancellationToken);

    public Task<CatalogOpenResult> OpenExistingAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => OpenExisting(cancellationToken), cancellationToken);

    private CatalogOpenResult CreateNew(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var databasePath = Path.GetFullPath(_storageLocations.CatalogDatabasePath);
        var catalogDirectory = Path.GetDirectoryName(databasePath)!;

        try
        {
            Directory.CreateDirectory(catalogDirectory);
            using (new FileStream(databasePath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            {
                // Atomic ownership claim. SQLite initializes the deliberately empty file below.
            }
        }
        catch (IOException exception) when (File.Exists(databasePath))
        {
            return Failure(CatalogOpenStatus.AlreadyExists, exception, databasePath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Failure(CatalogOpenStatus.StorageUnavailable, exception, databasePath);
        }

        var connections = new CatalogSqliteConnectionFactory(databasePath);
        try
        {
            RunMigrations(connections, 0, isNewCatalog: true, cancellationToken);
            return BuildSuccessResult(connections, CatalogOpenStatus.Created);
        }
        catch (CatalogOpenException exception)
        {
            connections.ClearPool();
            DeleteFailedNewCatalog(databasePath);
            return new(exception.Status, Diagnostic: exception.Message, SchemaVersion: exception.SchemaVersion);
        }
        catch (Exception exception) when (IsCatalogAccessException(exception))
        {
            connections.ClearPool();
            DeleteFailedNewCatalog(databasePath);
            return Failure(ClassifySqliteFailure(exception), exception, databasePath);
        }
    }

    private CatalogOpenResult OpenExisting(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var databasePath = Path.GetFullPath(_storageLocations.CatalogDatabasePath);
        var catalogDirectory = Path.GetDirectoryName(databasePath)!;

        if (!Directory.Exists(catalogDirectory))
            return new(CatalogOpenStatus.StorageUnavailable,
                Diagnostic: $"The configured Catalog directory is unavailable: {catalogDirectory}");
        try
        {
            if ((File.GetAttributes(databasePath) & FileAttributes.Directory) != 0)
                return new(CatalogOpenStatus.Unreadable,
                    Diagnostic: $"The configured Catalog database path is a directory: {databasePath}");
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return new(CatalogOpenStatus.MissingExpectedCatalog,
                Diagnostic: $"The expected Catalog database is missing: {databasePath}");
        }
        catch (UnauthorizedAccessException exception)
        {
            return Failure(CatalogOpenStatus.Unreadable, exception, databasePath);
        }
        catch (IOException exception)
        {
            return Failure(CatalogOpenStatus.StorageUnavailable, exception, databasePath);
        }

        try
        {
            var inspectedVersion = InspectExistingCatalog(databasePath);
            if (inspectedVersion > CurrentSchemaVersion)
            {
                return new(CatalogOpenStatus.UnsupportedFutureSchema,
                    Diagnostic: $"Catalog schema {inspectedVersion} requires a newer Lightflow version.",
                    SchemaVersion: inspectedVersion);
            }

            var connections = new CatalogSqliteConnectionFactory(databasePath);
            try
            {
                if (inspectedVersion < CurrentSchemaVersion)
                    RunMigrations(connections, inspectedVersion, isNewCatalog: false, cancellationToken);
                return BuildSuccessResult(connections, CatalogOpenStatus.Ready);
            }
            catch
            {
                connections.ClearPool();
                throw;
            }
        }
        catch (CatalogOpenException exception)
        {
            return new(exception.Status, Diagnostic: exception.Message, SchemaVersion: exception.SchemaVersion);
        }
        catch (Exception exception) when (IsCatalogAccessException(exception))
        {
            return Failure(ClassifySqliteFailure(exception), exception, databasePath);
        }
    }

    private int InspectExistingCatalog(string databasePath)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        }.ToString();
        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        EnsureIntegrity(connection, quick: true);

        var applicationId = Convert.ToInt32(ExecuteScalar(connection, "PRAGMA application_id;"));
        if (applicationId != SqliteApplicationId)
            throw new CatalogOpenException(CatalogOpenStatus.NotLightflowCatalog,
                "The database does not contain the expected Lightflow Catalog identity.");
        var schemaVersion = ReadSchemaVersion(connection);
        if (schemaVersion is > 0 && schemaVersion <= CurrentSchemaVersion)
        {
            try
            {
                ValidateMigrationHistory(connection, schemaVersion);
                ReadCatalogIdentity(connection);
            }
            catch (CatalogOpenException)
            {
                throw;
            }
            catch (SqliteException exception)
            {
                throw new CatalogOpenException(CatalogOpenStatus.NotLightflowCatalog,
                    "The database has a conflicting or incomplete Lightflow Catalog identity.",
                    schemaVersion, exception);
            }
        }
        return schemaVersion;
    }

    private void RunMigrations(
        CatalogSqliteConnectionFactory connections,
        int startingVersion,
        bool isNewCatalog,
        CancellationToken cancellationToken)
    {
        if (startingVersion > CurrentSchemaVersion)
            throw new CatalogOpenException(CatalogOpenStatus.UnsupportedFutureSchema,
                $"Catalog schema {startingVersion} requires a newer Lightflow version.", startingVersion);
        if (startingVersion == CurrentSchemaVersion) return;

        if (!isNewCatalog)
        {
            using (var connection = connections.OpenConnection())
                EnsureIntegrity(connection, quick: false);
            connections.ClearPool();

            if (_migrationBackup is null)
            {
                throw new CatalogOpenException(CatalogOpenStatus.MigrationBackupRequired,
                    "A SQLite-aware pre-migration backup service is required before upgrading this Catalog.",
                    startingVersion);
            }

            var backup = _migrationBackup.PrepareForMigrationAsync(
                    connections.DatabasePath, startingVersion, CurrentSchemaVersion, cancellationToken)
                .GetAwaiter().GetResult();
            if (!backup.Succeeded)
            {
                throw new CatalogOpenException(CatalogOpenStatus.MigrationBackupFailed,
                    backup.Diagnostic ?? "The required pre-migration Catalog backup failed.", startingVersion);
            }
        }

        var catalogId = startingVersion == 0 ? Guid.NewGuid() : ReadCatalogIdentity(connections).CatalogId;
        foreach (var migration in _migrations.Where(migration => migration.Version > startingVersion))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var connection = connections.OpenConnection();
                using var transaction = connection.BeginTransaction();
                var appliedUtc = FormatUtc(DateTime.UtcNow);
                migration.Apply(connection, transaction, new(catalogId, appliedUtc));

                using (var history = connection.CreateCommand())
                {
                    history.Transaction = transaction;
                    history.CommandText = """
                        INSERT INTO SchemaMigrations (Version, Name, AppliedUtc)
                        VALUES ($version, $name, $appliedUtc);
                        """;
                    history.Parameters.AddWithValue("$version", migration.Version);
                    history.Parameters.AddWithValue("$name", migration.Name);
                    history.Parameters.AddWithValue("$appliedUtc", appliedUtc);
                    history.ExecuteNonQuery();
                }

                using (var version = connection.CreateCommand())
                {
                    version.Transaction = transaction;
                    version.CommandText = $"PRAGMA user_version = {migration.Version};";
                    version.ExecuteNonQuery();
                }

                transaction.Commit();
                startingVersion = migration.Version;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                throw new CatalogOpenException(CatalogOpenStatus.MigrationFailed,
                    $"Catalog migration {migration.Version} ({migration.Name}) failed: {exception.Message}",
                    startingVersion, exception);
            }
        }
    }

    private CatalogOpenResult BuildSuccessResult(
        CatalogSqliteConnectionFactory connections,
        CatalogOpenStatus successStatus)
    {
        using var connection = connections.OpenConnection();
        var policy = CatalogSqliteConnectionFactory.ApplyRuntimePolicy(connection);
        EnsureIntegrity(connection, quick: true);
        var version = ReadSchemaVersion(connection);
        if (version != CurrentSchemaVersion)
            throw new CatalogOpenException(CatalogOpenStatus.MigrationFailed,
                $"Catalog schema {version} did not reach expected schema {CurrentSchemaVersion}.", version);
        ValidateMigrationHistory(connection, version);
        var identity = ReadCatalogIdentity(connection);
        return new(successStatus,
            new CatalogDatabaseSession(connections.DatabasePath, version, identity, policy, connections),
            SchemaVersion: version);
    }

    private static CatalogIdentity ReadCatalogIdentity(CatalogSqliteConnectionFactory connections)
    {
        using var connection = connections.OpenConnection();
        return ReadCatalogIdentity(connection);
    }

    private static CatalogIdentity ReadCatalogIdentity(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT ApplicationIdentity, CatalogId, CreatedUtc, UpdatedUtc
            FROM CatalogInfo
            WHERE SingletonId = 1;
            """;
        using var reader = command.ExecuteReader();
        if (!reader.Read() || reader.GetString(0) != ApplicationIdentity ||
            !Guid.TryParseExact(reader.GetString(1), "D", out var catalogId) ||
            !DateTimeOffset.TryParse(reader.GetString(2), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out var createdUtc) ||
            !DateTimeOffset.TryParse(reader.GetString(3), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out var updatedUtc) || reader.Read())
        {
            throw new CatalogOpenException(CatalogOpenStatus.NotLightflowCatalog,
                "The database is missing valid Lightflow Catalog identity metadata.");
        }

        return new(catalogId, ApplicationIdentity, createdUtc, updatedUtc);
    }

    private static void ValidateMigrationHistory(SqliteConnection connection, int schemaVersion)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Version FROM SchemaMigrations ORDER BY Version;";
        using var reader = command.ExecuteReader();
        var expected = 1;
        while (reader.Read())
        {
            if (reader.GetInt32(0) != expected++)
                throw new CatalogOpenException(CatalogOpenStatus.NotLightflowCatalog,
                    "The Catalog migration history is incomplete or unordered.");
        }
        if (expected - 1 != schemaVersion)
            throw new CatalogOpenException(CatalogOpenStatus.NotLightflowCatalog,
                "The Catalog migration history does not match its schema version.");
    }

    private static void EnsureIntegrity(SqliteConnection connection, bool quick)
    {
        var result = Convert.ToString(ExecuteScalar(connection,
            quick ? "PRAGMA quick_check;" : "PRAGMA integrity_check;"));
        if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
            throw new CatalogOpenException(CatalogOpenStatus.Corrupt,
                $"Catalog {(quick ? "quick" : "full integrity")} check failed: {result}");
    }

    private static int ReadSchemaVersion(SqliteConnection connection) =>
        Convert.ToInt32(ExecuteScalar(connection, "PRAGMA user_version;"));

    private static object? ExecuteScalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    private static IReadOnlyList<CatalogMigration> ValidateMigrations(IReadOnlyList<CatalogMigration> migrations)
    {
        ArgumentNullException.ThrowIfNull(migrations);
        var ordered = migrations.OrderBy(migration => migration.Version).ToArray();
        for (var index = 0; index < ordered.Length; index++)
        {
            if (ordered[index].Version != index + 1)
                throw new ArgumentException("Catalog migrations must be contiguous and start at version 1.", nameof(migrations));
            if (string.IsNullOrWhiteSpace(ordered[index].Name))
                throw new ArgumentException("Catalog migrations must have a diagnostic name.", nameof(migrations));
        }
        return ordered;
    }

    private static string FormatUtc(DateTime utc) => utc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static bool IsCatalogAccessException(Exception exception) =>
        exception is SqliteException or IOException or UnauthorizedAccessException or InvalidOperationException;

    internal static CatalogOpenStatus ClassifySqliteFailure(Exception exception)
    {
        if (exception is SqliteException sqlite)
        {
            if (sqlite.SqliteErrorCode == 11) return CatalogOpenStatus.Corrupt;
            if (sqlite.SqliteErrorCode == 26) return CatalogOpenStatus.Unreadable;
            if (sqlite.SqliteErrorCode is 8 or 3) return CatalogOpenStatus.Unreadable;
            if (sqlite.SqliteErrorCode is 10 or 14 or 5 or 6)
                return CatalogOpenStatus.StorageUnavailable;
        }
        if (exception is IOException) return CatalogOpenStatus.StorageUnavailable;
        return CatalogOpenStatus.Unreadable;
    }

    private static CatalogOpenResult Failure(CatalogOpenStatus status, Exception exception, string databasePath) =>
        new(status, Diagnostic: $"Catalog at '{databasePath}' could not be used: {exception.Message}");

    private static void DeleteFailedNewCatalog(string databasePath)
    {
        foreach (var path in new[] { databasePath, databasePath + "-wal", databasePath + "-shm" })
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private sealed class CatalogOpenException : Exception
    {
        public CatalogOpenException(
            CatalogOpenStatus status,
            string message,
            int? schemaVersion = null,
            Exception? innerException = null)
            : base(message, innerException)
        {
            Status = status;
            SchemaVersion = schemaVersion;
        }

        public CatalogOpenStatus Status { get; }
        public int? SchemaVersion { get; }
    }
}
