using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace LightflowStudio;

public partial class CatalogDestinationDialog : Window
{
    private readonly bool _move;
    internal CatalogDestinationDialog(string proposal, bool move, bool fromRecovery = false)
    {
        InitializeComponent();
        _move = move;
        Title = Heading.Text = move ? "Move Catalog" : "Create New Catalog";
        ActionButton.Content = Title;
        Description.Text = move
            ? "Choose where to store your Catalog. Lightflow will copy and validate it before switching. The previous Catalog is retained for recovery; new edits won't update that copy."
            : fromRecovery ? "Create a new Catalog to continue."
            : "Create a new Catalog at the location below.";
        LocationLabel.Text = move ? "Destination parent folder" : "Lightflow Catalog Location";
        MoveChildFields.Visibility = move ? Visibility.Visible : Visibility.Collapsed;
        ChildName.Text = "Lightflow Catalog";
        Destination.Text = proposal;
        LocationHint.Text = move
            ? "Choose a parent folder and a new Catalog folder name. The resulting folder must not already exist."
            : "Choose an empty folder or enter a new folder location for your Catalog.";
        UpdateFinalLocation();
        SourceInitialized += (_, _) => WindowAppearance.EnableDarkTitleBar(this);
    }
    internal string? SelectedDirectory { get; private set; }
    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFolderDialog { Title = _move ? "Choose the destination parent folder" : "Choose the new Catalog folder" };
        if (Directory.Exists(Destination.Text)) picker.InitialDirectory = Destination.Text;
        if (picker.ShowDialog(this) == true) SelectFolder(picker.FolderName);
    }
    internal void SelectFolder(string folder) => Destination.Text = folder;
    private void Location_Changed(object sender, System.Windows.Controls.TextChangedEventArgs e) => UpdateFinalLocation();
    private string ResultingPath()
    {
        var parent = Destination.Text.Trim();
        if (!Path.IsPathFullyQualified(parent)) throw new ArgumentException("Enter a complete folder path or use Browse.");
        if (!_move) return Path.GetFullPath(parent);
        var name = ChildName.Text.Trim();
        if (string.IsNullOrEmpty(name) || name is "." or ".." || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.EndsWith('.') || name.EndsWith(' '))
            throw new ArgumentException("Enter a single new Catalog folder name.");
        return Path.GetFullPath(Path.Combine(parent, name));
    }
    private void UpdateFinalLocation()
    {
        if (FinalLocation is null || ChildName is null || Destination is null) return;
        try { FinalLocation.Text = ResultingPath(); }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
        { FinalLocation.Text = "Choose a parent folder and enter a new Catalog folder name."; }
        SelectedDirectory = null;
        if (ErrorText is not null) ErrorText.Text = "";
    }
    internal bool TryAccept()
    {
        try
        {
            SelectedDirectory = null;
            var path = ResultingPath();
            if (_move && !Directory.Exists(Path.GetDirectoryName(path)))
                throw new ArgumentException("Choose an existing destination parent folder.");
            if (_move && (Directory.Exists(path) || File.Exists(path)))
                throw new ArgumentException("The resulting Catalog folder already exists. Choose another name or parent folder.");
            SelectedDirectory = path;
            return true;
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
        { ErrorText.Text = error.Message; return false; }
    }
    private void Accept_Click(object sender, RoutedEventArgs e)
    { if (TryAccept()) DialogResult = true; }
    internal static string? Choose(Window owner, string proposal, bool move, bool fromRecovery = false)
    {
        var dialog = new CatalogDestinationDialog(proposal, move, fromRecovery) { Owner = owner };
        return dialog.ShowDialog() == true ? dialog.SelectedDirectory : null;
    }
}
