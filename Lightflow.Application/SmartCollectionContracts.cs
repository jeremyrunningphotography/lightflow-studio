namespace LightflowStudio;
public enum SmartCollectionSourceKind { Folder, Collection }
public sealed record SmartCollectionSource(SmartCollectionSourceKind Kind, Guid? RootId = null,
    string? RelativeFolder = null, Guid? CollectionId = null, bool IncludeSubfolders = false)
{
    public SmartCollectionSource Validate()
    {
        if (Kind == SmartCollectionSourceKind.Folder && RootId is { } root && root != Guid.Empty &&
            RelativeFolder is not null && CollectionId is null)
            return this with { RelativeFolder = RelativeFolder.Length == 0 ? "" : CatalogRelativePaths.NormalizeRelativePath(RelativeFolder) };
        if (Kind == SmartCollectionSourceKind.Collection && CollectionId is { } collection && collection != Guid.Empty &&
            RootId is null && RelativeFolder is null && !IncludeSubfolders) return this;
        throw new ArgumentException("Choose a Folder or static Collection Source.");
    }
}
public sealed record CatalogSmartCollectionDefinition(MediaCollection Organization, SmartCollectionSource Source, ICatalogSavedQuery Query)
{
    public Guid SmartCollectionId => Organization.CollectionId;
}
