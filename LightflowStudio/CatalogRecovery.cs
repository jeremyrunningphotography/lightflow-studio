using Lightflow.Application;
namespace LightflowStudio;
internal sealed class SqliteCatalogRecoveryService : CatalogRecoveryService
{
    public SqliteCatalogRecoveryService(ILightflowStorageLocations locations, Func<DateTimeOffset>? utcNow = null)
        : base(WindowsCatalogRecoveryEffects.Instance, locations, utcNow) { }
    internal static IReadOnlyList<CatalogBackup> ListUserBackups(LightflowStorageLocations locations, string destination) => CatalogRecoveryService.ListUserBackups(WindowsCatalogRecoveryEffects.Instance, locations, destination);
    internal static ClosedCatalogManifest VerifyTransport(string directory, bool allowCatalogOwnershipEvidence = false) => CatalogRecoveryService.VerifyTransport(WindowsCatalogRecoveryEffects.Instance, directory, allowCatalogOwnershipEvidence);
    internal static IReadOnlyList<ClosedCatalogExternalDependency> ReadExternalLutDependencies(string snapshot) => CatalogRecoveryService.ReadExternalLutDependencies(WindowsCatalogRecoveryEffects.Instance, snapshot);
    internal static void FinalizeRelocationCompatibility(string source, string destination) => CatalogRecoveryService.FinalizeRelocationCompatibility(WindowsCatalogRecoveryEffects.Instance, source, destination);
}
