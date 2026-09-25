using System.IO;

namespace LightflowStudio;

// Evaluate configured locations on every call: Preview/Catalog relocation must not leave a stale exclusion.
internal sealed class OwnedStoragePaths(Func<ILightflowStorageLocations> locations)
{
    public bool Contains(string path)
    {
        var current = locations();
        var full = Path.GetFullPath(path);
        return new[] { current.ApplicationDataDirectory, current.CatalogDirectory, current.PreviewsDirectory,
            current.TemporaryDirectory }.Any(root => Within(full, root));
    }
    private static bool Within(string path, string directory)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        return string.Equals(path, root, StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
