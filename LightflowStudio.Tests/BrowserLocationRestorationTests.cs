using LightflowStudio;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class BrowserLocationRestorationTests
{
    [Fact]
    public void AncestorRelativeFolderCandidates_WalksFromTheFullPathUpToTheRootItself()
    {
        var candidates = BrowserLocationRestoration.AncestorRelativeFolderCandidates("Trips/Iceland/RAW");

        Assert.Equal(["Trips/Iceland/RAW", "Trips/Iceland", "Trips", ""], candidates);
    }

    [Fact]
    public void AncestorRelativeFolderCandidates_TreatsTheRootLocationAsItsOwnOnlyCandidate()
    {
        Assert.Equal([""], BrowserLocationRestoration.AncestorRelativeFolderCandidates(""));
    }

    [Fact]
    public async Task RestoreAsync_NoSavedLocationDoesNothing()
    {
        using var session = Session(new FakeRoots(), new FakeDiscovery(), EmptyFolders(), new FakeFileSystem());

        var result = await BrowserLocationRestoration.RestoreAsync(session, new FakeRoots(), null);

        Assert.Equal(BrowserRestorationOutcome.NoSavedLocation, result.Outcome);
        Assert.Null(result.State);
    }

    [Fact]
    public async Task RestoreAsync_UnknownRootFallsBackToTheNormalStorageLocationStateWithoutError()
    {
        var roots = new FakeRoots();
        using var session = Session(roots, new FakeDiscovery(), EmptyFolders(), new FakeFileSystem());

        var result = await BrowserLocationRestoration.RestoreAsync(session, roots,
            new() { RootId = Guid.NewGuid(), RelativeFolder = "" });

        Assert.Equal(BrowserRestorationOutcome.RootNotFound, result.Outcome);
        Assert.Null(result.State);
    }

    [Fact]
    public async Task RestoreAsync_IsRootAgnosticAndFollowsRootIdEvenWhenThePhysicalPathHasBeenRemapped()
    {
        var root = Root("Library", @"D:\NewDrive\Library");
        var roots = new FakeRoots(root);
        var fileSystem = new FakeFileSystem(@"D:\NewDrive\Library", @"D:\NewDrive\Library\Trips");
        var discovery = new FakeDiscovery();
        var folders = new FakeFolders((request, _) => Task.FromResult(Success(request, FileEntry(root.RootId, "Trips/clip.mp4"))));
        using var session = Session(roots, discovery, folders, fileSystem);

        // The saved diagnostic path still points at the machine's OLD mapping (e.g. before a Media Root remap);
        // restoration must resolve through RootId + RelativeFolder, never by trusting this stale absolute path.
        var saved = new WorkspaceBrowserLocationState
        {
            RootId = root.RootId,
            RelativeFolder = "Trips",
            LastResolvedAbsolutePath = @"C:\OldDrive\Library\Trips"
        };

        var result = await BrowserLocationRestoration.RestoreAsync(session, roots, saved);

        Assert.Equal(BrowserRestorationOutcome.Restored, result.Outcome);
        Assert.Equal(BrowserFolderStatus.Ready, result.State!.Status);
        Assert.Equal(root.RootId, result.State.Location!.RootId);
        Assert.Equal("Trips", result.State.Location.RelativeFolder);
        Assert.Equal(@"D:\NewDrive\Library\Trips", result.State.Location.AbsolutePath);
    }

    [Fact]
    public Task RestoreAsync_UnavailableMappedRootPreservesLocationContextAsTheExistingHonestUnavailableState() =>
        AssertOfflineRootIsPreserved(MediaRootAvailability.Unavailable);

    [Fact]
    public Task RestoreAsync_UnmappedRootPreservesLocationContextAsTheExistingHonestUnavailableState() =>
        AssertOfflineRootIsPreserved(MediaRootAvailability.Unmapped);

    private static async Task AssertOfflineRootIsPreserved(MediaRootAvailability availability)
    {
        var physicalPath = availability == MediaRootAvailability.Unmapped ? null : @"E:\Archive";
        var root = Root("Archive", null) with { Availability = availability, PhysicalPath = physicalPath, Diagnostic = "Not connected here." };
        var roots = new FakeRoots(root);
        var discovery = new FakeDiscovery();
        var folders = new FakeFolders((request, _) => Task.FromResult(Success(request)));
        using var session = Session(roots, discovery, folders, new FakeFileSystem());

        var result = await BrowserLocationRestoration.RestoreAsync(session, roots,
            new() { RootId = root.RootId, RelativeFolder = "Trips/Iceland" });

        Assert.Equal(BrowserRestorationOutcome.RootUnavailable, result.Outcome);
        Assert.Equal(BrowserFolderStatus.RootUnavailable, result.State!.Status);
        Assert.Equal("Not connected here.", result.State.Diagnostic);
        Assert.Empty(discovery.Requests);
    }

    [Fact]
    public async Task RestoreAsync_DeletedDeepFolderFallsBackToTheNearestStillValidAncestor()
    {
        var root = Root("Library", @"C:\Library");
        var roots = new FakeRoots(root);
        // Only the root and "Trips" still exist on disk; "Trips/Iceland/RAW" (the saved folder) was deleted.
        var fileSystem = new FakeFileSystem(@"C:\Library", @"C:\Library\Trips");
        var discovery = new FakeDiscovery();
        var folders = new FakeFolders((request, _) => Task.FromResult(Success(request, FileEntry(root.RootId, "Trips/clip.mp4"))));
        using var session = Session(roots, discovery, folders, fileSystem);

        var result = await BrowserLocationRestoration.RestoreAsync(session, roots,
            new() { RootId = root.RootId, RelativeFolder = "Trips/Iceland/RAW" });

        Assert.Equal(BrowserRestorationOutcome.Restored, result.Outcome);
        Assert.Equal("Trips", result.State!.Location!.RelativeFolder);
        // Exactly one reconciliation happened, for the ancestor that actually resolved — the deleted deeper
        // candidates fail at the cheap existence check and never reach reconciliation/enumeration at all.
        Assert.Equal("Trips", Assert.Single(discovery.Requests).RelativeFolder);
    }

    [Fact]
    public async Task RestoreAsync_FallsBackToTheRootItselfWhenTheEntireSavedFolderChainIsGone()
    {
        var root = Root("Library", @"C:\Library");
        var roots = new FakeRoots(root);
        var fileSystem = new FakeFileSystem(@"C:\Library");
        var folders = new FakeFolders((request, _) => Task.FromResult(Success(request)));
        using var session = Session(roots, new FakeDiscovery(), folders, fileSystem);

        var result = await BrowserLocationRestoration.RestoreAsync(session, roots,
            new() { RootId = root.RootId, RelativeFolder = "Trips/Iceland" });

        Assert.Equal(BrowserRestorationOutcome.Restored, result.Outcome);
        Assert.Equal("", result.State!.Location!.RelativeFolder);
    }

    [Fact]
    public async Task RestoreAsync_NothingResolvableFallsBackToTheNormalStorageLocationStateRatherThanBlockingStartup()
    {
        var root = Root("Library", @"C:\Library");
        var roots = new FakeRoots(root);
        // The root itself reports online, but even it cannot currently be listed (e.g. a transient failure).
        var folders = new FakeFolders((request, _) => Task.FromResult(
            new MediaFolderEnumerationResult(MediaFolderEnumerationStatus.AccessDenied, request.RelativeFolder ?? "", [], "Access denied.")));
        using var session = Session(roots, new FakeDiscovery(), folders, new FakeFileSystem(@"C:\Library"));

        var result = await BrowserLocationRestoration.RestoreAsync(session, roots,
            new() { RootId = root.RootId, RelativeFolder = "" });

        Assert.Equal(BrowserRestorationOutcome.Unresolvable, result.Outcome);
        Assert.Null(result.State);
    }

    private static BrowserNavigationSession Session(FakeRoots roots, FakeDiscovery discovery, FakeFolders folders,
        FakeFileSystem fileSystem) => new(roots, new BrowserLocationResolver(roots, fileSystem), discovery, folders);

    private static FakeFolders EmptyFolders() => new((request, _) => Task.FromResult(Success(request)));

    private static MediaRootInfo Root(string name, string? path) =>
        new(Guid.NewGuid(), name, path, path is null ? MediaRootAvailability.Unmapped : MediaRootAvailability.Online);

    private static MediaFolderEntry FileEntry(Guid rootId, string path) =>
        new(rootId, path, path.ToUpperInvariant(), Path.GetFileName(path), false,
            new(MediaTypeCategory.Video, "mp4"), 10, DateTimeOffset.UtcNow);

    private static MediaFolderEnumerationResult Success(MediaFolderEnumerationRequest request,
        params MediaFolderEntry[] entries) => new(MediaFolderEnumerationStatus.Succeeded, request.RelativeFolder ?? "", entries);

    private sealed class FakeFileSystem(params string[] existingDirectories) : IBrowserLocationFileSystem
    {
        private readonly HashSet<string> _existing = new(
            existingDirectories.Select(MediaPathSemantics.NormalizeRootPath), StringComparer.OrdinalIgnoreCase);

        public bool DirectoryExists(string path) => _existing.Contains(MediaPathSemantics.NormalizeRootPath(path));
    }

    private sealed class FakeRoots(params MediaRootInfo[] initial) : IMediaRootService
    {
        private readonly List<MediaRootInfo> _roots = [.. initial];
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
        public List<MediaFolderEnumerationRequest> Requests { get; } = [];
        public Task<MediaDiscoveryRefreshResult> RefreshAsync(MediaFolderEnumerationRequest request,
            DerivedWorkPriority priority = DerivedWorkPriority.Background, CancellationToken cancellationToken = default,
            CancellationToken derivedWorkCancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(new MediaDiscoveryRefreshResult(
                new(CatalogReconciliationStatus.Succeeded, request.RootId, request.RelativeFolder ?? "", []), null));
        }
    }

    private sealed class FakeFolders(Func<MediaFolderEnumerationRequest, CancellationToken, Task<MediaFolderEnumerationResult>> enumerate)
        : IMediaFolderEnumerator
    {
        public Task<MediaFolderEnumerationResult> EnumerateAsync(MediaFolderEnumerationRequest request, CancellationToken cancellationToken = default) =>
            enumerate(request, cancellationToken);
    }
}
