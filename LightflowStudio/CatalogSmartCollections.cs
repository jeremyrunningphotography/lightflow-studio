namespace LightflowStudio;
internal sealed record SmartCollectionDefinition(MediaCollection Organization, SmartCollectionSource Source, BrowserQueryIntent Query)
{
    public Guid SmartCollectionId => Organization.CollectionId;
}
