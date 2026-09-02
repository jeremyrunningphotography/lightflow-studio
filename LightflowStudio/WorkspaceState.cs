using System.IO;
using System.Text.Json;

namespace LightflowStudio;

/// <summary>
/// Root-agnostic Browser location identity: the Media Root's stable Catalog identity plus the folder's path
/// relative to that root. <see cref="LastResolvedAbsolutePath"/> is retained only as fallback/diagnostic
/// information; restoration never treats it as authoritative.
/// </summary>
internal sealed record WorkspaceBrowserLocationState
{
    public Guid RootId { get; init; }
    public string RelativeFolder { get; init; } = "";
    public string? LastResolvedAbsolutePath { get; init; }

    /// <summary>
    /// #124 (superseded): recursive-scope configuration is now durable, user-authored Catalog data — see
    /// <see cref="BrowserRecursiveRoot"/> — not workspace/session state, since it must survive application
    /// restarts, navigating elsewhere, and Media Root remapping independent of whatever Browser folder
    /// happens to be open when Lightflow closes. This property is kept only so a document written by the
    /// short-lived earlier revision of this feature still deserializes without error; it is never written by
    /// <see cref="WorkspaceStateService.SetBrowserLocation"/> and never read to control effective recursive
    /// mode. Do not use it for anything new.
    /// </summary>
    public bool IncludeSubfolders { get; init; }
}

/// <summary>Desktop window bounds. <see cref="Width"/>/<see cref="Height"/>/<see cref="Left"/>/<see cref="Top"/> are always the restored (non-maximized, non-minimized) bounds.</summary>
internal sealed record WorkspaceWindowState
{
    public double Width { get; init; }
    public double Height { get; init; }
    public double Left { get; init; }
    public double Top { get; init; }
    public bool IsMaximized { get; init; }
}

/// <summary>
/// Persisted workspace splitter/panel dimensions. New properties can be added here for future panels
/// (Inspector width, Player filmstrip height, Compare divider position, ...) without any architectural change:
/// missing values simply deserialize to null and the corresponding UI keeps its built-in default.
/// </summary>
internal sealed record WorkspaceLayoutState
{
    public double? BrowserLocationsPaneWidth { get; init; }
    public double? JobsDrawerWidth { get; init; }
    public double? FullJobsListPaneWidth { get; init; }

    /// <summary>
    /// #125: the Browser's chosen thumbnail-size level, persisted as a plain index into
    /// <see cref="BrowserGridLayout.ThumbnailSizes"/> rather than the <see cref="BrowserThumbnailSize"/> enum
    /// itself or a raw pixel value — presentation/UI state only, never Catalog or Preview data. Null means
    /// "no preference saved yet"; the Browser falls back to <see cref="BrowserGridLayout.DefaultThumbnailSize"/>.
    /// </summary>
    public int? BrowserThumbnailSizeLevel { get; init; }

    /// <summary>#161: one global Browser presentation preference, stored beside Preview size.</summary>
    public int? BrowserViewMode { get; init; }
    public Guid? BrowserCollectionId { get; init; }
    public IReadOnlyList<Guid> BrowserExpandedCollectionSetIds { get; init; } = [];
    public bool? BrowserLocationsSectionExpanded { get; init; }
    public bool? BrowserCollectionsSectionExpanded { get; init; }
}

/// <summary>Versioned, tolerant root document for per-user/per-machine workspace UI state. Never Catalog or Preview data.</summary>
internal sealed record WorkspaceState
{
    public const int CurrentVersion = 1;

    // Mirrors MainWindow.xaml's BrowserNavigationColumn MinWidth/MaxWidth.
    public const double MinLocationsPaneWidth = 220;
    public const double MaxLocationsPaneWidth = 520;
    public const double MinJobsDrawerWidth = 340;
    public const double MaxJobsDrawerWidth = 620;
    public const double MinFullJobsListPaneWidth = 340;
    public const double MaxFullJobsListPaneWidth = 720;

    public int Version { get; init; } = CurrentVersion;
    public WorkspaceBrowserLocationState? Browser { get; init; }
    public WorkspaceWindowState? Window { get; init; }
    public WorkspaceLayoutState? Layout { get; init; }

