using System.Text.Json;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class WorkspaceContinuationTests
{
    [Theory]
    [InlineData(1, 1000, 800, 117)]
    [InlineData(1, 2000, 1600, 217)]
    [InlineData(null, 1000, 800, 800)]
    [InlineData(9, 1000, 800, 800)]
    [InlineData(1, 0, 0, 0)]
    public void Scroll_UsesCurrentRowGeometryOrClampedPixelFallback(int? row, double extent, double maximum, double expected)
    {
        var saved = new WorkspaceGridState { VerticalOffset = 907, WithinRowOffset = 17 };
        Assert.Equal(expected, saved.RestoreOffset(row, 10, extent, maximum));
    }

    [Fact]
    public void Schema_RepeatedServiceLifecyclesRetainIndependentSections()
    {
        WithFile(path =>
        {
            var root = Guid.NewGuid();
            var state = new WorkspaceStateService(path);
            state.SetBrowserLocation(root, "shoot", @"D:\shoot");
            state.SetRightPanel(420, true, "subclips");
            state.SetBrowserViewMode(BrowserViewMode.Hybrid);
            state.SetBrowserThumbnailSizeLevel(4);
            state.SetContinuation(new() { Query = new() { SearchText = "take" }, Grid = new() { VerticalOffset = 50 } });
            state.Save();
            for (var cycle = 1; cycle <= 3; cycle++)
            {
                state = new WorkspaceStateService(path);
                Assert.Equal(root, state.Current.Browser!.RootId);
                Assert.Equal("subclips", state.Current.Layout!.RightPanelActiveSurface);
                Assert.Equal(420, state.Current.Layout.RightPanelWidth);
                Assert.True(state.Current.Layout.RightPanelOpen);
                Assert.Equal(BrowserViewMode.Hybrid, state.GetBrowserViewMode());
                Assert.Equal(4, state.Current.Layout.BrowserThumbnailSizeLevel);
                Assert.Equal("take", state.Current.Continuation!.Query.SearchText);
                Assert.Equal(cycle * 50, state.Current.Continuation.Grid.VerticalOffset);
                state.SetContinuation(state.Current.Continuation with { Grid = new() { VerticalOffset = (cycle + 1) * 50 } });
                state.Save();
            }
        });
    }

    [Fact]
    public async Task Tree_OfflineAndFailedBranchesDoNotBlockAnotherRoot()
    {
        var offline = new MediaRootInfo(Guid.NewGuid(), "Offline", @"Z:\Offline", MediaRootAvailability.Unavailable);
        var online = new MediaRootInfo(Guid.NewGuid(), "Online", @"D:\Online", MediaRootAvailability.Online);
        var tree = new BrowserTreeModel();
        var calls = new List<string>();
        var folders = new Folders((request, _) =>
        {
            Assert.Equal(online.RootId, request.RootId);
            var folder = request.RelativeFolder ?? ""; calls.Add(folder);
            return Task.FromResult(folder == "bad"
                ? new MediaFolderEnumerationResult(MediaFolderEnumerationStatus.AccessDenied, folder, [])
                : new MediaFolderEnumerationResult(MediaFolderEnumerationStatus.Succeeded, folder,
                    folder == "" ? [Directory(online.RootId, "bad"), Directory(online.RootId, "good")] : []));
        });
        await WorkspaceTreeRestoration.RestoreAsync(tree, new Roots(offline, online), folders,
            [new() { RootId = offline.RootId }, new() { RootId = Guid.NewGuid() },
             new() { RootId = online.RootId, RelativeFolder = "bad" }, new() { RootId = online.RootId, RelativeFolder = "good" }],
            CancellationToken.None, action => action());
        Assert.Equal(new[] { "", "bad", "good" }, calls);
        Assert.True(tree.FindByPath(@"D:\Online\good")!.IsExpanded);
        Assert.False(tree.FindByPath(@"D:\Online\bad")!.IsExpanded);
        Assert.Null(tree.SelectedNode);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("42")]
    public void Schema_NonObjectDocumentFallsBackSafely(string json) => WithFile(path =>
    {
        File.WriteAllText(path, json);
        Assert.Equal(WorkspaceState.Empty, WorkspaceStateStore.Load(path));
    });

    [Fact]
    public void Schema_RoundTripsTypedQueryIdentitySelectionAndPlayer()
    {
        var root = Guid.NewGuid(); var asset = Guid.NewGuid(); var second = Guid.NewGuid();
        var state = new WorkspaceState
        {
            Browser = new() { RootId = root, RelativeFolder = "shoot" },
            Continuation = new()
            {
                Query = new() { SearchText = "take", SortMode = BrowserSortMode.Duration, SortDescending = true,
                    Filters = [BrowserFilterPredicate.ForMediaType(MediaTypeCategory.Video), BrowserFilterPredicate.ForRating(BrowserNumberComparison.GreaterThan, 2)] },
                QueryLocked = true,
                ExpandedFolders = [new() { RootId = root, RelativeFolder = "archive/2024" }],
                TreeVerticalOffset = 333, TreeHorizontalOffset = 12,
                Grid = new() { SelectedAssetIds = [asset, second], AnchorAssetId = second, TopAssetId = asset, WithinRowOffset = 17, VerticalOffset = 907 },
                Player = new() { Asset = new(root, "shoot/take.mp4", "key", "take.mp4", MediaPresentationKind.Video, asset),
                    Position = TimeSpan.FromMilliseconds(19323), PixelZoom = 2, PanX = 10, PanY = -20, Loop = true,
                    Speed = 0.5, Cadence = new(24000, 1001), Volume = 45, Muted = true }
            }
        };
        WithFile(path =>
        {
            WorkspaceStateStore.Save(path, state);
            var actual = WorkspaceStateStore.Load(path);
            Assert.Equal(2, actual.Version);
            Assert.Equal(state.Browser, actual.Browser);
            Assert.Equal(state.Continuation.Query.SearchText, actual.Continuation!.Query.SearchText);
            Assert.Equal(state.Continuation.Query.Filters, actual.Continuation.Query.Filters);
            Assert.Equal(state.Continuation.Grid.SelectedAssetIds, actual.Continuation.Grid.SelectedAssetIds);
            Assert.Equal(second, actual.Continuation.Grid.AnchorAssetId);
            Assert.Equal(state.Continuation.Player!.Position, actual.Continuation.Player!.Position);
            Assert.Equal(state.Continuation.Player.Cadence, actual.Continuation.Player.Cadence);
            Assert.Equal(45, actual.Continuation.Player.Volume);
            Assert.True(actual.Continuation.QueryLocked);
            Assert.Equal(state.Continuation.ExpandedFolders, actual.Continuation.ExpandedFolders);
        });
    }

    [Fact]
    public void Schema_CorruptPlayerRecoversBrowserQueryAndGridIndependently()
    {
        WithFile(path =>
        {
            var root = Guid.NewGuid();
            File.WriteAllText(path, """{"Version":2,"Browser":{"RootId":"ROOT","RelativeFolder":"shoot"},"Continuation":{"Query":{"SearchText":"take"},"Grid":{"VerticalOffset":321},"Player":{"Position":"broken"}}} """.Replace("ROOT", root.ToString()));
            var actual = WorkspaceStateStore.Load(path);
            Assert.Equal(root, actual.Browser!.RootId);
            Assert.Equal("take", actual.Continuation!.Query.SearchText);
            Assert.Equal(321, actual.Continuation.Grid.VerticalOffset);
            Assert.Null(actual.Continuation.Player);
        });
    }

    [Fact]
    public void Schema_LegacyAndInvalidValuesRemainSafe()
    {
        var root = Guid.NewGuid();
        WithFile(path =>
        {
            File.WriteAllText(path, """{"Version":1,"Browser":{"RootId":"ROOT","RelativeFolder":null},"Layout":{"RightPanelOpen":true}}""".Replace("ROOT", root.ToString()));
            var state = WorkspaceStateStore.Load(path);
            Assert.Equal("", state.Browser!.RelativeFolder);
            Assert.Null(state.Continuation);
            Assert.True(state.Layout!.RightPanelOpen);
        });
        var saved = WorkspaceContinuationState.Normalize(new()
        {
            TreeVerticalOffset = double.NaN,
            ExpandedFolders = [new() { RootId = root, RelativeFolder = "../escape" }, new() { RootId = root, RelativeFolder = "archive" }],
            Player = new() { Asset = new(root, "clip.mp4", "key", "clip", MediaPresentationKind.Video, Guid.NewGuid()),
                PixelZoom = 100, Speed = 100, PanX = double.PositiveInfinity, Position = TimeSpan.FromSeconds(-1) }
        })!;
        Assert.Single(saved.ExpandedFolders);
        Assert.Equal(0, saved.TreeVerticalOffset);
        Assert.Null(saved.Player!.PixelZoom);
        Assert.Equal(1, saved.Player.Speed);
        Assert.Equal(TimeSpan.Zero, saved.Player.Position);
    }

    [Fact]
    public void Selection_RetainsOnlyCurrentResultsAndRemapsShiftAnchor()
    {
        var root = Guid.NewGuid(); var a = Guid.NewGuid(); var b = Guid.NewGuid(); var c = Guid.NewGuid();
        var grid = new BrowserGridModel();
        grid.Populate([Entry(root, "a.mp4", a), Entry(root, "b.mp4", b), Entry(root, "c.mp4", c)]);
        grid.RestoreWorkspaceSelection(new() { SelectedAssetIds = [a, b, Guid.NewGuid()], AnchorAssetId = b });
        Assert.Equal(new[] { a, b }, grid.SelectedAssetIdsInBrowserOrder);
        grid.SelectRange(2);
        Assert.Equal(new[] { b, c }, grid.SelectedAssetIdsInBrowserOrder);
        grid.SetQuery(new() { SearchText = "a" });
        grid.RestoreWorkspaceSelection(new() { SelectedAssetIds = [a, b], AnchorAssetId = b });
        Assert.Equal(new[] { a }, grid.SelectedAssetIdsInBrowserOrder);
        Assert.Equal(a, grid.SelectionAnchorAssetId);
    }

    [Fact]
    public async Task Tree_RestoresUnrelatedBranchesOnceWithoutWalkingTheirDescendants()
    {
        var root = new MediaRootInfo(Guid.NewGuid(), "Library", @"D:\Remapped", MediaRootAvailability.Online);
        var tree = new BrowserTreeModel();
        var calls = new List<string>();
        var folders = new Folders((request, _) =>
        {
            var folder = request.RelativeFolder ?? ""; calls.Add(folder);
            return Task.FromResult(new MediaFolderEnumerationResult(MediaFolderEnumerationStatus.Succeeded, folder,
                folder == "" ? [Directory(root.RootId, "active"), Directory(root.RootId, "archive"), Directory(root.RootId, "other")]
                : folder == "archive" ? [Directory(root.RootId, "archive/2024")]
                : [Directory(root.RootId, folder + "/unvisited")]));
        });
        await WorkspaceTreeRestoration.RestoreAsync(tree, new Roots(root), folders,
            [new() { RootId = root.RootId, RelativeFolder = "" }, new() { RootId = root.RootId, RelativeFolder = "active" },
             new() { RootId = root.RootId, RelativeFolder = "archive/2024" }, new() { RootId = root.RootId, RelativeFolder = "missing" }],
            CancellationToken.None, action => action());
        Assert.Equal(new[] { "", "active", "archive", "archive/2024" }, calls);
        Assert.True(tree.FindByPath(@"D:\Remapped\archive\2024")!.IsExpanded);
        Assert.False(tree.FindByPath(@"D:\Remapped\other")!.IsMaterialized);
        Assert.Null(tree.SelectedNode);
    }

    [Fact]
    public async Task Tree_CollapseWhileEnumerationIsPendingRevokesLatePublish()
    {
        var root = new MediaRootInfo(Guid.NewGuid(), "Library", @"D:\Library", MediaRootAvailability.Online);
        var tree = new BrowserTreeModel();
        using var request = new WorkspaceRestorationRequest();
        var reply = new TaskCompletionSource<MediaFolderEnumerationResult>();
        var task = WorkspaceTreeRestoration.RestoreAsync(tree, new Roots(root), new Folders((_, _) => reply.Task),
            [new() { RootId = root.RootId }], request.Token, action => action());
        tree.Roots[0].IsExpanded = false;
        request.Cancel();
        reply.SetResult(new(MediaFolderEnumerationStatus.Succeeded, "", [Directory(root.RootId, "late")]));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.False(tree.Roots[0].IsExpanded);
        Assert.False(tree.Roots[0].IsMaterialized);
        Assert.Null(tree.FindByPath(@"D:\Library\late"));
    }

    internal static MediaFolderEntry Entry(Guid root, string path, Guid asset) =>
        new(root, path, path.ToUpperInvariant(), Path.GetFileName(path), false, new(MediaTypeCategory.Video), 10, DateTimeOffset.UnixEpoch, AssetId: asset);
    private static MediaFolderEntry Directory(Guid root, string path) => Entry(root, path, Guid.NewGuid()) with { IsDirectory = true };
    private static void WithFile(Action<string> action)
    {
        var path = Path.Combine(Path.GetTempPath(), $"workspace-247-{Guid.NewGuid()}.json");
        try { action(path); } finally { File.Delete(path); }
    }
    private sealed class Folders(Func<MediaFolderEnumerationRequest, CancellationToken, Task<MediaFolderEnumerationResult>> enumerate) : IMediaFolderEnumerator
    {
        public Task<MediaFolderEnumerationResult> EnumerateAsync(MediaFolderEnumerationRequest request, CancellationToken cancellationToken = default) => enumerate(request, cancellationToken);
    }
    private sealed class Roots(params MediaRootInfo[] roots) : IMediaRootService
    {
        public Task<IReadOnlyList<MediaRootInfo>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<MediaRootInfo>>(roots);
        public Task<MediaRootInfo?> GetAsync(Guid rootId, CancellationToken cancellationToken = default) => Task.FromResult(roots.FirstOrDefault(root => root.RootId == rootId));
        public Task<MediaRootChangeResult> CreateAsync(string displayName, string physicalPath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MediaRootChangeResult> RenameAsync(Guid rootId, string displayName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MediaRootChangeResult> RemapAsync(Guid rootId, string physicalPath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MediaPathResolution> ResolveAsync(Guid rootId, string relativePath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
