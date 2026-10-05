using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Lightflow.G3;

public sealed class ShortcutWindow : Window
{
    private Button? _recording;
    public ShortcutWindow()
    {
        Title = "Settings • Keyboard Shortcuts"; Width = 960; Height = 700;
        var rows = new StackPanel { Spacing = 8, Margin = new Thickness(14) };
        rows.Children.Add(new TextBlock { Text = "Keyboard Shortcuts", FontSize = 20 });
        rows.Children.Add(new TextBlock { Text = "Proof capture only; changes remain staged." });
        foreach (var category in new[] { "Browser", "Player", "Presentation", "Workflow" })
        {
            var commands = new StackPanel { Spacing = 6 };
            for (var i = 0; i < 20; i++)
            {
                var edit = new Button { Content = "Edit" };
                var feedback = new TextBlock { Text = "Unassigned", VerticalAlignment = VerticalAlignment.Center };
                var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12,
                    Children = { new TextBlock { Text = $"{category} representative command {i + 1}", Width = 310,
                        VerticalAlignment = VerticalAlignment.Center }, feedback, edit } };
                edit.Click += (_, _) => { _recording = edit; edit.Content = "Press shortcut…"; edit.Focus(); };
                edit.KeyDown += (_, e) =>
                {
                    if (_recording != edit) return;
                    if (e.Key is Key.Escape or Key.Tab) { edit.Content = "Edit"; _recording = null; e.Handled = e.Key == Key.Escape; return; }
                    if (e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin) return;
                    feedback.Text = $"{e.KeyModifiers}+{e.Key} • Unsaved"; edit.Content = "Edit"; _recording = null; e.Handled = true;
                };
                commands.Children.Add(line);
            }
            rows.Children.Add(new Expander { Header = $"{category} • 20 commands", Content = commands,
                BorderBrush = Brush.Parse("#FF9A66"), BorderThickness = new Thickness(1), IsExpanded = false });
        }
        var layout = new DockPanel();
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8, Margin = new Thickness(14), Children = { new Button { Content = "Restore Defaults", IsEnabled = false },
                new Button { Content = "Save Settings", IsEnabled = false } } };
        DockPanel.SetDock(footer, Dock.Bottom); layout.Children.Add(footer);
        layout.Children.Add(new ScrollViewer { Content = rows }); Content = layout;
        Deactivated += (_, _) => { if (_recording is { } edit) edit.Content = "Edit"; _recording = null; };
    }
}
