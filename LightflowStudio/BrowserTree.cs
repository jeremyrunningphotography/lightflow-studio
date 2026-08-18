using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

namespace LightflowStudio;

internal sealed class BrowserTreeNode : INotifyPropertyChanged
{
    private bool _isExpanded;
    private bool _isSelected;

    public BrowserTreeNode(string displayName, string? absolutePath, BrowserStorageEntry? storage = null,
        bool placeholder = false)
    {
        DisplayName = displayName;
        AbsolutePath = absolutePath;
        Storage = storage;
        IsPlaceholder = placeholder;
    }

    public string DisplayName { get; private set; }
    public string? AbsolutePath { get; private set; }
    public BrowserStorageEntry? Storage { get; private set; }
    public bool IsPlaceholder { get; }
    public ObservableCollection<BrowserTreeNode> Children { get; } = [];

    /// <summary>
    /// True once <see cref="Children"/> reflects a real folder enumeration for this node rather than only a
    /// lazy-load placeholder or a synthetic single-child stub created while materializing an ancestor chain
    /// toward a not-yet-visited deep folder. Sibling folders are only trustworthy once this is true.
    /// </summary>
    public bool IsMaterialized { get; set; }
    public string? Diagnostic => Storage?.Diagnostic;
    public MediaRootAvailability Availability => Storage?.Availability ?? MediaRootAvailability.Online;
    public bool IsStorageLocation => Storage is not null;
    public string? StorageStatus => Storage is null ? null : $"{Storage.DisplayKind} · {Storage.Availability}";

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

    public void UpdateStorage(BrowserStorageEntry storage)
    {
        DisplayName = storage.DisplayName;
        AbsolutePath = storage.PhysicalPath;
        Storage = storage;
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(AbsolutePath));
        OnPropertyChanged(nameof(Storage));
        OnPropertyChanged(nameof(Diagnostic));
        OnPropertyChanged(nameof(Availability));
        OnPropertyChanged(nameof(IsStorageLocation));
        OnPropertyChanged(nameof(StorageStatus));
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>Maintains the filesystem hierarchy presented by the Browser independently of WPF.</summary>
internal sealed class BrowserTreeModel
{
    public ObservableCollection<BrowserTreeNode> Roots { get; } = [];
    public BrowserTreeNode? SelectedNode { get; private set; }

    public void SetStorageEntries(IEnumerable<BrowserStorageEntry> entries)
    {
        var existing = Roots.Where(node => node.Storage is not null)
            .ToDictionary(node => node.Storage!.Key, StringComparer.OrdinalIgnoreCase);
        var desired = new List<BrowserTreeNode>();
        foreach (var entry in entries)
        {
            BrowserTreeNode node;
            if (existing.TryGetValue(entry.Key, out var prior) && SamePath(prior.AbsolutePath, entry.PhysicalPath))
            {
                node = prior;
                node.UpdateStorage(entry);
            }
            else
            {
                node = new BrowserTreeNode(entry.DisplayName, entry.PhysicalPath, entry);
                if (entry.Availability == MediaRootAvailability.Online && entry.PhysicalPath is not null)
                    node.Children.Add(NewPlaceholder());
            }
            desired.Add(node);
        }

        Reconcile(Roots, desired);
        if (SelectedNode is not null && !Roots.Any(root => ContainsNode(root, SelectedNode)))
        {
            SelectedNode.IsSelected = false;
            SelectedNode = null;
        }
    }

    public IReadOnlyList<MediaFolderEntry> Synchronize(BrowserFolderState state)
    {
        if (state.Location is null)
            return state.Entries.Where(entry => !entry.IsDirectory).ToArray();

        var root = FindRoot(state.Location) ?? AddCurrentRoot(state.Location);

        var current = EnsurePath(root, state.Location.AbsolutePath);
        Select(current);
        ReplaceDirectories(current, state.Location.RootPath, state.Entries);
        return state.Entries.Where(entry => !entry.IsDirectory).ToArray();
    }

    /// <summary>
    /// Ancestor nodes from the root down to (but excluding) <paramref name="location"/> itself, in top-down
    /// order, that still need their real children loaded through a folder enumeration before their sibling
    /// folders can be trusted to be visible. Also walks/creates the identity chain toward the target exactly
    /// like <see cref="Synchronize"/> does, so the target node exists and is selectable immediately, even
    /// before any returned ancestor is materialized. Safe to call repeatedly/idempotently — already
    /// materialized or already-created nodes are reused rather than duplicated.
    /// </summary>
    public IReadOnlyList<BrowserTreeNode> GetUnmaterializedAncestors(BrowserLocation location)
    {
        var root = FindRoot(location) ?? AddCurrentRoot(location);
        var chain = EnsurePathChain(root, location.AbsolutePath);
        return chain.Take(chain.Count - 1).Where(node => !node.IsMaterialized).ToArray();
    }

