using LightflowStudio;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class BrowserNavigationTests
{
    [Fact]
    public async Task Navigate_UsesAuthoritativeRefreshThenListsFolder()
    {
        var root = Root("Library");
        var discovery = new FakeDiscovery();
        var folders = new FakeFolders((request, _) => Task.FromResult(Success(request,
            DirectoryEntry(root.RootId, "Trips"), FileEntry(root.RootId, "clip.mp4"))));
        using var session = new BrowserNavigationSession(new FakeRoots(root), discovery, folders);

        var result = await session.NavigateToRootAsync(root.RootId);

        Assert.NotNull(result);
        Assert.Equal(BrowserFolderStatus.Ready, result.Status);
        Assert.Equal(["Trips", "clip.mp4"], result.Entries.Select(item => item.Name));
        Assert.Equal(DerivedWorkPriority.Visible, discovery.Priorities.Single());
        Assert.Equal(root.RootId, discovery.Requests.Single().RootId);
        Assert.Single(folders.Requests);
    }

    [Fact]
    public async Task NestedNavigation_SupportsUpBackForwardAndRefreshWithoutDuplicatingHistory()
    {
        var root = Root("Library");
        var discovery = new FakeDiscovery();
        var folders = new FakeFolders((request, _) => Task.FromResult(Success(request,
            request.RelativeFolder is null ? DirectoryEntry(root.RootId, "Trips") : FileEntry(root.RootId, "Trips/clip.mp4"))));
        using var session = new BrowserNavigationSession(new FakeRoots(root), discovery, folders);
        await session.NavigateToRootAsync(root.RootId);
        await session.NavigateToFolderAsync(DirectoryEntry(root.RootId, "Trips"));

        Assert.Equal("Trips", session.State.Location!.RelativeFolder);
        Assert.True(session.State.CanGoBack);
        Assert.True(session.State.CanGoUp);

        await session.UpAsync();
        Assert.Equal("", session.State.Location!.RelativeFolder);
        await session.BackAsync();
        Assert.Equal("Trips", session.State.Location!.RelativeFolder);
        await session.ForwardAsync();
        Assert.Equal("", session.State.Location!.RelativeFolder);

        var beforeRefresh = folders.Requests.Count;
        await session.RefreshAsync();
        Assert.Equal(beforeRefresh + 1, folders.Requests.Count);
        Assert.True(session.State.CanGoBack);
        Assert.False(session.State.CanGoForward);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task UnavailableRoot_IsReportedWithoutDiscoveryOrEnumeration(int availabilityValue)
    {
        var availability = availabilityValue == 1 ? MediaRootAvailability.Unavailable : MediaRootAvailability.Unmapped;
        var root = Root("Archive") with { Availability = availability, Diagnostic = "Drive is unavailable." };
        var discovery = new FakeDiscovery();
        var folders = new FakeFolders((request, _) => Task.FromResult(Success(request)));
        using var session = new BrowserNavigationSession(new FakeRoots(root), discovery, folders);

        var result = await session.NavigateToRootAsync(root.RootId);

        Assert.Equal(BrowserFolderStatus.RootUnavailable, result!.Status);
        Assert.Equal("Drive is unavailable.", result.Diagnostic);
        Assert.Empty(discovery.Requests);
        Assert.Empty(folders.Requests);
    }

    [Fact]
    public async Task FailedEnumeration_RemainsHonestAndDoesNotInferAnEmptyFolder()
    {
        var root = Root("Library");
        var folders = new FakeFolders((request, _) => Task.FromResult(new MediaFolderEnumerationResult(
            MediaFolderEnumerationStatus.AccessDenied, request.RelativeFolder ?? "", [], "Access denied.")));
        using var session = new BrowserNavigationSession(new FakeRoots(root), new FakeDiscovery(), folders);

        var result = await session.NavigateToRootAsync(root.RootId);

        Assert.Equal(BrowserFolderStatus.AccessDenied, result!.Status);
        Assert.Equal("Access denied.", result.Diagnostic);
        Assert.Empty(result.Entries);
    }

    [Fact]
    public async Task RapidRootSwitch_LatestRequestWinsAndSuppressesStalePublication()
    {
        var first = Root("First");
        var second = Root("Second");
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var discovery = new FakeDiscovery(async (request, _, _, _) =>
        {
            if (request.RootId == first.RootId)
            {
                firstEntered.SetResult();
                await releaseFirst.Task; // Deliberately ignore cancellation to exercise generation suppression.
            }
            return DiscoverySuccess(request);
        });
        var folders = new FakeFolders((request, _) => Task.FromResult(Success(request,
            FileEntry(request.RootId, request.RootId == first.RootId ? "old.mp4" : "new.mp4"))));
        using var session = new BrowserNavigationSession(new FakeRoots(first, second), discovery, folders);

        var oldRequest = session.NavigateToRootAsync(first.RootId);
        await firstEntered.Task;
        var latest = await session.NavigateToRootAsync(second.RootId);
        releaseFirst.SetResult();
        var obsolete = await oldRequest;

        Assert.Null(obsolete);
        Assert.Equal(second.RootId, latest!.Location!.RootId);
        Assert.Equal("new.mp4", latest.Entries.Single().Name);
        Assert.Equal(second.RootId, session.State.Location!.RootId);
    }

    [Fact]
    public async Task CanceledNavigation_DoesNotReplaceThePreviouslyAcceptedLocation()
    {
        var first = Root("First");
        var second = Root("Second");
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
        var folders = new FakeFolders((request, _) => Task.FromResult(Success(request)));
        using var session = new BrowserNavigationSession(new FakeRoots(first, second), discovery, folders);
        await session.NavigateToRootAsync(first.RootId);
        using var cancellation = new CancellationTokenSource();

        var canceledNavigation = session.NavigateToRootAsync(second.RootId, cancellation.Token);
        await entered.Task;
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceledNavigation);
        Assert.Equal(first.RootId, session.State.Location!.RootId);
    }

    private static MediaRootInfo Root(string name) =>
        new(Guid.NewGuid(), name, $"C:\\{name}", MediaRootAvailability.Online, null);

    private static MediaFolderEntry DirectoryEntry(Guid rootId, string path) =>
        new(rootId, path, path.ToUpperInvariant(), Path.GetFileName(path), true,
            new(MediaTypeCategory.Unknown), null, DateTimeOffset.UtcNow);

    private static MediaFolderEntry FileEntry(Guid rootId, string path) =>
        new(rootId, path, path.ToUpperInvariant(), Path.GetFileName(path), false,
            new(MediaTypeCategory.Video, "mp4"), 10, DateTimeOffset.UtcNow);

    private static MediaFolderEnumerationResult Success(MediaFolderEnumerationRequest request,
        params MediaFolderEntry[] entries) =>
        new(MediaFolderEnumerationStatus.Succeeded, request.RelativeFolder ?? "", entries);

    private static MediaDiscoveryRefreshResult DiscoverySuccess(MediaFolderEnumerationRequest request) =>
        new(new(CatalogReconciliationStatus.Succeeded, request.RootId, request.RelativeFolder ?? "", []), null);

    private sealed class FakeRoots(params MediaRootInfo[] roots) : IMediaRootService
    {
        public Task<IReadOnlyList<MediaRootInfo>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MediaRootInfo>>(roots);
        public Task<MediaRootInfo?> GetAsync(Guid rootId, CancellationToken cancellationToken = default) =>
            Task.FromResult(roots.FirstOrDefault(root => root.RootId == rootId));
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
        public List<DerivedWorkPriority> Priorities { get; } = [];
        public Task<MediaDiscoveryRefreshResult> RefreshAsync(MediaFolderEnumerationRequest request, DerivedWorkPriority priority = DerivedWorkPriority.Background, CancellationToken cancellationToken = default, CancellationToken derivedWorkCancellationToken = default)
        {
            Requests.Add(request);
            Priorities.Add(priority);
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
