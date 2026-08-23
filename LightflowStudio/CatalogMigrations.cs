using Microsoft.Data.Sqlite;

namespace LightflowStudio;

internal sealed record CatalogMigration(
    int Version,
    string Name,
    Action<SqliteConnection, SqliteTransaction, CatalogMigrationContext> Apply);

internal sealed record CatalogMigrationContext(Guid CatalogId, string AppliedUtc);

internal static class CatalogMigrations
{
    public static IReadOnlyList<CatalogMigration> All { get; } =
    [
        new(1, "Initial catalog identity, roots, mappings, and assets", ApplyVersion1),
        new(2, "Browser recursive-scope roots", ApplyVersion2),
        new(3, "Durable asset media ranges", ApplyVersion3),
        new(4, "Managed LUT resources and durable asset Color intent", ApplyVersion4),
        new(5, "Folder-backed LUT resource identity", ApplyVersion5)
    ];

    private static void ApplyVersion1(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CatalogMigrationContext context)
    {
        Execute(connection, transaction,
            $"PRAGMA application_id = {CatalogDatabaseService.SqliteApplicationId};");
        Execute(connection, transaction, """
            CREATE TABLE CatalogInfo (
                SingletonId INTEGER NOT NULL PRIMARY KEY CHECK (SingletonId = 1),
                ApplicationIdentity TEXT NOT NULL CHECK (length(ApplicationIdentity) > 0),
                CatalogId TEXT NOT NULL UNIQUE CHECK (length(CatalogId) = 36),
                CreatedUtc TEXT NOT NULL CHECK (length(CreatedUtc) = 28),
                UpdatedUtc TEXT NOT NULL CHECK (length(UpdatedUtc) = 28)
            );

            CREATE TABLE SchemaMigrations (
                Version INTEGER NOT NULL PRIMARY KEY CHECK (Version > 0),
                Name TEXT NOT NULL UNIQUE CHECK (length(Name) > 0),
                AppliedUtc TEXT NOT NULL CHECK (length(AppliedUtc) = 28)
            );

            CREATE TABLE MediaRoots (
                RootId TEXT NOT NULL PRIMARY KEY CHECK (length(RootId) = 36),
                DisplayName TEXT NOT NULL CHECK (length(trim(DisplayName)) > 0),
                SourceStatus TEXT NOT NULL DEFAULT 'unknown'
                    CHECK (SourceStatus IN ('unknown', 'online', 'offline', 'unavailable')),
                CreatedUtc TEXT NOT NULL CHECK (length(CreatedUtc) = 28),
                UpdatedUtc TEXT NOT NULL CHECK (length(UpdatedUtc) = 28)
            );

            CREATE TABLE MediaRootMappings (
                MappingId TEXT NOT NULL PRIMARY KEY CHECK (length(MappingId) = 36),
                RootId TEXT NOT NULL,
                MachineId TEXT NOT NULL CHECK (length(trim(MachineId)) > 0),
                PhysicalPath TEXT NOT NULL CHECK (length(trim(PhysicalPath)) > 0),
                SourceStatus TEXT NOT NULL DEFAULT 'unknown'
                    CHECK (SourceStatus IN ('unknown', 'online', 'offline', 'unavailable')),
                CreatedUtc TEXT NOT NULL CHECK (length(CreatedUtc) = 28),
                UpdatedUtc TEXT NOT NULL CHECK (length(UpdatedUtc) = 28),
                FOREIGN KEY (RootId) REFERENCES MediaRoots (RootId) ON DELETE RESTRICT,
                UNIQUE (RootId, MachineId)
            );

            CREATE INDEX IX_MediaRootMappings_MachineId
                ON MediaRootMappings (MachineId);

            CREATE TABLE MediaAssets (
                AssetId TEXT NOT NULL PRIMARY KEY CHECK (length(AssetId) = 36),
                RootId TEXT NOT NULL,
                RelativePath TEXT NOT NULL
                    CHECK (length(RelativePath) > 0 AND substr(RelativePath, 1, 1) <> '/'
                        AND instr(RelativePath, '\') = 0),
                RelativePathKey TEXT NOT NULL CHECK (length(RelativePathKey) > 0),
                MediaType TEXT NOT NULL DEFAULT 'unknown' CHECK (length(MediaType) > 0),
                FileSizeBytes INTEGER NOT NULL CHECK (FileSizeBytes >= 0),
                LastWriteUtcTicks INTEGER NOT NULL CHECK (LastWriteUtcTicks >= 0),
                FingerprintVersion INTEGER NULL CHECK (FingerprintVersion IS NULL OR FingerprintVersion > 0),
                SourceFingerprint TEXT NULL CHECK (SourceFingerprint IS NULL OR length(SourceFingerprint) > 0),
                SourceStatus TEXT NOT NULL DEFAULT 'unknown'
                    CHECK (SourceStatus IN ('unknown', 'available', 'missing')),
                CreatedUtc TEXT NOT NULL CHECK (length(CreatedUtc) = 28),
                UpdatedUtc TEXT NOT NULL CHECK (length(UpdatedUtc) = 28),
                LastSeenUtc TEXT NULL CHECK (LastSeenUtc IS NULL OR length(LastSeenUtc) = 28),
                FOREIGN KEY (RootId) REFERENCES MediaRoots (RootId) ON DELETE RESTRICT,
                UNIQUE (RootId, RelativePathKey),
                CHECK ((FingerprintVersion IS NULL) = (SourceFingerprint IS NULL))
            );

            CREATE INDEX IX_MediaAssets_RootId_SourceStatus
                ON MediaAssets (RootId, SourceStatus);
            CREATE INDEX IX_MediaAssets_SourceFingerprint
                ON MediaAssets (FingerprintVersion, SourceFingerprint)
                WHERE SourceFingerprint IS NOT NULL;
            """);

        using var identity = connection.CreateCommand();
        identity.Transaction = transaction;
        identity.CommandText = """
            INSERT INTO CatalogInfo
                (SingletonId, ApplicationIdentity, CatalogId, CreatedUtc, UpdatedUtc)
            VALUES
                (1, $applicationIdentity, $catalogId, $createdUtc, $createdUtc);
            """;
        identity.Parameters.AddWithValue("$applicationIdentity", CatalogDatabaseService.ApplicationIdentity);
        identity.Parameters.AddWithValue("$catalogId", context.CatalogId.ToString("D"));
        identity.Parameters.AddWithValue("$createdUtc", context.AppliedUtc);
        identity.ExecuteNonQuery();
    }

