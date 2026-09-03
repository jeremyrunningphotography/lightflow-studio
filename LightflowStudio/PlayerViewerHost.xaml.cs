using System.IO;
using System.Collections.ObjectModel;
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

internal sealed record PreviewFrameIntentChangedEventArgs(Guid AssetId, TimeSpan? Position, bool Reset);

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
    private readonly ISubclipService? _subclips;
    private readonly ISubclipPosterService? _subclipPosters;
    private readonly IFrameScreengrabService? _screengrabService;
    private readonly IFolderLauncher _folderLauncher;
    private readonly ILutLibraryCache? _lutCache;
    private readonly IAssetColorStore? _assetColors;
    private readonly IPreferredPreviewFrameStore? _preferredPreviewFrames;
    private readonly IAssetClassificationStore? _classifications;
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
    private MediaRange? _selectedSubclipRange;
    private Guid? _selectedSubclipId;
    private readonly ObservableCollection<SubclipPanelItem> _subclipItems = [];
    private CancellationTokenSource? _subclipWorkCts;
    private bool _subclipsDrawerOpen;
    private bool _stopAtOutDuringPlayback;
    private bool _stoppingAtOut;
    private MediaDecodedFrame? _retainedSteppedFrame;
    private string? _lastScreengrabDirectory;
    private readonly FrameStepQueue _frameStepQueue = new();
    private bool _updatingColor;
    private bool _colorActive;
    private bool _momentaryColorBypass;
    private PlayerColorPipeline? _colorPipeline;
    private LutLibrarySnapshot? _cameraLibrary;
    private LutLibrarySnapshot? _creativeLibrary;
    private long _colorRefreshRevision;
    private bool _previewFrameBusy;
    private AssetClassification? _classification;

    private sealed record LutChoice(Guid? LutId, string DisplayName, bool OpensFolder = false)
    {
        public override string ToString() => DisplayName;
    }
    private sealed record PreparedColor(LutLibrarySnapshot CameraLibrary, LutLibrarySnapshot CreativeLibrary,
        AssetColorIntent Intent, PlayerColorPipeline Pipeline, string? Diagnostic);

    internal PlayerViewerHost(MediaPlaybackCoordinator coordinator, IMediaRangeStore? rangeStore = null,
        ISubclipService? subclips = null, IFrameScreengrabService? screengrabService = null,
        IFolderLauncher? folderLauncher = null, ISubclipPosterService? subclipPosters = null,
        ILutLibraryCache? lutCache = null, IAssetColorStore? assetColors = null,
        Func<string>? cameraLutFolder = null, Func<string>? creativeLutFolder = null,
        Action<PlayerOpenMilestone>? openMilestone = null,
        IPreferredPreviewFrameStore? preferredPreviewFrames = null,
        IAssetClassificationStore? classifications = null)
    {
        _coordinator = coordinator;
        _rangeStore = rangeStore;
        _subclips = subclips;
        _subclipPosters = subclipPosters;
        _screengrabService = screengrabService;
        _folderLauncher = folderLauncher ?? new ShellFolderLauncher();
        _lutCache = lutCache;
        _assetColors = assetColors;
        _cameraLutFolder = cameraLutFolder;
        _creativeLutFolder = creativeLutFolder;
        _openMilestone = openMilestone;
        _preferredPreviewFrames = preferredPreviewFrames;
        _classifications = classifications;
        InitializeComponent();
        SubclipsList.DataContext = _subclipItems;
    }

    /// <summary>Raised by the Back button or Esc. The host decides what "back" means (for the Browser, returning to Grid presentation at its preserved context).</summary>
    public event EventHandler? BackRequested;

    /// <summary>Raised after a saved review-range change commits so any host can refresh its own presentation.</summary>
    internal event EventHandler<MediaRangeStateChangedEventArgs>? RangeStateChanged;
    internal event EventHandler<AssetColorStateChangedEventArgs>? ColorStateChanged;
    internal event EventHandler<SubclipStateChangedEventArgs>? SubclipStateChanged;
    internal event EventHandler<PreviewFrameIntentChangedEventArgs>? PreviewFrameIntentChanged;
    internal event EventHandler<AssetClassification>? ClassificationChanged;
    internal event EventHandler<PlayerViewerExportRequestedEventArgs>? ExportRequested;
    internal event EventHandler<PlayerViewerSubclipsExportRequestedEventArgs>? ExportSelectedSubclipsRequested;
    internal event EventHandler<SubclipsDrawerStateRequestedEventArgs>? SubclipsDrawerStateRequested;
    internal PlayerViewerAsset? CurrentAsset => _currentAsset;
    internal IReadOnlySet<Guid> SelectedSubclipIds =>
        SubclipsList.SelectedItems.Cast<SubclipPanelItem>().Select(item => item.SubclipId).ToHashSet();
    internal Guid? ActiveSubclipId => _selectedSubclipId;

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
        if (_subclipsDrawerOpen) RequestSubclipsDrawer(open: false);
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
        await LoadClassificationAsync(asset.AssetId, generation, token).ConfigureAwait(true);
        ResetSubclipWork();
        SubclipsPanel.Visibility = Visibility.Collapsed;
        AddSubclipButton.IsEnabled = false;
        if (asset.Kind == MediaPresentationKind.Video && asset.AssetId is Guid subclipAssetId)
            await LoadSubclipsAsync(subclipAssetId, generation, _subclipWorkCts!.Token).ConfigureAwait(true);
        if (generation != _generation) return;
        SetExportEnabled(false);
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
            SetExportEnabled(_currentAsset?.AssetId is not null);
            SetAudioControlsEnabled(info.AudioStreams.Count > 0);
            UpdateAudioControlsFromService();
            TransportBar.Visibility = Visibility.Visible;
            PublishCurrentCachedChoices();
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
        SetExportEnabled(false);
        SetAudioControlsEnabled(false);
        _reviewRange = null;
        _selectedSubclipRange = null;
        _selectedSubclipId = null;
        _subclipWorkCts?.Cancel();
        _subclipWorkCts?.Dispose();
        _subclipWorkCts = null;
        _subclipItems.Clear();
        SubclipsPanel.Visibility = Visibility.Collapsed;
        _subclipsDrawerOpen = false;
        _stopAtOutDuringPlayback = false;
        _stoppingAtOut = false;
        _retainedSteppedFrame = null;
        _colorPipeline = null;
        _cameraLibrary = _creativeLibrary = null;
        _colorRefreshRevision++;
        _momentaryColorBypass = false;
        _colorActive = false;
        _updatingColor = true;
        CameraLutCombo.ItemsSource = CreativeLutCombo.ItemsSource = null;
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
        UpdatePausedFrameActions();
    }

    private void UpdatePausedFrameActions()
    {
        var timestamp = _retainedSteppedFrame?.Timestamp ?? _service?.Snapshot.DisplayedTimestamp;
        var canUseDisplayedFrame = PositionSlider.IsEnabled && !_previewFrameBusy &&
            _currentAsset?.Kind == MediaPresentationKind.Video &&
            _service?.Snapshot.State == MediaPlaybackState.Paused &&
            timestamp is { IsDecodedPresentationTimestamp: true };
        ScreengrabButton.IsEnabled = canUseDisplayedFrame && _screengrabService is not null;
        SetPreviewFrameButton.IsEnabled = canUseDisplayedFrame && _currentAsset?.AssetId is not null &&
            _preferredPreviewFrames is not null;
    }

    private void SetColorControlsEnabled(bool enabled)
    {
        CameraLutCombo.IsEnabled = CreativeLutCombo.IsEnabled = enabled;
    }

    internal void SetExportEnabled(bool enabled) => ExportButton.IsEnabled = enabled;
    internal void SetSelectedSubclipExportEnabled(bool enabled)
    {
        ExportSelectedSubclipsMenuItem.IsEnabled = enabled;
        ExportAllSubclipsMenuItem.IsEnabled = _subclipItems.Count > 0;
    }

    private void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentAsset is not { Kind: MediaPresentationKind.Video, AssetId: Guid assetId }) return;
        SetExportEnabled(false);
        ExportRequested?.Invoke(this, new PlayerViewerExportRequestedEventArgs(assetId));
    }

    private void PublishCurrentCachedChoices()
    {
        if (_lutCache is null) return;
        var camera = _lutCache.Snapshot(ColorLutStage.Camera);
        var creative = _lutCache.Snapshot(ColorLutStage.Creative);
        var cameraChoices = MakeChoices(camera, null);
        var creativeChoices = MakeChoices(creative, null);
        _updatingColor = true;
        CameraLutCombo.ItemsSource = cameraChoices;
        CreativeLutCombo.ItemsSource = creativeChoices;
        CameraLutCombo.SelectedItem = cameraChoices[0];
        CreativeLutCombo.SelectedItem = creativeChoices[0];
        _cameraLibrary = camera;
        _creativeLibrary = creative;
        _updatingColor = false;
    }

    private async Task<PreparedColor?> PrepareColorAsync(CancellationToken token)
    {
        if (_currentAsset?.AssetId is not Guid assetId || _lutCache is null || _assetColors is null
            || _cameraLutFolder is null || _creativeLutFolder is null) return null;
        LutLibrarySnapshot cameraLibrary, creativeLibrary;
        AssetColorIntent intent;
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
            _colorActive = prepared.Intent.IsActive;
            _service.SetColorPipeline(prepared.Pipeline, !_colorActive || _momentaryColorBypass);
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
            _colorActive = intent.IsActive;
            _service.SetColorPipeline(_colorPipeline, !_colorActive || _momentaryColorBypass);
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
            _colorActive = intent.IsActive;
            _service?.SetColorPipeline(_colorPipeline, !_colorActive || _momentaryColorBypass);
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
        try
        {
            await _assetColors.SetStageAsync([assetId], stage, choice.LutId);
            SetStatus(null);
            var committed = await _assetColors.GetAsync(assetId);
            ColorStateChanged?.Invoke(this, new(assetId, committed.HasColor));
            await ApplyColorIntentAsync(committed, CancellationToken.None);
        }
        catch (Exception exception) { SetStatus($"Color assignment could not be saved. {exception.Message}"); }
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
            ReviewRangePlaybackPolicy.HasReachedArmedOutBoundary(ActivePlaybackRange, _stopAtOutDuringPlayback, displayed.Position) &&
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
        else UpdatePausedFrameActions();
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
                _stopAtOutDuringPlayback = ReviewRangePlaybackPolicy.ShouldArmOutBoundary(ActivePlaybackRange, position);
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
        var presentedRange = PresentedRange;
        var presentation = PlayerRangeTimelinePresentation.For(presentedRange, duration);
        ReviewRangeIndicator.HasActiveTrim = presentation.HasSelectedSpan;
        ReviewRangeIndicator.HasProportions = presentation.HasProportions;
        ReviewRangeIndicator.ShowBoundaries = presentation.ShowBoundaries;
        ReviewRangeIndicator.StartFraction = presentation.StartFraction;
        ReviewRangeIndicator.WidthFraction = presentation.WidthFraction;
        var hasIn = presentedRange?.In is not null;
        var hasOut = presentedRange?.Out is not null;
        SetInButton.Tag = hasIn ? "Active" : null;
        SetOutButton.Tag = hasOut ? "Active" : null;
        System.Windows.Automation.AutomationProperties.SetItemStatus(SetInButton, hasIn ? "Active" : "");
        System.Windows.Automation.AutomationProperties.SetItemStatus(SetOutButton, hasOut ? "Active" : "");
        InTimeButton.Visibility = ClearInButton.Visibility = hasIn ? Visibility.Visible : Visibility.Collapsed;
        OutTimeButton.Visibility = ClearOutButton.Visibility = hasOut ? Visibility.Visible : Visibility.Collapsed;
        if (presentedRange?.In is { } rangeIn) InTimeButton.Content = FormatTimestamp(rangeIn);
        if (presentedRange?.Out is { } rangeOut) OutTimeButton.Content = FormatTimestamp(rangeOut);
        AddSubclipButton.IsEnabled = CurrentSubclipCreationEligibility().CanCreate;
    }

    private SubclipCreationEligibility CurrentSubclipCreationEligibility() =>
        SubclipCreationEligibility.Evaluate(
            _subclips is not null && _currentAsset is { Kind: MediaPresentationKind.Video, AssetId: not null },
            _reviewRange, _service?.SourceInfo?.Duration);

    private async Task StopAtOutAsync()
    {
        var playbackRange = ActivePlaybackRange;
        if (_service is null || playbackRange is null || _stoppingAtOut) return;
        _stoppingAtOut = true;
        _stopAtOutDuringPlayback = false;
        try
        {
            await _service.PauseAsync().ConfigureAwait(true);
            await _service.SeekAsync(playbackRange.EffectiveOut).ConfigureAwait(true);
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) { SetStatus(exception.Message); }
        finally { _stoppingAtOut = false; }
    }

    private async void SetIn_Click(object sender, RoutedEventArgs e)
    {
        if (_service?.SourceInfo is not { } info || _service.Snapshot.DisplayedTimestamp is not { } timestamp) return;
        ExitSubclipReviewForWorkingRangeEdit();
        var candidate = ReviewRangeBoundaryPolicy.SetIn(info.Duration, _reviewRange, timestamp.Position);
        if (candidate.Validate().Count != 0) { SetStatus("In must be before the end of the source."); return; }
        try { await SaveRangeAsync(candidate); SetStatus(null); }
        catch (Exception exception) { SetStatus($"The range could not be saved. {exception.Message}"); }
    }

    private async void SetOut_Click(object sender, RoutedEventArgs e)
    {
        if (_service?.SourceInfo is not { } info || _service.Snapshot.DisplayedTimestamp is not { } timestamp) return;
        ExitSubclipReviewForWorkingRangeEdit();
        var candidate = ReviewRangeBoundaryPolicy.SetOut(info.Duration, _reviewRange, timestamp.Position);
        if (candidate.Validate().Count != 0) { SetStatus("Out must be after the start of the source."); return; }
        try { await SaveRangeAsync(candidate); SetStatus(null); }
        catch (Exception exception) { SetStatus($"The range could not be saved. {exception.Message}"); }
    }

    private async void CreateSubclip()
    {
        var eligibility = CurrentSubclipCreationEligibility();
        if (!eligibility.CanCreate || eligibility.MaterializedRange is not { } range)
        { SetStatus(eligibility.Problem); return; }
        if (_subclips is null || _currentAsset?.AssetId is not Guid assetId) return;
        try
        {
            var result = await _subclips.CreateAsync(assetId, range);
            var subclip = result.Subclip;
            if (_currentAsset?.AssetId == assetId)
            {
                var item = _subclipItems.FirstOrDefault(candidate => candidate.SubclipId == subclip.SubclipId);
                if (item is null)
                {
                    item = new SubclipPanelItem(subclip);
                    var insertAt = 0;
                    while (insertAt < _subclipItems.Count &&
                           SubclipCurrentOrder.Compare(_subclipItems[insertAt].Subclip, subclip) < 0) insertAt++;
                    _subclipItems.Insert(insertAt, item);
                }
                UpdateSubclipEmptyState();
                if (result.Created && _subclipWorkCts is { } work) _ = LoadPosterAsync(item, _generation, work.Token);
                SubclipsList.SelectedItems.Clear();
                SubclipsList.SelectedItem = item;
                SubclipsList.ScrollIntoView(item);
            }
            RequestSubclipsDrawer(open: true);
            SubclipStateChanged?.Invoke(this, new(assetId, hasSubclips: true));
            SetStatus(result.Created ? $"{subclip.Name} created." : null);
        }
        catch (Exception exception) { SetStatus($"The Subclip could not be created. {exception.Message}"); }
    }

    private async void ClearIn_Click(object sender, RoutedEventArgs e) => await ClearBoundaryAsync(clearIn: true);
    private async void ClearOut_Click(object sender, RoutedEventArgs e) => await ClearBoundaryAsync(clearIn: false);

    private async Task ClearBoundaryAsync(bool clearIn)
    {
        ExitSubclipReviewForWorkingRangeEdit();
        if (_service?.SourceInfo is not { } info || _reviewRange is null) return;
        var range = new MediaRange(info.Duration, clearIn ? null : _reviewRange.In, clearIn ? _reviewRange.Out : null);
        try { await SaveRangeAsync(range.IsFullSource ? null : range); SetStatus(null); }
        catch (Exception exception) { SetStatus($"The range could not be saved. {exception.Message}"); }
    }

    private async void InTime_Click(object sender, RoutedEventArgs e) => await SeekToBoundaryAsync(PresentedRange?.In);
    private async void OutTime_Click(object sender, RoutedEventArgs e) => await SeekToBoundaryAsync(PresentedRange?.Out);

    private void ExitSubclipReviewForWorkingRangeEdit()
    {
        if (_selectedSubclipId is null && _selectedSubclipRange is null) return;
        SubclipsList.UnselectAll();
        _selectedSubclipId = null;
        _selectedSubclipRange = null;
        UpdateRangePresentation();
    }

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
        if (_screengrabService is null || _service?.SourceInfo is not { } source || !ScreengrabButton.IsEnabled ||
            _service.Snapshot.State != MediaPlaybackState.Paused)
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
                UpdatePausedFrameActions();
        }
    }

    private async void SetPreviewFrame_Click(object sender, RoutedEventArgs e)
    {
        if (_preferredPreviewFrames is null || _currentAsset?.AssetId is not Guid assetId ||
            _service?.SourceInfo is not { } source || !SetPreviewFrameButton.IsEnabled) return;
        var generation = _generation;
        _previewFrameBusy = true;
        UpdatePausedFrameActions();
        try
        {
            await _frameStepQueue.WaitUntilIdleAsync().ConfigureAwait(true);
            if (generation != _generation || _service?.Snapshot.State != MediaPlaybackState.Paused) return;
            var timestamp = _retainedSteppedFrame?.Timestamp ?? _service.Snapshot.DisplayedTimestamp;
            if (timestamp is not { IsDecodedPresentationTimestamp: true }) return;
            await _preferredPreviewFrames.SetAsync(assetId, timestamp, source.Duration).ConfigureAwait(true);
            if (generation == _generation)
            {
                SetStatus($"Browser Preview set to {FormatTimestamp(timestamp.Position)}.");
                PreviewFrameIntentChanged?.Invoke(this, new(assetId, timestamp.Position, false));
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or Microsoft.Data.Sqlite.SqliteException)
        {
            if (generation == _generation) SetStatus($"Could not set Browser Preview: {exception.Message}");
        }
        finally
        {
            if (generation == _generation) { _previewFrameBusy = false; UpdatePausedFrameActions(); }
        }
    }

    private async void ResetPreviewFrame_Click(object sender, RoutedEventArgs e)
    {
        if (_preferredPreviewFrames is null || _currentAsset?.AssetId is not Guid assetId) return;
        var generation = _generation;
        _previewFrameBusy = true;
        UpdatePausedFrameActions();
        try
        {
            await _preferredPreviewFrames.ResetAsync(assetId).ConfigureAwait(true);
            if (generation == _generation)
            {
                SetStatus("Browser Preview reset to automatic selection.");
                PreviewFrameIntentChanged?.Invoke(this, new(assetId, null, true));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or Microsoft.Data.Sqlite.SqliteException)
        {
            if (generation == _generation) SetStatus($"Could not reset Browser Preview: {exception.Message}");
        }
        finally
        {
            if (generation == _generation) { _previewFrameBusy = false; UpdatePausedFrameActions(); }
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

    private MediaRange? PresentedRange => _selectedSubclipRange ?? _reviewRange;
    private MediaRange? ActivePlaybackRange => PresentedRange;

    private void ResetSubclipWork()
    {
        _subclipWorkCts?.Cancel();
        _subclipWorkCts?.Dispose();
        _subclipWorkCts = new CancellationTokenSource();
        _selectedSubclipId = null;
        _selectedSubclipRange = null;
        _subclipItems.Clear();
        UpdateRangePresentation();
        UpdateSubclipEmptyState();
    }

    private async Task LoadSubclipsAsync(Guid assetId, long generation, CancellationToken token)
    {
        if (_subclips is null) { UpdateSubclipEmptyState(); return; }
        try
        {
            var subclips = await _subclips.ListAsync(assetId, token).ConfigureAwait(true);
            if (generation != _generation || _currentAsset?.AssetId != assetId || token.IsCancellationRequested) return;
            _subclipItems.Clear();
            foreach (var subclip in SubclipCurrentOrder.Apply(subclips))
            {
                var item = new SubclipPanelItem(subclip);
                _subclipItems.Add(item);
                _ = LoadPosterAsync(item, generation, token);
            }
            UpdateSubclipEmptyState();
            if (subclips.Count > 0) RequestSubclipsDrawer(open: true);
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            if (generation == _generation) SetStatus($"Subclips unavailable: {exception.Message}");
        }
    }

    private async Task LoadPosterAsync(SubclipPanelItem item, long generation, CancellationToken token)
    {
        if (_subclipPosters is null) return;
        try
        {
            var result = await _subclipPosters.GetAsync(item.Subclip, token).ConfigureAwait(true);
            if (!result.Succeeded || generation != _generation || token.IsCancellationRequested ||
                _currentAsset?.AssetId != item.Subclip.AssetId || !_subclipItems.Contains(item)) return;
            using var stream = new FileStream(result.Path!, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 64 * 1024, FileOptions.SequentialScan);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            if (decoder.Frames.Count == 0) return;
            var bitmap = decoder.Frames[0];
            bitmap.Freeze();
            item.Poster = bitmap;
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FileFormatException) { }
    }

    private void UpdateSubclipEmptyState()
    {
        var hasSubclips = _subclipItems.Count > 0;
        SubclipsEmptyText.Visibility = hasSubclips ? Visibility.Collapsed : Visibility.Visible;
        ExportSubclipsButton.IsEnabled = hasSubclips;
        ExportAllSubclipsMenuItem.IsEnabled = hasSubclips;
    }

    internal void SetSubclipsDrawerOpen(bool open)
    {
        _subclipsDrawerOpen = open && _currentAsset is { Kind: MediaPresentationKind.Video, AssetId: not null };
        SubclipsPanel.Visibility = _subclipsDrawerOpen ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RequestSubclipsDrawer(bool open)
    {
        if (SubclipsDrawerStateRequested is null) SetSubclipsDrawerOpen(open);
        else SubclipsDrawerStateRequested.Invoke(this, new(open));
    }

    private void AddSubclip_Click(object sender, RoutedEventArgs e) => CreateSubclip();

    private void ExportSubclips_Click(object sender, RoutedEventArgs e)
    {
        ExportSubclipsMenu.PlacementTarget = ExportSubclipsButton;
        ExportSubclipsMenu.IsOpen = true;
    }

    private void ExportSelectedSubclips_Click(object sender, RoutedEventArgs e) =>
        RequestSubclipExport(selectedOnly: true);

    private void ExportAllSubclips_Click(object sender, RoutedEventArgs e) =>
        RequestSubclipExport(selectedOnly: false);

    private void RequestSubclipExport(bool selectedOnly)
    {
        if (_currentAsset?.AssetId is not Guid assetId) return;
        var selectedIds = SelectedSubclipIds;
        var selected = _subclipItems.Where(item => !selectedOnly || selectedIds.Contains(item.SubclipId))
            .Select(item => item.SubclipId).ToArray();
        if (selected.Length == 0) return;
        if (selectedOnly) ExportSelectedSubclipsMenuItem.IsEnabled = false;
        else ExportAllSubclipsMenuItem.IsEnabled = false;
        ExportSelectedSubclipsRequested?.Invoke(this,
            new PlayerViewerSubclipsExportRequestedEventArgs(assetId, selected));
    }

    private void SubclipsPanel_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var source = e.OriginalSource as DependencyObject;
        if (FindVisualAncestor<System.Windows.Controls.ListBoxItem>(source) is not null ||
            FindVisualAncestor<System.Windows.Controls.Primitives.ScrollBar>(source) is not null ||
            FindVisualAncestor<System.Windows.Controls.Primitives.ButtonBase>(source) is not null ||
            FindVisualAncestor<System.Windows.Controls.Primitives.TextBoxBase>(source) is not null)
            return;
        SubclipsList.UnselectAll();
    }

    private async void SubclipsList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        foreach (var item in _subclipItems) item.IsSelected = SubclipsList.SelectedItems.Contains(item);
        DeleteSelectedSubclipsButton.IsEnabled = SubclipsList.SelectedItems.Count > 0;
        ExportSelectedSubclipsMenuItem.IsEnabled = SubclipsList.SelectedItems.Count > 0;
        var selected = e.AddedItems.Cast<SubclipPanelItem>().LastOrDefault()
            ?? SubclipsList.SelectedItems.Cast<SubclipPanelItem>().FirstOrDefault(item => item.SubclipId == _selectedSubclipId)
            ?? SubclipsList.SelectedItems.Cast<SubclipPanelItem>().LastOrDefault();
        if (selected is null)
        {
            _selectedSubclipId = null;
            _selectedSubclipRange = null;
            UpdateRangePresentation();
            return;
        }
        if (_service is null) return;
        SetActiveSubclipReview(selected);
        RestoreLiveVideoSurface();
        try
        {
            await _service.SeekAsync(selected.Subclip.In).ConfigureAwait(true);
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) { SetStatus(exception.Message); }
    }

    private async void SubclipsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FindVisualAncestor<System.Windows.Controls.Primitives.ButtonBase>(e.OriginalSource as DependencyObject) is not null ||
            FindVisualAncestor<System.Windows.Controls.Primitives.TextBoxBase>(e.OriginalSource as DependencyObject) is not null)
            return;
        var item = (e.OriginalSource as FrameworkElement)?.DataContext as SubclipPanelItem
            ?? (e.Source as FrameworkElement)?.DataContext as SubclipPanelItem
            ?? FindVisualAncestor<System.Windows.Controls.ListBoxItem>(e.OriginalSource as DependencyObject)?.DataContext as SubclipPanelItem;
        if (_service is null || item is null) return;
        SetActiveSubclipReview(item);
        if (!SubclipsList.SelectedItems.Contains(item)) SubclipsList.SelectedItems.Add(item);
        RestoreLiveVideoSurface();
        try
        {
            await _service.SeekAsync(item.Subclip.In).ConfigureAwait(true);
            _stopAtOutDuringPlayback = true;
            await _service.PlayAsync().ConfigureAwait(true);
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) { SetStatus(exception.Message); }
    }

    private void SetActiveSubclipReview(SubclipPanelItem item)
    {
        _selectedSubclipId = item.SubclipId;
        _selectedSubclipRange = new(item.Subclip.SourceDuration, item.Subclip.In, item.Subclip.Out);
        UpdateRangePresentation();
    }

    private void RenameSubclip_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not SubclipPanelItem item) return;
        item.IsEditing = true;
        Dispatcher.BeginInvoke(() =>
        {
            if (FindVisualChild<System.Windows.Controls.TextBox>(SubclipsList.ItemContainerGenerator.ContainerFromItem(item)) is { } editor)
            { editor.Text = item.Name; editor.Focus(); editor.SelectAll(); }
        }, System.Windows.Threading.DispatcherPriority.Input);
    }

    private async void SubclipName_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is System.Windows.Controls.TextBox { DataContext: SubclipPanelItem item } editor && item.IsEditing)
            await CommitRenameAsync(item, editor.Text).ConfigureAwait(true);
    }

    private async void SubclipName_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (sender is not System.Windows.Controls.TextBox { DataContext: SubclipPanelItem item } editor) return;
        if (e.Key == Key.Escape) { e.Handled = true; editor.Text = item.Name; item.IsEditing = false; Focus(); return; }
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        await CommitRenameAsync(item, editor.Text).ConfigureAwait(true);
        Focus();
    }

    private async Task CommitRenameAsync(SubclipPanelItem item, string name)
    {
        if (_subclips is null || !item.IsEditing) return;
        if (string.IsNullOrWhiteSpace(name)) { item.IsEditing = false; return; }
        if (string.Equals(name.Trim(), item.Name, StringComparison.Ordinal)) { item.IsEditing = false; return; }
        try
        {
            var updated = await _subclips.RenameAsync(item.SubclipId, item.Subclip.Revision, name).ConfigureAwait(true);
            item.Replace(updated);
            item.IsEditing = false;
        }
        catch (SubclipConcurrencyException exception)
        {
            item.IsEditing = false;
            SetStatus(exception.Message);
            await ReloadCurrentSubclipsAsync().ConfigureAwait(true);
        }
        catch (Exception exception) { SetStatus($"Rename failed: {exception.Message}"); }
    }

    private async void DeleteSubclip_Click(object sender, RoutedEventArgs e)
    {
        if (_subclips is null || (sender as FrameworkElement)?.Tag is not SubclipPanelItem item) return;
        try
        {
            await _subclips.DeleteAsync(item.SubclipId, item.Subclip.Revision).ConfigureAwait(true);
            _subclipPosters?.Remove(item.Subclip.AssetId, item.SubclipId);
            if (_selectedSubclipId == item.SubclipId)
            {
                _selectedSubclipId = null;
                _selectedSubclipRange = null;
                UpdateRangePresentation();
            }
            _subclipItems.Remove(item);
            UpdateSubclipEmptyState();
            SubclipStateChanged?.Invoke(this, new(item.Subclip.AssetId, _subclipItems.Count > 0));
        }
        catch (SubclipConcurrencyException exception)
        {
            SetStatus(exception.Message);
            await ReloadCurrentSubclipsAsync().ConfigureAwait(true);
        }
        catch (Exception exception) { SetStatus($"Delete failed: {exception.Message}"); }
    }

    private async void DeleteSelectedSubclips_Click(object sender, RoutedEventArgs e)
    {
        if (_subclips is null || _currentAsset?.AssetId is not Guid assetId) return;
        var selected = SubclipsList.SelectedItems.Cast<SubclipPanelItem>().ToArray();
        if (selected.Length == 0) return;
        if (selected.Length > 1 && Window.GetWindow(this) is { } owner && !ConfirmationDialog.Confirm(owner,
                "Delete Subclips", $"Delete {selected.Length} selected Subclips?",
                "This removes only the saved Subclip definitions.",
                "Source media, the working range, Jobs, and unselected Subclips are not changed.", "Delete Subclips"))
            return;
        try
        {
            await _subclips.DeleteAsync(assetId,
                selected.Select(item => new SubclipOrder(item.SubclipId, item.Subclip.Revision)).ToArray()).ConfigureAwait(true);
            foreach (var item in selected) _subclipPosters?.Remove(item.Subclip.AssetId, item.SubclipId);
            if (_selectedSubclipId is Guid active && selected.Any(item => item.SubclipId == active))
            { _selectedSubclipId = null; _selectedSubclipRange = null; UpdateRangePresentation(); }
            var settleIndex = Math.Min(selected.Min(item => _subclipItems.IndexOf(item)),
                Math.Max(0, _subclipItems.Count - selected.Length - 1));
            foreach (var item in selected) _subclipItems.Remove(item);
            UpdateSubclipEmptyState();
            SubclipStateChanged?.Invoke(this, new(assetId, _subclipItems.Count > 0));
            if (_subclipItems.Count > 0) SubclipsList.SelectedItem = _subclipItems[settleIndex];
        }
        catch (SubclipConcurrencyException exception)
        {
            SetStatus(exception.Message);
            await ReloadCurrentSubclipsAsync().ConfigureAwait(true);
        }
        catch (Exception exception) { SetStatus($"Delete failed: {exception.Message}"); }
    }

    private async Task ReloadCurrentSubclipsAsync()
    {
        if (_currentAsset?.AssetId is not Guid assetId || _subclipWorkCts is not { } work) return;
        await LoadSubclipsAsync(assetId, _generation, work.Token).ConfigureAwait(true);
    }

    private static T? FindVisualChild<T>(DependencyObject? parent) where T : DependencyObject
    {
        if (parent is null) return null;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match) return match;
            if (FindVisualChild<T>(child) is { } descendant) return descendant;
        }
        return null;
    }

    private static T? FindVisualAncestor<T>(DependencyObject? child) where T : DependencyObject
    {
        while (child is not null)
        {
            if (child is T match) return match;
            child = VisualTreeHelper.GetParent(child);
        }
        return null;
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
        e.Handled = TryHandleShortcut(e.Key, e.OriginalSource as DependencyObject);
    }

    internal bool TryHandleShortcut(Key key, DependencyObject? inputOwner)
    {
        if (IsTextEntryControl(inputOwner)) return false;
        if (key is Key.Left or Key.Right && IsArrowKeyOwnedByFocusedControl(inputOwner)) return false;
        if (key >= Key.D0 && key <= Key.D5)
        {
            _ = SetRatingAsync(key - Key.D0, toggleCurrent: false);
            return _currentAsset?.AssetId is not null;
        }
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && key is Key.Up or Key.Down)
        {
            _ = StepFlagAsync(key == Key.Up ? 1 : -1);
            return _currentAsset?.AssetId is not null;
        }
        switch (key)
        {
            case Key.C when _service is not null && _colorActive && !_momentaryColorBypass:
                _momentaryColorBypass = true;
                RestoreLiveVideoSurface();
                _service.SetColorPipeline(_colorPipeline, true);
                return true;
            case Key.Escape:
                BackRequested?.Invoke(this, EventArgs.Empty);
                return true;
            case Key.Space:
                if (_service is not null && PositionSlider.IsEnabled) PlayPause_Click(this, new RoutedEventArgs());
                return true;
            case Key.I:
                if (_service is not null && PositionSlider.IsEnabled) SetIn_Click(this, new RoutedEventArgs());
                return true;
            case Key.O:
                if (_service is not null && PositionSlider.IsEnabled) SetOut_Click(this, new RoutedEventArgs());
                return true;
            case Key.S:
                CreateSubclip();
                return true;
            case Key.Left:
                if (_service is not null && PositionSlider.IsEnabled) RequestStep(forward: false);
                return true;
            case Key.Right:
                if (_service is not null && PositionSlider.IsEnabled) RequestStep(forward: true);
                return true;
        }
        return false;
    }

    private async Task LoadClassificationAsync(Guid? assetId, long generation, CancellationToken token)
    {
        _classification = assetId is { } id && _classifications is not null
            ? (await _classifications.GetAsync([id], token).ConfigureAwait(true)).GetValueOrDefault(id)
            : null;
        if (generation == _generation) SyncClassificationControls();
    }

    private void SyncClassificationControls()
    {
        var ratingButtons = new[] { PlayerRating1, PlayerRating2, PlayerRating3, PlayerRating4, PlayerRating5 };
        for (var index = 0; index < ratingButtons.Length; index++)
        {
            ratingButtons[index].IsEnabled = _classification is not null;
            ratingButtons[index].IsChecked = _classification?.Rating >= index + 1;
        }
        PlayerReject.IsEnabled = PlayerUnflagged.IsEnabled = PlayerPick.IsEnabled = _classification is not null;
        PlayerReject.IsChecked = _classification?.Flag == AssetFlag.Rejected;
        PlayerUnflagged.IsChecked = _classification?.Flag == AssetFlag.Unflagged;
        PlayerPick.IsChecked = _classification?.Flag == AssetFlag.Picked;
        var labelButtons = new[] { PlayerNoLabel, PlayerLabelRed, PlayerLabelYellow, PlayerLabelGreen, PlayerLabelBlue, PlayerLabelPurple };
        foreach (var button in labelButtons) button.IsEnabled = _classification is not null;
        PlayerNoLabel.IsChecked = _classification?.ColorLabel is null;
        PlayerLabelRed.IsChecked = _classification?.ColorLabel == AssetColorLabel.Red;
        PlayerLabelYellow.IsChecked = _classification?.ColorLabel == AssetColorLabel.Yellow;
        PlayerLabelGreen.IsChecked = _classification?.ColorLabel == AssetColorLabel.Green;
        PlayerLabelBlue.IsChecked = _classification?.ColorLabel == AssetColorLabel.Blue;
        PlayerLabelPurple.IsChecked = _classification?.ColorLabel == AssetColorLabel.Purple;
    }

    private async Task SaveClassificationAsync(AssetClassification value)
    {
        if (_classifications is null || _currentAsset?.AssetId != value.AssetId) return;
        await _classifications.SaveAsync(value).ConfigureAwait(true);
        _classification = value;
        SyncClassificationControls();
        ClassificationChanged?.Invoke(this, value);
    }

    private Task SetRatingAsync(int rating, bool toggleCurrent) => _classification is { } value
        ? SaveClassificationAsync(value with { Rating = AssetClassificationCommandPolicy.SetRating(value.Rating, rating, toggleCurrent) }) : Task.CompletedTask;
    private Task StepFlagAsync(int delta) => _classification is { } value
        ? SaveClassificationAsync(value with { Flag = AssetClassificationCommandPolicy.StepFlag(value.Flag, delta) }) : Task.CompletedTask;
    private void PlayerRating_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton { Tag: string text } && int.TryParse(text, out var rating))
            _ = SetRatingAsync(rating, toggleCurrent: true);
    }
    private void PlayerFlag_Click(object sender, RoutedEventArgs e)
    {
        if (_classification is { } value && sender is ToggleButton { Tag: string text } && Enum.TryParse<AssetFlag>(text, out var flag))
            _ = SaveClassificationAsync(value with { Flag = flag });
    }
    private void PlayerColorLabel_Click(object sender, RoutedEventArgs e)
    {
        if (_classification is not { } value || sender is not ToggleButton { Tag: string text }) return;
        AssetColorLabel? label = text == "None" ? null : Enum.TryParse<AssetColorLabel>(text, out var parsed) ? parsed : null;
        _ = SaveClassificationAsync(value with { ColorLabel = label });
    }

    private void PlayerViewerHost_PreviewKeyUp(object sender, System.Windows.Input.KeyEventArgs e)
    {
        e.Handled = TryHandleShortcutKeyUp(e.Key);
    }

    internal bool TryHandleShortcutKeyUp(Key key)
    {
        if (key != Key.C || !_momentaryColorBypass) return false;
        _momentaryColorBypass = false;
        _service?.SetColorPipeline(_colorPipeline, !_colorActive);
        return true;
    }

    private void PlayerViewerHost_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (!_momentaryColorBypass) return;
        _momentaryColorBypass = false;
        _service?.SetColorPipeline(_colorPipeline, !_colorActive);
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

    internal static bool IsTextEntryControl(DependencyObject? element)
    {
        while (element is not null)
        {
            if (element is System.Windows.Controls.Primitives.TextBoxBase ||
                element is System.Windows.Controls.ComboBox combo && combo.IsEditable)
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

internal sealed class AssetColorStateChangedEventArgs(Guid assetId, bool hasColor) : EventArgs
{
    public Guid AssetId { get; } = assetId;
    public bool HasColor { get; } = hasColor;
}

internal sealed class SubclipStateChangedEventArgs(Guid assetId, bool hasSubclips) : EventArgs
{
    public Guid AssetId { get; } = assetId;
    public bool HasSubclips { get; } = hasSubclips;
}

internal sealed class SubclipsDrawerStateRequestedEventArgs(bool open) : EventArgs
{
    public bool Open { get; } = open;
}
