using System.Diagnostics;
using System.IO;

namespace LightflowStudio;

internal static class JobOutputLocation
{
    internal static ProcessStartInfo? RevealRequest(string path) => File.Exists(path)
        ? ExplorerShell.Request(path, selectFile: true)
        : null;
}