    public static WorkspaceState Empty { get; } = new();

    /// <summary>Coerces a freshly deserialized (possibly malformed or partially unusable) document into a safe document. Never throws.</summary>
    public static WorkspaceState Normalize(WorkspaceState? state)
    {
        if (state is null) return Empty;
        return new WorkspaceState
        {
            Version = CurrentVersion,
            Browser = NormalizeBrowser(state.Browser),
            Window = NormalizeWindow(state.Window),
            Layout = NormalizeLayout(state.Layout)
        };
    }

    private static WorkspaceBrowserLocationState? NormalizeBrowser(WorkspaceBrowserLocationState? browser)
    {
        if (browser is null || browser.RootId == Guid.Empty) return null;
        try
        {
            var relative = browser.RelativeFolder.Length == 0
                ? ""
                : MediaPathSemantics.NormalizeRelativePath(browser.RelativeFolder);
            return browser with { RelativeFolder = relative };
        }
        catch (ArgumentException)
        {
            // A malformed relative folder invalidates only the Browser section, not the whole document.
            return null;
        }
    }

    private static WorkspaceWindowState? NormalizeWindow(WorkspaceWindowState? window) =>
        window is null || window.Width <= 0 || window.Height <= 0 ||
        !double.IsFinite(window.Width) || !double.IsFinite(window.Height) ||
        !double.IsFinite(window.Left) || !double.IsFinite(window.Top)
            ? null
            : window;

    private static WorkspaceLayoutState? NormalizeLayout(WorkspaceLayoutState? layout)
    {
        if (layout is null) return null;
        var paneWidth = layout.BrowserLocationsPaneWidth is { } width && double.IsFinite(width)
            ? Math.Clamp(width, MinLocationsPaneWidth, MaxLocationsPaneWidth)
            : (double?)null;
        var jobsDrawerWidth = layout.JobsDrawerWidth is { } drawerWidth && double.IsFinite(drawerWidth)
            ? Math.Clamp(drawerWidth, MinJobsDrawerWidth, MaxJobsDrawerWidth)
            : (double?)null;
        var fullJobsListPaneWidth = layout.FullJobsListPaneWidth is { } listWidth && double.IsFinite(listWidth)
            ? Math.Clamp(listWidth, MinFullJobsListPaneWidth, MaxFullJobsListPaneWidth)
            : (double?)null;
        // Clamped rather than discarded: an out-of-range level (e.g. from a future build with more sizes,
        // opened by an older one) still lands on a valid size instead of silently reverting to the default.
        var thumbnailSizeLevel = layout.BrowserThumbnailSizeLevel is { } level
            ? Math.Clamp(level, 0, BrowserGridLayout.ThumbnailSizes.Count - 1)
            : (int?)null;
        var browserViewMode = layout.BrowserViewMode is { } mode && Enum.IsDefined((BrowserViewMode)mode)
            ? mode : (int?)null;
        var collectionId = layout.BrowserCollectionId is { } id && id != Guid.Empty ? id : (Guid?)null;
        var expandedSets = (layout.BrowserExpandedCollectionSetIds ?? []).Where(id => id != Guid.Empty).Distinct().ToArray();
        return layout with { BrowserLocationsPaneWidth = paneWidth, JobsDrawerWidth = jobsDrawerWidth,
            FullJobsListPaneWidth = fullJobsListPaneWidth,
            BrowserThumbnailSizeLevel = thumbnailSizeLevel, BrowserViewMode = browserViewMode,
            BrowserCollectionId = collectionId, BrowserExpandedCollectionSetIds = expandedSets };
    }
}

/// <summary>Tolerant, atomic JSON persistence for <see cref="WorkspaceState"/>, matching the AppSettingsStore convention.</summary>
internal static class WorkspaceStateStore
{
    public static WorkspaceState Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return WorkspaceState.Empty;
            return WorkspaceState.Normalize(JsonSerializer.Deserialize<WorkspaceState>(File.ReadAllText(path)));
        }
        catch (JsonException)
        {
            return WorkspaceState.Empty;
        }
        catch (IOException)
        {
            return WorkspaceState.Empty;
        }
        catch (UnauthorizedAccessException)
        {
            return WorkspaceState.Empty;
        }
    }

    public static void Save(string path, WorkspaceState state)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        var temporaryPath = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporaryPath,
                JsonSerializer.Serialize(WorkspaceState.Normalize(state), new JsonSerializerOptions { WriteIndented = true }));
            if (File.Exists(path)) File.Replace(temporaryPath, path, null);
            else File.Move(temporaryPath, path);
        }
        finally
        {
            try { File.Delete(temporaryPath); } catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
    }
}

