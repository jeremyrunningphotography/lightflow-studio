using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace LightflowStudio;

public partial class CatalogDestinationDialog : Window
{
    private readonly bool _move;
    internal CatalogDestinationDialog(string proposal, bool move)
    {
        InitializeComponent();
        _move = move;
        Title = Heading.Text = move ? "Move Catalog" : "Create New Catalog";
        ActionButton.Content = Title;
        Description.Text = move
            ? "Move the current Catalog to the exact location below. Lightflow will copy and validate it before switching. The original is retained as a recovery source."
            : "Create an independent Catalog at the exact location below. Your previous Catalog and its files will be preserved.";
        Destination.Text = proposal;
        LocationHint.Text = move
            ? "Enter a new directory within an existing folder. The location above is the exact resulting Catalog directory; no child folder will be added."
            : "Select a safely empty folder or enter a new folder path. The location above is the exact resulting Catalog directory; no child folder will be added.";
        SourceInitialized += (_, _) => WindowAppearance.EnableDarkTitleBar(this);
    }
    internal string? SelectedDirectory { get; private set; }
    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFolderDialog { Title = "Select the exact resulting Catalog directory" };
        if (Directory.Exists(Destination.Text)) picker.InitialDirectory = Destination.Text;
        if (picker.ShowDialog(this) == true) Destination.Text = picker.FolderName;
    }
    internal bool TryAccept()
    {
        try
        {
            var path = Destination.Text.Trim();
            if (!Path.IsPathFullyQualified(path)) throw new ArgumentException("Enter a complete directory path or use Browse.");
            SelectedDirectory = Path.GetFullPath(path);
            if (_move && (!Directory.Exists(Path.GetDirectoryName(SelectedDirectory)) || Directory.Exists(SelectedDirectory) || File.Exists(SelectedDirectory)))
                throw new ArgumentException("Move Catalog needs a new directory within an existing folder. Enter the exact new directory path.");
            return true;
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
        { ErrorText.Text = error.Message; return false; }
    }
    private void Accept_Click(object sender, RoutedEventArgs e)
    { if (TryAccept()) DialogResult = true; }
    internal static string? Choose(Window owner, string proposal, bool move)
    {
        var dialog = new CatalogDestinationDialog(proposal, move) { Owner = owner };
        return dialog.ShowDialog() == true ? dialog.SelectedDirectory : null;
    }
}
