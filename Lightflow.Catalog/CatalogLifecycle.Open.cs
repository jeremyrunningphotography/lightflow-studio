namespace LightflowStudio;

public sealed partial class CatalogLifecycle
{
    /// <summary>
    /// Opens the existing SQLite owner only after native admission and writer evidence.
    /// The returned session remains unpublished until the activation/configuration owner commits.
    /// </summary>
    public static async Task<CatalogOpenResult> OpenAdmittedAsync(
        ILightflowStorageLocations configured,
        CatalogStorageAdmission admission,
        StartupStoreEvidence catalogEvidence,
        StartupStoreEvidence? previewEvidence,
        bool create,
        Guid? expectedCatalogId,
        ICatalogMigrationBackup? migrationBackup = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(catalogEvidence);
        RequireCompletePeers(catalogEvidence, previewEvidence);
        var bound = admission.LocationsForUse();
        catalogEvidence.RequireOwnership(bound.CatalogDatabasePath);
        var database = new CatalogDatabaseService(configured, migrationBackup)
        {
            CleanStartup = catalogEvidence.KnownClean,
            ValidationReason = catalogEvidence.Reason,
            ExpectedCatalogId = expectedCatalogId,
            ValidateStorageAccess = admission.ValidateClosedDatabaseBoundary,
            ResolvedDatabasePath = bound.CatalogDatabasePath,
        };
        await admission.ValidateAsync(cancellationToken).ConfigureAwait(false);
        var opened = create
            ? await database.CreateNewAsync(cancellationToken).ConfigureAwait(false)
            : await database.OpenExistingAsync(cancellationToken).ConfigureAwait(false);
        if (opened.IsSuccess)
        {
            try
            {
                opened.Session!.CloseBeforeActivation();
                await admission.ValidateAsync(cancellationToken, "AfterCatalogOpen_AfterInitialSQLiteUse").ConfigureAwait(false);
            }
            catch
            {
                await opened.Session!.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }
        return opened;
    }
}
