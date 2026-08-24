using System.IO;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
// UseWindowsForms is enabled project-wide (for OpenFileDialog elsewhere), which brings a global
// System.Windows.Forms using into scope alongside System.Windows.Controls/System.Windows.Input — both
// declare UserControl/KeyEventArgs, so this file resolves them explicitly rather than ambiguously,
// mirroring MainWindow.xaml.cs's existing fully-qualified System.Windows.Input.KeyEventArgs convention.
using UserControl = System.Windows.Controls.UserControl;

namespace LightflowStudio;

/// <summary>
/// Lightflow's integrated Player/Viewer presentation content (#110). Deliberately independent of
/// <c>MainWindow</c> and Browser chrome: it takes a <see cref="MediaPlaybackCoordinator"/> (the same
/// global playback engine every other consumer reuses) and a host-agnostic <see cref="PlayerViewerAsset"/> —
/// nothing Browser-specific — so a future floating/new-window host (#112) can embed this exact control rather
/// than reimplementing playback wiring. Video plays through the shared #53 engine via
/// <see cref="MediaPlaybackLeaseSession"/>/<see cref="MediaPlaybackView"/> — the same lease-wrapper
/// <c>TrimEditorPlayback</c> derives from for trim-boundary seeking. Review ranges reuse <see cref="MediaRange"/>
/// without adding a second playback engine; still images decode directly through WIC, reusing
/// <see cref="WicImageThumbnailRenderer"/>'s existing EXIF-orientation handling rather than a second image path.
/// </summary>
public partial class PlayerViewerHost : UserControl
{
    private readonly MediaPlaybackCoordinator _coordinator;
    private readonly IMediaRangeStore? _rangeStore;
    private readonly IFrameScreengrabService? _screengrabService;
    private readonly IFolderLauncher _folderLauncher;
    private readonly ILutLibraryCache? _lutCache;
    private readonly IAssetColorStore? _assetColors;
    private readonly Func<string>? _cameraLutFolder;
    private readonly Func<string>? _creativeLutFolder;
    private readonly Action<PlayerOpenMilestone>? _openMilestone;
    private long _generation;
    private MediaPlaybackLeaseSession? _playback;
    private IMediaPlaybackService? _service;
    private MediaPlaybackView? _mediaView;
    private PlayerViewerAsset? _currentAsset;
    private bool _updatingPosition;
    private bool _updatingVolume;
    private MediaRange? _reviewRange;
    private bool _stopAtOutDuringPlayback;
    private bool _stoppingAtOut;
    private MediaDecodedFrame? _retainedSteppedFrame;
    private string? _lastScreengrabDirectory;
    private readonly FrameStepQueue _frameStepQueue = new();
    private bool _updatingColor;
    private bool _persistentColorEnabled;
    private bool _momentaryColorBypass;
    private PlayerColorPipeline? _colorPipeline;
    private LutLibrarySnapshot? _cameraLibrary;
    private LutLibrarySnapshot? _creativeLibrary;
    private long _colorRefreshRevision;

    private sealed record LutChoice(Guid? LutId, string DisplayName, bool OpensFolder = false)
    {
        public override string ToString() => DisplayName;
    }
    private sealed record PreparedColor(LutLibrarySnapshot CameraLibrary, LutLibrarySnapshot CreativeLibrary,
        AssetColorIntent Intent, PlayerColorPipeline Pipeline, string? Diagnostic);

    internal PlayerViewerHost(MediaPlaybackCoordinator coordinator, IMediaRangeStore? rangeStore = null,
        IFrameScreengrabService? screengrabService = null, IFolderLauncher? folderLauncher = null,
        ILutLibraryCache? lutCache = null, IAssetColorStore? assetColors = null,
        Func<string>? cameraLutFolder = null, Func<string>? creativeLutFolder = null,
        Action<PlayerOpenMilestone>? openMilestone = null)
    {
        _coordinator = coordinator;
        _rangeStore = rangeStore;
        _screengrabService = screengrabService;
        _folderLauncher = folderLauncher ?? new ShellFolderLauncher();
        _lutCache = lutCache;
        _assetColors = assetColors;
        _cameraLutFolder = cameraLutFolder;
        _creativeLutFolder = creativeLutFolder;
        _openMilestone = openMilestone;
        InitializeComponent();
    }

    /// <summary>Raised by the Back button or Esc. The host decides what "back" means (for the Browser, returning to Grid presentation at its preserved context).</summary>
    public event EventHandler? BackRequested;

    /// <summary>Raised after a saved review-range change commits so any host can refresh its own presentation.</summary>
    internal event EventHandler<MediaRangeStateChangedEventArgs>? RangeStateChanged;
    internal PlayerViewerAsset? CurrentAsset => _currentAsset;

