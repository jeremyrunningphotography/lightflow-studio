using Lightflow.Actions;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Button = System.Windows.Controls.Button;

namespace LightflowStudio;

public partial class MainWindow
{
    // Presentation taxonomy only: action identity, arguments and runtime ownership stay in the catalog.
    internal sealed record ShortcutRow(string Id, string Category, string Group, string Label, string Context,
        string Current, string Default, bool Customized)
    {
        public bool Assigned => Current != "Unassigned";
        public string State => !Assigned ? "Unassigned" : Customized ? "Customized" : "Default";
        public string Detail => $"{Context} · {State}" + (Customized ? $" · Default: {Default}" : "");
        public string AccessibleName => $"{Label}, {Current}, {Detail}";
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

    internal static ShortcutSection[] BuildShortcutSections(ShortcutProfile profile, string query, Dictionary<string, bool> expansion)
    {
        var rows = new KeyboardShortcutResolver(profile, ShortcutPlatform.Windows).Query().Select(info => {
            var command = info.Command;
            var (section, group) = ShortcutGroup(command);
            return new ShortcutRow(command.Id, section, group, info.Label,
                command.Context == ShortcutContext.Home ? "Browser & Player" : command.Context.ToString(),
                info.Current?.Display(ShortcutPlatform.Windows) ?? "Unassigned",
                info.Default?.Display(ShortcutPlatform.Windows) ?? "Unassigned", info.Customized);
        }).Where(row => $"{row.Label} {row.Category} {row.Group} {row.Id} {row.Context} {row.Current} {row.Default}"
            .Contains(query.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();
        var searching = !string.IsNullOrWhiteSpace(query);
        var order = new[] { "Navigation & Open", "Ratings", "Flags", "Color Labels", "Thumbnail Size",
            "Playback & Frames", "In / Out & Subclips", "Review Navigation & Markers", "Color / Compare Original",
            "Volume & Mute", "Playback Speed", "Zoom", "Loop, Fullscreen & Filmstrip", "Right Panel & Panel Selection", "Export" };
        return new[] { "Browser", "Player", "Presentation", "Review / Shell" }.Select(title => {
            var groups = rows.Where(row => row.Category == title).GroupBy(row => row.Group)
                .OrderBy(group => Array.IndexOf(order, group.Key))
                .Select(group => new ShortcutSubgroup(group.Key, group.ToArray())).ToArray();
            return new ShortcutSection(title, groups, searching || expansion.GetValueOrDefault(title),
                searching ? null : value => expansion[title] = value);
        }).Where(section => section.Count > 0).ToArray();
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
        ReviewShellActions.TogglePanel or ReviewShellActions.ShowPanel => ("Review / Shell", "Right Panel & Panel Selection"),
        ReviewShellActions.Export => ("Review / Shell", "Export"),
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
}
