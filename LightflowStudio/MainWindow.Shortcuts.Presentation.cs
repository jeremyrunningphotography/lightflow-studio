using Lightflow.Actions;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Button = System.Windows.Controls.Button;
using Point = System.Windows.Point;

namespace LightflowStudio;

public partial class MainWindow
{
    // Presentation taxonomy only: action identity, arguments and runtime ownership stay in the catalog.
    internal sealed class ShortcutRow(string id, string category, string group, string label, string context) : INotifyPropertyChanged
    {
        public string Id { get; } = id;
        public string Category { get; } = category;
        public string Group { get; } = group;
        public string Label { get; } = label;
        public string Context { get; } = context;
        public string Current { get; private set; } = "Unassigned";
        public string Default { get; private set; } = "Unassigned";
        public bool Customized { get; private set; }
        public bool Unsaved { get; private set; }
        public bool Assigned => Current != "Unassigned";
        public string State => !Assigned ? "Unassigned" : Customized ? "Customized" : "Default";
        public string Detail => string.Join(" · ", new[] {
            Customized && Assigned ? "Customized" : null,
            Customized ? $"Default: {Default}" : null, Unsaved ? "Unsaved" : null }.Where(s => s is not null));
        public string AccessibleName => $"{Label}, {Current}, {Context}, {State}, {Detail}";
        public bool IsCapturing { get; private set; }
        public bool CanApply { get; private set; }
        public string Candidate { get; private set; } = "";
        public string CaptureMessage { get; private set; } = "";
        public string CaptureTitle => CanApply ? "New shortcut" : Candidate.Length > 0 ? "Shortcut unavailable" : "Recording shortcut…";
        public void Update(ShortcutInfo current, ShortcutInfo saved) {
            Current = current.Current?.Display(ShortcutPlatform.Windows) ?? "Unassigned";
            Default = current.Default?.Display(ShortcutPlatform.Windows) ?? "Unassigned";
            Customized = current.Customized;
            Unsaved = current.Current != saved.Current || current.Customized != saved.Customized;
            Notify();
        }
        public void Capture(bool active, string candidate = "", string message = "", bool canApply = false) {
            IsCapturing = active; Candidate = candidate; CaptureMessage = message; CanApply = canApply; Notify();
        }
        private void Notify() => PropertyChanged?.Invoke(this, new(""));
        public event PropertyChangedEventHandler? PropertyChanged;
    }
    internal sealed record ShortcutSubgroup(string Title, ShortcutRow[] Rows);
    internal sealed class ShortcutSection(string title, ShortcutSubgroup[] groups, bool expanded, Action<bool>? remember) : INotifyPropertyChanged
    {
        public string Title { get; } = title;
        public ShortcutSubgroup[] Groups { get; } = groups;
        public int Count => Groups.Sum(g => g.Rows.Length);
        public string Summary => string.Join(" · ", Groups.Select(g => g.Title));
        public string AccessibleName => $"{Title}, {Count} shortcuts. {Summary}";
        private bool _expanded = expanded;
        public bool IsExpanded {
            get => _expanded;
            set { if (_expanded == value) return; _expanded = value; remember?.Invoke(value); PropertyChanged?.Invoke(this, new(nameof(IsExpanded))); }
        }
        public event PropertyChangedEventHandler? PropertyChanged;
    }
    private readonly Dictionary<string, bool> _shortcutExpansion = new();
    private readonly Dictionary<string, ShortcutRow> _shortcutRowModels = new();
    private long _shortcutFocusVersion;