/// <summary>
/// Reusable, WPF-independent workspace-state abstraction. Holds the current document in memory so callers can
/// update individual sections cheaply and often, while <see cref="Save"/> is only ever called explicitly at
/// deliberate lifecycle points (never implicitly on every mutation), keeping disk writes infrequent.
/// </summary>
internal sealed class WorkspaceStateService
{
    private readonly string _path;
    private WorkspaceState _current;

    public WorkspaceStateService(string path) : this(path, WorkspaceStateStore.Load(path)) { }

    internal WorkspaceStateService(string path, WorkspaceState initial)
    {
        _path = path;
        _current = WorkspaceState.Normalize(initial);
    }

    public WorkspaceState Current => _current;

    public void SetBrowserLocation(Guid rootId, string relativeFolder, string? lastResolvedAbsolutePath) =>
        _current = _current with
        {
            Browser = new WorkspaceBrowserLocationState
            {
                RootId = rootId,
                RelativeFolder = relativeFolder,
                LastResolvedAbsolutePath = lastResolvedAbsolutePath
            }
        };

    public void SetWindow(WorkspaceWindowState window) => _current = _current with { Window = window };

    // Both layout setters merge into the existing Layout section rather than replacing it outright — with
    // two independent properties now living there (#125 added BrowserThumbnailSizeLevel alongside #121's
    // BrowserLocationsPaneWidth), a wholesale replacement would silently drop whichever one was set most
    // recently, since SaveWorkspaceState calls both setters back-to-back at the same shutdown/debounce point.
    public void SetBrowserLocationsPaneWidth(double width) =>
        _current = _current with { Layout = (_current.Layout ?? new WorkspaceLayoutState()) with { BrowserLocationsPaneWidth = width } };

    public void SetJobsDrawerWidth(double width) =>
        _current = _current with { Layout = (_current.Layout ?? new WorkspaceLayoutState()) with { JobsDrawerWidth = width } };

    public void SetFullJobsListPaneWidth(double width) =>
        _current = _current with { Layout = (_current.Layout ?? new WorkspaceLayoutState()) with { FullJobsListPaneWidth = width } };

    public void SetBrowserThumbnailSizeLevel(int level) =>
        _current = _current with { Layout = (_current.Layout ?? new WorkspaceLayoutState()) with { BrowserThumbnailSizeLevel = level } };

    public BrowserViewMode GetBrowserViewMode() => _current.Layout?.BrowserViewMode is { } mode &&
        Enum.IsDefined((BrowserViewMode)mode) ? (BrowserViewMode)mode : BrowserViewMode.Preview;

    public void SetBrowserViewMode(BrowserViewMode mode) =>
        _current = _current with { Layout = (_current.Layout ?? new WorkspaceLayoutState()) with { BrowserViewMode = (int)mode } };

    public void SetBrowserCollectionState(Guid? collectionId, IReadOnlySet<Guid> expandedSetIds) =>
        _current = _current with { Layout = (_current.Layout ?? new WorkspaceLayoutState()) with
        {
            BrowserCollectionId = collectionId,
            BrowserExpandedCollectionSetIds = expandedSetIds.Order().ToArray()
        } };

    public void SetBrowserScopeSectionState(bool locationsExpanded, bool collectionsExpanded) =>
        _current = _current with { Layout = (_current.Layout ?? new WorkspaceLayoutState()) with
        {
            BrowserLocationsSectionExpanded = locationsExpanded,
            BrowserCollectionsSectionExpanded = collectionsExpanded
        } };

    /// <summary>Writes the current in-memory document atomically. Never throws: a failed save must not disrupt shutdown or navigation.</summary>
    public void Save()
    {
        try { WorkspaceStateStore.Save(_path, _current); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }
}
