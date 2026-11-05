using System.IO;
namespace LightflowStudio;

// Compatibility composition: the existing Windows isolation guard is preserved.
internal sealed class PreviewStoreService(ILightflowStorageLocations locations, TimeProvider? time = null)
    : CatalogPreviewStoreService(locations, time, ApplicationDataProfile.GuardAccess);

internal static class PreviewArtifactFiles
{
    public static bool Exists(string previewsDirectory, string relativePath)
    {
        try { return File.Exists(MediaPathSemantics.ResolveContained(previewsDirectory, relativePath)); }
        catch (ArgumentException) { return false; }
    }
}
