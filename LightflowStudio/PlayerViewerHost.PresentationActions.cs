using Lightflow.Actions;
using System.Windows;

namespace LightflowStudio;

public partial class PlayerViewerHost
{
    private ReviewPresentationActions _presentationActions = null!;
    internal ReviewPresentationActions PresentationActions => _presentationActions;
    internal IReadOnlyList<Guid> AllSubclipIds => _subclipItems.Select(item => item.SubclipId).ToArray();
    internal Func<ExportEntry, Task<ActionResult>>? OpenSemanticExport { get; set; }
    internal void SetExportEntryBusy(ExportEntry entry, bool busy)
    {
        if (entry == ExportEntry.PlayerVideo) SetExportEnabled(!busy && ActionContext.SourceReady && _currentAsset?.AssetId is not null);
        else if (entry == ExportEntry.PlayerSelectedSubclips) ExportSelectedSubclipsMenuItem.IsEnabled = !busy && SelectedSubclipIds.Count > 0;
        else if (entry == ExportEntry.PlayerAllSubclips) ExportAllSubclipsMenuItem.IsEnabled = !busy && _subclipItems.Count > 0;
    }
    private async Task DispatchPresentationAsync(string id, ActionArguments arguments)
    {
        var invocation = new ActionInvocation(id, arguments, TransportActionSource, Guid.NewGuid(), ActionTarget);
        PresentActionResult(invocation, await _presentationActions.InvokeAsync(invocation));
    }
    private void LoopChoice_Click(object sender, RoutedEventArgs e)
    {
        LoopChoice.IsChecked = LoopChoice.IsChecked != true;
        _ = DispatchPresentationAsync(ReviewPresentationActions.Toggle, new PresentationToggleArguments(PresentationToggle.Loop));
    }
    private async Task DispatchExportEntryAsync(ExportEntry entry)
    {
        if (OpenSemanticExport is not null) {
            var result = await OpenSemanticExport(entry);
            if (result.Outcome == ActionOutcome.Failed) SetStatus(result.Diagnostic);
        }
    }
    private sealed class WindowsReviewPresentationPort(PlayerViewerHost host) : IReviewPresentationPort
    {
        public PlayerActionContext Context => host.ActionContext;
        public ActionEligibility Eligibility(string id, ActionArguments arguments)
        {
            var context = Context;
            var video = context.SourceReady;
            var readyVisual = host._currentAsset is not null && host._pixelWidth > 0 && host._pixelHeight > 0 &&
                (host._currentAsset.Kind == MediaPresentationKind.Video ? context.SourceReady : host.ImageSurface.Source is not null);
            var audio = video && host._service?.SourceInfo?.AudioStreams.Count > 0;
            return id switch {
                ReviewPresentationActions.Volume => new(audio, audio ? ActionUnavailableReason.None : ActionUnavailableReason.AudioUnavailable),
                ReviewPresentationActions.Speed => new(video, video ? ActionUnavailableReason.None : ActionUnavailableReason.SourceUnavailable),
                ReviewPresentationActions.Zoom or ReviewPresentationActions.StepZoom => new(readyVisual, readyVisual ? ActionUnavailableReason.None : ActionUnavailableReason.ViewportUnavailable),
                ReviewPresentationActions.Toggle when arguments is PresentationToggleArguments { Toggle: PresentationToggle.Mute } => new(audio, audio ? ActionUnavailableReason.None : ActionUnavailableReason.AudioUnavailable),
                ReviewPresentationActions.Toggle when arguments is PresentationToggleArguments { Toggle: PresentationToggle.Loop } => new(video, video ? ActionUnavailableReason.None : ActionUnavailableReason.SourceUnavailable),
                ReviewPresentationActions.Toggle when arguments is PresentationToggleArguments { Toggle: PresentationToggle.Fullscreen } => new(readyVisual && Window.GetWindow(host) is not null, readyVisual && Window.GetWindow(host) is not null ? ActionUnavailableReason.None : ActionUnavailableReason.ViewportUnavailable),
                ReviewPresentationActions.Toggle => new(host._currentAsset is not null, host._currentAsset is not null ? ActionUnavailableReason.None : ActionUnavailableReason.SourceUnavailable),
                _ => new(false, ActionUnavailableReason.UnknownAction)
            };
        }
        private void Check(PlayerActionTarget target, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (host.ActionTarget != target) throw new OperationCanceledException("Player changed.");
        }
        public Task<ActionResult> ChangeVolumeAsync(PlayerActionTarget target, VolumeArguments arguments, CancellationToken token)
        {
            Check(target, token);
            var service = host._service!;
            var volume = Math.Clamp(arguments.Mode == AdjustmentMode.Set ? arguments.Percent : service.Volume + arguments.Percent, 0, 100);
            var changed = service.Volume != volume;
            service.Volume = volume;
            host.UpdateAudioControlsFromService();
            return Task.FromResult(new ActionResult(changed ? ActionOutcome.Completed : ActionOutcome.NoChange));
        }
        public async Task<ActionResult> ChangeSpeedAsync(PlayerActionTarget target, ReviewSpeed speed, CancellationToken token)
        {
            Check(target, token);
            var service = host._service!;
            var cadence = host.CadenceChoiceBox.SelectedItem as CadenceChoice ?? new("Source", 1);
            host.RestoreLiveVideoSurface();
            await service.SetReviewOptionsAsync(new(PlaybackReviewOptions.Speeds[(int)speed], cadence.Divisor,
                cadence.Divisor == 1 ? cadence.Rate : null), token);
            Check(target, token);
            host._updatingReview = true;
            try { host.SpeedChoice.SelectedIndex = (int)speed; }
            finally { host._updatingReview = false; }
            return new(ActionOutcome.Completed);
        }
        public Task<ActionResult> ChangeZoomAsync(PlayerActionTarget target, ReviewZoom zoom, CancellationToken token)
        {
            Check(target, token);
            double? value = zoom switch { ReviewZoom.Half => 0.5, ReviewZoom.ActualPixels => 1, ReviewZoom.Double => 2, ReviewZoom.Quadruple => 4, _ => null };
            var changed = host._pixelZoom != value || host._panX != 0 || host._panY != 0;
            host._pixelZoom = value; host._panX = host._panY = 0;
            host._updatingReview = true;
            try { host.ZoomChoice.SelectedIndex = (int)zoom; }
            finally { host._updatingReview = false; }
            host.ApplyViewport();
            return Task.FromResult(new ActionResult(changed ? ActionOutcome.Completed : ActionOutcome.NoChange));
        }
        public Task<ActionResult> StepZoomAsync(PlayerActionTarget target, int direction, CancellationToken token)
        {
            Check(target, token);
            var before = host._pixelZoom;
            host.ZoomViewportCore(direction);
            return Task.FromResult(new ActionResult(before == host._pixelZoom ? ActionOutcome.NoChange : ActionOutcome.Completed));
        }
        public Task<ActionResult> ToggleAsync(PlayerActionTarget target, PresentationToggle toggle, CancellationToken token)
        {
            Check(target, token);
            switch (toggle) {
                case PresentationToggle.Mute: host._service!.Mute = !host._service.Mute; host.UpdateAudioControlsFromService(); break;
                case PresentationToggle.Loop: host.LoopChoice.IsChecked = host.LoopChoice.IsChecked != true; break;
                case PresentationToggle.Fullscreen: host.ToggleFullscreenCore(); break;
                case PresentationToggle.Filmstrip: host.FilmstripVisible = !host.FilmstripVisible; break;
            }
            return Task.FromResult(new ActionResult(ActionOutcome.Completed));
        }
    }
}
