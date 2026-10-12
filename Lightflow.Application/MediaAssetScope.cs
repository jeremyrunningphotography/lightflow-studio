namespace LightflowStudio;
public static class MediaAssetScope
{
    public static bool Contains(MediaAsset asset, Guid rootId, string folder, bool recursive)
    {
        if (asset.RootId != rootId) return false;
        var prefix = string.IsNullOrEmpty(folder) ? "" : CatalogRelativePaths.RelativePathKey(folder) + "/";
        return asset.RelativePathKey.StartsWith(prefix, StringComparison.Ordinal) &&
            (recursive || !asset.RelativePathKey[prefix.Length..].Contains('/'));
    }

}
