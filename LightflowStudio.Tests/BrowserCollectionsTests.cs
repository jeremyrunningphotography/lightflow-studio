using LightflowStudio;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class BrowserCollectionsTests
{
    [Fact]
    public void RightClick_TargetsClickedCollectionInsteadOfPreviouslySelectedSet()
    {
        var selectedSet = new BrowserCollectionNode(Set("Selected set", 0));
        var clickedCollection = new BrowserCollectionNode(Collection("Clicked collection", 0, selectedSet.Id));

        var target = BrowserCollectionInteraction.ContextTarget(clickedCollection, selectedSet);

        Assert.Same(clickedCollection, target);
        Assert.True(target.IsCollection);
    }

    [Fact]
    public void Placement_DefaultsToContainingSetButAllowsTopLevelOverride()
    {
        var set = new BrowserCollectionNode(Set("Set", 0));
        var collection = new BrowserCollectionNode(Collection("Collection", 0, set.Id));

        Assert.Equal(set.Id, BrowserCollectionPlacement.SuggestedParent(collection));
        Assert.Equal(set.Id, BrowserCollectionPlacement.SuggestedParent(set));
        Assert.Null(BrowserCollectionPlacement.SuggestedParent(null));
        var options = BrowserCollectionPlacement.Options([set]);
        Assert.Null(options[0].CollectionSetId);
        Assert.Equal("Top level", options[0].DisplayName);
        Assert.Equal(set.Id, options[1].CollectionSetId);
    }

    [Fact]
    public void ActiveScopeSelection_HasOneAuthoritativeKind()
    {
        var selection = new BrowserScopeSelection();
        selection.ActivateFolder();
        Assert.Equal(BrowserScopeSelectionKind.Folder, selection.Active);
        selection.ActivateCollection();
        Assert.Equal(BrowserScopeSelectionKind.Collection, selection.Active);
    }

    [Fact]
    public void DragIntent_DistinguishesSiblingInsertionFromDropIntoSet()
    {
        var first = new BrowserCollectionNode(Collection("First", 0));
        var second = new BrowserCollectionNode(Collection("Second", 1));
        var set = new BrowserCollectionNode(Set("Set", 0));

        Assert.Equal(BrowserCollectionDropKind.InsertBefore, BrowserCollectionInteraction.DropAt(first, second, 0.1).Kind);
        Assert.Equal(BrowserCollectionDropKind.InsertAfter, BrowserCollectionInteraction.DropAt(first, second, 0.9).Kind);
        Assert.Equal(BrowserCollectionDropKind.IntoSet, BrowserCollectionInteraction.DropAt(first, set, 0.5).Kind);
    }

    [Fact]
    public void NameSort_IsExplicitAndPreservesSeparateKindInputs()
    {
        var nodes = new[] { new BrowserCollectionNode(Collection("Zulu", 0)), new BrowserCollectionNode(Collection("alpha", 1)) };
        Assert.Equal(["alpha", "Zulu"], BrowserCollectionInteraction.OrderByName(nodes, false).Select(node => node.Name));
        Assert.Equal(["Zulu", "alpha"], BrowserCollectionInteraction.OrderByName(nodes, true).Select(node => node.Name));
    }

    [Fact]
    public void HierarchyDrop_AllowsSetTargetsAndTopLevelWhileRejectingCyclesAndCollectionTargets()
    {
        var parent = new BrowserCollectionNode(Set("Parent", 0));
        var child = new BrowserCollectionNode(Set("Child", 0, parent.Id));
        var other = new BrowserCollectionNode(Set("Other", 1));
        var collection = new BrowserCollectionNode(Collection("Collection", 0, parent.Id));
        parent.Children.Add(child);
        parent.Children.Add(collection);

        Assert.True(BrowserCollectionInteraction.CanDrop(collection, other));
        Assert.True(BrowserCollectionInteraction.CanDrop(child, other));
        Assert.True(BrowserCollectionInteraction.CanDrop(child, null));
        Assert.False(BrowserCollectionInteraction.CanDrop(parent, child));
        Assert.False(BrowserCollectionInteraction.CanDrop(other, collection));
    }

    [Fact]
    public void Tree_PresentsSetsBeforeCollectionsAtEachLevelAndRestoresExpansionSelection()
    {
        var top = Set("Top", 0);
        var child = Set("Child", 0, top.CollectionSetId);
        var nested = Collection("Nested", 0, child.CollectionSetId);
        var rootCollection = Collection("Root collection", 0);
        var model = new BrowserCollectionTreeModel();

        model.Populate([top, child], [rootCollection, nested], new HashSet<Guid> { top.CollectionSetId }, nested.CollectionId);

        Assert.Equal([top.CollectionSetId, rootCollection.CollectionId], model.Roots.Select(node => node.Id));
        Assert.True(model.Roots[0].IsExpanded);
        Assert.Equal(child.CollectionSetId, Assert.Single(model.Roots[0].Children).Id);
        Assert.Equal(nested.CollectionId, Assert.Single(model.Roots[0].Children[0].Children).Id);
        Assert.Equal(nested.CollectionId, model.SelectedNode!.Id);
    }

    [Fact]
    public async Task Scope_UsesMembershipOnlyPreservesOrderAndTruthfullyRetainsUnavailableMembers()
    {
        var collection = Collection("Picks", 0);
        var available = Asset("same.jpg", MediaAssetSourceStatus.Available);
        var missing = Asset("same.jpg", MediaAssetSourceStatus.Missing);
        var memberships = new[] { Membership(collection.CollectionId, missing.AssetId, 0), Membership(collection.CollectionId, available.AssetId, 1) };
        var service = new BrowserCollectionScopeService(new FakeCollections(collection, memberships),
            new FakeAssets([available, missing]), new FakeRoots([available, missing]), MediaTypeRegistry.CreateDefault(), () => null);

        var scope = await service.LoadAsync(collection.CollectionId);

        Assert.Equal([missing.AssetId, available.AssetId], scope.Entries.Select(entry => entry.AssetId));
        Assert.Equal([false, true], scope.Entries.Select(entry => entry.IsAvailable));
        Assert.Equal(1, scope.UnavailableCount);
        Assert.All(scope.Entries, entry => Assert.StartsWith("asset:", entry.StableKey));

        var grid = new BrowserGridModel();
        grid.Populate(scope.Entries);
        Assert.Equal(2, grid.TotalCount); // equal relative paths across roots never collide in Collection scope
        Assert.Contains(grid.Tiles, tile => !tile.IsAvailable && tile.AssetId == missing.AssetId);
    }

    [Fact]
    public async Task Scope_LargeCollectionFeedsExistingGridQuerySelectionAndVirtualizedRows()
    {
        var collection = Collection("Large", 0);
        var assets = Enumerable.Range(0, 2500).Select(index => Asset($"image-{index:D4}.jpg", MediaAssetSourceStatus.Available)).ToArray();
        var memberships = assets.Select((asset, index) => Membership(collection.CollectionId, asset.AssetId, index)).ToArray();
        var service = new BrowserCollectionScopeService(new FakeCollections(collection, memberships),
            new FakeAssets(assets), new FakeRoots(assets), MediaTypeRegistry.CreateDefault(), () => null);

        var scope = await service.LoadAsync(collection.CollectionId);
        var grid = new BrowserGridModel();
        grid.SetColumns(8);
        grid.Populate(scope.Entries);
        grid.SetQuery(BrowserQuery.Default with { SearchText = "image-249" });
        grid.SelectSingle(0);

        Assert.Equal(2500, grid.TotalCount);
        Assert.Equal(10, grid.VisibleCount);
        Assert.Equal(2, grid.Rows.Count);
        Assert.Single(grid.SelectedAssetIdsInBrowserOrder);
    }

    private static CollectionSet Set(string name, int ordinal, Guid? parent = null) =>
        new(Guid.NewGuid(), parent, name, ordinal, 1, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
    private static MediaCollection Collection(string name, int ordinal, Guid? parent = null) =>
        new(Guid.NewGuid(), parent, name, ordinal, 1, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
    private static CollectionMembership Membership(Guid collectionId, Guid assetId, int ordinal) =>
        new(collectionId, assetId, ordinal, 1, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
    private static MediaAsset Asset(string path, MediaAssetSourceStatus status) =>
        new(Guid.NewGuid(), Guid.NewGuid(), path, path.ToUpperInvariant(), "image", 10, DateTimeOffset.UtcNow.UtcTicks,
            null, status, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private sealed class FakeAssets(IReadOnlyList<MediaAsset> assets) : IMediaAssetService
    {
        public Task<IReadOnlyList<MediaAsset>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult(assets);
        public Task<MediaAssetOperationResult> CreateAsync(Guid rootId, string relativePath, string mediaType = "unknown", CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MediaAssetResolution?> GetAsync(Guid assetId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MediaAssetResolution?> FindAsync(Guid rootId, string relativePath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MediaAssetOperationResult> ObserveAsync(Guid assetId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<int> MarkMissingAsync(IReadOnlyCollection<Guid> assetIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeRoots(IReadOnlyList<MediaAsset> assets) : IMediaRootService
    {
        public Task<IReadOnlyList<MediaRootInfo>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MediaRootInfo>>(assets.Select(asset => asset.RootId).Distinct()
                .Select(id => new MediaRootInfo(id, id.ToString(), @"C:\media", MediaRootAvailability.Online)).ToArray());
        public Task<MediaRootInfo?> GetAsync(Guid rootId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MediaRootChangeResult> CreateAsync(string displayName, string physicalPath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MediaRootChangeResult> RenameAsync(Guid rootId, string displayName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MediaRootChangeResult> RemapAsync(Guid rootId, string physicalPath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MediaPathResolution> ResolveAsync(Guid rootId, string relativePath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeCollections(MediaCollection collection, IReadOnlyList<CollectionMembership> memberships) : ICollectionOrganizationService
    {
        public Task<MediaCollection?> GetCollectionAsync(Guid collectionId, CancellationToken cancellationToken = default) => Task.FromResult<MediaCollection?>(collection);
        public Task<IReadOnlyList<CollectionMembership>> ListMembershipsAsync(Guid collectionId, CancellationToken cancellationToken = default) => Task.FromResult(memberships);
        public Task<IReadOnlyList<CollectionSet>> ListSetsAsync(Guid? parentCollectionSetId = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<MediaCollection>> ListCollectionsAsync(Guid? parentCollectionSetId = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CollectionSet?> GetSetAsync(Guid collectionSetId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CollectionSet> CreateSetAsync(string name, Guid? parentCollectionSetId = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MediaCollection> CreateCollectionAsync(string name, Guid? parentCollectionSetId = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CollectionSet> RenameSetAsync(Guid collectionSetId, long expectedRevision, string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MediaCollection> RenameCollectionAsync(Guid collectionId, long expectedRevision, string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CollectionSet> ReparentSetAsync(Guid collectionSetId, long expectedRevision, Guid? parentCollectionSetId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MediaCollection> ReparentCollectionAsync(Guid collectionId, long expectedRevision, Guid? parentCollectionSetId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteSetAsync(Guid collectionSetId, long expectedRevision, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteCollectionAsync(Guid collectionId, long expectedRevision, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<CollectionSet>> ReorderSetsAsync(Guid? parentCollectionSetId, IReadOnlyList<CollectionOrder> order, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<MediaCollection>> ReorderCollectionsAsync(Guid? parentCollectionSetId, IReadOnlyList<CollectionOrder> order, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CollectionMembershipCreateResult> AddMembershipAsync(Guid collectionId, Guid assetId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<CollectionMembershipCreateResult>> AddMembershipsAsync(Guid collectionId, IReadOnlyList<Guid> assetIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task RemoveMembershipAsync(Guid collectionId, Guid assetId, long expectedRevision, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task RemoveMembershipsAsync(Guid collectionId, IReadOnlyList<CollectionOrder> memberships, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<CollectionMembership>> ReorderMembershipsAsync(Guid collectionId, IReadOnlyList<CollectionOrder> order, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
