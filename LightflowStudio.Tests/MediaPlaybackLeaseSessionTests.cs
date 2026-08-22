using System.Windows;
using Xunit;

namespace LightflowStudio.Tests;

/// <summary>
/// Tests the shared lease wrapper both TrimEditorPlayback (via inheritance, adding trim-boundary seeking) and
/// PlayerViewerHost (used directly, since Browser review has no In/Out concept) consume — see
/// TrimEditorPlaybackTests for the trim-boundary-seeking behavior layered on top.
/// </summary>
public sealed class MediaPlaybackLeaseSessionTests
{
    [Fact]
    public async Task Session_LoadsPausedSupportsPlaybackAndReleasesOwnershipOnDispose()
    {
        var backend = new FakeBackend();
        await using var coordinator = new MediaPlaybackCoordinator(() => new MediaPlaybackService(backend));
        await using (var session = new MediaPlaybackLeaseSession(coordinator))
        {
            var playback = await session.OpenAsync(Path.GetFullPath("clip.mp4"));
            Assert.Equal(MediaPlaybackState.Paused, playback.Snapshot.State);
            Assert.Same(playback, session.Service);

            await playback.PlayAsync();
            Assert.Equal(MediaPlaybackState.Playing, playback.Snapshot.State);
            await playback.PauseAsync();
            Assert.Equal(MediaPlaybackState.Paused, playback.Snapshot.State);

            await playback.SeekAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(TimeSpan.FromMilliseconds(10005), playback.Snapshot.DisplayedTimestamp!.Position);
            await playback.StepForwardAsync();
            Assert.Equal(FakeBackend.NextFrame, playback.Snapshot.DisplayedTimestamp!.Position);
            await playback.StepBackwardAsync();
            Assert.Equal(FakeBackend.PreviousFrame, playback.Snapshot.DisplayedTimestamp!.Position);
        }
        Assert.Equal(0, backend.ActiveSessions);
    }

    [Fact]
    public async Task RepeatedSessions_TransferTheOnePlaybackLeaseAndReleasePreviousSourceOnOpen()
    {
        var backend = new FakeBackend();
        await using var coordinator = new MediaPlaybackCoordinator(() => new MediaPlaybackService(backend));

        await using (var first = new MediaPlaybackLeaseSession(coordinator))
        {
            await first.OpenAsync(Path.GetFullPath("A.mp4"));
            Assert.Equal(1, backend.ActiveSessions);
        }
        Assert.Equal(0, backend.ActiveSessions);

        await using var second = new MediaPlaybackLeaseSession(coordinator);
        var playback = await second.OpenAsync(Path.GetFullPath("B.mp4"));
        Assert.Equal(Path.GetFullPath("B.mp4"), playback.Snapshot.SourcePath);
        Assert.Equal(1, backend.ActiveSessions);
    }

    [Fact]
    public async Task Session_ReopeningWithoutDisposeReusesTheSameLease()
    {
        var backend = new FakeBackend();
        await using var coordinator = new MediaPlaybackCoordinator(() => new MediaPlaybackService(backend));
        await using var session = new MediaPlaybackLeaseSession(coordinator);

        await session.OpenAsync(Path.GetFullPath("A.mp4"));
        var serviceAfterFirstOpen = session.Service;
        await session.OpenAsync(Path.GetFullPath("B.mp4"));

        Assert.Same(serviceAfterFirstOpen, session.Service);
        Assert.Equal(Path.GetFullPath("B.mp4"), session.Service!.Snapshot.SourcePath);
    }

    private sealed class FakeBackend : IMediaPlaybackBackend
    {
        public static readonly TimeSpan NextFrame = TimeSpan.FromMilliseconds(10100);
        public static readonly TimeSpan PreviousFrame = TimeSpan.FromMilliseconds(9900);
        public int ActiveSessions { get; private set; }
        public event EventHandler<MediaPresentationTimestamp>? FramePresented { add { } remove { } }
        public event EventHandler<MediaPlaybackError>? Failed { add { } remove { } }
        public int Volume { get; set; } = 100;
        public bool Mute { get; set; }
        public FrameworkElement CreatePresentationSurface() => new();
        public void ReleasePresentationSurface(FrameworkElement surface) { }
        public void CancelPending() { }
        public Task<PlaybackBackendOpened> OpenAsync(string sourcePath, CancellationToken token)
        {
            ActiveSessions = 1;
            var source = new MediaPlaybackSourceInfo(sourcePath, TimeSpan.FromSeconds(60), TimeSpan.Zero, 1920, 1080, [], null, false);
            return Task.FromResult(new PlaybackBackendOpened(source, new(TimeSpan.Zero)));
        }
        public Task CloseAsync(CancellationToken token) { ActiveSessions = 0; return Task.CompletedTask; }
        public Task PlayAsync(CancellationToken token) => Task.CompletedTask;
        public Task PauseAsync(CancellationToken token) => Task.CompletedTask;
        public Task<MediaPresentationTimestamp> SeekAsync(TimeSpan position, CancellationToken token) =>
            Task.FromResult(new MediaPresentationTimestamp(position + TimeSpan.FromMilliseconds(5)));
        public Task<MediaPresentationTimestamp> StepForwardAsync(CancellationToken token) => Task.FromResult(new MediaPresentationTimestamp(NextFrame));
        public Task<MediaPresentationTimestamp> StepBackwardAsync(CancellationToken token) => Task.FromResult(new MediaPresentationTimestamp(PreviousFrame));
        public Task<MediaDecodedFrame> GetFrameAsync(TimeSpan position, CancellationToken token) => throw new NotSupportedException();
        public ValueTask DisposeAsync() { ActiveSessions = 0; return ValueTask.CompletedTask; }
    }
}
