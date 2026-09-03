using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

namespace LightflowStudio;

internal enum BrowserCollectionNodeKind { Set, Collection }

internal sealed record BrowserAssetDragPayload(IReadOnlyList<Guid> AssetIds);

internal static class BrowserAssetDragSelection
{
    public static bool ShouldDeferSingleSelection(bool tileIsSelected, int selectionCount,
        bool shiftPressed, bool controlPressed) =>
        tileIsSelected && selectionCount > 1 && !shiftPressed && !controlPressed;

    public static IReadOnlyList<Guid> AssetIdsForDrag(bool originIsSelected, Guid? originAssetId,
        IReadOnlyList<Guid> selectedAssetIds) =>
        originIsSelected ? selectedAssetIds : originAssetId is { } id ? [id] : [];
}

internal static class BrowserCollectionActivation
{
    public static bool IsInteractive(BrowserCollectionNode node, BrowserCollectionNode? pointerTarget,
        bool keyboardSelectionPending) => ReferenceEquals(node, pointerTarget) || keyboardSelectionPending;

    public static bool ShouldIgnoreDelayedReveal(BrowserCollectionNode node, BrowserCollectionNode? revealedNode,
        bool interactive) => ReferenceEquals(node, revealedNode) && !interactive;
}

internal static class CollectionMembershipFeedback
{
    public static string ForAdd(int added, int duplicates, int assetCount, int collectionCount, string? collectionName)
    {
        if (added == 0)
            return collectionCount == 1 && collectionName is not null
                ? $"{assetCount} media item{(assetCount == 1 ? " is" : "s are")} already in {collectionName}"
                : $"{duplicates} media item{(duplicates == 1 ? " was" : "s were")} already present";
        var result = collectionCount == 1 && collectionName is not null
            ? $"Added {added} media item{(added == 1 ? "" : "s")} to {collectionName}"
            : $"Added {added} media items to {collectionCount} Collections";
        return duplicates > 0 ? $"{result} • {duplicates} media item{(duplicates == 1 ? " was" : "s were")} already present" : result;
    }
}

internal static class BrowserCollectionMembershipInteraction
{
    public static bool CanDrop(BrowserAssetDragPayload? payload, BrowserCollectionNode? target) =>
        payload is { AssetIds.Count: > 0 } && target?.IsCollection == true;

    public static IReadOnlyList<Guid> MoveBefore(IReadOnlyList<Guid> current, IReadOnlyList<Guid> moving, Guid target)
    {
        var movingSet = moving.ToHashSet();
        var remaining = current.Where(id => !movingSet.Contains(id)).ToList();
        var targetIndex = remaining.IndexOf(target);
        if (targetIndex < 0) return current;
        remaining.InsertRange(targetIndex, moving.Where(current.Contains));
        return remaining;
    }
}

internal sealed class BrowserCollectionNode : INotifyPropertyChanged
{
    private bool _isExpanded;
    private bool _isSelected;
    private bool _isAssetDropTarget;

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
    public bool IsAssetDropTarget
    {
        get => _isAssetDropTarget;
        set { if (_isAssetDropTarget == value) return; _isAssetDropTarget = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new(name));
}

internal sealed record CollectionSetPlacementOption(Guid? CollectionSetId, string DisplayName);

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
        var output = new List<CollectionSetPlacementOption> { new(null, "Top level") };
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

internal enum BrowserScopeSelectionKind { None, Folder, Collection }

internal sealed class BrowserScopeSelection
{
    public BrowserScopeSelectionKind Active { get; private set; }
    public void ActivateFolder() => Active = BrowserScopeSelectionKind.Folder;
    public void ActivateCollection() => Active = BrowserScopeSelectionKind.Collection;

    public bool ShouldActivateFolder(BrowserTreeNode node, BrowserTreeNode? pointerTarget,
        bool folderTreeHasKeyboardFocus, BrowserTreeNode? passiveRevealTarget)
    {
        var interactive = ReferenceEquals(node, pointerTarget) || folderTreeHasKeyboardFocus;
        if (Active == BrowserScopeSelectionKind.Collection && !interactive) return false;
        return interactive || !ReferenceEquals(node, passiveRevealTarget);
    }
}

internal enum BrowserCollectionDropKind { None, InsertBefore, InsertAfter, IntoSet }
internal sealed record BrowserCollectionDrop(BrowserCollectionDropKind Kind, BrowserCollectionNode Target);
internal sealed record BrowserCollectionInsertionDestination(Guid? ParentSetId, int Ordinal);
internal sealed record BrowserCollectionInsertionChoice(BrowserCollectionNode Target, BrowserCollectionDropKind Kind,
    BrowserCollectionInsertionDestination Destination);

internal sealed class BrowserCollectionDragHover
{
    public static readonly TimeSpan Dwell = TimeSpan.FromMilliseconds(1350);
    private BrowserCollectionNode? _dragged;
    private BrowserCollectionNode? _target;
    private DateTimeOffset _started;

    public BrowserCollectionNode? PendingTarget => _target;

