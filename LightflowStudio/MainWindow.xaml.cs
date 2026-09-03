using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Controls.Primitives;
using System.Windows.Automation;
using System.Windows.Input;
using Microsoft.Data.Sqlite;
using Forms = System.Windows.Forms;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using MessageBox = System.Windows.MessageBox;

namespace LightflowStudio;

internal enum RightDrawerKind { None, Jobs, Subclips }

public partial class MainWindow : Window
{
    private const double BrowserCollectionRowHeight = 26;
    private const double BrowserCollectionIndent = 19;
    private static bool JobsRuntimeEnabled => true;
    private string? _ffmpeg;
    private string? _ffprobe;
    private JobCancellation? _jobCancellation;
    private readonly BatchProgressState _batchProgress = new();
    private readonly string? _commandLineFolder;
    private readonly bool _jobsWorkspaceSmokeTest;
    private readonly LightflowStorageCoordinator _storage;
    private readonly StorageStartupStatus _storageStartupStatus;
    private readonly string? _storageDiagnostic;
    private AppSettings _settings = new();
    private AppState _state = new();
    private Process? _activeEncodingProcess;
    private JobExecution<EncodingJobOptions, EncodingItemResult>? _activeEncodingJob;
    private readonly EncodingPauseController _encodingPause = new();
    private readonly ObservableCollection<BatchFileOption> _batchFiles = [];
    private readonly BatchFileSelectionMemory _batchSelectionMemory = new();
    private readonly ActivityLogFile _activityLogFile = App.ActivityLog;
    private readonly ITrimHistoryStore _trimHistory;
    private readonly IJobHistoryStore _jobHistory;
    private readonly JobRuntimeStore<EncodingJobOptions, EncodingItemResult> _jobRuntimeStore;
    private readonly ApplicationJobsRuntime<EncodingJobOptions, EncodingItemResult> _jobsRuntime;
    private readonly GlobalExportScheduler _exportScheduler;
    private readonly ExportJobCoordinator _exportCoordinator;
    private readonly ObservableCollection<JobCardPresentation> _jobsDrawerCards = [];
    private readonly HashSet<Guid> _expandedJobIds = [];
    private readonly HashSet<Guid> _dismissedTerminalJobIds = [];
    private readonly HashSet<Guid> _deletedFullJobsTerminalJobIds = [];
    private int _jobsPresentationPending;
    private double _jobsDrawerWidth = 380;
    private RightDrawerKind _openRightDrawer;
    private bool _subclipsContextAvailable;
    private double _browserLocationsPreferredWidth = 280;
    private bool _applyingBrowserResponsiveLayout;
    private JobRuntime<EncodingJobOptions, EncodingItemResult>? _activeJobRuntime;
    private EncodingJobExecutor? _activeJobExecutor;
    private readonly ObservableCollection<JobsWorkspaceItem> _historyRecords = [];
    private bool _synchronizingJobsSelection;
    private IReadOnlyList<EncodingJobHistoryRecord> _durableHistoryRecords = [];
    private readonly ObservableCollection<MediaRootInfo> _mediaRoots = [];
    private IReadOnlyList<LutOption> _lutOptions = [LutCatalog.NoLut];
    private long _lutSettingsRevision;
    private readonly ObservableCollection<BrowserStorageEntry> _browserStorageEntries = [];
    private readonly BrowserGridModel _browserGrid = new();
    private readonly BrowserTreeModel _browserTree = new();
    private readonly BrowserCollectionTreeModel _browserCollectionTree = new();
    private readonly BrowserCollectionScopeService _browserCollectionScopes;
    private readonly BrowserNavigationSession _browserNavigation;
    private BrowserFolderState? _lastLoadedBrowserState;
    private readonly DispatcherTimer _batchFolderRefreshTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private CancellationTokenSource? _batchMetadataCts;
    private CancellationTokenSource? _previewMaintenanceCts;
    private readonly Dictionary<ToggleButton, CancellationTokenSource> _requirementHelpDismissals = [];
    private Stopwatch? _batchStopwatch;
    private bool _closeAfterCurrent;
    private bool _forceClose;
    private bool _subfolderUsesResolutionDefault = true;
    private bool _updatingSubfolderName;
    private bool _filenameSuffixUsesResolutionDefault = true;
    private bool _updatingFilenameSuffix;
    private static readonly double[] FrameRateValues = [0, 23.976, 24, 25, 29.97, 30, 50, 59.94, 60];
    private static readonly int[] AudioSampleRates = [0, 44100, 48000, 96000];
    private long _browserUiGeneration;
    private long _browserAssetStateRevision;
    private readonly Dictionary<Guid, long> _browserAssetStateRevisions = [];
    private bool _synchronizingBrowserTree;
    /// <summary>The node most recently targeted by a passive (non-interactive) tree reveal, consumed by <see cref="BrowserFolderTree_SelectedItemChanged"/> the first time a matching event arrives. See that method's doc comment.</summary>
    private BrowserTreeNode? _browserTreeRevealedNode;
    private BrowserTreeNode? _browserFolderPointerTarget;
    private readonly WorkspaceStateService _workspaceState;
    private readonly DispatcherTimer _workspaceSaveTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private WindowState _lastNonMinimizedWindowState = WindowState.Normal;
    private readonly DispatcherTimer _browserSearchDebounceTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private readonly DispatcherTimer _browserMetadataResortTimer = new() { Interval = TimeSpan.FromMilliseconds(800) };
    private bool _synchronizingBrowserQuery;
    private string? _browserQueryScope;
    private BrowserQuery? _lockedBrowserQuery;
    private IDerivedWorkBatch? _activeBrowserDerivedWorkBatch;
    private readonly Dictionary<MediaTypeCategory, ToggleButton> _browserQuickFilterButtons = [];
    private BrowserThumbnailSize _browserThumbnailSize = BrowserGridLayout.DefaultThumbnailSize;
    private bool _synchronizingBrowserThumbnailSize;
    // #124: identity of the candidate media set currently populating the grid — folder + scope mode. Unlike
    // _browserQueryScope (folder only, used to decide whether BrowserQuery resets), a change here always
    // clears Browser selection, including toggling Include Subfolders while the same folder stays open.
    private string? _browserScopeIdentity;
    private BrowserCollectionScope? _activeCollectionScope;
    private CancellationTokenSource? _collectionScopeCts;
    private bool _synchronizingCollectionTree;
    private BrowserCollectionNode? _browserCollectionTreeRevealedNode;
    private System.Windows.Point _collectionDragStart;
    private BrowserCollectionNode? _collectionDragNode;
    private System.Windows.Point _browserAssetDragStart;
    private BrowserGridTile? _browserAssetDragTile;
    private BrowserGridTile? _browserAssetPendingSingleSelection;
    private BrowserCollectionNode? _browserCollectionPointerTarget;
    private bool _browserCollectionKeyboardSelectionPending;
    private readonly BrowserCollectionDragSession _collectionDragSession = new();
    private LowLevelMouseWheelHook? _collectionDragWheelHook;
    private BrowserCollectionNode? _browserCollectionActionNode;
    private readonly BrowserScopeSelection _browserScopeSelection = new();
    private CollectionDropAdorner? _collectionDropAdorner;
    private System.Windows.Documents.AdornerLayer? _collectionDropAdornerLayer;
    private readonly BrowserCollectionDragHover _collectionDragHover = new();
    private readonly DispatcherTimer _collectionDragHoverTimer = new() { Interval = BrowserCollectionDragHover.Dwell };
    private bool _synchronizingBrowserScopeMode;
    private readonly DispatcherTimer _browserRecursiveRefreshDebounceTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    /// <summary>Denominator as of the last recursive-progress report, so <see cref="ApplyRecursiveScopeLoadingProgress"/> can tell whether discovery is still actively growing. Reset alongside everything else in <see cref="ResetBrowserLoadingProgress"/>.</summary>
    private int _browserRecursiveProgressLastDiscovered;
    // #124 (revised): every stored Catalog recursive root, as of the most recent navigation — see
    // BrowserFolderState.RecursiveRoots. Cached here so Locations-tree icon sync never needs its own Catalog
    // round-trip; refreshed unconditionally in ApplyBrowserState alongside everything else that state drives.
    private IReadOnlyList<BrowserRecursiveRoot> _browserRecursiveRoots = [];
    // #110: which content the Browser's central area currently shows. PlayerViewerHost is created lazily
    // (first open) and reused for the rest of the app's lifetime rather than recreated per-asset — see
    // EnsurePlayerViewerHost. _browserGridScrollViewer is resolved once from BrowserGridRows' own templated
    // ScrollViewer (it lives inside a ControlTemplate, so it has no x:Name field of its own) and cached.
    private BrowserPresentationMode _browserPresentation = BrowserPresentationMode.Grid;
    private PlayerViewerHost? _playerViewerHost;
    private ScrollViewer? _browserGridScrollViewer;
    private double _browserGridScrollOffset;
    private CapabilityInvocation? _browserEncodingInvocation;
    private CancellationTokenSource? _browserEncodingHandoffCts;
    private long _browserColorSelectionRevision;
    private bool _updatingBrowserColorSelectors;
    private bool _lutInitializationCompleted;
    private long _browserVisualIdentityAuditGeneration = -1;