    /// <summary>Backfills a tree node's real children (siblings) from an actual folder enumeration.</summary>
    public void ApplyDirectoryListing(BrowserTreeNode node, string rootPath, IReadOnlyList<MediaFolderEntry> entries) =>
        ReplaceDirectories(node, rootPath, entries);

    /// <summary>
    /// #124: the deepest node currently visible as a row underneath <paramref name="node"/> in the Locations
    /// tree's own display order — i.e. <paramref name="node"/> itself if it is collapsed or has no real
    /// children yet, otherwise the same recursive answer for its last non-placeholder child. A standard
    /// TreeView renders a node's children (in order) immediately after that node's own row and before its
    /// next sibling, so the last child's own subtree is always what renders last under a node — following
    /// "last real child, recursively" therefore always lands on the true bottom-most visible row. Used to
    /// bound the #124 recursive-scope outline honestly: a collapsed branch's hidden descendants are never
    /// counted, and nothing is force-expanded/materialized merely to compute this.
    /// </summary>
    public static BrowserTreeNode LastVisibleDescendant(BrowserTreeNode node)
    {
        if (!node.IsExpanded) return node;
        BrowserTreeNode? lastRealChild = null;
        foreach (var child in node.Children) if (!child.IsPlaceholder) lastRealChild = child;
        return lastRealChild is null ? node : LastVisibleDescendant(lastRealChild);
    }

    public void RequestSelection(BrowserTreeNode node)
    {
        if (node.IsPlaceholder || !Roots.Any(root => ContainsNode(root, node))) return;
        Select(node);
    }

    public BrowserTreeNode? RequestSelection(BrowserLocation? location)
    {
        if (location is null) return null;
        var root = FindRoot(location) ?? AddCurrentRoot(location);
        var node = EnsurePath(root, location.AbsolutePath);
        Select(node);
        return node;
    }

    public BrowserTreeNode? RequestSelection(string absolutePath)
    {
        var root = Roots.Where(node => node.AbsolutePath is not null &&
                MediaPathSemantics.Contains(node.AbsolutePath, absolutePath))
            .OrderByDescending(node => node.AbsolutePath!.Length)
            .FirstOrDefault();
        if (root is null) return null;
        var node = EnsurePath(root, absolutePath);
        Select(node);
        return node;
    }

    public void RestoreSelection(BrowserLocation? location)
    {
        if (location is null)
        {
            if (SelectedNode is not null) SelectedNode.IsSelected = false;
            SelectedNode = null;
            return;
        }
        var root = FindRoot(location) ?? AddCurrentRoot(location);
        Select(EnsurePath(root, location.AbsolutePath));
    }

    private BrowserTreeNode? FindRoot(BrowserLocation location)
    {
        var matchingIdentity = Roots.FirstOrDefault(node => node.Storage?.RootId == location.RootId);
        if (matchingIdentity is not null) return matchingIdentity;
        return Roots.Where(node => node.AbsolutePath is not null &&
                MediaPathSemantics.Contains(node.AbsolutePath, location.AbsolutePath))
            .OrderByDescending(node => node.AbsolutePath!.Length)
            .FirstOrDefault();
    }

    private BrowserTreeNode AddCurrentRoot(BrowserLocation location)
    {
        var storage = new BrowserStorageEntry($"root:{location.RootId}", location.RootName, location.RootPath,
            BrowserStorageKind.ManagedRoot, MediaRootAvailability.Online, location.RootId);
        var node = new BrowserTreeNode(location.RootName, location.RootPath, storage);
        node.Children.Add(NewPlaceholder());
        Roots.Add(node);
        return node;
    }

    private static BrowserTreeNode EnsurePath(BrowserTreeNode root, string targetPath) =>
        EnsurePathChain(root, targetPath)[^1];

