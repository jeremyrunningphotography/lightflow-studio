using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

namespace LightflowStudio;

public partial class SmartCollectionDialog : Window
{
    private readonly IBrowserLocationResolver _locations;
    private SmartCollectionSource? _folderSource;
    private readonly ObservableCollection<BrowserFilterPredicate> _rules = [];
    private sealed record Choice(Guid Id, string Name);
    private sealed record FieldChoice(BrowserFilterField Field, string Name);
    private sealed record ComparisonChoice(BrowserNumberComparison Value, string Name);
    internal SmartCollectionDialog(IBrowserLocationResolver locations, IReadOnlyList<MediaRootInfo> roots,
        IReadOnlyList<CollectionSetPlacementOption> sets, IReadOnlyList<(Guid Id, string Name)> collections,
        string name, Guid? parent, SmartCollectionSource? source, BrowserQueryIntent query, bool editing)
    {
        _locations = locations;
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
        SearchText.Text = query.SearchText; MatchCombo.SelectedIndex = (int)query.MatchMode;
        foreach (var rule in query.Filters) _rules.Add(rule);
        RulesList.ItemsSource = _rules;
        ComparisonCombo.DisplayMemberPath = "Name";
        ComparisonCombo.ItemsSource = Enum.GetValues<BrowserNumberComparison>().Select(value => new ComparisonChoice(value, BrowserFilterPredicate.ComparisonSymbol(value))).ToArray();
        ComparisonCombo.SelectedIndex = 0;
        FieldCombo.ItemsSource = Enum.GetValues<BrowserFilterField>().Select(f => new FieldChoice(f, BrowserPredicateEditor.FieldName(f))).ToArray();
        FieldCombo.SelectedIndex = 0;
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
    internal BrowserQueryIntent Query => new() { SearchText = SearchText.Text, MatchMode = (BrowserMatchMode)MatchCombo.SelectedIndex, Filters = _rules.ToArray() };
    private void SourceKindChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FolderPanel is null) return;
        FolderPanel.Visibility = SourceKindCombo.SelectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
        SourceCollectionCombo.Visibility = SourceKindCombo.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
    }
    private async void ChooseFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Choose Smart Collection Source" };
        if (dialog.ShowDialog(this) != true) return;
        var result = await _locations.ResolveAsync(dialog.FolderName);
        if (!result.Succeeded) { ErrorText.Text = result.Diagnostic ?? "This folder could not be used."; return; }
        _folderSource = new(SmartCollectionSourceKind.Folder, result.RootId, result.RelativeFolder);
        FolderText.Text = dialog.FolderName; ErrorText.Text = "";
    }
    private void FieldChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FieldCombo.SelectedItem is not FieldChoice field) return;
        ValueHint.Text = BrowserPredicateEditor.Hint(field.Field);
        ComparisonCombo.IsEnabled = field.Field == BrowserFilterField.Rating;
    }
    private void AddRule_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (FieldCombo.SelectedItem is not FieldChoice field) return;
            var rule = BrowserPredicateEditor.Create(field.Field, ValueText.Text, ((ComparisonChoice)ComparisonCombo.SelectedItem).Value);
            if (!_rules.Contains(rule)) _rules.Add(rule);
            ValueText.Clear(); ErrorText.Text = "";
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or OverflowException) { ErrorText.Text = ex.Message; }
    }
    private void RemoveRule_Click(object sender, RoutedEventArgs e)
    { if (RulesList.SelectedItem is BrowserFilterPredicate rule) _rules.Remove(rule); }
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (CollectionName.Length == 0) { ErrorText.Text = "Enter a name."; NameText.Focus(); return; }
        if (Source is null) { ErrorText.Text = "Choose a Folder or static Collection Source."; return; }
        DialogResult = true;
    }
}
