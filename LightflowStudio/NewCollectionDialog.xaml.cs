using System.Windows;

namespace LightflowStudio;

public partial class NewCollectionDialog : Window
{
    internal NewCollectionDialog(IReadOnlyList<CollectionSetPlacementOption> sets, Guid? suggestedParent,
        bool createSet = false)
    {
        InitializeComponent();
        DialogTitle.Text = createSet ? "New Collection Set" : "New Collection";
        NameText.SetValue(System.Windows.Automation.AutomationProperties.NameProperty,
            createSet ? "Collection Set name" : "Collection name");
        SetCombo.ItemsSource = sets;
        var suggested = sets.FirstOrDefault(item => item.CollectionSetId == suggestedParent);
        SetCombo.SelectedItem = suggested ?? sets.First();
        WithinSetCheck.IsChecked = suggestedParent is not null;
        SetCombo.IsEnabled = WithinSetCheck.IsChecked == true;
        Loaded += (_, _) => NameText.Focus();
        SourceInitialized += (_, _) => WindowAppearance.EnableDarkTitleBar(this);
    }

    public string CollectionName => NameText.Text.Trim();
    public Guid? ParentSetId => WithinSetCheck.IsChecked == true
        ? (SetCombo.SelectedItem as CollectionSetPlacementOption)?.CollectionSetId : null;
    private void PlacementChanged(object sender, RoutedEventArgs e) =>
        SetCombo.IsEnabled = WithinSetCheck.IsChecked == true;
    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
    private void Create_Click(object sender, RoutedEventArgs e)
    {
        if (CollectionName.Length == 0 || SetCombo.SelectedItem is null) return;
        DialogResult = true;
        Close();
    }
}
