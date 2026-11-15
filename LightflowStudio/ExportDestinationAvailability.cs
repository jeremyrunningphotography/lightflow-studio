using System.IO;

namespace LightflowStudio;

internal static class ExportDestinationAvailability
{
    // Worker-only: no output directories are created by planning. Check the nearest
    // existing ancestor with an exclusively owned disposable file, then execution
    // still rechecks permissions and source/output safety at the point of use.
    internal static string? Check(string directory, string? explicitRoot = null)
    {
        try
        {
            if (explicitRoot is not null && !Directory.Exists(explicitRoot))
                return $"The output folder is unavailable: {explicitRoot}. Reconnect it or choose another folder.";
            var ancestor = directory;
            while (!Directory.Exists(ancestor))
                ancestor = Path.GetDirectoryName(ancestor) ?? throw new IOException("The destination drive or share is unavailable.");
            var probe = Path.Combine(ancestor, $".lightflow-write-check-{Guid.NewGuid():N}.tmp");
            using var stream = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1,
                FileOptions.DeleteOnClose);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return $"Lightflow cannot write to {directory}. Choose another folder or check permissions. {ex.Message}";
        }
    }
}