    /// <summary>
    /// Opens one asset, releasing whatever was previously open first. Safe to call repeatedly for rapid
    /// Browser ↔ Player transitions or fast successive opens: a generation token guards every awaited step so
    /// only the most recently requested open can ever publish UI state, matching the same latest-request-wins
    /// discipline the underlying playback engine already applies to its own operations.
    /// </summary>
    internal async Task OpenAsync(PlayerViewerAsset asset, MediaPathResolution resolution, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(asset);
        var generation = ++_generation;
        _openMilestone?.Invoke(PlayerOpenMilestone.PreviousAssetReleaseStarted);
        try { await ReleaseCurrentAsync().ConfigureAwait(true); }
        catch (Exception exception)
        {
            // Best-effort teardown of whatever was previously open; a failure releasing the OLD asset must
            // not prevent attempting to open the NEW one, and must not fault this call unobserved (MainWindow
            // invokes it fire-and-forget from the tile double-click/Enter handler) — unfiltered, matching
            // MainWindow.RunBrowserNavigationAsync's own catch-all convention for a fire-and-forget UI entry
            // point, since an unanticipated exception type here must still surface as a status message rather
            // than propagate as a silent unobserved task fault.
            SetStatus(exception.Message);
        }
        _openMilestone?.Invoke(PlayerOpenMilestone.PreviousAssetReleaseCompleted);
        if (generation != _generation) return;

        _currentAsset = asset;
        AssetNameText.Text = asset.Name;
        SetScreengrabFeedback(null);
        SetStatus("Loading…");

        if (resolution.PhysicalPath is null || !resolution.Exists)
        {
            SetStatus(resolution.Diagnostic ?? "This file is unavailable.");
            Focus();
            return;
        }

        try
        {
            if (asset.Kind == MediaPresentationKind.Video)
                await OpenVideoAsync(resolution.PhysicalPath, generation, token).ConfigureAwait(true);
            else
                await OpenImageAsync(resolution.PhysicalPath, generation, token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            // Unfiltered: a genuinely unanticipated exception (e.g. a native/Direct3D presentation-surface
            // failure inside OpenVideoAsync, not just the anticipated IOException/InvalidOperationException/
            // etc. cases) must still surface as a status message here rather than propagate unobserved out of
            // this fire-and-forget call — OpenVideoAsync/OpenImageAsync already guarantee the lease/state
            // they touched is released before any exception reaches this catch.
            if (generation != _generation) return;
            SetStatus(exception.Message);
        }
        finally
        {
            if (generation == _generation) Focus();
        }
    }

    /// <summary>
    /// Releases playback and clears presentation. Safe to call whether or not anything is currently open.
    /// Generation-guarded exactly like <see cref="OpenAsync"/>: <see cref="ReleaseCurrentAsync"/>'s actual
    /// resource teardown always runs unconditionally, but a stale/superseded Close's UI-state clearing (asset
    /// name, status) is skipped if a newer Open has already published a different asset's state by the time
    /// this Close's own await settles — otherwise a slow-to-tear-down Close from a lease-theft recovery
    /// (<see cref="HandleStateChanged"/>) could clobber a newly-opened asset's header/status after the fact.
    /// </summary>
    internal async Task CloseAsync()
    {
        var generation = ++_generation;
        await ReleaseCurrentAsync().ConfigureAwait(true);
        if (generation != _generation) return;
        _currentAsset = null;
        AssetNameText.Text = "";
        SetStatus(null);
    }

    private async Task OpenVideoAsync(string absolutePath, long generation, CancellationToken token)
    {
        var playback = new MediaPlaybackLeaseSession(_coordinator);
        _openMilestone?.Invoke(PlayerOpenMilestone.PlaybackBackendOpenStarted);
        var service = await playback.OpenAsync(absolutePath, token).ConfigureAwait(true);
        _openMilestone?.Invoke(PlayerOpenMilestone.PlaybackBackendOpenCompleted);
        if (generation != _generation) { await playback.DisposeAsync().ConfigureAwait(true); return; }

        _playback = playback;
        _service = service;
        service.StateChanged += Playback_StateChanged;
        try
        {
            if (service.SourceInfo is not { } info || info.Duration <= TimeSpan.Zero || service.Snapshot.State == MediaPlaybackState.Failed)
                throw new InvalidOperationException(service.Snapshot.Error?.Message ?? "The video could not be decoded for preview.");

            PositionSlider.Maximum = info.Duration.TotalMilliseconds;
            DurationText.Text = FormatTimestamp(info.Duration);
            var restoredRange = await RestoreRangeAsync(info.Duration, assetId: _currentAsset?.AssetId, token).ConfigureAwait(true);
            if (generation != _generation) return;
            _reviewRange = restoredRange;
            UpdateRangePresentation();
            if (_reviewRange?.In is { } savedIn)
                await service.SeekAsync(savedIn, token).ConfigureAwait(true);
            if (generation != _generation) return;
            // Attach the native presentation only after the optional saved-In seek has settled. Creating it
            // earlier lets Flyleaf's already-open default/source-start frame become visible before the seek,
            // producing a brief flash. The player remains paused throughout; this changes presentation order,
            // not the shared playback/backend path or its authoritative decoded-timestamp semantics.
            _mediaView = new MediaPlaybackView(service);
            VideoHost.Children.Add(_mediaView);
            _openMilestone?.Invoke(PlayerOpenMilestone.PresentationSurfaceCreated);
            UpdateFromSnapshot(service.Snapshot);
            SetTransportEnabled(true);
            SetAudioControlsEnabled(info.AudioStreams.Count > 0);
            UpdateAudioControlsFromService();
            TransportBar.Visibility = Visibility.Visible;
            SetColorControlsEnabled(true);
            SetStatus(null);
            _openMilestone?.Invoke(PlayerOpenMilestone.PlayerControlsPublished);
            _ = CompleteColorAfterOpenAsync(generation, token);
        }
        catch
        {
            // Any failure after acquiring the lease — an invalid/failed source, or a presentation-surface
            // construction failure inside `new MediaPlaybackView(service)` — must release it before
            // propagating, whatever the exception type. Otherwise _service stays assigned to a session
            // nothing further can use, and Space (guarded only on "_service is not null", unlike StepAsync
            // which also checks PositionSlider.IsEnabled) would still reach it despite every transport
            // control being visibly disabled.
            service.StateChanged -= Playback_StateChanged;
            _service = null;
            _playback = null;
            await playback.DisposeAsync().ConfigureAwait(true);
            throw;
        }
    }

    private async Task OpenImageAsync(string absolutePath, long generation, CancellationToken token)
    {
        var bitmap = await Task.Run(() => DecodeImage(absolutePath), token).ConfigureAwait(true);
        if (generation != _generation) return;
        ImageSurface.Source = bitmap;
        ImageSurface.Visibility = Visibility.Visible;
        SetStatus(null);
    }

    private static BitmapSource DecodeImage(string absolutePath)
    {
        using var stream = new FileStream(absolutePath, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, 64 * 1024, FileOptions.SequentialScan);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        if (decoder.Frames.Count == 0) throw new InvalidOperationException("The image contains no decodable frames.");
        var frame = decoder.Frames[0];
        var orientation = WicImageThumbnailRenderer.ReadOrientation(frame.Metadata as BitmapMetadata);
        BitmapSource bitmap = WicImageThumbnailRenderer.ApplyOrientation(frame, orientation);
        bitmap.Freeze();
        return bitmap;
    }

    private async Task ReleaseCurrentAsync()
    {
        // Invalidates the frame-step backlog before releasing _service — no further queued steps are applied
        // or reported to a service that may now hold a different source or none at all. This does not itself
        // wait for a step already genuinely in flight; that one native decode keeps running regardless (see
        // FrameStepQueue's own doc comment — there is no way to abort it), and MediaPlaybackService's existing
        // cancel-on-close/generation handling governs what happens when the close below reaches it.
        _frameStepQueue.Reset();
        _service?.SetColorPipeline(null, false);
        var mediaView = _mediaView;
        _mediaView = null;
        VideoHost.Children.Clear();
        if (_service is not null) _service.StateChanged -= Playback_StateChanged;
        _service = null;
        var playback = _playback;
        _playback = null;
        mediaView?.Dispose();
        if (playback is not null) await playback.DisposeAsync().ConfigureAwait(true);

        ImageSurface.Source = null;
        ImageSurface.Visibility = Visibility.Collapsed;
        RestoreLiveVideoSurface();
        TransportBar.Visibility = Visibility.Collapsed;
        SetTransportEnabled(false);
        SetAudioControlsEnabled(false);
        _reviewRange = null;
        _stopAtOutDuringPlayback = false;
        _stoppingAtOut = false;
        _retainedSteppedFrame = null;
        _colorPipeline = null;
        _cameraLibrary = _creativeLibrary = null;
        _colorRefreshRevision++;
        _momentaryColorBypass = false;
        _updatingColor = true;
        CameraLutCombo.ItemsSource = CreativeLutCombo.ItemsSource = null;
        ColorToggleButton.IsChecked = _persistentColorEnabled;
        _updatingColor = false;
        SetColorControlsEnabled(false);
        UpdateRangePresentation();
        SetScreengrabFeedback(null);
    }

    private void SetStatus(string? message)
    {
        StatusText.Text = message ?? "";
        StatusText.Visibility = string.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void SetTransportEnabled(bool enabled)
    {
        PositionSlider.IsEnabled = enabled;
        PreviousFrameButton.IsEnabled = enabled;
        NextFrameButton.IsEnabled = enabled;
        PlayPauseButton.IsEnabled = enabled;
        SetInButton.IsEnabled = enabled;
        SetOutButton.IsEnabled = enabled;
        ScreengrabButton.IsEnabled = enabled && _screengrabService is not null;
    }

    private void SetColorControlsEnabled(bool enabled)
    {
        ColorToggleButton.IsEnabled = enabled;
        UpdateColorSelectorEnabled();
    }

    private void UpdateColorSelectorEnabled() =>
        CameraLutCombo.IsEnabled = CreativeLutCombo.IsEnabled = ColorToggleButton.IsEnabled
            && _cameraLibrary is not null && _creativeLibrary is not null
            && _persistentColorEnabled && !_momentaryColorBypass;

    private async Task<PreparedColor?> PrepareColorAsync(CancellationToken token)
    {
        if (_currentAsset?.AssetId is not Guid assetId || _lutCache is null || _assetColors is null
            || _cameraLutFolder is null || _creativeLutFolder is null) return null;
        LutLibrarySnapshot cameraLibrary, creativeLibrary;
        AssetColorIntent intent;
        _openMilestone?.Invoke(PlayerOpenMilestone.ColorCacheWaitStarted);
        await _lutCache.WaitUntilInitializedAsync(token).ConfigureAwait(true);
        _openMilestone?.Invoke(PlayerOpenMilestone.ColorCacheWaitCompleted);
        cameraLibrary = _lutCache.Snapshot(ColorLutStage.Camera);
        creativeLibrary = _lutCache.Snapshot(ColorLutStage.Creative);
        _openMilestone?.Invoke(PlayerOpenMilestone.ColorAssignmentReadStarted);
        intent = await _assetColors.GetAsync(assetId, token).ConfigureAwait(true);
        _openMilestone?.Invoke(PlayerOpenMilestone.ColorAssignmentReadCompleted);
        CubeLutData? camera = null, creative = null;
        _openMilestone?.Invoke(PlayerOpenMilestone.RuntimeLutLoadStarted);
        if (intent.Camera is { Availability: LutResourceAvailability.Available } cam)
            camera = await _lutCache.GetRuntimeAsync(ColorLutStage.Camera, cam.LutId, token).ConfigureAwait(true);
        if (intent.Creative is { Availability: LutResourceAvailability.Available } look)
            creative = await _lutCache.GetRuntimeAsync(ColorLutStage.Creative, look.LutId, token).ConfigureAwait(true);
        _openMilestone?.Invoke(PlayerOpenMilestone.RuntimeLutLoadCompleted);
        var missing = new[] { intent.Camera, intent.Creative }.OfType<ColorLutReference>()
            .FirstOrDefault(x => x.Availability != LutResourceAvailability.Available);
        return new(cameraLibrary, creativeLibrary, intent, new(camera, creative), missing is null ? null
            : $"Assigned LUT unavailable: {missing.DisplayName}. {missing.Diagnostic}");
    }

    private async Task CompleteColorAfterOpenAsync(long generation, CancellationToken token)
    {
        var colorRevision = _colorRefreshRevision;
        try
        {
            var prepared = await PrepareColorAsync(token).ConfigureAwait(true);
            if (generation != _generation || colorRevision != _colorRefreshRevision
                || _service is null || prepared is null) return;
            _service.SetColorPipeline(prepared.Pipeline, !_persistentColorEnabled || _momentaryColorBypass);
            PublishColor(prepared);
            SetStatus(prepared.Diagnostic);
            _openMilestone?.Invoke(PlayerOpenMilestone.ColorPublished);
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            if (generation == _generation && colorRevision == _colorRefreshRevision)
                SetStatus($"Color could not be applied. {exception.Message}");
        }
    }

    private void PublishColor(PreparedColor? prepared)
    {
        if (prepared is null) return;
        var cameraChoices = MakeChoices(prepared.CameraLibrary, prepared.Intent.Camera);
        var creativeChoices = MakeChoices(prepared.CreativeLibrary, prepared.Intent.Creative);
        _updatingColor = true;
        CameraLutCombo.ItemsSource = cameraChoices;
        CreativeLutCombo.ItemsSource = creativeChoices;
        CameraLutCombo.SelectedItem = cameraChoices.FirstOrDefault(x => x.LutId == prepared.Intent.Camera?.LutId) ?? cameraChoices[0];
        CreativeLutCombo.SelectedItem = creativeChoices.FirstOrDefault(x => x.LutId == prepared.Intent.Creative?.LutId) ?? creativeChoices[0];
        _colorPipeline = prepared.Pipeline;
        _cameraLibrary = prepared.CameraLibrary;
        _creativeLibrary = prepared.CreativeLibrary;
        _updatingColor = false;
        SetColorControlsEnabled(true);
    }

    private static LutChoice[] MakeChoices(LutLibrarySnapshot library, ColorLutReference? assigned)
    {
        var choices = new[] { new LutChoice(null, "No LUT") }
            .Concat(LutCatalog.Options(library.Resources).Skip(1).Select(x => new LutChoice(x.LutId, x.DisplayName)))
            .ToList();
        if (assigned is { Availability: not LutResourceAvailability.Available }
            && choices.All(choice => choice.LutId != assigned.LutId))
            choices.Add(new(assigned.LutId, $"{assigned.DisplayName} (Unavailable)"));
        choices.Add(new(null, "Open LUT Folder…", OpensFolder: true));
        return choices.ToArray();
    }

    /// <summary>Refreshes only the stage selectors whose persisted roots changed. A revision guard prevents a
    /// slower older scan from publishing over a newer Settings save; playback position/state are untouched.</summary>
    internal async Task RefreshColorFoldersAsync(bool cameraChanged, bool creativeChanged,
        CancellationToken token = default)
    {
        if ((!cameraChanged && !creativeChanged) || _currentAsset?.AssetId is not Guid assetId
            || _service is null || _lutCache is null || _assetColors is null
            || _cameraLutFolder is null || _creativeLutFolder is null) return;
        var revision = ++_colorRefreshRevision;
        var generation = _generation;
        try
        {
            await _lutCache.WaitUntilInitializedAsync(token).ConfigureAwait(true);
            var cameraLibrary = cameraChanged
                ? _lutCache.Snapshot(ColorLutStage.Camera)
                : _cameraLibrary;
            var creativeLibrary = creativeChanged
                ? _lutCache.Snapshot(ColorLutStage.Creative)
                : _creativeLibrary;
            if (cameraLibrary is null || creativeLibrary is null) return;
            var intent = await _assetColors.GetAsync(assetId, token).ConfigureAwait(true);
            var camera = cameraChanged ? await LoadStageAsync(ColorLutStage.Camera, intent.Camera, token).ConfigureAwait(true)
                : _colorPipeline?.Camera;
            var creative = creativeChanged ? await LoadStageAsync(ColorLutStage.Creative, intent.Creative, token).ConfigureAwait(true)
                : _colorPipeline?.Creative;
            if (revision != _colorRefreshRevision || generation != _generation || _service is null) return;

            _updatingColor = true;
            if (cameraChanged)
            {
                var choices = MakeChoices(cameraLibrary, intent.Camera);
                CameraLutCombo.ItemsSource = choices;
                CameraLutCombo.SelectedItem = choices.FirstOrDefault(x => x.LutId == intent.Camera?.LutId) ?? choices[0];
            }
            if (creativeChanged)
            {
                var choices = MakeChoices(creativeLibrary, intent.Creative);
                CreativeLutCombo.ItemsSource = choices;
                CreativeLutCombo.SelectedItem = choices.FirstOrDefault(x => x.LutId == intent.Creative?.LutId) ?? choices[0];
            }
            _updatingColor = false;
            _cameraLibrary = cameraLibrary;
            _creativeLibrary = creativeLibrary;
            _colorPipeline = new(camera, creative);
            _service.SetColorPipeline(_colorPipeline, !_persistentColorEnabled || _momentaryColorBypass);
            var missing = new[] { intent.Camera, intent.Creative }.OfType<ColorLutReference>()
                .FirstOrDefault(reference => reference.Availability != LutResourceAvailability.Available);
            SetStatus(missing is null ? null : $"Assigned LUT unavailable: {missing.DisplayName}. {missing.Diagnostic}");
        }
        catch (OperationCanceledException) when (revision != _colorRefreshRevision || token.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (revision == _colorRefreshRevision && generation == _generation)
                SetStatus($"The Player LUT folders could not be refreshed. {exception.Message}");
        }
    }

    private async Task<CubeLutData?> LoadStageAsync(ColorLutStage stage, ColorLutReference? reference,
        CancellationToken token)
    {
        if (reference is not { Availability: LutResourceAvailability.Available }) return null;
        return await _lutCache!.GetRuntimeAsync(stage, reference.LutId, token).ConfigureAwait(true);
    }

    private async Task ApplyColorIntentAsync(AssetColorIntent intent, CancellationToken token)
    {
        try
        {
            CubeLutData? camera = null, creative = null;
            if (intent.Camera is { Availability: LutResourceAvailability.Available } cam)
                camera = await _lutCache!.GetRuntimeAsync(ColorLutStage.Camera, cam.LutId, token);
            if (intent.Creative is { Availability: LutResourceAvailability.Available } look)
                creative = await _lutCache!.GetRuntimeAsync(ColorLutStage.Creative, look.LutId, token);
            var missing = new[] { intent.Camera, intent.Creative }.OfType<ColorLutReference>().FirstOrDefault(x => x.Availability != LutResourceAvailability.Available);
            _colorPipeline = new(camera, creative);
            RestoreLiveVideoSurface();
            _service?.SetColorPipeline(_colorPipeline, !_persistentColorEnabled || _momentaryColorBypass);
            if (missing is not null) SetStatus($"Assigned LUT unavailable: {missing.DisplayName}. {missing.Diagnostic}");
        }
        catch (Exception exception) { _colorPipeline = null; _service?.SetColorPipeline(null, false); SetStatus($"Color could not be applied. {exception.Message}"); }
    }

    private async void CameraLutCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) => await ChangeColorStageAsync(ColorLutStage.Camera, CameraLutCombo, e);
    private async void CreativeLutCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) => await ChangeColorStageAsync(ColorLutStage.Creative, CreativeLutCombo, e);
    private async Task ChangeColorStageAsync(ColorLutStage stage, System.Windows.Controls.ComboBox selector,
        System.Windows.Controls.SelectionChangedEventArgs change)
    {
        var choice = selector.SelectedItem as LutChoice;
        if (!_updatingColor && choice?.OpensFolder == true)
        {
            _updatingColor = true;
            selector.SelectedItem = change.RemovedItems.OfType<LutChoice>().FirstOrDefault(item => !item.OpensFolder)
                ?? selector.Items.OfType<LutChoice>().First(item => !item.OpensFolder);
            _updatingColor = false;
            try
            {
                var folder = (stage == ColorLutStage.Camera ? _cameraLutFolder : _creativeLutFolder)?.Invoke();
                if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                    throw new DirectoryNotFoundException("The configured LUT folder is unavailable. Choose it in Settings.");
                _folderLauncher.Open(folder);
            }
            catch (Exception exception) { SetStatus($"The LUT folder could not be opened. {exception.Message}"); }
            return;
        }
        if (_updatingColor || choice is null || _currentAsset?.AssetId is not Guid assetId || _assetColors is null
            || _cameraLutFolder is null || _creativeLutFolder is null) return;
        try { await _assetColors.SetStageAsync([assetId], stage, choice.LutId); SetStatus(null); await ApplyColorIntentAsync(await _assetColors.GetAsync(assetId), CancellationToken.None); }
        catch (Exception exception) { SetStatus($"Color assignment could not be saved. {exception.Message}"); }
    }
    private void ColorToggleButton_Click(object sender, RoutedEventArgs e)
    {
        RestoreLiveVideoSurface();
        var enabled = ColorToggleButton.IsChecked == true;
        _persistentColorEnabled = enabled;
        UpdateColorSelectorEnabled();
        if (!enabled) _momentaryColorBypass = false;
        _service?.SetColorPipeline(_colorPipeline, !enabled);
    }