    /// <summary>
    /// #124 (revised): durable, user-authored Browser recursive-scope configuration — "Include Subfolders"
    /// established on a folder. Deliberately Catalog data, not workspace/session state: it must survive
    /// application restarts, navigating elsewhere, and Media Root physical-path remapping, independent of
    /// whatever Browser folder happens to be open when Lightflow closes. Identity is root-agnostic
    /// (<c>RootId</c> + normalized relative folder), matching every other logical-identity table in this
    /// schema; no absolute physical path is ever persisted here. <c>RelativeFolder</c> is empty for a
    /// recursive root established at a Media Root's own top level. <c>RelativeFolderKey</c> is the
    /// case-insensitive comparison key (mirrors <c>MediaAssets.RelativePathKey</c>); the unique constraint on
    /// it is what makes establishing an already-recursive folder idempotent at the database level, on top of
    /// the service-layer normalization that keeps the table free of redundant nested roots.
    /// </summary>
    private static void ApplyVersion2(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CatalogMigrationContext context)
    {
        Execute(connection, transaction, """
            CREATE TABLE BrowserRecursiveScopes (
                ScopeId TEXT NOT NULL PRIMARY KEY CHECK (length(ScopeId) = 36),
                RootId TEXT NOT NULL,
                RelativeFolder TEXT NOT NULL CHECK (instr(RelativeFolder, '\') = 0),
                RelativeFolderKey TEXT NOT NULL,
                CreatedUtc TEXT NOT NULL CHECK (length(CreatedUtc) = 28),
                UpdatedUtc TEXT NOT NULL CHECK (length(UpdatedUtc) = 28),
                FOREIGN KEY (RootId) REFERENCES MediaRoots (RootId) ON DELETE CASCADE,
                UNIQUE (RootId, RelativeFolderKey)
            );

            CREATE INDEX IX_BrowserRecursiveScopes_RootId
                ON BrowserRecursiveScopes (RootId);
            """);
    }

