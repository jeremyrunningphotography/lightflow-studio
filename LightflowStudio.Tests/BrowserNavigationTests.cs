using LightflowStudio;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class BrowserNavigationTests
{
    [Fact]
    public async Task ZeroManagedRoots_LocalFolderCreatesVolumeAnchorAndReconcilesOnlyVisitedFolder()
    {
        var roots = new FakeRoots();
        var discovery = new FakeDiscovery();
        var folders = new FakeFolders((request, _) => Task.FromResult(Success(request,
            FileEntry(request.RootId, "Media/clip.mp4"))));
        using var session = Session(roots, discovery, folders);

        var result = await session.NavigateToPathAsync(@"C:\Media");

        Assert.Equal(BrowserFolderStatus.Ready, result!.Status);
        var created = Assert.Single(roots.AutomaticCreations);
        Assert.Equal(@"C:\", created.Path);
        Assert.Equal("Media", discovery.Requests.Single().RelativeFolder);
        Assert.Equal("Media", folders.Requests.Single().RelativeFolder);
        Assert.DoesNotContain(discovery.Requests, request => request.RelativeFolder is null);
    }

    [Fact]
    public async Task RevisitingVolumeAndDifferentFoldersReusesOneStableAnchor()
    {
        var roots = new FakeRoots();
        using var session = Session(roots, new FakeDiscovery(), EmptyFolders());

        var first = await session.NavigateToPathAsync(@"D:\Photos\Day1");
        var second = await session.NavigateToPathAsync(@"D:\Photos\Day2");
        var revisit = await session.NavigateToPathAsync(@"D:\Photos\Day1");

        Assert.Single(roots.AutomaticCreations);
        Assert.Equal(@"D:\", roots.AutomaticCreations[0].Path);
        Assert.Equal(first!.Location!.RootId, second!.Location!.RootId);
        Assert.Equal(first.Location.RootId, revisit!.Location!.RootId);
        Assert.Equal("Photos/Day1", revisit.Location.RelativeFolder);
    }

    [Fact]
    public async Task ExistingMostSpecificAncestorRootIsReusedWithoutAutomaticCreation()
    {
        var managed = Root("Photos", @"E:\Libraries\Photos");
        var broader = Root("Drive E", @"E:\");
        var roots = new FakeRoots(broader, managed);
        using var session = Session(roots, new FakeDiscovery(), EmptyFolders());

        var result = await session.NavigateToPathAsync(@"E:\Libraries\Photos\Trips");

        Assert.Equal(managed.RootId, result!.Location!.RootId);
        Assert.Equal("Trips", result.Location.RelativeFolder);
        Assert.Empty(roots.AutomaticCreations);
    }

    [Theory]
    [InlineData(@"\\server\share\Photos", @"\\server\share")]
    [InlineData(@"F:\Camera\Clips", @"F:\")]
    public void NaturalAnchorUsesShareOrVolumeBoundary(string location, string expected)
    {
        Assert.Equal(expected, BrowserLocationResolver.NaturalAnchor(location));
    }

    [Fact]
    public async Task StorageEntriesIncludeReadyAndUnavailableVolumesWithoutManagedRoots()
    {
        var provider = new BrowserStorageProvider(new FakeRoots(), new FakeVolumes(
            new(@"G:\", "REMOVABLE (G:)", true),
            new(@"Z:\", "MAPPED (Z:)", false, "Disconnected.")));

        var entries = await provider.ListAsync();

        Assert.Equal(2, entries.Count);
        Assert.All(entries, entry => Assert.Equal(BrowserStorageKind.Volume, entry.Kind));
        Assert.Contains(entries, entry => entry.PhysicalPath == @"G:\" && entry.Availability == MediaRootAvailability.Online);
        Assert.Contains(entries, entry => entry.PhysicalPath == @"Z:\" && entry.Availability == MediaRootAvailability.Unavailable);
    }

    [Fact]
    public void WindowsVolumeProvider_ProblematicDriveDoesNotHideHealthyDrives()
    {
        var provider = new WindowsBrowserVolumeProvider(new FakeDriveSource(
            new FakeDrive(@"C:\", "System", true),
            new FakeDrive(@"Q:\", "Broken", true, new IOException("Device is not ready.")),
            new FakeDrive(@"R:\", "Media", true)));

        var volumes = provider.ListVolumes();

        Assert.Contains(volumes, volume => volume.RootPath == @"C:\" && volume.IsReady);
        Assert.Contains(volumes, volume => volume.RootPath == @"R:\" && volume.IsReady);
        var problematic = Assert.Single(volumes, volume => volume.RootPath == @"Q:\");
        Assert.False(problematic.IsReady);
        Assert.Contains("Device is not ready", problematic.Diagnostic);
    }

    [Fact]
    public async Task NestedNavigationSupportsUpBackForwardAndRefresh()
    {
        var root = Root("Library", @"C:\Library");
        var roots = new FakeRoots(root);
        var folders = new FakeFolders((request, _) => Task.FromResult(Success(request,
            request.RelativeFolder is null ? DirectoryEntry(root.RootId, "Trips") : FileEntry(root.RootId, "Trips/clip.mp4"))));
        using var session = Session(roots, new FakeDiscovery(), folders);
        await session.NavigateToRootAsync(root.RootId);
        await session.NavigateToFolderAsync(DirectoryEntry(root.RootId, "Trips"));

        Assert.Equal("Trips", session.State.Location!.RelativeFolder);
        await session.UpAsync();
        Assert.Equal("", session.State.Location!.RelativeFolder);
        await session.BackAsync();
        Assert.Equal("Trips", session.State.Location!.RelativeFolder);
        await session.ForwardAsync();
        Assert.Equal("", session.State.Location!.RelativeFolder);
        var count = folders.Requests.Count;
        await session.RefreshAsync();
        Assert.Equal(count + 1, folders.Requests.Count);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task UnavailableManagedRootIsReportedWithoutReconciliation(int availabilityValue)
    {
        var availability = availabilityValue == 1 ? MediaRootAvailability.Unavailable : MediaRootAvailability.Unmapped;
        var root = Root("Archive", @"X:\Archive") with { Availability = availability, Diagnostic = "Drive unavailable." };
        var discovery = new FakeDiscovery();
        using var session = Session(new FakeRoots(root), discovery, EmptyFolders());

        var result = await session.NavigateToRootAsync(root.RootId);

        Assert.Equal(BrowserFolderStatus.RootUnavailable, result!.Status);
        Assert.Empty(discovery.Requests);
    }

    [Fact]
    public async Task RapidSwitchLatestRequestWinsAndCanceledRequestKeepsAcceptedState()
    {
        var first = Root("First", @"C:\First");
        var second = Root("Second", @"C:\Second");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var discovery = new FakeDiscovery(async (request, _, _, _) =>
        {
            if (request.RootId == first.RootId) { entered.SetResult(); await release.Task; }
            return DiscoverySuccess(request);
        });
        using var session = Session(new FakeRoots(first, second), discovery, EmptyFolders());

        var obsolete = session.NavigateToRootAsync(first.RootId);
        await entered.Task;
        var latest = await session.NavigateToRootAsync(second.RootId);
        release.SetResult();

        Assert.Null(await obsolete);
        Assert.Equal(second.RootId, latest!.Location!.RootId);
        Assert.Equal(second.RootId, session.State.Location!.RootId);
    }

    [Fact]
    public async Task CallerCancellationAndEnumerationFailureDoNotPublishMisleadingFolderContents()
    {
        var first = Root("First", @"C:\First");
        var second = Root("Second", @"C:\Second");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var discovery = new FakeDiscovery(async (request, _, cancellationToken, _) =>
        {
            if (request.RootId == second.RootId)
            {
                entered.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            return DiscoverySuccess(request);
        });
        using var session = Session(new FakeRoots(first, second), discovery, EmptyFolders());
        await session.NavigateToRootAsync(first.RootId);
        using var cancellation = new CancellationTokenSource();

        var canceled = session.NavigateToRootAsync(second.RootId, cancellation.Token);
        await entered.Task;
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceled);
        Assert.Equal(first.RootId, session.State.Location!.RootId);

        var deniedFolders = new FakeFolders((request, _) => Task.FromResult(new MediaFolderEnumerationResult(
            MediaFolderEnumerationStatus.AccessDenied, request.RelativeFolder ?? "", [], "Access denied.")));
        using var denied = Session(new FakeRoots(first), new FakeDiscovery(), deniedFolders);
        var deniedResult = await denied.NavigateToRootAsync(first.RootId);
        Assert.Equal(BrowserFolderStatus.AccessDenied, deniedResult!.Status);
        Assert.Empty(deniedResult.Entries);
    }

    [Fact]
    public async Task SuccessfulNavigationSurfacesTheScheduledDerivedWorkBatchForGridThumbnailUpdates()
    {
        var root = Root("Library", @"C:\Library");
        var reconciliation = new CatalogReconciliationResult(CatalogReconciliationStatus.Succeeded, root.RootId, "",
            [new(Guid.NewGuid(), "clip.mp4", CatalogReconciliationItemStatus.New)]);
        var batch = new DerivedWorkBatch(reconciliation, static _ => { });
        batch.Seal();
        var discovery = new FakeDiscovery((request, _, _, _) =>
            Task.FromResult(new MediaDiscoveryRefreshResult(reconciliation, batch)));
        var folders = new FakeFolders((request, _) => Task.FromResult(Success(request, FileEntry(root.RootId, "clip.mp4"))));
        using var session = Session(new FakeRoots(root), discovery, folders);

        var result = await session.NavigateToRootAsync(root.RootId);

        Assert.Same(batch, result!.DerivedWork);
    }

    [Fact]
    public async Task FailedReconciliationLeavesDerivedWorkNull()
    {
        var root = Root("Library", @"C:\Library");
        var discovery = new FakeDiscovery((request, _, _, _) => Task.FromResult(new MediaDiscoveryRefreshResult(
            new(CatalogReconciliationStatus.Failed, request.RootId, request.RelativeFolder ?? "", [], Diagnostic: "boom"), null)));
        using var session = Session(new FakeRoots(root), discovery, EmptyFolders());

        var result = await session.NavigateToRootAsync(root.RootId);

        Assert.Equal(BrowserFolderStatus.Failed, result!.Status);
        Assert.Null(result.DerivedWork);
    }

    [Fact]
    public async Task FailedNavigationDoesNotReplaceLastSuccessfullyLoadedSessionState()
    {
        var root = Root("Library", @"C:\Library");
        var fail = false;
        var folders = new FakeFolders((request, _) => Task.FromResult(fail
            ? new MediaFolderEnumerationResult(MediaFolderEnumerationStatus.AccessDenied,
                request.RelativeFolder ?? "", [], "Access denied.")
            : Success(request, FileEntry(root.RootId, "loaded.mp4"))));
        using var session = Session(new FakeRoots(root), new FakeDiscovery(), folders);
        var loaded = await session.NavigateToRootAsync(root.RootId);
        fail = true;
        var failed = await session.RefreshAsync();

        Assert.Equal(BrowserFolderStatus.Ready, loaded!.Status);
        Assert.Equal(BrowserFolderStatus.AccessDenied, failed!.Status);
        Assert.Equal(loaded, session.State);
    }

    [Fact]
    public void DefaultScopeModeIsDirectFolder()
    {
        using var session = Session(new FakeRoots(), new FakeDiscovery(), EmptyFolders());
        Assert.Equal(BrowserScopeMode.DirectFolder, session.State.Mode);
    }

    [Fact]
    public async Task SetIncludeSubfoldersAsync_WithNoLocationOpenIsANoOp()
    {
        // #124 (revised): there is no folder to attach a Catalog recursive root to yet, so this must not
        // create one, reload, or touch anything — unlike the pre-revision design, effective mode is never a
        // standalone field that can be set ahead of navigating anywhere.
        var discovery = new FakeDiscovery();
        var folders = EmptyFolders();
        var recursiveRoots = new BrowserRecursiveRootService(new InMemoryRecursiveRootRepository());
        using var session = Session(new FakeRoots(), discovery, folders, recursiveRoots: recursiveRoots);

        var state = await session.SetIncludeSubfoldersAsync(true);

        Assert.Equal(BrowserScopeMode.DirectFolder, session.State.Mode);
        Assert.Empty(discovery.Requests);
        Assert.Empty(folders.Requests);
        Assert.Empty(await recursiveRoots.ListAsync());
        Assert.Same(session.State, state);
    }

    [Fact]
    public async Task SetIncludeSubfoldersAsync_DisablingAnAlreadyDirectFolderStillReloadsAsASameFolderRefresh()
    {
        // ApplyIncludeSubfoldersAsync always reloads through the normal generation/cancellation machinery —
        // IBrowserRecursiveRootService.DisableAsync itself no-ops the Catalog write when nothing governs this
        // folder, but the reload still happens, as a same-folder refresh that never pushes a back-stack entry.
        var root = Root("Library", @"C:\Library");
        var discovery = new FakeDiscovery();
        var folders = new FakeFolders((request, _) => Task.FromResult(Success(request, FileEntry(root.RootId, "clip.mp4"))));
        using var session = Session(new FakeRoots(root), discovery, folders);
        await session.NavigateToRootAsync(root.RootId);
        var requestsBefore = folders.Requests.Count;

        var state = await session.SetIncludeSubfoldersAsync(false);

        Assert.Equal(requestsBefore + 1, folders.Requests.Count);
        Assert.Equal(BrowserScopeMode.DirectFolder, state!.Mode);
        Assert.Null(session.BackTarget);
    }

    [Fact]
    public async Task SetIncludeSubfoldersAsync_DisablingFromAnInheritedDescendantKeepsTheSameLocationAndRemovesTheGoverningRootFromTheRepository()
    {
        // #124: disabling Include Subfolders from a folder that only INHERITS recursion (not the stored root
        // itself) must (a) never change BrowserNavigationSession.State.Location — the same folder stays open,
        // just in direct mode — and (b) actually delete the governing ancestor root from the Catalog
        // repository, not merely stop showing it as recursive in memory. Proven directly against the
        // repository here, independent of any WPF/tree-selection concern, to isolate whether the session/
        // service layer itself is correct.
        var root = Root("Library", @"C:\Library");
        var folders = new FakeFolders((request, _) => Task.FromResult(Success(request)));
        var repository = new InMemoryRecursiveRootRepository(new BrowserRecursiveRoot(Guid.NewGuid(), root.RootId, "2026"));
        var recursiveRoots = new BrowserRecursiveRootService(repository);
        using var session = Session(new FakeRoots(root), new FakeDiscovery(), folders, recursiveRoots: recursiveRoots);
        var deep = await session.NavigateToPathAsync(@"C:\Library\2026\August\Wedding");
        Assert.Equal(BrowserScopeMode.IncludeSubfolders, deep!.Mode); // inherited from the stored "2026" root

        var state = await session.SetIncludeSubfoldersAsync(false);

        Assert.Equal("2026/August/Wedding", state!.Location!.RelativeFolder);
        Assert.Equal("2026/August/Wedding", session.State.Location!.RelativeFolder);
        Assert.Equal(BrowserScopeMode.DirectFolder, state.Mode);
        Assert.Empty(await repository.ListAsync()); // the governing "2026" root is actually gone, not just hidden
    }

    [Fact]
    public async Task SetIncludeSubfoldersAsync_ToggleOffSequence_PreservesTreeSelectionAndRevertsIconsAcrossSessionAndTreeLayersTogether()
    {
        // #124: end-to-end proof spanning the session layer AND MainWindow's own BrowserTreeModel/icon-sync
        // logic in a single sequence (mirroring exactly what MainWindow does on a successful toggle: re-run
        // BrowserTreeModel.Synchronize against the new BrowserFolderState, then recompute every materialized
        // node's IsRecursiveScope from the returned RecursiveRoots) — not just the repository, and not just the
        // tree model in isolation, so a bug at the seam between them (e.g. stale RecursiveRoots reused across
        // the toggle) would show up here even if each layer's own isolated tests stayed green.
        var root = Root("Library", @"C:\Library");
        var folders = new FakeFolders((request, _) => Task.FromResult(Success(request)));
        var repository = new InMemoryRecursiveRootRepository(new BrowserRecursiveRoot(Guid.NewGuid(), root.RootId, "2026"));
        var recursiveRoots = new BrowserRecursiveRootService(repository);
        using var session = Session(new FakeRoots(root), new FakeDiscovery(), folders, recursiveRoots: recursiveRoots);
        var tree = new BrowserTreeModel();
        tree.SetStorageEntries([new($"root:{root.RootId}", root.DisplayName, root.PhysicalPath!,
            BrowserStorageKind.ManagedRoot, MediaRootAvailability.Online, root.RootId)]);

        var before = await session.NavigateToPathAsync(@"C:\Library\2026\August\Wedding");
        tree.Synchronize(before!);
        SyncIcons(tree, before!.RecursiveRoots ?? []);
        var weddingNode = tree.SelectedNode!;
        var ancestorRootNode = Descendants(tree.Roots)
            .Single(node => node.RootId == root.RootId && node.RelativeFolder == "2026");
        Assert.True(weddingNode.IsRecursiveScope); // inherited
        Assert.True(ancestorRootNode.IsRecursiveScope); // the stored root itself
        Assert.True(weddingNode.IsFilledFolderIcon);
        Assert.True(ancestorRootNode.IsFilledFolderIcon);

        var after = await session.SetIncludeSubfoldersAsync(false);
        tree.Synchronize(after!);
        SyncIcons(tree, after!.RecursiveRoots ?? []);

        Assert.Same(weddingNode, tree.SelectedNode); // exact same node instance, never a different row
        Assert.True(weddingNode.IsSelected);
        Assert.False(weddingNode.IsRecursiveScope); // no longer effectively recursive
        Assert.True(weddingNode.IsFilledFolderIcon); // still filled — it is the selected folder
        Assert.False(ancestorRootNode.IsRecursiveScope); // the governing root is actually gone
        Assert.False(ancestorRootNode.IsFilledFolderIcon); // not selected, no longer recursive: outline
        Assert.Single(Descendants(tree.Roots), node => node.IsSelected);
        Assert.Empty(await repository.ListAsync());
    }

    private static void SyncIcons(BrowserTreeModel tree, IReadOnlyList<BrowserRecursiveRoot> roots)
    {
        foreach (var node in Descendants(tree.Roots).Where(node => node.RootId is not null))
            node.IsRecursiveScope = BrowserRecursiveRootLogic.IsEffectivelyRecursive(roots, node.RootId!.Value, node.RelativeFolder!);
    }

    private static IEnumerable<BrowserTreeNode> Descendants(IEnumerable<BrowserTreeNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in Descendants(node.Children)) yield return child;
        }
    }

    [Fact]
    public async Task SetIncludeSubfoldersAsync_EnablingReloadsTheOpenFolderThroughRecursiveDiscoveryAndPopulatesRecursiveMediaEntries()
    {
        var root = Root("Library", @"C:\Library");
        var folders = new FakeFolders((request, _) => Task.FromResult(Success(request, DirectoryEntry(root.RootId, "Sub"))));
        var recursive = new FakeRecursiveDiscovery((request, _, _, _, _) => Task.FromResult(new RecursiveScopeResult(
            CatalogReconciliationStatus.Succeeded, request.RootId, request.RelativeFolder ?? "",
            [FileEntry(root.RootId, "clip.mp4"), FileEntry(root.RootId, "Sub/deep.mp4")], null)));
        using var session = Session(new FakeRoots(root), new FakeDiscovery(), folders, recursive);
        await session.NavigateToRootAsync(root.RootId);

        var state = await session.SetIncludeSubfoldersAsync(true);

        Assert.Equal(BrowserScopeMode.IncludeSubfolders, state!.Mode);
        Assert.NotNull(state.RecursiveMediaEntries);
        Assert.Equal(2, state.RecursiveMediaEntries!.Count);
        // The direct-child folder listing (Entries) is unaffected — the Locations tree still shows "Sub".
        Assert.Contains(state.Entries, entry => entry.IsDirectory && entry.Name == "Sub");
    }

    [Fact]
    public async Task DirectMode_RecursiveMediaEntriesIsAlwaysNull()
    {
        var root = Root("Library", @"C:\Library");
        var folders = new FakeFolders((request, _) => Task.FromResult(Success(request, FileEntry(root.RootId, "clip.mp4"))));
        using var session = Session(new FakeRoots(root), new FakeDiscovery(), folders);

        var result = await session.NavigateToRootAsync(root.RootId);

        Assert.Equal(BrowserScopeMode.DirectFolder, result!.Mode);
        Assert.Null(result.RecursiveMediaEntries);
    }

    [Fact]
    public async Task SetIncludeSubfoldersAsync_DoesNotPushABackStackEntryForTheFolderThatWasAlreadyOpen()
    {
        var root = Root("Library", @"C:\Library");
        var folders = new FakeFolders((request, _) => Task.FromResult(Success(request, FileEntry(root.RootId, "clip.mp4"))));
        var recursive = new FakeRecursiveDiscovery();
        using var session = Session(new FakeRoots(root), new FakeDiscovery(), folders, recursive);
        await session.NavigateToRootAsync(root.RootId);

        await session.SetIncludeSubfoldersAsync(true);

        Assert.Null(session.BackTarget);
    }

    [Fact]
    public async Task RecursiveScope_ObsoleteSlowRecursiveWalkNeverOverwritesANewerDirectModeToggle()
    {
        var root = Root("Library", @"C:\Library");
        var folders = new FakeFolders((request, _) => Task.FromResult(Success(request, FileEntry(root.RootId, "clip.mp4"))));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var recursive = new FakeRecursiveDiscovery(async (request, _, _, _, _) =>
        {
            entered.SetResult();
            await release.Task;
            return new RecursiveScopeResult(CatalogReconciliationStatus.Succeeded, request.RootId,
                request.RelativeFolder ?? "", [FileEntry(root.RootId, "recursive-only.mp4")], null);
        });
        using var session = Session(new FakeRoots(root), new FakeDiscovery(), folders, recursive);
        await session.NavigateToRootAsync(root.RootId);

        var obsolete = session.SetIncludeSubfoldersAsync(true);
        await entered.Task;
        // Turning recursion back off before the slow recursive walk finishes must win promptly rather than
        // waiting for the obsolete recursive work — the same generation/cancellation guarantee every other
        // rapid-navigation scenario already relies on.
        var latest = await session.SetIncludeSubfoldersAsync(false);
        release.SetResult();

        Assert.Null(await obsolete);
        Assert.Equal(BrowserScopeMode.DirectFolder, latest!.Mode);
        Assert.Null(latest.RecursiveMediaEntries);
        Assert.Equal(BrowserScopeMode.DirectFolder, session.State.Mode);
    }

    [Fact]
    public async Task RecursiveScope_NavigatingToADifferentFolderCancelsAnObsoleteRecursiveWalkOfThePreviousFolder()
    {
        var folderA = Root("A", @"C:\A");
        var folderB = Root("B", @"C:\B");
        var folders = new FakeFolders((request, _) => Task.FromResult(Success(request)));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var recursive = new FakeRecursiveDiscovery(async (request, _, _, _, _) =>
        {
            if (request.RootId == folderA.RootId) { entered.SetResult(); await release.Task; }
            return new RecursiveScopeResult(CatalogReconciliationStatus.Succeeded, request.RootId,
                request.RelativeFolder ?? "", [], null);
        });
        // Both folders are independent, already-established recursive roots (#124 revised: multiple disjoint
        // roots persist independently) rather than a single pre-navigation mode toggle, since effective mode
        // is now always derived per-folder from the Catalog rather than settable ahead of any navigation.
        var recursiveRoots = new BrowserRecursiveRootService(new InMemoryRecursiveRootRepository(
            new(Guid.NewGuid(), folderA.RootId, ""), new(Guid.NewGuid(), folderB.RootId, "")));
        using var session = Session(new FakeRoots(folderA, folderB), new FakeDiscovery(), folders, recursive, recursiveRoots);

        var obsolete = session.NavigateToRootAsync(folderA.RootId);
        await entered.Task;
        var latest = await session.NavigateToRootAsync(folderB.RootId);
        release.SetResult();

        Assert.Null(await obsolete);
        Assert.Equal(folderB.RootId, latest!.Location!.RootId);
        Assert.Equal(folderB.RootId, session.State.Location!.RootId);
    }

    [Fact]
    public async Task RecursiveScopeProgressChanged_FiresWithReportsFromTheActiveRecursiveWalk()
    {
        var root = Root("Library", @"C:\Library");
        var folders = new FakeFolders((request, _) => Task.FromResult(Success(request)));
        IProgress<RecursiveScopeProgress>? capturedProgress = null;
        var recursive = new FakeRecursiveDiscovery((request, _, _, _, progress) =>
        {
            capturedProgress = progress;
            return Task.FromResult(new RecursiveScopeResult(CatalogReconciliationStatus.Succeeded,
                request.RootId, request.RelativeFolder ?? "", [], null));
        });
        using var session = Session(new FakeRoots(root), new FakeDiscovery(), folders, recursive);
        await session.NavigateToRootAsync(root.RootId);
        var received = new List<RecursiveScopeProgress>();
        session.RecursiveScopeProgressChanged += (_, progress) => received.Add(progress);

        // The fake resolves synchronously, so report while it is still the in-flight (current) generation is
        // only observable via the reporter itself, but this still proves the wiring: a progress instance was
        // handed to the discovery service and reporting through it reaches the session's event.
        await session.SetIncludeSubfoldersAsync(true);
        Assert.NotNull(capturedProgress);
        capturedProgress!.Report(new(4, 2));

        Assert.Contains(received, report => report.FoldersDiscovered == 4 && report.FoldersVisited == 2);
    }

    [Fact]
    public async Task RecursiveScopeProgressChanged_ObsoleteWalkReportsNeverReachSubscribersAfterANewerRequestSupersedesIt()
    {
        var root = Root("Library", @"C:\Library");
        var folders = new FakeFolders((request, _) => Task.FromResult(Success(request)));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        IProgress<RecursiveScopeProgress>? capturedProgress = null;
        var recursive = new FakeRecursiveDiscovery(async (request, _, _, _, progress) =>
        {
            capturedProgress = progress;
            entered.SetResult();
            await release.Task;
            return new RecursiveScopeResult(CatalogReconciliationStatus.Succeeded, request.RootId,
                request.RelativeFolder ?? "", [], null);
        });
        using var session = Session(new FakeRoots(root), new FakeDiscovery(), folders, recursive);
        await session.NavigateToRootAsync(root.RootId);
        var received = new List<RecursiveScopeProgress>();
        session.RecursiveScopeProgressChanged += (_, progress) => received.Add(progress);

        var obsolete = session.SetIncludeSubfoldersAsync(true);
        await entered.Task;
        capturedProgress!.Report(new(2, 1)); // reported while this walk is still the current generation
        var latest = await session.SetIncludeSubfoldersAsync(false); // supersedes it
        capturedProgress!.Report(new(9, 9)); // reported after supersession — must never reach subscribers
        release.SetResult();
        await obsolete;

        Assert.Equal(BrowserScopeMode.DirectFolder, latest!.Mode);
        Assert.Contains(received, report => report is { FoldersDiscovered: 2, FoldersVisited: 1 });
        Assert.DoesNotContain(received, report => report is { FoldersDiscovered: 9, FoldersVisited: 9 });
    }

    [Fact]
    public async Task EffectiveScopeDetermined_FiresWithLocationAndModeForAnOrdinaryDirectFolderNavigation()
    {
        var root = Root("Library", @"C:\Library");
        var folders = new FakeFolders((request, _) => Task.FromResult(Success(request, FileEntry(root.RootId, "clip.mp4"))));
        using var session = Session(new FakeRoots(root), new FakeDiscovery(), folders);
        BrowserEffectiveScope? captured = null;
        session.EffectiveScopeDetermined += (_, scope) => captured = scope;

        await session.NavigateToRootAsync(root.RootId);

        Assert.NotNull(captured);
        Assert.Equal(root.RootId, captured!.Location.RootId);
        Assert.Equal(BrowserScopeMode.DirectFolder, captured.Mode);
    }

    [Fact]
    public async Task EffectiveScopeDetermined_FiresWithIncludeSubfoldersModeBeforeTheSlowRecursiveWalkCompletes()
    {
        // #124 (further revised): tree icons/toolbar toggle must reflect effective mode immediately once the
        // Catalog mutation succeeds, not once the (potentially slow) recursive walk it triggers finishes.
        var root = Root("Library", @"C:\Library");
        var folders = new FakeFolders((request, _) => Task.FromResult(Success(request)));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var recursive = new FakeRecursiveDiscovery(async (request, _, _, _, _) =>
        {
            entered.SetResult();
            await release.Task;
            return new RecursiveScopeResult(CatalogReconciliationStatus.Succeeded, request.RootId,
                request.RelativeFolder ?? "", [], null);
        });
        using var session = Session(new FakeRoots(root), new FakeDiscovery(), folders, recursive);
        await session.NavigateToRootAsync(root.RootId);
        BrowserEffectiveScope? captured = null;
        session.EffectiveScopeDetermined += (_, scope) => captured = scope;

        var enabling = session.SetIncludeSubfoldersAsync(true);
        await entered.Task; // the recursive walk is now blocked mid-flight

        Assert.NotNull(captured);
        Assert.Equal(BrowserScopeMode.IncludeSubfolders, captured!.Mode);
        Assert.False(enabling.IsCompleted); // proves the event above really did fire before the walk finished

        release.SetResult();
        await enabling;
    }

    [Fact]
    public async Task EffectiveScopeDetermined_ObsoleteDeterminationsNeverReachSubscribersAfterANewerRequestSupersedesThem()
    {
        var root = Root("Library", @"C:\Library");
        var folders = new FakeFolders((request, _) => Task.FromResult(Success(request)));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gatedRoots = new BrowserRecursiveRootService(new GatedRecursiveRootRepository(entered, release));
        using var session = Session(new FakeRoots(root), new FakeDiscovery(), folders, recursiveRoots: gatedRoots);
        var received = new List<BrowserEffectiveScope>();
        session.EffectiveScopeDetermined += (_, scope) => received.Add(scope);

        var obsolete = session.NavigateToRootAsync(root.RootId);
        await entered.Task; // obsolete's Catalog root list fetch is now blocked mid-flight
        var latest = await session.NavigateToRootAsync(root.RootId); // begins a newer generation first
        release.SetResult();
        await obsolete;

        Assert.Single(received); // only the current generation's determination ever reached the subscriber
        Assert.NotNull(latest);
    }

    /// <summary>Blocks only its first ListAsync call (the "obsolete" generation) — later calls (a superseding generation) return immediately, otherwise the test itself would deadlock waiting for a release that comes after the superseding call.</summary>
    private sealed class GatedRecursiveRootRepository(TaskCompletionSource entered, TaskCompletionSource release)
        : IBrowserRecursiveRootRepository
    {
        private int _calls;

        public async Task<IReadOnlyList<BrowserRecursiveRoot>> ListAsync(CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _calls) == 1)
            {
                entered.TrySetResult();
                await release.Task;
            }
            return [];
        }

        public Task CreateAsync(Guid rootId, string relativeFolder, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<int> DeleteAsync(IReadOnlyCollection<Guid> scopeIds, CancellationToken cancellationToken = default) =>
            Task.FromResult(0);
    }

    private static BrowserNavigationSession Session(FakeRoots roots, FakeDiscovery discovery, FakeFolders folders,
        IRecursiveMediaDiscoveryService? recursiveDiscovery = null, IBrowserRecursiveRootService? recursiveRoots = null) =>
        new(roots, new BrowserLocationResolver(roots, new ExistingFileSystem()), discovery, folders,
            recursiveRoots ?? new BrowserRecursiveRootService(new InMemoryRecursiveRootRepository()), recursiveDiscovery);

    /// <summary>In-memory <see cref="IBrowserRecursiveRootRepository"/> — reuses the real <see cref="BrowserRecursiveRootService"/> normalization logic rather than duplicating it in test doubles.</summary>
    private sealed class InMemoryRecursiveRootRepository(params BrowserRecursiveRoot[] seed) : IBrowserRecursiveRootRepository
    {
        private readonly List<BrowserRecursiveRoot> _roots = [.. seed];

        public Task<IReadOnlyList<BrowserRecursiveRoot>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<BrowserRecursiveRoot>>([.. _roots]);

        public Task CreateAsync(Guid rootId, string relativeFolder, CancellationToken cancellationToken = default)
        {
            _roots.Add(new(Guid.NewGuid(), rootId, relativeFolder));
            return Task.CompletedTask;
        }

        public Task<int> DeleteAsync(IReadOnlyCollection<Guid> scopeIds, CancellationToken cancellationToken = default) =>
            Task.FromResult(_roots.RemoveAll(root => scopeIds.Contains(root.ScopeId)));
    }

    private sealed class FakeRecursiveDiscovery : IRecursiveMediaDiscoveryService
    {
        private readonly Func<MediaFolderEnumerationRequest, DerivedWorkPriority, CancellationToken, CancellationToken,
            IProgress<RecursiveScopeProgress>?, Task<RecursiveScopeResult>> _discover;

        public FakeRecursiveDiscovery(
            Func<MediaFolderEnumerationRequest, DerivedWorkPriority, CancellationToken, CancellationToken,
                IProgress<RecursiveScopeProgress>?, Task<RecursiveScopeResult>>? discover = null) =>
            _discover = discover ?? ((request, _, _, _, _) => Task.FromResult(new RecursiveScopeResult(
                CatalogReconciliationStatus.Succeeded, request.RootId, request.RelativeFolder ?? "", [], null)));

        public List<MediaFolderEnumerationRequest> Requests { get; } = [];

        public Task<RecursiveScopeResult> DiscoverAsync(MediaFolderEnumerationRequest request,
            DerivedWorkPriority priority = DerivedWorkPriority.Background, CancellationToken enumerationToken = default,
            CancellationToken derivedWorkToken = default, IProgress<RecursiveScopeProgress>? progress = null)
        {
            Requests.Add(request);
            return _discover(request, priority, enumerationToken, derivedWorkToken, progress);
        }
    }

    private static FakeFolders EmptyFolders() =>
        new((request, _) => Task.FromResult(Success(request)));

    private static MediaRootInfo Root(string name, string path) =>
        new(Guid.NewGuid(), name, path, MediaRootAvailability.Online);

    private static MediaFolderEntry DirectoryEntry(Guid rootId, string path) =>
        new(rootId, path, path.ToUpperInvariant(), Path.GetFileName(path), true,
            MediaTypeClassification.Unknown, null, DateTimeOffset.UtcNow);

    private static MediaFolderEntry FileEntry(Guid rootId, string path) =>
        new(rootId, path, path.ToUpperInvariant(), Path.GetFileName(path), false,
            new(MediaTypeCategory.Video, "mp4"), 10, DateTimeOffset.UtcNow);

    private static MediaFolderEnumerationResult Success(MediaFolderEnumerationRequest request,
        params MediaFolderEntry[] entries) =>
        new(MediaFolderEnumerationStatus.Succeeded, request.RelativeFolder ?? "", entries);

    private static MediaDiscoveryRefreshResult DiscoverySuccess(MediaFolderEnumerationRequest request) =>
        new(new(CatalogReconciliationStatus.Succeeded, request.RootId, request.RelativeFolder ?? "", []), null);

    private sealed class ExistingFileSystem : IBrowserLocationFileSystem
    {
        public bool DirectoryExists(string path) => true;
    }

    private sealed class FakeVolumes(params BrowserVolume[] volumes) : IBrowserVolumeProvider
    {
        public IReadOnlyList<BrowserVolume> ListVolumes() => volumes;
    }

    private sealed class FakeDriveSource(params IBrowserDrive[] drives) : IBrowserDriveSource
    {
        public IReadOnlyList<IBrowserDrive> ListDrives() => drives;
    }

    private sealed class FakeDrive(string rootPath, string label, bool ready, Exception? failure = null) : IBrowserDrive
    {
        public string Name => rootPath;
        public string RootPath => rootPath;
        public bool IsReady => failure is null ? ready : throw failure;
        public string VolumeLabel => label;
    }

    private sealed class FakeRoots(params MediaRootInfo[] initial) : IMediaRootService
    {
        private readonly List<MediaRootInfo> _roots = [.. initial];
        public List<(string Name, string Path)> AutomaticCreations { get; } = [];
        public Task<IReadOnlyList<MediaRootInfo>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MediaRootInfo>>(_roots.ToArray());
        public Task<MediaRootInfo?> GetAsync(Guid rootId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_roots.FirstOrDefault(root => root.RootId == rootId));
        public Task<MediaRootChangeResult> CreateBrowserAnchorAsync(string displayName, string physicalPath, CancellationToken cancellationToken = default)
        {
            var normalized = MediaPathSemantics.NormalizeRootPath(physicalPath);
            var existing = _roots.FirstOrDefault(root => root.PhysicalPath is not null &&
                string.Equals(MediaPathSemantics.NormalizeRootPath(root.PhysicalPath), normalized, StringComparison.OrdinalIgnoreCase));
            if (existing is not null) return Task.FromResult(new MediaRootChangeResult(true, existing));
            AutomaticCreations.Add((displayName, physicalPath));
            var created = Root(displayName, normalized);
            _roots.Add(created);
            return Task.FromResult(new MediaRootChangeResult(true, created));
        }
        public Task<MediaRootChangeResult> CreateAsync(string displayName, string physicalPath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MediaRootChangeResult> RenameAsync(Guid rootId, string displayName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MediaRootChangeResult> RemapAsync(Guid rootId, string physicalPath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MediaPathResolution> ResolveAsync(Guid rootId, string relativePath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeDiscovery : IMediaDiscoveryRefreshService
    {
        private readonly Func<MediaFolderEnumerationRequest, DerivedWorkPriority, CancellationToken, CancellationToken, Task<MediaDiscoveryRefreshResult>> _refresh;
        public FakeDiscovery(Func<MediaFolderEnumerationRequest, DerivedWorkPriority, CancellationToken, CancellationToken, Task<MediaDiscoveryRefreshResult>>? refresh = null) =>
            _refresh = refresh ?? ((request, _, _, _) => Task.FromResult(DiscoverySuccess(request)));
        public List<MediaFolderEnumerationRequest> Requests { get; } = [];
        public Task<MediaDiscoveryRefreshResult> RefreshAsync(MediaFolderEnumerationRequest request, DerivedWorkPriority priority = DerivedWorkPriority.Background, CancellationToken cancellationToken = default, CancellationToken derivedWorkCancellationToken = default)
        {
            Requests.Add(request);
            return _refresh(request, priority, cancellationToken, derivedWorkCancellationToken);
        }
    }

    private sealed class FakeFolders(Func<MediaFolderEnumerationRequest, CancellationToken, Task<MediaFolderEnumerationResult>> enumerate) : IMediaFolderEnumerator
    {
        public List<MediaFolderEnumerationRequest> Requests { get; } = [];
        public Task<MediaFolderEnumerationResult> EnumerateAsync(MediaFolderEnumerationRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return enumerate(request, cancellationToken);
        }
    }
}