    public bool Track(BrowserCollectionNode? dragged, BrowserCollectionNode? target,
        BrowserCollectionDropKind kind, DateTimeOffset now)
    {
        var eligible = dragged is not null && target is { IsSet: true, IsExpanded: false } &&
            kind == BrowserCollectionDropKind.IntoSet && BrowserCollectionInteraction.CanDrop(dragged, target);
        if (!eligible)
        {
            Reset();
            return false;
        }
        if (ReferenceEquals(_dragged, dragged) && ReferenceEquals(_target, target)) return false;
        _dragged = dragged;
        _target = target;
        _started = now;
        return true;
    }

    public BrowserCollectionNode? TakeReady(DateTimeOffset now)
    {
        if (_dragged is null || _target is null || now - _started < Dwell) return null;
        var target = _target;
        var valid = !target.IsExpanded && BrowserCollectionInteraction.CanDrop(_dragged, target);
        Reset();
        return valid ? target : null;
    }

    public void Reset()
    {
        _dragged = null;
        _target = null;
        _started = default;
    }
}

internal sealed class BrowserCollectionDragSession
{
    public BrowserCollectionNode? Payload { get; private set; }

    public void Begin(BrowserCollectionNode payload) => Payload = payload;
    public void End() => Payload = null;

    public bool RouteWheel(bool pointerOverSidebar, Func<bool> scroll, Action<BrowserCollectionNode> retarget)
    {
        var payload = Payload;
        if (payload is null || !pointerOverSidebar || !scroll()) return false;
        retarget(payload);
        return true;
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


    public static BrowserCollectionDrop DropAt(BrowserCollectionNode dragged, BrowserCollectionNode target,
        double relativeY)
    {
        if (ReferenceEquals(dragged, target)) return new(BrowserCollectionDropKind.None, target);
        if (CanInsertBeside(dragged, target) && relativeY < 0.25)
            return new(BrowserCollectionDropKind.InsertBefore, target);
        if (CanInsertBeside(dragged, target) && relativeY > 0.75)
            return new(BrowserCollectionDropKind.InsertAfter, target);
        if (target.IsSet && CanDrop(dragged, target) && relativeY is >= 0.25 and <= 0.75)
            return new(BrowserCollectionDropKind.IntoSet, target);
        if (CanInsertBeside(dragged, target))
            return new(relativeY < 0.5 ? BrowserCollectionDropKind.InsertBefore : BrowserCollectionDropKind.InsertAfter, target);
        return new(BrowserCollectionDropKind.None, target);
    }

    private static bool CanInsertBeside(BrowserCollectionNode dragged, BrowserCollectionNode target)
    {
        if (!dragged.IsSet || target.ParentSetId is not { } parent) return true;
        return dragged.Id != parent && !BrowserCollectionTreeModel.Flatten(dragged.Children).Any(node => node.Id == parent);
    }

    public static BrowserCollectionNode[] OrderByName(IEnumerable<BrowserCollectionNode> nodes, bool descending) =>
        (descending ? nodes.OrderByDescending(node => node.Name, StringComparer.CurrentCultureIgnoreCase)
            : nodes.OrderBy(node => node.Name, StringComparer.CurrentCultureIgnoreCase)).ToArray();

    public static BrowserCollectionInsertionDestination? ResolveInsertion(
        IEnumerable<BrowserCollectionNode> roots, BrowserCollectionNode dragged,
        BrowserCollectionNode target, BrowserCollectionDropKind kind)
    {
        if (kind is not (BrowserCollectionDropKind.InsertBefore or BrowserCollectionDropKind.InsertAfter)) return null;

        var all = BrowserCollectionTreeModel.Flatten(roots).ToArray();
        BrowserCollectionNode anchor = target;
        Guid? destinationParent;
        var beforeAnchor = kind == BrowserCollectionDropKind.InsertBefore;

        if (kind == BrowserCollectionDropKind.InsertAfter && target is { IsSet: true, IsExpanded: true } &&
            target.Children.Count > 0)
        {
            destinationParent = target.Id;
            anchor = target.Children[0];
            beforeAnchor = true;
        }
        else
        {
            destinationParent = target.ParentSetId;
        }

        if (dragged.IsSet && (destinationParent == dragged.Id ||
            BrowserCollectionTreeModel.Flatten(dragged.Children).Any(node => node.Id == destinationParent))) return null;

        var siblings = (destinationParent is { } destinationParentId
                ? all.Single(node => node.IsSet && node.Id == destinationParentId).Children
                : roots)
            .Where(node => node.Id != dragged.Id)
            .OrderBy(node => node.Ordinal)
            .ToList();
        var anchorIndex = siblings.FindIndex(node => node.Id == anchor.Id);
        if (anchorIndex < 0) return null;
        return new(destinationParent, anchorIndex + (beforeAnchor ? 0 : 1));
    }

    public static IReadOnlyList<BrowserCollectionInsertionChoice> ResolveInsertionChoices(
        IEnumerable<BrowserCollectionNode> roots, BrowserCollectionNode dragged,
        BrowserCollectionNode target, BrowserCollectionDropKind kind)
    {
        if (kind is not (BrowserCollectionDropKind.InsertBefore or BrowserCollectionDropKind.InsertAfter)) return [];
        var rootList = roots.ToArray();
        var visible = VisibleNodes(rootList).Where(node => !ReferenceEquals(node, dragged)).ToArray();
        var targetIndex = Array.IndexOf(visible, target);
        if (targetIndex < 0) return [];
        var boundary = targetIndex + (kind == BrowserCollectionDropKind.InsertAfter ? 1 : 0);
        var candidates = new List<BrowserCollectionInsertionChoice>(2);
        Add(boundary > 0 ? visible[boundary - 1] : null, BrowserCollectionDropKind.InsertAfter);
        Add(boundary < visible.Length ? visible[boundary] : null, BrowserCollectionDropKind.InsertBefore);
        return candidates;

        void Add(BrowserCollectionNode? candidateTarget, BrowserCollectionDropKind candidateKind)
        {
            if (candidateTarget is null || ReferenceEquals(candidateTarget, dragged)) return;
            if (ResolveInsertion(rootList, dragged, candidateTarget, candidateKind) is not { } destination) return;
            var duplicate = candidates.FindIndex(candidate => candidate.Destination == destination);
            var choice = new BrowserCollectionInsertionChoice(candidateTarget, candidateKind, destination);
            if (duplicate >= 0) candidates[duplicate] = choice;
            else candidates.Add(choice);
        }
    }

    public static IReadOnlyList<BrowserCollectionInsertionChoice> ResolveTrailingInsertionChoices(
        IEnumerable<BrowserCollectionNode> roots, BrowserCollectionNode dragged)
    {
        var rootList = roots.ToArray();
        var all = BrowserCollectionTreeModel.Flatten(rootList).ToArray();
        var draggedSubtree = BrowserCollectionTreeModel.Flatten([dragged]).Select(node => node.Id).ToHashSet();
        var cursor = VisibleNodes(rootList).LastOrDefault(node => !draggedSubtree.Contains(node.Id));
        if (cursor is null) return [];

        var choices = new List<BrowserCollectionInsertionChoice>();
        if (cursor is { IsSet: true, IsExpanded: true } && IsValidParent(cursor.Id))
        {
            var childOrdinal = cursor.Children.Count(node => node.Id != dragged.Id);
            choices.Add(new BrowserCollectionInsertionChoice(cursor, BrowserCollectionDropKind.InsertAfter,
                new BrowserCollectionInsertionDestination(cursor.Id, childOrdinal)));
        }

        while (true)
        {
            var parentId = cursor.ParentSetId;
            var siblings = parentId is { } id
                ? all.Single(node => node.IsSet && node.Id == id).Children.AsEnumerable()
                : rootList.AsEnumerable();
            var destination = new BrowserCollectionInsertionDestination(parentId,
                siblings.Count(node => node.Id != dragged.Id));
            if (IsValidParent(parentId))
                choices.Add(new BrowserCollectionInsertionChoice(cursor, BrowserCollectionDropKind.InsertAfter,
                    destination));
            if (parentId is not { } parentSetId) break;
            cursor = all.Single(node => node.IsSet && node.Id == parentSetId);
        }
        return choices;

        bool IsValidParent(Guid? parentId) => !dragged.IsSet ||
            (parentId != dragged.Id && !BrowserCollectionTreeModel.Flatten(dragged.Children)
                .Any(node => node.Id == parentId));
    }

    public static BrowserCollectionInsertionChoice? SelectTrailingInsertionChoice(
        IEnumerable<(BrowserCollectionInsertionChoice Choice, double Indent)> candidates, double pointerX)
    {
        var ordered = candidates.OrderBy(candidate => candidate.Indent).ToArray();
        if (ordered.Length == 0) return null;
        for (var index = 0; index < ordered.Length - 1; index++)
        {
            var boundary = (ordered[index].Indent + ordered[index + 1].Indent) / 2;
            if (pointerX < boundary) return ordered[index].Choice;
        }
        return ordered[^1].Choice;
    }

    private static IEnumerable<BrowserCollectionNode> VisibleNodes(IEnumerable<BrowserCollectionNode> nodes)
    {
        foreach (var node in nodes.OrderBy(node => node.Ordinal))
        {
            yield return node;
            if (!node.IsSet || !node.IsExpanded) continue;
            foreach (var child in VisibleNodes(node.Children)) yield return child;
        }
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

        foreach (var setNode in nodes.Values)
        {
            var ordered = setNode.Children.OrderBy(node => node.Ordinal).ThenBy(node => node.Kind).ThenBy(node => node.Id).ToArray();
            setNode.Children.Clear();
            foreach (var child in ordered) setNode.Children.Add(child);
        }

        Roots.Clear();
        var rootNodes = sets.Where(set => set.ParentCollectionSetId is null).Select(set => nodes[set.CollectionSetId])
            .Concat(collections.Where(collection => collection.ParentCollectionSetId is null)
                .Select(collection => new BrowserCollectionNode(collection)))
            .OrderBy(node => node.Ordinal).ThenBy(node => node.Kind).ThenBy(node => node.Id);
        foreach (var node in rootNodes)
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