    private static void ApplyVersion3(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CatalogMigrationContext context)
    {
        Execute(connection, transaction, """
            CREATE TABLE MediaAssetRanges (
                RangeId TEXT NOT NULL PRIMARY KEY CHECK (length(RangeId) = 36),
                AssetId TEXT NOT NULL,
                Kind TEXT NOT NULL DEFAULT 'primary' CHECK (length(trim(Kind)) > 0),
                Ordinal INTEGER NOT NULL DEFAULT 0 CHECK (Ordinal >= 0),
                InTicks INTEGER NULL CHECK (InTicks IS NULL OR InTicks >= 0),
                OutTicks INTEGER NULL CHECK (OutTicks IS NULL OR OutTicks >= 0),
                SourceDurationTicks INTEGER NOT NULL CHECK (SourceDurationTicks > 0),
                CreatedUtc TEXT NOT NULL CHECK (length(CreatedUtc) = 28),
                UpdatedUtc TEXT NOT NULL CHECK (length(UpdatedUtc) = 28),
                FOREIGN KEY (AssetId) REFERENCES MediaAssets (AssetId) ON DELETE CASCADE,
                UNIQUE (AssetId, Kind, Ordinal),
                CHECK (InTicks IS NOT NULL OR OutTicks IS NOT NULL),
                CHECK (InTicks IS NULL OR OutTicks IS NULL OR InTicks < OutTicks),
                CHECK (InTicks IS NULL OR InTicks < SourceDurationTicks),
                CHECK (OutTicks IS NULL OR OutTicks <= SourceDurationTicks)
            );

            CREATE INDEX IX_MediaAssetRanges_AssetId
                ON MediaAssetRanges (AssetId);
            """);
    }

    private static void ApplyVersion4(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CatalogMigrationContext context)
    {
        Execute(connection, transaction, """
            CREATE TABLE LutResources (
                LutId TEXT NOT NULL PRIMARY KEY CHECK (length(LutId) = 36),
                DisplayName TEXT NOT NULL CHECK (length(trim(DisplayName)) > 0),
                OriginalFileName TEXT NOT NULL CHECK (length(trim(OriginalFileName)) > 0),
                ContentSha256 TEXT NOT NULL UNIQUE CHECK (length(ContentSha256) = 64),
                CubeContent BLOB NOT NULL CHECK (length(CubeContent) > 0),
                LutKind TEXT NOT NULL CHECK (LutKind IN ('1d', '3d')),
                LutSize INTEGER NOT NULL CHECK (LutSize >= 2),
                CreatedUtc TEXT NOT NULL CHECK (length(CreatedUtc) = 28),
                UpdatedUtc TEXT NOT NULL CHECK (length(UpdatedUtc) = 28)
            );

            CREATE INDEX IX_LutResources_DisplayName ON LutResources (DisplayName COLLATE NOCASE);

            CREATE TABLE MediaAssetColor (
                AssetId TEXT NOT NULL PRIMARY KEY CHECK (length(AssetId) = 36),
                CameraLutId TEXT NULL CHECK (CameraLutId IS NULL OR length(CameraLutId) = 36),
                CreativeLutId TEXT NULL CHECK (CreativeLutId IS NULL OR length(CreativeLutId) = 36),
                CreatedUtc TEXT NOT NULL CHECK (length(CreatedUtc) = 28),
                UpdatedUtc TEXT NOT NULL CHECK (length(UpdatedUtc) = 28),
                FOREIGN KEY (AssetId) REFERENCES MediaAssets (AssetId) ON DELETE CASCADE,
                FOREIGN KEY (CameraLutId) REFERENCES LutResources (LutId) ON DELETE RESTRICT,
                FOREIGN KEY (CreativeLutId) REFERENCES LutResources (LutId) ON DELETE RESTRICT,
                CHECK (CameraLutId IS NOT NULL OR CreativeLutId IS NOT NULL)
            );

            CREATE INDEX IX_MediaAssetColor_CameraLutId ON MediaAssetColor (CameraLutId);
            CREATE INDEX IX_MediaAssetColor_CreativeLutId ON MediaAssetColor (CreativeLutId);
            """);
    }

