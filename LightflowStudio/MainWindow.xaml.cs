using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Microsoft.Data.Sqlite;
using Forms = System.Windows.Forms;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using MessageBox = System.Windows.MessageBox;

namespace LightflowStudio;

public partial class MainWindow : Window
{
    private string? _ffmpeg;
    private string? _ffprobe;
    private JobCancellation? _jobCancellation;
    private readonly BatchProgressState _batchProgress = new();
    private readonly string? _commandLineFolder;
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
    private readonly ObservableCollection<EncodingJobHistoryRecord> _historyRecords = [];
    private readonly ObservableCollection<MediaRootInfo> _mediaRoots = [];
    private readonly ObservableCollection<BrowserStorageEntry> _browserStorageEntries = [];
    private readonly BrowserGridModel _browserGrid = new();
    private readonly BrowserTreeModel _browserTree = new();
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
    private bool _synchronizingBrowserTree;
    private readonly WorkspaceStateService _workspaceState;
    private readonly DispatcherTimer _workspaceSaveTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private WindowState _lastNonMinimizedWindowState = WindowState.Normal;

    internal MainWindow(LightflowStorageCoordinator storage, StorageStartupStatus storageStartupStatus,
        string? storageDiagnostic)
    {
        _storage = storage;
        _storageStartupStatus = storageStartupStatus;
        _storageDiagnostic = storageDiagnostic;
        _browserNavigation = new BrowserNavigationSession(storage.MediaRoots, storage.BrowserLocations,
            storage.MediaDiscovery, storage.MediaFolders);
        _trimHistory = new TrimHistoryStore(storage.Locations.TrimHistoryPath);
        _jobHistory = new JobHistoryStore(storage.Locations.JobHistoryPath);
        _workspaceState = new WorkspaceStateService(storage.Locations.WorkspaceStatePath);
        InitializeComponent();
        ApplyRestoredWorkspaceLayout();
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
        SourceInitialized += (_, _) => WindowAppearance.EnableDarkTitleBar(this);
        StateChanged += (_, _) =>
        {
            if (WindowState != WindowState.Minimized) _lastNonMinimizedWindowState = WindowState;
        };
        _commandLineFolder = Environment.GetCommandLineArgs().Skip(1).FirstOrDefault(Directory.Exists);
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
                    MainTabs.SelectedIndex = ShellWorkspaceSelection.Index(ShellWorkspace.Encoding);
                }
                BatchFileList.ItemsSource = _batchFiles;
                HistoryList.ItemsSource = _historyRecords;
                MediaRootsList.ItemsSource = _mediaRoots;
                BrowserFolderTree.ItemsSource = _browserTree.Roots;
                BrowserGridRows.ItemsSource = _browserGrid.Rows;
                if (_storage.MediaMonitoring is { } monitoring) monitoring.FolderRefreshed += BrowserMonitoring_FolderRefreshed;

                // Browser is the default, immediately visible workspace: get its Locations storage entries
                // (needed so an offline saved root already has a tree node to show its honest state against)
                // and kick off restoration before any Encoding/History/Settings-only work below, none of
                // which the user is looking at yet. Measured on real hardware: this alone cut the delay
                // before restoration starts from ~1.1s to ~0.16s. Restoration itself proceeds independently.
                await RefreshBrowserStorageAsync();
                _ = RestoreBrowserLocationAsync(_workspaceState.Current.Browser);

                RefreshCatalogBackups();
                RefreshHistory();
                LocateTools();
                await RefreshDependencyHealthAsync();
                RefreshBatchFiles();
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
            if (_storage.MediaMonitoring is { } monitoring) monitoring.FolderRefreshed -= BrowserMonitoring_FolderRefreshed;
            _workspaceSaveTimer.Stop();
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
            BrowserNavigationColumn.Width = new GridLength(paneWidth);
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
        BrowserLoadingText.Text = label is null ? "Restoring your last location…" : $"Loading {label}…";
        if (!string.IsNullOrWhiteSpace(saved.LastResolvedAbsolutePath)) BrowserCurrentPath.Text = saved.LastResolvedAbsolutePath;
        BrowserEmptyState.Visibility = Visibility.Collapsed;
        BrowserLoadingOverlay.Visibility = Visibility.Visible;
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
    /// Captures the window's restored bounds, maximized state, and Locations-pane width, then flushes the
    /// latest workspace state to disk. Called on normal shutdown; a debounced save also covers Browser
    /// location changes mid-session for crash resilience.
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
        _workspaceState.SetBrowserLocationsPaneWidth(BrowserNavigationColumn.ActualWidth);
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

