using LightflowStudio;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class MediaRootMonitoringTests : IDisposable
{
    private readonly string _temporary = Path.Combine(Path.GetTempPath(), $"lightflow-monitor-{Guid.NewGuid():N}");
    private readonly Guid _rootId = Guid.NewGuid();

    [Fact]
    public async Task CreateChangeDeleteAndRepeatedEventsCoalesceToOneAuthoritativeFolderRefresh()
    {
        var context = CreateContext();
        await context.Service.SynchronizeAsync();
        await context.Service.FlushAsync();
        context.Refresh.Requests.Clear();
        var file = Path.Combine(_temporary, "shoot", "clip.mp4");

        context.Watcher.Publish(new(_rootId, MediaRootChangeKind.Created, file));
        context.Watcher.Publish(new(_rootId, MediaRootChangeKind.Changed, file));
        context.Watcher.Publish(new(_rootId, MediaRootChangeKind.Changed, file));
        context.Watcher.Publish(new(_rootId, MediaRootChangeKind.Deleted, file));
        await context.Service.FlushAsync();

        var request = Assert.Single(context.Refresh.Requests);
        Assert.Equal(_rootId, request.RootId);
        Assert.Equal("shoot", request.RelativeFolder);
        await context.Service.DisposeAsync();
    }

    [Fact]
    public async Task FolderRefreshedFiresWithTheReconciledFolderSoBrowserCanRefreshAnOpenView()
    {
        var context = CreateContext();
        await context.Service.SynchronizeAsync();
        await context.Service.FlushAsync();
        context.Refresh.Requests.Clear();
        var notifications = new List<MediaFolderEnumerationRequest>();
        context.Service.FolderRefreshed += (_, request) => notifications.Add(request);
        var file = Path.Combine(_temporary, "shoot", "clip.mp4");

        context.Watcher.Publish(new(_rootId, MediaRootChangeKind.Created, file));
        await context.Service.FlushAsync();

        var notification = Assert.Single(notifications);
        Assert.Equal(_rootId, notification.RootId);
        Assert.Equal("shoot", notification.RelativeFolder);
        await context.Service.DisposeAsync();
    }

    [Fact]
    public async Task DebounceWaitsAndRepeatedHintsRestartOneFolderSubmission()
    {
        Directory.CreateDirectory(_temporary);
        var roots = new FakeRoots(new MediaRootInfo(_rootId, "Media", _temporary, MediaRootAvailability.Online));
        var refresh = new FakeRefresh();
        var factory = new FakeWatcherFactory();
        var service = new MediaRootMonitoringService(roots, refresh, factory, TimeSpan.FromMilliseconds(100));
        await service.StartAsync();
        var file = Path.Combine(_temporary, "shoot", "clip.mp4");

        factory.Created.Single().Publish(new(_rootId, MediaRootChangeKind.Changed, file));
        factory.Created.Single().Publish(new(_rootId, MediaRootChangeKind.Changed, file));
        Assert.Empty(refresh.Requests);
        await refresh.Submitted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Single(refresh.Requests);
        Assert.Equal("shoot", refresh.Requests[0].RelativeFolder);
        await service.DisposeAsync();
    }

    [Fact]
    public async Task RenameRefreshesBothFoldersWithoutInferringAssetIdentity()
    {
        var context = CreateContext();
        await context.Service.SynchronizeAsync();
        await context.Service.FlushAsync();
        context.Refresh.Requests.Clear();

        context.Watcher.Publish(new(_rootId, MediaRootChangeKind.Renamed,
            Path.Combine(_temporary, "new", "clip.mp4"), Path.Combine(_temporary, "old", "clip.mp4")));
        await context.Service.FlushAsync();

        Assert.Equal(["new", "old"], context.Refresh.Requests.Select(request => request.RelativeFolder).Order());
        await context.Service.DisposeAsync();
    }

    [Fact]
    public async Task WatcherErrorRequestsConservativeRootRefresh() =>
        await AssertWatcherFailure(MediaRootChangeKind.Error);

    [Fact]
    public async Task WatcherOverflowRequestsConservativeRootRefresh() =>
        await AssertWatcherFailure(MediaRootChangeKind.Overflow);

    private async Task AssertWatcherFailure(MediaRootChangeKind kind)
    {
        var context = CreateContext();
        await context.Service.SynchronizeAsync();
        await context.Service.FlushAsync();
        context.Refresh.Requests.Clear();

        context.Watcher.Publish(new(_rootId, kind, Diagnostic: "notifications lost"));
        await context.Service.FlushAsync();

        Assert.Null(Assert.Single(context.Refresh.Requests).RelativeFolder);
        Assert.Equal(MediaRootMonitorStatus.Degraded, context.Service.State.Status);
        Assert.Contains("notifications lost", context.Service.State.Diagnostic);
        await context.Service.DisposeAsync();
    }

    [Fact]
    public async Task OutsideAndLinkedPathsCannotBecomeTargetedRefreshes()
    {
        var context = CreateContext();
        await context.Service.SynchronizeAsync();
        await context.Service.FlushAsync();
        context.Refresh.Requests.Clear();

        context.Watcher.Publish(new(_rootId, MediaRootChangeKind.Created,
            Path.Combine(Path.GetDirectoryName(_temporary)!, "outside.mp4")));
        await context.Service.FlushAsync();

        Assert.Null(Assert.Single(context.Refresh.Requests).RelativeFolder);
        await context.Service.DisposeAsync();
    }

    [Fact]
    public async Task OfflineAndRemappedRootsDisposeAndRecreateWatchersThenRequestFallback()
    {
        var roots = new FakeRoots(new MediaRootInfo(_rootId, "Media", _temporary, MediaRootAvailability.Online));
        var refresh = new FakeRefresh();
        var factory = new FakeWatcherFactory();
        var service = new MediaRootMonitoringService(roots, refresh, factory, TimeSpan.FromHours(1));
        await service.SynchronizeAsync();
        await service.FlushAsync();
        refresh.Requests.Clear();
        var first = factory.Created.Single();

        roots.Roots = [new(_rootId, "Media", _temporary, MediaRootAvailability.Unavailable)];
        await service.SynchronizeAsync();
        Assert.True(first.Disposed);
        Assert.Equal(0, service.State.WatchedRoots);

        var remapped = Path.Combine(_temporary, "remapped");
        Directory.CreateDirectory(remapped);
        roots.Roots = [new(_rootId, "Media", remapped, MediaRootAvailability.Online)];
        await service.SynchronizeAsync();
        await service.FlushAsync();
        Assert.Equal(2, factory.Created.Count);
        Assert.Null(Assert.Single(refresh.Requests).RelativeFolder);
        await service.DisposeAsync();
    }

    [Fact]
    public async Task DisposalCancelsMonitoringAndRejectsLaterEvents()
    {
        var context = CreateContext();
        await context.Service.StartAsync();
        await context.Service.DisposeAsync();
        var count = context.Refresh.Requests.Count;

        context.Watcher.Publish(new(_rootId, MediaRootChangeKind.Changed, Path.Combine(_temporary, "clip.mp4")));

        Assert.Equal(MediaRootMonitorStatus.Stopped, context.Service.State.Status);
        Assert.Equal(count, context.Refresh.Requests.Count);
    }

    [Fact]
    public async Task StoppedRemainsTerminalWhenInFlightRefreshReturnsCanceledDuringDisposal()
    {
        Directory.CreateDirectory(_temporary);
        var roots = new FakeRoots(new MediaRootInfo(_rootId, "Media", _temporary, MediaRootAvailability.Online));
        var refresh = new BlockingRefresh();
        var factory = new FakeWatcherFactory();
        var service = new MediaRootMonitoringService(roots, refresh, factory, TimeSpan.Zero);
        var states = new List<MediaRootMonitorStatus>();
        service.StateChanged += (_, state) => states.Add(state.Status);
        await service.StartAsync();
        factory.Created.Single().Publish(new(_rootId, MediaRootChangeKind.Changed,
            Path.Combine(_temporary, "clip.mp4")));
        await refresh.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var disposal = service.DisposeAsync().AsTask();
        await disposal.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(MediaRootMonitorStatus.Stopped, service.State.Status);
        var stopped = states.LastIndexOf(MediaRootMonitorStatus.Stopped);
        Assert.True(stopped >= 0);
        Assert.DoesNotContain(MediaRootMonitorStatus.Degraded, states.Skip(stopped + 1));
        Assert.Equal(MediaRootMonitorStatus.Stopped, states[^1]);
    }

    [Fact]
    public async Task ManualAuthoritativeRefreshRemainsIndependentWhenWatcherCreationFails()
    {
        var roots = new FakeRoots(new MediaRootInfo(_rootId, "Media", _temporary, MediaRootAvailability.Online));
        var refresh = new FakeRefresh();
        var service = new MediaRootMonitoringService(roots, refresh, new ThrowingWatcherFactory());
        await service.SynchronizeAsync();

        var manual = await refresh.RefreshAsync(new(_rootId));

        Assert.True(manual.Reconciliation.Succeeded);
        Assert.Equal(MediaRootMonitorStatus.Degraded, service.State.Status);
        Assert.Single(refresh.Requests);
        await service.DisposeAsync();
    }

    private TestContext CreateContext()
    {
        Directory.CreateDirectory(_temporary);
        var roots = new FakeRoots(new MediaRootInfo(_rootId, "Media", _temporary, MediaRootAvailability.Online));
        var refresh = new FakeRefresh();
        var factory = new FakeWatcherFactory();
        var service = new MediaRootMonitoringService(roots, refresh, factory, TimeSpan.FromHours(1));
        return new(service, refresh, factory);
    }

    public void Dispose()
    {
        try { Directory.Delete(_temporary, true); } catch { }
    }

    private sealed record TestContext(MediaRootMonitoringService Service, FakeRefresh Refresh, FakeWatcherFactory Factory)
    {
        public FakeWatcher Watcher => Factory.Created.Single();
    }

    private sealed class FakeWatcherFactory : IMediaRootWatcherFactory
    {
        public List<FakeWatcher> Created { get; } = [];
        public IMediaRootWatcher Create(MediaRootInfo root, Action<MediaRootChange> publish)
        {
            var watcher = new FakeWatcher(publish);
            Created.Add(watcher);
            return watcher;
        }
    }

    private sealed class ThrowingWatcherFactory : IMediaRootWatcherFactory
    {
        public IMediaRootWatcher Create(MediaRootInfo root, Action<MediaRootChange> publish) =>
            throw new IOException("watch unavailable");
    }

    private sealed class FakeWatcher(Action<MediaRootChange>? publish = null) : IMediaRootWatcher
    {
        public bool Disposed { get; private set; }
        public void Start() { }
        public void Publish(MediaRootChange change) => publish?.Invoke(change);
        public void Dispose() => Disposed = true;
    }

    private sealed class FakeRefresh : IMediaDiscoveryRefreshService
    {
        public List<MediaFolderEnumerationRequest> Requests { get; } = [];
        public TaskCompletionSource Submitted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<MediaDiscoveryRefreshResult> RefreshAsync(MediaFolderEnumerationRequest request,
            DerivedWorkPriority priority = DerivedWorkPriority.Background, CancellationToken cancellationToken = default,
            CancellationToken derivedWorkCancellationToken = default)
        {
            Requests.Add(request);
            Submitted.TrySetResult();
            var result = new CatalogReconciliationResult(CatalogReconciliationStatus.Succeeded, request.RootId,
                request.RelativeFolder ?? string.Empty, []);
            return Task.FromResult(new MediaDiscoveryRefreshResult(result, null));
        }
    }

    private sealed class BlockingRefresh : IMediaDiscoveryRefreshService
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<MediaDiscoveryRefreshResult> RefreshAsync(MediaFolderEnumerationRequest request,
            DerivedWorkPriority priority = DerivedWorkPriority.Background, CancellationToken cancellationToken = default,
            CancellationToken derivedWorkCancellationToken = default)
        {
            Entered.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, derivedWorkCancellationToken); }
            catch (OperationCanceledException) when (derivedWorkCancellationToken.IsCancellationRequested) { }
            var canceled = new CatalogReconciliationResult(CatalogReconciliationStatus.Canceled, request.RootId,
                request.RelativeFolder ?? string.Empty, [], Diagnostic: "Monitoring shutdown canceled refresh.");
            return new(canceled, null, canceled.Diagnostic);
        }
    }

    private sealed class FakeRoots(params MediaRootInfo[] roots) : IMediaRootService
    {
        public IReadOnlyList<MediaRootInfo> Roots { get; set; } = roots;
        public Task<IReadOnlyList<MediaRootInfo>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult(Roots);
        public Task<MediaRootInfo?> GetAsync(Guid rootId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Roots.SingleOrDefault(root => root.RootId == rootId));
        public Task<MediaRootChangeResult> CreateAsync(string displayName, string physicalPath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MediaRootChangeResult> RenameAsync(Guid rootId, string displayName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MediaRootChangeResult> RemapAsync(Guid rootId, string physicalPath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MediaPathResolution> ResolveAsync(Guid rootId, string relativePath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
