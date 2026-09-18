using System.Windows;
using System.Windows.Controls;

namespace LightflowStudio;

internal static class MarkerMenus
{
    public static string Label(TimelineMarker marker) => string.IsNullOrWhiteSpace(marker.Name) ? marker.PositionLabel : marker.Name;
    public static ContextMenu Empty(FrameworkElement owner) => new()
    { Style = (Style)owner.FindResource("LightflowContextMenuStyle") };
    public static MenuItem Item(FrameworkElement owner, string label, Action action, bool enabled = true)
    {
        var item = new MenuItem { Header = label, IsEnabled = enabled, Style = (Style)owner.FindResource("LightflowMenuItemStyle") };
        item.Click += (_, e) => { e.Handled = true; action(); };
        return item;
    }
    public static ContextMenu Create(FrameworkElement owner, Action rename, Func<Task> clear)
    {
        var menu = Empty(owner);
        menu.Items.Add(Item(owner, "Rename Marker", rename));
        menu.Items.Add(Item(owner, "Clear Marker", async () => await clear()));
        return menu;
    }
}
