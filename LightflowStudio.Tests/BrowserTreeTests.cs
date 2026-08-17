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
        Assert.Single(Descendants(model.Roots), node => node.IsSelected);
        Assert.All(Descendants(model.Roots).Where(node => !ReferenceEquals(node, model.SelectedNode)),
            node => Assert.False(node.IsSelected));
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

    [Fact]
    public void IndependentExpandedBranchesSurviveSelectionAndAncestorNavigation()
    {
        var rootId = Guid.NewGuid();
        var model = Model(rootId);
        model.Synchronize(State(rootId, "", Directory(rootId, "A"), Directory(rootId, "B")));
        var branchA = Find(model, @"C:\A")!;
        var branchB = Find(model, @"C:\B")!;

        model.Synchronize(State(rootId, "A", Directory(rootId, "A/Child")));
        model.Synchronize(State(rootId, "B", Directory(rootId, "B/Child")));
        branchA.IsExpanded = true;
        branchB.IsExpanded = true;
        var childA = Find(model, @"C:\A\Child")!;
        var childB = Find(model, @"C:\B\Child")!;
        childA.IsExpanded = true;
        childB.IsExpanded = true;
        var branchCollectionActions = new List<System.Collections.Specialized.NotifyCollectionChangedAction>();
        model.Roots[0].Children.CollectionChanged += (_, args) => branchCollectionActions.Add(args.Action);

        model.Synchronize(State(rootId, "A/Child", File(rootId, "A/Child/a.mp4")));
        model.Synchronize(State(rootId, "", Directory(rootId, "A"), Directory(rootId, "B")));

        Assert.True(branchA.IsExpanded);
        Assert.True(branchB.IsExpanded);
        Assert.True(childA.IsExpanded);
        Assert.True(childB.IsExpanded);
        Assert.Same(model.Roots[0], model.SelectedNode);
        Assert.Single(Descendants(model.Roots), node => node.IsSelected);
        Assert.DoesNotContain(System.Collections.Specialized.NotifyCollectionChangedAction.Reset,
            branchCollectionActions);
    }

    [Fact]
    public void HistoryAndDirectPathExpandNeededAncestorsWithoutCollapsingOtherBranches()
    {
        var rootId = Guid.NewGuid();
        var model = Model(rootId);
        model.Synchronize(State(rootId, "", Directory(rootId, "A"), Directory(rootId, "B")));
        model.Synchronize(State(rootId, "B", Directory(rootId, "B/Deep")));
        var branchB = Find(model, @"C:\B")!;
        var deepB = Find(model, @"C:\B\Deep")!;
        branchB.IsExpanded = true;
        deepB.IsExpanded = true;

        model.Synchronize(State(rootId, "A/Direct/Path", File(rootId, "A/Direct/Path/a.mp4")));
        Assert.True(branchB.IsExpanded);
        Assert.True(deepB.IsExpanded);
        Assert.True(Find(model, @"C:\A")!.IsExpanded);
        Assert.True(Find(model, @"C:\A\Direct")!.IsExpanded);

        model.Synchronize(State(rootId, "A/Direct", Directory(rootId, "A/Direct/Path")));
        model.Synchronize(State(rootId, "A/Direct/Path", File(rootId, "A/Direct/Path/a.mp4")));
        Assert.True(branchB.IsExpanded);
        Assert.True(deepB.IsExpanded);
        Assert.Single(Descendants(model.Roots), node => node.IsSelected);
    }

    [Fact]
    public void StorageRefreshReusesExpandedNodesWithStableIdentity()
    {
        var rootId = Guid.NewGuid();
        var model = Model(rootId);
        model.Synchronize(State(rootId, "Photos", Directory(rootId, "Photos/Trips")));
        var root = model.Roots[0];
        var photos = Find(model, @"C:\Photos")!;
        root.IsExpanded = true;
        photos.IsExpanded = true;
        var collectionActions = new List<System.Collections.Specialized.NotifyCollectionChangedAction>();
        model.Roots.CollectionChanged += (_, args) => collectionActions.Add(args.Action);

        model.SetStorageEntries([
            new("volume:C", "Renamed Disk (C:)", @"C:\", BrowserStorageKind.Volume,
                MediaRootAvailability.Online, rootId)
        ]);

        Assert.Same(root, model.Roots[0]);
        Assert.Same(photos, Find(model, @"C:\Photos"));
        Assert.True(root.IsExpanded);
        Assert.True(photos.IsExpanded);
        Assert.Equal("Renamed Disk (C:)", root.DisplayName);
        Assert.DoesNotContain(System.Collections.Specialized.NotifyCollectionChangedAction.Reset, collectionActions);
    }

    [Fact]
    public void RequestedSelectionChangesImmediatelyWithoutMutatingTreeLayoutOrLoadedFiles()
    {
        var rootId = Guid.NewGuid();
        var model = Model(rootId);
        var loadedFiles = model.Synchronize(State(rootId, "", Directory(rootId, "A"),
            File(rootId, "loaded.mp4")));
        var root = model.Roots[0];
        var target = Find(model, @"C:\A")!;
        root.IsExpanded = true;
        var collectionChanges = 0;
        root.Children.CollectionChanged += (_, _) => collectionChanges++;
        var selectionChanges = 0;
        target.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(BrowserTreeNode.IsSelected)) selectionChanges++;
        };

        model.RequestSelection(target);

        Assert.Same(target, model.SelectedNode);
        Assert.True(target.IsSelected);
        Assert.False(root.IsSelected);
        Assert.True(root.IsExpanded);
        Assert.Equal(0, collectionChanges);
        Assert.Equal("loaded.mp4", Assert.Single(loadedFiles).Name);
        Assert.Equal(1, selectionChanges);

        model.Synchronize(State(rootId, "A", File(rootId, "A/new.mp4")));
        Assert.Same(target, model.SelectedNode);
        Assert.Equal(1, selectionChanges);
    }

    [Fact]
    public void RapidRequestsAndFailureRestorationKeepOneDeliberateSelection()
    {
        var rootId = Guid.NewGuid();
        var model = Model(rootId);
        var loaded = State(rootId, "", Directory(rootId, "A"), Directory(rootId, "B"));
        model.Synchronize(loaded);
        var root = model.Roots[0];
        var branchA = Find(model, @"C:\A")!;
        var branchB = Find(model, @"C:\B")!;

        model.RequestSelection(branchA);
        model.RequestSelection(branchB);

        Assert.Same(branchB, model.SelectedNode);
        Assert.False(branchA.IsSelected);
        Assert.Single(Descendants(model.Roots), node => node.IsSelected);

        model.RestoreSelection(loaded.Location);
        Assert.Same(root, model.SelectedNode);
        Assert.Single(Descendants(model.Roots), node => node.IsSelected);
    }

    [Theory]
    [InlineData(100, 200, 40, 28, 40)]
    [InlineData(100, 200, 350, 28, 178)]
    [InlineData(100, 200, 150, 28, 100)]
    public void ProgrammaticSelectionUsesMinimalVerticalReveal(double current, double viewport,
        double rowTop, double rowHeight, double expected)
    {
        Assert.Equal(expected,
            BrowserTreeScroll.RevealVerticalOffset(current, viewport, rowTop, rowHeight));
    }

    [Theory]
    [InlineData(160, 200, 120, 44, 120)] // Deep item clipped on the left while scrolled right.
    [InlineData(120, 200, 28, 44, 28)] // Up selects a less-indented parent.
    [InlineData(0, 100, 160, 44, 104)] // Direct path reveals a deeply nested target's leading area.
    [InlineData(100, 200, 150, 44, 100)] // Fully visible leading area does not move.
    public void ProgrammaticSelectionUsesMinimalHorizontalReveal(double current, double viewport,
        double rowLeft, double leadingAreaWidth, double expected)
    {
        Assert.Equal(expected,
            BrowserTreeScroll.RevealHorizontalOffset(current, viewport, rowLeft, leadingAreaWidth));
    }

    [Fact]
    public void DirectPathMaterializationExposesRealSiblingFoldersForEveryAncestor()
    {
        // Regression: direct-path navigation to a never-visited deep folder must not leave ancestors as a
        // synthetic single-child chain that hides their real sibling folders.
        var rootId = Guid.NewGuid();
        var model = Model(rootId);
        var state = State(rootId, "Media/2026/Session", File(rootId, "Media/2026/Session/a.mp4"));

        model.Synchronize(state);
        var pending = model.GetUnmaterializedAncestors(state.Location!);

        Assert.Equal([@"C:\", @"C:\Media", @"C:\Media\2026"], pending.Select(node => node.AbsolutePath));
        Assert.All(pending, node => Assert.False(node.IsMaterialized));

        // Simulate the real folder enumerations a materialization pass performs for each ancestor.
        model.ApplyDirectoryListing(pending[0], @"C:\",
            [Directory(rootId, "Media"), Directory(rootId, "OtherTopLevelFolder")]);
        model.ApplyDirectoryListing(pending[1], @"C:\",
            [Directory(rootId, "Media/2026"), Directory(rootId, "Media/2025")]);
        model.ApplyDirectoryListing(pending[2], @"C:\",
            [Directory(rootId, "Media/2026/Session"), Directory(rootId, "Media/2026/OtherSession")]);

        Assert.NotNull(Find(model, @"C:\OtherTopLevelFolder"));
        Assert.NotNull(Find(model, @"C:\Media\2025"));
        Assert.NotNull(Find(model, @"C:\Media\2026\OtherSession"));
        Assert.All(pending, node => Assert.True(node.IsMaterialized));
        Assert.Equal(@"C:\Media\2026\Session", model.SelectedNode!.AbsolutePath);
        Assert.True(model.SelectedNode!.IsSelected);
        Assert.Single(Descendants(model.Roots), node => node.IsSelected);
    }

    [Fact]
    public void AncestorsAlreadyMaterializedByOrdinaryNavigationNeedNoBackfill()
    {
        // The common case (Back/Forward, click-driven expansion, revisiting a folder) must stay a no-op:
        // every ancestor already has a real listing from its own earlier Synchronize call.
        var rootId = Guid.NewGuid();
        var model = Model(rootId);
        model.Synchronize(State(rootId, "", Directory(rootId, "Media")));
        model.Synchronize(State(rootId, "Media", Directory(rootId, "Media/2026")));
        model.Synchronize(State(rootId, "Media/2026", Directory(rootId, "Media/2026/Session")));
        var state = State(rootId, "Media/2026/Session", File(rootId, "Media/2026/Session/a.mp4"));
        model.Synchronize(state);

        var pending = model.GetUnmaterializedAncestors(state.Location!);

        Assert.Empty(pending);
    }

    [Fact]
    public void AncestorMaterializationPreservesUnrelatedExpandedBranchesAndSelection()
    {
        var rootId = Guid.NewGuid();
        var model = Model(rootId);
        model.Synchronize(State(rootId, "", Directory(rootId, "A"), Directory(rootId, "B")));
        var branchB = Find(model, @"C:\B")!;
        branchB.IsExpanded = true;

        var state = State(rootId, "A/Deep/Target", File(rootId, "A/Deep/Target/a.mp4"));
        model.Synchronize(state);
        var pending = model.GetUnmaterializedAncestors(state.Location!);
        Assert.Equal([@"C:\A", @"C:\A\Deep"], pending.Select(node => node.AbsolutePath));
        model.ApplyDirectoryListing(pending[0], @"C:\", [Directory(rootId, "A/Deep"), Directory(rootId, "A/Sibling")]);
        model.ApplyDirectoryListing(pending[1], @"C:\",
            [Directory(rootId, "A/Deep/Target"), Directory(rootId, "A/Deep/OtherTarget")]);

        Assert.True(branchB.IsExpanded);
        Assert.NotNull(Find(model, @"C:\A\Sibling"));
        Assert.NotNull(Find(model, @"C:\A\Deep\OtherTarget"));
        Assert.Equal(@"C:\A\Deep\Target", model.SelectedNode!.AbsolutePath);
        Assert.Single(Descendants(model.Roots), node => node.IsSelected);
    }

    [Fact]
    public void ProgrammaticPathSelectionExpandsAncestorsWithoutCollapsingOtherBranches()
    {
        var rootId = Guid.NewGuid();
        var model = Model(rootId);
        model.Synchronize(State(rootId, "", Directory(rootId, "A"), Directory(rootId, "B")));
        var branchB = Find(model, @"C:\B")!;
        branchB.IsExpanded = true;

        var selected = model.RequestSelection(@"C:\A\Deep\Target");

        Assert.NotNull(selected);
        Assert.True(selected.IsSelected);
        Assert.True(Find(model, @"C:\A")!.IsExpanded);
        Assert.True(Find(model, @"C:\A\Deep")!.IsExpanded);
        Assert.True(branchB.IsExpanded);
        Assert.Single(Descendants(model.Roots), node => node.IsSelected);
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
