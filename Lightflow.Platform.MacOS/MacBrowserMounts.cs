namespace Lightflow.Platform.MacOS;

/// <summary>Visible native volume namespaces, independent of Catalog identity and active-storage policy.</summary>
public static class MacBrowserMounts
{
    public static string Anchor(string path, IEnumerable<string> mounts) => mounts
        .Select(Path.TrimEndingDirectorySeparator)
        .Where(mount => (mount == "/" || mount.StartsWith("/Volumes/", StringComparison.Ordinal)) &&
            (path == mount || path.StartsWith(mount == "/" ? "/" : mount + "/", StringComparison.Ordinal)))
        .OrderByDescending(mount => mount.Length).FirstOrDefault()
        ?? throw new IOException("This folder has no available volume boundary.");
}
