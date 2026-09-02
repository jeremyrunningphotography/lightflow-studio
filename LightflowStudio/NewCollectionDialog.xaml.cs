using System.Windows;

namespace LightflowStudio;

public partial class NewCollectionDialog : Window
{
    internal NewCollectionDialog(IReadOnlyList<CollectionSetPlacementOption> sets, Guid? suggestedParent)
    {
        InitializeComponent();
        SetCombo.ItemsSource = sets;
        var suggested = sets.FirstOrDefault(item => item.CollectionSetId == suggestedParent);
        WithinSetCheck.IsChecked = suggested is not null;
        SetCombo.SelectedItem = suggested ?? sets.FirstOrDefault();
        SetCombo.IsEnabled = WithinSetCheck.IsChecked == true && sets.Count > 0;
        Loaded += (_, _) => NameText.Focus();
        SourceInitialized += (_, _) => WindowAppearance.EnableDarkTitleBar(this);
    }

    public string CollectionName => NameText.Text.Trim();
    public Guid? ParentSetId => WithinSetCheck.IsChecked == true && SetCombo.SelectedItem is CollectionSetPlacementOption option
        ? option.CollectionSetId : null;
    private void PlacementChanged(object sender, RoutedEventArgs e) =>
        SetCombo.IsEnabled = WithinSetCheck.IsChecked == true && SetCombo.Items.Count > 0;
    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
    private void Create_Click(object sender, RoutedEventArgs e)
    {
        if (CollectionName.Length == 0 || (WithinSetCheck.IsChecked == true && SetCombo.SelectedItem is null)) return;
        DialogResult = true;
        Close();
    }
}