    internal static ShortcutSection[] BuildShortcutSections(ShortcutProfile profile, string query, Dictionary<string, bool> expansion,
        Dictionary<string, ShortcutRow>? models = null, string? pinnedId = null, ShortcutProfile? savedProfile = null)
    {
        models ??= new();
        UpdateShortcutModels(profile, savedProfile ?? profile, models);
        var rows = KeyboardCommandCatalog.Commands.Select(command => models[command.Id])
            .Where(row => row.Id == pinnedId || $"{row.Label} {row.Category} {row.Group} {row.Id} {row.Context} {row.Current} {row.Default}"
                .Contains(query.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();
        var searching = !string.IsNullOrWhiteSpace(query);
        var order = new[] { "Navigation & Open", "Ratings", "Flags", "Color Labels", "Thumbnail Size",
            "Playback & Frames", "In / Out & Subclips", "Review Navigation & Markers", "Color / Compare Original",
            "Volume & Mute", "Playback Speed", "Zoom", "Loop, Fullscreen & Filmstrip", "Right Panel & Panel Selection", "Export" };
        return new[] { "Browser", "Player", "Presentation", "Workflow" }.Select(title => {
            var groups = rows.Where(row => row.Category == title).GroupBy(row => row.Group)
                .OrderBy(group => Array.IndexOf(order, group.Key))
                .Select(group => new ShortcutSubgroup(group.Key, group.ToArray())).ToArray();
            return new ShortcutSection(title, groups, searching || groups.Any(g => g.Rows.Any(r => r.Id == pinnedId)) || expansion.GetValueOrDefault(title),
                searching ? null : value => expansion[title] = value);
        }).Where(section => section.Count > 0).ToArray();
    }
    private static void UpdateShortcutModels(ShortcutProfile profile, ShortcutProfile savedProfile, Dictionary<string, ShortcutRow> models)
    {
        var saved = new KeyboardShortcutResolver(savedProfile, ShortcutPlatform.Windows).Query().ToDictionary(i => i.Command.Id);
        foreach (var info in new KeyboardShortcutResolver(profile, ShortcutPlatform.Windows).Query()) {
            var command = info.Command;
            var (section, group) = ShortcutGroup(command);
            if (!models.TryGetValue(command.Id, out var row)) models[command.Id] = row = new(command.Id, section, group, info.Label,
                command.Context == ShortcutContext.Home ? "Browser & Player" : command.Context.ToString());
            row.Update(info, saved[command.Id]);
        }
    }
    private static (string Section, string Group) ShortcutGroup(BindableCommand command) => command.Action.Id switch {
        BrowserActions.NavigateSelection or BrowserActions.OpenCurrent => ("Browser", "Navigation & Open"),
        BrowserActions.SetRating => ("Browser", "Ratings"),
        BrowserActions.SetFlag or BrowserActions.StepFlag => ("Browser", "Flags"),
        BrowserActions.SetColorLabel => ("Browser", "Color Labels"),
        ReviewShellActions.ThumbnailSize => ("Browser", "Thumbnail Size"),
        PlayerActions.PlayPause or PlayerActions.StepFrame => ("Player", "Playback & Frames"),
        PlayerActions.SetBoundary or PlayerActions.CreateSubclip => ("Player", "In / Out & Subclips"),
        PlayerActions.TraverseReview or PlayerActions.AddMarker or PlayerActions.NavigateMarker => ("Player", "Review Navigation & Markers"),
        PlayerActions.ColorBypass => ("Player", "Color / Compare Original"),
        ReviewPresentationActions.Volume => ("Presentation", "Volume & Mute"),
        ReviewPresentationActions.Speed => ("Presentation", "Playback Speed"),
        ReviewPresentationActions.Zoom or ReviewPresentationActions.StepZoom => ("Presentation", "Zoom"),
        ReviewPresentationActions.Toggle => ("Presentation", command.Arguments is PresentationToggleArguments { Toggle: PresentationToggle.Mute } ? "Volume & Mute" : "Loop, Fullscreen & Filmstrip"),
        ReviewShellActions.TogglePanel or ReviewShellActions.ShowPanel => ("Workflow", "Right Panel & Panel Selection"),
        ReviewShellActions.Export => ("Workflow", "Export"),
        _ => (command.Action.Category, "Other")
    };

    private void ShortcutMore_Click(object sender, RoutedEventArgs e)
    {
        var button = (Button)sender;
        var row = (ShortcutRow)button.DataContext;
        var menu = new ContextMenu { Style = (Style)FindResource("LightflowContextMenuStyle"), PlacementTarget = button };
        void Add(string label, bool enabled, RoutedEventHandler handler) {
            var item = new MenuItem { Header = label, Tag = row.Id, IsEnabled = enabled, Style = (Style)FindResource("LightflowMenuItemStyle") };
            System.Windows.Automation.AutomationProperties.SetName(item, $"{label} shortcut for {row.Label}");
            item.Click += handler;
            menu.Items.Add(item);
        }
        Add("Unassign", row.Assigned, ShortcutClear_Click);
        Add("Reset to Default", row.Customized, ShortcutReset_Click);
        button.ContextMenu = menu;
        menu.IsOpen = true;
    }

    internal FrameworkElement? FindShortcutControl(string id, string name) => ShortcutVisuals(ShortcutRows)
        .OfType<FrameworkElement>().FirstOrDefault(element => element.Name == name && element.DataContext is ShortcutRow { Id: var rowId } && rowId == id);
    private void ShortcutSection_Collapsed(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource == sender && sender is Expander { DataContext: ShortcutSection section } && _captureRow?.Category == section.Title)
            CancelShortcutCapture(false);
    }
    private static IEnumerable<DependencyObject> ShortcutVisuals(DependencyObject parent)
    {
        for (var index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); index++) {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, index);
            yield return child;
            foreach (var descendant in ShortcutVisuals(child)) yield return descendant;
        }
    }
    private void FocusShortcutControl(string id, string name)
    {
        var version = ++_shortcutFocusVersion;
        // Menus restore their own focus on closing; defer until that completes. No Search fallback.
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, new Action(() => {
            if (version != _shortcutFocusVersion || !IsActive || MainTabs.SelectedIndex != ShellDestinationSelection.Index(ShellDestination.Settings) ||
                SettingsShortcutsPage.Visibility != Visibility.Visible) return;
            ShortcutRows.UpdateLayout();
            FindShortcutControl(id, name)?.Focus();
            if (FindShortcutControl(id, "ShortcutRowContainer") is not { } row) return;
            var top = row.TranslatePoint(new Point(), SettingsShortcutsPage).Y;
            var viewport = SettingsShortcutsPage.ViewportHeight;
            if (top < 0) SettingsShortcutsPage.ScrollToVerticalOffset(SettingsShortcutsPage.VerticalOffset + top);
            else if (row.ActualHeight <= viewport && top + row.ActualHeight > viewport)
                SettingsShortcutsPage.ScrollToVerticalOffset(SettingsShortcutsPage.VerticalOffset + top + row.ActualHeight - viewport);
        }));
    }
}