    internal MainWindow(LightflowStorageCoordinator storage, StorageStartupStatus storageStartupStatus,
        string? storageDiagnostic)
    {
        _storage = storage;
        _storageStartupStatus = storageStartupStatus;
        _storageDiagnostic = storageDiagnostic;
        _browserNavigation = new BrowserNavigationSession(storage.MediaRoots, storage.BrowserLocations,
            storage.MediaDiscovery, storage.MediaFolders, storage.BrowserRecursiveRoots, storage.RecursiveMediaDiscovery);
        _browserNavigation.EffectiveScopeDetermined += BrowserNavigation_EffectiveScopeDetermined;
        _browserNavigation.RecursiveScopeProgressChanged += BrowserNavigation_RecursiveScopeProgressChanged;
        _browserCollectionScopes = new(storage.Collections, storage.MediaAssets, storage.MediaRoots, storage.MediaTypes, () => storage.DerivedWork);
        _trimHistory = new TrimHistoryStore(storage.Locations.TrimHistoryPath);
        _jobHistory = new JobHistoryStore(storage.Locations.JobHistoryPath);
        _jobRuntimeStore = new JobRuntimeStore<EncodingJobOptions, EncodingItemResult>(storage.Locations.JobRuntimePath);
        _jobsRuntime = new ApplicationJobsRuntime<EncodingJobOptions, EncodingItemResult>(
            (plan, runtime, paused) => _jobRuntimeStore.Save(plan, runtime, paused));
        _jobsRuntime.Changed += JobsRuntime_Changed;
        var modernQueuePath = Path.Combine(Path.GetDirectoryName(storage.Locations.JobRuntimePath)!, "export-jobs.v2.json");
        _exportScheduler = new GlobalExportScheduler(storage.Settings.MaxSimultaneousExports, () =>
        {
            if (_ffmpeg is null || _ffprobe is null) throw new InvalidOperationException("FFmpeg and FFprobe are required for Export.");
            var executor = new EncodingJobExecutor(_ffmpeg, _ffprobe, _storage.Locations.OutputIdentityDirectory,
                diagnostic: line => _activityLogFile.TryAppend(line));
            return new ExportExecutorLease(executor.ExecuteAsync, executor.TerminateAll);
        }, new ExportQueueStore(modernQueuePath), definition => EncodingJobRecovery.Revalidate(
            definition.PlanItem, definition.Recipe, _storage.Locations.OutputIdentityDirectory), persistMaximum: maximum =>
        {
            _settings = _settings with { MaxSimultaneousExports = maximum };
            try { _storage.SaveSettings(_settings); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            { _activityLogFile.TryAppend($"[App] Could not save global Export concurrency: {exception.Message}"); }
        }, isQueuePaused: storage.Settings.IsExportQueuePaused, persistQueuePaused: paused =>
        {
            _settings = _settings with { IsExportQueuePaused = paused };
            try { _storage.SaveSettings(_settings); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            { _activityLogFile.TryAppend($"[App] Could not save the Export queue pause policy: {exception.Message}"); }
        });
        _exportCoordinator = new ExportJobCoordinator(_exportScheduler, _jobHistory);
        _exportCoordinator.Completed += _ => Dispatcher.BeginInvoke(RefreshHistory);
        _exportScheduler.Changed += ExportScheduler_Changed;
        _exportScheduler.SubmissionAccepted += _ => Dispatcher.BeginInvoke(() =>
        {
            OpenJobsDrawer();
        });
        _workspaceState = new WorkspaceStateService(storage.Locations.WorkspaceStatePath);
        InitializeComponent();
        InitializeBrowserQuickFilterButtons();
        SyncBrowserStatusBarVisibility();
        ApplyRestoredWorkspaceLayout();
        _storage.ThumbnailActivity.Changed += (_, change) => Dispatcher.BeginInvoke(() =>
            _browserGrid.ApplyThumbnailGenerating(change.AssetId, change.IsGenerating));
        if (_workspaceState.Current.Browser is { } savedBrowserLocation) ShowBrowserRestoringState(savedBrowserLocation);
        _batchFolderRefreshTimer.Tick += (_, _) =>
        {
            _batchFolderRefreshTimer.Stop();
            RefreshBatchFiles();
        };
        _workspaceSaveTimer.Tick += (_, _) =>
        {
            _workspaceSaveTimer.Stop();
            _workspaceState.Save();
        };
        _collectionDragHoverTimer.Tick += (_, _) => ExpandHoveredCollectionSet();
        _browserSearchDebounceTimer.Tick += (_, _) =>
        {
            _browserSearchDebounceTimer.Stop();
            ApplyBrowserQuery(query => query with { SearchText = BrowserSearchBox.Text });
        };
        _browserMetadataResortTimer.Tick += (_, _) =>
        {
            _browserMetadataResortTimer.Stop();
            _browserGrid.ReapplyQuery();
            UpdateBrowserStatusText();
        };
        _browserRecursiveRefreshDebounceTimer.Tick += (_, _) =>
        {
            _browserRecursiveRefreshDebounceTimer.Stop();
            // #124: a relevant monitoring event arriving while a load is already in flight — most commonly the
            // recursive scan's own folder reads, which some drives/watchers (particularly removable/network
            // media) report back as spurious "changed" notifications — must never restart it from scratch. The
            // in-flight load already performs a full, current enumerate+reconcile pass over the same scope and
            // will reflect this change once it completes; starting a second one here would cancel it mid-walk
            // (Begin() latest-wins) and silently reset FoldersVisited to zero, making one continuous recursive
            // scan look like it keeps restarting. Monitoring is a hint only — explicit Refresh stays
            // authoritative regardless — so it is safe to simply drop a hint that arrives mid-load rather than
            // rescheduling it.
            if (_activeCollectionScope is not null) return;
            if (BrowserLoadingOverlay.Visibility == Visibility.Visible) return;
            _ = RunBrowserNavigationAsync(() => _browserNavigation.RefreshAsync());
        };
        SourceInitialized += (_, _) => WindowAppearance.EnableDarkTitleBar(this);
        StateChanged += (_, _) =>
        {
            if (WindowState != WindowState.Minimized) _lastNonMinimizedWindowState = WindowState;
        };
        _commandLineFolder = Environment.GetCommandLineArgs().Skip(1).FirstOrDefault(Directory.Exists);
        _jobsWorkspaceSmokeTest = Environment.GetCommandLineArgs().Contains("--jobs-workspace-smoke-test", StringComparer.OrdinalIgnoreCase);
        Loaded += async (_, _) =>
        {
            try
            {
                AboutVersionText.Text = $"Version {AppVersion.Display}  •  Built for the creative workflow";
                _settings = _storage.Settings;
                _state = AppStateStore.Load(_storage.Locations.StatePath);
                PopulateSettingsControls(_settings);
                ApplySettingsToBatch(_settings);
                ApplyStateToBatch(_state);
                if (_commandLineFolder is not null)
                {
                    InputFolder.Text = _commandLineFolder;
                }
                BatchFileList.ItemsSource = _batchFiles;
                HistoryList.ItemsSource = _historyRecords;
                JobsDrawerList.ItemsSource = _jobsDrawerCards;
                MediaRootsList.ItemsSource = _mediaRoots;
                BrowserFolderTree.ItemsSource = _browserTree.Roots;
                BrowserCollectionTree.ItemsSource = _browserCollectionTree.Roots;
                BrowserGridRows.ItemsSource = _browserGrid.Rows;
                if (_storage.MediaMonitoring is { } monitoring) monitoring.FolderRefreshed += BrowserMonitoring_FolderRefreshed;

                // Browser is the default, immediately visible workspace: get its Locations storage entries
                // (needed so an offline saved root already has a tree node to show its honest state against)
                // and kick off restoration before any Encoding/History/Settings-only work below, none of
                // which the user is looking at yet. Measured on real hardware: this alone cut the delay
                // before restoration starts from ~1.1s to ~0.16s. Restoration itself proceeds independently.
                await RefreshBrowserStorageAsync();
                await RefreshCollectionsAsync();
                if (_workspaceState.Current.Layout?.BrowserCollectionId is { } collectionId)
                    _ = LoadCollectionScopeAsync(collectionId);
                else
                    _ = RestoreBrowserLocationAsync(_workspaceState.Current.Browser);

                RefreshCatalogBackups();
                RefreshHistory();
                if (_jobsWorkspaceSmokeTest)
                    MainTabs.SelectedIndex = ShellDestinationSelection.Index(ShellDestination.Jobs);
                LocateTools();
                _exportScheduler.MaxSimultaneousExports = _settings.MaxSimultaneousExports;
                ApplyJobsPresentation(_exportScheduler.Jobs);
                ReportRecoveredJobs();
                await RefreshDependencyHealthAsync();
                RefreshBatchFiles();
                _ = InitializeLutsAsync();
                RefreshLuts();
                await RefreshMediaRootsAsync();
                await RefreshPreviewUsageAsync();
                if (_storageStartupStatus != StorageStartupStatus.Ready)
                    SettingsMessage.Text = $"Catalog unavailable: {_storageDiagnostic}";
                else if (!_storage.PreviewAvailable)
                    SettingsMessage.Text = _storage.PreviewDiagnostic;
                else if (_storage.RecoveryDiagnostic is not null)
                    SettingsMessage.Text = _storage.RecoveryDiagnostic;
            }
            catch (Exception exception)
            {
                _activityLogFile.TryAppend($"[App] Main window initialization failed: {exception}");
                BrowserEmptyTitle.Text = "Storage locations could not be loaded";
                BrowserEmptyMessage.Text = $"Lightflow remains available. Details were written to {_activityLogFile.Path}.";
                BrowserEmptyState.Visibility = Visibility.Visible;
            }
        };
        Closed += (_, _) =>
        {
            _exportCoordinator.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _activeJobExecutor?.TerminateAll();
            _browserEncodingHandoffCts?.Cancel();
            _browserEncodingHandoffCts = null;
            _collectionScopeCts?.Cancel();
            _collectionScopeCts?.Dispose();
            if (_storage.MediaMonitoring is { } monitoring) monitoring.FolderRefreshed -= BrowserMonitoring_FolderRefreshed;
            _browserNavigation.RecursiveScopeProgressChanged -= BrowserNavigation_RecursiveScopeProgressChanged;
            _browserNavigation.EffectiveScopeDetermined -= BrowserNavigation_EffectiveScopeDetermined;
            _workspaceSaveTimer.Stop();
            _browserSearchDebounceTimer.Stop();
            _browserMetadataResortTimer.Stop();
            _browserRecursiveRefreshDebounceTimer.Stop();
            _collectionDragHoverTimer.Stop();
            _browserNavigation.Dispose();
        };
    }

    /// <summary>
    /// Applies previously saved window bounds/maximized state and Locations-pane width before the window is
    /// shown. A no-op on first launch (no saved state), leaving MainWindow.xaml's declared defaults in effect.
    /// </summary>
    private void ApplyRestoredWorkspaceLayout()
    {
        if (_workspaceState.Current.Window is { } window)
        {
            var workAreas = Forms.Screen.AllScreens.Select(screen => new ScreenWorkArea(screen.WorkingArea.Left,
                screen.WorkingArea.Top, screen.WorkingArea.Width, screen.WorkingArea.Height)).ToArray();
            var clamped = WorkspaceWindowPlacement.Clamp(window, workAreas, MinWidth, MinHeight);
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = clamped.Left;
            Top = clamped.Top;
            Width = clamped.Width;
            Height = clamped.Height;
            if (window.IsMaximized) WindowState = WindowState.Maximized;
            // StateChanged does not fire for this initial programmatic assignment, so seed the tracked
            // last-non-minimized state directly or a maximized restore would be read back as Normal at close.
            _lastNonMinimizedWindowState = WindowState;
        }

        if (_workspaceState.Current.Layout?.BrowserLocationsPaneWidth is { } paneWidth)
        {
            _browserLocationsPreferredWidth = paneWidth;
            BrowserNavigationColumn.Width = new GridLength(paneWidth);
        }
        if (_workspaceState.Current.Layout?.JobsDrawerWidth is { } drawerWidth)
            _jobsDrawerWidth = drawerWidth;
        if (_workspaceState.Current.Layout?.FullJobsListPaneWidth is { } jobsListWidth)
            FullJobsListColumn.Width = new GridLength(jobsListWidth);

        // Unconditional (not just inside an `if`): this is also what seeds Resources["BrowserTileWidth"]/
        // ["BrowserTileThumbnailHeight"] for the very first frame, whether or not a size was ever saved.
        var savedThumbnailSize = _workspaceState.Current.Layout?.BrowserThumbnailSizeLevel is { } level
            ? BrowserGridLayout.ThumbnailSizeFromLevel(level)
            : BrowserGridLayout.DefaultThumbnailSize;
        ApplyBrowserThumbnailSize(savedThumbnailSize);
        ApplyBrowserViewMode(_workspaceState.GetBrowserViewMode(), persist: false);
        var layout = _workspaceState.Current.Layout;
        BrowserLocationsSectionToggle.IsChecked = layout?.BrowserLocationsSectionExpanded ?? true;
        BrowserCollectionsSectionToggle.IsChecked = layout?.BrowserCollectionsSectionExpanded ?? true;
        ApplyBrowserScopeSectionVisibility();
    }

    /// <summary>
    /// Reflects the remembered Browser destination as early as safely possible: called before
    /// <c>Loaded</c> even fires (right after <see cref="ApplyRestoredWorkspaceLayout"/>), using only the
    /// synchronously-available saved location, so it is part of the window's first rendered frame rather
    /// than appearing after a delay behind the default "Choose a storage location" placeholder.
    /// </summary>
    private void ShowBrowserRestoringState(WorkspaceBrowserLocationState saved)
    {
        var label = BrowserLocationRestoration.DescribeSavedLocation(saved);
        if (!string.IsNullOrWhiteSpace(saved.LastResolvedAbsolutePath)) BrowserCurrentPath.Text = saved.LastResolvedAbsolutePath;
        ShowBrowserLoadingState(label is null ? "Restoring your last location…" : $"Loading {label}…");
    }

    /// <summary>
    /// The single authoritative entry point into the Browser center presentation's Loading state — the one
    /// place a new navigation generation retires whatever the previous generation was showing. The Browser
    /// center presentation (<see cref="BrowserEmptyState"/>/<see cref="BrowserGridRows"/>/<see cref="BrowserLoadingOverlay"/>)
    /// represents exactly one authoritative state at a time; a completed scope's content, empty, or failure
    /// presentation must never remain visible once a new navigation begins — <see cref="BrowserLoadingOverlay"/>'s
    /// own background is deliberately semi-transparent (so the truthful progress bar it hosts stays legible
    /// against the shell), which previously let a previous folder's media tiles show faintly through it rather
    /// than actually disappearing; hiding the grid outright here, not merely painting over it, is what "the
    /// prior scope stops being presented" actually requires. <see cref="BrowserGridModel"/>'s own tile data is
    /// untouched — only its visual presentation is collapsed — so a same-folder refresh or a failure that falls
    /// back to the last-loaded content never has to re-fetch or repopulate anything. Only
    /// <see cref="ApplyBrowserState"/>/<see cref="ApplyBrowserNavigationFailure"/> — themselves already gated on
    /// the current <see cref="_browserUiGeneration"/> — are allowed to show <see cref="BrowserEmptyState"/> or
    /// <see cref="BrowserGridRows"/> again, once this same generation's loading actually finishes. Called at
    /// every point a new loading sequence starts (an ordinary navigation via <see cref="RunBrowserNavigationAsync"/>,
    /// and workspace restoration via <see cref="ShowBrowserRestoringState"/>), so both paths retire the
    /// previous presentation identically.
    /// </summary>
    private void ShowBrowserLoadingState(string label)
    {
        BrowserLoadingText.Text = label;
        BrowserEmptyState.Visibility = Visibility.Collapsed;
        BrowserGridRows.Visibility = Visibility.Collapsed;
        ResetBrowserLoadingProgress();
        BrowserLoadingOverlay.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// #124: begins every loading sequence indeterminate — "begin indeterminate, transition to determinate
    /// only once the traversal naturally knows enough" — regardless of whether this turns out to be a direct
    /// or recursive load, or an ordinary navigation vs. workspace restoration. Never called mid-load; only at
    /// the point a new loading sequence starts, before anything about its actual progress is known.
    /// </summary>
    private void ResetBrowserLoadingProgress()
    {
        BrowserLoadingProgressBar.IsIndeterminate = true;
        BrowserLoadingProgressBar.Value = 0;
        _browserRecursiveProgressLastDiscovered = 0;
    }

    /// <summary>Restores the default, honest "no location open" Browser state, e.g. when restoration resolves nothing to show.</summary>
    private void ShowDefaultBrowserEmptyState()
    {
        BrowserCurrentPath.Text = "";
        BrowserEmptyTitle.Text = "Choose a storage location";
        BrowserEmptyMessage.Text = "Select a drive, mapped location, or managed library to browse its folders and supported media.";
        BrowserEmptyState.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// Captures the window's restored bounds, maximized state, Locations-pane width, and #125's Browser
    /// thumbnail-size level, then flushes the latest workspace state to disk. Called on normal shutdown; a
    /// debounced save also covers Browser location changes mid-session for crash resilience. The thumbnail
    /// size is read from <see cref="_browserThumbnailSize"/> (kept current by every
    /// <see cref="ApplyBrowserThumbnailSize"/> call) rather than persisted live on every slider tick,
    /// mirroring how <see cref="BrowserNavigationColumn"/>'s width is only captured here too.
    /// </summary>
    private void SaveWorkspaceState()
    {
        _workspaceSaveTimer.Stop();
        var bounds = RestoreBounds;
        if (bounds.Width > 0 && bounds.Height > 0)
            _workspaceState.SetWindow(new WorkspaceWindowState
            {
                Width = bounds.Width,
                Height = bounds.Height,
                Left = bounds.Left,
                Top = bounds.Top,
                IsMaximized = _lastNonMinimizedWindowState == WindowState.Maximized
            });
        _workspaceState.SetBrowserLocationsPaneWidth(_browserLocationsPreferredWidth);
        if (JobsDrawer.Visibility == Visibility.Visible) _jobsDrawerWidth = JobsDrawerColumn.ActualWidth;
        _workspaceState.SetJobsDrawerWidth(_jobsDrawerWidth);
        _workspaceState.SetFullJobsListPaneWidth(FullJobsListColumn.ActualWidth);
        _workspaceState.SetBrowserThumbnailSizeLevel((int)_browserThumbnailSize);
        _workspaceState.SetBrowserCollectionState(_activeCollectionScope?.Collection.CollectionId,
            _browserCollectionTree.ExpandedSetIds());
        _workspaceState.SetBrowserScopeSectionState(BrowserLocationsSectionToggle.IsChecked == true,
            BrowserCollectionsSectionToggle.IsChecked == true);
        _workspaceState.Save();
    }

    /// <summary>
    /// Restores the last Browser location saved by <see cref="ApplyBrowserState"/>, reusing the same
    /// #98-#108 navigation session every manual Locations interaction uses (see
    /// <see cref="BrowserLocationRestoration"/>). Restoration failures are logged and otherwise silent:
    /// the Browser simply remains in its default empty state, and startup is never blocked.
    /// </summary>
    private async Task RestoreBrowserLocationAsync(WorkspaceBrowserLocationState? saved)
    {
        if (saved is null) return;
        var generation = ++_browserUiGeneration;
        // Already showing from ShowBrowserRestoringState (called before Loaded even fires); re-asserted
        // here so this method stays correct regardless of what state the canvas was left in beforehand.
        BrowserLoadingOverlay.Visibility = Visibility.Visible;
        try
        {
            // #124 (revised): recursive mode is no longer a session field to pre-set before restoring —
            // it is derived live from the Catalog's stored recursive roots against whatever folder actually
            // loads, exactly like an interactive navigation. Restoration therefore needs no scope-mode step
            // of its own; it simply drives the same navigation path every other Locations interaction uses.
            var result = await BrowserLocationRestoration.RestoreAsync(_browserNavigation, _storage.MediaRoots, saved)
                .ConfigureAwait(true);
            if (generation != _browserUiGeneration) return;
            if (!ApplyBrowserSuccessState(result.State, generation))
            {
                if (result.State is { } failure)
                {
                    ApplyBrowserNavigationFailure(failure);
                    // Unlike an ordinary failed navigation, restoration never had a genuinely open previous
                    // folder to preserve here: the address bar was only seeded early for cosmetic effect.
                    BrowserCurrentPath.Text = "";
                }
                else ShowDefaultBrowserEmptyState();
            }
        }
        catch (OperationCanceledException)
        {
            if (generation == _browserUiGeneration) ShowDefaultBrowserEmptyState();
        }
        catch (Exception exception) when (exception is InvalidOperationException or MachineIdentityException or
            ArgumentException or IOException or UnauthorizedAccessException)
        {
            _activityLogFile.TryAppend($"[Workspace] Browser location restoration failed: {exception.Message}");
            if (generation == _browserUiGeneration) ShowDefaultBrowserEmptyState();
        }
        finally
        {
            if (generation == _browserUiGeneration) BrowserLoadingOverlay.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    /// #101's monitoring hint reaches an open Browser view here. Direct mode preserves the exact-folder
    /// behavior #108 established. #124 recursive mode additionally treats any descendant folder inside the
    /// active base scope as relevant (via <see cref="BrowserScope.IsWithinFolderScope"/>) — unrelated
    /// sibling/ancestor/other-root events are still ignored. Because a burst of filesystem activity across
    /// many descendant folders can raise several <see cref="IMediaRootMonitoringService.FolderRefreshed"/>
    /// events in quick succession, and each one would otherwise re-run the entire recursive scope, relevant
    /// recursive events are coalesced through <see cref="_browserRecursiveRefreshDebounceTimer"/> into a
    /// single authoritative refresh rather than one per event — a refresh-storm guard on top of #101's own
    /// per-folder debounce, using the same debounce convention. Either way this is a hint only: explicit
    /// Refresh remains authoritative regardless.
    /// </summary>
    private void BrowserMonitoring_FolderRefreshed(object? sender, MediaFolderEnumerationRequest request) =>
        Dispatcher.BeginInvoke(() =>
        {
            if (_activeCollectionScope is not null) return;
            var location = _browserNavigation.State.Location;
            if (location is null || location.RootId != request.RootId) return;
            if (_browserNavigation.State.Mode == BrowserScopeMode.IncludeSubfolders)
            {
                if (!BrowserScope.IsWithinFolderScope(request.RelativeFolder, location.RelativeFolder)) return;
                _browserRecursiveRefreshDebounceTimer.Stop();
                _browserRecursiveRefreshDebounceTimer.Start();
                return;
            }
            if (!string.Equals(location.RelativeFolder ?? "", request.RelativeFolder ?? "", StringComparison.OrdinalIgnoreCase))
                return;
            _ = RunBrowserNavigationAsync(() => _browserNavigation.RefreshAsync());
        });

    /// <summary>
    /// Selecting a row is the one action that changes Browser scope/contents. <see cref="_browserTreeRevealedNode"/>
    /// guards against a WPF-internal hazard that timing alone cannot reliably close: <c>TreeView.SelectedItemChanged</c>
    /// fires whenever <c>TreeView.SelectedItem</c> changes for *any* reason, including a purely passive,
    /// programmatic <see cref="BrowserTreeNode.IsSelected"/> push (see <see cref="RequestBrowserTreeSelection(BrowserLocation?)"/>)
    /// — and for a node whose container WPF has not yet realized (routine for a folder never visited before,
    /// especially several levels deep — exactly the startup-restoration case), that event is deferred to a
    /// later, unpredictable layout pass rather than firing synchronously, so no fixed dispatcher-priority delay
    /// can be relied on to still be "inside" a synchronization window by the time it lands. Comparing the
    /// event's node against the SPECIFIC node the most recent passive reveal targeted — set only by
    /// <see cref="RequestBrowserTreeSelection(BrowserLocation?)"/>/<see cref="RequestBrowserTreeSelection(string)"/>,
    /// the two "sync the tree to a navigation already happening elsewhere" call sites, never by an interactive
    /// click — closes the gap regardless of how long WPF defers it: that reveal's own navigation (if any) is
    /// already being driven independently through <see cref="BrowserNavigationSession"/>, so this event must
    /// never start a second, competing one that would cancel it via the ordinary latest-wins path. The field is
    /// consumed (cleared) the first time it matches, so a later, genuinely new interactive selection of that
    /// same row is never suppressed.
    /// </summary>
    private async void BrowserFolderTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (_synchronizingBrowserTree || e.NewValue is not BrowserTreeNode { IsPlaceholder: false } node) return;
        var interactive = ReferenceEquals(_browserFolderPointerTarget, node) || BrowserFolderTree.IsKeyboardFocusWithin;
        var activate = _browserScopeSelection.ShouldActivateFolder(node, _browserFolderPointerTarget,
            BrowserFolderTree.IsKeyboardFocusWithin, _browserTreeRevealedNode);
        _browserFolderPointerTarget = null;
        if (!activate)
        {
            if (ReferenceEquals(node, _browserTreeRevealedNode)) _browserTreeRevealedNode = null;
            if (_browserScopeSelection.Active == BrowserScopeSelectionKind.Collection)
            {
                _synchronizingBrowserTree = true;
                try
                {
                    _browserTree.RestoreSelection(null);
                    ClearRealizedTreeSelection(BrowserFolderTree);
                }
                finally { _synchronizingBrowserTree = false; }
            }
            return;
        }
        if (interactive && ReferenceEquals(node, _browserTreeRevealedNode)) _browserTreeRevealedNode = null;
        ActivateFolderScopeSelection();
        _collectionScopeCts?.Cancel();
        RequestBrowserTreeSelection(node);
        if (node.Storage is { Kind: BrowserStorageKind.ManagedRoot, RootId: { } rootId })
            await RunBrowserNavigationAsync(() => _browserNavigation.NavigateToRootAsync(rootId));
        else if (!string.IsNullOrWhiteSpace(node.AbsolutePath))
            await RunBrowserNavigationAsync(() => _browserNavigation.NavigateToPathAsync(node.AbsolutePath));
    }

    /// <summary>
    /// Expanding a folder — via the disclosure chevron, a double-click, or the keyboard — materializes its real
    /// children (siblings) for lazy-loading, exactly like <see cref="RevealBrowserTreeAncestorsAsync"/> already
    /// does for ancestors, but never selects it or navigates into it: hierarchy exploration and
    /// selection/navigation are deliberately separate actions, matching a conventional tree control. Only a row
    /// click or keyboard selection (<see cref="BrowserFolderTree_SelectedItemChanged"/>) changes Browser scope/
    /// contents. Previously this called <c>RunBrowserNavigationAsync</c> directly — reusing "navigate here" as
    /// the mechanism for fetching a real listing — which also selected the row and replaced the grid/address
    /// bar on every expand, and (since <see cref="BrowserTreeModel.EnsurePathChain"/> expands every ancestor
    /// while revealing a deep restored/direct-path location) could race a startup restoration's own in-flight
    /// navigation for a completely different, shallower folder — the root cause of a startup fallback and of a
    /// concurrent recursive scan losing its progress and silently restarting. Requires the node to already
    /// carry a <see cref="BrowserTreeNode.RootId"/>: a bare, not-yet-anchored Volume row (a raw drive letter
    /// never yet navigated into) has none, so materialization cannot proceed until the row is clicked once to
    /// establish its Catalog anchor — a narrow, honest trade-off (never silently mis-navigating) rather than
    /// duplicating filesystem-listing logic in WPF just to materialize an unanchored drive's children without a
    /// Catalog root. Every path that cannot materialize real children (missing anchor, a root that no longer
    /// resolves to a physical path, an enumeration failure or exception) collapses the node back via
    /// <see cref="CollapseUnmaterializableNode"/> rather than returning early and leaving its "Loading…"
    /// placeholder child stuck showing forever with no further feedback — an honest closed/re-expandable
    /// chevron, not a false promise of in-progress work.
    /// </summary>
    private async void BrowserFolderTreeItem_Expanded(object sender, RoutedEventArgs e)
    {
        if (_synchronizingBrowserTree || (sender as FrameworkElement)?.DataContext is not BrowserTreeNode node ||
            node.IsPlaceholder || !node.Children.Any(child => child.IsPlaceholder))
            return;
        if (node.RootId is not { } rootId || node.RelativeFolder is not { } relativeFolder)
        {
            CollapseUnmaterializableNode(node);
            return;
        }

        var root = await _storage.MediaRoots.GetAsync(rootId).ConfigureAwait(true);
        if (root?.PhysicalPath is not { } rootPath)
        {
            CollapseUnmaterializableNode(node);
            return;
        }

        MediaFolderEnumerationResult listing;
        try
        {
            listing = await _storage.MediaFolders.EnumerateAsync(new(rootId, EmptyToNull(relativeFolder))).ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            CollapseUnmaterializableNode(node);
            return;
        }
        if (!listing.Succeeded)
        {
            CollapseUnmaterializableNode(node);
            return;
        }

        _synchronizingBrowserTree = true;
        try { _browserTree.ApplyDirectoryListing(node, rootPath, listing.Entries); }
        finally { _synchronizingBrowserTree = false; }
        SyncBrowserTreeRecursiveIcons();
    }

    /// <summary>
    /// Reverts a node's disclosure state to closed (still showing its lazy-load placeholder, untouched) after
    /// an expand attempt that could not materialize real children — never leaves the "Loading…" placeholder
    /// visibly stuck with no further feedback. The same reentrancy guard every other programmatic tree
    /// mutation uses keeps this from re-triggering <see cref="BrowserFolderTreeItem_Expanded"/> itself. A
    /// later, genuine expand attempt (e.g. after the row's own click has established a Catalog anchor, or once
    /// a transient enumeration failure has cleared) runs this handler fresh and can still succeed.
    /// </summary>
    private void CollapseUnmaterializableNode(BrowserTreeNode node)
    {
        _synchronizingBrowserTree = true;
        try { node.IsExpanded = false; }
        finally { _synchronizingBrowserTree = false; }
    }

    private static string? EmptyToNull(string value) => value.Length == 0 ? null : value;

    private void RequestBrowserTreeSelection(BrowserTreeNode node)
    {
        _synchronizingBrowserTree = true;
        try { _browserTree.RequestSelection(node); }
        finally { _synchronizingBrowserTree = false; }
    }

    private void RequestBrowserTreeSelection(BrowserLocation? location)
    {
        if (location is null) return;
        _synchronizingBrowserTree = true;
        BrowserTreeNode? node;
        try { node = _browserTree.RequestSelection(location); }
        catch { _synchronizingBrowserTree = false; throw; }
        if (node is null) { _synchronizingBrowserTree = false; return; }
        _browserTreeRevealedNode = node;
        BringBrowserTreeNodeIntoView(node);
        DeferBrowserTreeSynchronizingReset();
    }

    private void RequestBrowserTreeSelection(string absolutePath)
    {
        _synchronizingBrowserTree = true;
        BrowserTreeNode? node;
        try { node = _browserTree.RequestSelection(absolutePath); }
        catch { _synchronizingBrowserTree = false; throw; }
        if (node is null) { _synchronizingBrowserTree = false; return; }
        _browserTreeRevealedNode = node;
        BringBrowserTreeNodeIntoView(node);
        DeferBrowserTreeSynchronizingReset();
    }

    /// <summary>
    /// For a node whose container WPF has not yet realized — routine for a deep path visited for the first
    /// time, exactly the startup-restoration case — setting <see cref="BrowserTreeNode.IsSelected"/>/
    /// <see cref="BrowserTreeNode.IsExpanded"/> here does not synchronously produce the corresponding
    /// <see cref="TreeViewItem.IsSelected"/>/<see cref="TreeViewItem.Expanded"/> WPF-side effects the
    /// <c>TreeView.ItemContainerStyle</c> bindings drive: those only apply once WPF actually generates a
    /// container for the node, deferred to a later layout pass. If <see cref="_synchronizingBrowserTree"/> had
    /// already been reset to false by then (as it used to be, synchronously, right after
    /// <see cref="BrowserTreeModel.RequestSelection(BrowserLocation)"/> returned), that deferred
    /// <c>SelectedItemChanged</c>/<c>Expanded</c> would reach <see cref="BrowserFolderTree_SelectedItemChanged"/>/
    /// <see cref="BrowserFolderTreeItem_Expanded"/> unguarded and be mistaken for a real interactive action,
    /// re-triggering navigation and bumping <see cref="_browserUiGeneration"/> — which then made the actually-
    /// in-flight navigation's own generation check fail, silently skipping <see cref="ApplyBrowserSuccessState"/>
    /// for it entirely. Deferring the reset to the same <see cref="DispatcherPriority.Loaded"/> pass
    /// <see cref="BringBrowserTreeNodeIntoView"/> already waits for (by which point container generation for a
    /// freshly revealed node has reliably caught up) keeps the guard open across that gap instead.
    /// </summary>
    private void DeferBrowserTreeSynchronizingReset() =>
        _ = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => _synchronizingBrowserTree = false);

    private void BringBrowserTreeNodeIntoView(BrowserTreeNode node)
    {
        const double disclosureAndIconWidth = 44;
        _ = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            var container = FindBrowserTreeItem(BrowserFolderTree, node);
            if (container is null) return;
            // Programmatic selection only sets IsSelected (the background fill); the focus-ring outline is a
            // separate IsKeyboardFocused-driven trigger for accessibility. Giving the revealed container real
            // keyboard focus here makes direct-path/programmatic navigation look and behave the same as a
            // manual click, matching Explorer-like navigation conventions.
            container.Focus();
            var rowPosition = container.TranslatePoint(new System.Windows.Point(0, 0), BrowserFolderTree);
            var verticalOffset = BrowserTreeScroll.RevealVerticalOffset(BrowserFolderScrollViewer.VerticalOffset,
                BrowserFolderScrollViewer.ViewportHeight, rowPosition.Y, container.ActualHeight);
            var horizontalOffset = BrowserTreeScroll.RevealHorizontalOffset(BrowserFolderScrollViewer.HorizontalOffset,
                BrowserFolderScrollViewer.ViewportWidth, rowPosition.X, disclosureAndIconWidth);
            BrowserFolderScrollViewer.ScrollToVerticalOffset(
                Math.Min(verticalOffset, BrowserFolderScrollViewer.ScrollableHeight));
            BrowserFolderScrollViewer.ScrollToHorizontalOffset(
                Math.Min(horizontalOffset, BrowserFolderScrollViewer.ScrollableWidth));
        });
    }

    /// <summary>
    /// Materializes every ancestor between the Locations root and <paramref name="location"/> that direct-path
    /// or other programmatic navigation has not yet visited, so the tree shows real sibling folders along the
    /// whole path instead of the synthetic single-child chain <see cref="BrowserTreeModel"/> creates just to
    /// preserve node identity while the real listing is unknown. Ancestors already materialized by ordinary
    /// click-driven expansion are skipped, so this is a no-op in the common case.
    /// </summary>
    /// <remarks>
    /// #124: <see cref="SyncBrowserTreeRecursiveIcons"/> used to be called only once, after the whole loop —
    /// exactly the case a generation change mid-loop (a newer navigation superseding this one, most often the
    /// very first visit to a subtree, e.g. straight out of startup restoration, where every ancestor genuinely
    /// needs a real enumeration and this loop is not a same-generation no-op) exits early for at the two
    /// generation checks below, on a real, multi-await materialization path with no synchronous alternative.
    /// When that happened, whichever ancestors HAD already been materialized in this same call (their
    /// RootId/RelativeFolder identity freshly set by <see cref="BrowserTreeModel.ApplyDirectoryListing"/>)
    /// never received an <see cref="BrowserTreeNode.IsRecursiveScope"/> value at all — defaulting to false —
    /// and nothing downstream was guaranteed to revisit them: a later navigation elsewhere in the same
    /// recursive subtree resyncs every node <em>already</em> known to <see cref="_browserTree"/>, but these
    /// ones only became known moments before this method's own abort, so a governing recursive root (and
    /// every sibling under it) could end up permanently stuck showing the outline icon despite the Catalog
    /// correctly still covering it — reported specifically against the startup-restored subtree, since an
    /// already-visited-this-session subtree's ancestors are already materialized and this loop is a same-
    /// generation no-op for it. Syncing immediately after each individual materialization — not deferred to
    /// the end of the loop — means every ancestor this call successfully materializes gets a correct icon
    /// state before any later generation check can ever skip it.
    /// </remarks>
    private async Task RevealBrowserTreeAncestorsAsync(BrowserLocation location, long generation)
    {
        IReadOnlyList<BrowserTreeNode> pending;
        _synchronizingBrowserTree = true;
        try { pending = _browserTree.GetUnmaterializedAncestors(location); }
        finally { _synchronizingBrowserTree = false; }

        foreach (var ancestor in pending)
        {
            if (generation != _browserUiGeneration) return;
            if (ancestor.AbsolutePath is not { } absolutePath) continue;

            MediaFolderEnumerationResult listing;
            try
            {
                listing = await _storage.MediaFolders.EnumerateAsync(
                    new(location.RootId, RelativeFolderUnderRoot(location.RootPath, absolutePath))).ConfigureAwait(true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                continue;
            }
            if (generation != _browserUiGeneration) return;
            if (!listing.Succeeded) continue;

            _synchronizingBrowserTree = true;
            try { _browserTree.ApplyDirectoryListing(ancestor, location.RootPath, listing.Entries); }
            finally { _synchronizingBrowserTree = false; }
            SyncBrowserTreeRecursiveIcons();
        }

        if (generation == _browserUiGeneration && _browserTree.SelectedNode is { } selected)
            BringBrowserTreeNodeIntoView(selected);
    }

    /// <summary>
    /// #124 (revised): reflects effective recursive mode as Locations-tree iconography rather than a group
    /// outline. Walks every currently-materialized node (never forces expansion/materialization — a
    /// collapsed branch's <see cref="BrowserTreeNode.Children"/> is just its one lazy-load placeholder, which
    /// this skips) and sets each node's <see cref="BrowserTreeNode.IsRecursiveScope"/> purely from its already
    /// carried <see cref="BrowserTreeNode.RootId"/>/<see cref="BrowserTreeNode.RelativeFolder"/> identity
    /// against <see cref="_browserRecursiveRoots"/> — no filesystem/Catalog work happens here. Cheap enough to
    /// call unconditionally on every successful navigation, matching this file's existing preference for
    /// always-recompute over a fragile equality short-circuit.
    /// </summary>
    private void SyncBrowserTreeRecursiveIcons()
    {
        foreach (var root in _browserTree.Roots) SyncBrowserTreeRecursiveIcon(root);
    }

    private void SyncBrowserTreeRecursiveIcon(BrowserTreeNode node)
    {
        if (!node.IsPlaceholder && node.RootId is { } rootId && node.RelativeFolder is { } relativeFolder)
            node.IsRecursiveScope = BrowserRecursiveRootLogic.IsEffectivelyRecursive(_browserRecursiveRoots, rootId, relativeFolder);
        foreach (var child in node.Children) SyncBrowserTreeRecursiveIcon(child);
    }


    private static DependencyObject? FindDescendantByName(DependencyObject parent, string name)
    {
        var count = VisualTreeHelper.GetChildrenCount(parent);
        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is FrameworkElement { } element && element.Name == name) return child;
            if (FindDescendantByName(child, name) is { } found) return found;
        }
        return null;
    }

    private static string? RelativeFolderUnderRoot(string rootPath, string absolutePath) =>
        string.Equals(MediaPathSemantics.NormalizeRootPath(rootPath), MediaPathSemantics.NormalizeRootPath(absolutePath),
            StringComparison.OrdinalIgnoreCase)
            ? null
            : MediaPathSemantics.NormalizeRelativePath(Path.GetRelativePath(rootPath, absolutePath));

    private static TreeViewItem? FindBrowserTreeItem(ItemsControl parent, BrowserTreeNode target)
    {
        foreach (var item in parent.Items)
        {
            if (parent.ItemContainerGenerator.ContainerFromItem(item) is not TreeViewItem container) continue;
            if (ReferenceEquals(item, target)) return container;
            if (item is BrowserTreeNode { IsExpanded: true } && FindBrowserTreeItem(container, target) is { } child)
                return child;
        }
        return null;
    }

    private void BrowserScopePane_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!ScrollBrowserScopeByWheel(e.Delta)) return;
        e.Handled = true;
    }

    private bool ScrollBrowserScopeByWheel(int delta)
    {
        const double pixelsPerWheelNotch = 48;
        var distance = -(delta / (double)Mouse.MouseWheelDeltaForOneLine) * pixelsPerWheelNotch;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) && BrowserFolderScrollViewer.ScrollableWidth > 0)
        {
            var offset = Math.Clamp(BrowserFolderScrollViewer.HorizontalOffset + distance, 0,
                BrowserFolderScrollViewer.ScrollableWidth);
            if (Math.Abs(offset - BrowserFolderScrollViewer.HorizontalOffset) < 0.01) return false;
            BrowserFolderScrollViewer.ScrollToHorizontalOffset(offset);
        }
        else if (BrowserFolderScrollViewer.ScrollableHeight > 0)
        {
            var offset = Math.Clamp(BrowserFolderScrollViewer.VerticalOffset + distance, 0,
                BrowserFolderScrollViewer.ScrollableHeight);
            if (Math.Abs(offset - BrowserFolderScrollViewer.VerticalOffset) < 0.01) return false;
            BrowserFolderScrollViewer.ScrollToVerticalOffset(offset);
        }
        else
            return false;
        return true;
    }

    private async void BrowserGo_Click(object sender, RoutedEventArgs e) => await NavigateToEnteredBrowserPathAsync();

    private async void BrowserCurrentPath_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        await NavigateToEnteredBrowserPathAsync();
    }

    private async Task NavigateToEnteredBrowserPathAsync()
    {
        if (!string.IsNullOrWhiteSpace(BrowserCurrentPath.Text))
        {
            RequestBrowserTreeSelection(BrowserCurrentPath.Text);
            await RunBrowserNavigationAsync(() => _browserNavigation.NavigateToPathAsync(BrowserCurrentPath.Text));
        }
    }

    private async void BrowserBack_Click(object sender, RoutedEventArgs e)
    {
        RequestBrowserTreeSelection(_browserNavigation.BackTarget);
        await RunBrowserNavigationAsync(() => _browserNavigation.BackAsync());
    }

    private async void BrowserForward_Click(object sender, RoutedEventArgs e)
    {
        RequestBrowserTreeSelection(_browserNavigation.ForwardTarget);
        await RunBrowserNavigationAsync(() => _browserNavigation.ForwardAsync());
    }

    private async void BrowserUp_Click(object sender, RoutedEventArgs e)
    {
        RequestBrowserTreeSelection(_browserNavigation.UpTarget);
        await RunBrowserNavigationAsync(() => _browserNavigation.UpAsync());
    }

    private async void BrowserRefresh_Click(object sender, RoutedEventArgs e)
    {
        if (_activeCollectionScope is { } collection)
            await LoadCollectionScopeAsync(collection.Collection.CollectionId);
        else
            await RunBrowserNavigationAsync(() => _browserNavigation.RefreshAsync());
    }

    private void BrowserFolderTree_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) =>
        _browserFolderPointerTarget = BrowserTreeNodeFromElement(e.OriginalSource as DependencyObject);

    /// <summary>
    /// #124 (revised): toggles Include Subfolders for whichever folder is currently open, via
    /// <see cref="BrowserNavigationSession.SetIncludeSubfoldersAsync"/> — establishing/removing a durable
    /// Catalog recursive root rather than flipping a settable field — through the same
    /// generation/cancellation/latest-wins machinery as any other navigation, so a rapid re-toggle or a folder
    /// change mid-scan safely supersedes this request rather than racing it. Deliberately does not touch
    /// <see cref="_browserQueryScope"/> or call <see cref="ResetBrowserQueryToolbar"/>: #124 requires the
    /// current search/filter/sort to survive a scope change. Selection clearing happens in
    /// <see cref="ApplyBrowserState"/> once the new scope's results actually arrive, not here, so a request
    /// that ends up superseded never touches selection at all.
    /// </summary>
    private async void BrowserIncludeSubfoldersButton_Click(object sender, RoutedEventArgs e)
    {
        if (_synchronizingBrowserScopeMode) return;
        var enabled = BrowserIncludeSubfoldersButton.IsChecked == true;
        var mode = enabled ? BrowserScopeMode.IncludeSubfolders : BrowserScopeMode.DirectFolder;
        await RunBrowserNavigationAsync(() => _browserNavigation.SetIncludeSubfoldersAsync(enabled), mode);
    }

    /// <summary>Applies a successful navigation result and reveals its Locations-tree ancestors. Returns false (without side effects) for a stale, null, or non-success state.</summary>
    private bool ApplyBrowserSuccessState(BrowserFolderState? state, long generation)
    {
        if (state is null || generation != _browserUiGeneration ||
            state.Status is not (BrowserFolderStatus.Ready or BrowserFolderStatus.Empty))
            return false;
        ApplyBrowserState(state);
        if (state.Location is { } location) _ = RevealBrowserTreeAncestorsAsync(location, generation);
        return true;
    }

    /// <summary>
    /// Drives one navigation/scope operation through the shared loading-overlay/generation machinery.
    /// <paramref name="scopeModeOverride"/> lets a caller that is about to *change* the scope mode (the
    /// Include Subfolders toggle) show the right label immediately, since effective mode for the folder about
    /// to load is not known synchronously — it is derived live from the Catalog partway through
    /// <paramref name="navigate"/> — every other caller passes nothing and simply reflects whichever mode the
    /// last successfully committed state (<see cref="BrowserFolderState.Mode"/>) already showed, a reasonable
    /// best-available guess for the label that self-corrects once the load actually completes.
    /// </summary>
    private async Task RunBrowserNavigationAsync(Func<Task<BrowserFolderState?>> navigate,
        BrowserScopeMode? scopeModeOverride = null)
    {
        var generation = ++_browserUiGeneration;
        ShowBrowserLoadingState((scopeModeOverride ?? _browserNavigation.State.Mode) == BrowserScopeMode.IncludeSubfolders
            ? "Scanning folder and subfolders…" : "Loading folder…");
        try
        {
            var state = await navigate();
            if (state is not null && generation == _browserUiGeneration && !ApplyBrowserSuccessState(state, generation))
                ApplyBrowserNavigationFailure(state);
        }
        catch (OperationCanceledException)
        {
            if (generation == _browserUiGeneration) RestoreLoadedBrowserSelection();
        }
        catch (Exception exception)
        {
            if (generation == _browserUiGeneration)
                ApplyBrowserNavigationFailure(new(_browserNavigation.State.Location, BrowserFolderStatus.Failed, [],
                    $"Lightflow could not open this folder: {exception.Message}",
                    _browserNavigation.State.CanGoBack, _browserNavigation.State.CanGoForward,
                    _browserNavigation.State.CanGoUp));
        }
        finally
        {
            if (generation == _browserUiGeneration)
                BrowserLoadingOverlay.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    /// #124: <see cref="BrowserNavigationSession.RecursiveScopeProgressChanged"/> is generation-gated at the
    /// source — a report from a superseded recursive walk (mode toggled off, a different folder opened, the
    /// session disposed) never reaches this handler at all — so no additional staleness check is needed here;
    /// this only ever applies progress for whichever recursive walk is still actually current. Marshaled onto
    /// the UI thread like every other cross-thread signal in this file, since the event can fire from a
    /// background/thread-pool continuation inside the recursive walk.
    /// </summary>
    private void BrowserNavigation_RecursiveScopeProgressChanged(object? sender, RecursiveScopeProgress progress) =>
        Dispatcher.BeginInvoke(() => ApplyRecursiveScopeLoadingProgress(progress));

    /// <summary>
    /// Applies one live recursive-walk progress report to the shared loading progress bar. Stays indeterminate
    /// until <see cref="RecursiveScopeProgress.FoldersDiscovered"/> grows past the trivial single-folder case,
    /// so a small recursive scope (nothing left to discover beyond the base folder) never flashes a
    /// near-instant, uninformative "1 of 1" determinate bar before the overlay disappears — and, just as
    /// importantly, stays indeterminate for as long as the denominator is still actively growing report to
    /// report, via <see cref="_browserRecursiveProgressLastDiscovered"/>: flipping to determinate the instant
    /// <see cref="RecursiveScopeProgress.FoldersDiscovered"/> first reaches 2 previously produced a jarring
    /// visual — a brief, misleadingly high percentage immediately followed by a hard leftward jump as the rest
    /// of a wide folder's siblings were discovered a moment later, in the very next report. Waiting for
    /// discovery to hold steady for at least one report keeps the percentage honest without ever showing a
    /// value that is about to be immediately superseded by a much larger denominator. Never fabricates a
    /// percentage: both values come directly from the service-layer walk, never counted here.
    /// </summary>
    private void ApplyRecursiveScopeLoadingProgress(RecursiveScopeProgress progress)
    {
        var stillDiscovering = progress.FoldersDiscovered > _browserRecursiveProgressLastDiscovered;
        _browserRecursiveProgressLastDiscovered = progress.FoldersDiscovered;
        if (progress.FoldersDiscovered < 2 || stillDiscovering) { BrowserLoadingProgressBar.IsIndeterminate = true; return; }
        BrowserLoadingProgressBar.IsIndeterminate = false;
        BrowserLoadingProgressBar.Maximum = progress.FoldersDiscovered;
        BrowserLoadingProgressBar.Value = Math.Min(progress.FoldersVisited, progress.FoldersDiscovered);
    }

    private void ApplyBrowserState(BrowserFolderState state)
    {
        ActivateFolderScopeSelection();
        _activeCollectionScope = null;
        BrowserCurrentPath.IsReadOnly = false;
        _workspaceState.SetBrowserCollectionState(null, _browserCollectionTree.ExpandedSetIds());
        _synchronizingCollectionTree = true;
        try { _browserCollectionTree.Select(null); }
        finally { _synchronizingCollectionTree = false; }
        _lastLoadedBrowserState = state;
        var scope = state.Location is { } scopeLocation ? $"folder:{scopeLocation.RootId:D}:{scopeLocation.RelativeFolder}" : null;
        // A genuinely new scope (different folder, or navigating away from/into a location entirely) starts
        // sort/filter/search over: the media-area toolbar narrows *the current* scope, not a remembered one.
        // Refreshing the same folder (monitoring, explicit Refresh) must not disturb the chosen view of it.
        // Deliberately keyed on folder only, NOT scope mode: #124 requires BrowserQuery to survive toggling
        // Include Subfolders, even though the candidate set itself changes.
        if (scope != _browserQueryScope) ResetBrowserQueryToolbar();
        _browserQueryScope = scope;

        // #124: unlike _browserQueryScope above, selection identity DOES include scope mode — folder
        // navigation and toggling Include Subfolders must both clear Browser selection unconditionally, so an
        // asset outside the newly active scope can never remain invisibly selected (see #75). A true
        // same-scope refresh (explicit Refresh, monitoring) leaves this identity unchanged and therefore
        // preserves selection via BrowserGridModel.Populate's existing key-based retention.
        var scopeIdentity = state.Location is { } identityLocation
            ? $"folder:{identityLocation.RootId:D}:{identityLocation.RelativeFolder}:{state.Mode}" : null;
        // #110: a genuine navigation/scope change (not a same-folder refresh) leaves whatever asset the
        // Player/Viewer was showing behind — it belonged to the scope being left. A same-folder refresh
        // (explicit Refresh, a relevant monitoring event) reaches this with an unchanged scopeIdentity and
        // therefore never disturbs an open Player/Viewer, exactly like it already preserves selection below.
        if (scopeIdentity != _browserScopeIdentity && _browserPresentation == BrowserPresentationMode.PlayerViewer)
            _ = ReturnToBrowserGridAsync(restoreScrollOffset: false, focusGrid: false);
        if (scopeIdentity != _browserScopeIdentity) _browserGrid.ClearSelection();
        _browserScopeIdentity = scopeIdentity;
        // #124 (revised): the same round-trip that determined effective mode already fetched every stored
        // Catalog recursive root — reused here for Locations-tree iconography rather than querying again.
        _browserRecursiveRoots = state.RecursiveRoots ?? [];

        _synchronizingBrowserTree = true;
        IReadOnlyList<MediaFolderEntry> directFiles;
        try { directFiles = _browserTree.Synchronize(state); }
        finally { _synchronizingBrowserTree = false; }
        // #124: the tree always synchronizes against direct children (state.Entries, via Synchronize above);
        // the grid's candidate set additionally expands to every descendant folder's media while recursive
        // scope is active. See BrowserFolderState.RecursiveMediaEntries.
        _browserGrid.Populate(state.RecursiveMediaEntries ?? directFiles);
        UpdateBrowserGridColumns();
        if (state.DerivedWork is { } batch)
        {
            _browserGrid.ApplyAssetIdentities(batch.Reconciliation.Items);
            foreach (var item in batch.Reconciliation.Items)
                _browserGrid.ApplyThumbnailGenerating(item.AssetId, _storage.ThumbnailActivity.IsGenerating(item.AssetId));
        }
        if (state.DerivedWork is { } stateBatch)
            _ = LoadBrowserAssetStatesAsync(stateBatch.Reconciliation.Items, _browserUiGeneration, _browserAssetStateRevision);
        AttachBrowserDerivedWork(state.DerivedWork, _browserUiGeneration);
        AuditBrowserVisualIdentitiesAfterLutInitialization();
        BrowserCurrentPath.Text = state.Location?.DisplayPath ?? "";
        BrowserBackButton.IsEnabled = state.CanGoBack;
        BrowserForwardButton.IsEnabled = state.CanGoForward;
        BrowserUpButton.IsEnabled = state.CanGoUp;
        BrowserRefreshButton.IsEnabled = state.Location is not null;
        BrowserQueryToolbar.IsEnabled = state.Location is not null;
        SyncBrowserSubfoldersCapability(state);
        SyncBrowserScopeToggle();
        SyncBrowserTreeRecursiveIcons();
        // Reveals the (now-current) grid content that ShowBrowserLoadingState hid at the start of this
        // navigation — the underlying BrowserGridModel data was never cleared, only its presentation.
        BrowserGridRows.Visibility = Visibility.Visible;
        var presentableCount = _browserGrid.TotalCount;
        BrowserEmptyState.Visibility = state.Status == BrowserFolderStatus.Ready && presentableCount > 0
            ? Visibility.Collapsed : Visibility.Visible;
        BrowserEmptyTitle.Text = state.Status switch
        {
            BrowserFolderStatus.Ready when presentableCount == 0 => "No media files in this folder",
            BrowserFolderStatus.Empty when state.Location is not null => "No media files in this folder",
            BrowserFolderStatus.RootUnavailable => "Media Root unavailable",
            BrowserFolderStatus.RootNotFound => "Media Root not found",
            BrowserFolderStatus.FolderNotFound => "Folder not found",
            BrowserFolderStatus.AccessDenied => "Access denied",
            BrowserFolderStatus.InvalidPath => "Folder cannot be opened",
            BrowserFolderStatus.FolderUnavailable => "Folder unavailable",
            BrowserFolderStatus.Failed => "Folder could not be loaded",
            BrowserFolderStatus.CatalogUnavailable => "Catalog unavailable",
            _ => "Choose a storage location"
        };
        BrowserEmptyMessage.Text = state.Diagnostic ?? (state.Location is null
            ? "Select a drive, mapped location, or managed library to browse its folders and supported media."
            : "Choose another folder from the navigation pane or refresh this location.");
        UpdateBrowserStatusText();

        if (state.Location is { } location)
        {
            // Selection is intentionally never persisted here, and #124 (revised) no longer persists scope
            // mode here either — recursive-root configuration is durable Catalog data now, not workspace
            // state (see BrowserRecursiveRoot); only the plain folder identity is remembered.
            _workspaceState.SetBrowserLocation(location.RootId, location.RelativeFolder, location.AbsolutePath);
            _workspaceSaveTimer.Stop();
            _workspaceSaveTimer.Start();
        }
    }

    private async Task LoadBrowserAssetStatesAsync(IReadOnlyList<CatalogReconciliationItem> items, long generation, long revision)
    {
        try
        {
            var states = await _storage.BrowserAssetStates.GetQueryStatesAsync(items.Select(item => item.AssetId).ToArray())
                .ConfigureAwait(true);
            if (generation != _browserUiGeneration) return;
            foreach (var (assetId, state) in states)
            {
                // A committed Player change that landed after this read began owns that asset's current
                // presentation. Other assets remain safe to apply; an unrelated/previous-scope save must
                // never discard the entire folder's state projection.
                var changedAt = _browserAssetStateRevisions.TryGetValue(assetId, out var assetRevision)
                    ? assetRevision : (long?)null;
                if (BrowserAssetStateRevisionPolicy.CanApply(revision, changedAt))
                    _browserGrid.ApplyAssetState(assetId, state);
            }
            if (_browserGrid.Query.Filters.Any(filter => filter.Field is BrowserFilterField.ColorState or
                BrowserFilterField.CameraLutState or BrowserFilterField.CreativeLutState or
                BrowserFilterField.ReviewRangeState or BrowserFilterField.SubclipState or BrowserFilterField.Rating or
                BrowserFilterField.Flag or BrowserFilterField.ColorLabel or BrowserFilterField.Keyword))
            {
                _browserGrid.ReapplyQuery();
                UpdateBrowserStatusText();
            }
        }
        catch (OperationCanceledException) { }
        catch (InvalidOperationException) { }
    }

    private const string BrowserIncludeSubfoldersDefaultToolTip =
        "Include Subfolders — browse this folder and every descendant folder as one media set";

    /// <summary>
    /// #124: Include Subfolders only makes sense where establishing it would actually do something — a folder
    /// with zero immediate child folders can never become a recursive root. <see cref="BrowserTreeNode.HasSubfolders"/>
    /// answers this for free from data <see cref="ApplyBrowserState"/> (the only caller, right after
    /// <see cref="BrowserTreeModel.Synchronize"/> populates the newly-selected node's real children) already
    /// has on hand — never a synchronous filesystem probe from this control itself, and never forces the tree
    /// to expand merely to answer the question. Effective recursive mode always wins regardless of the
    /// selected folder's own children: disabling from an inherited recursive LEAF must stay possible (that is
    /// how its governing ancestor root gets removed), so "no subfolders" only ever disables the OFF state, not
    /// the ability to turn OFF an inherited ON. While the answer is still unknown (a not-yet-materialized
    /// node, e.g. immediately after <see cref="BrowserNavigation_EffectiveScopeDetermined"/>'s early fast path
    /// but before this method next runs), this method simply has not run yet for that generation — the button
    /// keeps showing whatever its previous, still-valid state was rather than flashing disabled and back.
    /// </summary>
    private void SyncBrowserSubfoldersCapability(BrowserFolderState state)
    {
        var effectiveRecursive = state.Mode == BrowserScopeMode.IncludeSubfolders;
        var definitelyNoSubfolders = _browserTree.SelectedNode?.HasSubfolders == false;
        var noSubfolders = state.Location is not null && !effectiveRecursive && definitelyNoSubfolders;
        BrowserIncludeSubfoldersButton.IsEnabled = state.Location is not null && !noSubfolders;
        BrowserIncludeSubfoldersButton.ToolTip = noSubfolders ? "No subfolders" : BrowserIncludeSubfoldersDefaultToolTip;
    }

    /// <summary>Reflects the navigation session's current #124 effective scope mode on the toggle without re-entering its Click handler.</summary>
    private void SyncBrowserScopeToggle() => SyncBrowserScopeToggle(_browserNavigation.State.Mode);

    private void SyncBrowserScopeToggle(BrowserScopeMode mode)
    {
        _synchronizingBrowserScopeMode = true;
        try { BrowserIncludeSubfoldersButton.IsChecked = mode == BrowserScopeMode.IncludeSubfolders; }
        finally { _synchronizingBrowserScopeMode = false; }
    }

    /// <summary>
    /// #124 (further revised): applies the fast, purely Catalog/location-derived half of a navigation's
    /// outcome — Locations-tree selection/reveal/scroll, toolbar toggle, and tree icons — the moment it is
    /// known, rather than waiting for the (potentially slow) recursive discovery or authoritative
    /// reconciliation <see cref="BrowserNavigationSession.EffectiveScopeDetermined"/> precedes. This is what
    /// makes startup restoration (and enabling/disabling Include Subfolders, and navigating into or out of an
    /// existing recursive subtree) feel immediate: the tree reveals/selects/scrolls to the location that is
    /// actually loading, and the toolbar/icons reflect its effective mode, all before the eventual media
    /// result set arrives — reusing exactly the same tree machinery (<see cref="RequestBrowserTreeSelection(BrowserLocation?)"/>,
    /// <see cref="RevealBrowserTreeAncestorsAsync"/>, <see cref="BringBrowserTreeNodeIntoView"/>) every
    /// interactive navigation already drives, never a second startup-only implementation. <see cref="ApplyBrowserState"/>
    /// still redundantly re-applies all of this once the full state commits — cheap, and a safety net for the
    /// rare case a later navigation's full commit lands after this event but is for the same folder. Marshaled
    /// onto the UI thread like every other cross-thread <see cref="BrowserNavigationSession"/> signal, since
    /// the event can fire from a background/thread-pool continuation.
    /// </summary>
    private void BrowserNavigation_EffectiveScopeDetermined(object? sender, BrowserEffectiveScope scope) =>
        Dispatcher.BeginInvoke(() =>
        {
            if (_browserScopeSelection.Active == BrowserScopeSelectionKind.Collection) return;
            _browserRecursiveRoots = scope.RecursiveRoots;
            SyncBrowserTreeRecursiveIcons();
            SyncBrowserScopeToggle(scope.Mode);
            RequestBrowserTreeSelection(scope.Location);
            _ = RevealBrowserTreeAncestorsAsync(scope.Location, _browserUiGeneration);
        });

    private void ApplyBrowserNavigationFailure(BrowserFolderState failure)
    {
        RestoreLoadedBrowserSelection();
        SyncBrowserScopeToggle();
        // Unlike ShowBrowserLoadingState's hide (a new scope is being fetched, so the old one is no longer
        // relevant), a failure means the previously loaded folder is still exactly what "remains loaded" per
        // the message below — restore its content presentation rather than leaving the grid hidden.
        BrowserGridRows.Visibility = Visibility.Visible;
        BrowserEmptyTitle.Text = failure.Status switch
        {
            BrowserFolderStatus.RootUnavailable => "Media Root unavailable",
            BrowserFolderStatus.RootNotFound => "Media Root not found",
            BrowserFolderStatus.FolderNotFound => "Folder not found",
            BrowserFolderStatus.AccessDenied => "Access denied",
            BrowserFolderStatus.InvalidPath => "Folder cannot be opened",
            BrowserFolderStatus.CatalogUnavailable => "Catalog unavailable",
            _ => "Folder could not be loaded"
        };
        BrowserEmptyMessage.Text = failure.Diagnostic ?? "The previous folder remains loaded. Try again when the location is available.";
        BrowserEmptyState.Visibility = Visibility.Visible;
    }

    private void RestoreLoadedBrowserSelection()
    {
        _synchronizingBrowserTree = true;
        try { _browserTree.RestoreSelection(_lastLoadedBrowserState?.Location); }
        finally { _synchronizingBrowserTree = false; }
    }

    private void UpdateBrowserGridColumns()
    {
        const double scrollbarAllowance = 20;
        var width = BrowserGridHost.ActualWidth - BrowserGridHost.Padding.Left - BrowserGridHost.Padding.Right - scrollbarAllowance;
        if (width <= 0) return;
        _browserGrid.SetColumns(BrowserGridLayout.ComputeColumns(width, BrowserGridLayout.TileWidthFor(_browserThumbnailSize)));
    }

    private void BrowserGridHost_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateBrowserGridColumns();

    /// <summary>
    /// #125's single authoritative apply point: tracks the chosen size, pushes it to the slider (guarded so
    /// this can never re-enter <see cref="BrowserThumbnailSizeSlider_ValueChanged"/>), updates the
    /// decrease/increase buttons' enabled state from the same authoritative range, updates the two
    /// DynamicResource values every tile's Width/thumbnail-area-height template-binds to, and reflows the
    /// grid at the new tile footprint. Deliberately touches nothing about <see cref="BrowserQuery"/>,
    /// Browser scope, Catalog, or Preview generation — existing cached thumbnails are simply redrawn at the
    /// new element size by WPF's own Image/Stretch handling, never re-fetched or regenerated. The slider and
    /// the two step buttons are three inputs to this one method, never three separate sizing paths.
    /// </summary>
    private void ApplyBrowserThumbnailSize(BrowserThumbnailSize size)
    {
        _browserThumbnailSize = size;
        _synchronizingBrowserThumbnailSize = true;
        try { BrowserThumbnailSizeSlider.Value = (int)size; }
        finally { _synchronizingBrowserThumbnailSize = false; }
        BrowserThumbnailSizeDecreaseButton.IsEnabled = (int)size > 0;
        BrowserThumbnailSizeIncreaseButton.IsEnabled = (int)size < BrowserGridLayout.ThumbnailSizes.Count - 1;
        Resources["BrowserTileWidth"] = BrowserGridLayout.TileWidthFor(size);
        Resources["BrowserTileThumbnailHeight"] = BrowserGridLayout.ThumbnailAreaHeightFor(size);
        Resources["BrowserTileInfoPreviewHeight"] = Math.Max(48, BrowserGridLayout.ThumbnailAreaHeightFor(size) - 30);
        Resources["BrowserStateIconSpacing"] = new Thickness(0, 0, size == BrowserThumbnailSize.Small ? 3 : 7, 0);
        UpdateBrowserGridColumns();
    }

    private void BrowserThumbnailSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_synchronizingBrowserThumbnailSize) return;
        ApplyBrowserThumbnailSize(BrowserGridLayout.ThumbnailSizeFromLevel((int)Math.Round(e.NewValue)));
    }

    /// <summary>Steps exactly one level toward Small; disabled (so unreachable by click) once already there — see <see cref="BrowserGridLayout.StepLevel"/>.</summary>
    private void BrowserThumbnailSizeDecreaseButton_Click(object sender, RoutedEventArgs e) =>
        ApplyBrowserThumbnailSize(BrowserGridLayout.StepLevel(_browserThumbnailSize, -1));

    /// <summary>Steps exactly one level toward Maximum; disabled (so unreachable by click) once already there — see <see cref="BrowserGridLayout.StepLevel"/>.</summary>
    private void BrowserThumbnailSizeIncreaseButton_Click(object sender, RoutedEventArgs e) =>
        ApplyBrowserThumbnailSize(BrowserGridLayout.StepLevel(_browserThumbnailSize, 1));

    private void BrowserGridHost_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _browserGrid.ClearSelection();
        BrowserGridRows.Focus();
        UpdateBrowserStatusText();
    }

    private void BrowserGridTile_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not BrowserGridTile tile) return;
        _browserAssetPendingSingleSelection = null;
        _browserAssetDragStart = e.GetPosition(BrowserGridRows);
        _browserAssetDragTile = tile;
        // #110: a real double-click always opens, matching ordinary Explorer/media-browser convention,
        // regardless of an incidental modifier key still down from the first click.
        if (e.ClickCount >= 2)
        {
            _browserGrid.SelectSingle(tile.Index);
            UpdateBrowserStatusText();
            e.Handled = true;
            _ = OpenBrowserPlayerViewerAsync(tile);
            return;
        }
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) _browserGrid.SelectRange(tile.Index);
        else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) _browserGrid.ToggleCtrl(tile.Index);
        else if (BrowserAssetDragSelection.ShouldDeferSingleSelection(tile.IsSelected,
                     _browserGrid.SelectedKeys.Count, shiftPressed: false, controlPressed: false))
            _browserAssetPendingSingleSelection = tile;
        else _browserGrid.SelectSingle(tile.Index);
        BrowserGridRows.Focus();
        UpdateBrowserStatusText();
        e.Handled = true;
    }

    private void BrowserGridTile_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not BrowserGridTile tile) return;
        if (BrowserSelectionActions.ShouldReplaceSelectionOnRightClick(tile.IsSelected))
            _browserGrid.SelectSingle(tile.Index);
        BrowserGridRows.Focus();
        UpdateBrowserStatusText();
    }

    private void BrowserGridTile_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (((FrameworkElement)sender).ContextMenu is not { } menu) return;
        var state = CurrentBrowserSelectionActions();
        ((MenuItem)menu.Items[0]).IsEnabled = state.SelectionCount > 0 && _browserGrid.SelectedAssetIdsInBrowserOrder.Count == state.SelectionCount;
        ((MenuItem)menu.Items[1]).IsEnabled = state.SelectionCount > 0 && _activeCollectionScope is not null;
        ((MenuItem)menu.Items[3]).IsEnabled = state.CanExport;
        ((MenuItem)menu.Items[4]).IsEnabled = state.CanRegenerateThumbnails;
        ((MenuItem)menu.Items[7]).IsEnabled = state.CanAssignCameraLut && BrowserCameraLutCombo.IsEnabled;
        ((MenuItem)menu.Items[8]).IsEnabled = state.CanAssignCreativeLut && BrowserCreativeLutCombo.IsEnabled;
    }

    private void BrowserGridTile_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _browserAssetDragTile is null) return;
        var current = e.GetPosition(BrowserGridRows);
        if (Math.Abs(current.X - _browserAssetDragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - _browserAssetDragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        var tile = _browserAssetDragTile;
        _browserAssetDragTile = null;
        _browserAssetPendingSingleSelection = null;
        var ids = BrowserAssetDragSelection.AssetIdsForDrag(tile.IsSelected, tile.AssetId,
            _browserGrid.SelectedAssetIdsInBrowserOrder);
        if (ids.Count == 0) return;
        System.Windows.DragDrop.DoDragDrop((DependencyObject)sender, new BrowserAssetDragPayload(ids),
            System.Windows.DragDropEffects.Copy | System.Windows.DragDropEffects.Move);
    }

    private void BrowserGridTile_DragOver(object sender, System.Windows.DragEventArgs e)
    {
        var payload = e.Data.GetData(typeof(BrowserAssetDragPayload)) as BrowserAssetDragPayload;
        e.Effects = payload is not null && CanManuallyReorderCollection()
            ? System.Windows.DragDropEffects.Move : System.Windows.DragDropEffects.None;
        e.Handled = true;
    }

    private void BrowserGridTile_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not BrowserGridTile tile ||
            !ReferenceEquals(tile, _browserAssetPendingSingleSelection)) return;
        _browserAssetPendingSingleSelection = null;
        _browserAssetDragTile = null;
        _browserGrid.SelectSingle(tile.Index);
        UpdateBrowserStatusText();
        e.Handled = true;
    }

    private async void BrowserGridTile_Drop(object sender, System.Windows.DragEventArgs e)
    {
        var payload = e.Data.GetData(typeof(BrowserAssetDragPayload)) as BrowserAssetDragPayload;
        if (payload is null || _activeCollectionScope is null || !CanManuallyReorderCollection() ||
            ((FrameworkElement)sender).DataContext is not BrowserGridTile { AssetId: { } target }) return;
        var memberships = await _storage.Collections.ListMembershipsAsync(_activeCollectionScope.Collection.CollectionId);
        var current = memberships.OrderBy(item => item.Ordinal).Select(item => item.AssetId).ToArray();
        var reordered = BrowserCollectionMembershipInteraction.MoveBefore(current, payload.AssetIds, target);
        if (reordered.SequenceEqual(current)) return;
        var byId = memberships.ToDictionary(item => item.AssetId);
        await RunCollectionActionAsync(async () =>
        {
            await _storage.Collections.ReorderMembershipsAsync(_activeCollectionScope.Collection.CollectionId,
                reordered.Select(id => new CollectionOrder(id, byId[id].Revision)).ToArray());
            _browserGrid.ApplyManualOrder(reordered);
            UpdateBrowserStatusText();
        });
        e.Handled = true;
    }

    private bool CanManuallyReorderCollection() => _activeCollectionScope is not null &&
        _browserGrid.Query.SortMode == BrowserSortMode.Manual && _browserGrid.Query.Filters.Count == 0 &&
        string.IsNullOrWhiteSpace(_browserGrid.Query.SearchText);

    private async void BrowserAddToCollection_Click(object sender, RoutedEventArgs e) => await AddBrowserSelectionToCollectionsAsync();

    private async Task AddBrowserSelectionToCollectionsAsync()
    {
        var assetIds = _browserGrid.SelectedAssetIdsInBrowserOrder;
        if (assetIds.Count == 0) return;
        var choices = BrowserCollectionTreeModel.Flatten(_browserCollectionTree.Roots)
            .Where(node => node.IsCollection).Select(node => (node.Id, CollectionDisplayPath(node.Id))).ToArray();
        var dialog = new AddToCollectionDialog(assetIds.Count, choices, CreateCollectionFromAddFlowAsync) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        var destinationName = dialog.SelectedCollectionIds.Count == 1
            ? BrowserCollectionTreeModel.Flatten(_browserCollectionTree.Roots)
                .FirstOrDefault(node => node.Id == dialog.SelectedCollectionIds[0])?.Name : null;
        await RunCollectionActionAsync(async () =>
        {
            var added = 0;
            foreach (var collectionId in dialog.SelectedCollectionIds)
                added += (await _storage.Collections.AddMembershipsAsync(collectionId, assetIds)).Count(result => result.Created);
            var attempted = assetIds.Count * dialog.SelectedCollectionIds.Count;
            var duplicates = attempted - added;
            BrowserStatusText.Text = CollectionMembershipFeedback.ForAdd(added, duplicates,
                assetIds.Count, dialog.SelectedCollectionIds.Count, destinationName);
        });
    }

    private string CollectionDisplayPath(Guid collectionId)
    {
        var nodes = BrowserCollectionTreeModel.Flatten(_browserCollectionTree.Roots).ToDictionary(node => node.Id);
        var node = nodes[collectionId];
        var parts = new List<string> { node.Name };
        var parent = node.ParentSetId;
        while (parent is { } id && nodes.TryGetValue(id, out var set)) { parts.Insert(0, set.Name); parent = set.ParentSetId; }
        return string.Join(" / ", parts);
    }

    private async Task<MediaCollection?> CreateCollectionFromAddFlowAsync()
    {
        var dialog = new NewCollectionDialog(BrowserCollectionPlacement.Options(_browserCollectionTree.Roots), null) { Owner = this };
        if (dialog.ShowDialog() != true) return null;
        MediaCollection? created = null;
        await RunCollectionActionAsync(async () =>
        {
            created = await _storage.Collections.CreateCollectionAsync(dialog.CollectionName, dialog.ParentSetId);
            await RefreshCollectionsAsync(_activeCollectionScope?.Collection.CollectionId);
        });
        return created;
    }

    private async void BrowserRemoveFromCollection_Click(object sender, RoutedEventArgs e)
        => await RemoveBrowserSelectionFromActiveCollectionAsync();

    private async Task RemoveBrowserSelectionFromActiveCollectionAsync()
    {
        if (_activeCollectionScope is null) return;
        var selected = _browserGrid.SelectedAssetIdsInBrowserOrder.ToHashSet();
        var memberships = await _storage.Collections.ListMembershipsAsync(_activeCollectionScope.Collection.CollectionId);
        var removing = memberships.Where(item => selected.Contains(item.AssetId))
            .Select(item => new CollectionOrder(item.AssetId, item.Revision)).ToArray();
        if (removing.Length == 0) return;
        var collectionId = _activeCollectionScope.Collection.CollectionId;
        var collectionName = _activeCollectionScope.Collection.Name;
        if (!ConfirmationDialog.Confirm(this, "Remove from Collection",
                $"Remove {removing.Length} media item{(removing.Length == 1 ? "" : "s")} from “{collectionName}”?",
                "The media remains available in its folders and any other Collections.", null,
                "Remove", "Keep in Collection")) return;
        await RunCollectionActionAsync(async () =>
        {
            await _storage.Collections.RemoveMembershipsAsync(collectionId, removing);
            await LoadCollectionScopeAsync(collectionId);
            BrowserStatusText.Text = $"Removed {removing.Length} media item{(removing.Length == 1 ? "" : "s")} from {collectionName}";
        });
    }

    private async void BrowserExport_Click(object sender, RoutedEventArgs e) => await ExportBrowserSelectionAsync();
    private async void BrowserContextExport_Click(object sender, RoutedEventArgs e) => await ExportBrowserSelectionAsync();
    private async void BrowserContextExportSubclips_Click(object sender, RoutedEventArgs e) =>
        await ExportBrowserSubclipsAsync();

    private async Task ExportBrowserSelectionAsync()
    {
        var state = CurrentBrowserSelectionActions();
        if (!state.CanExport) return;
        await ExportBrowserAssetsAsync(_browserGrid.SelectedAssetIdsInBrowserOrder);
    }

    private async Task ExportBrowserAssetsAsync(IReadOnlyList<Guid> assetIds)
    {
        var location = _lastLoadedBrowserState?.Location;
        if (location is null && _activeCollectionScope is null)
        {
            MessageBox.Show("The current Browser location is no longer available.", "Export",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        await ApplyEncodingHandoffAsync(new CapabilityInvocation("video.encode", assetIds,
            location is null ? null : new CapabilitySourceContext(location.RootId, location.RelativeFolder)));
    }

    private async Task ExportBrowserSubclipsAsync()
    {
        var state = CurrentBrowserSelectionActions();
        if (!state.CanExport) return;
        var location = _lastLoadedBrowserState?.Location;
        await ApplySubclipExportHandoffAsync(new(SubclipExportEntryKind.BrowserSources,
            _browserGrid.SelectedAssetIdsInBrowserOrder, SourceContext:
                location is null ? null : new CapabilitySourceContext(location.RootId, location.RelativeFolder),
            IncludeNoSubclipSources: true));
    }

    private async void BrowserRegenerateThumbnails_Click(object sender, RoutedEventArgs e) =>
        await RegenerateBrowserThumbnailsAsync();
    private async void BrowserContextRegenerateThumbnails_Click(object sender, RoutedEventArgs e) =>
        await RegenerateBrowserThumbnailsAsync();

    private async Task RegenerateBrowserThumbnailsAsync()
    {
        var state = CurrentBrowserSelectionActions();
        var ids = BrowserThumbnailRegeneration.ResolveTargets(
            state.CanRegenerateThumbnails ? _browserGrid.SelectedAssetIdsInBrowserOrder : [],
            state.SelectionCount, _browserGrid.ThumbnailApplicableAssetIdsInScope);
        if (ids.Count == 0) return;
        if (BrowserThumbnailRegeneration.RequiresConfirmation(state.SelectionCount, ids.Count) &&
            MessageBox.Show($"Regenerate Previews for all {ids.Count} applicable assets in the current Browser scope?",
                "Regenerate Previews", MessageBoxButton.YesNo, MessageBoxImage.Question,
                MessageBoxResult.No) != MessageBoxResult.Yes) return;
        BrowserRegenerateThumbnailsButton.IsEnabled = false;
        BrowserStatusText.Text = $"Regenerating {ids.Count} Preview{(ids.Count == 1 ? "" : "s")}…";
        try
        {
            var progress = new Progress<PreviewRegenerationCompleted>(ApplyCompletedPreview);
            var results = await _storage.RegenerateThumbnailsAsync(ids, progress: progress);
            var failed = results.Count(result => !result.Succeeded);
            BrowserStatusText.Text = failed == 0
                ? $"Regenerated {results.Count} Preview{(results.Count == 1 ? "" : "s")}"
                : $"Regenerated {results.Count - failed}; {failed} could not be regenerated";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            MessageBox.Show($"Preview regeneration failed: {exception.Message}", "Regenerate Previews",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { UpdateBrowserSelectionActions(); }
    }

    private void BrowserCameraLutCombo_DropDownOpened(object sender, EventArgs e) => _ = RefreshBrowserColorSelectorsAsync();
    private void BrowserCreativeLutCombo_DropDownOpened(object sender, EventArgs e) => _ = RefreshBrowserColorSelectorsAsync();
    private async void BrowserCameraLutCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        await ApplyBrowserLutComboSelectionAsync((System.Windows.Controls.ComboBox)sender, ColorLutStage.Camera);
    private async void BrowserCreativeLutCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        await ApplyBrowserLutComboSelectionAsync((System.Windows.Controls.ComboBox)sender, ColorLutStage.Creative);
    private void BrowserContextCameraLut_SubmenuOpened(object sender, RoutedEventArgs e) =>
        PopulateBrowserLutMenu((MenuItem)sender, ColorLutStage.Camera);
    private void BrowserContextCreativeLut_SubmenuOpened(object sender, RoutedEventArgs e) =>
        PopulateBrowserLutMenu((MenuItem)sender, ColorLutStage.Creative);

    private static void ApplyBrowserLutPresentation(System.Windows.Controls.ComboBox combo,
        BrowserLutPickerPresentation presentation)
    {
        combo.Items.Clear();
        combo.DisplayMemberPath = nameof(BrowserLutActionOption.Label);
        foreach (var option in presentation.Options) combo.Items.Add(option);
        combo.SelectedIndex = presentation.SelectedIndex;
    }

    private async Task ApplyBrowserLutComboSelectionAsync(System.Windows.Controls.ComboBox combo, ColorLutStage stage)
    {
        if (_updatingBrowserColorSelectors || combo.SelectedItem is not BrowserLutActionOption { IsAction: true } action) return;
        await AssignBrowserLutAsync(stage, action.LutId, action.Label);
    }

    private void PopulateBrowserLutMenu(MenuItem parent, ColorLutStage stage)
    {
        parent.Items.Clear();
        PopulateBrowserLutItems(parent.Items, stage);
    }

    private void PopulateBrowserLutItems(ItemCollection items, ColorLutStage stage)
    {
        var resources = _storage.LutCache.Snapshot(stage).Resources;
        Add(null, "No LUT");
        foreach (var resource in resources) Add(resource.LutId, resource.DisplayName);
        void Add(Guid? lutId, string label)
        {
            var item = new MenuItem { Header = label, Style = (Style)FindResource("LightflowMenuItemStyle") };
            item.Click += async (_, _) => await AssignBrowserLutAsync(stage, lutId, label);
            items.Add(item);
        }
    }

    private async Task AssignBrowserLutAsync(ColorLutStage stage, Guid? lutId, string label)
    {
        var state = CurrentBrowserSelectionActions();
        if (stage == ColorLutStage.Camera ? !state.CanAssignCameraLut : !state.CanAssignCreativeLut) return;
        var ids = _browserGrid.SelectedAssetIdsInBrowserOrder;
        try
        {
            await _storage.AssetColors.SetStageAsync(ids, stage, lutId);
            var committed = await _storage.AssetColors.GetAsync(ids);
            foreach (var id in ids)
                ApplyCommittedBrowserAssetStateFlag(id, BrowserAssetState.Color, committed[id].HasColor);
            await RefreshBrowserColorSelectorsAsync();
            _ = RegenerateColorThumbnailsAsync(ids);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or KeyNotFoundException or SqliteException)
        {
            MessageBox.Show($"The {EncodingLutResourceStore.StageName(stage)} LUT assignment failed: {exception.Message}",
                "Assign Color", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }


    private async Task RegenerateColorThumbnailsAsync(IReadOnlyList<Guid> ids)
    {
        try
        {
            await _storage.RegenerateThumbnailsAsync(ids,
                progress: new Progress<PreviewRegenerationCompleted>(ApplyCompletedPreview),
                mode: PreviewRegenerationMode.EnsureCurrent);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            BrowserStatusText.Text = $"Color Preview regeneration could not complete: {exception.Message}";
        }
    }

    private void ApplyCompletedPreview(PreviewRegenerationCompleted completed)
    {
        if (completed.Result.Succeeded && completed.Result.ThumbnailPath is { } path)
            _browserGrid.ApplyThumbnail(completed.AssetId, path);
    }

    private async void BrowserGridRows_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.A && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            _browserGrid.SelectAll();
            UpdateBrowserStatusText();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Delete && _activeCollectionScope is not null && _browserGrid.SelectedKeys.Count > 0)
        {
            e.Handled = true;
            await RemoveBrowserSelectionFromActiveCollectionAsync();
            return;
        }
        // #110: Enter opens the single selected item — a conservative reading of "open" for a keyboard user;
        // opening a multi-selection is #111's filmstrip/review-set territory, deliberately not decided here.
        if (e.Key == Key.Enter && _browserGrid.SelectedKeys.Count == 1)
        {
            var tile = _browserGrid.Tiles.FirstOrDefault(t => _browserGrid.SelectedKeys.Contains(t.Key));
            if (tile is null) return;
            e.Handled = true;
            _ = OpenBrowserPlayerViewerAsync(tile);
        }
    }

    /// <summary>
    /// #110: opens one tile into the Browser's Player/Viewer presentation state. Resolves the tile's stable
    /// <c>RootId</c>+<c>RelativePath</c> identity to an absolute path through the same <see cref="IMediaRootService"/>
    /// every other Catalog-identity consumer already uses (no second path-resolution mechanism), then always
    /// switches presentation — an offline/missing file still opens into the Player/Viewer, which shows the
    /// resolution's own diagnostic and leaves Back/Esc available, rather than silently failing on the grid.
    /// Guarded by <see cref="_browserUiGeneration"/> — the same generation every other navigation-triggered
    /// update already checks — so a fast Locations-tree click landing while this is still awaiting
    /// <c>ResolveAsync</c> can never open a stale tile from the folder just left over the newly navigated one.
    /// </summary>
    private async Task OpenBrowserPlayerViewerAsync(BrowserGridTile tile)
    {
        var generation = _browserUiGeneration;
        var asset = new PlayerViewerAsset(tile.RootId, tile.RelativePath, tile.Key, tile.Name,
            MediaPresentationClassification.KindFor(tile.Category), tile.AssetId);
        MediaPathResolution resolution;
        // Unfiltered: this is a fire-and-forget UI entry point (invoked as `_ = OpenBrowserPlayerViewerAsync(tile)`
        // from the tile double-click/Enter handler), matching RunBrowserNavigationAsync's own catch-all
        // convention for the same reason — an unanticipated exception type here must still resolve to the
        // "file unavailable" diagnostic path rather than silently do nothing as an unobserved task fault.
        try { resolution = await _storage.MediaRoots.ResolveAsync(tile.RootId, tile.RelativePath).ConfigureAwait(true); }
        catch (Exception exception)
        {
            resolution = new(tile.RootId, tile.RelativePath, tile.Key, null, MediaRootAvailability.Unavailable, false, exception.Message);
        }
        if (generation != _browserUiGeneration) return;

        CaptureBrowserGridScrollOffset();
        EnsurePlayerViewerHost();
        SetBrowserPresentationMode(BrowserPresentationMode.PlayerViewer);
        SetSubclipsContextAvailable(asset.Kind == MediaPresentationKind.Video && asset.AssetId is not null);
        await _playerViewerHost!.OpenAsync(asset, resolution).ConfigureAwait(true);
    }

    private void EnsurePlayerViewerHost()
    {
        if (_playerViewerHost is not null) return;
        _playerViewerHost = new PlayerViewerHost(App.Playback, _storage.MediaRanges, _storage.Subclips,
            new FrameScreengrabService(() => _storage.Settings.ScreengrabDirectory),
            subclipPosters: _storage.CreateSubclipPosterService(),
            lutCache: _storage.LutCache,
            assetColors: _storage.AssetColors, cameraLutFolder: () => _storage.Settings.CameraLutFolder,
            creativeLutFolder: () => _storage.Settings.CreativeLutFolder,
            preferredPreviewFrames: _storage.PreferredPreviewFrames,
            classifications: _storage.AssetClassifications);
        _playerViewerHost.BackRequested += (_, _) => _ = ReturnToBrowserGridAsync();
        _playerViewerHost.ExportRequested += PlayerViewerHost_ExportRequested;
        _playerViewerHost.ExportSelectedSubclipsRequested += PlayerViewerHost_ExportSelectedSubclipsRequested;
        _playerViewerHost.SubclipsDrawerStateRequested += (_, request) =>
            SetRightDrawer(request.Open ? RightDrawerKind.Subclips : RightDrawerKind.None);
        _playerViewerHost.RangeStateChanged += (_, change) =>
            ApplyCommittedBrowserAssetStateFlag(change.AssetId, BrowserAssetState.ReviewRange, change.HasSavedRange);
        _playerViewerHost.ColorStateChanged += (_, change) =>
        {
            ApplyCommittedBrowserAssetStateFlag(change.AssetId, BrowserAssetState.Color, change.HasColor);
            _ = RegenerateColorThumbnailsAsync([change.AssetId]);
        };
        _playerViewerHost.SubclipStateChanged += (_, change) =>
            ApplyCommittedBrowserAssetStateFlag(change.AssetId, BrowserAssetState.Subclips, change.HasSubclips);
        _playerViewerHost.PreviewFrameIntentChanged += (_, change) =>
            _ = RegeneratePreferredFrameThumbnailAsync(change.AssetId);
        _playerViewerHost.ClassificationChanged += (_, change) =>
        {
            var revision = ++_browserAssetStateRevision;
            _browserAssetStateRevisions[change.AssetId] = revision;
            _browserGrid.ApplyClassification(change);
            _browserGrid.ReapplyQuery();
            UpdateBrowserStatusText();
        };
        BrowserPlayerHost.Content = _playerViewerHost;
    }

    private async Task RegeneratePreferredFrameThumbnailAsync(Guid assetId)
    {
        try
        {
            await _storage.RegenerateThumbnailsAsync([assetId],
                progress: new Progress<PreviewRegenerationCompleted>(ApplyCompletedPreview),
                mode: PreviewRegenerationMode.Force);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            BrowserStatusText.Text = $"Browser Preview regeneration could not complete: {exception.Message}";
        }
    }

    /// <summary>
    /// Publishes one already-committed durable property without replacing unrelated Browser state. Advancing
    /// the per-asset revision before applying the flag also prevents any older in-flight full projection
    /// from overwriting this newer truth when it completes.
    /// </summary>
    private void ApplyCommittedBrowserAssetStateFlag(Guid assetId, BrowserAssetState flag, bool enabled)
    {
        var revision = ++_browserAssetStateRevision;
        _browserAssetStateRevisions[assetId] = revision;
        _browserGrid.ApplyAssetStateFlag(assetId, flag, enabled);
        if (_browserGrid.Query.Filters.Any(filter => filter.Field is BrowserFilterField.ColorState or
            BrowserFilterField.CameraLutState or BrowserFilterField.CreativeLutState or
            BrowserFilterField.ReviewRangeState or BrowserFilterField.SubclipState))
        {
            _browserGrid.ReapplyQuery();
            UpdateBrowserStatusText();
        }
        _ = RefreshCommittedBrowserAssetQueryStateAsync(assetId, revision);
    }

    private async Task RefreshCommittedBrowserAssetQueryStateAsync(Guid assetId, long revision)
    {
        try
        {
            var states = await _storage.BrowserAssetStates.GetQueryStatesAsync([assetId]).ConfigureAwait(true);
            if (_browserAssetStateRevisions.GetValueOrDefault(assetId) != revision ||
                !states.TryGetValue(assetId, out var state)) return;
            _browserGrid.ApplyAssetState(assetId, state);
            if (_browserGrid.Query.Filters.Any(filter => filter.Field is BrowserFilterField.ColorState or
                BrowserFilterField.CameraLutState or BrowserFilterField.CreativeLutState or
                BrowserFilterField.ReviewRangeState or BrowserFilterField.SubclipState))
            {
                _browserGrid.ReapplyQuery();
                UpdateBrowserStatusText();
            }
        }
        catch (OperationCanceledException) { }
        catch (InvalidOperationException) { }
    }

    private void BrowserViewMode_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton { Tag: string value } && Enum.TryParse<BrowserViewMode>(value, out var mode))
            ApplyBrowserViewMode(mode, persist: true);
    }

    private void ApplyBrowserViewMode(BrowserViewMode mode, bool persist)
    {
        _browserGrid.SetViewMode(mode);
        BrowserPreviewViewButton.IsChecked = mode == BrowserViewMode.Preview;
        BrowserInfoViewButton.IsChecked = mode == BrowserViewMode.Info;
        BrowserHybridViewButton.IsChecked = mode == BrowserViewMode.Hybrid;
        if (!persist) return;
        _workspaceState.SetBrowserViewMode(mode);
        _workspaceSaveTimer.Stop();
        _workspaceSaveTimer.Start();
    }

    private void SetBrowserPresentationMode(BrowserPresentationMode mode)
    {
        _browserPresentation = mode;
        BrowserGridHost.Visibility = mode == BrowserPresentationMode.Grid ? Visibility.Visible : Visibility.Collapsed;
        BrowserPlayerHost.Visibility = mode == BrowserPresentationMode.PlayerViewer ? Visibility.Visible : Visibility.Collapsed;
        // #110: the query toolbar (Subfolders, All/Images/RAW/Video, Search, Filter, Sort) describes/manipulates
        // the Grid's own result set — irrelevant while reviewing one open asset, and hiding it is presentation
        // only: BrowserQueryToolbar's Grid row is Auto-height, so collapsing it reclaims its space automatically
        // without touching BrowserQuery/filter/sort/search state, which ApplyBrowserState continues to own
        // exactly as before. Restored (and IsEnabled re-evaluated by the same ApplyBrowserState path) the
        // instant Grid mode is reselected, so returning via Back/Esc shows it exactly as the user left it.
        BrowserQueryToolbar.Visibility = mode == BrowserPresentationMode.Grid ? Visibility.Visible : Visibility.Collapsed;
        BrowserNavigationToolbar.Visibility = mode == BrowserPresentationMode.Grid ? Visibility.Visible : Visibility.Collapsed;
        BrowserSelectionActionToolbar.Visibility = mode == BrowserPresentationMode.Grid ? Visibility.Visible : Visibility.Collapsed;
        BrowserNavigationGap.Height = mode == BrowserPresentationMode.Grid ? new GridLength(8) : new GridLength(0);
        // Presentation controls (thumbnail size) are Grid-specific; SyncBrowserStatusBarVisibility's own
        // Browser-tab-active condition already covers whether the whole trailing group shows at all.
        SyncBrowserStatusBarVisibility();
    }

    /// <summary>
    /// Returns to Grid presentation, releasing whatever the Player/Viewer had open. Idempotent — safe to call
    /// when already showing the grid. Presentation switches synchronously, <em>before</em> awaiting the actual
    /// close: the underlying <see cref="PlayerViewerHost.CloseAsync"/> teardown is real async backend/native
    /// work, and leaving the Player/Viewer (and any still-playing video/audio) visible until it settles would
    /// mean the user keeps seeing/hearing the old asset even after <see cref="ApplyBrowserState"/> has already
    /// repopulated the grid underneath it for a newly navigated folder. Switching first also means
    /// <c>_browserPresentation != PlayerViewer</c> alone is enough to guard re-entry — a second trigger arriving
    /// while the first's close is still pending sees Grid already active and no-ops, without needing a separate
    /// in-flight flag. A throw from the close itself is therefore no longer presentation-state-critical (Grid
    /// is already showing); it is still awaited so the caller's own fire-and-forget task fault (observed by
    /// <c>App</c>'s unobserved-task-exception logging) reflects a genuine teardown failure rather than being
    /// silently dropped. <paramref name="restoreScrollOffset"/>/<paramref name="focusGrid"/> are false only for
    /// <see cref="ApplyBrowserState"/>'s auto-return on a genuine scope change: the captured scroll offset
    /// belongs to the folder being left (applying it to the newly navigated folder's freshly populated grid
    /// would scroll to an arbitrary, unrelated position instead of the top), and focus already belongs to
    /// whatever the navigation that triggered this itself just focused (e.g. a Locations-tree row).
    /// </summary>
    private async Task ReturnToBrowserGridAsync(bool restoreScrollOffset = true, bool focusGrid = true)
    {
        if (_openRightDrawer == RightDrawerKind.Subclips) SetRightDrawer(RightDrawerKind.None);
        SetSubclipsContextAvailable(false);
        if (_browserPresentation != BrowserPresentationMode.PlayerViewer) return;
        var playerViewerHost = _playerViewerHost;
        SetBrowserPresentationMode(BrowserPresentationMode.Grid);
        if (restoreScrollOffset) RestoreBrowserGridScrollOffset();
        if (focusGrid) BrowserGridRows.Focus();
        if (playerViewerHost is not null) await playerViewerHost.CloseAsync().ConfigureAwait(true);
    }

    private void CaptureBrowserGridScrollOffset()
    {
        var scrollViewer = FindBrowserGridScrollViewer();
        if (scrollViewer is not null) _browserGridScrollOffset = scrollViewer.VerticalOffset;
    }

    private void RestoreBrowserGridScrollOffset()
    {
        var scrollViewer = FindBrowserGridScrollViewer();
        scrollViewer?.ScrollToVerticalOffset(_browserGridScrollOffset);
    }

    /// <summary>
    /// BrowserGridRows' ScrollViewer lives inside its own ControlTemplate (see MainWindow.xaml's
    /// BrowserGridScrollViewer), so it has no x:Name codegen field of its own — resolved once via the existing
    /// FindDescendantByName visual-tree walk (already used for the #124 tree-icon diagnostics above) and
    /// cached, rather than walked on every capture/restore.
    /// </summary>
    private ScrollViewer? FindBrowserGridScrollViewer() =>
        _browserGridScrollViewer ??= FindDescendantByName(BrowserGridRows, "BrowserGridScrollViewer") as ScrollViewer;

    /// <summary>
    /// Applies <paramref name="transform"/> to the grid's current query and keeps every toolbar visual
    /// (filter checkboxes, chip row, sort-direction glyph) in sync with the result — the single place that
    /// touches <see cref="BrowserGridModel.SetQuery"/> so those visuals can never drift from the model.
    /// </summary>
    private void ApplyBrowserQuery(Func<BrowserQuery, BrowserQuery> transform)
    {
        _browserGrid.SetQuery(transform(_browserGrid.Query));
        if (_lockedBrowserQuery is not null) _lockedBrowserQuery = _browserGrid.Query;
        SyncBrowserQueryToolbarVisuals();
        UpdateBrowserStatusText();
    }

    /// <summary>Returns the toolbar to its defaults for a newly opened scope, without re-triggering each control's own change handler.</summary>
    private void ResetBrowserQueryToolbar(BrowserSortMode sortMode = BrowserSortMode.Name)
    {
        var query = _lockedBrowserQuery is { } locked
            ? locked.SortMode == BrowserSortMode.Manual && sortMode != BrowserSortMode.Manual
                ? locked with { SortMode = BrowserSortMode.Name, SortDescending = false }
                : locked
            : BrowserQuery.Default with { SortMode = sortMode };
        _synchronizingBrowserQuery = true;
        try
        {
            _browserSearchDebounceTimer.Stop();
            BrowserSearchBox.Text = query.SearchText;
            ((ComboBoxItem)BrowserSortCombo.Items[(int)BrowserSortMode.Manual]).Visibility =
                sortMode == BrowserSortMode.Manual ? Visibility.Visible : Visibility.Collapsed;
            BrowserSortCombo.SelectedIndex = (int)query.SortMode;
            BrowserFilterButton.IsChecked = false;
        }
        finally { _synchronizingBrowserQuery = false; }
        _browserGrid.SetQuery(query);
        SyncBrowserQueryToolbarVisuals();
    }

    private void BrowserQueryLockButton_Click(object sender, RoutedEventArgs e)
    {
        _lockedBrowserQuery = BrowserQueryLockButton.IsChecked == true ? _browserGrid.Query : null;
        BrowserQueryLockButton.ToolTip = _lockedBrowserQuery is null
            ? "Keep the complete search, filters, and sort while changing folders or Collections"
            : "Browser query locked; click to restore normal per-scope reset behavior";
    }

    /// <summary>
    /// Appends one independent toggle per <see cref="BrowserGridModel.PresentableCategories"/> after the
    /// static "All" segment, so the row can never silently lack a button for a category the grid actually
    /// presents. Called once from the constructor — these controls live for the app's lifetime, unlike the
    /// per-folder state <see cref="ResetBrowserQueryToolbar"/> resets.
    /// </summary>
    private void InitializeBrowserQuickFilterButtons()
    {
        var categories = BrowserGridModel.PresentableCategories;
        for (var i = 0; i < categories.Count; i++)
        {
            var category = categories[i];
            var label = BrowserFilterPredicate.ForMediaType(category).Label;
            var button = new ToggleButton
            {
                Style = (Style)FindResource("BrowserQuickFilterSegmentStyle"),
                Content = label,
                Tag = category,
                // Every segment divides from its neighbor with a thin right border except the last, which
                // instead meets the shared chip's own rounded right edge — see BrowserQuickFilterSegmentStyle.
                BorderThickness = i == categories.Count - 1 ? new Thickness(0) : new Thickness(0, 0, 1, 0)
            };
            AutomationProperties.SetName(button, $"Toggle {label} media type");
            button.Click += BrowserQuickFilterCategoryButton_Click;
            _browserQuickFilterButtons[category] = button;
            BrowserQuickFilterSegments.Children.Add(button);
        }
    }

    /// <summary>Reflects the grid's current query onto every toolbar control that displays it, guarded so this never re-enters the controls' own change handlers.</summary>
    private void SyncBrowserQueryToolbarVisuals()
    {
        _synchronizingBrowserQuery = true;
        try
        {
            var filters = _browserGrid.Query.Filters;
            var activeMediaTypes = filters.Where(f => f.Field == BrowserFilterField.MediaType)
                .Select(f => f.MediaTypeValue).Where(value => value is not null).Select(value => value!.Value).ToHashSet();
            // The permanent media-type toggles already communicate that whole facet's state, so a Media
            // Type predicate never also produces a chip; the chip row exists only for predicates (future
            // fields) that have no permanent toolbar representation of their own.
            var advancedFilters = filters.Where(f => f.Field != BrowserFilterField.MediaType).ToArray();
            BrowserFilterChips.ItemsSource = advancedFilters;
            BrowserFilterChips.Visibility = advancedFilters.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            BrowserClearAdvancedFiltersButton.IsEnabled = advancedFilters.Length > 0;

            // Each media-type button is an independent toggle — multiple may be active at once, ORed
            // together. "All" reflects the neutral "no explicit predicate" state and is derived only from
            // there being zero active predicates, never inferred from every button happening to be checked:
            // "no filter" and "every type explicitly selected" produce the same visible tiles but must stay
            // two distinct, faithfully-preserved selections.
            BrowserQuickFilterAllButton.IsChecked = activeMediaTypes.Count == 0;
            foreach (var (category, button) in _browserQuickFilterButtons) button.IsChecked = activeMediaTypes.Contains(category);

            UpdateBrowserSortDirectionGlyph();
        }
        finally { _synchronizingBrowserQuery = false; }
    }

    private void UpdateBrowserSortDirectionGlyph()
    {
        var descending = _browserGrid.Query.SortDescending;
        BrowserSortDirectionButton.IsEnabled = _browserGrid.Query.SortMode != BrowserSortMode.Manual;
        BrowserSortDirectionButton.Content = descending ? "\uE70D" : "\uE70E";
        BrowserSortDirectionButton.ToolTip = descending
            ? "Sort descending (click to reverse)" : "Sort ascending (click to reverse)";
    }

    private void BrowserSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_synchronizingBrowserQuery) return;
        _browserSearchDebounceTimer.Stop();
        _browserSearchDebounceTimer.Start();
    }

    private void BrowserSortCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_synchronizingBrowserQuery) return;
        ApplyBrowserQuery(query => query with { SortMode = (BrowserSortMode)BrowserSortCombo.SelectedIndex });
    }

    private void BrowserSortDirection_Click(object sender, RoutedEventArgs e) =>
        ApplyBrowserQuery(query => query with { SortDescending = !query.SortDescending });

    private void BrowserFilterPopup_Opened(object? sender, EventArgs e) => RefreshBrowserAdvancedFilterOptions();

    private void RefreshBrowserAdvancedFilterOptions()
    {
        var tiles = _browserGrid.AdvancedFilterContextTiles;
        var cameraOptions = Options(tiles.Select(tile => tile.CameraDisplayName)
            .Where(value => value is not null).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .Select(value => BrowserFilterPredicate.ForText(BrowserFilterField.Camera, value!)));
        BrowserCameraFilterOptions.ItemsSource = cameraOptions;
        PresentDescriptiveFacet(BrowserCameraFilterGroup, BrowserCameraFilterOptions, BrowserCameraFilterInformation,
            cameraOptions, "Camera", tiles.Count(tile => tile.CameraDisplayName is not null), tiles.Count);

        var lensOptions = Options(tiles.Select(tile => tile.LensModel)
            .Where(value => value is not null).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .Select(value => BrowserFilterPredicate.ForText(BrowserFilterField.Lens, value!)));
        BrowserLensFilterOptions.ItemsSource = lensOptions;
        PresentDescriptiveFacet(BrowserLensFilterGroup, BrowserLensFilterOptions, BrowserLensFilterInformation,
            lensOptions, "Lens", tiles.Count(tile => tile.LensModel is not null), tiles.Count);

        BrowserCaptureDateFilterGroup.Visibility = tiles.Any(tile => tile.MetadataApplied && tile.CaptureDate is not null)
            ? Visibility.Visible : Visibility.Collapsed;

        var durationValues = tiles.Where(tile => tile.MetadataApplied && tile.DurationSeconds is > 0)
            .Select(tile => tile.DurationSeconds!.Value).ToArray();
        var durationOptions = Options(new[] { 10d, 30d, 60d, 300d }
            .Where(threshold => durationValues.Any(value => value >= threshold) && durationValues.Any(value => value < threshold))
            .Select(threshold => BrowserFilterPredicate.ForMinimum(BrowserFilterField.Duration, threshold)));
        BrowserDurationFilterCombo.ItemsSource = durationOptions;
        BrowserDurationFilterCombo.SelectedIndex = durationOptions.Count > 0 ? 0 : -1;
        BrowserDurationFilterGroup.Visibility = durationOptions.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        var resolutionOptions = Options(tiles
            .Where(tile => tile.PixelWidth is > 0 && tile.PixelHeight is > 0)
            .Select(tile => (Width: tile.PixelWidth!.Value, Height: tile.PixelHeight!.Value)).Distinct()
            .OrderBy(size => size.Width * (long)size.Height)
            .Select(size => BrowserFilterPredicate.ForResolution(size.Width, size.Height)));
        BrowserResolutionFilterOptions.ItemsSource = resolutionOptions;
        PresentDescriptiveFacet(BrowserResolutionFilterGroup, BrowserResolutionFilterOptions, BrowserResolutionFilterInformation,
            resolutionOptions, "Resolution", tiles.Count(tile => tile.PixelWidth is > 0 && tile.PixelHeight is > 0), tiles.Count);

        var frameRateOptions = Options(tiles.Select(tile => BrowserFrameRate.Canonicalize(tile.FrameRate))
            .Where(value => value is not null).Select(value => value!.Value).Distinct().OrderBy(value => value)
            .Select(BrowserFilterPredicate.ForFrameRate));
        BrowserFrameRateFilterOptions.ItemsSource = frameRateOptions;
        PresentDescriptiveFacet(BrowserFrameRateFilterGroup, BrowserFrameRateFilterOptions, BrowserFrameRateFilterInformation,
            frameRateOptions, "Frame rate", tiles.Count(tile => tile.FrameRate is > 0), tiles.Count);

        var hydratedStateTiles = tiles.Where(tile => tile.AssetStateApplied).ToArray();
        var stateOptions = new[]
        {
            StateOption(BrowserFilterField.ColorState, true, hydratedStateTiles.Count(tile => tile.HasColorState)),
            StateOption(BrowserFilterField.ColorState, false, hydratedStateTiles.Count(tile => !tile.HasColorState)),
            StateOption(BrowserFilterField.CameraLutState, true, hydratedStateTiles.Count(tile => tile.HasCameraLut)),
            StateOption(BrowserFilterField.CreativeLutState, true, hydratedStateTiles.Count(tile => tile.HasCreativeLut)),
            StateOption(BrowserFilterField.ReviewRangeState, true, hydratedStateTiles.Count(tile => tile.HasReviewRange)),
            StateOption(BrowserFilterField.ReviewRangeState, false, hydratedStateTiles.Count(tile => !tile.HasReviewRange)),
            StateOption(BrowserFilterField.SubclipState, true, hydratedStateTiles.Count(tile => tile.HasSubclips)),
            StateOption(BrowserFilterField.SubclipState, false, hydratedStateTiles.Count(tile => !tile.HasSubclips))
        };
        BrowserStateFilterOptions.ItemsSource = stateOptions;
        BrowserStateFilterGroup.Visibility = Visibility.Visible;

        var classificationOptions = Options(
            new[] { BrowserFilterPredicate.ForMinimum(BrowserFilterField.Rating, 0) }
                .Concat(Enumerable.Range(1, 5).Select(value => BrowserFilterPredicate.ForMinimum(BrowserFilterField.Rating, value)))
                .Concat(Enum.GetValues<AssetFlag>().Select(value => BrowserFilterPredicate.ForText(BrowserFilterField.Flag, value.ToString())))
                .Concat(Enum.GetValues<AssetColorLabel>().Select(value => BrowserFilterPredicate.ForText(BrowserFilterField.ColorLabel, value.ToString())))
                .Concat(hydratedStateTiles.SelectMany(tile => tile.Keywords).Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                    .Select(value => BrowserFilterPredicate.ForText(BrowserFilterField.Keyword, value))));
        BrowserClassificationFilterOptions.ItemsSource = classificationOptions;
        BrowserClassificationFilterGroup.Visibility = Visibility.Visible;
    }

    private static void PresentDescriptiveFacet(StackPanel group, ItemsControl choices, TextBlock information,
        IReadOnlyList<BrowserFilterOption> options, string fieldLabel, int knownCount, int contextualCount)
    {
        group.Visibility = options.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        choices.Visibility = options.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        information.Visibility = options.Count == 1 ? Visibility.Visible : Visibility.Collapsed;
        if (options.Count != 1) return;

        var coverage = knownCount == contextualCount
            ? $"all {knownCount} {PluralizeItems(knownCount)}"
            : $"{knownCount} known {PluralizeItems(knownCount)}";
        information.Text = $"{fieldLabel} {options[0].DescriptiveValueLabel} · {coverage}";
    }

    private static string PluralizeItems(int count) => count == 1 ? "item" : "items";

    private BrowserFilterOption StateOption(BrowserFilterField field, bool value, int count)
    {
        var predicate = BrowserFilterPredicate.ForState(field, value);
        var active = _browserGrid.Query.Filters.Contains(predicate);
        return new BrowserFilterOption(predicate, active, count > 0 || active, count);
    }

    private IReadOnlyList<BrowserFilterOption> Options(IEnumerable<BrowserFilterPredicate> predicates) =>
        predicates.Select(predicate => new BrowserFilterOption(predicate, _browserGrid.Query.Filters.Contains(predicate))).ToArray();

    private void BrowserAdvancedFilterCheck_Click(object sender, RoutedEventArgs e)
    {
        if (_synchronizingBrowserQuery || ((FrameworkElement)sender).DataContext is not BrowserFilterOption option) return;
        var enabled = ((System.Windows.Controls.CheckBox)sender).IsChecked == true;
        ApplyBrowserQuery(query => enabled ? query.WithFilterAdded(option.Predicate) : query.WithFilterRemoved(option.Predicate));
    }

    private void BrowserAddCaptureDateFilter_Click(object sender, RoutedEventArgs e)
    {
        var from = BrowserCaptureDateFrom.SelectedDate;
        var to = BrowserCaptureDateTo.SelectedDate;
        if (from is null && to is null) return;
        if (from is { } start && to is { } end && start.Date > end.Date) (from, to) = (to, from);
        ApplyBrowserQuery(query => query.WithFilterAdded(BrowserFilterPredicate.ForDateRange(from, to)));
    }

    private void BrowserAddDurationFilter_Click(object sender, RoutedEventArgs e)
    {
        if (BrowserDurationFilterCombo.SelectedItem is not BrowserFilterOption option) return;
        ApplyBrowserQuery(query => query.WithFilterAdded(option.Predicate));
    }

    private void BrowserClearAdvancedFilters_Click(object sender, RoutedEventArgs e) =>
        ApplyBrowserQuery(query => query with
        {
            Filters = query.Filters.Where(filter => filter.Field == BrowserFilterField.MediaType).ToArray()
        });

    private void ToggleBrowserMediaTypeFilter(MediaTypeCategory category, bool isActive)
    {
        if (_synchronizingBrowserQuery) return;
        var predicate = BrowserFilterPredicate.ForMediaType(category);
        ApplyBrowserQuery(query => isActive ? query.WithFilterAdded(predicate) : query.WithFilterRemoved(predicate));
    }

    private void BrowserFilterChip_Remove_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not BrowserFilterPredicate predicate) return;
        ApplyBrowserQuery(query => query.WithFilterRemoved(predicate));
    }

    /// <summary>"All": clears the media-type facet entirely rather than picking a value for it.</summary>
    private void BrowserQuickFilterAllButton_Click(object sender, RoutedEventArgs e)
    {
        if (_synchronizingBrowserQuery) return;
        ApplyBrowserQuery(query => query.WithoutField(BrowserFilterField.MediaType));
    }

    /// <summary>Shared by every dynamically-created quick-filter segment (see InitializeBrowserQuickFilterButtons):
    /// each is an independent toggle for its own category, routing through the same guarded helper the Filter ▾
    /// checkboxes use so both stay mutually consistent.</summary>
    private void BrowserQuickFilterCategoryButton_Click(object sender, RoutedEventArgs e)
    {
        if (_synchronizingBrowserQuery) return;
        var button = (ToggleButton)sender;
        ToggleBrowserMediaTypeFilter((MediaTypeCategory)button.Tag, button.IsChecked == true);
    }

    /// <summary>Ctrl+F focuses the Browser search box, but only while the Browser workspace is showing an open, filterable location.</summary>
    private void MainWindow_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (PlayerOwnsShortcutContext() && _playerViewerHost!.TryHandleShortcut(
                e.Key, e.OriginalSource as DependencyObject))
        {
            e.Handled = true;
            return;
        }
        var inputOwner = e.OriginalSource as DependencyObject;
        if (_browserPresentation == BrowserPresentationMode.Grid &&
            MainTabs.SelectedIndex == ShellDestinationSelection.Index(ShellDestination.Home) &&
            !PlayerViewerHost.IsTextEntryControl(inputOwner))
        {
            if (e.Key >= Key.D0 && e.Key <= Key.D5)
            {
                _ = SetSelectedBrowserRatingsAsync(e.Key - Key.D0);
                e.Handled = true;
                return;
            }
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key is Key.Up or Key.Down)
            {
                _ = StepSelectedBrowserFlagsAsync(e.Key == Key.Up ? 1 : -1);
                e.Handled = true;
                return;
            }
        }
        if (e.Key == Key.Escape && MainTabs.SelectedIndex == ShellDestinationSelection.Index(ShellDestination.Jobs)
            && Keyboard.FocusedElement is not System.Windows.Controls.Primitives.TextBoxBase
            && Keyboard.FocusedElement is not System.Windows.Controls.ComboBox)
        {
            MainTabs.SelectedIndex = ShellDestinationSelection.Index(ShellDestination.Home);
            e.Handled = true;
            return;
        }
        if (e.Key != Key.F || !Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) return;
        if (MainTabs.SelectedIndex != ShellDestinationSelection.Index(ShellDestination.Home) || !BrowserQueryToolbar.IsEnabled) return;
        BrowserSearchBox.Focus();
        BrowserSearchBox.SelectAll();
        e.Handled = true;
    }

    private void MainWindow_PreviewKeyUp(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (!PlayerOwnsShortcutContext() || !_playerViewerHost!.TryHandleShortcutKeyUp(e.Key)) return;
        e.Handled = true;
    }

    private bool PlayerOwnsShortcutContext() =>
        MainTabs.SelectedIndex == ShellDestinationSelection.Index(ShellDestination.Home) &&
        _browserPresentation == BrowserPresentationMode.PlayerViewer && _playerViewerHost is not null;

    private async Task SetSelectedBrowserRatingsAsync(int rating)
    {
        foreach (var tile in _browserGrid.SelectedTilesInBrowserOrder.Where(tile => tile.AssetId is not null))
        {
            var value = tile.Classification ?? AssetClassification.Empty(tile.AssetId!.Value);
            await CommitBrowserClassificationAsync(value with { Rating = rating }).ConfigureAwait(true);
        }
    }

    private void BrowserRating_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string text } || !int.TryParse(text, out var rating)) return;
        _ = SetSelectedBrowserRatingsFromMenuAsync(rating);
    }

    private async Task SetSelectedBrowserRatingsFromMenuAsync(int rating)
    {
        foreach (var tile in _browserGrid.SelectedTilesInBrowserOrder.Where(tile => tile.AssetId is not null))
        {
            var value = tile.Classification ?? AssetClassification.Empty(tile.AssetId!.Value);
            await CommitBrowserClassificationAsync(value with { Rating = value.Rating == rating ? 0 : rating }).ConfigureAwait(true);
        }
    }

    private void BrowserFlag_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string text } || !Enum.TryParse<AssetFlag>(text, out var flag)) return;
        _ = SetSelectedBrowserFlagsAsync(flag);
    }

    private async Task SetSelectedBrowserFlagsAsync(AssetFlag flag)
    {
        foreach (var tile in _browserGrid.SelectedTilesInBrowserOrder.Where(tile => tile.AssetId is not null))
        {
            var value = tile.Classification ?? AssetClassification.Empty(tile.AssetId!.Value);
            await CommitBrowserClassificationAsync(value with { Flag = flag }).ConfigureAwait(true);
        }
    }

    private void BrowserColorLabel_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string text }) return;
        AssetColorLabel? label = text == "None" ? null : Enum.TryParse<AssetColorLabel>(text, out var parsed) ? parsed : null;
        _ = SetSelectedBrowserColorLabelsAsync(label);
    }

    private async Task SetSelectedBrowserColorLabelsAsync(AssetColorLabel? label)
    {
        foreach (var tile in _browserGrid.SelectedTilesInBrowserOrder.Where(tile => tile.AssetId is not null))
        {
            var value = tile.Classification ?? AssetClassification.Empty(tile.AssetId!.Value);
            await CommitBrowserClassificationAsync(value with { ColorLabel = label }).ConfigureAwait(true);
        }
    }

    private void BrowserAddKeyword_Click(object sender, RoutedEventArgs e)
    {
        var keyword = Microsoft.VisualBasic.Interaction.InputBox("Keyword to add to the selected Media:",
            "Add keyword", "").Trim();
        if (keyword.Length == 0) return;
        _ = AddSelectedBrowserKeywordAsync(keyword);
    }

    private async Task AddSelectedBrowserKeywordAsync(string keyword)
    {
        foreach (var tile in _browserGrid.SelectedTilesInBrowserOrder.Where(tile => tile.AssetId is not null))
        {
            var value = tile.Classification ?? AssetClassification.Empty(tile.AssetId!.Value);
            await CommitBrowserClassificationAsync(value with { Keywords = [.. value.Keywords, keyword] }).ConfigureAwait(true);
        }
    }

    private void BrowserClearKeywords_Click(object sender, RoutedEventArgs e) => _ = ClearSelectedBrowserKeywordsAsync();

    private async Task ClearSelectedBrowserKeywordsAsync()
    {
        foreach (var tile in _browserGrid.SelectedTilesInBrowserOrder.Where(tile => tile.AssetId is not null))
        {
            var value = tile.Classification ?? AssetClassification.Empty(tile.AssetId!.Value);
            await CommitBrowserClassificationAsync(value with { Keywords = [] }).ConfigureAwait(true);
        }
    }

    private async Task StepSelectedBrowserFlagsAsync(int delta)
    {
        foreach (var tile in _browserGrid.SelectedTilesInBrowserOrder.Where(tile => tile.AssetId is not null))
        {
            var value = tile.Classification ?? AssetClassification.Empty(tile.AssetId!.Value);
            await CommitBrowserClassificationAsync(value with
                { Flag = (AssetFlag)Math.Clamp((int)value.Flag + delta, -1, 1) }).ConfigureAwait(true);
        }
    }

    private async Task CommitBrowserClassificationAsync(AssetClassification value)
    {
        await _storage.AssetClassifications.SaveAsync(value).ConfigureAwait(true);
        var revision = ++_browserAssetStateRevision;
        _browserAssetStateRevisions[value.AssetId] = revision;
        _browserGrid.ApplyClassification(value);
        _browserGrid.ReapplyQuery();
        UpdateBrowserStatusText();
    }

    /// <summary>
    /// Lightweight Browser status: visible/total item counts, selection count/size (scoped to the whole
    /// selection, not just what the current filter shows — see <see cref="BrowserGridModel.SelectedTotalSizeBytes"/>),
    /// and whether Preview generation is still active for this folder. Always kept current regardless of
    /// which tab is active — see <see cref="SyncBrowserStatusBarVisibility"/> for when it's actually shown.
    /// </summary>
    private void UpdateBrowserStatusText()
    {
        var progress = _activeBrowserDerivedWorkBatch?.Progress;
        var isGenerating = progress?.Status == DerivedWorkBatchStatus.Running;
        var remaining = progress is null ? 0 : progress.Pending + progress.Running;
        BrowserStatusText.Text = BrowserStatusPresentation.Describe(_browserGrid.VisibleCount, _browserGrid.TotalCount,
            _browserGrid.SelectedKeys.Count, _browserGrid.SelectedTotalSizeBytes, isGenerating, remaining);
        if (_activeCollectionScope is { UnavailableCount: > 0 } scope)
            BrowserStatusText.Text += $" • {scope.UnavailableCount} unavailable";
        UpdateBrowserSelectionActions();
    }

    private async void PlayerViewerHost_ExportRequested(object? sender, PlayerViewerExportRequestedEventArgs e)
    {
        try { await ExportBrowserAssetsAsync([e.AssetId]); }
        finally
        {
            if (_playerViewerHost?.CurrentAsset?.AssetId == e.AssetId)
                _playerViewerHost.SetExportEnabled(true);
        }
    }

    private async void PlayerViewerHost_ExportSelectedSubclipsRequested(object? sender,
        PlayerViewerSubclipsExportRequestedEventArgs e)
    {
        try
        {
            await ApplySubclipExportHandoffAsync(new(SubclipExportEntryKind.PlayerSelection, [e.AssetId],
                e.SelectedSubclipIds));
        }
        finally
        {
            if (_playerViewerHost?.CurrentAsset?.AssetId == e.AssetId)
                _playerViewerHost.SetSelectedSubclipExportEnabled(
                    _playerViewerHost.SelectedSubclipIds.Count > 0);
        }
    }

    private BrowserSelectionActionState CurrentBrowserSelectionActions() =>
        BrowserSelectionActions.Evaluate(_browserGrid.SelectedTilesInBrowserOrder);

    private void UpdateBrowserSelectionActions()
    {
        if (BrowserExportButton is null) return;
        var state = CurrentBrowserSelectionActions();
        BrowserExportButton.IsEnabled = state.CanExport;
        BrowserRegenerateThumbnailsButton.IsEnabled = state.CanRegenerateThumbnails ||
            state.SelectionCount == 0 && _browserGrid.ThumbnailApplicableAssetIdsInScope.Count > 0;
        var regenerateLabel = BrowserThumbnailRegeneration.ProductLabel(
            state.SelectionCount, state.CanRegenerateThumbnails);
        BrowserRegenerateThumbnailsButton.ToolTip = regenerateLabel;
        AutomationProperties.SetName(BrowserRegenerateThumbnailsButton, regenerateLabel);
        BrowserCameraLutCombo.IsEnabled = state.CanAssignCameraLut;
        BrowserCreativeLutCombo.IsEnabled = state.CanAssignCreativeLut;
        _ = RefreshBrowserColorSelectorsAsync();
    }

    private async Task RefreshBrowserColorSelectorsAsync()
    {
        var revision = ++_browserColorSelectionRevision;
        var ids = _browserGrid.SelectedAssetIdsInBrowserOrder.ToArray();
        var state = CurrentBrowserSelectionActions();
        if (!state.CanAssignCameraLut || ids.Length != state.SelectionCount)
        {
            SetBrowserColorSelectorsUnavailable();
            return;
        }
        BrowserCameraLutCombo.IsEnabled = BrowserCreativeLutCombo.IsEnabled = false;
        try
        {
            var colorsTask = _storage.AssetColors.GetAsync(ids);
            var resolutionTasks = ids.Select(id => _storage.MediaAssets.GetAsync(id)).ToArray();
            await Task.WhenAll(resolutionTasks).ConfigureAwait(true);
            var colors = await colorsTask.ConfigureAwait(true);
            if (revision != _browserColorSelectionRevision || !ids.SequenceEqual(_browserGrid.SelectedAssetIdsInBrowserOrder)) return;
            var availability = resolutionTasks.Select(task => task.Result is
                { SourceExists: true, RootAvailability: MediaRootAvailability.Online }).ToArray();
            if (!BrowserSelectionActions.CanAssignLutColor(state, availability))
            {
                SetBrowserColorSelectorsUnavailable();
                return;
            }
            var intents = ids.Select(id => colors[id]).ToArray();
            _updatingBrowserColorSelectors = true;
            ApplyBrowserLutPresentation(BrowserCameraLutCombo, BrowserLutActionPicker.Present(ColorLutStage.Camera,
                _storage.LutCache.Snapshot(ColorLutStage.Camera).Resources, intents));
            ApplyBrowserLutPresentation(BrowserCreativeLutCombo, BrowserLutActionPicker.Present(ColorLutStage.Creative,
                _storage.LutCache.Snapshot(ColorLutStage.Creative).Resources, intents));
            _updatingBrowserColorSelectors = false;
            BrowserCameraLutCombo.IsEnabled = BrowserCreativeLutCombo.IsEnabled = true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or KeyNotFoundException or SqliteException)
        {
            if (revision == _browserColorSelectionRevision) SetBrowserColorSelectorsUnavailable();
        }
        finally { _updatingBrowserColorSelectors = false; }
    }

    private void SetBrowserColorSelectorsUnavailable()
    {
        _updatingBrowserColorSelectors = true;
        ApplyBrowserLutPresentation(BrowserCameraLutCombo,
            BrowserLutActionPicker.Present(ColorLutStage.Camera, [], []));
        ApplyBrowserLutPresentation(BrowserCreativeLutCombo,
            BrowserLutActionPicker.Present(ColorLutStage.Creative, [], []));
        _updatingBrowserColorSelectors = false;
        BrowserCameraLutCombo.IsEnabled = BrowserCreativeLutCombo.IsEnabled = false;
    }

    private void BrowserWorkspaceRoot_SizeChanged(object sender, SizeChangedEventArgs e) =>
        ApplyBrowserResponsiveLayout();

    private void BrowserNavigationSplitter_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (_applyingBrowserResponsiveLayout) return;
        _browserLocationsPreferredWidth = Math.Clamp(BrowserNavigationColumn.ActualWidth,
            WorkspaceState.MinLocationsPaneWidth, WorkspaceState.MaxLocationsPaneWidth);
        ApplyBrowserResponsiveLayout();
    }

    private void ApplyBrowserResponsiveLayout()
    {
        if (BrowserWorkspaceRoot is null || BrowserWorkspaceRoot.ActualWidth <= 0 || _applyingBrowserResponsiveLayout) return;
        _applyingBrowserResponsiveLayout = true;
        try
        {
            const double temporaryMinimumLocationsWidth = 140;
            const double minimumUsefulCenterWidth = 220;
            var maximumLocationsWidth = Math.Max(temporaryMinimumLocationsWidth,
                BrowserWorkspaceRoot.ActualWidth - BrowserNavigationSplitter.ActualWidth - minimumUsefulCenterWidth);
            var constrained = maximumLocationsWidth < WorkspaceState.MinLocationsPaneWidth;
            BrowserNavigationColumn.MinWidth = constrained
                ? temporaryMinimumLocationsWidth
                : WorkspaceState.MinLocationsPaneWidth;
            var effectiveLocationsWidth = Math.Min(_browserLocationsPreferredWidth, maximumLocationsWidth);
            effectiveLocationsWidth = Math.Max(BrowserNavigationColumn.MinWidth, effectiveLocationsWidth);
            if (Math.Abs(BrowserNavigationColumn.Width.Value - effectiveLocationsWidth) > 0.5)
                BrowserNavigationColumn.Width = new GridLength(effectiveLocationsWidth);

            var centerWidth = Math.Max(0, BrowserWorkspaceRoot.ActualWidth - effectiveLocationsWidth - BrowserNavigationSplitter.ActualWidth);
            var compactLocationChrome = centerWidth < 380;
            BrowserBackButton.Width = compactLocationChrome ? 26 : 38;
            BrowserForwardButton.Width = compactLocationChrome ? 26 : 38;
            BrowserUpButton.Width = compactLocationChrome ? 26 : 38;
            BrowserRefreshButton.Width = compactLocationChrome ? 26 : 38;
            BrowserGoButton.Padding = compactLocationChrome ? new Thickness(3, 7, 3, 7) : new Thickness(12, 7, 12, 7);
            BrowserCurrentPath.Margin = compactLocationChrome ? new Thickness(1, 0, 1, 0) : new Thickness(14, 0, 6, 0);
            BrowserScopeGapColumn.Width = new GridLength(compactLocationChrome ? 4 : 16);
            BrowserIncludeSubfoldersButton.Padding = compactLocationChrome ? new Thickness(3, 7, 3, 7) : new Thickness(10, 7, 10, 7);
            BrowserIncludeSubfoldersButton.Tag = compactLocationChrome ? "Compact" : null;
            Grid.SetRow(BrowserIncludeSubfoldersButton, 0);
            Grid.SetColumn(BrowserIncludeSubfoldersButton, 5);
            Grid.SetColumnSpan(BrowserIncludeSubfoldersButton, 1);
            BrowserIncludeSubfoldersButton.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
            BrowserIncludeSubfoldersButton.Margin = new Thickness(0);

            const double combinedLowerControlsBreakpoint = 1120;
            var stackLowerControls = centerWidth < combinedLowerControlsBreakpoint;
            Grid.SetRow(BrowserQueryToolbar, 2);
            Grid.SetColumn(BrowserQueryToolbar, 0);
            Grid.SetColumnSpan(BrowserQueryToolbar, stackLowerControls ? 2 : 1);
            Grid.SetRow(BrowserSelectionActionToolbar, stackLowerControls ? 4 : 2);
            Grid.SetColumn(BrowserSelectionActionToolbar, stackLowerControls ? 0 : 1);
            Grid.SetColumnSpan(BrowserSelectionActionToolbar, stackLowerControls ? 2 : 1);
            BrowserSelectionActionToolbar.Margin = stackLowerControls
                ? new Thickness(0)
                : new Thickness(8, 0, 0, 0);

            BrowserColorActions.Orientation = System.Windows.Controls.Orientation.Horizontal;
            Grid.SetRow(BrowserColorActions, 0);
            Grid.SetColumn(BrowserColorActions, 0);
            Grid.SetColumnSpan(BrowserColorActions, 1);
            Grid.SetRow(BrowserExportButton, 0);
            Grid.SetColumn(BrowserExportButton, 1);
            Grid.SetColumnSpan(BrowserExportButton, 1);
            BrowserExportButton.HorizontalAlignment = System.Windows.HorizontalAlignment.Right;
            BrowserExportButton.Margin = new Thickness(12, 0, 4, 0);
            var lutWidth = centerWidth < 420
                ? Math.Clamp((centerWidth - 120) / 2, 40, 150)
                : 150;
            BrowserCameraLutCombo.Width = lutWidth;
            BrowserCreativeLutCombo.Width = lutWidth;
        }
        finally { _applyingBrowserResponsiveLayout = false; }
    }

    private void MainTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.Source, MainTabs)) return;
        SyncBrowserStatusBarVisibility();
        UpdateSubclipsPullVisibility();
        // #110: switching to another workspace while a video is open in the Player/Viewer must not leave it
        // silently playing audio in a hidden tab. This pauses rather than returning to Grid — switching tabs
        // is not "leaving" the Browser, so the open asset and its position stay exactly as the user left them.
        if (MainTabs.SelectedIndex != ShellDestinationSelection.Index(ShellDestination.Home) &&
            _browserPresentation == BrowserPresentationMode.PlayerViewer && _playerViewerHost is not null)
        {
            if (_openRightDrawer == RightDrawerKind.Subclips) SetRightDrawer(RightDrawerKind.None);
            _ = _playerViewerHost.PauseIfPlayingAsync();
        }
    }

    private void ApplicationMenu_Click(object sender, RoutedEventArgs e)
    {
        ApplicationMenu.PlacementTarget = ApplicationMenuButton;
        ApplicationMenu.IsOpen = true;
    }

    private void OpenSettings_Click(object sender, RoutedEventArgs e) =>
        MainTabs.SelectedIndex = ShellDestinationSelection.Index(ShellDestination.Settings);

    private void SettingsCategoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsInitialized || SettingsGeneralPage is null) return;
        var category = (SettingsCategoryList.SelectedItem as ListBoxItem)?.Tag as string ?? "General";
        SettingsGeneralPage.Visibility = category == "General" ? Visibility.Visible : Visibility.Collapsed;
        SettingsColorPage.Visibility = category == "Color" ? Visibility.Visible : Visibility.Collapsed;
        SettingsExportPage.Visibility = category == "Export" ? Visibility.Visible : Visibility.Collapsed;
        SettingsStoragePage.Visibility = category == "Storage" ? Visibility.Visible : Visibility.Collapsed;
        SettingsToolsPage.Visibility = category == "Tools" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OpenAbout_Click(object sender, RoutedEventArgs e) =>
        MainTabs.SelectedIndex = ShellDestinationSelection.Index(ShellDestination.About);

    private void UtilityBackToBrowser_Click(object sender, RoutedEventArgs e) =>
        MainTabs.SelectedIndex = ShellDestinationSelection.Index(ShellDestination.Home);

    private void CompatibilityReviewBack_Click(object sender, RoutedEventArgs e) =>
        MainTabs.SelectedIndex = ShellDestinationSelection.Index(ShellDestination.Jobs);

    /// <summary>
    /// #126: one application-wide status strip rather than a Browser-specific bar stacked above an unrelated
    /// global one. The Browser segment (count/selection/Preview activity, plus the reserved slot for #125's
    /// thumbnail control) is part of that same strip and only shown while the Browser tab is active; app
    /// health (<see cref="StatusText"/>) is unaffected and always visible regardless of tab. Guarded with a
    /// null check, not the usual <c>_synchronizingBrowserQuery</c> flag, because <c>SelectedIndex="0"</c> is
    /// a XAML-declared default on <see cref="MainTabs"/> — WPF can raise <c>SelectionChanged</c> for it while
    /// InitializeComponent is still connecting these later-declared elements, so the explicit call after
    /// InitializeComponent in the constructor is what actually establishes the correct initial state.
    /// </summary>
    private void SyncBrowserStatusBarVisibility()
    {
        if (BrowserStatusText is null) return;
        var isBrowserActive = MainTabs.SelectedIndex == ShellDestinationSelection.Index(ShellDestination.Home);
        var visibility = isBrowserActive ? Visibility.Visible : Visibility.Collapsed;
        BrowserStatusText.Visibility = visibility;
        BrowserStatusDivider.Visibility = visibility;
        // #110: thumbnail size only applies to Grid presentation — hidden while the Player/Viewer is showing,
        // exactly as it is already hidden outside the Browser tab entirely.
        BrowserPresentationControls.Visibility = isBrowserActive && _browserPresentation == BrowserPresentationMode.Grid
            ? Visibility.Visible : Visibility.Collapsed;
        if (isBrowserActive) UpdateBrowserStatusText();
    }

    private void AttachBrowserDerivedWork(IDerivedWorkBatch? batch, long generation)
    {
        // Assigned unconditionally (including null) so a folder with nothing scheduled never keeps showing
        // "Generating previews…" left over from whichever folder was open before it.
        _activeBrowserDerivedWorkBatch = batch;
        if (batch is null) return;
        EventHandler<DerivedWorkProgress> handler = null!;
        handler = (_, _) => Dispatcher.BeginInvoke(() => _ = ApplyBrowserDerivedWorkResultsAsync(batch, generation));
        batch.ProgressChanged += handler;
        _ = ApplyBrowserDerivedWorkResultsAsync(batch, generation);
        _ = batch.Completion.ContinueWith(_ => batch.ProgressChanged -= handler, TaskScheduler.Default);
    }

    private async Task ApplyBrowserDerivedWorkResultsAsync(IDerivedWorkBatch batch, long generation)
    {
        if (generation != _browserUiGeneration || _storage.Previews is not { } previews) return;
        var pendingThumbnails = new HashSet<Guid>(
            BrowserDerivedWorkProjection.AssetsNeedingThumbnailLookup(batch.Results, _browserGrid.HasThumbnail));
        var pendingMetadata = new HashSet<Guid>(
            BrowserDerivedWorkProjection.AssetsNeedingMetadataLookup(batch.Results, _browserGrid.HasMetadataApplied));
        var sortRelevantMetadataChanged = false;

        IReadOnlyDictionary<Guid, PreviewRecord> records;
        try { records = await previews.GetManyAsync(pendingThumbnails.Union(pendingMetadata).ToArray()).ConfigureAwait(true); }
        catch { return; }

        foreach (var assetId in pendingThumbnails.Union(pendingMetadata))
        {
            if (generation != _browserUiGeneration) return;
            if (generation != _browserUiGeneration || !records.TryGetValue(assetId, out var record)) continue;

            if (pendingThumbnails.Contains(assetId) && record.ThumbnailRelativePath is not null &&
                record.ThumbnailState == PreviewComponentState.Current)
            {
                string? absolute = null;
                try { absolute = MediaPathSemantics.ResolveContained(_storage.Locations.PreviewsDirectory, record.ThumbnailRelativePath); }
                catch { /* leave the placeholder; a later refresh may resolve a valid path */ }
                if (absolute is not null && File.Exists(absolute)) _browserGrid.ApplyThumbnail(assetId, absolute);
            }

            if (pendingMetadata.Contains(assetId) && record.MetadataState == PreviewComponentState.Current)
            {
                var metadata = BrowserQueryEngine.ExtractMetadata(record.MetadataJson);
                if (_browserGrid.ApplyMetadata(assetId, metadata)) sortRelevantMetadataChanged = true;
            }
        }

        UpdateBrowserStatusText();
        var hasMetadataFilter = _browserGrid.Query.Filters.Any(filter => filter.Field is BrowserFilterField.Camera or
            BrowserFilterField.Lens or BrowserFilterField.CaptureDate or BrowserFilterField.Duration or
            BrowserFilterField.Resolution or BrowserFilterField.FrameRate);
        if (sortRelevantMetadataChanged && (_browserGrid.Query.SortMode is BrowserSortMode.CaptureDate or BrowserSortMode.Duration || hasMetadataFilter))
        {
            // Coalesce into one re-sort ~800ms after updates settle, rather than resorting/reflowing per
            // asset while a large folder's metadata is still streaming in — see #109's responsiveness goal.
            _browserMetadataResortTimer.Stop();
            _browserMetadataResortTimer.Start();
        }
    }

    private void LocateTools(string? configuredPath = null)
    {
        var baseDir = AppContext.BaseDirectory;
        _ffmpeg = ExecutableLocator.Find("ffmpeg.exe", Path.Combine(baseDir, "ffmpeg", "bin", "ffmpeg.exe"), configured: configuredPath ?? _settings.FfmpegPath);
        var besideFfmpeg = _ffmpeg is null ? "" : Path.Combine(Path.GetDirectoryName(_ffmpeg)!, "ffprobe.exe");
        _ffprobe = ExecutableLocator.Find("ffprobe.exe", Path.Combine(baseDir, "ffmpeg", "bin", "ffprobe.exe"), configured: besideFfmpeg);
        StatusText.Text = _ffmpeg is null ? "FFmpeg not found — configure it in Settings" : $"FFmpeg ready: {_ffmpeg}";
    }

    private async Task ApplyEncodingHandoffAsync(CapabilityInvocation invocation)
    {
        _browserEncodingHandoffCts?.Cancel();
        var cancellation = new CancellationTokenSource();
        _browserEncodingHandoffCts = cancellation;
        BrowserExportButton.IsEnabled = false;
        try
        {
            var result = await new EncodingCapabilityHandoff(_storage.MediaAssets, _storage.MediaRoots,
                    _storage.MediaRanges, _storage.AssetColors, _storage.LutCache,
                    new EncodingLutResourceStore(EncodingLutResourceStore.DefaultDirectory))
                .MaterializeAsync(invocation, cancellation.Token).ConfigureAwait(true);
            if (!ReferenceEquals(_browserEncodingHandoffCts, cancellation)) return;
            if (!result.Succeeded)
            {
                if (ReferenceEquals(_browserEncodingInvocation, invocation))
                {
                    _batchMetadataCts?.Cancel();
                    _batchFiles.Clear();
                    UpdateBatchFileSummary();
                }
                MessageBox.Show("The selection was not sent to Export:\n\n" + string.Join("\n", result.Errors),
                    "Cannot export selection", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var resourceStore = new EncodingLutResourceStore(EncodingLutResourceStore.DefaultDirectory);
            var model = new ExportDialogModel(result, _settings.Encoding,
                _storage.LutCache.Snapshot(ColorLutStage.Camera).Resources,
                _storage.LutCache.Snapshot(ColorLutStage.Creative).Resources, resourceStore);
            var dialog = new ExportDialog(model, _exportCoordinator, _ffprobe) { Owner = this };
            dialog.ShowDialog();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SqliteException)
        {
            MessageBox.Show($"The Browser selection could not be prepared: {exception.Message}",
                "Cannot export selection", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (ReferenceEquals(_browserEncodingHandoffCts, cancellation))
            {
                _browserEncodingHandoffCts = null;
                UpdateBrowserSelectionActions();
            }
            cancellation.Dispose();
        }
    }

    private async Task ApplySubclipExportHandoffAsync(SubclipExportInvocation invocation)
    {
        _browserEncodingHandoffCts?.Cancel();
        var cancellation = new CancellationTokenSource();
        _browserEncodingHandoffCts = cancellation;
        BrowserExportButton.IsEnabled = false;
        try
        {
            var sourceHandoff = new EncodingCapabilityHandoff(_storage.MediaAssets, _storage.MediaRoots,
                _storage.MediaRanges, _storage.AssetColors, _storage.LutCache,
                new EncodingLutResourceStore(EncodingLutResourceStore.DefaultDirectory));
            var result = await new SubclipExportCapabilityHandoff(sourceHandoff, _storage.Subclips)
                .MaterializeAsync(invocation, cancellation.Token).ConfigureAwait(true);
            if (!ReferenceEquals(_browserEncodingHandoffCts, cancellation)) return;
            if (!result.Succeeded)
            {
                MessageBox.Show("The Subclip selection was not sent to Export:\n\n" +
                    string.Join("\n", result.Errors), "Cannot export Subclips",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var resourceStore = new EncodingLutResourceStore(EncodingLutResourceStore.DefaultDirectory);
            var model = new ExportDialogModel(result, _settings.Encoding,
                _storage.LutCache.Snapshot(ColorLutStage.Camera).Resources,
                _storage.LutCache.Snapshot(ColorLutStage.Creative).Resources, resourceStore,
                revalidate: async (includeNoSubclipSources, token) => await new SubclipExportCapabilityHandoff(
                    new EncodingCapabilityHandoff(_storage.MediaAssets, _storage.MediaRoots,
                        _storage.MediaRanges, _storage.AssetColors, _storage.LutCache, resourceStore),
                    _storage.Subclips).MaterializeAsync(invocation with
                    {
                        IncludeNoSubclipSources = invocation.EntryKind == SubclipExportEntryKind.BrowserSources &&
                            includeNoSubclipSources
                    }, token), namingDefault: ExportNamingDefault.Subclip);
            new ExportDialog(model, _exportCoordinator, _ffprobe) { Owner = this }.ShowDialog();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SqliteException)
        {
            MessageBox.Show($"The Subclip selection could not be prepared: {exception.Message}",
                "Cannot export Subclips", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (ReferenceEquals(_browserEncodingHandoffCts, cancellation))
            {
                _browserEncodingHandoffCts = null;
                UpdateBrowserSelectionActions();
            }
            cancellation.Dispose();
        }
    }
    private async Task RefreshDependencyHealthAsync()
    {
        DependencySummary.Text = "Checking the tools needed for export…";
        DependencyResults.ItemsSource = null;
        var report = await DependencyHealthCheck.RunAsync(_ffmpeg, _ffprobe);
        DependencyResults.ItemsSource = report.Items;
        DependencySummary.Text = report.Summary;
        StatusText.Text = report.IsReady ? "Export tools ready" : "Export setup needs attention — open Settings";
        _activityLogFile.TryAppend($"[App] Dependency check: {report.Summary}");
    }

    private void RequirementHelp_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is not ToggleButton button) return;
        if (_requirementHelpDismissals.Remove(button, out var pending)) pending.Cancel();
    }

    private async void RequirementHelp_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is not ToggleButton button || button.IsChecked != true) return;
        if (_requirementHelpDismissals.Remove(button, out var pending)) pending.Cancel();
        var dismissal = new CancellationTokenSource();
        _requirementHelpDismissals[button] = dismissal;
        try
        {
            await Task.Delay(900, dismissal.Token);
            if (!button.IsMouseOver) button.IsChecked = false;
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (_requirementHelpDismissals.TryGetValue(button, out var current) && ReferenceEquals(current, dismissal))
                _requirementHelpDismissals.Remove(button);
            dismissal.Dispose();
        }
    }

    private void RequirementHelpPopup_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ToggleButton button }) button.IsChecked = false;
    }
    private async void CheckDependencies_Click(object sender, RoutedEventArgs e)
    {
        LocateTools(SettingsFfmpegPath.Text);
        await RefreshDependencyHealthAsync();
    }
    private static string? PickFolder(string description, string? initialFolder = null)
    {
        using var dlg = new Forms.FolderBrowserDialog { Description = description, UseDescriptionForTitle = true };
        if (ResolveFolderPickerInitialDirectory(initialFolder) is { } start)
        {
            dlg.InitialDirectory = start;
            dlg.SelectedPath = start;
        }
        return dlg.ShowDialog() == Forms.DialogResult.OK ? dlg.SelectedPath : null;
    }

    internal static string? ResolveFolderPickerInitialDirectory(string? configuredFolder)
    {
        if (string.IsNullOrWhiteSpace(configuredFolder)) return null;
        try
        {
            var candidate = Path.GetFullPath(configuredFolder.Trim());
            while (!Directory.Exists(candidate))
            {
                var parent = Directory.GetParent(candidate)?.FullName;
                if (string.IsNullOrEmpty(parent) || string.Equals(parent, candidate, StringComparison.OrdinalIgnoreCase))
                    return null;
                candidate = parent;
            }
            return candidate;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException
                                          or NotSupportedException) { return null; }
    }

    private void BrowseInput_Click(object sender, RoutedEventArgs e)
    {
        if (PickFolder("Select the folder containing video files", InputFolder.Text) is not { } folder) return;
        InputFolder.Text = folder;
        RefreshBatchFiles();
    }

    private void BrowseOutputFolder_Click(object sender, RoutedEventArgs e)
    {
        if (PickFolder("Select the output folder", OutputSpecificFolder.Text) is { } folder) OutputSpecificFolder.Text = folder;
    }


    private void OutputMode_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (IsLoaded) { UpdateOutputModeUi(); RefreshBatchFiles(); }
    }

    private void OutputDestination_LostKeyboardFocus(object sender, System.Windows.Input.KeyboardFocusChangedEventArgs e) => RefreshBatchFiles();

    private void UpdateOutputModeUi()
    {
        var mode = (OutputDestinationMode)Math.Clamp(OutputMode.SelectedIndex, 0, 2);
        OutputSameFolderPanel.Visibility = mode == OutputDestinationMode.SameFolder ? Visibility.Visible : Visibility.Collapsed;
        OutputSubfolderPanel.Visibility = mode == OutputDestinationMode.Subfolder ? Visibility.Visible : Visibility.Collapsed;
        OutputSpecificPanel.Visibility = mode == OutputDestinationMode.SpecificFolder ? Visibility.Visible : Visibility.Collapsed;
        UpdatePreserveFolderStructureUi();
    }

    private void UpdatePreserveFolderStructureUi()
    {
        var mode = (OutputDestinationMode)Math.Clamp(OutputMode.SelectedIndex, 0, 2);
        PreserveFolderStructure.Visibility = FolderStructurePolicy.IsAvailable(Recursive.IsChecked == true, mode)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }
    private void Resolution_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        if (_subfolderUsesResolutionDefault) SetResolutionSubfolderName();
        if (_filenameSuffixUsesResolutionDefault) SetResolutionFilenameSuffix();
        RefreshBatchFiles();
    }

    private void OutputSubfolderName_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (IsLoaded && !_updatingSubfolderName) _subfolderUsesResolutionDefault = false;
    }

    private void OutputFilenameSuffix_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (IsLoaded && !_updatingFilenameSuffix) _filenameSuffixUsesResolutionDefault = false;
    }

    private void SetResolutionFilenameSuffix()
    {
        _updatingFilenameSuffix = true;
        OutputFilenameSuffix.Text = $"_{EncodingPathPlanner.ResolutionName((OutputResolution)Math.Clamp(Resolution.SelectedIndex, 0, 5))}";
        _updatingFilenameSuffix = false;
        _filenameSuffixUsesResolutionDefault = true;
    }
    private void SetResolutionSubfolderName()
    {
        _updatingSubfolderName = true;
        OutputSubfolderName.Text = EncodingPathPlanner.ResolutionName((OutputResolution)Math.Clamp(Resolution.SelectedIndex, 0, 5));
        _updatingSubfolderName = false;
        _subfolderUsesResolutionDefault = true;
    }
    private void InputFolder_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (!IsLoaded) return;
        _browserEncodingInvocation = null;
        _batchFolderRefreshTimer.Stop();
        _batchMetadataCts?.Cancel();
        RememberBatchFileSelection();
        _batchFiles.Clear();
        UpdateBatchFileSummary();
        _batchFolderRefreshTimer.Start();
    }
    private void Recursive_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        _browserEncodingInvocation = null;
        UpdatePreserveFolderStructureUi();
        RefreshBatchFiles();
    }
    private void SettingsRecursive_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        SettingsPreserveFolderStructure.Visibility = SettingsRecursive.IsChecked == true
            ? Visibility.Visible
            : Visibility.Collapsed;
    }
    private async void RefreshBatchFiles_Click(object sender, RoutedEventArgs e)
    {
        if (_browserEncodingInvocation is { } invocation) await ApplyEncodingHandoffAsync(invocation);
        else RefreshBatchFiles();
    }
    private void BatchFileSelection_Click(object sender, RoutedEventArgs e)
    {
        RememberBatchFileSelection();
        UpdateBatchFileSummary();
    }
    private void SelectAllBatchFiles_Click(object sender, RoutedEventArgs e) => SetBatchFileSelection(true);
    private void SelectNoBatchFiles_Click(object sender, RoutedEventArgs e) => SetBatchFileSelection(false);

    private void TrimBatchFile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { DataContext: BatchFileOption option }) return;
        var currentIdentity = TrimSourceIdentity.Read(option.FilePath);
        if (option.SourceIdentity is null || currentIdentity is null || !option.SourceIdentity.Matches(currentIdentity))
        {
            MessageBox.Show(
                "This video has changed since it was added. Refresh the file list before editing its trim.",
                "Video changed",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var editor = new TrimEditorWindow(option.FilePath, option.TrimRange) { Owner = this };
        var dialogResult = editor.ShowDialog();
        if (dialogResult != true) return;
        var identityAfterEditing = TrimSourceIdentity.Read(option.FilePath);
        if (identityAfterEditing is null || !option.SourceIdentity.Matches(identityAfterEditing))
        {
            MessageBox.Show(
                "This video changed while the trim editor was open. The existing trim was left unchanged.",
                "Video changed",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }
        try
        {
            TrimStatePersistence.ApplyDialogResult(dialogResult, option, editor.AppliedRange, _trimHistory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _activityLogFile.TryAppend($"[Trim] Could not persist trim for {option.FilePath}: {exception}");
            MessageBox.Show("The trim was applied for this session but could not be saved for later.", "Trim history", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void RefreshBatchFiles()
    {
        if (_browserEncodingInvocation is not null) return;
        _batchFolderRefreshTimer.Stop();
        RememberBatchFileSelection();
        _batchMetadataCts?.Cancel();
        _batchMetadataCts?.Dispose();
        _batchMetadataCts = new CancellationTokenSource();
        _batchFiles.Clear();
        string? excludedOutput = null;
        try
        {
            if ((OutputDestinationMode)Math.Clamp(OutputMode.SelectedIndex, 0, 2) != OutputDestinationMode.SameFolder)
            {
                var candidate = OutputDestinationPlanner.ResolveRoot(InputFolder.Text, (OutputResolution)Math.Clamp(Resolution.SelectedIndex, 0, 5), CurrentOutputDestination());
                if (!string.Equals(Path.GetFullPath(candidate).TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(InputFolder.Text).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)) excludedOutput = candidate;
            }
        }
        catch (ArgumentException) { }
        string? excludedSuffix = null;
        try
        {
            if ((OutputDestinationMode)Math.Clamp(OutputMode.SelectedIndex, 0, 2) == OutputDestinationMode.SameFolder)
                excludedSuffix = OutputDestinationPlanner.ResolveFilenameSuffix((OutputResolution)Math.Clamp(Resolution.SelectedIndex, 0, 5), CurrentOutputDestination());
        }
        catch (ArgumentException) { }
        foreach (var option in BatchFileSelection.Discover(InputFolder.Text, Recursive.IsChecked == true, excludedOutput, excludedSuffix))
        {
            if (_trimHistory.Restore(option.FilePath) is { } restored) option.ApplyTrim(restored);
            _batchFiles.Add(option);
        }
        ConfigureAssignedColorUi();
        _batchSelectionMemory.Apply(InputFolder.Text, _batchFiles);
        UpdateBatchFileSummary();
        _ = LoadBatchMetadataAsync(_batchFiles.ToList(), _batchMetadataCts.Token);
    }

    private async Task LoadBatchMetadataAsync(IReadOnlyList<BatchFileOption> options, CancellationToken token)
    {
        if (_ffprobe is null)
        {
            foreach (var option in options) option.MarkMetadataUnavailable();
            MediaWarningAnalyzer.Apply(options);
            UpdateBatchFileSummary();
            return;
        }

        try
        {
            foreach (var option in options)
            {
                token.ThrowIfCancellationRequested();
                var result = await CaptureAsync(_ffprobe, FfmpegCommandBuilder.ProbeMetadata(option.FilePath), token);
                if (result.ExitCode == 0 && MediaMetadataParser.TryParse(result.StdOut, option.FileSizeBytes, out var metadata))
                    option.ApplyMetadata(metadata);
                else
                    option.MarkMetadataUnavailable();
                MediaWarningAnalyzer.Apply(options);
                UpdateBatchFileSummary();
            }
        }
        catch (OperationCanceledException)
        {
            // A newer folder selection replaced this analysis.
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            _activityLogFile.TryAppend($"[App] Metadata probe failed: {ex}");
            foreach (var option in options.Where(item => item.IsAnalyzing)) option.MarkMetadataUnavailable();
            MediaWarningAnalyzer.Apply(options);
            UpdateBatchFileSummary();
        }
    }

    private void SetBatchFileSelection(bool selected)
    {
        foreach (var option in _batchFiles) option.IsSelected = selected;
        RememberBatchFileSelection();
        UpdateBatchFileSummary();
    }

    private void RememberBatchFileSelection()
    {
        if (_batchFiles.Count > 0) _batchSelectionMemory.Remember(InputFolder.Text, _batchFiles);
    }

    private void UpdateBatchFileSummary()
    {
        BatchFileSummary.Text = BatchFileSelection.Summary(_batchFiles);
        UpdateBatchReadiness();
    }

    private void UpdateBatchReadiness(bool updateGuidance = true)
    {
        var folder = InputFolder.Text.Trim();
        var selected = _batchFiles.Count(file => file.IsSelected);
        var presentation = BatchStartReadiness.Evaluate(folder.Length > 0, Directory.Exists(folder), _batchFiles.Count, selected);
        StartButton.IsEnabled = _jobCancellation is null && presentation.CanStart;
        if (updateGuidance) CurrentFileText.Text = presentation.Guidance;
    }

    private async void RefreshLuts_Click(object sender, RoutedEventArgs e)
    {
        await RefreshLutsAsync(refreshCamera: true, refreshCreative: true);
        if (_playerViewerHost is not null)
            await _playerViewerHost.RefreshColorFoldersAsync(cameraChanged: true, creativeChanged: true);
    }

    private int RefreshLuts()
    {
        var previousSelection = LutSelection.SelectedItem as LutOption;
        var preferredPath = previousSelection?.FilePath ?? _state.LastLutPath;
        var options = _lutOptions;
        var lutCount = options.Count - 1;
        LutSelection.ItemsSource = options;
        LutSelection.SelectedItem = LutCatalog.SelectPreferred(options, preferredPath);
        return lutCount;
    }

    private void LutSelection_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (SelectedLutPath is not { } path || string.Equals(path, _state.LastLutPath, StringComparison.OrdinalIgnoreCase)) return;
        _state = _state with { LastLutPath = path };
        try
        {
            AppStateStore.Save(_storage.Locations.StatePath, _state);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _activityLogFile.TryAppend($"[App] Could not remember LUT selection: {ex}");
            SettingsMessage.Text = $"Could not remember LUT selection: {ex.Message}";
        }
    }

    private void EncodingColorModeSelection_Changed(object sender,
        System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (LutSelection is null) return;
        LutSelection.IsEnabled = CurrentEncodingColorMode == EncodingColorMode.OriginalOrManual;
    }

    private EncodingColorMode CurrentEncodingColorMode =>
        AssignedColorConfiguration?.Visibility == Visibility.Visible && EncodingColorModeSelection.SelectedIndex == 0
            ? EncodingColorMode.Assigned
            : EncodingColorMode.OriginalOrManual;

    private void ConfigureAssignedColorUi(EncodingColorMode? restoredMode = null)
    {
        var colors = _batchFiles.Where(file => file.AssignedColor?.HasAssignments == true).ToList();
        AssignedColorConfiguration.Visibility = colors.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (colors.Count == 0)
        {
            EncodingColorModeSelection.SelectedIndex = 1;
            AssignedColorSummary.Text = "";
            LutSelection.IsEnabled = true;
            return;
        }
        var enabled = colors.Count(file => file.AssignedColor!.ColorEnabled);
        EncodingColorModeSelection.SelectedIndex = restoredMode is { } mode
            ? mode == EncodingColorMode.Assigned ? 0 : 1
            : enabled > 0 ? 0 : 1;
        var distinct = colors.Select(file => string.Join(" → ", file.AssignedColor!.OrderedPipeline
            .Select(resource => resource.DisplayName))).Distinct(StringComparer.Ordinal).Count();
        AssignedColorSummary.Text = $"{colors.Count} input{(colors.Count == 1 ? "" : "s")} with assigned Color" +
            (distinct > 1 ? $" · {distinct} different pipelines" : "") +
            (enabled < colors.Count ? $" · {colors.Count - enabled} saved Off" : "");
        LutSelection.IsEnabled = CurrentEncodingColorMode == EncodingColorMode.OriginalOrManual;
    }

    private void BrowseDefaultVideoFolder_Click(object sender, RoutedEventArgs e)
    {
        if (PickFolder("Select the default video folder", SettingsDefaultVideoFolder.Text) is { } folder)
            SettingsDefaultVideoFolder.Text = folder;
    }

    private void BrowseScreengrabFolder_Click(object sender, RoutedEventArgs e)
    {
        if (PickFolder("Select the folder for full-resolution screengrabs", SettingsScreengrabDirectory.Text) is { } folder)
            SettingsScreengrabDirectory.Text = folder;
    }

    private void SettingsPath_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsInitialized || SettingsDefaultVideoFolderStatus is null) return;
        RefreshSettingsPathStatuses();
    }

    private void RefreshSettingsPathStatuses()
    {
        SettingsDefaultVideoFolderStatus.Text = SettingsFolderStatus(SettingsDefaultVideoFolder.Text);
        SettingsScreengrabDirectoryStatus.Text = SettingsFolderStatus(SettingsScreengrabDirectory.Text);
        SettingsFfmpegPathStatus.Text = string.IsNullOrWhiteSpace(SettingsFfmpegPath.Text)
            ? "Using bundled FFmpeg when available, then the first copy on PATH."
            : File.Exists(SettingsFfmpegPath.Text)
                ? "FFmpeg executable is available."
                : "This executable is currently unavailable. The setting will not be saved until the path is valid.";
    }

    private static string SettingsFolderStatus(string? path) => string.IsNullOrWhiteSpace(path)
        ? "Enter a folder path."
        : Directory.Exists(path)
            ? "Folder is available."
            : "This folder is currently unavailable. Lightflow will keep the path and try it when needed.";

    private void BrowseSettingsCameraLutFolder_Click(object sender, RoutedEventArgs e)
    {
        if (PickFolder("Select the Camera LUT folder", SettingsCameraLutFolder.Text) is { } folder)
        {
            SettingsCameraLutFolder.Text = folder;
            SettingsCameraLutFolderStatus.Text = "Select Save Settings to scan this Camera LUT root.";
        }
    }

    private void BrowseSettingsCreativeLutFolder_Click(object sender, RoutedEventArgs e)
    {
        if (PickFolder("Select the Creative LUT folder", SettingsCreativeLutFolder.Text) is { } folder)
        {
            SettingsCreativeLutFolder.Text = folder;
            SettingsCreativeLutFolderStatus.Text = "Select Save Settings to scan this Creative LUT root.";
        }
    }

    private async Task<int> InitializeLutsAsync()
    {
        if (!_storage.CatalogAvailable) return PublishCachedLuts();
        await Task.WhenAll(
            InitializeLutStageAsync(ColorLutStage.Camera, _settings.CameraLutFolder,
                _settings.CameraLutIncludeSubfolders),
            InitializeLutStageAsync(ColorLutStage.Creative, _settings.CreativeLutFolder,
                _settings.CreativeLutIncludeSubfolders));
        _lutInitializationCompleted = true;
        AuditBrowserVisualIdentitiesAfterLutInitialization();
        return PublishCachedLuts();
    }

    private void AuditBrowserVisualIdentitiesAfterLutInitialization()
    {
        var loaded = _lastLoadedBrowserState?.DerivedWork;
        var scheduler = _storage.DerivedWork;
        if (!BrowserVisualIdentityAuditPolicy.ShouldSchedule(_lutInitializationCompleted, _browserUiGeneration,
                _browserVisualIdentityAuditGeneration, loaded is not null, scheduler is not null))
            return;
        _browserVisualIdentityAuditGeneration = _browserUiGeneration;
        var scheduled = scheduler!.TrySchedule(loaded!.Reconciliation, DerivedWorkPriority.Visible);
        if (scheduled.Accepted) AttachBrowserDerivedWork(scheduled.Batch, _browserUiGeneration);
    }

    private async Task InitializeLutStageAsync(ColorLutStage stage, string folder, bool includeSubfolders)
    {
        try
        {
            await _storage.LutCache.RefreshAsync(stage, folder, includeSubfolders);
            PublishCachedLuts();
            if (_playerViewerHost is not null)
                await _playerViewerHost.RefreshColorFoldersAsync(stage == ColorLutStage.Camera,
                    stage == ColorLutStage.Creative);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException
                                          or NotSupportedException or InvalidOperationException or SqliteException)
        {
            SettingsMessage.Text = $"The {stage} LUT collection could not be refreshed: {exception.Message}";
        }
    }

    private async Task<int> RefreshLutsAsync(bool refreshCamera, bool refreshCreative)
    {
        if (!_storage.CatalogAvailable)
        {
            _lutOptions = [LutCatalog.NoLut];
            return RefreshLuts();
        }
        try
        {
            if (refreshCamera)
                await _storage.LutCache.RefreshAsync(ColorLutStage.Camera, _settings.CameraLutFolder,
                    _settings.CameraLutIncludeSubfolders);
            if (refreshCreative)
                await _storage.LutCache.RefreshAsync(ColorLutStage.Creative, _settings.CreativeLutFolder,
                    _settings.CreativeLutIncludeSubfolders);
            return PublishCachedLuts();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException
                                          or NotSupportedException or InvalidOperationException or SqliteException)
        {
            SettingsMessage.Text = $"The LUT collections could not be refreshed: {exception.Message}";
            return PublishCachedLuts();
        }
    }

    private int PublishCachedLuts()
    {
        try
        {
            var camera = _storage.LutCache.Snapshot(ColorLutStage.Camera);
            var creative = _storage.LutCache.Snapshot(ColorLutStage.Creative);
            _lutOptions = LutCatalog.CombinedOptions(camera.Resources, creative.Resources);
            var count = RefreshLuts();
            SettingsCameraLutFolderStatus.Text = LutFolderStatus(camera);
            SettingsCreativeLutFolderStatus.Text = LutFolderStatus(creative);
            return count;
        }
        catch (Exception exception) when (exception is InvalidOperationException)
        {
            _lutOptions = [LutCatalog.NoLut];
            var count = RefreshLuts();
            SettingsCameraLutFolderStatus.Text = SettingsCreativeLutFolderStatus.Text = exception.Message;
            return count;
        }
    }

    private static string LutFolderStatus(LutLibrarySnapshot snapshot)
    {
        var count = snapshot.Resources.Count;
        return snapshot.Problems.Count == 0
            ? count == 0 ? "No compatible .cube LUTs found in this folder."
                : $"{count} compatible LUT{(count == 1 ? "" : "s")} available."
            : $"{count} compatible LUT{(count == 1 ? "" : "s")} available; "
                + $"{snapshot.Problems.Count} file{(snapshot.Problems.Count == 1 ? " was" : "s were")} skipped. "
                + $"{snapshot.Problems[0].FileName}: {snapshot.Problems[0].Diagnostic}";
    }

    private void BrowseSettingsFfmpeg_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "FFmpeg executable|ffmpeg.exe", Title = "Select ffmpeg.exe" };
        if (File.Exists(SettingsFfmpegPath.Text)) dialog.InitialDirectory = Path.GetDirectoryName(SettingsFfmpegPath.Text);
        if (dialog.ShowDialog() == true) SettingsFfmpegPath.Text = dialog.FileName;
    }

    private async void ChangeCatalogLocation_Click(object sender, RoutedEventArgs e)
    {
        if (!_storage.CatalogAvailable)
        {
            MessageBox.Show("The configured Catalog is not currently available. Lightflow will not replace or redirect it. Restore access to the configured location before relocating it.",
                "Catalog unavailable", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var destination = PickFolder("Choose the new Catalog folder", _storage.Locations.CatalogDirectory);
        if (destination is null) return;
        var confirmation = MessageBox.Show(
            "Lightflow will safely copy and validate the Catalog, switch only after validation succeeds, and retain the original as a recovery source. Continue?",
            "Move Catalog", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirmation != MessageBoxResult.Yes) return;
        SettingsMessage.Text = "Moving and validating the Catalog…";
        var result = await _storage.RelocateCatalogAsync(destination);
        SettingsCatalogDirectory.Text = _storage.Locations.CatalogDirectory;
        SettingsMessage.Text = result.Succeeded
            ? result.Diagnostic ?? "Catalog moved successfully. The original Catalog was retained."
            : result.Diagnostic;
        if (!result.Succeeded) MessageBox.Show(result.Diagnostic, "Catalog was not moved", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private async void ChangePreviewsLocation_Click(object sender, RoutedEventArgs e)
    {
        var destination = PickFolder("Choose the new Previews folder", _storage.Locations.PreviewsDirectory);
        if (destination is null) return;
        var choice = MessageBox.Show(
            "Choose Yes to move existing Previews. Choose No to use the new location and rebuild Previews as needed. Choose Cancel to keep the current location.",
            "Change Previews Location", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (choice == MessageBoxResult.Cancel) return;
        var mode = choice == MessageBoxResult.Yes ? PreviewRelocationMode.MoveExisting : PreviewRelocationMode.SwitchAndRebuild;
        SettingsMessage.Text = mode == PreviewRelocationMode.MoveExisting ? "Moving Previews…" : "Changing Previews location…";
        var result = await _storage.RelocatePreviewsAsync(destination, mode);
        SettingsPreviewsDirectory.Text = _storage.Locations.PreviewsDirectory;
        SettingsMessage.Text = result.Succeeded ? "Previews location changed successfully." : result.Diagnostic;
        if (!result.Succeeded) MessageBox.Show(result.Diagnostic, "Previews location was not changed", MessageBoxButton.OK, MessageBoxImage.Error);
        await RefreshPreviewUsageAsync();
    }

    private async void RefreshPreviewUsage_Click(object sender, RoutedEventArgs e) =>
        await RefreshPreviewUsageAsync();

    private async Task RefreshPreviewUsageAsync()
    {
        try
        {
            var usage = await _storage.GetPreviewUsageAsync();
            PreviewUsageText.Text = usage is null
                ? _storage.PreviewDiagnostic ?? "Preview storage is unavailable."
                : $"{FormatBytes(usage.TotalBytes)} used — {usage.RecordCount:N0} records, " +
                  $"{usage.ArtifactCount:N0} generated files{(usage.OrphanCount == 0 ? "" : $", {usage.OrphanCount:N0} orphaned")}";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or SqliteException)
        {
            PreviewUsageText.Text = $"Preview usage is unavailable: {exception.Message}";
        }
    }

    private async void CleanupPreviews_Click(object sender, RoutedEventArgs e)
    {
        await RunPreviewMaintenanceAsync(async token =>
        {
            PreviewMaintenanceStatus.Text = "Cleaning stale and unreferenced Preview files…";
            var result = await _storage.CleanupPreviewsAsync(token);
            PreviewMaintenanceStatus.Text = result.Succeeded
                ? $"Cleanup complete: {result.FilesRemoved:N0} files removed, {FormatBytes(result.BytesFreed)} freed." +
                  (result.Diagnostic is null ? "" : $" {result.Diagnostic}")
                : result.Diagnostic;
        });
    }

    private async void ClearPreviews_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("Clear all rebuildable Preview metadata and generated images? Catalog data and source media will not be changed.",
            "Clear Previews", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        await RunPreviewMaintenanceAsync(async token =>
        {
            PreviewMaintenanceStatus.Text = "Clearing Previews…";
            var result = await _storage.ClearPreviewsAsync(token);
            PreviewMaintenanceStatus.Text = result.Succeeded ? "Previews cleared." : result.Diagnostic;
        });
    }

    private async void RebuildPreviews_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("Clear and rebuild Preview metadata and visual Previews for all available Catalog assets? Offline sources will be skipped and can be rebuilt later.",
            "Rebuild Previews", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        await RunPreviewMaintenanceAsync(async token =>
        {
            PreviewMaintenanceProgress.Visibility = Visibility.Visible;
            var progress = new Progress<PreviewRebuildProgress>(value =>
            {
                PreviewMaintenanceProgress.Maximum = Math.Max(1, value.Total);
                PreviewMaintenanceProgress.Value = value.Completed;
                PreviewMaintenanceStatus.Text = value.Total == 0 ? "No Catalog assets to rebuild."
                    : $"Rebuilding {value.Completed:N0} of {value.Total:N0}: {value.CurrentItem}";
            });
            var result = await _storage.RebuildPreviewsAsync(progress, token);
            PreviewMaintenanceStatus.Text = result.Succeeded
                ? $"Rebuild complete: {result.Rebuilt:N0} rebuilt, {result.Skipped:N0} unavailable or unsupported."
                : result.Diagnostic;
        });
    }

    private async Task RunPreviewMaintenanceAsync(Func<CancellationToken, Task> operation)
    {
        if (_previewMaintenanceCts is not null) return;
        _previewMaintenanceCts = new();
        SetPreviewMaintenanceControls(running: true);
        try { await operation(_previewMaintenanceCts.Token); }
        catch (OperationCanceledException) { PreviewMaintenanceStatus.Text = "Preview maintenance canceled. Completed work remains valid."; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or SqliteException)
        {
            PreviewMaintenanceStatus.Text = $"Preview maintenance failed: {exception.Message}";
            _activityLogFile.TryAppend($"[App] Preview maintenance failed: {exception}");
        }
        finally
        {
            _previewMaintenanceCts.Dispose();
            _previewMaintenanceCts = null;
            SetPreviewMaintenanceControls(running: false);
            await RefreshPreviewUsageAsync();
        }
    }

    private void CancelPreviewMaintenance_Click(object sender, RoutedEventArgs e) => _previewMaintenanceCts?.Cancel();

    private void SetPreviewMaintenanceControls(bool running)
    {
        RefreshPreviewUsageButton.IsEnabled = !running;
        CleanupPreviewsButton.IsEnabled = !running;
        ClearPreviewsButton.IsEnabled = !running;
        RebuildPreviewsButton.IsEnabled = !running;
        ChangePreviewsLocationButton.IsEnabled = !running;
        SaveSettingsButton.IsEnabled = !running;
        CancelPreviewMaintenanceButton.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        if (!running) PreviewMaintenanceProgress.Visibility = Visibility.Collapsed;
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)Math.Max(0, bytes);
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return $"{value:0.#} {units[unit]}";
    }

    private async Task RefreshMediaRootsAsync()
    {
        _mediaRoots.Clear();
        if (!_storage.CatalogAvailable)
        {
            MediaRootsEmptyText.Text = "The Catalog is unavailable. Export remains available, but Media Roots cannot be managed.";
            MediaRootsEmptyText.Visibility = Visibility.Visible;
            return;
        }
        try
        {
            foreach (var root in await _storage.MediaRoots.ListAsync()) _mediaRoots.Add(root);
            MediaRootsEmptyText.Text = "No Media Roots yet. Add one to give media a stable Catalog identity.";
            MediaRootsEmptyText.Visibility = _mediaRoots.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception exception)
        {
            MediaRootsEmptyText.Text = $"Media Roots could not be loaded: {exception.Message}";
            MediaRootsEmptyText.Visibility = Visibility.Visible;
        }
    }

    private async Task RefreshBrowserStorageAsync()
    {
        _browserStorageEntries.Clear();
        try
        {
            foreach (var entry in await _storage.BrowserStorage.ListAsync())
                _browserStorageEntries.Add(entry);
            _browserTree.SetStorageEntries(_browserStorageEntries);
            BrowserRootsEmptyState.Visibility = _browserStorageEntries.Count == 0
                ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception exception)
        {
            BrowserRootsEmptyState.Visibility = Visibility.Visible;
            _activityLogFile.TryAppend($"[App] Storage locations unavailable: {exception.Message}");
        }
    }

    private async Task RefreshCollectionsAsync(Guid? selectedCollectionId = null)
    {
        try
        {
            var sets = new List<CollectionSet>();
            var collections = new List<MediaCollection>();
            var pending = new Queue<Guid?>();
            pending.Enqueue(null);
            while (pending.Count > 0)
            {
                var parent = pending.Dequeue();
                var children = await _storage.Collections.ListSetsAsync(parent);
                sets.AddRange(children);
                foreach (var child in children) pending.Enqueue(child.CollectionSetId);
                collections.AddRange(await _storage.Collections.ListCollectionsAsync(parent));
            }
            var expanded = _browserCollectionTree.Roots.Count == 0
                ? (_workspaceState.Current.Layout?.BrowserExpandedCollectionSetIds ?? []).ToHashSet()
                : _browserCollectionTree.ExpandedSetIds();
            _synchronizingCollectionTree = true;
            try { _browserCollectionTree.Populate(sets, collections, expanded, selectedCollectionId); }
            finally { _synchronizingCollectionTree = false; }
            _browserCollectionActionNode = null;
            _browserCollectionTreeRevealedNode = _browserCollectionTree.SelectedNode;
            BrowserCollectionsEmptyState.Visibility = sets.Count + collections.Count == 0
                ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException)
        {
            BrowserCollectionsEmptyState.Text = $"Collections unavailable: {exception.Message}";
            BrowserCollectionsEmptyState.Visibility = Visibility.Visible;
        }
    }

    private async void BrowserCollectionTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (_synchronizingCollectionTree || e.NewValue is not BrowserCollectionNode node) return;
        var interactive = BrowserCollectionActivation.IsInteractive(node, _browserCollectionPointerTarget,
            _browserCollectionKeyboardSelectionPending);
        _browserCollectionPointerTarget = null;
        _browserCollectionKeyboardSelectionPending = false;
        if (BrowserCollectionActivation.ShouldIgnoreDelayedReveal(node, _browserCollectionTreeRevealedNode, interactive))
        {
            _browserCollectionTreeRevealedNode = null;
            return;
        }
        if (interactive && ReferenceEquals(node, _browserCollectionTreeRevealedNode))
            _browserCollectionTreeRevealedNode = null;
        _browserCollectionTree.Select(node);
        _browserCollectionActionNode = node;
        if (node.IsCollection)
        {
            ActivateCollectionScopeSelection(node);
            await LoadCollectionScopeAsync(node.Id);
        }
    }

    private void ActivateFolderScopeSelection()
    {
        _browserScopeSelection.ActivateFolder();
        _synchronizingCollectionTree = true;
        try
        {
            _browserCollectionTree.Select(null);
            ClearRealizedTreeSelection(BrowserCollectionTree);
        }
        finally { _synchronizingCollectionTree = false; }
    }

    private void ActivateCollectionScopeSelection(BrowserCollectionNode? node)
    {
        _browserScopeSelection.ActivateCollection();
        _synchronizingBrowserTree = true;
        try
        {
            _browserTree.RestoreSelection(null);
            ClearRealizedTreeSelection(BrowserFolderTree);
        }
        finally { _synchronizingBrowserTree = false; }
        _synchronizingCollectionTree = true;
        try { _browserCollectionTree.Select(node); }
        finally { _synchronizingCollectionTree = false; }
    }

    private static void ClearRealizedTreeSelection(DependencyObject parent)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is TreeViewItem item && item.IsSelected) item.IsSelected = false;
            ClearRealizedTreeSelection(child);
        }
    }

    private void BrowserScopeSectionToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (BrowserLocationsSectionContent is null || BrowserCollectionsSectionContent is null) return;
        ApplyBrowserScopeSectionVisibility();
        _workspaceState.SetBrowserScopeSectionState(BrowserLocationsSectionToggle.IsChecked == true,
            BrowserCollectionsSectionToggle.IsChecked == true);
        _workspaceSaveTimer.Stop();
        _workspaceSaveTimer.Start();
    }

    private void ApplyBrowserScopeSectionVisibility()
    {
        BrowserLocationsSectionContent.Visibility = BrowserLocationsSectionToggle.IsChecked == true
            ? Visibility.Visible : Visibility.Collapsed;
        BrowserCollectionsSectionContent.Visibility = BrowserCollectionsSectionToggle.IsChecked == true
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private async Task LoadCollectionScopeAsync(Guid collectionId)
    {
        _collectionScopeCts?.Cancel();
        _collectionScopeCts?.Dispose();
        var request = _collectionScopeCts = new CancellationTokenSource();
        var generation = ++_browserUiGeneration;
        ShowBrowserLoadingState("Loading Collection…");
        try
        {
            var scope = await _browserCollectionScopes.LoadAsync(collectionId, request.Token).ConfigureAwait(true);
            if (request.IsCancellationRequested || generation != _browserUiGeneration) return;
            ApplyCollectionScope(scope, generation);
            await RefreshCollectionsAsync(collectionId);
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) when (exception is InvalidOperationException or KeyNotFoundException or IOException)
        {
            BrowserLoadingOverlay.Visibility = Visibility.Collapsed;
            MessageBox.Show(exception.Message, "Collection could not be opened", MessageBoxButton.OK, MessageBoxImage.Warning);
            await RefreshCollectionsAsync();
        }
    }

    private void ApplyCollectionScope(BrowserCollectionScope scope, long generation)
    {
        ActivateCollectionScopeSelection(BrowserCollectionTreeModel.Flatten(_browserCollectionTree.Roots)
            .FirstOrDefault(node => node.Id == scope.Collection.CollectionId));
        var queryScope = $"collection:{scope.Collection.CollectionId:D}";
        if (queryScope != _browserQueryScope) ResetBrowserQueryToolbar(BrowserSortMode.Manual);
        _browserQueryScope = queryScope;
        if (queryScope != _browserScopeIdentity && _browserPresentation == BrowserPresentationMode.PlayerViewer)
            _ = ReturnToBrowserGridAsync(restoreScrollOffset: false, focusGrid: false);
        if (queryScope != _browserScopeIdentity) _browserGrid.ClearSelection();
        _browserScopeIdentity = queryScope;
        _activeCollectionScope = scope;
        _lastLoadedBrowserState = null;
        _browserGrid.Populate(scope.Entries);
        UpdateBrowserGridColumns();
        _ = LoadCollectionPreviewStateAsync(scope.Assets.Select(item => item.AssetId).ToArray(), generation);
        _ = LoadBrowserAssetStatesAsync(scope.Assets, generation, _browserAssetStateRevision);
        AttachBrowserDerivedWork(scope.DerivedWork, generation);
        BrowserCurrentPath.Text = $"Collections / {scope.Collection.Name}";
        BrowserCurrentPath.IsReadOnly = true;
        BrowserBackButton.IsEnabled = false;
        BrowserForwardButton.IsEnabled = false;
        BrowserUpButton.IsEnabled = false;
        BrowserRefreshButton.IsEnabled = true;
        BrowserQueryToolbar.IsEnabled = true;
        BrowserIncludeSubfoldersButton.IsChecked = false;
        BrowserIncludeSubfoldersButton.IsEnabled = false;
        BrowserGridRows.Visibility = Visibility.Visible;
        BrowserLoadingOverlay.Visibility = Visibility.Collapsed;
        BrowserEmptyState.Visibility = _browserGrid.TotalCount == 0 ? Visibility.Visible : Visibility.Collapsed;
        BrowserEmptyTitle.Text = "No media in this Collection";
        BrowserEmptyMessage.Text = "Media membership is managed separately; Collection Sets organize Collections and do not contain media directly.";
        UpdateBrowserStatusText();
        _workspaceState.SetBrowserCollectionState(scope.Collection.CollectionId, _browserCollectionTree.ExpandedSetIds());
        _workspaceSaveTimer.Stop();
        _workspaceSaveTimer.Start();
    }

    private async Task LoadCollectionPreviewStateAsync(IReadOnlyList<Guid> assetIds, long generation)
    {
        if (_storage.Previews is not { } previews) return;
        IReadOnlyDictionary<Guid, PreviewRecord> records;
        try { records = await previews.GetManyAsync(assetIds).ConfigureAwait(true); }
        catch { return; }
        var metadataChanged = false;
        foreach (var (assetId, record) in records)
        {
            if (generation != _browserUiGeneration) return;
            if (record.ThumbnailState == PreviewComponentState.Current && record.ThumbnailRelativePath is { } relative)
            {
                try
                {
                    var absolute = MediaPathSemantics.ResolveContained(_storage.Locations.PreviewsDirectory, relative);
                    if (File.Exists(absolute)) _browserGrid.ApplyThumbnail(assetId, absolute);
                }
                catch { }
            }
            if (record.MetadataState == PreviewComponentState.Current &&
                _browserGrid.ApplyMetadata(assetId, BrowserQueryEngine.ExtractMetadata(record.MetadataJson)))
                metadataChanged = true;
        }
        if (metadataChanged) _browserGrid.ReapplyQuery();
        UpdateBrowserStatusText();
    }

    private async void BrowserNewCollection_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new NewCollectionDialog(BrowserCollectionPlacement.Options(_browserCollectionTree.Roots),
            BrowserCollectionPlacement.SuggestedParent(_browserCollectionTree.SelectedNode)) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        await RunCollectionActionAsync(async () =>
        {
            var created = await _storage.Collections.CreateCollectionAsync(dialog.CollectionName, dialog.ParentSetId);
            await LoadCollectionScopeAsync(created.CollectionId);
        });
    }

    private async void BrowserNewCollectionSet_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new NewCollectionDialog(BrowserCollectionPlacement.Options(_browserCollectionTree.Roots),
            BrowserCollectionPlacement.SuggestedParent(_browserCollectionTree.SelectedNode), createSet: true) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        await RunCollectionActionAsync(async () =>
        {
            var created = await _storage.Collections.CreateSetAsync(dialog.CollectionName, dialog.ParentSetId);
            await RefreshCollectionsAsync();
            if (BrowserCollectionTreeModel.Flatten(_browserCollectionTree.Roots)
                .FirstOrDefault(node => node.Id == created.CollectionSetId) is { } node) node.IsExpanded = true;
        });
    }

    private async void BrowserCollectionRename_Click(object sender, RoutedEventArgs e)
    {
        if (CollectionActionNode is not { } node) return;
        var name = TextEntryDialog.Prompt(this, $"Rename {(node.IsSet ? "Collection Set" : "Collection")}", "Name", node.Name);
        if (name is null) return;
        await RunCollectionActionAsync(async () =>
        {
            if (node.IsSet) await _storage.Collections.RenameSetAsync(node.Id, node.Revision, name);
            else await _storage.Collections.RenameCollectionAsync(node.Id, node.Revision, name);
            if (node.IsCollection && _activeCollectionScope?.Collection.CollectionId == node.Id)
                await LoadCollectionScopeAsync(node.Id);
            else
                await RefreshCollectionsAsync(_activeCollectionScope?.Collection.CollectionId);
        });
    }

    private async void BrowserCollectionDelete_Click(object sender, RoutedEventArgs e)
    {
        if (CollectionActionNode is not { } node) return;
        var kind = node.IsSet ? "Collection Set" : "Collection";
        if (node.IsSet && node.Children.Count > 0)
        {
            NoticeDialog.Show(this, "Collection Set not empty", $"“{node.Name}” can’t be deleted yet.",
                "Move or delete every nested Collection and Collection Set first, then try again.");
            return;
        }
        var detail = node.IsCollection ? "This removes Collection membership only. Source media will not be deleted."
            : "Only this empty organizational Set will be removed.";
        if (!ConfirmationDialog.Confirm(this, $"Delete {kind}", $"Delete “{node.Name}”?", detail, null, "Delete")) return;
        try
        {
            if (node.IsSet) await _storage.Collections.DeleteSetAsync(node.Id, node.Revision);
            else await _storage.Collections.DeleteCollectionAsync(node.Id, node.Revision);
            if (node.IsCollection && _activeCollectionScope?.Collection.CollectionId == node.Id)
            {
                _activeCollectionScope = null;
                _browserGrid.Populate([]);
                await RunBrowserNavigationAsync(() => _browserNavigation.RefreshAsync());
            }
            await RefreshCollectionsAsync();
        }
        catch (CollectionNotEmptyException)
        {
            NoticeDialog.Show(this, "Collection Set not empty", "This Collection Set can’t be deleted yet.",
                "The hierarchy changed and the Set now contains an item. Refresh, empty it, and try again.");
        }
        catch (Exception exception) when (IsCollectionActionFailure(exception)) { await HandleCollectionActionFailureAsync(exception); }
    }

    private void BrowserCollectionTree_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var node = CollectionActionNode;
        if (node is null) { e.Handled = true; return; }
        BrowserCollectionMoveMenu.Items.Clear();
        AddCollectionMoveTarget("Top level", null);
        var descendants = node.IsSet ? BrowserCollectionTreeModel.Flatten(node.Children).Select(child => child.Id).ToHashSet() : [];
        foreach (var set in BrowserCollectionTreeModel.Flatten(_browserCollectionTree.Roots).Where(item => item.IsSet &&
                     item.Id != node.Id && !descendants.Contains(item.Id)))
            AddCollectionMoveTarget(set.Name, set.Id);
        var siblings = CollectionSiblings(node);
        var index = siblings.FindIndex(item => item.Id == node.Id);
        BrowserCollectionMoveUpMenu.IsEnabled = index > 0;
        BrowserCollectionMoveDownMenu.IsEnabled = index >= 0 && index < siblings.Count - 1;
    }

    private void AddCollectionMoveTarget(string name, Guid? parent)
    {
        var item = new MenuItem { Header = name, Tag = parent?.ToString("D") ?? "root", Style = (Style)FindResource("LightflowMenuItemStyle") };
        item.Click += BrowserCollectionMoveTarget_Click;
        BrowserCollectionMoveMenu.Items.Add(item);
    }

    private async void BrowserCollectionMoveTarget_Click(object sender, RoutedEventArgs e)
    {
        if (CollectionActionNode is not { } node || sender is not MenuItem item) return;
        var parent = Equals(item.Tag, "root") ? (Guid?)null : Guid.Parse((string)item.Tag);
        if (node.ParentSetId == parent) return;
        await RunCollectionActionAsync(async () =>
        {
            if (node.IsSet) await _storage.Collections.ReparentSetAsync(node.Id, node.Revision, parent);
            else await _storage.Collections.ReparentCollectionAsync(node.Id, node.Revision, parent);
            await RefreshCollectionsAsync(_activeCollectionScope?.Collection.CollectionId);
        });
    }

    private async void BrowserCollectionMoveUp_Click(object sender, RoutedEventArgs e) => await ReorderCollectionNodeAsync(-1);
    private async void BrowserCollectionMoveDown_Click(object sender, RoutedEventArgs e) => await ReorderCollectionNodeAsync(1);

    private async void BrowserCollectionSortByName_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string tag }) return;
        var parts = tag.Split(':');
        var parent = parts[0] == "root" ? (Guid?)null
            : CollectionActionNode is { IsSet: true } set ? set.Id
            : CollectionActionNode?.ParentSetId;
        var descending = parts[^1] == "desc";
        await SortCollectionChildrenByNameAsync(parent, descending);
    }

    private async Task SortCollectionChildrenByNameAsync(Guid? parent, bool descending)
    {
        var children = BrowserCollectionTreeModel.Flatten(_browserCollectionTree.Roots)
            .Where(node => node.ParentSetId == parent).ToArray();
        var ordered = BrowserCollectionInteraction.OrderByName(children, descending);
        await RunCollectionActionAsync(async () =>
        {
            if (ordered.Length > 1)
                await _storage.Collections.ReorderHierarchyAsync(parent, ordered.Select(HierarchyOrder).ToArray());
            await RefreshCollectionsAsync(_activeCollectionScope?.Collection.CollectionId);
        });
    }

    private async Task ReorderCollectionNodeAsync(int delta)
    {
        if (CollectionActionNode is not { } node) return;
        var siblings = CollectionSiblings(node);
        var index = siblings.FindIndex(item => item.Id == node.Id);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= siblings.Count) return;
        (siblings[index], siblings[target]) = (siblings[target], siblings[index]);
        var order = siblings.Select(HierarchyOrder).ToArray();
        await RunCollectionActionAsync(async () =>
        {
            await _storage.Collections.ReorderHierarchyAsync(node.ParentSetId, order);
            await RefreshCollectionsAsync(_activeCollectionScope?.Collection.CollectionId);
        });
    }

    private List<BrowserCollectionNode> CollectionSiblings(BrowserCollectionNode node) =>
        BrowserCollectionTreeModel.Flatten(_browserCollectionTree.Roots)
            .Where(item => item.ParentSetId == node.ParentSetId)
            .OrderBy(item => item.Ordinal).ToList();

    private void BrowserCollectionTreeItem_ExpansionChanged(object sender, RoutedEventArgs e)
        => PersistCollectionExpansionState();

    private void PersistCollectionExpansionState()
    {
        _workspaceState.SetBrowserCollectionState(_activeCollectionScope?.Collection.CollectionId,
            _browserCollectionTree.ExpandedSetIds());
        _workspaceSaveTimer.Stop();
        _workspaceSaveTimer.Start();
    }

    private async Task RunCollectionActionAsync(Func<Task> action)
    {
        try { await action(); }
        catch (Exception exception) when (IsCollectionActionFailure(exception)) { await HandleCollectionActionFailureAsync(exception); }
    }

    private static bool IsCollectionActionFailure(Exception exception) => exception is InvalidOperationException or
        ArgumentException or IOException or UnauthorizedAccessException or SqliteException;

    private async Task HandleCollectionActionFailureAsync(Exception exception)
    {
        if (exception is CollectionConcurrencyException)
        {
            await RefreshCollectionsAsync(_activeCollectionScope?.Collection.CollectionId);
            NoticeDialog.Show(this, "Collections changed", "The Collections hierarchy changed before this action finished.",
                "The latest hierarchy is now shown. Review it and try the action again.");
            return;
        }
        NoticeDialog.Show(this, "Collection was not changed", "Lightflow couldn’t complete that Collection action.", exception.Message);
    }

    private void BrowserCollectionTree_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _collectionDragStart = e.GetPosition(BrowserCollectionTree);
        _collectionDragNode = CollectionNodeFromElement(e.OriginalSource as DependencyObject);
        _browserCollectionPointerTarget = _collectionDragNode;
    }

    private void BrowserCollectionTree_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key is not (Key.Up or Key.Down or Key.Left or Key.Right or Key.Home or Key.End or Key.PageUp or Key.PageDown))
            return;
        _browserCollectionKeyboardSelectionPending = true;
        Dispatcher.BeginInvoke(() => _browserCollectionKeyboardSelectionPending = false,
            System.Windows.Threading.DispatcherPriority.Input);
    }

    private void BrowserCollectionTree_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (CollectionNodeFromElement(e.OriginalSource as DependencyObject) is not { } clicked) return;
        var target = BrowserCollectionInteraction.ContextTarget(clicked, _browserCollectionTree.SelectedNode);
        _browserCollectionActionNode = target;
    }

    private void BrowserCollectionTree_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _collectionDragNode is null) return;
        var current = e.GetPosition(BrowserCollectionTree);
        if (Math.Abs(current.X - _collectionDragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - _collectionDragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        var dragged = _collectionDragNode;
        _collectionDragNode = null;
        _collectionDragSession.Begin(dragged);
        _collectionDragWheelHook = LowLevelMouseWheelHook.TryInstall(RouteCollectionDragWheel);
        try { System.Windows.DragDrop.DoDragDrop(BrowserCollectionTree, dragged, System.Windows.DragDropEffects.Move); }
        finally
        {
            _collectionDragWheelHook?.Dispose();
            _collectionDragWheelHook = null;
            _collectionDragSession.End();
            CancelCollectionDragHover();
            ClearCollectionDropFeedback();
        }
    }

    private bool RouteCollectionDragWheel(System.Windows.Point screenPoint, int delta)
    {
        var sidebarPoint = BrowserFolderScrollViewer.PointFromScreen(screenPoint);
        var pointerOverSidebar = sidebarPoint.X >= 0 && sidebarPoint.Y >= 0 &&
            sidebarPoint.X <= BrowserFolderScrollViewer.ActualWidth &&
            sidebarPoint.Y <= BrowserFolderScrollViewer.ActualHeight;
        return _collectionDragSession.RouteWheel(pointerOverSidebar,
            () => ScrollBrowserScopeByWheel(delta), dragged =>
            {
                CancelCollectionDragHover();
                ClearCollectionDropFeedback();
                _ = Dispatcher.BeginInvoke(DispatcherPriority.Render,
                    () => RefreshCollectionDragFeedback(dragged, screenPoint));
            });
    }

    private void RefreshCollectionDragFeedback(BrowserCollectionNode dragged, System.Windows.Point screenPoint)
    {
        if (!ReferenceEquals(_collectionDragSession.Payload, dragged)) return;
        var position = BrowserCollectionTree.PointFromScreen(screenPoint);
        var container = CollectionTreeItemAtHeader(position);
        var target = container?.DataContext as BrowserCollectionNode;
        var drop = target is not null && container is not null
            ? BrowserCollectionInteraction.DropAt(dragged, target,
                HeaderRelativeY(position, container)) : null;
        if (drop is { Kind: BrowserCollectionDropKind.InsertBefore or BrowserCollectionDropKind.InsertAfter } &&
            BrowserCollectionInteraction.ResolveInsertion(_browserCollectionTree.Roots, dragged, drop.Target,
                drop.Kind) is null) drop = null;
        if (drop is not null)
        {
            TrackCollectionDragHover(dragged, drop);
            ShowCollectionDropFeedback(container, dragged, drop);
            return;
        }
        if (TryResolveTrailingInsertion(dragged, position, out var trailing, out var line))
        {
            CancelCollectionDragHover();
            ShowTrailingCollectionDropFeedback(line, trailing.Destination);
        }
    }

    private void BrowserCollectionTree_DragOver(object sender, System.Windows.DragEventArgs e)
    {
        var assets = e.Data.GetData(typeof(BrowserAssetDragPayload)) as BrowserAssetDragPayload;
        if (assets is not null)
        {
            CancelCollectionDragHover();
            ClearCollectionDropFeedback();
            var assetTarget = CollectionTreeItemAtHeader(e.GetPosition(BrowserCollectionTree))?.DataContext as BrowserCollectionNode;
            ClearAssetCollectionDropTargets();
            if (BrowserCollectionMembershipInteraction.CanDrop(assets, assetTarget)) assetTarget!.IsAssetDropTarget = true;
            e.Effects = BrowserCollectionMembershipInteraction.CanDrop(assets, assetTarget)
                ? System.Windows.DragDropEffects.Copy : System.Windows.DragDropEffects.None;
            e.Handled = true;
            return;
        }
        var dragged = e.Data.GetData(typeof(BrowserCollectionNode)) as BrowserCollectionNode;
        var container = CollectionTreeItemAtHeader(e.GetPosition(BrowserCollectionTree));
        var target = container?.DataContext as BrowserCollectionNode;
        var drop = dragged is not null && target is not null && container is not null
            ? BrowserCollectionInteraction.DropAt(dragged, target, HeaderRelativeY(e, container)) : null;
        if (dragged is not null && drop is { Kind: BrowserCollectionDropKind.InsertBefore or BrowserCollectionDropKind.InsertAfter } &&
            BrowserCollectionInteraction.ResolveInsertion(_browserCollectionTree.Roots, dragged, drop.Target, drop.Kind) is null)
            drop = null;
        if (dragged is not null && drop is null &&
            TryResolveTrailingInsertion(dragged, e.GetPosition(BrowserCollectionTree), out var trailing, out var line))
        {
            CancelCollectionDragHover();
            ShowTrailingCollectionDropFeedback(line, trailing.Destination);
            e.Effects = System.Windows.DragDropEffects.Move;
            e.Handled = true;
            return;
        }
        TrackCollectionDragHover(dragged, drop);
        ShowCollectionDropFeedback(container, dragged, drop);
        e.Effects = drop is { Kind: not BrowserCollectionDropKind.None }
            ? System.Windows.DragDropEffects.Move : System.Windows.DragDropEffects.None;
        e.Handled = true;
    }

    private static BrowserTreeNode? BrowserTreeNodeFromElement(DependencyObject? element)
    {
        while (element is not null)
        {
            if (element is FrameworkElement { DataContext: BrowserTreeNode node }) return node;
            element = VisualTreeHelper.GetParent(element);
        }
        return null;
    }

    private void BrowserCollectionTree_DragLeave(object sender, System.Windows.DragEventArgs e)
    {
        var position = e.GetPosition(BrowserCollectionTree);
        if (position.X >= 0 && position.Y >= 0 && position.X <= BrowserCollectionTree.ActualWidth &&
            position.Y <= BrowserCollectionTree.ActualHeight) return;
        CancelCollectionDragHover();
        ClearCollectionDropFeedback();
        ClearAssetCollectionDropTargets();
    }
    private async void BrowserCollectionTree_Drop(object sender, System.Windows.DragEventArgs e)
    {
        var assets = e.Data.GetData(typeof(BrowserAssetDragPayload)) as BrowserAssetDragPayload;
        if (assets is not null)
        {
            var assetTarget = CollectionTreeItemAtHeader(e.GetPosition(BrowserCollectionTree))?.DataContext as BrowserCollectionNode;
            ClearAssetCollectionDropTargets();
            if (!BrowserCollectionMembershipInteraction.CanDrop(assets, assetTarget)) { e.Handled = true; return; }
            await RunCollectionActionAsync(async () =>
            {
                var results = await _storage.Collections.AddMembershipsAsync(assetTarget!.Id, assets.AssetIds);
                var created = results.Count(result => result.Created);
                BrowserStatusText.Text = CollectionMembershipFeedback.ForAdd(created, results.Count - created,
                    assets.AssetIds.Count, 1, assetTarget.Name);
            });
            e.Handled = true;
            return;
        }
        var dragged = e.Data.GetData(typeof(BrowserCollectionNode)) as BrowserCollectionNode;
        var container = CollectionTreeItemAtHeader(e.GetPosition(BrowserCollectionTree));
        var target = container?.DataContext as BrowserCollectionNode;
        var drop = dragged is not null && target is not null && container is not null
            ? BrowserCollectionInteraction.DropAt(dragged, target, HeaderRelativeY(e, container)) : null;
        CancelCollectionDragHover();
        ClearCollectionDropFeedback();
        if (dragged is not null && drop is null &&
            TryResolveTrailingInsertion(dragged, e.GetPosition(BrowserCollectionTree), out var trailing, out _))
        {
            e.Handled = true;
            await ReorderCollectionNodeAsync(dragged, trailing.Destination);
            return;
        }
        if (dragged is null || drop is null || drop.Kind == BrowserCollectionDropKind.None) return;
        if (drop.Kind == BrowserCollectionDropKind.IntoSet) await MoveIntoCollectionSetAsync(dragged, drop.Target);
        else if (BrowserCollectionInteraction.ResolveInsertion(_browserCollectionTree.Roots, dragged, drop.Target, drop.Kind)
                 is { } destination)
            await ReorderCollectionNodeAsync(dragged, destination);
        e.Handled = true;
    }

    private void ClearAssetCollectionDropTargets()
    {
        foreach (var node in BrowserCollectionTreeModel.Flatten(_browserCollectionTree.Roots))
            node.IsAssetDropTarget = false;
    }

    private static double HeaderRelativeY(System.Windows.DragEventArgs e, TreeViewItem container) =>
        e.GetPosition(container).Y / Math.Max(1, Math.Min(BrowserCollectionRowHeight, container.ActualHeight));

    private double HeaderRelativeY(System.Windows.Point treePosition, TreeViewItem container)
    {
        var origin = container.TranslatePoint(new System.Windows.Point(0, 0), BrowserCollectionTree);
        return (treePosition.Y - origin.Y) /
            Math.Max(1, Math.Min(BrowserCollectionRowHeight, container.ActualHeight));
    }

    private TreeViewItem? CollectionTreeItemAtHeader(System.Windows.Point position) =>
        CollectionTreeItems(BrowserCollectionTree).FirstOrDefault(item =>
        {
            var origin = item.TranslatePoint(new System.Windows.Point(0, 0), BrowserCollectionTree);
            var row = new Rect(origin.X, origin.Y, Math.Max(0, BrowserCollectionTree.ActualWidth - origin.X),
                Math.Min(BrowserCollectionRowHeight, item.ActualHeight));
            return row.Contains(position);
        });

    private bool TryResolveTrailingInsertion(BrowserCollectionNode dragged, System.Windows.Point position,
        out BrowserCollectionInsertionChoice choice, out CollectionInsertionLine line)
    {
        choice = null!;
        line = null!;
        var containers = CollectionTreeItems(BrowserCollectionTree)
            .Where(container => container.DataContext is BrowserCollectionNode)
            .ToDictionary(container => ((BrowserCollectionNode)container.DataContext).Id);
        var lastItem = CollectionTreeItems(BrowserCollectionTree).LastOrDefault();
        if (lastItem is null) return false;
        var lastOrigin = lastItem.TranslatePoint(new System.Windows.Point(0, 0), BrowserCollectionTree);
        var lastBottom = lastOrigin.Y + Math.Min(BrowserCollectionRowHeight, lastItem.ActualHeight);
        if (position.X < 0 || position.X > BrowserCollectionTree.ActualWidth ||
            position.Y < lastBottom || position.Y > BrowserCollectionTree.ActualHeight) return false;

        var candidates = BrowserCollectionInteraction.ResolveTrailingInsertionChoices(
                _browserCollectionTree.Roots, dragged)
            .Where(candidate => containers.ContainsKey(candidate.Target.Id))
            .Select(candidate => (Choice: candidate,
                Indent: CollectionInsertionIndent(candidate, containers[candidate.Target.Id])))
            .ToArray();
        var active = BrowserCollectionInteraction.SelectTrailingInsertionChoice(candidates, position.X);
        if (active is null) return false;
        choice = active;
        var item = containers[active.Target.Id];
        line = new CollectionInsertionLine(item, active.Kind, active.Destination, lastBottom,
            CollectionInsertionIndent(active, item));
        return true;
    }

    private double CollectionInsertionIndent(BrowserCollectionInsertionChoice choice, TreeViewItem item)
    {
        var left = item.TranslatePoint(new System.Windows.Point(0, 0), BrowserCollectionTree).X;
        return choice.Destination.ParentSetId == choice.Target.Id ? left + BrowserCollectionIndent : left;
    }

    private static IEnumerable<TreeViewItem> CollectionTreeItems(ItemsControl parent)
    {
        foreach (var item in parent.Items)
        {
            if (parent.ItemContainerGenerator.ContainerFromItem(item) is not TreeViewItem container) continue;
            yield return container;
            if (!container.IsExpanded) continue;
            foreach (var child in CollectionTreeItems(container)) yield return child;
        }
    }

    private void TrackCollectionDragHover(BrowserCollectionNode? dragged, BrowserCollectionDrop? drop)
    {
        var changed = _collectionDragHover.Track(dragged, drop?.Target, drop?.Kind ?? BrowserCollectionDropKind.None,
            DateTimeOffset.UtcNow);
        if (_collectionDragHover.PendingTarget is null)
        {
            _collectionDragHoverTimer.Stop();
            return;
        }
        if (!changed) return;
        _collectionDragHoverTimer.Stop();
        _collectionDragHoverTimer.Start();
    }

    private void ExpandHoveredCollectionSet()
    {
        _collectionDragHoverTimer.Stop();
        if (_collectionDragHover.TakeReady(DateTimeOffset.UtcNow) is not { } target) return;
        target.IsExpanded = true;
        PersistCollectionExpansionState();
    }

    private void CancelCollectionDragHover()
    {
        _collectionDragHoverTimer.Stop();
        _collectionDragHover.Reset();
    }

    private async Task ReorderCollectionNodeAsync(BrowserCollectionNode dragged,
        BrowserCollectionInsertionDestination destination)
    {
        var destinationParent = destination.ParentSetId;
        var siblings = BrowserCollectionTreeModel.Flatten(_browserCollectionTree.Roots)
            .Where(node => node.ParentSetId == destinationParent && node.Id != dragged.Id)
            .OrderBy(node => node.Ordinal).ToList();
        await RunCollectionActionAsync(async () =>
        {
            var moved = dragged.ParentSetId == destinationParent
                ? HierarchyOrder(dragged)
                : dragged.IsSet
                    ? new CollectionHierarchyOrder(CollectionHierarchyItemKind.Set, dragged.Id,
                        (await _storage.Collections.ReparentSetAsync(dragged.Id, dragged.Revision, destinationParent)).Revision)
                    : new CollectionHierarchyOrder(CollectionHierarchyItemKind.Collection, dragged.Id,
                        (await _storage.Collections.ReparentCollectionAsync(dragged.Id, dragged.Revision, destinationParent)).Revision);
            var order = siblings.Select(HierarchyOrder).ToList();
            order.Insert(Math.Clamp(destination.Ordinal, 0, order.Count), moved);
            await _storage.Collections.ReorderHierarchyAsync(destinationParent, order);
            await RefreshCollectionsAsync(_activeCollectionScope?.Collection.CollectionId);
        });
    }

    private static CollectionHierarchyOrder HierarchyOrder(BrowserCollectionNode node) => new(
        node.IsSet ? CollectionHierarchyItemKind.Set : CollectionHierarchyItemKind.Collection,
        node.Id, node.Revision);

    private void ShowCollectionDropFeedback(TreeViewItem? item, BrowserCollectionNode? dragged,
        BrowserCollectionDrop? drop)
    {
        var kind = drop?.Kind ?? BrowserCollectionDropKind.None;
        if (item is null || kind == BrowserCollectionDropKind.None) { ClearCollectionDropFeedback(); return; }
        _collectionDropAdornerLayer ??= System.Windows.Documents.AdornerLayer.GetAdornerLayer(BrowserCollectionTree);
        if (_collectionDropAdornerLayer is null) return;
        if (_collectionDropAdorner is null)
        {
            _collectionDropAdorner = new CollectionDropAdorner(BrowserCollectionTree,
                (System.Windows.Media.Brush)FindResource("ShellFocusBrush"));
            _collectionDropAdornerLayer.Add(_collectionDropAdorner);
        }
        if (kind == BrowserCollectionDropKind.IntoSet)
        {
            _collectionDropAdorner.UpdateTarget(item);
            return;
        }
        if (dragged is null || drop is null ||
            BrowserCollectionInteraction.ResolveInsertion(_browserCollectionTree.Roots, dragged, drop.Target, kind)
                is not { } activeDestination)
        {
            ClearCollectionDropFeedback();
            return;
        }
        var containers = CollectionTreeItems(BrowserCollectionTree)
            .Where(container => container.DataContext is BrowserCollectionNode)
            .ToDictionary(container => ((BrowserCollectionNode)container.DataContext).Id);
        var lines = BrowserCollectionInteraction.ResolveInsertionChoices(
                _browserCollectionTree.Roots, dragged, drop.Target, kind)
            .Where(choice => choice.Destination == activeDestination)
            .Where(choice => containers.ContainsKey(choice.Target.Id))
            .Select(choice => new CollectionInsertionLine(containers[choice.Target.Id], choice.Kind, choice.Destination))
            .ToArray();
        if (lines.Length == 0) { ClearCollectionDropFeedback(); return; }
        _collectionDropAdorner.UpdateLines(lines, activeDestination);
    }

    private void ShowTrailingCollectionDropFeedback(CollectionInsertionLine line,
        BrowserCollectionInsertionDestination destination)
    {
        _collectionDropAdornerLayer ??= System.Windows.Documents.AdornerLayer.GetAdornerLayer(BrowserCollectionTree);
        if (_collectionDropAdornerLayer is null) return;
        if (_collectionDropAdorner is null)
        {
            _collectionDropAdorner = new CollectionDropAdorner(BrowserCollectionTree,
                (System.Windows.Media.Brush)FindResource("ShellFocusBrush"));
            _collectionDropAdornerLayer.Add(_collectionDropAdorner);
        }
        _collectionDropAdorner.UpdateLines([line], destination);
    }

    private void ClearCollectionDropFeedback()
    {
        if (_collectionDropAdorner is not null) _collectionDropAdornerLayer?.Remove(_collectionDropAdorner);
        _collectionDropAdorner = null;
        _collectionDropAdornerLayer = null;
    }

    private sealed record CollectionInsertionLine(TreeViewItem Item, BrowserCollectionDropKind Edge,
        BrowserCollectionInsertionDestination Destination, double? ExplicitY = null, double? ExplicitLeft = null);

    private sealed class CollectionDropAdorner(
        UIElement adornedElement, System.Windows.Media.Brush accent)
        : System.Windows.Documents.Adorner(adornedElement)
    {
        private Rect _row;
        private BrowserCollectionDropKind _kind;
        private IReadOnlyList<(Rect Row, BrowserCollectionDropKind Edge,
            BrowserCollectionInsertionDestination Destination, double? ExplicitY, double? ExplicitLeft)> _lines = [];
        private BrowserCollectionInsertionDestination? _activeDestination;

        public void UpdateTarget(TreeViewItem item)
        {
            var origin = item.TranslatePoint(new System.Windows.Point(0, 0), AdornedElement);
            _row = new Rect(origin.X, origin.Y, Math.Max(item.ActualWidth, AdornedElement.RenderSize.Width - origin.X),
                Math.Min(BrowserCollectionRowHeight, item.ActualHeight));
            _kind = BrowserCollectionDropKind.IntoSet;
            _lines = [];
            _activeDestination = null;
            InvalidateVisual();
        }

        public void UpdateLines(IReadOnlyList<CollectionInsertionLine> lines,
            BrowserCollectionInsertionDestination activeDestination)
        {
            _kind = BrowserCollectionDropKind.None;
            _row = Rect.Empty;
            _activeDestination = activeDestination;
            _lines = lines.Select(line =>
            {
                var origin = line.Item.TranslatePoint(new System.Windows.Point(0, 0), AdornedElement);
                var row = new Rect(origin.X, origin.Y,
                    Math.Max(line.Item.ActualWidth, AdornedElement.RenderSize.Width - origin.X),
                    Math.Min(BrowserCollectionRowHeight, line.Item.ActualHeight));
                return (row, line.Edge, line.Destination, line.ExplicitY, line.ExplicitLeft);
            }).ToArray();
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            if (_kind == BrowserCollectionDropKind.IntoSet)
            {
                var target = new Rect(_row.Left + 1.5, _row.Top + 1.5,
                    Math.Max(0, _row.Width - 3), Math.Max(0, _row.Height - 3));
                drawingContext.DrawRoundedRectangle(null, new System.Windows.Media.Pen(accent, 2), target, 5, 5);
                return;
            }
            foreach (var line in _lines)
            {
                var dual = _lines.Count > 1;
                var y = line.ExplicitY ??
                    (line.Edge == BrowserCollectionDropKind.InsertBefore ? line.Row.Top : line.Row.Bottom);
                if (dual) y += line.Edge == BrowserCollectionDropKind.InsertBefore ? 2 : -2;
                var active = line.Destination == _activeDestination;
                var pen = new System.Windows.Media.Pen(accent, active ? 4 : 2)
                {
                    StartLineCap = PenLineCap.Round,
                    EndLineCap = PenLineCap.Round
                };
                drawingContext.DrawLine(pen, new System.Windows.Point(line.ExplicitLeft ?? line.Row.Left, y),
                    new System.Windows.Point(line.Row.Right, y));
            }
        }

        protected override HitTestResult? HitTestCore(PointHitTestParameters hitTestParameters) => null;
    }

    private void BrowserCollectionsTopLevel_DragEnter(object sender, System.Windows.DragEventArgs e) =>
        BrowserCollectionsTopLevel_DragOver(e);
    private void BrowserCollectionsTopLevel_DragLeave(object sender, System.Windows.DragEventArgs e) =>
        ((FrameworkElement)sender).Opacity = 1;
    private void BrowserCollectionsTopLevel_DragOver(System.Windows.DragEventArgs e)
    {
        var dragged = e.Data.GetData(typeof(BrowserCollectionNode)) as BrowserCollectionNode;
        e.Effects = dragged is not null && BrowserCollectionInteraction.CanDrop(dragged, null)
            ? System.Windows.DragDropEffects.Move : System.Windows.DragDropEffects.None;
        BrowserCollectionsTopLevelDropTarget.Opacity = e.Effects == System.Windows.DragDropEffects.Move ? 0.65 : 1;
        e.Handled = true;
    }
    private async void BrowserCollectionsTopLevel_Drop(object sender, System.Windows.DragEventArgs e)
    {
        ((FrameworkElement)sender).Opacity = 1;
        var dragged = e.Data.GetData(typeof(BrowserCollectionNode)) as BrowserCollectionNode;
        if (dragged is null || !BrowserCollectionInteraction.CanDrop(dragged, null)) return;
        await ReparentCollectionNodeAsync(dragged, null);
        e.Handled = true;
    }

    private async Task ReparentCollectionNodeAsync(BrowserCollectionNode node, Guid? parent) =>
        await RunCollectionActionAsync(async () =>
        {
            if (node.IsSet) await _storage.Collections.ReparentSetAsync(node.Id, node.Revision, parent);
            else await _storage.Collections.ReparentCollectionAsync(node.Id, node.Revision, parent);
            await RefreshCollectionsAsync(_activeCollectionScope?.Collection.CollectionId);
        });

    private async Task MoveIntoCollectionSetAsync(BrowserCollectionNode node, BrowserCollectionNode target)
    {
        var expandAfterMove = !target.IsExpanded;
        await RunCollectionActionAsync(async () =>
        {
            // The Catalog reparent contracts append to the destination's mixed children. Direct row drops
            // intentionally use that default; exact child placement belongs to the insertion-line gesture.
            if (node.IsSet) await _storage.Collections.ReparentSetAsync(node.Id, node.Revision, target.Id);
            else await _storage.Collections.ReparentCollectionAsync(node.Id, node.Revision, target.Id);
            if (expandAfterMove)
            {
                target.IsExpanded = true;
                PersistCollectionExpansionState();
            }
            await RefreshCollectionsAsync(_activeCollectionScope?.Collection.CollectionId);
            if (expandAfterMove) await RevealCollectionNodeAsync(node.Id);
        });
    }

    private async Task RevealCollectionNodeAsync(Guid nodeId)
    {
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Loaded);
        BrowserCollectionTree.UpdateLayout();
        var node = BrowserCollectionTreeModel.Flatten(_browserCollectionTree.Roots)
            .FirstOrDefault(item => item.Id == nodeId);
        if (node is null) return;
        var container = CollectionTreeItems(BrowserCollectionTree)
            .FirstOrDefault(item => ReferenceEquals(item.DataContext, node));
        if (container is null) return;
        var visiblePosition = container.TranslatePoint(new System.Windows.Point(0, 0), BrowserFolderScrollViewer);
        var rowTop = BrowserFolderScrollViewer.VerticalOffset + visiblePosition.Y;
        var verticalOffset = BrowserTreeScroll.RevealVerticalOffset(BrowserFolderScrollViewer.VerticalOffset,
            BrowserFolderScrollViewer.ViewportHeight, rowTop, BrowserCollectionRowHeight);
        BrowserFolderScrollViewer.ScrollToVerticalOffset(
            Math.Min(verticalOffset, BrowserFolderScrollViewer.ScrollableHeight));
    }

    private static BrowserCollectionNode? CollectionNodeFromElement(DependencyObject? element)
    {
        while (element is not null)
        {
            if (element is FrameworkElement { DataContext: BrowserCollectionNode node }) return node;
            element = VisualTreeHelper.GetParent(element);
        }
        return null;
    }

    private BrowserCollectionNode? CollectionActionNode => _browserCollectionActionNode ?? _browserCollectionTree.SelectedNode;

    private async void AddMediaRoot_Click(object sender, RoutedEventArgs e)
    {
        if (!_storage.CatalogAvailable) return;
        var folder = PickFolder("Choose a Media Root folder", _settings.DefaultVideoFolder);
        if (folder is null) return;
        var suggested = new DirectoryInfo(folder).Name;
        var name = PromptForMediaRootName("Add Media Root", "Name this Media Root", suggested);
        if (name is null) return;
        var result = await _storage.MediaRoots.CreateAsync(name, folder);
        await ShowMediaRootResultAsync(result, "Media Root added.");
    }

    private async void RenameMediaRoot_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not MediaRootInfo root) return;
        var name = PromptForMediaRootName("Rename Media Root", "Media Root name", root.DisplayName);
        if (name is null) return;
        var result = await _storage.MediaRoots.RenameAsync(root.RootId, name);
        await ShowMediaRootResultAsync(result, "Media Root renamed.");
    }

    private async void ReconnectMediaRoot_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not MediaRootInfo root) return;
        var folder = PickFolder($"Reconnect {root.DisplayName}", root.PhysicalPath ?? _settings.DefaultVideoFolder);
        if (folder is null) return;
        var result = await _storage.MediaRoots.RemapAsync(root.RootId, folder);
        await ShowMediaRootResultAsync(result, "Media Root connected.");
    }

    private async Task ShowMediaRootResultAsync(MediaRootChangeResult result, string success)
    {
        SettingsMessage.Text = result.Succeeded ? success : result.Diagnostic;
        if (!result.Succeeded)
            MessageBox.Show(result.Diagnostic, "Media Root was not changed", MessageBoxButton.OK, MessageBoxImage.Warning);
        await RefreshMediaRootsAsync();
        await RefreshBrowserStorageAsync();
    }

    private string? PromptForMediaRootName(string title, string prompt, string initial)
    {
        var input = new System.Windows.Controls.TextBox { Text = initial, MinWidth = 320, Margin = new Thickness(0, 8, 0, 14) };
        var ok = new System.Windows.Controls.Button { Content = "Save", IsDefault = true, MinWidth = 82 };
        var cancel = new System.Windows.Controls.Button { Content = "Cancel", IsCancel = true, MinWidth = 82 };
        var buttons = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
        buttons.Children.Add(cancel); buttons.Children.Add(ok);
        var content = new System.Windows.Controls.StackPanel { Margin = new Thickness(20) };
        content.Children.Add(new System.Windows.Controls.TextBlock { Text = prompt, Foreground = (System.Windows.Media.Brush)FindResource("TextBrush") });
        content.Children.Add(input); content.Children.Add(buttons);
        var dialog = new Window
        {
            Title = title,
            Owner = this,
            Content = content,
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(23, 26, 32))
        };
        ok.Click += (_, _) => { if (!string.IsNullOrWhiteSpace(input.Text)) dialog.DialogResult = true; };
        input.SelectAll(); input.Focus();
        return dialog.ShowDialog() == true ? input.Text.Trim() : null;
    }

    private async void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        if (!TryReadEncodingControls(out var encoding, out var encodingError))
        {
            MessageBox.Show(encodingError, "Export settings", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!int.TryParse(SettingsPreviewCacheQuotaGb.Text, out var previewQuota) || previewQuota is < 1 or > 1024)
        {
            MessageBox.Show("Preview cache limit must be a whole number from 1 to 1024 GB.", "Preview settings",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var settings = ReadSettingsControls(encoding);
        if (!string.IsNullOrWhiteSpace(settings.FfmpegPath) && !File.Exists(settings.FfmpegPath))
        {
            MessageBox.Show("The configured FFmpeg executable does not exist.", "Settings", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            var previousCameraFolder = _settings.CameraLutFolder;
            var previousCreativeFolder = _settings.CreativeLutFolder;
            var previousCameraRecursive = _settings.CameraLutIncludeSubfolders;
            var previousCreativeRecursive = _settings.CreativeLutIncludeSubfolders;
            _storage.SaveSettings(settings);
            _settings = _storage.Settings;
            var cameraChanged = !string.Equals(previousCameraFolder, _settings.CameraLutFolder, StringComparison.OrdinalIgnoreCase)
                                || previousCameraRecursive != _settings.CameraLutIncludeSubfolders;
            var creativeChanged = !string.Equals(previousCreativeFolder, _settings.CreativeLutFolder, StringComparison.OrdinalIgnoreCase)
                                  || previousCreativeRecursive != _settings.CreativeLutIncludeSubfolders;
            var lutSettingsRevision = ++_lutSettingsRevision;
            ApplySettingsToBatch(settings);
            LocateTools();
            await RefreshDependencyHealthAsync();
            RefreshBatchFiles();
            var lutCount = await RefreshLutsAsync(cameraChanged, creativeChanged);
            if (lutSettingsRevision != _lutSettingsRevision) return;
            if (_playerViewerHost is not null)
                await _playerViewerHost.RefreshColorFoldersAsync(cameraChanged, creativeChanged);
            SettingsMessage.Text = $"Settings saved. {lutCount} LUT{(lutCount == 1 ? "" : "s")} available.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _activityLogFile.TryAppend($"[App] Could not save settings: {ex}");
            MessageBox.Show(ex.Message, "Could not save settings", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ResetSettings_Click(object sender, RoutedEventArgs e)
    {
        PopulateSettingsControls(AppSettings.Normalize(new AppSettings()));
        SettingsMessage.Text = "Default values loaded. Select Save Settings to apply them.";
    }

    private AppSettings ReadSettingsControls(EncodingOptions encoding)
    {
        var selectedPreset = (EncodingPreset)Math.Clamp(SettingsEncodingPreset.SelectedIndex, 0, 4);
        if (selectedPreset != EncodingPreset.Custom && encoding != EncodingPresetCatalog.Get(selectedPreset))
            selectedPreset = EncodingPreset.Custom;
        return AppSettings.Normalize(new AppSettings
        {
            DefaultVideoFolder = SettingsDefaultVideoFolder.Text,
            ScreengrabDirectory = SettingsScreengrabDirectory.Text,
            CameraLutFolder = SettingsCameraLutFolder.Text,
            CameraLutIncludeSubfolders = SettingsCameraLutIncludeSubfolders.IsChecked == true,
            CreativeLutFolder = SettingsCreativeLutFolder.Text,
            CreativeLutIncludeSubfolders = SettingsCreativeLutIncludeSubfolders.IsChecked == true,
            FfmpegPath = SettingsFfmpegPath.Text,
            DefaultResolution = (OutputResolution)SettingsResolution.SelectedIndex,
            DefaultRecovery = (RecoveryStrategy)SettingsRecoveryMode.SelectedIndex,
            IncludeSubfolders = SettingsRecursive.IsChecked == true,
            PreserveFolderStructure = SettingsPreserveFolderStructure.IsChecked == true,
            OverwriteExistingFiles = SettingsOverwriteExisting.IsChecked == true,
            DetailedActivityLogging = ShowEncodingDetails.IsChecked == true,
            EncodingPreset = selectedPreset,
            PreviewCacheQuotaGb = int.TryParse(SettingsPreviewCacheQuotaGb.Text, out var quota) ? quota : 20,
            Encoding = encoding
        });
    }

    private bool TryReadEncodingControls(out EncodingOptions options, out string error)
    {
        options = EncodingPresetCatalog.Recommended;
        error = "";
        if (!TryReadInt(SettingsEncoderPreset.Text, "NVENC preset", out var encoderPreset)
            || !TryReadInt(SettingsQuality.Text, "Quality", out var quality)
            || !TryReadInt(SettingsTargetBitrate.Text, "Target bitrate", out var targetBitrate)
            || !TryReadInt(SettingsMaxBitrate.Text, "Maximum bitrate", out var maxBitrate)
            || !TryReadInt(SettingsAqStrength.Text, "AQ strength", out var aqStrength)
            || !TryReadInt(SettingsAudioBitrate.Text, "AAC bitrate", out var audioBitrate))
        {
            error = _numericSettingError;
            return false;
        }

        options = new EncodingOptions
        {
            Backend = EncoderBackend.NvidiaNvenc,
            Codec = (VideoCodec)SettingsVideoCodec.SelectedIndex,
            EncoderPreset = encoderPreset,
            Tune = (EncoderTune)SettingsTune.SelectedIndex,
            RateControl = (RateControlMode)SettingsRateControl.SelectedIndex,
            Quality = quality,
            TargetBitrateMbps = targetBitrate,
            MaxBitrateMbps = maxBitrate,
            Multipass = (MultipassMode)SettingsMultipass.SelectedIndex,
            SpatialAq = SettingsSpatialAq.IsChecked == true,
            TemporalAq = SettingsTemporalAq.IsChecked == true,
            AqStrength = aqStrength,
            PixelFormat = (VideoPixelFormat)SettingsPixelFormat.SelectedIndex,
            FrameRate = FrameRateValues[Math.Clamp(SettingsFrameRate.SelectedIndex, 0, FrameRateValues.Length - 1)],
            Deinterlace = SettingsDeinterlace.IsChecked == true,
            AudioMode = (AudioEncodingMode)SettingsAudioMode.SelectedIndex,
            AudioBitrateKbps = audioBitrate,
            AudioSampleRate = AudioSampleRates[Math.Clamp(SettingsAudioSampleRate.SelectedIndex, 0, AudioSampleRates.Length - 1)],
            AudioChannels = Math.Clamp(SettingsAudioChannels.SelectedIndex, 0, 2),
            Container = (OutputContainer)SettingsContainer.SelectedIndex,
            FastStart = SettingsFastStart.IsChecked == true
        };
        var errors = EncodingOptionValidator.Validate(options);
        if (errors.Count == 0) return true;
        error = string.Join(Environment.NewLine, errors);
        return false;
    }

    private string _numericSettingError = "";
    private bool TryReadInt(string text, string label, out int value)
    {
        if (int.TryParse(text, out value)) return true;
        _numericSettingError = $"{label} must be a whole number.";
        return false;
    }

    private void ApplyEncodingPreset_Click(object sender, RoutedEventArgs e)
    {
        if (SettingsEncodingPreset.SelectedIndex == (int)EncodingPreset.Custom)
        {
            SettingsMessage.Text = "Custom settings are already displayed; choose a named preset to replace them.";
            return;
        }
        var preset = (EncodingPreset)Math.Clamp(SettingsEncodingPreset.SelectedIndex, 0, 3);
        PopulateEncodingControls(EncodingPresetCatalog.Get(preset));
        SettingsMessage.Text = $"{SettingsEncodingPreset.Text} preset loaded. Select Save Settings to apply it.";
    }
    private void PopulateSettingsControls(AppSettings settings)
    {
        SettingsCatalogDirectory.Text = _storage.Locations.CatalogDirectory;
        SettingsPreviewsDirectory.Text = _storage.Locations.PreviewsDirectory;
        SettingsPreviewCacheQuotaGb.Text = settings.PreviewCacheQuotaGb.ToString(CultureInfo.InvariantCulture);
        SettingsDefaultVideoFolder.Text = settings.DefaultVideoFolder;
        SettingsScreengrabDirectory.Text = settings.ScreengrabDirectory;
        SettingsCameraLutFolder.Text = settings.CameraLutFolder;
        SettingsCameraLutIncludeSubfolders.IsChecked = settings.CameraLutIncludeSubfolders;
        SettingsCreativeLutFolder.Text = settings.CreativeLutFolder;
        SettingsCreativeLutIncludeSubfolders.IsChecked = settings.CreativeLutIncludeSubfolders;
        SettingsFfmpegPath.Text = settings.FfmpegPath;
        SettingsResolution.SelectedIndex = (int)settings.DefaultResolution;
        SettingsRecoveryMode.SelectedIndex = (int)settings.DefaultRecovery;
        SettingsRecursive.IsChecked = settings.IncludeSubfolders;
        SettingsPreserveFolderStructure.IsChecked = settings.PreserveFolderStructure;
        SettingsOverwriteExisting.IsChecked = settings.OverwriteExistingFiles;
        ShowEncodingDetails.IsChecked = settings.DetailedActivityLogging;
        SettingsEncodingPreset.SelectedIndex = (int)settings.EncodingPreset;
        PopulateEncodingControls(settings.Encoding);
        RefreshSettingsPathStatuses();
    }

    private sealed record CatalogBackupDisplay(CatalogBackup Backup, string DisplayName);

    private void RefreshCatalogBackups()
    {
        CatalogBackupSelection.ItemsSource = _storage.CatalogBackups
            .Select(x => new CatalogBackupDisplay(x, $"{x.CreatedUtc.LocalDateTime:g} — {x.Kind} — schema {x.SchemaVersion}"))
            .ToArray();
        CatalogBackupSelection.SelectedIndex = CatalogBackupSelection.Items.Count > 0 ? 0 : -1;
    }

    private async void BackupCatalog_Click(object sender, RoutedEventArgs e)
    {
        SettingsMessage.Text = "Creating and validating Catalog backup…";
        var result = await _storage.BackupCatalogAsync();
        SettingsMessage.Text = result.Succeeded ? "Catalog backup created and validated." : result.Diagnostic;
        if (!result.Succeeded) MessageBox.Show(result.Diagnostic, "Catalog backup failed", MessageBoxButton.OK, MessageBoxImage.Error);
        RefreshCatalogBackups();
    }

    private async void RestoreCatalog_Click(object sender, RoutedEventArgs e)
    {
        if (CatalogBackupSelection.SelectedItem is not CatalogBackupDisplay selected) return;
        if (MessageBox.Show("Restore this validated backup? Lightflow will protect the current Catalog first. Previews are not changed.",
            "Restore Catalog", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        SettingsMessage.Text = "Validating and restoring Catalog…";
        var result = await _storage.RestoreCatalogAsync(selected.Backup.Path);
        SettingsMessage.Text = result.Diagnostic ?? (result.Succeeded ? "Catalog restored successfully." : "Catalog restore failed.");
        MessageBox.Show(SettingsMessage.Text, result.Succeeded ? "Catalog restored" : "Catalog restore failed",
            MessageBoxButton.OK, result.Succeeded ? MessageBoxImage.Information : MessageBoxImage.Error);
        RefreshCatalogBackups();
        if (result.Succeeded) await RefreshMediaRootsAsync();
    }

    private void PopulateEncodingControls(EncodingOptions options)
    {
        SettingsEncoderBackend.SelectedIndex = 0;
        SettingsVideoCodec.SelectedIndex = (int)options.Codec;
        SettingsContainer.SelectedIndex = (int)options.Container;
        SettingsAudioMode.SelectedIndex = (int)options.AudioMode;
        SettingsEncoderPreset.Text = options.EncoderPreset.ToString();
        SettingsTune.SelectedIndex = (int)options.Tune;
        SettingsRateControl.SelectedIndex = (int)options.RateControl;
        SettingsMultipass.SelectedIndex = (int)options.Multipass;
        SettingsQuality.Text = options.Quality.ToString();
        SettingsTargetBitrate.Text = options.TargetBitrateMbps.ToString();
        SettingsMaxBitrate.Text = options.MaxBitrateMbps.ToString();
        SettingsAqStrength.Text = options.AqStrength.ToString();
        SettingsPixelFormat.SelectedIndex = (int)options.PixelFormat;
        SettingsFrameRate.SelectedIndex = Array.IndexOf(FrameRateValues, options.FrameRate) is var frameIndex && frameIndex >= 0 ? frameIndex : 0;
        SettingsAudioBitrate.Text = options.AudioBitrateKbps.ToString();
        SettingsAudioSampleRate.SelectedIndex = Array.IndexOf(AudioSampleRates, options.AudioSampleRate) is var sampleIndex && sampleIndex >= 0 ? sampleIndex : 0;
        SettingsAudioChannels.SelectedIndex = options.AudioChannels;
        SettingsDeinterlace.IsChecked = options.Deinterlace;
        SettingsSpatialAq.IsChecked = options.SpatialAq;
        SettingsTemporalAq.IsChecked = options.TemporalAq;
        SettingsFastStart.IsChecked = options.FastStart;
    }

    private void ApplySettingsToBatch(AppSettings settings)
    {
        InputFolder.Text = settings.DefaultVideoFolder;
        Resolution.SelectedIndex = (int)settings.DefaultResolution;
        RecoveryMode.SelectedIndex = (int)settings.DefaultRecovery;
        Recursive.IsChecked = settings.IncludeSubfolders;
        PreserveFolderStructure.IsChecked = settings.PreserveFolderStructure;
        OverwriteExisting.IsChecked = settings.OverwriteExistingFiles;
        OutputMode.SelectedIndex = (int)OutputDestinationMode.Subfolder;
        OutputSpecificFolder.Text = "";
        SetResolutionSubfolderName();
        SetResolutionFilenameSuffix();
        UpdateOutputModeUi();
        if (IsLoaded) RefreshBatchFiles();
    }

    private void ApplyStateToBatch(AppState state)
    {
        if (!state.HasBatchState) return;
        InputFolder.Text = state.LastVideoFolder;
        Resolution.SelectedIndex = (int)state.LastResolution;
        RecoveryMode.SelectedIndex = (int)state.LastRecovery;
        Recursive.IsChecked = state.LastIncludeSubfolders;
        PreserveFolderStructure.IsChecked = state.LastPreserveFolderStructure;
        OverwriteExisting.IsChecked = state.LastOverwriteExistingFiles;
        OutputMode.SelectedIndex = (int)state.LastOutputMode;
        OutputSpecificFolder.Text = state.LastSpecificOutputFolder;
        _subfolderUsesResolutionDefault = state.LastOutputSubfolderUsesResolutionDefault;
        _filenameSuffixUsesResolutionDefault = state.LastFilenameSuffixUsesResolutionDefault;
        if (_subfolderUsesResolutionDefault) SetResolutionSubfolderName();
        else OutputSubfolderName.Text = state.LastOutputSubfolder;
        if (_filenameSuffixUsesResolutionDefault) SetResolutionFilenameSuffix();
        else OutputFilenameSuffix.Text = state.LastFilenameSuffix;
        UpdateOutputModeUi();
    }

    private void SaveBatchState()
    {
        _state = _state with
        {
            HasBatchState = true,
            LastVideoFolder = InputFolder.Text,
            LastResolution = (OutputResolution)Math.Clamp(Resolution.SelectedIndex, 0, 5),
            LastRecovery = (RecoveryStrategy)Math.Clamp(RecoveryMode.SelectedIndex, 0, 2),
            LastIncludeSubfolders = Recursive.IsChecked == true,
            LastPreserveFolderStructure = PreserveFolderStructure.IsChecked == true,
            LastOverwriteExistingFiles = OverwriteExisting.IsChecked == true,
            LastOutputMode = (OutputDestinationMode)Math.Clamp(OutputMode.SelectedIndex, 0, 2),
            LastOutputSubfolder = OutputSubfolderName.Text,
            LastOutputSubfolderUsesResolutionDefault = _subfolderUsesResolutionDefault,
            LastFilenameSuffix = OutputFilenameSuffix.Text,
            LastFilenameSuffixUsesResolutionDefault = _filenameSuffixUsesResolutionDefault,
            LastSpecificOutputFolder = OutputSpecificFolder.Text
        };
        try { AppStateStore.Save(_storage.Locations.StatePath, _state); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { AppendDetailedLog($"Could not remember batch choices: {ex.Message}"); }
    }

    private OutputDestinationOptions CurrentOutputDestination() => new(
        (OutputDestinationMode)Math.Clamp(OutputMode.SelectedIndex, 0, 2),
        _subfolderUsesResolutionDefault ? "" : OutputSubfolderName.Text,
        OutputSpecificFolder.Text,
        _filenameSuffixUsesResolutionDefault ? "" : OutputFilenameSuffix.Text);

    private bool ShouldPreserveFolderStructure() => FolderStructurePolicy.ShouldPreserve(
        Recursive.IsChecked == true,
        (OutputDestinationMode)Math.Clamp(OutputMode.SelectedIndex, 0, 2),
        PreserveFolderStructure.IsChecked == true);
    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        if (!ValidateEncoderInputs()) return;
        SaveBatchState();
        _closeAfterCurrent = false;
        _jobCancellation = new JobCancellation();
        ToggleEncoding(true);

        var total = 0;
        var outputRoot = "";
        var outcome = "completed";
        Stopwatch? batchStart = null;
        JobExecution<EncodingJobOptions, EncodingItemResult>? execution = null;
        JobItemExecution<EncodingItemResult>? currentItem = null;
        EncodingOutputLifecycle? currentOutput = null;

        try
        {
            var sources = _batchFiles.Where(file => file.IsSelected).ToList();
            if (sources.Count == 0) throw new InvalidOperationException("Select at least one video file for this batch.");
            total = sources.Count;
            _batchProgress.StartBatch(total);
            ApplyProgressState();

            var recovery = (RecoveryStrategy)RecoveryMode.SelectedIndex;
            var resolution = (OutputResolution)Resolution.SelectedIndex;
            batchStart = Stopwatch.StartNew();
            _batchStopwatch = batchStart;
            var startedAt = DateTime.Now;

            CurrentFileText.Text = $"Analyzing {total} file{(total == 1 ? "" : "s")}…";
            AppendLog($"Preparing batch — {total} file{(total == 1 ? "" : "s")} discovered.");
            var durations = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            var resolvedRanges = new Dictionary<string, ResolvedMediaRange>(StringComparer.OrdinalIgnoreCase);
            foreach (var source in sources)
            {
                _jobCancellation.Token.ThrowIfCancellationRequested();
                var analyzedDuration = source.Metadata?.DurationSeconds ?? 0;
                durations[source.FilePath] = analyzedDuration > 0
                    ? analyzedDuration
                    : await ProbeDurationAsync(source.FilePath, _jobCancellation.Token);
                if (source.TrimRange is { } trim)
                {
                    var currentIdentity = TrimSourceIdentity.Read(source.FilePath);
                    if (source.SourceIdentity is null || currentIdentity is null || !source.SourceIdentity.Matches(currentIdentity))
                        throw new InvalidOperationException($"{source.DisplayName} changed after its trim was selected. Reopen Trim and choose the range again.");
                    var startTimestamp = source.Metadata?.StartTimestamp ?? TimeSpan.Zero;
                    CurrentFileText.Text = $"Analyzing trim boundaries for {source.DisplayName}…";
                    var timestampProbe = await CaptureAsync(_ffprobe!, FfmpegCommandBuilder.ProbeVideoPackets(source.FilePath, trim, startTimestamp), _jobCancellation.Token);
                    if (timestampProbe.ExitCode != 0)
                        throw new InvalidOperationException($"Could not validate the saved trim for {source.DisplayName}.");
                    try
                    {
                        resolvedRanges[source.FilePath] = EncodingRangeResolver.Resolve(trim, startTimestamp, timestampProbe.StdOut);
                    }
                    catch (Exception exception) when (exception is ArgumentException or InvalidDataException)
                    {
                        AppendDetailedLog($"Packet timestamps were insufficient for {source.DisplayName}; checking decoded frame timestamps.");
                        var frameProbe = await CaptureAsync(_ffprobe!, FfmpegCommandBuilder.ProbeVideoFrames(source.FilePath, trim, startTimestamp), _jobCancellation.Token);
                        if (frameProbe.ExitCode != 0) throw;
                        resolvedRanges[source.FilePath] = EncodingRangeResolver.Resolve(trim, startTimestamp, frameProbe.StdOut);
                    }
                }
            }
            var plan = CreateEncodingPlan(durations, resolvedRanges);
            if (!plan.IsValid)
                throw new InvalidOperationException(plan.Issues.Concat(plan.Items.SelectMany(item => item.Issues))
                    .First(issue => issue.Severity == JobIssueSeverity.Error).Message);
            execution = new JobExecution<EncodingJobOptions, EncodingItemResult>(plan);
            _activeEncodingJob = execution;
            execution.MarkStarted();
            execution.Queue();
            outputRoot = plan.Definition.Options.OutputRoot;
            Directory.CreateDirectory(outputRoot);

            var sourceDuration = TimeSpan.FromSeconds(plan.Items
                .Sum(item => item.Definition.MediaRange?.EffectiveDuration.TotalSeconds ?? 0));
            AppendLog(BatchLogFormatter.Started(total, outputRoot, resolution, recovery, sourceDuration, startedAt));
            AppendDetailedLog($"LUT: {(string.IsNullOrEmpty(SelectedLutPath) ? "None" : SelectedLutPath)}");
            AppendDetailedLog($"Input folder: {InputFolder.Text}");
            AppendDetailedLog($"Encoder: {_settings.Encoding.Codec} via NVIDIA NVENC; preset P{_settings.Encoding.EncoderPreset}; {_settings.Encoding.RateControl}; {_settings.Encoding.Container}");
            AppendDetailedLog($"Scanning subfolders: {(Recursive.IsChecked == true ? "Yes" : "No")}; preserve folder structure: {(ShouldPreserveFolderStructure() ? "Yes" : "No")}; overwrite existing files: {(OverwriteExisting.IsChecked == true ? "Yes" : "No")}");

            if (JobsRuntimeEnabled)
            {
                execution = null;
                _activeEncodingJob = null;
                AppendDetailedLog($"Parallel exports: {plan.Definition.Options.ParallelExports} file-level worker{(plan.Definition.Options.ParallelExports == 1 ? "" : "s")}");
                var runtimeExecutor = new EncodingJobExecutor(_ffmpeg!, _ffprobe!,
                    _storage.Locations.OutputIdentityDirectory,
                    diagnostic: line => _activityLogFile.TryAppend(line));
                _activeJobExecutor = runtimeExecutor;
                var runtime = _jobsRuntime.Queue(plan, plan.Definition.Options.ParallelExports,
                    (item, progress, token) => runtimeExecutor.ExecuteAsync(item, plan.Definition.Options, progress, token));
                _activeJobRuntime = runtime;
                PauseButton.IsEnabled = true;
                using var cancelRegistration = _jobCancellation.Token.Register(runtime.Cancel);
                var result = await runtime.Completion;
                outcome = result.State switch
                {
                    JobState.Cancelled => "cancelled",
                    JobState.Failed => "failed",
                    JobState.CompletedWithWarnings => "completed with warnings",
                    _ => "completed"
                };
                foreach (var itemResult in result.Items)
                {
                    var planned = plan.Items.First(item => item.Definition.Id == itemResult.ItemId);
                    var output = itemResult.OutputPaths.FirstOrDefault() ?? planned.OutputPaths.FirstOrDefault() ?? "output";
                    AppendLog(itemResult.State switch
                    {
                        JobState.Completed => $"Completed: {output}",
                        JobState.CompletedWithWarnings => $"Completed with warnings: {output}",
                        JobState.Skipped => $"Preserved existing file: {output}",
                        JobState.Cancelled => $"Cancelled: {Path.GetFileName(planned.Definition.SourceIdentity)}",
                        _ => $"FAILED: {planned.Definition.SourceIdentity} — {itemResult.Errors.FirstOrDefault() ?? "Unknown error"}"
                    });
                }
                AppendLog(BatchLogFormatter.Finished(outcome, total,
                    result.Summary.Completed + result.Summary.CompletedWithWarnings,
                    result.Summary.Failed, result.Summary.Skipped, batchStart.Elapsed, outputRoot));
                _jobHistory.Add(new EncodingJobHistoryRecord(
                    plan.Definition.Id, plan.Definition.Capability, plan.Definition.CreatedAt,
                    result.StartedAt, result.CompletedAt, result.State, plan.Definition, plan, result));
                RefreshHistory();
                CurrentFileText.Text = JobRuntimeStatusPresentation.Describe(runtime.Snapshot());

                // The runtime owns the authoritative execution/result; suppress the legacy adapter's
                // result path in this method's shared finally block.
                execution = null;
                batchStart = null;
                _activeJobRuntime = null;
                _activeJobExecutor = null;
                return;
            }

            var completed = 0;
            foreach (var item in execution.Items)
            {
                currentOutput = null;
                _jobCancellation.Token.ThrowIfCancellationRequested();
                currentItem = item;
                var input = item.PlanItem.Definition.SourceIdentity;
                var output = item.PlanItem.OutputPaths.Single();
                var duration = item.PlanItem.Definition.MediaRange?.EffectiveDuration.TotalSeconds
                               ?? durations.GetValueOrDefault(input);
                _batchProgress.StartFile();
                FileProgress.Value = _batchProgress.FilePercent;
                CurrentFileText.Text = $"{completed + 1}/{total}: {Path.GetFileName(input)}";
                AppendDetailedLog($"File {completed + 1} of {total}: {input}");
                AppendDetailedLog($"Output: {output}");
                AppendDetailedLog($"Detected duration: {FormatDuration(duration)}");
                if (item.PlanItem.Definition.ResolvedRange is { } appliedRange)
                    AppendDetailedLog($"Applied trim: In {appliedRange.RequestedRange.EffectiveIn:c}; Out frame {appliedRange.RequestedRange.EffectiveOut:c}; exported duration {appliedRange.EffectiveDuration:c}; source start {appliedRange.SourceStartTimestamp:c}");

                if (item.State == JobState.Skipped)
                {
                    completed++;
                    AppendLog($"Preserved existing file: {output}");
                    foreach (var warning in item.PlanItem.Issues.Where(issue => issue.Severity == JobIssueSeverity.Warning))
                        AppendLog($"Warning: {warning.Message}");
                    UpdateBatch(execution, item, completed, total, batchStart);
                    currentOutput = null;
                    if (_closeAfterCurrent)
                    {
                        outcome = "stopped after current file";
                        execution.CancelPending();
                        break;
                    }
                    continue;
                }

                item.Start();
                Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                var outputLifecycle = new EncodingOutputLifecycle(output, input);
                currentOutput = outputLifecycle;
                outputLifecycle.Prepare();
                if (outputLifecycle.RemovedStalePartial)
                    AppendDetailedLog($"Removed stale Lightflow partial output: {outputLifecycle.PartialPath}");
                AppendDetailedLog($"Partial output: {outputLifecycle.PartialPath}");
                var encodingOptions = plan.Definition.Options;
                var detailedOutput = encodingOptions.DetailedOutput;
                var colorLuts = encodingOptions.ColorMode == EncodingColorMode.Assigned
                    && item.PlanItem.Definition.AssignedColor is { ColorEnabled: true } color
                    ? color.OrderedPipeline.Select(resource => new EncodingLutResourceStore(
                        EncodingLutResourceStore.DefaultDirectory).Resolve(resource)).ToArray()
                    : [];
                var manualLut = encodingOptions.ColorMode == EncodingColorMode.OriginalOrManual
                    ? encodingOptions.LutPath : null;
                var args = FfmpegCommandBuilder.Encode(input, outputLifecycle.PartialPath, manualLut,
                    encodingOptions.Recovery, encodingOptions.Resolution, detailedOutput, encodingOptions.Encoding,
                    item.PlanItem.Definition.ResolvedRange, colorLuts);
                AppendDetailedLog(colorLuts.Length == 0 ? "Color: Original"
                    : $"Color: {string.Join(" -> ", item.PlanItem.Definition.AssignedColor!.OrderedPipeline.Select(resource => $"{EncodingLutResourceStore.StageName(resource.Stage)} {resource.DisplayName} [{resource.ContentSha256}]"))}");
                AppendDetailedLog($"Starting FFmpeg: {FormatCommand(_ffmpeg!, args)}");
                CurrentFileText.Text = item.PlanItem.Definition.ResolvedRange is null
                    ? $"Starting {completed + 1}/{total}: {Path.GetFileName(input)}…"
                    : $"Starting {completed + 1}/{total}: seeking to the selected range in {Path.GetFileName(input)}…";
                var exit = await RunFfmpegProgressAsync(args, duration, detailedOutput, p =>
                {
                    CurrentFileText.Text = $"{completed + 1}/{total}: {Path.GetFileName(input)}";
                    item.ReportProgress(p);
                    _batchProgress.ReportFileProgress(p);
                    FileProgress.Value = _batchProgress.FilePercent;
                    UpdateBatch(execution, item, completed, total, batchStart);
                }, _jobCancellation.Token);
                if (exit == 0)
                {
                    var validation = await CaptureAsync(_ffprobe!, FfmpegCommandBuilder.ProbeOutput(outputLifecycle.PartialPath), _jobCancellation.Token);
                    var expectsAudio = encodingOptions.Recovery != RecoveryStrategy.VideoOnly
                        && encodingOptions.Encoding.AudioMode != AudioEncodingMode.None
                        && item.PlanItem.Definition.SourceHasAudio != false;
                    var validationError = "FFprobe could not open the exported file.";
                    if (validation.ExitCode == 0 && EncodedOutputValidator.TryValidate(validation.StdOut,
                            item.PlanItem.Definition.MediaRange?.EffectiveDuration ?? TimeSpan.FromSeconds(duration), expectsAudio, out validationError))
                    {
                        AppendDetailedLog($"Validated partial output: {outputLifecycle.PartialPath}");
                        outputLifecycle.FinalizeValidatedOutput();
                        currentOutput = null;
                        AppendDetailedLog($"Finalized output: {output}");
                        var itemResult = new EncodingItemResult(exit,
                            item.PlanItem.Definition.ResolvedRange?.RequestedRange.SourceDuration ?? item.PlanItem.Definition.MediaRange?.SourceDuration,
                            item.PlanItem.Definition.ResolvedRange?.RequestedRange,
                            item.PlanItem.Definition.MediaRange?.EffectiveDuration);
                        try
                        {
                            EncodingOutputIdentityStore.Save(output, EncodingOutputIdentity.Create(item.PlanItem.Definition, encodingOptions));
                            item.Complete(itemResult);
                        }
                        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                        {
                            item.CompleteWithWarnings(["The output is valid, but its resume identity could not be saved."], itemResult);
                            AppendLog($"Warning: resume information could not be saved for {output}: {exception.Message}");
                        }
                        AppendLog($"Completed: {output}");
                    }
                    else
                    {
                        var reason = validationError;
                        var cleanupWarning = outputLifecycle.CleanupFailedAttempt();
                        currentOutput = null;
                        item.Fail(reason, new EncodingItemResult(exit,
                            item.PlanItem.Definition.MediaRange?.SourceDuration,
                            item.PlanItem.Definition.ResolvedRange?.RequestedRange,
                            item.PlanItem.Definition.MediaRange?.EffectiveDuration));
                        AppendLog($"FAILED validation: {input} — {reason}");
                        if (cleanupWarning is not null) AppendLog($"Warning: {cleanupWarning}");
                    }
                }
                else
                {
                    var cleanupWarning = outputLifecycle.CleanupFailedAttempt();
                    currentOutput = null;
                    item.Fail($"FFmpeg exited with code {exit}.", new EncodingItemResult(exit,
                        item.PlanItem.Definition.MediaRange?.SourceDuration,
                        item.PlanItem.Definition.ResolvedRange?.RequestedRange,
                        item.PlanItem.Definition.MediaRange?.EffectiveDuration));
                    AppendLog($"FAILED ({exit}): {input}");
                    if (cleanupWarning is not null) AppendLog($"Warning: {cleanupWarning}");
                }

                completed++;
                UpdateBatch(execution, item, completed, total, batchStart);
                currentItem = null;
                currentOutput = null;
                if (_closeAfterCurrent)
                {
                    outcome = "stopped after current file";
                    execution.CancelPending();
                    break;
                }
            }

            CurrentFileText.Text = outcome == "completed" ? "Batch complete" : "Current file complete — closing";
        }
        catch (OperationCanceledException)
        {
            outcome = "cancelled";
            if (currentOutput is not null)
            {
                var activeOutput = currentOutput;
                currentOutput = null;
                var cleanupWarning = activeOutput.CleanupFailedAttempt();
                if (cleanupWarning is not null) AppendLog($"Warning: {cleanupWarning}");
                else AppendDetailedLog($"Removed cancelled partial output: {activeOutput.PartialPath}");
            }
            if (currentItem?.State == JobState.Running) currentItem.Cancel();
            execution?.CancelPending();
            AppendLog("Export cancelled.");
            CurrentFileText.Text = "Cancelled";
        }
        catch (Exception ex)
        {
            outcome = "failed";
            if (currentOutput is not null)
            {
                var activeOutput = currentOutput;
                currentOutput = null;
                var cleanupWarning = activeOutput.CleanupFailedAttempt();
                if (cleanupWarning is not null) AppendLog($"Warning: {cleanupWarning}");
            }
            if (currentItem?.State == JobState.Running) currentItem.Fail(ex.Message);
            execution?.CancelPending();
            MessageBox.Show(ex.Message, "Export error", MessageBoxButton.OK, MessageBoxImage.Error);
            AppendLog(ex.ToString());
        }
        finally
        {
            if (batchStart is not null && total > 0 && execution is not null)
            {
                execution.CancelPending();
                var result = execution.Result();
                AppendLog(BatchLogFormatter.Finished(outcome, total,
                    result.Summary.Completed + result.Summary.CompletedWithWarnings,
                    result.Summary.Failed,
                    result.Summary.Skipped,
                    batchStart.Elapsed,
                    outputRoot));
                try
                {
                    _jobHistory.Add(new EncodingJobHistoryRecord(
                        execution.Plan.Definition.Id,
                        execution.Plan.Definition.Capability,
                        execution.Plan.Definition.CreatedAt,
                        result.StartedAt,
                        result.CompletedAt,
                        result.State,
                        execution.Plan.Definition,
                        execution.Plan,
                        result));
                    RefreshHistory();
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    AppendLog($"Could not save these Jobs for later: {exception.Message}");
                }
            }

            var shouldClose = _closeAfterCurrent;
            _batchStopwatch = null;
            _closeAfterCurrent = false;
            _activeEncodingJob = null;
            _activeJobRuntime = null;
            _activeJobExecutor = null;
            _jobCancellation.Dispose();
            _jobCancellation = null;
            ToggleEncoding(false);
            if (shouldClose)
            {
                _forceClose = true;
                _ = Dispatcher.BeginInvoke(new Action(Close));
            }
        }
    }

    private void RefreshHistory()
    {
        _durableHistoryRecords = _jobHistory.Load();
        RefreshJobsWorkspace();
    }

    private void RefreshJobsWorkspace()
    {
        if (HistoryList is null) return;
        var selectedIds = HistoryList.SelectedItems.Cast<JobsWorkspaceItem>().Select(item => item.JobId).ToHashSet();
        var focusedJobId = FocusedJobsWorkspaceItem()?.JobId;
        var filter = (JobsWorkspaceFilter)Math.Clamp(JobsFilter?.SelectedIndex ?? 0, 0, 7);
        var projected = JobsWorkspacePresentation.Project(_exportScheduler.Jobs, _durableHistoryRecords,
            JobsSearchText?.Text, filter, _deletedFullJobsTerminalJobIds);
        FullJobsMaximumExports.SelectedIndex = _exportScheduler.MaxSimultaneousExports - EncodingJobConcurrency.Minimum;
        ReconcileJobsWorkspace(projected);
        HistoryEmptyText.Visibility = _historyRecords.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        RestoreJobsSelection(JobsWorkspacePresentation.SurvivingSelection(selectedIds, _historyRecords), focusedJobId);
    }

    private JobsWorkspaceItem? FocusedJobsWorkspaceItem()
    {
        for (var current = Keyboard.FocusedElement as DependencyObject; current is not null;
             current = VisualTreeHelper.GetParent(current))
        {
            if (current is not ListBoxItem container) continue;
            return ItemsControl.ItemsControlFromItemContainer(container) == HistoryList
                ? container.DataContext as JobsWorkspaceItem
                : null;
        }
        return null;
    }

    private void RestoreJobsSelection(IReadOnlySet<Guid> selectedIds, Guid? focusedJobId)
    {
        _synchronizingJobsSelection = true;
        try
        {
            HistoryList.SelectedItems.Clear();
            foreach (var item in _historyRecords.Where(item => selectedIds.Contains(item.JobId)))
                HistoryList.SelectedItems.Add(item);
        }
        finally { _synchronizingJobsSelection = false; }
        ApplyJobsSelection();
        if (focusedJobId is not { } id) return;
        var focusedItem = _historyRecords.FirstOrDefault(item => item.JobId == id);
        if (focusedItem is not null && HistoryList.ItemContainerGenerator.ContainerFromItem(focusedItem) is ListBoxItem container)
            container.Focus();
    }

    private void ReconcileJobsWorkspace(IReadOnlyList<JobsWorkspaceItem> next)
    {
        for (var index = _historyRecords.Count - 1; index >= 0; index--)
            if (!next.Any(item => item.JobId == _historyRecords[index].JobId
                && item.HistoryRecordId == _historyRecords[index].HistoryRecordId)) _historyRecords.RemoveAt(index);
        for (var index = 0; index < next.Count; index++)
        {
            var existing = _historyRecords.FirstOrDefault(item => item.JobId == next[index].JobId
                && item.HistoryRecordId == next[index].HistoryRecordId);
            if (existing is null) _historyRecords.Insert(index, next[index]);
            else
            {
                var currentIndex = _historyRecords.IndexOf(existing);
                if (currentIndex != index) _historyRecords.Move(currentIndex, index);
                if (_historyRecords[index] != next[index]) _historyRecords[index] = next[index];
            }
        }
    }

    private void HistoryList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (!_synchronizingJobsSelection) ApplyJobsSelection();
    }

    private JobsSelectionEligibility CurrentJobsSelection() =>
        JobsSelectionEligibility.For(HistoryList.SelectedItems.Cast<JobsWorkspaceItem>());

    private void ApplyJobsSelection()
    {
        var selection = CurrentJobsSelection();
        var item = selection.IsSingle ? selection.Items[0] : null;
        HistoryDetails.Text = selection.Items.Count > 1 ? $"{selection.Items.Count} Jobs selected. Actions apply only when every selected Job is eligible."
            : item?.Details ?? "Select a job to inspect its results.";
        HistoryRerunButton.IsEnabled = item?.CanReviewAndRerun == true;
        JobsPauseButton.IsEnabled = selection.CanPause;
        JobsResumeButton.IsEnabled = selection.CanResume;
        JobsRetryButton.IsEnabled = item?.CanRetry == true;
        JobsCancelButton.IsEnabled = selection.CanCancel;
        JobsClearHistoryButton.IsEnabled = selection.CanClearHistory;
        JobsMoveEarlierButton.IsEnabled = item?.CanReorder == true;
        JobsMoveLaterButton.IsEnabled = item?.CanReorder == true;
        JobsRevealOutputButton.IsEnabled = item is not null && File.Exists(item.OutputPath);
        JobsPauseButton.Content = selection.Items.Count > 1 ? "Pause selected" : "Pause";
        JobsResumeButton.Content = selection.Items.Count > 1 ? "Resume selected" : "Resume";
        JobsPauseButton.ToolTip = selection.Items.Count > 1
            ? "Hold all selected Waiting Jobs before they start"
            : "Hold this Waiting Job before it starts";
        JobsResumeButton.ToolTip = selection.Items.Count > 1
            ? "Return all selected individually paused Jobs to the eligible queue"
            : "Return this individually paused Job to the eligible queue";
        AutomationProperties.SetHelpText(JobsPauseButton, JobsPauseButton.ToolTip.ToString()!);
        AutomationProperties.SetHelpText(JobsResumeButton, JobsResumeButton.ToolTip.ToString()!);
        JobsCancelButton.Content = selection.Items.Count > 1 ? "Cancel selected…" : "Cancel…";
    }

    private void RefreshHistory_Click(object sender, RoutedEventArgs e) => RefreshHistory();

    private void JobsSearchText_Changed(object sender, TextChangedEventArgs e) { if (IsLoaded) RefreshJobsWorkspace(); }
    private void JobsFilter_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (IsLoaded) RefreshJobsWorkspace(); }
    private void FullJobsMaximumExports_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded && FullJobsMaximumExports.SelectedIndex >= 0)
            _exportScheduler.MaxSimultaneousExports = FullJobsMaximumExports.SelectedIndex + EncodingJobConcurrency.Minimum;
    }

    private void ClearJobsHistory_Click(object sender, RoutedEventArgs e)
    {
        var selection = CurrentJobsSelection();
        if (!selection.CanClearHistory) return;
        var candidates = selection.Items;
        var ids = JobsWorkspacePresentation.BackingHistoryRecordIds(candidates);
        var records = _durableHistoryRecords.Where(record => ids.Contains(record.JobId)).ToList();
        if (records.Count == 0) return;
        var legacy = records.Count(record => record.Plan.Items.Count != 1);
        var detail = "Their saved Lightflow details, provenance, and Review & Rerun availability will be removed. " +
                     "Exported media, active Jobs, recovery state, and output identity are not deleted." +
                     (legacy > 0 ? " Some selected older Jobs were originally saved together and must be deleted together; all Jobs in those saved groups will be removed." : "");
        if (!ConfirmationDialog.Confirm(this, "Delete selected Jobs",
                JobsWorkspacePresentation.RemovalScope(records), detail, null, "Delete Jobs")) return;
        var terminalSchedulerJobIds = JobsWorkspacePresentation.TerminalSchedulerJobIdsForDeletedHistory(candidates, ids);
        _jobHistory.Remove(ids);
        _deletedFullJobsTerminalJobIds.UnionWith(terminalSchedulerJobIds);
        RefreshHistory();
    }

    private void RevealJobOutput_Click(object sender, RoutedEventArgs e)
    {
        if (HistoryList.SelectedItem is not JobsWorkspaceItem item || !File.Exists(item.OutputPath)) return;
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{item.OutputPath}\"") { UseShellExecute = true });
    }

    private void FullJobsPause_Click(object sender, RoutedEventArgs e)
    {
        var selection = CurrentJobsSelection();
        if (!selection.CanPause) return;
        foreach (var id in selection.Items.Select(item => item.JobId).ToList()) _exportScheduler.Pause(id);
    }
    private void FullJobsResume_Click(object sender, RoutedEventArgs e)
    {
        var selection = CurrentJobsSelection();
        if (!selection.CanResume) return;
        foreach (var id in selection.Items.Select(item => item.JobId).ToList()) _exportScheduler.Resume(id);
    }
    private void FullJobsRetry_Click(object sender, RoutedEventArgs e) { if (HistoryList.SelectedItem is JobsWorkspaceItem item) _exportScheduler.RetryNeedsAttention(item.JobId); }
    private void FullJobsMoveEarlier_Click(object sender, RoutedEventArgs e) { if (HistoryList.SelectedItem is JobsWorkspaceItem item) _exportScheduler.MoveWaiting(item.JobId, -1); }
    private void FullJobsMoveLater_Click(object sender, RoutedEventArgs e) { if (HistoryList.SelectedItem is JobsWorkspaceItem item) _exportScheduler.MoveWaiting(item.JobId, 1); }
    private void FullJobsCancel_Click(object sender, RoutedEventArgs e)
    {
        var selection = CurrentJobsSelection();
        if (!selection.CanCancel) return;
        var intended = selection.Items.Select(item => item.JobId).ToList();
        var single = selection.IsSingle ? selection.Items[0] : null;
        if (ConfirmationDialog.Confirm(this, selection.IsSingle ? "Cancel Export Job" : "Cancel Selected Export Jobs",
                selection.IsSingle ? $"Cancel {single!.Name}?" : $"Cancel all {intended.Count} selected Jobs?",
                "Incomplete output uses the existing cleanup policy.", single?.OutputPath, selection.IsSingle ? "Cancel Job" : "Cancel selected"))
            foreach (var id in intended) _exportScheduler.Cancel(id);
    }

    private void JobsBackToBrowser_Click(object sender, RoutedEventArgs e) =>
        MainTabs.SelectedIndex = ShellDestinationSelection.Index(ShellDestination.Home);

    private void RerunHistory_Click(object sender, RoutedEventArgs e)
    {
        if (HistoryList.SelectedItem is not JobsWorkspaceItem { HistoryRecord: { } record }) return;
        var preparation = EncodingHistoryRerun.Prepare(record);
        var restoration = EncodingHistoryRerun.Materialize(preparation);
        if (restoration.Restored.Count == 0)
        {
            MessageBox.Show("None of the original source files are still available and unchanged.", "Review Export job", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var options = preparation.Options;
        _settings = _settings with { Encoding = EncodingOptions.Normalize(options.Encoding) };
        InputFolder.Text = options.InputFolder;
        Resolution.SelectedIndex = (int)options.Resolution;
        RecoveryMode.SelectedIndex = (int)options.Recovery;
        OverwriteExisting.IsChecked = options.OverwriteExistingFiles;
        PreserveFolderStructure.IsChecked = options.PreserveFolderStructure;
        Recursive.IsChecked = options.IncludeSubfolders;
        ShowEncodingDetails.IsChecked = options.DetailedOutput;
        var sameFolderOutput = string.Equals(Path.GetFullPath(options.InputFolder).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(options.OutputRoot).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
        OutputMode.SelectedIndex = (int)(sameFolderOutput ? OutputDestinationMode.SameFolder : OutputDestinationMode.SpecificFolder);
        OutputSpecificFolder.Text = sameFolderOutput ? "" : options.OutputRoot;
        _filenameSuffixUsesResolutionDefault = false;
        OutputFilenameSuffix.Text = options.FilenameSuffix;
        UpdateOutputModeUi();
        RefreshLuts();
        LutSelection.SelectedItem = LutSelection.Items.Cast<LutOption>().FirstOrDefault(option =>
            string.Equals(option.FilePath, options.LutPath, StringComparison.OrdinalIgnoreCase))
            ?? LutSelection.Items.Cast<LutOption>().First(option => option.FilePath is null);
        _batchFolderRefreshTimer.Stop();
        _batchMetadataCts?.Cancel();
        _batchMetadataCts?.Dispose();
        _batchMetadataCts = new CancellationTokenSource();
        _batchFiles.Clear();
        foreach (var file in restoration.Restored) _batchFiles.Add(file);
        ConfigureAssignedColorUi(options.ColorMode);
        UpdateBatchFileSummary();
        _ = LoadBatchMetadataAsync(_batchFiles.ToList(), _batchMetadataCts.Token);
        MainTabs.SelectedIndex = ShellDestinationSelection.Index(ShellDestination.CompatibilityExportReview);
        CurrentFileText.Text = EncodingHistoryRerun.RestorationMessage(restoration);
    }
    private bool ValidateEncoderInputs()
    {
        if (_ffmpeg is null || !File.Exists(_ffmpeg)) { MessageBox.Show("FFmpeg was not found. Open Settings to configure ffmpeg.exe."); return false; }
        if (_ffprobe is null || !File.Exists(_ffprobe)) { MessageBox.Show("FFprobe was not found beside FFmpeg. Reinstall the packaged dependencies or update the FFmpeg path in Settings."); return false; }
        if (!Directory.Exists(InputFolder.Text)) { MessageBox.Show("Select a valid video folder."); return false; }
        if (_batchFiles.All(file => !file.IsSelected)) { MessageBox.Show("Select at least one video file for this batch."); return false; }
        if (CurrentEncodingColorMode == EncodingColorMode.OriginalOrManual
            && !LutCatalog.IsValidSelection(LutSelection.SelectedItem as LutOption)) { MessageBox.Show("Select a valid .cube LUT from the LUT dropdown, or choose No LUT."); return false; }
        try
        {
            var plan = CreateEncodingPlan();
            var error = plan.Issues.Concat(plan.Items.SelectMany(item => item.Issues))
                .FirstOrDefault(issue => issue.Severity == JobIssueSeverity.Error);
            if (error is not null) throw new ArgumentException(error.Message);
        }
        catch (ArgumentException ex) { MessageBox.Show(ex.Message, "Output location", MessageBoxButton.OK, MessageBoxImage.Warning); return false; }
        return true;
    }

    private JobPlan<EncodingJobOptions> CreateEncodingPlan(
        IReadOnlyDictionary<string, double>? durations = null,
        IReadOnlyDictionary<string, ResolvedMediaRange>? resolvedRanges = null)
    {
        var resolution = (OutputResolution)Math.Clamp(Resolution.SelectedIndex, 0, 5);
        var recovery = (RecoveryStrategy)Math.Clamp(RecoveryMode.SelectedIndex, 0, 2);
        var outputRoot = OutputDestinationPlanner.ResolveRoot(InputFolder.Text, resolution, CurrentOutputDestination());
        var suffix = OutputDestinationPlanner.ResolveFilenameSuffix(resolution, CurrentOutputDestination());
        var options = new EncodingJobOptions(
            InputFolder.Text,
            outputRoot,
            resolution,
            recovery,
            _settings.Encoding,
            CurrentEncodingColorMode == EncodingColorMode.OriginalOrManual ? SelectedLutPath : null,
            suffix,
            ShouldPreserveFolderStructure(),
            OverwriteExisting.IsChecked == true,
            ShowEncodingDetails.IsChecked == true,
            Recursive.IsChecked == true,
            CurrentEncodingColorMode);
        var sources = _batchFiles.Where(file => file.IsSelected).Select(file =>
        {
            var seconds = durations?.GetValueOrDefault(file.FilePath) ?? file.Metadata?.DurationSeconds ?? 0;
            return new EncodingSource(
                file.FilePath,
                file.FileSizeBytes,
                seconds > 0 ? TimeSpan.FromSeconds(seconds) : null,
                file.TrimRange,
                resolvedRanges?.GetValueOrDefault(file.FilePath),
                file.SourceIdentity?.LastWriteUtcTicks,
                file.Metadata?.HasAudio,
                file.CapabilityOrder,
                file.AssignedColor,
                file.Metadata is { } metadata ? new SourceMediaTraits(
                    metadata.VideoCodec, metadata.Width, metadata.Height, metadata.FrameRate, metadata.Container,
                    metadata.AudioCodec, metadata.AudioSampleRate, metadata.AudioChannels, metadata.AudioChannelLayout) : null,
                file.RestoredExport,
                RestoredName: file.RestoredName,
                ExportProvenance: file.ExportProvenance);
        });
        return EncodingJobPlanner.Plan(EncodingJobPlanner.Define(options, sources));
    }


    private string? SelectedLutPath => (LutSelection.SelectedItem as LutOption)?.FilePath;


    private async Task<int> RunFfmpegProgressAsync(List<string> args, double duration, bool detailedOutput, Action<double> progress, CancellationToken token)
    {
        using var process = StartProcess(_ffmpeg!, args, redirectError: true);
        _activeEncodingProcess = process;
        PauseButton.IsEnabled = true;
        PauseButton.Content = "Pause";
        try
        {
            var errors = new StringBuilder();
            var errTask = Task.Run(async () =>
            {
                while (await process.StandardError.ReadLineAsync(token) is { } line)
                {
                    errors.AppendLine(line);
                    _activityLogFile.TryAppend($"[FFmpeg] {line}");
                    if (detailedOutput) ShowInActivityLog($"[FFmpeg] {line}");
                }
            }, token);
            while (await process.StandardOutput.ReadLineAsync(token) is { } line)
            {
                if (FfmpegProgressParser.TryParsePercent(line, duration, out var percent)) progress(percent);
            }
            await process.WaitForExitAsync(token);
            await errTask;
            if (process.ExitCode != 0 && !detailedOutput) ShowInActivityLog(errors.ToString());
            progress(100);
            return process.ExitCode;
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
            }
            catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                _activityLogFile.TryAppend($"[App] Could not stop the cancelled encoder process cleanly: {exception.Message}");
            }
            throw;
        }
        finally
        {
            _encodingPause.Clear();
            PauseButton.IsEnabled = false;
            PauseButton.Content = "Pause";
            if (ReferenceEquals(_activeEncodingProcess, process)) _activeEncodingProcess = null;
        }
    }
    private void UpdateBatch(
        JobExecution<EncodingJobOptions, EncodingItemResult> execution,
        JobItemExecution<EncodingItemResult> current,
        int completed,
        int total,
        Stopwatch sw)
    {
        var progress = execution.Progress(current);
        _batchProgress.ReportBatchPercent(progress.OverallPercent ?? 0);
        BatchProgress.Value = _batchProgress.BatchPercent;
        var remaining = progress.TotalWork is > 0
            ? BatchEtaEstimator.Estimate(sw.Elapsed, progress.CompletedWork, progress.TotalWork.Value)
            : null;
        EtaText.Text = remaining is null
            ? $"Completed {completed} of {total} — estimated remaining: calculating…"
            : $"Completed {completed} of {total} — estimated remaining: {remaining:hh\\:mm\\:ss}";
    }
    private void ApplyProgressState()
    {
        BatchProgress.Value = _batchProgress.BatchPercent;
        FileProgress.Value = _batchProgress.FilePercent;
        EtaText.Text = _batchProgress.StatusText;
    }

    private void ReportRecoveredJobs()
    {
        var recovered = _jobRuntimeStore.LoadAll((item, options) => EncodingJobRecovery.Revalidate(
            item, options, _storage.Locations.OutputIdentityDirectory));
        foreach (var job in recovered.Where(job => job.Disposition != JobRecoveryDisposition.Terminal))
        {
            var state = job.Disposition == JobRecoveryDisposition.NeedsAttention
                ? "needs attention before it can resume"
                : job.Disposition == JobRecoveryDisposition.Paused ? "was restored paused" : "is waiting for review";
            AppendDetailedLog($"Recovered Export job {job.Checkpoint.Plan.Definition.Id} {state}. " +
                              "It will not start automatically before the Jobs review UI is available.");
            foreach (var issue in job.Issues) AppendDetailedLog($"Recovery: {issue.Message}");
        }
    }

    private void JobsRuntime_Changed(IReadOnlyList<JobRuntimeSnapshot<EncodingItemResult>> jobs)
    {
        var activeId = _activeJobRuntime?.Plan.Definition.Id;
        var snapshot = activeId is null ? null : jobs.FirstOrDefault(job => job.JobId == activeId);
        if (snapshot is null) return;
        _ = Dispatcher.BeginInvoke(() =>
        {
            _batchProgress.ReportBatchPercent(snapshot.Progress.OverallPercent ?? 0);
            BatchProgress.Value = _batchProgress.BatchPercent;
            FileProgress.Value = snapshot.Items.Where(item => item.State == JobState.Running)
                .Select(item => item.ProgressPercent ?? 0).DefaultIfEmpty(0).Average();
            EtaText.Text = snapshot.Eta is { } eta
                ? $"Completed {snapshot.Counts.Completed + snapshot.Counts.Skipped} of {snapshot.Counts.Total} — estimated remaining: {eta:hh\\:mm\\:ss}"
                : $"Completed {snapshot.Counts.Completed + snapshot.Counts.Skipped} of {snapshot.Counts.Total} — estimated remaining: unavailable";
            PauseButton.IsEnabled = snapshot.State is JobState.Running or JobState.Pausing or JobState.Paused or JobState.Queued;
            PauseButton.Content = snapshot.State is JobState.Pausing or JobState.Paused ? "Resume" : "Pause";
            if (snapshot.State is JobState.Pausing or JobState.Paused) SetBatchStatus(BatchStatus.Paused);
            else if (snapshot.State is JobState.Running or JobState.Queued) SetBatchStatus(BatchStatus.Encoding);
            CurrentFileText.Text = JobRuntimeStatusPresentation.Describe(snapshot);
            if (_closeAfterCurrent && snapshot.State == JobState.Paused)
            {
                _activeJobRuntime?.Cancel();
                _forceClose = true;
                _ = Dispatcher.BeginInvoke(new Action(Close));
            }
        });
    }
    private void ToggleEncoding(bool running)
    {
        BatchSourceConfiguration.IsEnabled = !running;
        BatchOutputConfiguration.IsEnabled = !running;
        BatchLutConfiguration.IsEnabled = !running;
        BatchFormatConfiguration.IsEnabled = !running;
        BatchFileContent.IsHitTestVisible = !running;
        if (running) StartButton.IsEnabled = false;
        else UpdateBatchReadiness(updateGuidance: false);
        CancelButton.IsEnabled = running;
        if (!running)
        {
            PauseButton.IsEnabled = false;
            PauseButton.Content = "Pause";
        }
        SetBatchStatus(running ? BatchStatus.Encoding : BatchStatus.Ready);
    }

    private void SetBatchStatus(BatchStatus status)
    {
        var presentation = BatchStatusPresentation.For(status);
        BatchStateText.Text = presentation.Text;
        BatchStateText.Foreground = (System.Windows.Media.Brush)FindResource(presentation.ForegroundResource);
        BatchStateBorder.Background = (System.Windows.Media.Brush)FindResource(presentation.BackgroundResource);
        BatchStateBorder.BorderBrush = (System.Windows.Media.Brush)FindResource(presentation.BorderResource);
    }
    private void Pause_Click(object sender, RoutedEventArgs e)
    {
        if (_activeJobRuntime is { } runtime)
        {
            if (runtime.IsPauseRequested)
            {
                runtime.Resume();
                AppendLog("Export resumed by user.");
            }
            else
            {
                runtime.Pause();
                AppendLog("Export is pausing; active file exports will finish and no new files will start.");
            }
            return;
        }
        var process = _activeEncodingProcess;
        if (process is null) return;

        if (_encodingPause.IsPaused)
        {
            ResumeEncoding(process, "Export resumed by user.");
            return;
        }

        if (!_encodingPause.Pause(process)) return;
        if (_batchStopwatch?.IsRunning == true) _batchStopwatch.Stop();
        PauseButton.Content = "Resume";
        SetBatchStatus(BatchStatus.Paused);
        AppendLog("Export paused by user.");
    }

    private void ResumeEncoding(Process? process, string logMessage)
    {
        _encodingPause.Resume(process);
        if (_batchStopwatch?.IsRunning == false) _batchStopwatch.Start();
        PauseButton.Content = "Pause";
        SetBatchStatus(BatchStatus.Encoding);
        AppendLog(logMessage);
    }
    private void Cancel_Click(object sender, RoutedEventArgs e) => CancelActiveEncoding();

    private void ExportScheduler_Changed(IReadOnlyList<ExportJobSnapshot> jobs)
    {
        if (Interlocked.Exchange(ref _jobsPresentationPending, 1) != 0) return;
        _ = Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            Interlocked.Exchange(ref _jobsPresentationPending, 0);
            ApplyJobsPresentation(_exportScheduler.Jobs);
        }));
    }

    private void ApplyJobsPresentation(IReadOnlyList<ExportJobSnapshot> jobs)
    {
        if (JobsStatusButton is null) return;
        var queuePaused = _exportScheduler.IsQueuePaused;
        JobsStatusButton.Content = JobsPresentation.StatusText(jobs, queuePaused);
        AutomationProperties.SetName(JobsStatusButton, $"{JobsStatusButton.Content}. Open full Jobs workspace.");
        JobsStatusButton.ToolTip = "Open full Jobs workspace";
        var activeCount = jobs.Count(job => !JobsPresentation.IsTerminal(job.State));
        JobsDrawerPullButton.Tag = activeCount > 0 ? "Active" : "Idle";
        JobsDrawerPullCount.Text = activeCount.ToString();
        JobsDrawerPullCount.Visibility = activeCount > 0 ? Visibility.Visible : Visibility.Collapsed;
        MaximumExportsCombo.SelectedIndex = _exportScheduler.MaxSimultaneousExports - EncodingJobConcurrency.Minimum;
        ApplyQueueGatePresentation(FullJobsQueueGateButton, queuePaused);
        ApplyQueueGatePresentation(JobsQueueGateButton, queuePaused);
        var visibleJobs = JobsPresentation.VisibleJobs(jobs, _dismissedTerminalJobIds);
        var cancellableCount = JobsPresentation.BulkCancellableJobs(jobs).Count;
        var clearableCount = visibleJobs.Count(job => JobsPresentation.IsDismissibleDrawerRow(job.State));
        var bulkAction = JobsPresentation.BulkAction(visibleJobs);
        var cancelAll = bulkAction == JobsBulkAction.CancelAll;
        JobsCancelAllButton.Content = cancelAll ? "Cancel all" : "Clear all";
        JobsCancelAllButton.IsEnabled = bulkAction != JobsBulkAction.None;
        JobsCancelAllButton.ToolTip = cancelAll
            ? $"Cancel {cancellableCount} active Jobs"
            : clearableCount > 0 ? $"Remove {clearableCount} Jobs from this drawer only" : "No Jobs to clear";
        AutomationProperties.SetName(JobsCancelAllButton, cancelAll
            ? $"Cancel all {cancellableCount} active Jobs"
            : clearableCount > 0 ? $"Clear all {clearableCount} dismissible Jobs from drawer" : "Clear all, no Jobs to clear");
        var cards = visibleJobs
            .Select(job => JobsPresentation.Card(job, _expandedJobIds.Contains(job.JobId))).ToList();
        JobsPresentation.Reconcile(_jobsDrawerCards, cards);
        if (MainTabs?.SelectedIndex == ShellDestinationSelection.Index(ShellDestination.Jobs)) RefreshJobsWorkspace();
    }

    private void JobsStatus_Click(object sender, RoutedEventArgs e)
    {
        MainTabs.SelectedIndex = ShellDestinationSelection.Index(ShellDestination.Jobs);
    }

    private void OpenJobsDrawer()
    {
        SetRightDrawer(RightDrawerKind.Jobs);
    }

    private void SetRightDrawer(RightDrawerKind drawer)
    {
        if (JobsDrawer is null) return;
        if (drawer == RightDrawerKind.Subclips && !_subclipsContextAvailable) drawer = RightDrawerKind.None;
        _openRightDrawer = drawer;
        _playerViewerHost?.SetSubclipsDrawerOpen(drawer == RightDrawerKind.Subclips);
        SubclipsDrawerPullChevron.Text = drawer == RightDrawerKind.Subclips ? "›" : "‹";
        SubclipsDrawerPullButton.ToolTip = drawer == RightDrawerKind.Subclips ? "Close Subclips drawer" : "Open Subclips drawer";
        AutomationProperties.SetName(SubclipsDrawerPullButton,
            drawer == RightDrawerKind.Subclips ? "Close Subclips drawer" : "Open Subclips drawer");
        if (drawer != RightDrawerKind.Jobs)
        {
            ApplyJobsDrawerClosed();
            return;
        }
        JobsDrawerColumn.MinWidth = WorkspaceState.MinJobsDrawerWidth;
        JobsDrawerColumn.Width = new GridLength(_jobsDrawerWidth);
        JobsDrawerSplitterColumn.Width = new GridLength(8);
        JobsDrawerSplitter.Visibility = Visibility.Visible;
        JobsDrawer.Visibility = Visibility.Visible;
        JobsDrawerPullChevron.Text = "›";
        JobsDrawerPullButton.ToolTip = "Close Jobs drawer";
        AutomationProperties.SetName(JobsDrawerPullButton, "Close Jobs drawer");
    }

    private void CloseJobsDrawer(bool manual)
    {
        if (_openRightDrawer == RightDrawerKind.Jobs) _openRightDrawer = RightDrawerKind.None;
        ApplyJobsDrawerClosed();
    }

    private void ApplyJobsDrawerClosed()
    {
        if (JobsDrawer.Visibility == Visibility.Visible) _jobsDrawerWidth = JobsDrawerColumn.ActualWidth;
        JobsDrawer.Visibility = Visibility.Collapsed;
        JobsDrawerSplitter.Visibility = Visibility.Collapsed;
        JobsDrawerSplitterColumn.Width = new GridLength(0);
        JobsDrawerColumn.MinWidth = 0;
        JobsDrawerColumn.Width = new GridLength(0);
        JobsDrawerPullChevron.Text = "‹";
        JobsDrawerPullButton.ToolTip = "Open Jobs drawer";
        AutomationProperties.SetName(JobsDrawerPullButton, "Open Jobs drawer");
    }

    private void JobsDrawerPull_Click(object sender, RoutedEventArgs e)
    {
        if (JobsDrawer.Visibility == Visibility.Visible) CloseJobsDrawer(true); else OpenJobsDrawer();
    }

    private void SubclipsDrawerPull_Click(object sender, RoutedEventArgs e) =>
        SetRightDrawer(_openRightDrawer == RightDrawerKind.Subclips ? RightDrawerKind.None : RightDrawerKind.Subclips);

    private void SetSubclipsContextAvailable(bool available)
    {
        _subclipsContextAvailable = available;
        if (!available && _openRightDrawer == RightDrawerKind.Subclips) SetRightDrawer(RightDrawerKind.None);
        UpdateSubclipsPullVisibility();
    }

    private void UpdateSubclipsPullVisibility()
    {
        if (SubclipsDrawerPullButton is null) return;
        var homeActive = MainTabs?.SelectedIndex == ShellDestinationSelection.Index(ShellDestination.Home);
        SubclipsDrawerPullButton.Visibility = _subclipsContextAvailable && homeActive &&
            _browserPresentation == BrowserPresentationMode.PlayerViewer ? Visibility.Visible : Visibility.Collapsed;
    }
    private void JobExpansionToggle_Click(object sender, RoutedEventArgs e)
    {
        if (JobIdFrom(sender) is not { } id) return;
        var expanded = _expandedJobIds.Add(id);
        if (!expanded) _expandedJobIds.Remove(id);
        _jobsDrawerCards.FirstOrDefault(card => card.JobId == id)?.SetExpanded(expanded);
    }

    private void MaximumExports_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var maximum = (MaximumExportsCombo?.SelectedIndex ?? -1) + EncodingJobConcurrency.Minimum;
        if (maximum >= EncodingJobConcurrency.Minimum && maximum != _exportScheduler.MaxSimultaneousExports)
            _exportScheduler.MaxSimultaneousExports = maximum;
    }

    private void JobsQueueGate_Click(object sender, RoutedEventArgs e)
    {
        if (_exportScheduler.IsQueuePaused) _exportScheduler.ResumeQueue();
        else _exportScheduler.PauseQueue();
    }

    private static void ApplyQueueGatePresentation(System.Windows.Controls.Button button, bool paused)
    {
        button.Content = paused ? "Resume Queue" : "Pause Queue";
        button.Tag = paused ? "Paused" : "Running";
        button.ToolTip = paused ? "Resume starting queued Jobs" : "Hold queued Jobs; running exports continue";
        AutomationProperties.SetName(button, paused ? "Resume Queue, queue paused" : "Pause Queue");
        AutomationProperties.SetHelpText(button, paused
            ? "Allow eligible Waiting Jobs to start up to Active exports."
            : "Hold queued Jobs before they start. Running exports continue.");
        button.FontWeight = paused ? FontWeights.SemiBold : FontWeights.Normal;
        button.Opacity = paused ? 1 : 0.9;
        if (paused)
        {
            button.Background = (System.Windows.Media.Brush)button.FindResource("ShellSelectionBrush");
            button.BorderBrush = (System.Windows.Media.Brush)button.FindResource("ShellFocusBrush");
        }
        else
        {
            button.ClearValue(System.Windows.Controls.Control.BackgroundProperty);
            button.ClearValue(System.Windows.Controls.Control.BorderBrushProperty);
        }
    }

    private Guid? JobIdFrom(object sender) => (sender as FrameworkElement)?.Tag is Guid id ? id : null;
    private void JobsPause_Click(object sender, RoutedEventArgs e) { if (JobIdFrom(sender) is { } id) _exportScheduler.Pause(id); }
    private void JobsResume_Click(object sender, RoutedEventArgs e) { if (JobIdFrom(sender) is { } id) _exportScheduler.Resume(id); }
    private void JobsRetry_Click(object sender, RoutedEventArgs e) { if (JobIdFrom(sender) is { } id) _exportScheduler.RetryNeedsAttention(id); }
    private void JobsMoveUp_Click(object sender, RoutedEventArgs e) { if (JobIdFrom(sender) is { } id) _exportScheduler.MoveWaiting(id, -1); }
    private void JobsMoveDown_Click(object sender, RoutedEventArgs e) { if (JobIdFrom(sender) is { } id) _exportScheduler.MoveWaiting(id, 1); }
    private void JobsCancel_Click(object sender, RoutedEventArgs e)
    {
        if (JobIdFrom(sender) is not { } id) return;
        var job = _exportScheduler.Jobs.FirstOrDefault(snapshot => snapshot.JobId == id && !JobsPresentation.IsTerminal(snapshot.State));
        if (job is null) return;
        if (ConfirmationDialog.Confirm(this, "Cancel Export Job", "Cancel this Export Job?",
            "Incomplete output uses the existing cleanup policy.", job.OutputPath, "Cancel Job"))
            _exportScheduler.Cancel(id);
    }

    private void JobsCancelAll_Click(object sender, RoutedEventArgs e)
    {
        var jobs = _exportScheduler.Jobs;
        var intended = JobsPresentation.BulkCancellableJobs(jobs).Select(job => job.JobId).ToList();
        if (intended.Count > 0)
        {
            var noun = intended.Count == 1 ? "Job" : "Jobs";
            if (!ConfirmationDialog.Confirm(this, "Cancel all Export Jobs", $"Cancel all {intended.Count} active {noun}?",
                "Needs-attention and terminal Jobs are unaffected. Incomplete outputs use the existing cleanup policy.",
                null, "Cancel all")) return;
            foreach (var id in intended) _exportScheduler.Cancel(id);
            return;
        }
        foreach (var job in JobsPresentation.VisibleJobs(jobs, _dismissedTerminalJobIds)
                     .Where(job => JobsPresentation.IsDismissibleDrawerRow(job.State)))
            _dismissedTerminalJobIds.Add(job.JobId);
        ApplyJobsPresentation(_exportScheduler.Jobs);
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        SaveBatchState();
        SaveWorkspaceState();
        _previewMaintenanceCts?.Cancel();
        if (!_forceClose && _exportCoordinator.ActiveCount > 0 && _jobCancellation is null)
        {
            e.Cancel = true;
            var exportCloseDialog = new EncodingCloseDialog { Owner = this };
            exportCloseDialog.ShowDialog();
            if (exportCloseDialog.Choice == EncodingCloseChoice.CloseNow)
            {
                _forceClose = true;
                _ = Dispatcher.BeginInvoke(new Action(Close));
            }
            return;
        }
        if (_jobCancellation is null || _forceClose) return;

        e.Cancel = true;
        if (_activeJobRuntime is { } runtime)
        {
            var jobsDialog = new EncodingCloseDialog { Owner = this };
            jobsDialog.ShowDialog();
            if (jobsDialog.Choice == EncodingCloseChoice.CloseNow)
            {
                _forceClose = true;
                CancelActiveEncoding();
                _ = Dispatcher.BeginInvoke(new Action(Close));
            }
            else if (jobsDialog.Choice == EncodingCloseChoice.CloseAfterCurrent)
            {
                _closeAfterCurrent = true;
                runtime.Pause();
                CurrentFileText.Text = "Will close after active file exports finish";
                AppendLog("Close requested — no new files will start; Lightflow will close after active exports finish.");
            }
            return;
        }
        var pausedProcess = _activeEncodingProcess;
        var wasAlreadyPaused = _encodingPause.IsPaused;
        var processPaused = wasAlreadyPaused || _encodingPause.Pause(pausedProcess);
        var pausedByDialog = processPaused && !wasAlreadyPaused;
        if (pausedByDialog && _batchStopwatch?.IsRunning == true) _batchStopwatch.Stop();
        if (pausedByDialog)
        {
            SetBatchStatus(BatchStatus.Paused);
            PauseButton.Content = "Resume";
            AppendDetailedLog("Export paused while the close options are open.");
        }

        var dialog = new EncodingCloseDialog { Owner = this };
        dialog.ShowDialog();
        if (dialog.Choice == EncodingCloseChoice.CloseNow)
        {
            _forceClose = true;
            CancelActiveEncoding();
            _encodingPause.Clear();
            _ = Dispatcher.BeginInvoke(new Action(Close));
            return;
        }

        if (processPaused && EncodingClosePolicy.ShouldResumeAfterDialog(wasAlreadyPaused, dialog.Choice))
        {
            ResumeEncoding(pausedProcess, dialog.Choice == EncodingCloseChoice.CloseAfterCurrent
                ? "Export resumed to finish the current file before closing."
                : "Export resumed.");
        }

        if (dialog.Choice == EncodingCloseChoice.CloseAfterCurrent)
        {
            _closeAfterCurrent = true;
            CurrentFileText.Text = "Will close after the current file finishes";
            AppendLog("Close requested — the application will close after the current file finishes.");
        }
    }

    private void CancelActiveEncoding()
    {
        _exportCoordinator.TerminateAll();
        _activeJobRuntime?.Cancel();
        _activeJobExecutor?.TerminateAll();
        _jobCancellation?.Cancel();
        _activeEncodingJob?.CancelPending();
        try
        {
            if (_activeEncodingProcess is { HasExited: false } process) process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // The process exited between the state check and the kill request.
        }
    }

    internal void ActivateFromLaunch(ApplicationLaunchRequest request)
    {
        _ = request; // Reserved for future file/folder/deep-link launch handling.
        ApplicationWindowActivation.RestoreAndActivate(new WpfApplicationWindow(this, _lastNonMinimizedWindowState));
    }
    private void AppendLog(string text)
    {
        _activityLogFile.TryAppend(text);
        ShowInActivityLog(text);
    }

    private void ShowInActivityLog(string text)
    {
        Dispatcher.Invoke(() =>
        {
            LogBox.Text = ActivityLog.Append(LogBox.Text, text);
            LogBox.CaretIndex = LogBox.Text.Length;
            LogBox.ScrollToEnd();
        });
    }

    private void AppendDetailedLog(string text)
    {
        var line = $"[App] {text}";
        _activityLogFile.TryAppend(line);
        if (ShowEncodingDetails.IsChecked == true) ShowInActivityLog(line);
    }

    private void ShowEncodingDetails_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        _settings = _settings with { DetailedActivityLogging = ShowEncodingDetails.IsChecked == true };
        try
        {
            _storage.SaveSettings(_settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppendLog($"Could not save the export-details preference: {ex.Message}");
        }
    }
    private static string FormatDuration(double seconds) =>
        seconds > 0 ? TimeSpan.FromSeconds(seconds).ToString(@"hh\:mm\:ss\.fff") : "Unavailable";

    private static string FormatCommand(string executable, IEnumerable<string> args) =>
        QuoteCommandArgument(executable) + " " + string.Join(" ", args.Select(QuoteCommandArgument));

    private static string QuoteCommandArgument(string value) =>
        value.Any(char.IsWhiteSpace) || value.Contains('"')
            ? "\"" + value.Replace("\"", "\\\"") + "\""
            : value;

    private async Task<double> ProbeDurationAsync(string file, CancellationToken token)
    {
        if (_ffprobe is null) return 0;
        var result = await CaptureAsync(_ffprobe, FfmpegCommandBuilder.ProbeDuration(file), token);
        return double.TryParse(result.StdOut.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0;
    }

    private static Process StartProcess(string exe, IEnumerable<string> args, bool redirectError)
    {
        var psi = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = redirectError };
        foreach (var arg in args) psi.ArgumentList.Add(arg);
        return Process.Start(psi) ?? throw new InvalidOperationException($"Could not start {exe}.");
    }
    private static async Task<(int ExitCode, string StdOut, string StdErr)> CaptureAsync(string exe, IEnumerable<string> args, CancellationToken token)
    {
        using var p = StartProcess(exe, args, true);
        var stdout = p.StandardOutput.ReadToEndAsync(token); var stderr = p.StandardError.ReadToEndAsync(token);
        await p.WaitForExitAsync(token); return (p.ExitCode, await stdout, await stderr);
    }

    private async Task<(int ExitCode, string StdOut, string StdErr)> CaptureLoggedAsync(string toolName, string exe, IEnumerable<string> args, CancellationToken token)
    {
        var argList = args as IReadOnlyList<string> ?? args.ToList();
        _activityLogFile.TryAppend($"[App] {toolName}: {FormatCommand(exe, argList)}");
        var result = await CaptureAsync(exe, argList, token);
        if (!string.IsNullOrWhiteSpace(result.StdOut)) _activityLogFile.TryAppend($"[{toolName}] stdout: {result.StdOut.Trim()}");
        if (!string.IsNullOrWhiteSpace(result.StdErr)) _activityLogFile.TryAppend($"[{toolName}] stderr: {result.StdErr.Trim()}");
        if (result.ExitCode != 0) _activityLogFile.TryAppend($"[App] {toolName} exited with code {result.ExitCode}.");
        return result;
    }
}
