using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Controls.Primitives;

namespace LightflowStudio;

public partial class TrimEditorWindow : Window
{
    private readonly string _sourcePath;
    private readonly MediaRange? _appliedRange;
    private readonly TrimEditorPlayback _playback = new(App.Playback);
    private readonly TrimEditorCloseLifecycle _closeLifecycle;
    private IMediaPlaybackService? _service;
    private MediaPlaybackView? _previewView;
    private TrimSelection? _selection;
    private bool _updatingPosition;
    private bool _releaseComplete;
    private readonly FrameStepQueue _frameStepQueue = new();

    internal TrimEditorWindow(string sourcePath, MediaRange? appliedRange)
    {
        InitializeComponent();
        _closeLifecycle = new(() => _playback.DisposeAsync());
        _sourcePath = sourcePath;
        _appliedRange = appliedRange;
        SourceName.Text = Path.GetFileName(sourcePath);
        Loaded += TrimEditorWindow_Loaded;
        SourceInitialized += (_, _) => WindowAppearance.EnableDarkTitleBar(this);
    }

    internal MediaRange? AppliedRange { get; private set; }

    private async void TrimEditorWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _service = await _playback.OpenAsync(_sourcePath);
            _service.StateChanged += Playback_StateChanged;
            if (_service.SourceInfo is not { } info || info.Duration <= TimeSpan.Zero || _service.Snapshot.State == MediaPlaybackState.Failed)
                throw new InvalidOperationException(_service.Snapshot.Error?.Message ?? "The source does not provide usable playback timing.");
            _previewView = new MediaPlaybackView(_service);
            PreviewHost.Children.Clear();
            PreviewHost.Children.Add(_previewView);
            _selection = new(info.Duration, _appliedRange);
            PositionSlider.Maximum = info.Duration.TotalMilliseconds;
            DurationText.Text = FormatTimestamp(info.Duration);
            UpdateSelectionText();
            if (_selection.InitialPlaybackPosition > TimeSpan.Zero)
            {
                MessageText.Text = "Moving to the saved In point…";
                await _playback.SeekToInitialPositionAsync(_selection);
                MessageText.Text = "";
            }
            UpdateFromSnapshot(_service.Snapshot);
            SetEditorEnabled(true);
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            MessageText.Text = exception.Message;
            LoadingText.Text = "Preview unavailable";
            SetEditorEnabled(false);
        }
    }

    private void Playback_StateChanged(object? sender, MediaPlaybackSnapshot snapshot) =>
        Dispatcher.BeginInvoke(() => UpdateFromSnapshot(snapshot));

    private void UpdateFromSnapshot(MediaPlaybackSnapshot snapshot)
    {
        if (snapshot.DisplayedTimestamp is { } timestamp)
        {
            _updatingPosition = true;
            PositionSlider.Value = Math.Clamp(timestamp.Position.TotalMilliseconds, PositionSlider.Minimum, PositionSlider.Maximum);
            _updatingPosition = false;
            CurrentTimeText.Text = FormatTimestamp(timestamp.Position);
        }
        PlayPauseButton.Content = snapshot.State == MediaPlaybackState.Playing ? "Pause" : "Play";
        // A failure arriving mid-session (not just at open) must disable playback controls the same way
        // PlayerViewerHost.UpdateFromSnapshot does — Play/seek/frame-step against an already-failed session
        // would otherwise still be reachable merely because these were enabled before the failure happened.
        // Set In/Out/Reset/Apply are left alone: they only ever record the last-known-good DisplayedTimestamp,
        // which remains valid, not a live call into the failed playback engine.
        if (snapshot.State == MediaPlaybackState.Failed)
        {
            MessageText.Text = snapshot.Error?.Message ?? "Playback failed.";
            PositionSlider.IsEnabled = false;
            PreviousFrameButton.IsEnabled = false;
            NextFrameButton.IsEnabled = false;
            PlayPauseButton.IsEnabled = false;
        }
    }

    private async void PositionSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingPosition || _service is null || !PositionSlider.IsEnabled) return;
        try { await _service.SeekAsync(TimeSpan.FromMilliseconds(e.NewValue)); }
        catch (OperationCanceledException) { }
        catch (Exception exception) { MessageText.Text = exception.Message; }
    }

    private async void PositionSlider_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var target = IsInsideSliderThumb(e.OriginalSource as DependencyObject)
            ? TimelinePointerTarget.PlayheadThumb
            : TimelinePointerTarget.Track;
        if (!TimelineSeek.ShouldSeek(target) || _service is null || !PositionSlider.IsEnabled) return;
        e.Handled = true;
        var position = TimelineSeek.PositionFromCoordinate(
            e.GetPosition(PositionSlider).X, PositionSlider.ActualWidth, TimeSpan.FromMilliseconds(PositionSlider.Maximum));
        _updatingPosition = true;
        PositionSlider.Value = position.TotalMilliseconds;
        _updatingPosition = false;
        try { await _service.SeekAsync(position); }
        catch (OperationCanceledException) { }
        catch (Exception exception) { MessageText.Text = exception.Message; }
    }

    private static bool IsInsideSliderThumb(DependencyObject? element)
    {
        while (element is not null)
        {
            if (element is Thumb) return true;
            element = VisualTreeHelper.GetParent(element);
        }
        return false;
    }

    private async void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        if (_service is null) return;
        try
        {
            if (_service.Snapshot.State == MediaPlaybackState.Playing) await _service.PauseAsync();
            else await _service.PlayAsync();
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) { MessageText.Text = exception.Message; }
    }

    private void PreviousFrame_Click(object sender, RoutedEventArgs e) => RequestStep(forward: false);
    private void NextFrame_Click(object sender, RoutedEventArgs e) => RequestStep(forward: true);
    private async void SeekIn_Click(object sender, RoutedEventArgs e) => await SeekBoundaryAsync(TrimBoundary.In);
    private async void SeekOut_Click(object sender, RoutedEventArgs e) => await SeekBoundaryAsync(TrimBoundary.Out);

    private async Task SeekBoundaryAsync(TrimBoundary boundary)
    {
        if (_selection is null || _service is null) return;
        try { await _playback.SeekToBoundaryAsync(_selection, boundary); }
        catch (OperationCanceledException) { }
        catch (Exception exception) { MessageText.Text = exception.Message; }
    }

    /// <summary>
    /// Before #110, this called <c>StepForwardAsync</c>/<c>StepBackwardAsync</c> directly with an implicit
    /// <see cref="CancellationToken.None"/> — every repeated Next Frame click at the last decoded frame hung
    /// for the engine's own internal 10-second timeout before showing an error, and rapid repeated clicks
    /// anywhere in the clip could reach the native engine faster than it could safely process them (see
    /// <see cref="FrameStepQueue"/>'s doc comment for the proven root cause and why every step now queues
    /// through it — shared with #110's <c>PlayerViewerHost</c>, the other UI consumer of this same call).
    /// </summary>
    private void RequestStep(bool forward)
    {
        if (_service is null) return;
        _frameStepQueue.RequestStep(_service, forward, exception => MessageText.Text = exception.Message);
    }

    private void SetIn_Click(object sender, RoutedEventArgs e)
    {
        if (_selection is null || _service?.Snapshot.DisplayedTimestamp is not { } timestamp) return;
        MessageText.Text = _selection.SetIn(timestamp.Position) ? "" : "In must be before Out.";
        UpdateSelectionText();
    }

    private void SetOut_Click(object sender, RoutedEventArgs e)
    {
        if (_selection is null || _service?.Snapshot.DisplayedTimestamp is not { } timestamp) return;
        MessageText.Text = _selection.SetOut(timestamp.Position) ? "" : "Out must be after In.";
        UpdateSelectionText();
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        _selection?.Reset();
        MessageText.Text = "";
        UpdateSelectionText();
    }

    private async void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (_selection is null) return;
        try
        {
            AppliedRange = _selection.Apply();
            await CloseAfterReleaseAsync(true);
        }
        catch (InvalidOperationException exception) { MessageText.Text = exception.Message; }
    }

    private async void Cancel_Click(object sender, RoutedEventArgs e) => await CloseAfterReleaseAsync(false);

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Space || _service is null || Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase) return;
        e.Handled = true;
        PlayPause_Click(this, new RoutedEventArgs());
    }

    private void UpdateSelectionText()
    {
        if (_selection is null) return;
        InTimeText.Text = $"In: {FormatTimestamp(_selection.In)}";
        OutTimeText.Text = $"Out: {FormatTimestamp(_selection.Out)}";
        EditorRangeIndicator.StartFraction = _selection.In.TotalSeconds / _selection.SourceDuration.TotalSeconds;
        EditorRangeIndicator.WidthFraction = (_selection.Out - _selection.In).TotalSeconds / _selection.SourceDuration.TotalSeconds;
    }

    private void SetEditorEnabled(bool enabled)
    {
        PositionSlider.IsEnabled = enabled;
        SetInButton.IsEnabled = enabled;
        InTimeLink.IsEnabled = enabled;
        SetOutButton.IsEnabled = enabled;
        OutTimeLink.IsEnabled = enabled;
        PreviousFrameButton.IsEnabled = enabled;
        NextFrameButton.IsEnabled = enabled;
        PlayPauseButton.IsEnabled = enabled;
        ResetButton.IsEnabled = enabled;
        ApplyButton.IsEnabled = enabled;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_releaseComplete)
        {
            base.OnClosing(e);
            return;
        }

        e.Cancel = true;
        _ = CloseAfterReleaseAsync(null);
    }

    private async Task CloseAfterReleaseAsync(bool? requestedResult)
    {
        // Invalidates any still-draining frame-step backlog before releasing _service — see FrameStepQueue.Reset.
        _frameStepQueue.Reset();
        SetEditorEnabled(false);
        if (_service is not null) _service.StateChanged -= Playback_StateChanged;
        PreviewHost.Children.Clear();
        var previewView = _previewView;
        _previewView = null;
        try
        {
            var preservedResult = await _closeLifecycle.CloseAsync(requestedResult);
            previewView?.Dispose();
            _releaseComplete = true;
            if (preservedResult.HasValue) DialogResult = preservedResult.Value;
            else Close();
        }
        catch (Exception exception)
        {
            previewView?.Dispose();
            MessageText.Text = $"Playback could not close cleanly: {exception.Message}";
            SetEditorEnabled(true);
        }
    }

    private static string FormatTimestamp(TimeSpan value)
    {
        var hours = (int)value.TotalHours;
        return $"{hours:00}:{value.Minutes:00}:{value.Seconds:00}.{value.Milliseconds:000}";
    }
}
