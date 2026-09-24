using System.Windows;
using System.Windows.Controls;

namespace LightflowStudio;

public partial class SmartCollectionDialog : Window
{
    private readonly IBrowserLocationResolver _locations;
    private SmartCollectionSource? _folderSource;
    private readonly List<BrowserFilterRowEditor> _rows = [];
    private IReadOnlyList<BrowserGridTile> _valueTiles = [];
    private readonly Func<SmartCollectionSource, Task<IReadOnlyList<BrowserGridTile>>>? _loadValues;
    private int _valuesRequest;
    private bool _ready;
    private sealed record MatchChoice(BrowserMatchMode Value, string Name);
    private sealed class MatchSelection
    {
        public MatchChoice[] Choices { get; } = [new(BrowserMatchMode.All, "all"), new(BrowserMatchMode.Any, "any")];
        public BrowserMatchMode Value { get; set; }
    }
    private readonly MatchSelection _match = new();
    private sealed record Choice(Guid Id, string Name);
    internal SmartCollectionDialog(IBrowserLocationResolver locations, IReadOnlyList<MediaRootInfo> roots,
        IReadOnlyList<CollectionSetPlacementOption> sets, IReadOnlyList<(Guid Id, string Name)> collections,
        string name, Guid? parent, SmartCollectionSource? source, BrowserQueryIntent query, bool editing, Func<SmartCollectionSource, Task<IReadOnlyList<BrowserGridTile>>>? loadValues = null)
    {
        _locations = locations; _loadValues = loadValues;
        InitializeComponent();
        Title = DialogTitle.Text = editing ? "Edit Smart Collection" : "Create Smart Collection";
        SaveButton.Content = editing ? "Save changes" : "Create Smart Collection";
        NameText.Text = name;
        LocationCombo.ItemsSource = sets;
        LocationCombo.SelectedItem = sets.FirstOrDefault(set => set.CollectionSetId == parent) ?? sets.First();
        SourceCollectionCombo.ItemsSource = collections.Select(c => new Choice(c.Id, c.Name)).ToArray();
        SourceCollectionCombo.SelectedItem = SourceCollectionCombo.Items.OfType<Choice>().FirstOrDefault(c => c.Id == source?.CollectionId);
        _folderSource = source?.Kind == SmartCollectionSourceKind.Folder ? source : null;
        if (_folderSource is { } folder)
        {
            var root = roots.FirstOrDefault(r => r.RootId == folder.RootId);
            FolderText.Text = $"{root?.DisplayName ?? "Unavailable Media Root"} / {folder.RelativeFolder}";
            RecursiveCheck.IsChecked = folder.IncludeSubfolders;
        }
        SourceKindCombo.SelectedIndex = source is null ? -1 : (int)source.Kind;
        _match.Value = query.MatchMode; MatchHost.Content = _match;
        foreach (var group in query.ToQuery().Filters.GroupBy(p => p.Field)) AddRow(group.Key, group);
        _ready = true;
        SourceCollectionCombo.SelectionChanged += async (_, _) => await RefreshValuesAsync();
        RecursiveCheck.Checked += async (_, _) => await RefreshValuesAsync();
        RecursiveCheck.Unchecked += async (_, _) => await RefreshValuesAsync();
        Loaded += async (_, _) => await RefreshValuesAsync();
        Closed += (_, _) => ++_valuesRequest;
        Loaded += (_, _) => NameText.Focus();
        SourceInitialized += (_, _) => WindowAppearance.EnableDarkTitleBar(this);
    }
    internal string CollectionName => NameText.Text.Trim();
    internal Guid? ParentSetId => (LocationCombo.SelectedItem as CollectionSetPlacementOption)?.CollectionSetId;
    internal SmartCollectionSource? Source => SourceKindCombo.SelectedIndex switch
    {
        0 => _folderSource is null ? null : _folderSource with { IncludeSubfolders = RecursiveCheck.IsChecked == true },
        1 => SourceCollectionCombo.SelectedItem is Choice c ? new(SmartCollectionSourceKind.Collection, CollectionId: c.Id) : null,
        _ => null
    };
    internal BrowserQueryIntent Query => new() { MatchMode = _match.Value, Filters = _rows.SelectMany(row => row.Predicates).ToArray() };
    private void SourceKindChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FolderPanel is null) return;
        FolderPanel.Visibility = SourceKindCombo.SelectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
        SourceCollectionCombo.Visibility = SourceKindCombo.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        if (_ready) _ = RefreshValuesAsync();
    }
    private async void ChooseFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Choose Smart Collection Source" };
        if (dialog.ShowDialog(this) != true) return;
        var result = await _locations.ResolveAsync(dialog.FolderName);
        if (!result.Succeeded) { ErrorText.Text = result.Diagnostic ?? "This folder could not be used."; return; }
        _folderSource = new(SmartCollectionSourceKind.Folder, result.RootId, result.RelativeFolder);
        FolderText.Text = dialog.FolderName; ErrorText.Text = ""; await RefreshValuesAsync();
    }
    private void AddRow(BrowserFilterField field, IEnumerable<BrowserFilterPredicate> alternatives)
    {
        BrowserFilterRowEditor? row = null;
        row = new(field, alternatives, _valueTiles, f => _rows.All(r => r.Field != f),
            () => { ErrorText.Text = ""; UpdateAddFilter(); },
            () => { _rows.Remove(row!); FilterRows.Children.Remove(row!); UpdateAddFilter(); });
        _rows.Add(row); FilterRows.Children.Add(row); UpdateAddFilter();
    }
    private void UpdateAddFilter() => AddFilterButton.IsEnabled = BrowserFilterDescriptors.All.Any(d => d.CanAuthor(_valueTiles) && _rows.All(r => r.Field != d.Field));
    private void AddFilter_Click(object sender, RoutedEventArgs e)
    {
        var descriptor = BrowserFilterDescriptors.All.FirstOrDefault(d => d.CanAuthor(_valueTiles) && _rows.All(row => row.Field != d.Field));
        if (descriptor is not null) AddRow(descriptor.Field, []);
    }
    private async Task RefreshValuesAsync()
    {
        var request = ++_valuesRequest;
        if (_loadValues is null) return;
        try
        {
            ValuesStatus.Text = "Loading known filter values…";
            var tiles = Source is { } source ? await _loadValues(source) : Array.Empty<BrowserGridTile>();
            if (request != _valuesRequest) return;
            _valueTiles = tiles;
            foreach (var row in _rows) row.RefreshValues(tiles);
            UpdateAddFilter();
            ValuesStatus.Text = "";
        }
        catch (Exception ex) when (ex is System.IO.IOException or InvalidOperationException or Microsoft.Data.Sqlite.SqliteException)
        {
            if (request != _valuesRequest) return;
            _valueTiles = [];
            foreach (var row in _rows) row.RefreshValues(_valueTiles);
            ValuesStatus.Text = "Known values could not be loaded. Saved choices remain available.";
        }
    }
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (CollectionName.Length == 0) { ErrorText.Text = "Enter a name."; NameText.Focus(); return; }
        if (Source is null) { ErrorText.Text = "Choose a Folder or static Collection Source."; return; }
        if (_rows.Any(row => !row.IsValid)) { ErrorText.Text = "Complete each filter or remove it. Date ranges must end on or after their start."; return; }
        DialogResult = true;
    }
}