    /// <summary>
    /// Walks/creates the node chain from <paramref name="root"/> down to <paramref name="targetPath"/>,
    /// inclusive of both ends. Intermediate nodes created along the way are structural placeholders only —
    /// each has exactly the one child leading toward the target, never a real listing of that ancestor's
    /// other children — callers that need real sibling folders must materialize those ancestors separately
    /// via <see cref="ApplyDirectoryListing"/> (see <see cref="GetUnmaterializedAncestors"/>).
    /// </summary>
    private static IReadOnlyList<BrowserTreeNode> EnsurePathChain(BrowserTreeNode root, string targetPath)
    {
        var rootPath = MediaPathSemantics.NormalizeRootPath(root.AbsolutePath!);
        var target = MediaPathSemantics.NormalizeRootPath(targetPath);
        if (string.Equals(rootPath, target, StringComparison.OrdinalIgnoreCase)) return [root];

        var relative = Path.GetRelativePath(rootPath, target);
        var chain = new List<BrowserTreeNode> { root };
        var current = root;
        var currentPath = rootPath;
        foreach (var segment in relative.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current.IsExpanded = true;
            currentPath = Path.Combine(currentPath, segment);
            var child = current.Children.FirstOrDefault(node => !node.IsPlaceholder &&
                string.Equals(node.AbsolutePath, currentPath, StringComparison.OrdinalIgnoreCase));
            if (child is null)
            {
                RemovePlaceholder(current);
                child = new BrowserTreeNode(segment, currentPath);
                child.Children.Add(NewPlaceholder());
                current.Children.Add(child);
            }
            chain.Add(child);
            current = child;
        }
        return chain;
    }

    private static void ReplaceDirectories(BrowserTreeNode current, string rootPath, IReadOnlyList<MediaFolderEntry> entries)
    {
        var existing = current.Children.Where(node => !node.IsPlaceholder && node.AbsolutePath is not null)
            .ToDictionary(node => node.AbsolutePath!, StringComparer.OrdinalIgnoreCase);
        var desired = new List<BrowserTreeNode>();
        foreach (var entry in entries.Where(entry => entry.IsDirectory))
        {
            var path = MediaPathSemantics.ResolveContained(rootPath, entry.RelativePath);
            if (!existing.TryGetValue(path, out var child))
            {
                child = new BrowserTreeNode(entry.Name, path);
                child.Children.Add(NewPlaceholder());
            }
            desired.Add(child);
        }
        Reconcile(current.Children, desired);
        current.IsMaterialized = true;
    }

    private void Select(BrowserTreeNode node)
    {
        if (SelectedNode is not null && !ReferenceEquals(SelectedNode, node)) SelectedNode.IsSelected = false;
        SelectedNode = node;
        node.IsSelected = true;
    }

    private static void RemovePlaceholder(BrowserTreeNode node)
    {
        foreach (var placeholder in node.Children.Where(child => child.IsPlaceholder).ToArray())
            node.Children.Remove(placeholder);
    }

    private static BrowserTreeNode NewPlaceholder() => new("Loading…", null, placeholder: true);

    private static bool SamePath(string? left, string? right) =>
        left is null || right is null ? left == right :
        string.Equals(MediaPathSemantics.NormalizeRootPath(left), MediaPathSemantics.NormalizeRootPath(right),
            StringComparison.OrdinalIgnoreCase);

    private static bool ContainsNode(BrowserTreeNode root, BrowserTreeNode target) =>
        ReferenceEquals(root, target) || root.Children.Any(child => ContainsNode(child, target));

    private static void Reconcile(ObservableCollection<BrowserTreeNode> target,
        IReadOnlyList<BrowserTreeNode> desired)
    {
        for (var index = 0; index < desired.Count; index++)
        {
            var currentIndex = target.IndexOf(desired[index]);
            if (currentIndex < 0) target.Insert(index, desired[index]);
            else if (currentIndex != index) target.Move(currentIndex, index);
        }
        while (target.Count > desired.Count) target.RemoveAt(target.Count - 1);
    }
}

internal static class BrowserTreeScroll
{
    public static double RevealVerticalOffset(double currentOffset, double viewportHeight, double rowTop,
        double rowHeight)
    {
        if (rowTop < currentOffset) return Math.Max(0, rowTop);
        var rowBottom = rowTop + rowHeight;
        return rowBottom > currentOffset + viewportHeight
            ? Math.Max(0, rowBottom - viewportHeight)
            : currentOffset;
    }

    public static double RevealHorizontalOffset(double currentOffset, double viewportWidth, double rowLeft,
        double leadingAreaWidth)
    {
        if (rowLeft < currentOffset) return Math.Max(0, rowLeft);
        var leadingAreaRight = rowLeft + leadingAreaWidth;
        return leadingAreaRight > currentOffset + viewportWidth
            ? Math.Max(0, leadingAreaRight - viewportWidth)
            : currentOffset;
    }
}
