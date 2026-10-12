using Lightflow.Application;
namespace LightflowStudio;
internal sealed partial class LightflowStorageCoordinator
{
    public static Task<StorageStartupResult> StartAsync(string? localApplicationData = null,
        CancellationToken cancellationToken = default, ICatalogRelocationTransfer? transfer = null,
        IStorageConfigurationStore? configuration = null, ICatalogSessionActivator? activator = null,
        ICatalogRecoveryService? recovery = null, LightflowStorageLocations? profile = null,
        InitializedDataProfile? initializedProfile = null, IStorageLocationAssessor? assessor = null,
        bool allowFirstCreation = true, string? existingCatalogDirectory = null, Guid? selectedCatalogId = null, bool resumePreviousCatalogOnFailure = true)
        => StartWindowsAsync(new WindowsCatalogLifecycleEffects(localApplicationData, transfer, configuration, activator,
            recovery, profile, initializedProfile, assessor), cancellationToken, allowFirstCreation, existingCatalogDirectory,
            selectedCatalogId, resumePreviousCatalogOnFailure);
    private static async Task<StorageStartupResult> StartWindowsAsync(WindowsCatalogLifecycleEffects effects, CancellationToken token,
        bool allowCreation, string? directory, Guid? identity, bool resume)
    {
        var result = await new CatalogLifecycle((ICatalogLifecycleEffects)effects).StartAsync(token, allowCreation, directory, identity, resume).ConfigureAwait(false);
        return new(result.Status, result.Coordinator is null ? null : effects.Owner, result.Diagnostic, result.RecoveryReason);
    }
    public Task<CatalogSelectionResult> InspectCatalogSelectionAsync(string directory, CancellationToken token = default)
        => _lifecycle.InspectCatalogSelectionAsync(directory, token);
    internal static Guid? VerifySelectedTransfer(string directory, bool previouslyValidated)
        => new CatalogLifecycle((ICatalogLifecycleEffects)new WindowsCatalogLifecycleEffects()).VerifySelectedTransfer(directory, previouslyValidated);
}
