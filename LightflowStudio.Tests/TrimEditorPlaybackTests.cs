using System.Windows;
using System.Windows.Controls;
using LightflowStudio;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class TrimEditorPlaybackTests
{
    [Fact]
    public async Task EditorSession_LoadsPausedUsesSettledTimestampsAndReleasesOwnership()
    {
        var backend = new FakeBackend();
        await using var coordinator = new MediaPlaybackCoordinator(() => new MediaPlaybackService(backend));
        await using (var editor = new TrimEditorPlayback(coordinator))
        {
            var playback = await editor.OpenAsync(Path.GetFullPath("source.mp4"));
            Assert.Equal(MediaPlaybackState.Paused, playback.Snapshot.State);
            await playback.PlayAsync();
            Assert.Equal(MediaPlaybackState.Playing, playback.Snapshot.State);
            await playback.PauseAsync();
            await playback.SeekAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(TimeSpan.FromMilliseconds(10005), playback.Snapshot.DisplayedTimestamp!.Position);
            await playback.StepForwardAsync();
            Assert.Equal(FakeBackend.NextFrame, playback.Snapshot.DisplayedTimestamp!.Position);
            await playback.StepBackwardAsync();
            Assert.Equal(FakeBackend.PreviousFrame, playback.Snapshot.DisplayedTimestamp!.Position);

            var draft = new TrimSelection(playback.SourceInfo!.Duration);
            Assert.True(draft.SetIn(playback.Snapshot.DisplayedTimestamp.Position));
            Assert.Equal(FakeBackend.PreviousFrame, draft.In);
        }
        Assert.Equal(0, backend.ActiveSessions);
    }

    [Fact]
    public async Task RepeatedEditorSessions_ReleaseAndReattachTheReusablePresentationSurface()
    {
        await StaDispatcher.RunAsync(async () =>
        {
            var backend = new FakeBackend();
            await using var coordinator = new MediaPlaybackCoordinator(() => new MediaPlaybackService(backend));
            var surfaces = new List<FrameworkElement>();
            foreach (var source in new[] { "same.mp4", "same.mp4", "different.mp4", "same.mp4" })
            {
                await using var editor = new TrimEditorPlayback(coordinator);
                var playback = await editor.OpenAsync(Path.GetFullPath(source));
                using (var view = new MediaPlaybackView(playback))
                {
                    Assert.True(backend.PresentationAttached);
                    Assert.Same(backend.ActiveSurface, view.Content);
                    surfaces.Add((FrameworkElement)view.Content);
                }
                Assert.False(backend.PresentationAttached);
            }

            Assert.Equal(4, backend.PresentationAttachCount);
            Assert.Equal(4, backend.PresentationReleaseCount);
            Assert.Equal(4, surfaces.Distinct().Count());
            Assert.Equal(0, backend.ActiveSessions);
        });
    }

    private sealed class FakeBackend : IMediaPlaybackBackend
    {
        public static readonly TimeSpan NextFrame = TimeSpan.FromMilliseconds(10100);
        public static readonly TimeSpan PreviousFrame = TimeSpan.FromMilliseconds(9900);
        public int ActiveSessions { get; private set; }
        public FrameworkElement? ActiveSurface { get; private set; }
        public bool PresentationAttached { get; private set; }
        public int PresentationAttachCount { get; private set; }
        public int PresentationReleaseCount { get; private set; }
        public event EventHandler<MediaPresentationTimestamp>? FramePresented { add { } remove { } }
        public event EventHandler<MediaPlaybackError>? Failed { add { } remove { } }
        public FrameworkElement CreatePresentationSurface()
        {
            if (PresentationAttached) throw new InvalidOperationException("The previous editor still owns the presentation surface.");
            PresentationAttached = true;
            PresentationAttachCount++;
            ActiveSurface = new Border();
            return ActiveSurface;
        }
        public void ReleasePresentationSurface(FrameworkElement surface)
        {
            Assert.Same(ActiveSurface, surface);
            PresentationAttached = false;
            ActiveSurface = null;
            PresentationReleaseCount++;
        }
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
