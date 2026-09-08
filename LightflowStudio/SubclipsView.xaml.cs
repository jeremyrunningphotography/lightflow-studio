using System.Windows;
using System.Windows.Input;

namespace LightflowStudio;

/// <summary>Retained Subclips presentation; Player owns commands, selection, and services.</summary>
public partial class SubclipsView : System.Windows.Controls.UserControl
{
    private readonly PlayerViewerHost _owner;
    internal SubclipsView(PlayerViewerHost owner)
    {
        _owner = owner;
        InitializeComponent();
    }

    private void SubclipsPanel_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) => _owner.SubclipsPanel_PreviewMouseLeftButtonDown(sender, e);
    private void AddSubclip_Click(object sender, RoutedEventArgs e) => _owner.AddSubclip_Click(sender, e);
    private void SubclipsList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) => _owner.SubclipsList_SelectionChanged(sender, e);
    private void SubclipsList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => _owner.SubclipsList_MouseDoubleClick(sender, e);
    private void SubclipName_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => _owner.SubclipName_LostKeyboardFocus(sender, e);
    private void SubclipName_KeyDown(object sender, System.Windows.Input.KeyEventArgs e) => _owner.SubclipName_KeyDown(sender, e);
    private void RenameSubclip_Click(object sender, RoutedEventArgs e) => _owner.RenameSubclip_Click(sender, e);
    private void DeleteSubclip_Click(object sender, RoutedEventArgs e) => _owner.DeleteSubclip_Click(sender, e);
    private void DeleteSelectedSubclips_Click(object sender, RoutedEventArgs e) => _owner.DeleteSelectedSubclips_Click(sender, e);
    private void ExportSubclips_Click(object sender, RoutedEventArgs e) => _owner.ExportSubclips_Click(sender, e);
    private void ExportSelectedSubclips_Click(object sender, RoutedEventArgs e) => _owner.ExportSelectedSubclips_Click(sender, e);
    private void ExportAllSubclips_Click(object sender, RoutedEventArgs e) => _owner.ExportAllSubclips_Click(sender, e);
}
