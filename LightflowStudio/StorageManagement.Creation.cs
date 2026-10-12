using Lightflow.Application;
namespace LightflowStudio;
internal sealed partial class LightflowStorageCoordinator
{
    internal static string ProposeNewCatalogDirectory(LightflowStorageLocations profile, AppSettings previous)
        => new CatalogLifecycle((ICatalogLifecycleEffects)new WindowsCatalogLifecycleEffects(profile: profile)).ProposeNewCatalogDirectory(profile, new WindowsCatalogConfigurationSnapshot(previous));
    internal static Task<StorageStartupResult> CreateCatalogAsync(string directory, LightflowStorageLocations profile,
        InitializedDataProfile? initializedProfile = null, IStorageConfigurationStore? configuration = null,
        ICatalogSessionActivator? activator = null, IStorageLocationAssessor? assessor = null, CancellationToken token = default)
        => CreateWindowsAsync(new WindowsCatalogLifecycleEffects(configuration: configuration, activator: activator,
            profile: profile, initializedProfile: initializedProfile, assessor: assessor), directory, token);
    private static async Task<StorageStartupResult> CreateWindowsAsync(WindowsCatalogLifecycleEffects effects, string directory, CancellationToken token)
    {
        var result = await new CatalogLifecycle((ICatalogLifecycleEffects)effects).CreateCatalogAsync(directory, token).ConfigureAwait(false);
        return new(result.Status, result.Coordinator is null ? null : effects.Owner, result.Diagnostic, result.RecoveryReason);
    }
}
