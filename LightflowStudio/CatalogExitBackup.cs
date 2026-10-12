namespace LightflowStudio;
internal static class CatalogBackupDestination
{
    public static string Default(LightflowStorageLocations locations) => CatalogBackupDestinationPolicy.Default(locations);
    public static string Validate(LightflowStorageLocations locations, string directory) => CatalogBackupDestinationPolicy.Validate(WindowsCatalogTransferEffects.Instance, locations, directory);
}
