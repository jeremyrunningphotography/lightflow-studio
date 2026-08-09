using System.Windows;
using System.Windows.Controls;
using LightflowStudio;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class MediaPlaybackServiceTests
{
    [Fact]
    public async Task Open_LoadsPausedAtBackendDecodedTimestamp()
    {
        var backend = new FakePlaybackBackend();
        await using var service = new MediaPlaybackService(backend);

        await service.OpenAsync(Path.GetFullPath("one.mp4"));

        Assert.Equal(MediaPlaybackState.Paused, service.Snapshot.State);
        Assert.Equal(TimeSpan.FromMilliseconds(125), service.Snapshot.DisplayedTimestamp?.Position);
        Assert.True(service.Snapshot.DisplayedTimestamp?.IsDecodedPresentationTimestamp);
    }

    [Fact]
    public async Task Seek_PreservesPausedAndPlayingIntent()
    {
        var backend = new FakePlaybackBackend();
        await using var service = new MediaPlaybackService(backend);
        await service.OpenAsync(Path.GetFullPath("one.mp4"));

        await service.SeekAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(MediaPlaybackState.Paused, service.Snapshot.State);
        Assert.Equal(TimeSpan.FromSeconds(2), service.Snapshot.DisplayedTimestamp?.Position);

        await service.PlayAsync();
        await service.SeekAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(MediaPlaybackState.Playing, service.Snapshot.State);
        Assert.Equal(2, backend.PlayCalls);
    }

    [Fact]
    public async Task RapidSourceSwitch_LatestRequestWinsWithoutStalePresentation()
    {
        var backend = new FakePlaybackBackend { OpenDelay = TimeSpan.FromMilliseconds(40) };
        await using var service = new MediaPlaybackService(backend);
        var presented = new List<string?>();
        service.StateChanged += (_, snapshot) =>
        {
            if (snapshot.State == MediaPlaybackState.Paused) presented.Add(snapshot.SourcePath);
        };

        var a = service.OpenAsync(Path.GetFullPath("A.mp4"));
        var b = service.OpenAsync(Path.GetFullPath("B.mp4"));
        var c = service.OpenAsync(Path.GetFullPath("C.mp4"));
        await Task.WhenAll(IgnoreCancellation(a), IgnoreCancellation(b), c);

        Assert.EndsWith("C.mp4", service.Snapshot.SourcePath, StringComparison.OrdinalIgnoreCase);
        Assert.All(presented, path => Assert.EndsWith("C.mp4", path, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(1, backend.ActiveSessions);
    }

    [Fact]
    public async Task Coordinator_TransfersSingleGlobalPlaybackOwnership()
    {
        var backends = new List<FakePlaybackBackend>();
        await using var coordinator = new MediaPlaybackCoordinator(() =>
        {
            var backend = new FakePlaybackBackend();
            backends.Add(backend);
            return new MediaPlaybackService(backend);
        });
        var firstOwner = Guid.NewGuid();
        var secondOwner = Guid.NewGuid();
        await using var first = await coordinator.AcquireAsync(firstOwner);
        await first.Service.OpenAsync(Path.GetFullPath("A.mp4"));

        await using var second = await coordinator.AcquireAsync(secondOwner);

        Assert.Same(first.Service, second.Service);
        Assert.Equal(MediaPlaybackState.Empty, first.Service.Snapshot.State);
        Assert.Equal(0, Assert.Single(backends).ActiveSessions);
    }

    [Fact]
    public void ApplicationFacingContracts_DoNotExposeFlyleafTypes()
    {
        var contractTypes = new[]
        {
            typeof(IMediaPlaybackService), typeof(MediaPlaybackSnapshot), typeof(MediaPlaybackSourceInfo),
            typeof(MediaPresentationTimestamp), typeof(MediaDecodedFrame), typeof(MediaPlaybackError)
        };

        foreach (var type in contractTypes)
        {
            Assert.DoesNotContain("Flyleaf", type.FullName, StringComparison.OrdinalIgnoreCase);
            foreach (var method in type.GetMethods())
            {
                Assert.DoesNotContain("Flyleaf", method.ReturnType.FullName ?? "", StringComparison.OrdinalIgnoreCase);
                Assert.All(method.GetParameters(), parameter =>
                    Assert.DoesNotContain("Flyleaf", parameter.ParameterType.FullName ?? "", StringComparison.OrdinalIgnoreCase));
            }
        }
    }

    [Fact]
    public void DependencyLocator_RequiresSharedAvLibraries()
    {
        var root = Directory.CreateTempSubdirectory("lightflow-playback-").FullName;
        try
        {
            Assert.False(PlaybackDependencyLocator.IsValid(root));
            File.WriteAllText(Path.Combine(root, "avcodec-62.dll"), "");
            File.WriteAllText(Path.Combine(root, "avformat-62.dll"), "");
            File.WriteAllText(Path.Combine(root, "avutil-60.dll"), "");
            Assert.True(PlaybackDependencyLocator.IsValid(root));
        }
        finally { Directory.Delete(root, true); }
    }

    private static async Task IgnoreCancellation(Task task)
    {
        try { await task; } catch (OperationCanceledException) { }
    }

    private sealed class FakePlaybackBackend : IMediaPlaybackBackend
    {
        private CancellationTokenSource _pending = new();
        public TimeSpan OpenDelay { get; init; }
        public int ActiveSessions { get; private set; }
        public int PlayCalls { get; private set; }
        public event EventHandler<MediaPresentationTimestamp>? FramePresented { add { } remove { } }
        public event EventHandler<MediaPlaybackError>? Failed { add { } remove { } }
        public FrameworkElement CreatePresentationSurface() => new Border();
        public void CancelPending()
        {
            var next = new CancellationTokenSource();
            var old = Interlocked.Exchange(ref _pending, next);
            old.Cancel(); old.Dispose();
        }
        public async Task<PlaybackBackendOpened> OpenAsync(string sourcePath, CancellationToken token)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _pending.Token);
            if (OpenDelay > TimeSpan.Zero) await Task.Delay(OpenDelay, linked.Token);
            ActiveSessions = 1;
            var timestamp = new MediaPresentationTimestamp(TimeSpan.FromMilliseconds(125));
            return new(new(sourcePath, TimeSpan.FromSeconds(10), TimeSpan.Zero, 1920, 1080, [], null, false), timestamp);
        }
        public Task CloseAsync(CancellationToken token) { ActiveSessions = 0; return Task.CompletedTask; }
        public Task PlayAsync(CancellationToken token) { PlayCalls++; return Task.CompletedTask; }
        public Task PauseAsync(CancellationToken token) => Task.CompletedTask;
        public Task<MediaPresentationTimestamp> SeekAsync(TimeSpan position, CancellationToken token) => Task.FromResult(new MediaPresentationTimestamp(position));
        public Task<MediaPresentationTimestamp> StepForwardAsync(CancellationToken token) => Task.FromResult(new MediaPresentationTimestamp(TimeSpan.FromSeconds(1)));
        public Task<MediaPresentationTimestamp> StepBackwardAsync(CancellationToken token) => Task.FromResult(new MediaPresentationTimestamp(TimeSpan.Zero));
        public Task<MediaDecodedFrame> GetFrameAsync(TimeSpan position, CancellationToken token) => Task.FromResult(new MediaDecodedFrame(new(position), 1, 1, 4, [0, 0, 0, 255]));
        public ValueTask DisposeAsync() { ActiveSessions = 0; _pending.Dispose(); return ValueTask.CompletedTask; }
    }
}
