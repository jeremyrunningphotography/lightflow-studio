using Lightflow.Actions;
using System.Windows;
using System.Windows.Controls.Primitives;
using Xunit;

namespace LightflowStudio.Tests;

public sealed partial class PlayerViewerHostLeaseTests
{
    [Fact]
    public async Task PresentationControllerAndUiShareAudioSpeedViewportAndTransientState()
    {
        await StaDispatcher.RunAsync(async () => {
            TestWpfApplication.EnsureLoaded();
            var backend = new FakeBackend(hasAudio: true);
            await using var coordinator = new MediaPlaybackCoordinator(() => new MediaPlaybackService(backend));
            var host = new PlayerViewerHost(coordinator); var window = CreateSubclipWindow(host);
            window.ShowActivated = false; window.Left = -32000; window.Opacity = 0; window.Show();
            try {
                var asset = ReviewAsset("presentation.mp4"); await host.OpenAsync(asset, ReviewPath(asset)); window.UpdateLayout();
                async Task<ActionResult> Call(string id, ActionArguments arguments, bool repeat = false) =>
                    await host.PresentationActions.InvokeAsync(SemanticCall(host, id, arguments) with { IsRepeat = repeat });
                Assert.Equal(ActionOutcome.Completed, (await Call(ReviewPresentationActions.Volume, new VolumeArguments(AdjustmentMode.Set, 45))).Outcome);
                Assert.Equal(45, backend.Volume); Assert.Equal(45, host.VolumeSlider.Value);
                host.VolumeSlider.Value = 55; Assert.Equal(55, backend.Volume);
                await Call(ReviewPresentationActions.Volume, new VolumeArguments(AdjustmentMode.Relative, 100)); Assert.Equal(100, backend.Volume);
                Assert.Equal(ActionOutcome.NoChange, (await Call(ReviewPresentationActions.Volume, new VolumeArguments(AdjustmentMode.Relative, 1))).Outcome);
                await Call(ReviewPresentationActions.Toggle, new PresentationToggleArguments(PresentationToggle.Mute)); Assert.True(backend.Mute);
                host.MuteButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); Assert.False(backend.Mute);
                host.CadenceChoiceBox.SelectedItem = host.CadenceChoiceBox.Items.Cast<CadenceChoice>().Single(choice => choice.Label == "24");
                foreach (var speed in Enum.GetValues<ReviewSpeed>()) {
                    Assert.Equal(ActionOutcome.Completed, (await Call(ReviewPresentationActions.Speed, new SpeedArguments(speed))).Outcome);
                    Assert.Equal(PlaybackReviewOptions.Speeds[(int)speed], backend.Options.Speed);
                    Assert.Equal(5, backend.Options.FrameDivisor);
                }
                await Call(ReviewPresentationActions.Zoom, new ZoomArguments(ReviewZoom.ActualPixels)); Assert.True(backend.Viewport.Zoom > 1);
                host.PanViewport(100, 100);
                await Call(ReviewPresentationActions.Zoom, new ZoomArguments(ReviewZoom.Fit)); Assert.Equal(new ViewerViewport(), backend.Viewport);
                await Call(ReviewPresentationActions.StepZoom, new LevelArguments(1), repeat: true); Assert.NotEqual(0, host.ZoomChoice.SelectedIndex);
                await Call(ReviewPresentationActions.Toggle, new PresentationToggleArguments(PresentationToggle.Loop)); Assert.True(host.LoopChoice.IsChecked);
                Assert.Equal(ActionOutcome.NoChange, (await Call(ReviewPresentationActions.Toggle, new PresentationToggleArguments(PresentationToggle.Loop), repeat: true)).Outcome);
                await Call(ReviewPresentationActions.Toggle, new PresentationToggleArguments(PresentationToggle.Filmstrip)); Assert.False(host.FilmstripVisible);
                var opens = backend.OpenPresentationOperations.Count(x => x == "open");
                await Call(ReviewPresentationActions.Toggle, new PresentationToggleArguments(PresentationToggle.Fullscreen)); Assert.True(host.IsFullscreen);
                await Call(ReviewPresentationActions.Toggle, new PresentationToggleArguments(PresentationToggle.Fullscreen)); Assert.False(host.IsFullscreen);
                Assert.Equal(opens, backend.OpenPresentationOperations.Count(x => x == "open"));
                var stale = SemanticCall(host, ReviewPresentationActions.Volume, new VolumeArguments(AdjustmentMode.Set, 0));
                await host.OpenAsync(asset with { Name = "replacement" }, ReviewPath(asset));
                Assert.Equal(ActionOutcome.Superseded, (await host.PresentationActions.InvokeAsync(stale)).Outcome); Assert.Equal(100, backend.Volume);
                Assert.False(host.LoopChoice.IsChecked); Assert.Equal(new PlaybackReviewOptions(), backend.Options);
                window.IsEnabled = false;
                Assert.Equal(ActionUnavailableReason.ModalInteraction, (await Call(ReviewPresentationActions.Speed, new SpeedArguments(ReviewSpeed.Normal))).Reason);
                window.IsEnabled = true;
                host.ActionPresentationActive = () => false;
                Assert.Equal(ActionUnavailableReason.InactivePresentation, (await Call(ReviewPresentationActions.Zoom, new ZoomArguments(ReviewZoom.Fit))).Reason);
            } finally { window.IsEnabled = true; await host.CloseAsync(); window.Close(); }
        });
    }

    [Fact]
    public async Task PresentationWaitsForVideoAttachmentAndSupportsReadyStillImages()
    {
        await StaDispatcher.RunAsync(async () => {
            TestWpfApplication.EnsureLoaded(); var backend = new FakeBackend();
            await using var coordinator = new MediaPlaybackCoordinator(() => new MediaPlaybackService(backend));
            PlayerViewerHost? host = null; var checkedLoading = false;
            host = new PlayerViewerHost(coordinator, openMilestone: milestone => {
                if (milestone != PlayerOpenMilestone.PresentationSurfaceCreated) return;
                checkedLoading = true;
                Assert.False(host!.PresentationActions.Eligibility(ReviewPresentationActions.Zoom, host.ActionTarget, new ZoomArguments(ReviewZoom.Fit)).Available);
                Assert.True(host.PresentationActions.Eligibility(ReviewPresentationActions.Toggle, host.ActionTarget, new PresentationToggleArguments(PresentationToggle.Fullscreen)).Available);
                host.ToggleFullscreen(); Assert.True(host.IsFullscreen); host.ToggleFullscreen(); Assert.False(host.IsFullscreen);
            });
            var window = CreateSubclipWindow(host); window.ShowActivated = false; window.Left = -32000; window.Opacity = 0; window.Show();
            var folder = Directory.CreateTempSubdirectory("lightflow-352-still-").FullName;
            try {
                var asset = ReviewAsset("loading.mp4"); await host.OpenAsync(asset, ReviewPath(asset)); Assert.True(checkedLoading);
                var path = Path.Combine(folder, "still.png");
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(System.Windows.Media.Imaging.BitmapSource.Create(1920, 1080, 96, 96,
                    System.Windows.Media.PixelFormats.Bgr24, null, new byte[1920 * 1080 * 3], 1920 * 3)));
                using (var stream = File.Create(path)) encoder.Save(stream);
                var photo = new PlayerViewerAsset(Guid.NewGuid(), "still.png", "STILL.PNG", "still.png", MediaPresentationKind.Image, Guid.NewGuid());
                await host.OpenAsync(photo, new(photo.RootId, photo.RelativePath, photo.Key, path, MediaRootAvailability.Online, true)); window.UpdateLayout();
                Assert.Equal(ActionOutcome.Completed, (await host.PresentationActions.InvokeAsync(SemanticCall(host, ReviewPresentationActions.Zoom, new ZoomArguments(ReviewZoom.ActualPixels)))).Outcome);
                Assert.Equal(2, host.ZoomChoice.SelectedIndex);
                Assert.Equal(ActionUnavailableReason.SourceUnavailable, (await host.PresentationActions.InvokeAsync(SemanticCall(host, ReviewPresentationActions.Speed, new SpeedArguments(ReviewSpeed.Double)))).Reason);
                Assert.Equal(ActionUnavailableReason.SourceUnavailable, (await host.PresentationActions.InvokeAsync(SemanticCall(host, ReviewPresentationActions.Toggle, new PresentationToggleArguments(PresentationToggle.Loop)))).Reason);
                Assert.Equal(ActionOutcome.Completed, (await host.PresentationActions.InvokeAsync(SemanticCall(host, ReviewPresentationActions.Toggle, new PresentationToggleArguments(PresentationToggle.Fullscreen)))).Outcome);
                Assert.True(host.IsFullscreen); host.ExitFullscreen();
            } finally { await host.CloseAsync(); window.Close(); Directory.Delete(folder, true); }
        });
    }
    [Fact]
    public async Task PresentationRejectsAudioWithoutAnAudioStreamAndInvalidValuesBeforeMutation()
    {
        await StaDispatcher.RunAsync(async () => {
            TestWpfApplication.EnsureLoaded();
            var backend = new FakeBackend();
            await using var coordinator = new MediaPlaybackCoordinator(() => new MediaPlaybackService(backend));
            var host = new PlayerViewerHost(coordinator); var asset = ReviewAsset("silent.mp4");
            try {
                await host.OpenAsync(asset, ReviewPath(asset));
                foreach (var arguments in new ActionArguments[] { new VolumeArguments(AdjustmentMode.Set, 101), new VolumeArguments(AdjustmentMode.Relative, -101), new VolumeArguments((AdjustmentMode)99, 0) })
                    Assert.Equal(ActionUnavailableReason.InvalidArguments, (await host.PresentationActions.InvokeAsync(SemanticCall(host, ReviewPresentationActions.Volume, arguments))).Reason);
                Assert.Equal(ActionUnavailableReason.AudioUnavailable, (await host.PresentationActions.InvokeAsync(SemanticCall(host, ReviewPresentationActions.Volume, new VolumeArguments(AdjustmentMode.Set, 0)))).Reason);
                Assert.Equal(ActionUnavailableReason.AudioUnavailable, (await host.PresentationActions.InvokeAsync(SemanticCall(host, ReviewPresentationActions.Toggle, new PresentationToggleArguments(PresentationToggle.Mute)))).Reason);
                Assert.Equal(100, backend.Volume); Assert.False(backend.Mute);
            } finally { await host.CloseAsync(); }
        });
    }
}
