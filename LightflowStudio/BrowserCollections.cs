using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

namespace LightflowStudio;

internal enum BrowserCollectionNodeKind { Set, Collection }

internal sealed class BrowserCollectionNode : INotifyPropertyChanged
{
    private bool _isExpanded;
    private bool _isSelected;

    public BrowserCollectionNode(CollectionSet set)
    {
        Kind = BrowserCollectionNodeKind.Set;
        Id = set.CollectionSetId;
        ParentSetId = set.ParentCollectionSetId;
        Name = set.Name;
        Ordinal = set.Ordinal;
        Revision = set.Revision;
    }

    public BrowserCollectionNode(MediaCollection collection)
    {
        Kind = BrowserCollectionNodeKind.Collection;
        Id = collection.CollectionId;
        ParentSetId = collection.ParentCollectionSetId;
        Name = collection.Name;
        Ordinal = collection.Ordinal;
        Revision = collection.Revision;
    }

    public BrowserCollectionNodeKind Kind { get; }
    public Guid Id { get; }
    public Guid? ParentSetId { get; }
    public string Name { get; }
    public int Ordinal { get; }
    public long Revision { get; }
    public bool IsSet => Kind == BrowserCollectionNodeKind.Set;
    public bool IsCollection => Kind == BrowserCollectionNodeKind.Collection;
    public ObservableCollection<BrowserCollectionNode> Children { get; } = [];

    public bool IsExpanded
    {
        get => _isExpanded;
        set { if (_isExpanded != value) { _isExpanded = value; OnPropertyChanged(); } }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set { if (_isSelected != value) { _isSelected = value; OnPropertyChanged(); } }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new(name));
}

internal sealed record CollectionSetPlacementOption(Guid CollectionSetId, string DisplayName);

internal static class BrowserCollectionPlacement
{
    public static Guid? SuggestedParent(BrowserCollectionNode? current) => current switch
    {
        { IsSet: true } => current.Id,
        { IsCollection: true, ParentSetId: { } parent } => parent,
        _ => null
    };

    public static IReadOnlyList<CollectionSetPlacementOption> Options(IEnumerable<BrowserCollectionNode> roots)
    {
        var output = new List<CollectionSetPlacementOption>();
        Add(roots, 0);
        return output;
        void Add(IEnumerable<BrowserCollectionNode> nodes, int depth)
        {
            foreach (var node in nodes.Where(node => node.IsSet).OrderBy(node => node.Ordinal))
            {
                output.Add(new(node.Id, $"{new string(' ', depth * 2)}{node.Name}"));
                Add(node.Children, depth + 1);
            }
        }
    }
}

internal static class BrowserCollectionInteraction
{
    public static BrowserCollectionNode ContextTarget(BrowserCollectionNode clicked, BrowserCollectionNode? selected) => clicked;
    public static bool CanDrop(BrowserCollectionNode dragged, BrowserCollectionNode? targetSet)
    {
        if (targetSet is not null && !targetSet.IsSet) return false;
        if (targetSet is null) return dragged.ParentSetId is not null;
        if (dragged.Id == targetSet.Id || dragged.ParentSetId == targetSet.Id) return false;
        return !dragged.IsSet || !BrowserCollectionTreeModel.Flatten(dragged.Children).Any(node => node.Id == targetSet.Id);
    }
}

internal sealed class BrowserCollectionTreeModel
{
    public ObservableCollection<BrowserCollectionNode> Roots { get; } = [];
    public BrowserCollectionNode? SelectedNode { get; private set; }

    public void Populate(IReadOnlyList<CollectionSet> sets, IReadOnlyList<MediaCollection> collections,
        IReadOnlySet<Guid>? expandedSetIds = null, Guid? selectedCollectionId = null)
    {
        var nodes = sets.ToDictionary(set => set.CollectionSetId, set => new BrowserCollectionNode(set));
        foreach (var set in sets.OrderBy(set => set.Ordinal))
        {
            var node = nodes[set.CollectionSetId];
            node.IsExpanded = expandedSetIds?.Contains(node.Id) == true;
            if (set.ParentCollectionSetId is { } parent && nodes.TryGetValue(parent, out var parentNode))
                parentNode.Children.Add(node);
        }
        foreach (var collection in collections.OrderBy(collection => collection.Ordinal))
        {
            var node = new BrowserCollectionNode(collection);
            node.IsSelected = collection.CollectionId == selectedCollectionId;
            if (node.IsSelected) SelectedNode = node;
            if (collection.ParentCollectionSetId is { } parent && nodes.TryGetValue(parent, out var parentNode))
                parentNode.Children.Add(node);
        }

        Roots.Clear();
        foreach (var node in sets.Where(set => set.ParentCollectionSetId is null).OrderBy(set => set.Ordinal)
                     .Select(set => nodes[set.CollectionSetId])) Roots.Add(node);
        foreach (var node in collections.Where(collection => collection.ParentCollectionSetId is null)
                     .OrderBy(collection => collection.Ordinal).Select(collection => new BrowserCollectionNode(collection)))
        {
            node.IsSelected = node.Id == selectedCollectionId;
            if (node.IsSelected) SelectedNode = node;
            Roots.Add(node);
        }
    }

