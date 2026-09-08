using System.Windows;
using System.Windows.Controls;

namespace LightflowStudio;

/// <summary>Reusable Home content host. Surfaces supply stable keys and live controls; no media or Jobs ownership.</summary>
public partial class ContextualRightPanel : System.Windows.Controls.UserControl
{
    public ContextualRightPanel() => InitializeComponent();
    internal event EventHandler? ActiveSurfaceChanged;
    internal string ActiveSurface => (SurfaceTabs.SelectedItem as TabItem)?.Tag as string ?? "inspector";
    internal void AddSurface(string key, string title, FrameworkElement content) =>
        SurfaceTabs.Items.Add(new TabItem { Tag = key, Header = title, Content = content });
    internal void SelectSurface(string? key) => SurfaceTabs.SelectedItem =
        SurfaceTabs.Items.Cast<TabItem>().FirstOrDefault(t => Equals(t.Tag, key)) ?? SurfaceTabs.Items.Cast<TabItem>().FirstOrDefault();
    private void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ReferenceEquals(e.Source, SurfaceTabs)) ActiveSurfaceChanged?.Invoke(this, EventArgs.Empty);
    }
}