    /// <summary>
    /// Volume/mute are gated separately from the rest of the transport: a video-only source (no audio stream)
    /// keeps Play/seek/frame-step usable while these stay disabled rather than controlling nothing meaningfully.
    /// </summary>
    private void SetAudioControlsEnabled(bool enabled)
    {
        MuteButton.IsEnabled = enabled;
        VolumeSlider.IsEnabled = enabled;
    }

    /// <summary>Reflects the shared playback session's current volume/mute — called once per open, since the
    /// session (and therefore its volume/mute) persists across whichever asset is currently showing.</summary>
    private void UpdateAudioControlsFromService()
    {
        if (_service is null) return;
        _updatingVolume = true;
        VolumeSlider.Value = _service.Volume;
        _updatingVolume = false;
        UpdateMuteIcon(_service.Mute);
    }

    private void UpdateMuteIcon(bool muted)
    {
        MuteIcon.Text = muted ? "" : "";
        MuteButton.ToolTip = muted ? "Unmute" : "Mute";
    }

    private void Playback_StateChanged(object? sender, MediaPlaybackSnapshot snapshot) =>
        Dispatcher.BeginInvoke(() => HandleStateChanged(snapshot));

    /// <summary>
    /// <see cref="MediaPlaybackCoordinator"/> allows only one active session at a time: a different consumer
    /// acquiring the lease (e.g. opening <c>TrimEditorWindow</c> while this control's video is still open)
    /// forcibly closes this control's source out from under it. <see cref="ReleaseCurrentAsync"/> always
    /// unsubscribes this handler before <em>our own</em> close, so an <see cref="MediaPlaybackState.Empty"/>
    /// snapshot arriving here while <see cref="_service"/> is still assigned can only mean that external
    /// takeover, never our own intentional close — silently leaving the transport enabled over a source that
    /// no longer exists would be worse than returning to Grid, which is what actually happened.
    /// </summary>
    private void HandleStateChanged(MediaPlaybackSnapshot snapshot)
    {
        if (snapshot.State == MediaPlaybackState.Empty && _service is not null)
        {
            _ = CloseAsync();
            BackRequested?.Invoke(this, EventArgs.Empty);
            return;
        }
        UpdateFromSnapshot(snapshot);
        if (snapshot.State == MediaPlaybackState.Playing && snapshot.DisplayedTimestamp is { } displayed &&
            ReviewRangePlaybackPolicy.HasReachedArmedOutBoundary(_reviewRange, _stopAtOutDuringPlayback, displayed.Position) &&
            !_stoppingAtOut)
            _ = StopAtOutAsync();
    }

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
        // A failure arriving mid-session (not just at open — see OpenVideoAsync's own upfront check) must
        // disable transport the same way: Play/seek/frame-step against an already-failed session would
        // otherwise still be reachable merely because the buttons were enabled before the failure happened.
        if (snapshot.State == MediaPlaybackState.Failed)
        {
            SetStatus(snapshot.Error?.Message ?? "Playback failed.");
            SetTransportEnabled(false);
        }
    }

    private async void PositionSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingPosition || _service is null || !PositionSlider.IsEnabled) return;
        RestoreLiveVideoSurface();
        try { await _service.SeekAsync(TimeSpan.FromMilliseconds(e.NewValue)); }
        catch (OperationCanceledException) { }
        catch (Exception exception) { SetStatus(exception.Message); }
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
        RestoreLiveVideoSurface();
        try { await _service.SeekAsync(position); }
        catch (OperationCanceledException) { }
        catch (Exception exception) { SetStatus(exception.Message); }
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

    /// <summary>
    /// Called when the host stops being the visible content (e.g. the Browser tab loses focus to another
    /// workspace) without actually closing the Player/Viewer — switching tabs is not "leaving" the Browser
    /// per #110's context-preservation requirement, so this pauses rather than releasing the playback lease.
    /// A no-op for an image Viewer or when nothing is currently playing.
    /// </summary>
    internal async Task PauseIfPlayingAsync()
    {
        if (_service?.Snapshot.State != MediaPlaybackState.Playing) return;
        try { await _service.PauseAsync(); }
        catch (OperationCanceledException) { }
        catch (Exception exception) { SetStatus(exception.Message); }
    }

    private async void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        if (_service is null) return;
        try
        {
            if (_service.Snapshot.State == MediaPlaybackState.Playing) await _service.PauseAsync();
            else
            {
                RestoreLiveVideoSurface();
                var position = _service.Snapshot.DisplayedTimestamp?.Position ?? TimeSpan.Zero;
                _stopAtOutDuringPlayback = ReviewRangePlaybackPolicy.ShouldArmOutBoundary(_reviewRange, position);
                await _service.PlayAsync();
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) { SetStatus(exception.Message); }
    }

    private void PreviousFrame_Click(object sender, RoutedEventArgs e) => RequestStep(forward: false);
    private void NextFrame_Click(object sender, RoutedEventArgs e) => RequestStep(forward: true);

    private void MuteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_service is null) return;
        _service.Mute = !_service.Mute;
        UpdateMuteIcon(_service.Mute);
    }

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingVolume || _service is null) return;
        _service.Volume = (int)e.NewValue;
    }

    /// <summary>
    /// Mirrors <see cref="PositionSlider_PreviewMouseLeftButtonDown"/>'s own click-to-set behavior: the
    /// PlaybackTimelineSlider style's track RepeatButtons only nudge by Slider.DecreaseLarge/IncreaseLarge on a
    /// click (the ordinary WPF Slider default), not jump straight to the clicked position — expected desktop
    /// volume-slider behavior is the latter, so this intercepts a track click (never a thumb click, which must
    /// still start an ordinary drag) and sets Value directly from the click's X position.
    /// </summary>
    private void VolumeSlider_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (IsInsideSliderThumb(e.OriginalSource as DependencyObject) || _service is null || !VolumeSlider.IsEnabled) return;
        e.Handled = true;
        VolumeSlider.Value = SliderClickToSet.ValueFromCoordinate(
            e.GetPosition(VolumeSlider).X, VolumeSlider.ActualWidth, VolumeSlider.Minimum, VolumeSlider.Maximum);
    }

    /// <summary>
    /// Queues one request through the shared bounded interaction queue. The queue serializes calls while the
    /// playback service remains responsible for completing each frame-step operation correctly.
    /// </summary>
    private void RequestStep(bool forward)
    {
        if (_service is null || !PositionSlider.IsEnabled) return;
        _frameStepQueue.RequestStep(ExecutePresentedStepAsync, forward, exception => SetStatus(exception.Message));
    }

    private async Task ExecutePresentedStepAsync(bool forward)
    {
        if (_service is null) return;
        if (forward && SteppedFrameSurface.Visibility != Visibility.Visible)
        {
            await _service.StepForwardAsync().ConfigureAwait(true);
            return;
        }

        if (SteppedFrameSurface.Visibility != Visibility.Visible)
        {
            var current = await CapturePresentedFrameAsync().ConfigureAwait(true);
            _retainedSteppedFrame = current;
            SteppedFrameSurface.Source = ToBitmapSource(current);
            SteppedFrameSurface.Visibility = Visibility.Visible;
            VideoHost.Visibility = Visibility.Hidden;

            // Flyleaf presents through a child HWND, so a WPF element cannot cover its reconstruction.
            // Complete the handoff to the retained bitmap before asking the backend to move at all.
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Render);
        }

        if (forward) await _service.StepForwardAsync().ConfigureAwait(true);
        else await _service.StepBackwardAsync().ConfigureAwait(true);

        var settled = await CapturePresentedFrameAsync().ConfigureAwait(true);
        _retainedSteppedFrame = settled;
        SteppedFrameSurface.Source = ToBitmapSource(settled);
    }

    private async Task<MediaRange?> RestoreRangeAsync(TimeSpan duration, Guid? assetId, CancellationToken token)
    {
        if (_rangeStore is not null && assetId is Guid stableAssetId)
        {
            var saved = await _rangeStore.RestoreAsync(stableAssetId, token).ConfigureAwait(true);
            if (saved is not null)
            {
                var adapted = new MediaRange(duration, saved.In, saved.Out);
                if (adapted.Validate().Count == 0) return adapted;
            }
        }
        return null;
    }

    private async Task SaveRangeAsync(MediaRange? range)
    {
        var savedRange = range?.IsFullSource == true ? null : range;
        if (_rangeStore is not null && _currentAsset?.AssetId is Guid assetId)
        {
            await _rangeStore.SaveAsync(assetId, savedRange).ConfigureAwait(true);
            RangeStateChanged?.Invoke(this, new(assetId, savedRange is not null));
        }
        _reviewRange = savedRange;
        UpdateRangePresentation();
    }

    private void UpdateRangePresentation()
    {
        var duration = _service?.SourceInfo?.Duration;
        var presentation = PlayerRangeTimelinePresentation.For(_reviewRange, duration);
        ReviewRangeIndicator.HasActiveTrim = presentation.HasSelectedSpan;
        ReviewRangeIndicator.HasProportions = presentation.HasProportions;
        ReviewRangeIndicator.ShowBoundaries = presentation.ShowBoundaries;
        ReviewRangeIndicator.StartFraction = presentation.StartFraction;
        ReviewRangeIndicator.WidthFraction = presentation.WidthFraction;
        var hasIn = _reviewRange?.In is not null;
        var hasOut = _reviewRange?.Out is not null;
        SetInButton.Tag = hasIn ? "Active" : null;
        SetOutButton.Tag = hasOut ? "Active" : null;
        System.Windows.Automation.AutomationProperties.SetItemStatus(SetInButton, hasIn ? "Active" : "");
        System.Windows.Automation.AutomationProperties.SetItemStatus(SetOutButton, hasOut ? "Active" : "");
        InTimeButton.Visibility = ClearInButton.Visibility = hasIn ? Visibility.Visible : Visibility.Collapsed;
        OutTimeButton.Visibility = ClearOutButton.Visibility = hasOut ? Visibility.Visible : Visibility.Collapsed;
        if (_reviewRange?.In is { } rangeIn) InTimeButton.Content = FormatTimestamp(rangeIn);
        if (_reviewRange?.Out is { } rangeOut) OutTimeButton.Content = FormatTimestamp(rangeOut);
    }

    private async Task StopAtOutAsync()
    {
        if (_service is null || _reviewRange is null || _stoppingAtOut) return;
        _stoppingAtOut = true;
        _stopAtOutDuringPlayback = false;
        try
        {
            await _service.PauseAsync().ConfigureAwait(true);
            await _service.SeekAsync(_reviewRange.EffectiveOut).ConfigureAwait(true);
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) { SetStatus(exception.Message); }
        finally { _stoppingAtOut = false; }
    }

    private async void SetIn_Click(object sender, RoutedEventArgs e)
    {
        if (_service?.SourceInfo is not { } info || _service.Snapshot.DisplayedTimestamp is not { } timestamp) return;
        var candidate = new MediaRange(info.Duration, timestamp.Position, _reviewRange?.Out);
        if (candidate.Validate().Count != 0) { SetStatus("In must be before Out and before the end of the source."); return; }
        try { await SaveRangeAsync(candidate); SetStatus(null); }
        catch (Exception exception) { SetStatus($"The range could not be saved. {exception.Message}"); }
    }

    private async void SetOut_Click(object sender, RoutedEventArgs e)
    {
        if (_service?.SourceInfo is not { } info || _service.Snapshot.DisplayedTimestamp is not { } timestamp) return;
        var candidate = new MediaRange(info.Duration, _reviewRange?.In, timestamp.Position);
        if (candidate.Validate().Count != 0) { SetStatus("Out must be after In."); return; }
        try { await SaveRangeAsync(candidate); SetStatus(null); }
        catch (Exception exception) { SetStatus($"The range could not be saved. {exception.Message}"); }
    }

    private async void ClearIn_Click(object sender, RoutedEventArgs e) => await ClearBoundaryAsync(clearIn: true);
    private async void ClearOut_Click(object sender, RoutedEventArgs e) => await ClearBoundaryAsync(clearIn: false);

    private async Task ClearBoundaryAsync(bool clearIn)
    {
        if (_service?.SourceInfo is not { } info || _reviewRange is null) return;
        var range = new MediaRange(info.Duration, clearIn ? null : _reviewRange.In, clearIn ? _reviewRange.Out : null);
        try { await SaveRangeAsync(range.IsFullSource ? null : range); SetStatus(null); }
        catch (Exception exception) { SetStatus($"The range could not be saved. {exception.Message}"); }
    }

    private async void InTime_Click(object sender, RoutedEventArgs e) => await SeekToBoundaryAsync(_reviewRange?.In);
    private async void OutTime_Click(object sender, RoutedEventArgs e) => await SeekToBoundaryAsync(_reviewRange?.Out);

    private async Task SeekToBoundaryAsync(TimeSpan? position)
    {
        if (_service is null || position is null) return;
        RestoreLiveVideoSurface();
        try { await _service.SeekAsync(position.Value); }
        catch (OperationCanceledException) { }
        catch (Exception exception) { SetStatus(exception.Message); }
    }

    private Task<MediaDecodedFrame> CapturePresentedFrameAsync() =>
        (_mediaView ?? throw new InvalidOperationException("No video presentation is active.")).CaptureFrameAsync();

    private async void Screengrab_Click(object sender, RoutedEventArgs e)
    {
        if (_screengrabService is null || _service?.SourceInfo is not { } source || !ScreengrabButton.IsEnabled)
            return;
        var generation = _generation;
        ScreengrabButton.IsEnabled = false;
        SetScreengrabFeedback("Saving…");
        try
        {
            await _frameStepQueue.WaitUntilIdleAsync().ConfigureAwait(true);
            if (generation != _generation || _service is null) return;
            // Backward stepping temporarily hides Flyleaf behind a retained native-size decoded bitmap while
            // the engine reconstructs. Saving that exact retained frame prevents a click during reconstruction
            // from capturing an intermediate frame; ordinary paused/playing capture uses the same backend
            // snapshot path that produced it. Neither path seeks, changes playback state, or touches audio.
            var frame = _retainedSteppedFrame ?? await CapturePresentedFrameAsync().ConfigureAwait(true);
            var result = await _screengrabService.SaveAsync(source.SourcePath, frame).ConfigureAwait(true);
            if (generation == _generation)
            {
                SetScreengrabFeedback(null);
                _lastScreengrabDirectory = Path.GetDirectoryName(result.Path);
                ScreengrabSuccessButton.ToolTip = $"Screengrab saved to {result.Path}. Open folder";
                ScreengrabSuccessButton.Visibility = Visibility.Visible;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            if (generation == _generation)
            {
                SetScreengrabFeedback($"Could not save frame: {exception.Message}");
                ScreengrabFeedbackText.ToolTip = exception.Message;
            }
        }
        finally
        {
            if (generation == _generation && _service is not null)
                ScreengrabButton.IsEnabled = PositionSlider.IsEnabled;
        }
    }

    private void SetScreengrabFeedback(string? message)
    {
        _lastScreengrabDirectory = null;
        ScreengrabSuccessButton.Visibility = Visibility.Collapsed;
        ScreengrabSuccessButton.ToolTip = "Screengrab saved. Open folder";
        ScreengrabFeedbackText.Text = message ?? "";
        ScreengrabFeedbackText.ToolTip = null;
        ScreengrabFeedbackText.Visibility = string.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ScreengrabSuccess_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_lastScreengrabDirectory)) return;
        try { _folderLauncher.Open(_lastScreengrabDirectory); }
        catch (Exception exception)
        {
            SetScreengrabFeedback($"Could not open screengrab folder: {exception.Message}");
            ScreengrabFeedbackText.ToolTip = exception.Message;
        }
    }

    private void RestoreLiveVideoSurface()
    {
        VideoHost.Visibility = Visibility.Visible;
        SteppedFrameSurface.Visibility = Visibility.Collapsed;
        SteppedFrameSurface.Source = null;
        _retainedSteppedFrame = null;
    }

    private static BitmapSource ToBitmapSource(MediaDecodedFrame frame)
    {
        var bitmap = BitmapSource.Create(
            frame.Width, frame.Height, 96, 96, PixelFormats.Bgra32, null, frame.BgraPixels, frame.Stride);
        bitmap.Freeze();
        return bitmap;
    }

    private void BackButton_Click(object sender, RoutedEventArgs e) => BackRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Owns its own keyboard shortcuts rather than relying on the host window, so any future host (#112's
    /// floating window included) gets identical behavior for free. Left/Right perform frame stepping unless
    /// focus is inside a text-entry control or slider that already owns arrow-key interaction.
    /// </summary>
    private void PlayerViewerHost_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key is Key.Left or Key.Right && IsArrowKeyOwnedByFocusedControl(e.OriginalSource as DependencyObject))
            return;
        switch (e.Key)
        {
            case Key.C when _service is not null && ColorToggleButton.IsEnabled &&
                ColorToggleButton.IsChecked == true && !_momentaryColorBypass:
                e.Handled = true;
                _momentaryColorBypass = true;
                ShowMomentaryColorBypass(true);
                RestoreLiveVideoSurface();
                _service.SetColorPipeline(_colorPipeline, true);
                return;
            case Key.Escape:
                e.Handled = true;
                BackRequested?.Invoke(this, EventArgs.Empty);
                return;
            case Key.Space when _service is not null && PositionSlider.IsEnabled:
                e.Handled = true;
                PlayPause_Click(this, new RoutedEventArgs());
                return;
            case Key.I when _service is not null && PositionSlider.IsEnabled:
                e.Handled = true;
                SetIn_Click(this, new RoutedEventArgs());
                return;
            case Key.O when _service is not null && PositionSlider.IsEnabled:
                e.Handled = true;
                SetOut_Click(this, new RoutedEventArgs());
                return;
            case Key.Left when _service is not null && PositionSlider.IsEnabled:
                e.Handled = true;
                RequestStep(forward: false);
                return;
            case Key.Right when _service is not null && PositionSlider.IsEnabled:
                e.Handled = true;
                RequestStep(forward: true);
                return;
        }
    }

    private void PlayerViewerHost_PreviewKeyUp(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.C || !_momentaryColorBypass) return;
        e.Handled = true;
        _momentaryColorBypass = false;
        ShowMomentaryColorBypass(false);
        _service?.SetColorPipeline(_colorPipeline, !_persistentColorEnabled);
    }

    private void PlayerViewerHost_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (!_momentaryColorBypass) return;
        _momentaryColorBypass = false;
        ShowMomentaryColorBypass(false);
        _service?.SetColorPipeline(_colorPipeline, !_persistentColorEnabled);
    }

    private void ShowMomentaryColorBypass(bool bypass)
    {
        _updatingColor = true;
        ColorToggleButton.IsChecked = bypass ? false : _persistentColorEnabled;
        _updatingColor = false;
        UpdateColorSelectorEnabled();
    }

    private static bool IsArrowKeyOwnedByFocusedControl(DependencyObject? element)
    {
        while (element is not null)
        {
            if (element is System.Windows.Controls.Primitives.TextBoxBase or System.Windows.Controls.Slider or
                System.Windows.Controls.Primitives.Thumb or System.Windows.Controls.Primitives.Selector)
                return true;
            element = VisualTreeHelper.GetParent(element);
        }
        return false;
    }

    private static string FormatTimestamp(TimeSpan value)
    {
        var hours = (int)value.TotalHours;
        return $"{hours:00}:{value.Minutes:00}:{value.Seconds:00}.{value.Milliseconds:000}";
    }
}

internal enum PlayerOpenMilestone
{
    PreviousAssetReleaseStarted,
    PreviousAssetReleaseCompleted,
    PlaybackBackendOpenStarted,
    PlaybackBackendOpenCompleted,
    PresentationSurfaceCreated,
    PlayerControlsPublished,
    ColorCacheWaitStarted,
    ColorCacheWaitCompleted,
    ColorAssignmentReadStarted,
    ColorAssignmentReadCompleted,
    RuntimeLutLoadStarted,
    RuntimeLutLoadCompleted,
    ColorPublished
}

internal sealed class MediaRangeStateChangedEventArgs(Guid assetId, bool hasSavedRange) : EventArgs
{
    public Guid AssetId { get; } = assetId;
    public bool HasSavedRange { get; } = hasSavedRange;
}
