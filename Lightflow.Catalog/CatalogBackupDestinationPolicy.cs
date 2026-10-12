using System.IO;
using Lightflow.Application;
namespace LightflowStudio;

public static class CatalogBackupDestinationPolicy
{
    public static string Default(ILightflowStorageLocations locations) =>
        Path.Combine(locations.ApplicationDataDirectory, "Catalog Backups");

    public static string Validate(ICatalogTransferNativeEffects effects, ILightflowStorageLocations locations, string directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Path.IsPathFullyQualified(directory))
            throw new ArgumentException("Choose an absolute backup folder path.");
        var path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory.Trim()));
        effects.RequireIsolatedDestination(locations, path);
        if (effects.PathsOverlap(path, locations.CatalogDirectory) ||
            effects.PathsOverlap(path, locations.PreviewsDirectory) ||
            effects.PathsOverlap(path, locations.TemporaryDirectory))
            throw new ArgumentException("Choose a backup folder separate from the live Catalog, Previews, and temporary storage.");
        // Reject aliases into managed storage, including non-isolated profiles.
        foreach (var storagePath in new[] { path, locations.CatalogDirectory, locations.PreviewsDirectory, locations.TemporaryDirectory })
        for (var ancestor = storagePath; ancestor is not null; ancestor = Path.GetDirectoryName(ancestor))
            if ((Directory.Exists(ancestor) || File.Exists(ancestor)) &&
                (File.GetAttributes(ancestor) & FileAttributes.ReparsePoint) != 0)
                throw new ArgumentException("Catalog and backup storage must not use links or junctions; their separation could not be verified.");
        if (File.Exists(path)) throw new IOException("The backup location is a file, not a folder.");
        return path;
    }
}

