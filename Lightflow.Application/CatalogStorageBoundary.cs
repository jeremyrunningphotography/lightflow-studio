namespace LightflowStudio;

public enum CatalogStorageBoundary
{
    BeforeDirectoryCreation_BeforeInitialSQLiteUse,
    AfterDirectoryCreation_BeforeInitialSQLiteUse,
    AfterAtomicCreation_BeforeInitialSQLiteUse,
    BeforeInspection_BeforeInitialSQLiteUse,
    MigrationBoundary_BeforeInitialSQLiteUse,
    MigrationBoundary_AfterInitialSQLiteUse,
    MigrationStep_BeforeInitialSQLiteUse,
    MigrationStep_AfterInitialSQLiteUse,
    AfterCatalogValidation_SQLiteClosed,
    FailedCreationCleanup_SQLiteClosed
}
