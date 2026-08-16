using LightflowStudio;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class BrowserTreeTests
{
    [Fact]
    public void Synchronize_ExpandsNestedHierarchySelectsFolderAndReturnsFilesOnly()
    {
        var rootId = Guid.NewGuid();
        var model = Model(rootId);
        var state = State(rootId, "Photos/Trips", Directory(rootId, "Photos/Trips/Day 1"),
            File(rootId, "Photos/Trips/clip.mp4"));

        var files = model.Synchronize(state);

        Assert.Equal(@"C:\Photos\Trips", model.SelectedNode!.AbsolutePath);
        Assert.True(model.Roots[0].IsExpanded);
        Assert.True(Find(model, @"C:\Photos")!.IsExpanded);
        Assert.Contains(Find(model, @"C:\Photos\Trips")!.Children,
            child => child.AbsolutePath == @"C:\Photos\Trips\Day 1");
        Assert.Single(files);
        Assert.False(files[0].IsDirectory);
    }

    [Fact]
    public void BackForwardUpStatesKeepTreeSelectionSynchronized()
    {
        var rootId = Guid.NewGuid();
        var model = Model(rootId);

        model.Synchronize(State(rootId, "Photos/Trips", File(rootId, "Photos/Trips/a.mp4")));
        Assert.Equal(@"C:\Photos\Trips", model.SelectedNode!.AbsolutePath);

        model.Synchronize(State(rootId, "Photos", Directory(rootId, "Photos/Trips")));
        Assert.Equal(@"C:\Photos", model.SelectedNode!.AbsolutePath);

        model.Synchronize(State(rootId, "Photos/Trips", File(rootId, "Photos/Trips/a.mp4")));
        Assert.Equal(@"C:\Photos\Trips", model.SelectedNode!.AbsolutePath);
    }

    [Fact]
    public void DirectPathStateCreatesAndSelectsMissingAncestorHierarchy()
    {
        var rootId = Guid.NewGuid();
        var model = Model(rootId);

        model.Synchronize(State(rootId, "Media/2026/Session", File(rootId, "Media/2026/Session/a.mp4")));

        Assert.Equal(@"C:\Media\2026\Session", model.SelectedNode!.AbsolutePath);
        Assert.True(Find(model, @"C:\Media")!.IsExpanded);
        Assert.True(Find(model, @"C:\Media\2026")!.IsExpanded);
    }

    [Fact]
    public void ManagedAncestorIsPreferredForItsLogicalRootIdentity()
    {
        var rootId = Guid.NewGuid();
        var model = new BrowserTreeModel();
        model.SetStorageEntries([
            new("volume:C", "C:", @"C:\", BrowserStorageKind.Volume, MediaRootAvailability.Online),
            new($"root:{rootId}", "Photos", @"C:\Libraries\Photos", BrowserStorageKind.ManagedRoot,
                MediaRootAvailability.Online, rootId)
        ]);

        model.Synchronize(new(new(rootId, "Photos", @"C:\Libraries\Photos", "Trips"),
            BrowserFolderStatus.Ready, [], null, false, false, true));

        Assert.Equal(@"C:\Libraries\Photos\Trips", model.SelectedNode!.AbsolutePath);
        Assert.Equal("Photos", model.Roots[1].DisplayName);
        Assert.True(model.Roots[1].IsExpanded);
    }

    [Fact]
    public void DirectUncPathAddsItsLogicalShareToTheHierarchy()
    {
        var rootId = Guid.NewGuid();
        var model = new BrowserTreeModel();

        model.Synchronize(new(new(rootId, "Media Share", @"\\server\media", "Projects/Show"),
            BrowserFolderStatus.Ready, [], null, false, false, true));

        var root = Assert.Single(model.Roots);
        Assert.Equal(@"\\server\media", root.AbsolutePath);
        Assert.Equal(@"\\server\media\Projects\Show", model.SelectedNode!.AbsolutePath);
        Assert.True(root.IsExpanded);
    }

    private static BrowserTreeModel Model(Guid rootId)
    {
        var model = new BrowserTreeModel();
        model.SetStorageEntries([
            new("volume:C", "Local Disk (C:)", @"C:\", BrowserStorageKind.Volume,
                MediaRootAvailability.Online, rootId)
        ]);
        return model;
    }

    private static BrowserFolderState State(Guid rootId, string relative, params MediaFolderEntry[] entries) =>
        new(new(rootId, "Local Disk", @"C:\", relative), BrowserFolderStatus.Ready, entries,
            null, true, true, true);

    private static MediaFolderEntry Directory(Guid rootId, string path) =>
        new(rootId, path, path.ToUpperInvariant(), Path.GetFileName(path), true,
            MediaTypeClassification.Unknown, null, DateTimeOffset.UtcNow);

    private static MediaFolderEntry File(Guid rootId, string path) =>
        new(rootId, path, path.ToUpperInvariant(), Path.GetFileName(path), false,
            new(MediaTypeCategory.Video, "mp4"), 10, DateTimeOffset.UtcNow);

    private static BrowserTreeNode? Find(BrowserTreeModel model, string path) =>
        Descendants(model.Roots).FirstOrDefault(node =>
            string.Equals(node.AbsolutePath, path, StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<BrowserTreeNode> Descendants(IEnumerable<BrowserTreeNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in Descendants(node.Children)) yield return child;
        }
    }
}
