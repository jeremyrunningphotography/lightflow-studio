using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;

namespace LightflowStudio;

internal sealed class AddToCollectionChoice(Guid id, string displayName) : INotifyPropertyChanged
{
    private bool _isSelected;
    public Guid CollectionId { get; } = id;
    public string DisplayName { get; } = displayName;
    public bool IsSelected { get => _isSelected; set { if (_isSelected == value) return; _isSelected = value; PropertyChanged?.Invoke(this, new(nameof(IsSelected))); } }
    public event PropertyChangedEventHandler? PropertyChanged;
}

public partial class AddToCollectionDialog : Window
{
    private readonly Func<Task<MediaCollection?>> _createCollection;
    private readonly List<AddToCollectionChoice> _choices;

    internal AddToCollectionDialog(int assetCount, IEnumerable<(Guid Id, string DisplayName)> collections,
        Func<Task<MediaCollection?>> createCollection)
    {
        InitializeComponent();
        _createCollection = createCollection;
        _choices = collections.Select(item => new AddToCollectionChoice(item.Id, item.DisplayName)).ToList();
        CollectionChoices.ItemsSource = _choices;
        SelectionSummary.Text = assetCount == 1 ? "Add 1 selected media item to one or more Collections." : $"Add {assetCount} selected media items to one or more Collections.";
        SourceInitialized += (_, _) => WindowAppearance.EnableDarkTitleBar(this);
    }

    public IReadOnlyList<Guid> SelectedCollectionIds => _choices.Where(choice => choice.IsSelected).Select(choice => choice.CollectionId).ToArray();

    private async void NewCollection_Click(object sender, RoutedEventArgs e)
    {
        var created = await _createCollection();
        if (created is null) return;
        var choice = new AddToCollectionChoice(created.CollectionId, created.Name) { IsSelected = true };
        _choices.Add(choice);
        CollectionChoices.Items.Refresh();
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedCollectionIds.Count == 0) return;
        DialogResult = true;
        Close();
    }
}
