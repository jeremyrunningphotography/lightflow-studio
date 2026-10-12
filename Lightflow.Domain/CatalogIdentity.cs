namespace LightflowStudio;

public sealed record CatalogIdentity(
    Guid CatalogId,
    string ApplicationIdentity,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc);

