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

    private static BrowserNavigationSession Session(FakeRoots roots, FakeDiscovery discovery, FakeFolders folders) =>
        new(roots, new BrowserLocationResolver(roots, new ExistingFileSystem()), discovery, folders);

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
