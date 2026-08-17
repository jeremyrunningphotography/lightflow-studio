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
}

/// <summary>Versioned, tolerant root document for per-user/per-machine workspace UI state. Never Catalog or Preview data.</summary>
internal sealed record WorkspaceState
{
    public const int CurrentVersion = 1;

    // Mirrors MainWindow.xaml's BrowserNavigationColumn MinWidth/MaxWidth.
    public const double MinLocationsPaneWidth = 220;
    public const double MaxLocationsPaneWidth = 520;

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
        if (layout.BrowserLocationsPaneWidth is not { } width || !double.IsFinite(width))
            return layout with { BrowserLocationsPaneWidth = null };
        return layout with { BrowserLocationsPaneWidth = Math.Clamp(width, MinLocationsPaneWidth, MaxLocationsPaneWidth) };
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

    public void SetBrowserLocationsPaneWidth(double width) =>
        _current = _current with { Layout = new WorkspaceLayoutState { BrowserLocationsPaneWidth = width } };

    /// <summary>Writes the current in-memory document atomically. Never throws: a failed save must not disrupt shutdown or navigation.</summary>
    public void Save()
    {
        try { WorkspaceStateStore.Save(_path, _current); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }
}
