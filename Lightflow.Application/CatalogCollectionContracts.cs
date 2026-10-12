namespace LightflowStudio;
public sealed record CollectionSet(
    Guid CollectionSetId, Guid? ParentCollectionSetId, string Name, int Ordinal, long Revision,
    DateTimeOffset CreatedUtc, DateTimeOffset UpdatedUtc);

public sealed record MediaCollection(
    Guid CollectionId, Guid? ParentCollectionSetId, string Name, int Ordinal, long Revision,
    DateTimeOffset CreatedUtc, DateTimeOffset UpdatedUtc)
{
    public bool IsSmartCollection { get; init; }
}

public sealed record CollectionMembership(
    Guid CollectionId, Guid AssetId, int Ordinal, long Revision,
    DateTimeOffset CreatedUtc, DateTimeOffset UpdatedUtc);

public sealed record CollectionOrder(Guid Id, long ExpectedRevision);
public enum CollectionHierarchyItemKind { Set, Collection }
public sealed record CollectionHierarchyOrder(CollectionHierarchyItemKind Kind, Guid Id, long ExpectedRevision);
public sealed record CollectionMembershipCreateResult(CollectionMembership Membership, bool Created);

public sealed class CollectionConcurrencyException(string message) : InvalidOperationException(message);
public sealed class CollectionHierarchyException(string message) : InvalidOperationException(message);
public sealed class CollectionNotEmptyException(string message) : InvalidOperationException(message);

public interface ICollectionOrganizationService
{
    Task<IReadOnlyList<CollectionSet>> ListSetsAsync(Guid? parentCollectionSetId = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MediaCollection>> ListCollectionsAsync(Guid? parentCollectionSetId = null, CancellationToken cancellationToken = default);
    Task<CollectionSet?> GetSetAsync(Guid collectionSetId, CancellationToken cancellationToken = default);
    Task<MediaCollection?> GetCollectionAsync(Guid collectionId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CollectionMembership>> ListMembershipsAsync(Guid collectionId, CancellationToken cancellationToken = default);
    Task<CollectionSet> CreateSetAsync(string name, Guid? parentCollectionSetId = null, CancellationToken cancellationToken = default);
    Task<MediaCollection> CreateCollectionAsync(string name, Guid? parentCollectionSetId = null, CancellationToken cancellationToken = default);
    Task<CollectionSet> RenameSetAsync(Guid collectionSetId, long expectedRevision, string name, CancellationToken cancellationToken = default);
    Task<MediaCollection> RenameCollectionAsync(Guid collectionId, long expectedRevision, string name, CancellationToken cancellationToken = default);
    Task<CollectionSet> ReparentSetAsync(Guid collectionSetId, long expectedRevision, Guid? parentCollectionSetId, CancellationToken cancellationToken = default);
    Task<MediaCollection> ReparentCollectionAsync(Guid collectionId, long expectedRevision, Guid? parentCollectionSetId, CancellationToken cancellationToken = default);
    Task DeleteSetAsync(Guid collectionSetId, long expectedRevision, CancellationToken cancellationToken = default);
    Task DeleteCollectionAsync(Guid collectionId, long expectedRevision, CancellationToken cancellationToken = default);
    Task ReorderHierarchyAsync(Guid? parentCollectionSetId, IReadOnlyList<CollectionHierarchyOrder> order, CancellationToken cancellationToken = default);
    Task<CollectionMembershipCreateResult> AddMembershipAsync(Guid collectionId, Guid assetId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CollectionMembershipCreateResult>> AddMembershipsAsync(Guid collectionId, IReadOnlyList<Guid> assetIds, CancellationToken cancellationToken = default);
    Task RemoveMembershipAsync(Guid collectionId, Guid assetId, long expectedRevision, CancellationToken cancellationToken = default);
    Task RemoveMembershipsAsync(Guid collectionId, IReadOnlyList<CollectionOrder> memberships, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CollectionMembership>> ReorderMembershipsAsync(Guid collectionId, IReadOnlyList<CollectionOrder> order, CancellationToken cancellationToken = default);
}


public interface ICatalogSavedQuery { string Serialize(); }
