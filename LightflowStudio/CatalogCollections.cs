namespace LightflowStudio;
internal sealed class CatalogCollectionOrganizationService(Func<CatalogDatabaseSession?> session, Func<DateTimeOffset>? utcNow = null)
    : CatalogCollectionOrganizationStore(session, utcNow, json => BrowserQueryIntent.Deserialize(json))
{
    public new async Task<SmartCollectionDefinition?> GetSmartCollectionAsync(Guid id, CancellationToken token = default)
    {
        var result = await base.GetSmartCollectionAsync(id, token).ConfigureAwait(false);
        return result is null ? null : Convert(result);
    }
    public async Task<SmartCollectionDefinition> SaveSmartCollectionAsync(string name, Guid? parent,
        SmartCollectionSource source, BrowserQueryIntent query, Guid? id = null, long? expectedRevision = null,
        CancellationToken token = default)
        => Convert(await base.SaveSmartCollectionAsync(name, parent, source, query, id, expectedRevision, token).ConfigureAwait(false));
    private static SmartCollectionDefinition Convert(CatalogSmartCollectionDefinition value)
        => new(value.Organization, value.Source, (BrowserQueryIntent)value.Query);
}
