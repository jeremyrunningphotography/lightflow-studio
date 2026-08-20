using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

namespace LightflowStudio;

internal sealed class BrowserTreeNode : INotifyPropertyChanged
{
    private bool _isExpanded;
    private bool _isSelected;
    private bool _isRecursiveScope;

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
    /// #124 (revised): this node's root-agnostic Catalog identity — null only for storage rows that are not a
    /// Media Root (e.g. a bare Volume) and therefore can never be a recursive-root scope. Set once, at the
    /// same point the node's <see cref="AbsolutePath"/> identity is established, via <see cref="SetIdentity"/>.
    /// </summary>
    public Guid? RootId { get; private set; }

    /// <summary>The folder's path relative to <see cref="RootId"/>, in the same normalized form as <see cref="BrowserRecursiveRoot.RelativeFolder"/> — "" for the Media Root's own top level.</summary>
    public string? RelativeFolder { get; private set; }

    /// <summary>
    /// #124 (revised): whether selecting this folder would browse it and all of its descendants — driven
    /// entirely by <see cref="RootId"/>/<see cref="RelativeFolder"/> against the Catalog's stored recursive
    /// roots (see <c>MainWindow.SyncBrowserTreeRecursiveIcon</c>). Deliberately independent of
    /// <see cref="IsSelected"/>: a folder can be the recursive scope without being the selected row, and vice
    /// versa.
    /// </summary>
    public bool IsRecursiveScope
    {
        get => _isRecursiveScope;
        set
        {
            if (_isRecursiveScope == value) return;
            _isRecursiveScope = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsFilledFolderIcon));
        }
    }

    /// <summary>
    /// #124 (further revised): the single authoritative "should this row's folder icon read as filled" answer
    /// — a selected direct folder, or effectively recursive (the stored root or an inherited descendant) —
    /// combining <see cref="IsSelected"/> and <see cref="IsRecursiveScope"/> in exactly one place so the tree
    /// row's icon can never depend on trigger-precedence ordering between two independently-firing XAML
    /// <c>DataTrigger</c>s. Never persisted; always recomputed live from the two underlying inputs.
    /// </summary>
    public bool IsFilledFolderIcon => _isSelected || _isRecursiveScope;

    internal void SetIdentity(Guid rootId, string relativeFolder)
    {
        RootId = rootId;
        RelativeFolder = relativeFolder;
    }

    /// <summary>
    /// True once <see cref="Children"/> reflects a real folder enumeration for this node rather than only a
    /// lazy-load placeholder or a synthetic single-child stub created while materializing an ancestor chain
    /// toward a not-yet-visited deep folder. Sibling folders are only trustworthy once this is true.
    /// </summary>
    public bool IsMaterialized { get; set; }

    /// <summary>
    /// #124: whether this folder has at least one immediate child folder — the "does Include Subfolders make
    /// sense here" question, immediate-child-existence only, never descendant media. <see langword="null"/>
    /// (unknown) while <see cref="Children"/> is still just the one lazy-load placeholder; otherwise a real
    /// answer from an actual folder enumeration, with no separate filesystem probe of its own. For the
    /// currently OPEN folder specifically, this becomes known "for free": <see cref="BrowserTreeModel.Synchronize"/>
    /// already populates its direct children from the same <c>BrowserFolderState.Entries</c> every navigation
    /// already fetches — this property just reads that existing data rather than duplicating discovery.
    /// </summary>
    public bool? HasSubfolders => Children.Count == 1 && Children[0].IsPlaceholder ? null : Children.Count > 0;
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
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsFilledFolderIcon));
        }
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
                if (entry.RootId is { } entryRootId) node.SetIdentity(entryRootId, "");
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

        var current = EnsurePath(root, state.Location.AbsolutePath, state.Location.RootId, state.Location.RootPath);
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
        var chain = EnsurePathChain(root, location.AbsolutePath, location.RootId, location.RootPath);
        return chain.Take(chain.Count - 1).Where(node => !node.IsMaterialized).ToArray();
    }

    /// <summary>Backfills a tree node's real children (siblings) from an actual folder enumeration.</summary>
    public void ApplyDirectoryListing(BrowserTreeNode node, string rootPath, IReadOnlyList<MediaFolderEntry> entries) =>
        ReplaceDirectories(node, rootPath, entries);

    public void RequestSelection(BrowserTreeNode node)
    {
        if (node.IsPlaceholder || !Roots.Any(root => ContainsNode(root, node))) return;
        Select(node);
    }

    public BrowserTreeNode? RequestSelection(BrowserLocation? location)
    {
        if (location is null) return null;
        var root = FindRoot(location) ?? AddCurrentRoot(location);
        var node = EnsurePath(root, location.AbsolutePath, location.RootId, location.RootPath);
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
        var node = EnsurePath(root, absolutePath, root.RootId, root.AbsolutePath);
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
        Select(EnsurePath(root, location.AbsolutePath, location.RootId, location.RootPath));
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
        node.SetIdentity(location.RootId, "");
        node.Children.Add(NewPlaceholder());
        Roots.Add(node);
        return node;
    }

    private static BrowserTreeNode EnsurePath(BrowserTreeNode root, string targetPath, Guid? rootId, string? rootBasePath) =>
        EnsurePathChain(root, targetPath, rootId, rootBasePath)[^1];

    /// <summary>
    /// Walks/creates the node chain from <paramref name="root"/> down to <paramref name="targetPath"/>,
    /// inclusive of both ends. Intermediate nodes created along the way are structural placeholders only —
    /// each has exactly the one child leading toward the target, never a real listing of that ancestor's
    /// other children — callers that need real sibling folders must materialize those ancestors separately
    /// via <see cref="ApplyDirectoryListing"/> (see <see cref="GetUnmaterializedAncestors"/>).
    /// </summary>
    /// <remarks>
    /// #124: <paramref name="rootId"/>/<paramref name="rootBasePath"/> carry the confirmed Catalog identity for
    /// the target's Media Root, known from a resolved <see cref="BrowserLocation"/> at every call site except
    /// <see cref="RequestSelection(string)"/> (which has no such location and passes <paramref name="root"/>'s
    /// own identity/path, reproducing the old root-relative behavior exactly). Previously this method only
    /// stamped identity on newly-created nodes, sourced from <c>root.RootId</c>/<c>root</c>-relative paths —
    /// wrong whenever <paramref name="root"/> is a generic, unanchored ancestor row (e.g. a bare drive/volume
    /// with no Catalog <c>RootId</c> of its own) sitting above the real Media Root, which left every synthetic
    /// node in the chain — including the selected target itself — permanently stuck with <c>RootId = null</c>
    /// the first time a not-yet-materialized location was reached this way (startup restoration and
    /// direct-path entry, both of which build this synthetic chain before any real enumeration). Reused nodes
    /// were never corrected afterward either, since <see cref="ReplaceDirectories"/> only calls
    /// <c>SetIdentity</c> for a node it creates fresh. That stale identity was invisible while the node stayed
    /// selected (<c>IsFilledFolderIcon</c> is true from <c>IsSelected</c> alone) but broke the moment selection
    /// moved elsewhere, and could never self-heal even after a later Include Subfolders change, since
    /// <c>SyncBrowserTreeRecursiveIcon</c> silently skips any node whose <c>RootId</c> is null. Now every node
    /// in the chain — new or reused — has its identity (re)confirmed unconditionally against the authoritative
    /// source, and only once its own path actually reaches or descends into <paramref name="rootBasePath"/>,
    /// so a generic ancestor row above the real Media Root is never mislabeled as that root itself.
    /// </remarks>
    private static IReadOnlyList<BrowserTreeNode> EnsurePathChain(BrowserTreeNode root, string targetPath, Guid? rootId, string? rootBasePath)
    {
        var rootPath = MediaPathSemantics.NormalizeRootPath(root.AbsolutePath!);
        ConfirmIdentity(root, rootPath, rootId, rootBasePath);

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
            ConfirmIdentity(child, currentPath, rootId, rootBasePath);
            chain.Add(child);
            current = child;
        }
        return chain;
    }

    /// <summary>
    /// Stamps <paramref name="node"/>'s Catalog identity from the authoritative <paramref name="rootId"/>/
    /// <paramref name="rootBasePath"/> — unconditionally, even if <paramref name="node"/> already carries an
    /// identity, so a previously-null or stale value from an earlier incomplete pass is always corrected. A
    /// no-op when no authoritative root was supplied, or when <paramref name="nodePath"/> sits above/outside
    /// <paramref name="rootBasePath"/> (an ancestor row — e.g. a bare drive/volume — is never mislabeled as
    /// the Media Root nested beneath it).
    /// </summary>
    private static void ConfirmIdentity(BrowserTreeNode node, string nodePath, Guid? rootId, string? rootBasePath)
    {
        if (rootId is not { } confirmedRootId || rootBasePath is null) return;
        if (!MediaPathSemantics.Contains(rootBasePath, nodePath)) return;
        node.SetIdentity(confirmedRootId, MediaPathSemantics.RelativeFolder(rootBasePath, nodePath));
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
                child.SetIdentity(entry.RootId, entry.RelativePath);
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
