using System.Diagnostics;
using System.IO;

namespace LightflowStudio;

internal static class JobOutputLocation
{
    internal static ProcessStartInfo? RevealRequest(string path) => File.Exists(path)
        ? new("explorer.exe", $"/select,\"{Path.GetFullPath(path)}\"") { UseShellExecute = true }
        : null;
}