    public IReadOnlySet<Guid> ExpandedSetIds() => Flatten(Roots).Where(node => node.IsSet && node.IsExpanded)
        .Select(node => node.Id).ToHashSet();

    public void Select(BrowserCollectionNode? selected)
    {
        foreach (var node in Flatten(Roots)) node.IsSelected = ReferenceEquals(node, selected);
        SelectedNode = selected;
    }

    public static IReadOnlyList<BrowserCollectionNode> Flatten(IEnumerable<BrowserCollectionNode> roots) =>
        roots.SelectMany(node => new[] { node }.Concat(Flatten(node.Children))).ToArray();
}

internal sealed record BrowserCollectionScope(
    MediaCollection Collection,
    IReadOnlyList<MediaFolderEntry> Entries,
    IReadOnlyList<CatalogReconciliationItem> Assets,
    int UnavailableCount,
    IDerivedWorkBatch? DerivedWork);

/// <summary>
/// Resolves durable Collection membership into the same entry/identity vocabulary consumed by the existing
/// Browser grid. It performs no filesystem enumeration and schedules Preview work through the existing bounded
/// derived-work scheduler only for currently available members.
/// </summary>
internal sealed class BrowserCollectionScopeService(
    ICollectionOrganizationService collections, IMediaAssetService assets, IMediaRootService roots, IMediaTypeRegistry mediaTypes,
    Func<IDerivedWorkScheduler?> derivedWork)
{
    public async Task<BrowserCollectionScope> LoadAsync(Guid collectionId,
        CancellationToken cancellationToken = default)
    {
        var collection = await collections.GetCollectionAsync(collectionId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("The Collection no longer exists.");
        var memberships = await collections.ListMembershipsAsync(collectionId, cancellationToken).ConfigureAwait(false);
        var catalog = (await assets.ListAsync(cancellationToken).ConfigureAwait(false))
            .ToDictionary(asset => asset.AssetId);
        var rootAvailability = (await roots.ListAsync(cancellationToken).ConfigureAwait(false))
            .ToDictionary(root => root.RootId, root => root.Availability);
        var entries = new List<MediaFolderEntry>(memberships.Count);
        var reconciliation = new List<CatalogReconciliationItem>(memberships.Count);
        var unavailable = 0;
        foreach (var membership in memberships.OrderBy(item => item.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!catalog.TryGetValue(membership.AssetId, out var asset)) continue; // FK makes this defensive only.
            var available = asset.SourceStatus == MediaAssetSourceStatus.Available &&
                rootAvailability.TryGetValue(asset.RootId, out var availability) && availability == MediaRootAvailability.Online;
            if (!available) unavailable++;
            var classification = mediaTypes.Classify(new(Path.GetFileName(asset.RelativePath)));
            entries.Add(new(asset.RootId, asset.RelativePath, asset.RelativePathKey,
                Path.GetFileName(asset.RelativePath), false, classification, asset.FileSizeBytes,
                new DateTimeOffset(asset.LastWriteUtcTicks, TimeSpan.Zero), $"asset:{asset.AssetId:D}",
                asset.AssetId, available));
            reconciliation.Add(new(asset.AssetId, asset.RelativePath, available
                ? CatalogReconciliationItemStatus.Unchanged : CatalogReconciliationItemStatus.Missing));
        }

        var result = new CatalogReconciliationResult(CatalogReconciliationStatus.Succeeded, Guid.Empty,
            $"collection:{collectionId:D}", reconciliation);
        var scheduled = derivedWork()?.TrySchedule(result, DerivedWorkPriority.Visible, cancellationToken);
        return new(collection, entries, reconciliation, unavailable,
            scheduled is { Accepted: true } ? scheduled.Batch : null);
    }
}
