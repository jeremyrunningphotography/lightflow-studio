using System.IO;

namespace LightflowStudio;

internal static class EncodingOutputCleanup
{
    public static void DeleteIncomplete(string output, string? identityCacheDirectory = null)
    {
        try { if (File.Exists(output)) File.Delete(output); } catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        EncodingOutputIdentityStore.Delete(output, identityCacheDirectory);
    }
}