    private void BrowserMonitoring_FolderRefreshed(object? sender, MediaFolderEnumerationRequest request) =>
        Dispatcher.BeginInvoke(async () =>
        {
            var location = _browserNavigation.State.Location;
            if (location is null || location.RootId != request.RootId ||
                !string.Equals(location.RelativeFolder ?? "", request.RelativeFolder ?? "", StringComparison.OrdinalIgnoreCase))
                return;
            await RunBrowserNavigationAsync(() => _browserNavigation.RefreshAsync());
        });

    private async void BrowserFolderTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (_synchronizingBrowserTree || e.NewValue is not BrowserTreeNode { IsPlaceholder: false } node) return;
        RequestBrowserTreeSelection(node);
        if (node.Storage is { Kind: BrowserStorageKind.ManagedRoot, RootId: { } rootId })
            await RunBrowserNavigationAsync(() => _browserNavigation.NavigateToRootAsync(rootId));
        else if (!string.IsNullOrWhiteSpace(node.AbsolutePath))
            await RunBrowserNavigationAsync(() => _browserNavigation.NavigateToPathAsync(node.AbsolutePath));
    }

    private async void BrowserFolderTreeItem_Expanded(object sender, RoutedEventArgs e)
    {
        if (_synchronizingBrowserTree || (sender as FrameworkElement)?.DataContext is not BrowserTreeNode node ||
            node.IsPlaceholder || !node.Children.Any(child => child.IsPlaceholder)) return;
        RequestBrowserTreeSelection(node);
        if (node.Storage is { Kind: BrowserStorageKind.ManagedRoot, RootId: { } rootId })
            await RunBrowserNavigationAsync(() => _browserNavigation.NavigateToRootAsync(rootId));
        else if (!string.IsNullOrWhiteSpace(node.AbsolutePath))
            await RunBrowserNavigationAsync(() => _browserNavigation.NavigateToPathAsync(node.AbsolutePath));
    }

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
        finally { _synchronizingBrowserTree = false; }
        if (node is not null) BringBrowserTreeNodeIntoView(node);
    }

    private void RequestBrowserTreeSelection(string absolutePath)
    {
        _synchronizingBrowserTree = true;
        BrowserTreeNode? node;
        try { node = _browserTree.RequestSelection(absolutePath); }
        finally { _synchronizingBrowserTree = false; }
        if (node is not null) BringBrowserTreeNodeIntoView(node);
    }

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
        }

        if (generation == _browserUiGeneration && _browserTree.SelectedNode is { } selected)
            BringBrowserTreeNodeIntoView(selected);
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

    private void BrowserFolderTree_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        const double pixelsPerWheelNotch = 48;
        var distance = -(e.Delta / (double)Mouse.MouseWheelDeltaForOneLine) * pixelsPerWheelNotch;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) && BrowserFolderScrollViewer.ScrollableWidth > 0)
            BrowserFolderScrollViewer.ScrollToHorizontalOffset(
                Math.Clamp(BrowserFolderScrollViewer.HorizontalOffset + distance, 0,
                    BrowserFolderScrollViewer.ScrollableWidth));
        else if (BrowserFolderScrollViewer.ScrollableHeight > 0)
            BrowserFolderScrollViewer.ScrollToVerticalOffset(
                Math.Clamp(BrowserFolderScrollViewer.VerticalOffset + distance, 0,
                    BrowserFolderScrollViewer.ScrollableHeight));
        else
            return;
        e.Handled = true;
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

    private async void BrowserRefresh_Click(object sender, RoutedEventArgs e) =>
        await RunBrowserNavigationAsync(() => _browserNavigation.RefreshAsync());

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

    private async Task RunBrowserNavigationAsync(Func<Task<BrowserFolderState?>> navigate)
    {
        var generation = ++_browserUiGeneration;
        BrowserLoadingText.Text = "Loading folder…";
        BrowserLoadingOverlay.Visibility = Visibility.Visible;
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

    private void ApplyBrowserState(BrowserFolderState state)
    {
        _lastLoadedBrowserState = state;
        _synchronizingBrowserTree = true;
        IReadOnlyList<MediaFolderEntry> files;
        try { files = _browserTree.Synchronize(state); }
        finally { _synchronizingBrowserTree = false; }
        _browserGrid.Populate(files);
        UpdateBrowserGridColumns();
        if (state.DerivedWork is { } batch) _browserGrid.ApplyAssetIdentities(batch.Reconciliation.Items);
        AttachBrowserDerivedWork(state.DerivedWork, _browserUiGeneration);
        BrowserCurrentPath.Text = state.Location?.DisplayPath ?? "";
        BrowserBackButton.IsEnabled = state.CanGoBack;
        BrowserForwardButton.IsEnabled = state.CanGoForward;
        BrowserUpButton.IsEnabled = state.CanGoUp;
        BrowserRefreshButton.IsEnabled = state.Location is not null;
        BrowserEmptyState.Visibility = state.Status == BrowserFolderStatus.Ready && files.Count > 0
            ? Visibility.Collapsed : Visibility.Visible;
        BrowserEmptyTitle.Text = state.Status switch
        {
            BrowserFolderStatus.Ready when files.Count == 0 => "No media files in this folder",
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

        if (state.Location is { } location)
        {
            // Selection is intentionally never persisted here; only the folder identity is remembered.
            _workspaceState.SetBrowserLocation(location.RootId, location.RelativeFolder, location.AbsolutePath);
            _workspaceSaveTimer.Stop();
            _workspaceSaveTimer.Start();
        }
    }

    private void ApplyBrowserNavigationFailure(BrowserFolderState failure)
    {
        RestoreLoadedBrowserSelection();
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
        _browserGrid.SetColumns(BrowserGridLayout.ComputeColumns(width));
    }

    private void BrowserGridHost_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateBrowserGridColumns();

    private void BrowserGridHost_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _browserGrid.ClearSelection();
        BrowserGridRows.Focus();
    }

    private void BrowserGridTile_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not BrowserGridTile tile) return;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) _browserGrid.SelectRange(tile.Index);
        else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) _browserGrid.ToggleCtrl(tile.Index);
        else _browserGrid.SelectSingle(tile.Index);
        BrowserGridRows.Focus();
        e.Handled = true;
    }

    private void BrowserGridRows_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.A || !Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) return;
        _browserGrid.SelectAll();
        e.Handled = true;
    }

    private void AttachBrowserDerivedWork(IDerivedWorkBatch? batch, long generation)
    {
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
        var pending = BrowserDerivedWorkProjection.AssetsNeedingThumbnailLookup(batch.Results, _browserGrid.HasThumbnail);
        foreach (var assetId in pending)
        {
            if (generation != _browserUiGeneration) return;
            PreviewRecord? record;
            try { record = await previews.GetAsync(assetId).ConfigureAwait(true); }
            catch { continue; }
            if (generation != _browserUiGeneration || record?.ThumbnailRelativePath is null ||
                record.ThumbnailState != PreviewComponentState.Current) continue;
            string absolute;
            try { absolute = MediaPathSemantics.ResolveContained(_storage.Locations.PreviewsDirectory, record.ThumbnailRelativePath); }
            catch { continue; }
            if (File.Exists(absolute)) _browserGrid.ApplyThumbnail(assetId, absolute);
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

    private void OpenEncodingWorkspace_Click(object sender, RoutedEventArgs e) =>
        MainTabs.SelectedIndex = ShellWorkspaceSelection.Index(ShellWorkspace.Encoding);
    private async Task RefreshDependencyHealthAsync()
    {
        DependencySummary.Text = "Checking the tools needed for encoding…";
        DependencyResults.ItemsSource = null;
        var report = await DependencyHealthCheck.RunAsync(_ffmpeg, _ffprobe);
        DependencyResults.ItemsSource = report.Items;
        DependencySummary.Text = report.Summary;
        StatusText.Text = report.IsReady ? "Encoding tools ready" : "Encoding setup needs attention — open Settings";
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
        if (!string.IsNullOrWhiteSpace(initialFolder) && Directory.Exists(initialFolder)) dlg.SelectedPath = initialFolder;
        return dlg.ShowDialog() == Forms.DialogResult.OK ? dlg.SelectedPath : null;
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
    private void RefreshBatchFiles_Click(object sender, RoutedEventArgs e) => RefreshBatchFiles();
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

    private void RefreshLuts_Click(object sender, RoutedEventArgs e) => RefreshLuts();

    private int RefreshLuts()
    {
        var previousSelection = LutSelection.SelectedItem as LutOption;
        var preferredPath = previousSelection?.FilePath ?? _state.LastLutPath;
        var options = LutCatalog.Options(_settings.LutFolder);
        var lutCount = options.Count - 1;
        LutSelection.ItemsSource = options;
        LutSelection.SelectedItem = LutCatalog.SelectPreferred(options, preferredPath);
        SettingsMessage.Text = lutCount == 0
            ? $"No .cube LUT files found in {_settings.LutFolder}. Encoding will run without a LUT."
            : $"Loaded {lutCount} LUT{(lutCount == 1 ? "" : "s")}.";
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

    private void BrowseDefaultVideoFolder_Click(object sender, RoutedEventArgs e)
    {
        if (PickFolder("Select the default video folder", SettingsDefaultVideoFolder.Text) is { } folder)
            SettingsDefaultVideoFolder.Text = folder;
    }

    private void BrowseSettingsLutFolder_Click(object sender, RoutedEventArgs e)
    {
        if (PickFolder("Select the folder containing .cube LUT files", SettingsLutFolder.Text) is { } folder)
            SettingsLutFolder.Text = folder;
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
        if (MessageBox.Show("Clear and rebuild Preview metadata and thumbnails for all available Catalog assets? Offline sources will be skipped and can be rebuilt later.",
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
            MediaRootsEmptyText.Text = "The Catalog is unavailable. Encoding remains available, but Media Roots cannot be managed.";
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
            MessageBox.Show(encodingError, "Encoding settings", MessageBoxButton.OK, MessageBoxImage.Warning);
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
            _storage.SaveSettings(settings);
            _settings = _storage.Settings;
            ApplySettingsToBatch(settings);
            LocateTools();
            await RefreshDependencyHealthAsync();
            RefreshBatchFiles();
            var lutCount = RefreshLuts();
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
        PopulateSettingsControls(new AppSettings());
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
            LutFolder = SettingsLutFolder.Text,
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
        SettingsLutFolder.Text = settings.LutFolder;
        SettingsFfmpegPath.Text = settings.FfmpegPath;
        SettingsResolution.SelectedIndex = (int)settings.DefaultResolution;
        SettingsRecoveryMode.SelectedIndex = (int)settings.DefaultRecovery;
        SettingsRecursive.IsChecked = settings.IncludeSubfolders;
        SettingsPreserveFolderStructure.IsChecked = settings.PreserveFolderStructure;
        SettingsOverwriteExisting.IsChecked = settings.OverwriteExistingFiles;
        ShowEncodingDetails.IsChecked = settings.DetailedActivityLogging;
        SettingsEncodingPreset.SelectedIndex = (int)settings.EncodingPreset;
        PopulateEncodingControls(settings.Encoding);
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
    private void BrowseMedia_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Video files|*.mp4;*.mov;*.mxf;*.mkv;*.avi|All files|*.*" };
        if (Directory.Exists(_settings.DefaultVideoFolder)) dialog.InitialDirectory = _settings.DefaultVideoFolder;
        if (dialog.ShowDialog() == true) MediaPath.Text = dialog.FileName;
    }
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
                    AppendDetailedLog($"Applied trim: In {appliedRange.RequestedRange.EffectiveIn:c}; Out frame {appliedRange.RequestedRange.EffectiveOut:c}; encoded duration {appliedRange.EffectiveDuration:c}; source start {appliedRange.SourceStartTimestamp:c}");

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
                var args = FfmpegCommandBuilder.Encode(input, outputLifecycle.PartialPath, encodingOptions.LutPath,
                    encodingOptions.Recovery, encodingOptions.Resolution, detailedOutput, encodingOptions.Encoding,
                    item.PlanItem.Definition.ResolvedRange);
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
                    var validationError = "FFprobe could not open the encoded file.";
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
            AppendLog("Encoding cancelled.");
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
            MessageBox.Show(ex.Message, "Encoding error", MessageBoxButton.OK, MessageBoxImage.Error);
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
                    AppendLog($"Could not save this batch to History: {exception.Message}");
                }
            }

            var shouldClose = _closeAfterCurrent;
            _batchStopwatch = null;
            _closeAfterCurrent = false;
            _activeEncodingJob = null;
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
        _historyRecords.Clear();
        foreach (var record in _jobHistory.Load()) _historyRecords.Add(record);
        HistoryEmptyText.Visibility = _historyRecords.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_historyRecords.Count > 0 && HistoryList.SelectedItem is null) HistoryList.SelectedIndex = 0;
    }

    private void HistoryList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        HistoryDetails.Text = (HistoryList.SelectedItem as EncodingJobHistoryRecord)?.DetailDisplay ?? "Select a job to inspect its results.";
        HistoryRerunButton.IsEnabled = HistoryList.SelectedItem is EncodingJobHistoryRecord;
    }

    private void RefreshHistory_Click(object sender, RoutedEventArgs e) => RefreshHistory();

    private void RerunHistory_Click(object sender, RoutedEventArgs e)
    {
        if (HistoryList.SelectedItem is not EncodingJobHistoryRecord record) return;
        var preparation = EncodingHistoryRerun.Prepare(record);
        var restoration = EncodingHistoryRerun.Materialize(preparation);
        if (restoration.Restored.Count == 0)
        {
            MessageBox.Show("None of the original source files are still available and unchanged.", "Review Encoding job", MessageBoxButton.OK, MessageBoxImage.Warning);
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
        UpdateBatchFileSummary();
        _ = LoadBatchMetadataAsync(_batchFiles.ToList(), _batchMetadataCts.Token);
        MainTabs.SelectedIndex = ShellWorkspaceSelection.Index(ShellWorkspace.Encoding);
        CurrentFileText.Text = EncodingHistoryRerun.RestorationMessage(restoration);
    }
    private bool ValidateEncoderInputs()
    {
        if (_ffmpeg is null || !File.Exists(_ffmpeg)) { MessageBox.Show("FFmpeg was not found. Open Settings to configure ffmpeg.exe."); return false; }
        if (_ffprobe is null || !File.Exists(_ffprobe)) { MessageBox.Show("FFprobe was not found beside FFmpeg. Reinstall the packaged dependencies or update the FFmpeg path in Settings."); return false; }
        if (!Directory.Exists(InputFolder.Text)) { MessageBox.Show("Select a valid video folder."); return false; }
        if (_batchFiles.All(file => !file.IsSelected)) { MessageBox.Show("Select at least one video file for this batch."); return false; }
        if (!LutCatalog.IsValidSelection(LutSelection.SelectedItem as LutOption)) { MessageBox.Show("Select a valid .cube LUT from the LUT dropdown, or choose No LUT."); return false; }
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
            SelectedLutPath,
            suffix,
            ShouldPreserveFolderStructure(),
            OverwriteExisting.IsChecked == true,
            ShowEncodingDetails.IsChecked == true,
            Recursive.IsChecked == true);
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
                file.Metadata?.HasAudio);
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
        var process = _activeEncodingProcess;
        if (process is null) return;

        if (_encodingPause.IsPaused)
        {
            ResumeEncoding(process, "Encoding resumed by user.");
            return;
        }

        if (!_encodingPause.Pause(process)) return;
        if (_batchStopwatch?.IsRunning == true) _batchStopwatch.Stop();
        PauseButton.Content = "Resume";
        SetBatchStatus(BatchStatus.Paused);
        AppendLog("Encoding paused by user.");
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

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        SaveBatchState();
        SaveWorkspaceState();
        _previewMaintenanceCts?.Cancel();
        if (_jobCancellation is null || _forceClose) return;

        e.Cancel = true;
        var pausedProcess = _activeEncodingProcess;
        var wasAlreadyPaused = _encodingPause.IsPaused;
        var processPaused = wasAlreadyPaused || _encodingPause.Pause(pausedProcess);
        var pausedByDialog = processPaused && !wasAlreadyPaused;
        if (pausedByDialog && _batchStopwatch?.IsRunning == true) _batchStopwatch.Stop();
        if (pausedByDialog)
        {
            SetBatchStatus(BatchStatus.Paused);
            PauseButton.Content = "Resume";
            AppendDetailedLog("Encoding paused while the close options are open.");
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
                ? "Encoding resumed to finish the current file before closing."
                : "Encoding resumed.");
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
            AppendLog($"Could not save the encoding-details preference: {ex.Message}");
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

    private async void Inspect_Click(object sender, RoutedEventArgs e) => await ToolAction(async () =>
    {
        EnsureProbe(); var r = await CaptureLoggedAsync("Inspect", _ffprobe!, FfmpegCommandBuilder.Inspect(MediaPath.Text), CancellationToken.None); ToolsOutput.Text = r.StdOut + r.StdErr;
    });

    private async void Verify_Click(object sender, RoutedEventArgs e) => await ToolAction(async () =>
    {
        EnsureMedia(); EnsureFfmpeg(); ToolsOutput.Text = "Verifying every decodable frame…";
        var r = await CaptureLoggedAsync("Verify", _ffmpeg!, FfmpegCommandBuilder.Verify(MediaPath.Text), CancellationToken.None);
        var report = Path.Combine(Path.GetDirectoryName(MediaPath.Text)!, Path.GetFileNameWithoutExtension(MediaPath.Text) + "_verification.csv");
        var status = r.ExitCode == 0 ? "completed" : "failed";
        File.WriteAllText(report, "file,status,exit_code,notes\r\n" + CsvFormatter.Escape(MediaPath.Text) + $",{status},{r.ExitCode}," + CsvFormatter.Escape(r.StdErr));
        ToolsOutput.Text = $"Verification {status}. Report: {report}\r\n\r\n{r.StdErr}";
    });

    private async void Rewrap_Click(object sender, RoutedEventArgs e) => await ToolAction(async () =>
    {
        EnsureMedia(); EnsureFfmpeg(); var output = Path.Combine(Path.GetDirectoryName(MediaPath.Text)!, Path.GetFileNameWithoutExtension(MediaPath.Text) + "_rewrapped.mp4");
        var r = await CaptureLoggedAsync("Rewrap", _ffmpeg!, FfmpegCommandBuilder.Rewrap(MediaPath.Text, output), CancellationToken.None);
        ToolsOutput.Text = r.ExitCode == 0 ? $"Created: {output}" : r.StdErr;
    });

    private async void Proxy_Click(object sender, RoutedEventArgs e) => await ToolAction(async () =>
    {
        EnsureMedia(); EnsureFfmpeg(); var output = Path.Combine(Path.GetDirectoryName(MediaPath.Text)!, Path.GetFileNameWithoutExtension(MediaPath.Text) + "_proxy.mp4");
        var r = await CaptureLoggedAsync("Proxy", _ffmpeg!, FfmpegCommandBuilder.Proxy(MediaPath.Text, output), CancellationToken.None);
        ToolsOutput.Text = r.ExitCode == 0 ? $"Created: {output}" : r.StdErr;
    });

    private async void ContactSheet_Click(object sender, RoutedEventArgs e) => await ToolAction(async () =>
    {
        EnsureMedia(); EnsureFfmpeg(); var output = Path.Combine(Path.GetDirectoryName(MediaPath.Text)!, Path.GetFileNameWithoutExtension(MediaPath.Text) + "_contact-sheet.jpg");
        var r = await CaptureLoggedAsync("ContactSheet", _ffmpeg!, FfmpegCommandBuilder.ContactSheet(MediaPath.Text, output), CancellationToken.None);
        ToolsOutput.Text = r.ExitCode == 0 ? $"Created: {output}" : r.StdErr;
    });

    private async Task ToolAction(Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex)
        {
            _activityLogFile.TryAppend($"[App] Media tool action failed: {ex}");
            MessageBox.Show(ex.Message, "Media tool", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
    private void EnsureMedia() { if (!File.Exists(MediaPath.Text)) throw new InvalidOperationException("Select a valid media file."); }
    private void EnsureFfmpeg() { if (_ffmpeg is null) throw new InvalidOperationException("FFmpeg was not found."); }
    private void EnsureProbe() { EnsureMedia(); if (_ffprobe is null) throw new InvalidOperationException("ffprobe.exe was not found beside FFmpeg or in PATH."); }

    private void OpenPremiere_Click(object sender, RoutedEventArgs e)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "PremiereHelper");
        if (!Directory.Exists(path)) path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "PremiereHelper"));
        if (Directory.Exists(path)) Process.Start(new ProcessStartInfo("explorer.exe", path) { UseShellExecute = true });
        else MessageBox.Show("PremiereHelper folder not found. It is included at the package root.");
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
