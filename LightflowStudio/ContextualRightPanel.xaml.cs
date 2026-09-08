using System.Windows;
using System.Windows.Controls;

namespace LightflowStudio;

/// <summary>Reusable Home content host. Surfaces supply stable keys and live controls; no media or Jobs ownership.</summary>
public partial class ContextualRightPanel : System.Windows.Controls.UserControl
{
    public ContextualRightPanel() => InitializeComponent();
    internal event EventHandler? ActiveSurfaceChanged;
    private bool _selectingFallback;
    internal string PreferredSurface { get; private set; } = "inspector";
    internal string ActiveSurface => (SurfaceTabs.SelectedItem as TabItem)?.Tag as string ?? "inspector";
    internal void AddSurface(string key, string title, FrameworkElement content, bool available = true)
    {
        var tab = new TabItem { Tag = key, Header = title, Content = content,
            Visibility = available ? Visibility.Visible : Visibility.Collapsed };
        System.Windows.Automation.AutomationProperties.SetName(tab, title);
        SurfaceTabs.Items.Add(tab);
        ApplyPreferredSurface();
    }
    internal void SelectSurface(string? key)
    {
        PreferredSurface = key ?? "inspector";
        ApplyPreferredSurface();
    }
    internal void SetSurfaceAvailable(string key, bool available)
    {
        var tab = SurfaceTabs.Items.Cast<TabItem>().FirstOrDefault(t => Equals(t.Tag, key));
        if (tab is null) return;
        tab.Visibility = available ? Visibility.Visible : Visibility.Collapsed;
        ApplyPreferredSurface();
    }
    private void ApplyPreferredSurface()
    {
        _selectingFallback = true;
        try
        {
            var available = SurfaceTabs.Items.Cast<TabItem>().Where(t => t.Visibility == Visibility.Visible);
            SurfaceTabs.SelectedItem = available.FirstOrDefault(t => Equals(t.Tag, PreferredSurface)) ?? available.FirstOrDefault();
        }
        finally { _selectingFallback = false; }
    }
    private void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.Source, SurfaceTabs) || _selectingFallback) return;
        PreferredSurface = ActiveSurface;
        ActiveSurfaceChanged?.Invoke(this, EventArgs.Empty);
    }
}
