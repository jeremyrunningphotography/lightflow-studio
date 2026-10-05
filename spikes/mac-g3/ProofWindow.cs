using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Lightflow.G3;

public sealed record MediaRow(int Id, string Name, string Kind, string Dimensions, string Size, string PreviewPath);
public sealed record MediaBand(MediaRow[] Items);

public sealed class ProofWindow : Window
{
    public MediaRow[] Rows { get; }
    public TableView Details { get; } = new() { SelectionMode = SelectionMode.Multiple, AutoScrollToSelectedItem = false };
    public ListBox Tiles { get; } = new() { AutoScrollToSelectedItem = false };
    public TextBox Search { get; } = new() { PlaceholderText = "Search media", Width = 250 };
    public int TileBuilds { get; private set; }
    public int ThumbnailLoads { get; private set; }
    public int ThumbnailDisposals { get; private set; }
    private readonly HashSet<int> _selected = [];
    private readonly ContentControl _media = new();
    private readonly TextBlock _status = new();
    private readonly Dictionary<int, Button> _visibleTiles = [];
    private int _columns = 4;
    private int _current;
    private int _anchor;
    private bool _details;

    public ProofWindow(int count)
    {
        Title = "Lightflow • G3 capability proof";
        Width = 1400; Height = 900; MinWidth = 1120; MinHeight = 720;
        FontSize = 13;
        var fixtureRoot = Path.Combine(Program.DataRoot, "fixtures");
        ImageEvidence.CreateFixtures(fixtureRoot);
        Rows = Enumerable.Range(0, count).Select(i => new MediaRow(i, $"Media_{i:D6}.jpg",
            i % 3 == 0 ? "Video" : "Image", "6000 × 4000", $"{8 + i % 64} MB",
            Path.Combine(fixtureRoot, $"thumb-{i % 32:D2}.png"))).ToArray();
        Details.ItemsSource = Rows;
        Details.Columns.Add(new TableViewColumn { Header = "Preview", Width = new GridLength(112),
            CellTemplate = new FuncDataTemplate<MediaRow>((row, _) => Thumbnail(row!, 96, 54)) });
        foreach (var (header, property, width) in new[] { ("Name", "Name", 240), ("Media Type", "Kind", 95),
            ("Dimensions", "Dimensions", 112), ("File Size", "Size", 95) })
            Details.Columns.Add(new TableViewColumn { Header = header, Binding = new Binding(property), Width = new GridLength(width) });
        // Exercise the full 18-column load from the accepted Details catalog.
        foreach (var header in new[] { "Rating", "Flag", "Capture Date", "Duration", "Frame Rate", "Modified Date",
            "Color Label", "Camera", "Lens", "Color / LUT", "Saved Range", "Subclips", "Keywords" })
            Details.Columns.Add(new TableViewColumn { Header = header, Width = new GridLength(100), Binding = new Binding("Kind") });
        Details.SelectionChanged += (_, _) =>
        {
            if (!_details) return;
            _selected.Clear();
            foreach (var row in Details.SelectedItems!.Cast<MediaRow>()) _selected.Add(row.Id);
            if (Details.SelectedItem is MediaRow current) _current = current.Id;
            Status();
        };
        Tiles.ItemTemplate = new FuncDataTemplate<MediaBand>((band, _) =>
        {
            if (band is null) return new Border();
            TileBuilds++;
            var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Height = 146 };
            foreach (var row in band!.Items)
            {
                var tile = new Button { Width = 168, Height = 138, Padding = new Thickness(6),
                    Content = new StackPanel { Spacing = 6, Children = { Thumbnail(row, 154, 98), new TextBlock { Text = row.Name } } } };
                AutomationProperties.SetName(tile, $"{row.Name}, {row.Kind} media");
                tile.Background = Brush.Parse(_selected.Contains(row.Id) ? "#282129" : "#171A20");
                tile.BorderBrush = Brush.Parse(_selected.Contains(row.Id) ? "#FF9A66" : "#2B303A");
                tile.Click += (_, _) => SelectTile(row.Id, false, false);
                tile.PointerPressed += (_, e) =>
                {
                    if (e.GetCurrentPoint(tile).Properties.IsRightButtonPressed)
                    { if (!_selected.Contains(row.Id)) SelectTile(row.Id, false, false); return; }
                    SelectTile(row.Id, e.KeyModifiers.HasFlag(KeyModifiers.Shift),
                        e.KeyModifiers.HasFlag(OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control));
                    e.Handled = true;
                };
                tile.ContextMenu = new ContextMenu { ItemsSource = new[] { new MenuItem { Header = "Inspect selection" },
                    new MenuItem { Header = "Regenerate Previews", IsEnabled = false } } };
                tile.AttachedToVisualTree += (_, _) => _visibleTiles[row.Id] = tile;
                tile.DetachedFromVisualTree += (_, _) => _visibleTiles.Remove(row.Id);
                line.Children.Add(tile);
            }
            return line;
        });
        Regroup(Rows);
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(14, 8),
            Children = { new TextBlock { Text = "Browser", FontSize = 18, VerticalAlignment = VerticalAlignment.Center }, Search,
                Action("Grid", () => ShowDetails(false)), Action("Details", () => ShowDetails(true)),
                Action("Settings", ShowSettings) } };
        Search.TextChanged += (_, _) =>
        {
            var filtered = Rows.Where(x => x.Name.Contains(Search.Text ?? "", StringComparison.OrdinalIgnoreCase)).ToArray();
            Details.ItemsSource = filtered; Regroup(filtered); Status();
        };
        Tiles.KeyDown += (_, e) =>
        {
            var next = e.Key switch { Key.Left => _current - 1, Key.Right => _current + 1,
                Key.Up => _current - _columns, Key.Down => _current + _columns,
                Key.Home => 0, Key.End => Rows.Length - 1, _ => -1 };
            if (next < 0) return;
            SelectTile(Math.Clamp(next, 0, Rows.Length - 1), e.KeyModifiers.HasFlag(KeyModifiers.Shift), false);
            Tiles.ScrollIntoView(_current / _columns); e.Handled = true;
        };
        var tree = new TreeView { ItemsSource = new[] { new TreeViewItem { Header = "Locations", IsExpanded = true,
            ItemsSource = new[] { new TreeViewItem { Header = "Proof media (online)" }, new TreeViewItem { Header = "Archive (offline)" } } } } };
        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("220,*,260") };
        body.Children.Add(new Border { Background = Brush.Parse("#171A20"), Padding = new Thickness(9), Child = tree });
        Grid.SetColumn(_media, 1); body.Children.Add(_media);
        var inspector = new Border { Background = Brush.Parse("#171A20"), Padding = new Thickness(9), Child = new StackPanel
        { Spacing = 12, Children = { new TextBlock { Text = "Inspector", FontSize = 18 }, new TextBlock { Text = "Catalog descriptions" },
            new TextBox { PlaceholderText = "Caption", AcceptsReturn = true, Height = 96 }, new TextBlock { Text = "Changes remain staged" } } } };
        Grid.SetColumn(inspector, 2); body.Children.Add(inspector);
        var shell = new DockPanel(); DockPanel.SetDock(toolbar, Dock.Top); shell.Children.Add(toolbar);
        DockPanel.SetDock(_status, Dock.Bottom); _status.Margin = new Thickness(14, 8); shell.Children.Add(_status); shell.Children.Add(body);
        Content = shell;
        SizeChanged += (_, _) =>
        {
            var columns = Math.Max(1, (int)((ClientSize.Width - 480) / 176));
            if (columns == _columns) return;
            _columns = columns; Regroup(Details.ItemsSource!.Cast<MediaRow>().ToArray()); Tiles.ScrollIntoView(_current / _columns);
        };
        var menu = new NativeMenu(); var settings = new NativeMenuItem("Settings…"); settings.Click += (_, _) => ShowSettings();
        menu.Items.Add(settings); NativeMenu.SetMenu(this, menu);
        ShowDetails(false);
    }

    private Image Thumbnail(MediaRow row, double width, double height)
    {
        var image = new Image { Width = width, Height = height, Stretch = Stretch.Uniform };
        if (row is null) return image;
        Bitmap? bitmap = null;
        image.AttachedToVisualTree += (_, _) => { bitmap = new Bitmap(row.PreviewPath); ThumbnailLoads++; image.Source = bitmap; };
        image.DetachedFromVisualTree += (_, _) => { image.Source = null; bitmap?.Dispose(); bitmap = null; ThumbnailDisposals++; };
        return image;
    }
    private void Regroup(MediaRow[] rows) => Tiles.ItemsSource = rows.Chunk(_columns).Select(x => new MediaBand(x)).ToArray();
    public void SelectTile(int id, bool extend, bool toggle)
    {
        if (extend) { _selected.Clear(); foreach (var i in Enumerable.Range(Math.Min(_anchor, id), Math.Abs(_anchor - id) + 1)) _selected.Add(i); }
        else if (toggle) { if (!_selected.Add(id)) _selected.Remove(id); _anchor = id; }
        else { _selected.Clear(); _selected.Add(id); _anchor = id; }
        _current = id;
        foreach (var (key, tile) in _visibleTiles)
        { tile.Background = Brush.Parse(_selected.Contains(key) ? "#282129" : "#171A20"); tile.BorderBrush = Brush.Parse(_selected.Contains(key) ? "#FF9A66" : "#2B303A"); }
        Status();
    }
    public int[] SelectedIds => _selected.Order().ToArray();
    public void ShowDetails(bool details)
    {
        _details = false;
        Details.SelectedItems!.Clear();
        foreach (var id in _selected) Details.SelectedItems.Add(Rows[id]);
        _details = details; _media.Content = details ? Details : Tiles;
        if (details) Details.ScrollIntoView(Rows[_current]); else Tiles.ScrollIntoView(_current / _columns);
        Status();
    }
    private void Status() => _status.Text = $"{Rows.Length:N0} synthetic media • {_selected.Count} selected • G3 proof; no Catalog connected";
    private static Button Action(string label, Action action)
    { var button = new Button { Content = label }; button.Click += (_, _) => action(); return button; }
    private void ShowSettings() => new ShortcutWindow().Show(this);
}
