using System.Windows;

namespace LightflowStudio;

public partial class PreviewLocationDialog : Window
{
    private PreviewRelocationMode _choice;
    public PreviewLocationDialog()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => WindowAppearance.EnableDarkTitleBar(this);
    }
    private void Move_Click(object sender, RoutedEventArgs e)
    { _choice = PreviewRelocationMode.MoveExisting; DialogResult = true; }
    private void Rebuild_Click(object sender, RoutedEventArgs e)
    { _choice = PreviewRelocationMode.SwitchAndRebuild; DialogResult = true; }
    internal static PreviewRelocationMode? Choose(Window owner, string destination)
    {
        var dialog = new PreviewLocationDialog { Owner = owner };
        dialog.DestinationText.Text = destination;
        return dialog.ShowDialog() == true ? dialog._choice : null;
    }
}