    private static void ApplyVersion5(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CatalogMigrationContext context)
    {
        Execute(connection, transaction, """
            DROP INDEX IX_MediaAssetColor_CameraLutId;
            DROP INDEX IX_MediaAssetColor_CreativeLutId;
            DROP INDEX IX_LutResources_DisplayName;

            ALTER TABLE MediaAssetColor RENAME TO MediaAssetColorV4;
            ALTER TABLE LutResources RENAME TO LutResourcesV4;

            CREATE TABLE LutResources (
                LutId TEXT NOT NULL PRIMARY KEY CHECK (length(LutId) = 36),
                DisplayName TEXT NOT NULL CHECK (length(trim(DisplayName)) > 0),
                OriginalFileName TEXT NOT NULL CHECK (length(trim(OriginalFileName)) > 0),
                ContentSha256 TEXT NOT NULL UNIQUE CHECK (length(ContentSha256) = 64),
                LutKind TEXT NOT NULL CHECK (LutKind = '3d'),
                LutSize INTEGER NOT NULL CHECK (LutSize >= 2),
                CreatedUtc TEXT NOT NULL CHECK (length(CreatedUtc) = 28),
                UpdatedUtc TEXT NOT NULL CHECK (length(UpdatedUtc) = 28)
            );

            INSERT INTO LutResources
                (LutId,DisplayName,OriginalFileName,ContentSha256,LutKind,LutSize,CreatedUtc,UpdatedUtc)
            SELECT LutId,DisplayName,OriginalFileName,ContentSha256,'3d',LutSize,CreatedUtc,UpdatedUtc
            FROM LutResourcesV4 WHERE LutKind='3d';

            CREATE INDEX IX_LutResources_DisplayName ON LutResources (DisplayName COLLATE NOCASE);

            CREATE TABLE MediaAssetColor (
                AssetId TEXT NOT NULL PRIMARY KEY CHECK (length(AssetId) = 36),
                CameraLutId TEXT NULL CHECK (CameraLutId IS NULL OR length(CameraLutId) = 36),
                CreativeLutId TEXT NULL CHECK (CreativeLutId IS NULL OR length(CreativeLutId) = 36),
                CreatedUtc TEXT NOT NULL CHECK (length(CreatedUtc) = 28),
                UpdatedUtc TEXT NOT NULL CHECK (length(UpdatedUtc) = 28),
                FOREIGN KEY (AssetId) REFERENCES MediaAssets (AssetId) ON DELETE CASCADE,
                FOREIGN KEY (CameraLutId) REFERENCES LutResources (LutId) ON DELETE RESTRICT,
                FOREIGN KEY (CreativeLutId) REFERENCES LutResources (LutId) ON DELETE RESTRICT,
                CHECK (CameraLutId IS NOT NULL OR CreativeLutId IS NOT NULL)
            );

            INSERT INTO MediaAssetColor
                (AssetId,CameraLutId,CreativeLutId,CreatedUtc,UpdatedUtc)
            SELECT AssetId,CameraLutId,CreativeLutId,CreatedUtc,UpdatedUtc FROM MediaAssetColorV4;

            CREATE INDEX IX_MediaAssetColor_CameraLutId ON MediaAssetColor (CameraLutId);
            CREATE INDEX IX_MediaAssetColor_CreativeLutId ON MediaAssetColor (CreativeLutId);

            DROP TABLE MediaAssetColorV4;
            DROP TABLE LutResourcesV4;
            """);
    }

    private static void Execute(SqliteConnection connection, SqliteTransaction transaction, string sql)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
