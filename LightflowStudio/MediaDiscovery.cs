using Lightflow.Application;
namespace LightflowStudio;

// Compatibility adapters keep Windows containment/profile effects and constructor semantics intact.
internal sealed class MediaFolderFileSystem() : ContainedMediaFolderFileSystem(new WindowsMediaRootPathEffects());
internal sealed class MediaFolderEnumerator(IMediaRootService roots, IMediaTypeRegistry mediaTypes,
    IMediaFolderFileSystem fileSystem, int maximumConcurrency = 2, Func<string, bool>? excludedPath = null)
    : CatalogMediaFolderEnumerator(roots, mediaTypes, fileSystem, new WindowsMediaRootPathEffects(), maximumConcurrency, excludedPath);
