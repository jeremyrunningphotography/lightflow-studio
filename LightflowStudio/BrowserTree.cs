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

    public string DisplayName { get; }
    public string? AbsolutePath { get; }
    public BrowserStorageEntry? Storage { get; }
    public bool IsPlaceholder { get; }
    public ObservableCollection<BrowserTreeNode> Children { get; } = [];
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
        Roots.Clear();
        SelectedNode = null;
        foreach (var entry in entries)
        {
            var node = new BrowserTreeNode(entry.DisplayName, entry.PhysicalPath, entry);
            if (entry.Availability == MediaRootAvailability.Online && entry.PhysicalPath is not null)
                node.Children.Add(NewPlaceholder());
            Roots.Add(node);
        }
    }

    public IReadOnlyList<MediaFolderEntry> Synchronize(BrowserFolderState state)
    {
        if (state.Location is null)
            return state.Entries.Where(entry => !entry.IsDirectory).ToArray();

        var root = FindRoot(state.Location) ?? AddCurrentRoot(state.Location);

        var current = EnsurePath(root, state.Location.AbsolutePath);
        Select(current);
        ReplaceDirectories(current, state);
        return state.Entries.Where(entry => !entry.IsDirectory).ToArray();
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

    private static BrowserTreeNode EnsurePath(BrowserTreeNode root, string targetPath)
    {
        var rootPath = MediaPathSemantics.NormalizeRootPath(root.AbsolutePath!);
        var target = MediaPathSemantics.NormalizeRootPath(targetPath);
        if (string.Equals(rootPath, target, StringComparison.OrdinalIgnoreCase)) return root;

        var relative = Path.GetRelativePath(rootPath, target);
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
            current = child;
        }
        return current;
    }

    private static void ReplaceDirectories(BrowserTreeNode current, BrowserFolderState state)
    {
        current.Children.Clear();
        if (state.Location is null) return;
        foreach (var entry in state.Entries.Where(entry => entry.IsDirectory))
        {
            var path = MediaPathSemantics.ResolveContained(state.Location.RootPath, entry.RelativePath);
            var child = new BrowserTreeNode(entry.Name, path);
            child.Children.Add(NewPlaceholder());
            current.Children.Add(child);
        }
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
}
