using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Automation;
using Button = System.Windows.Controls.Button;
using Brushes = System.Windows.Media.Brushes;

namespace LightflowStudio;

internal sealed class PlayerFullscreenOverlay : Grid
{
    internal const string Outward = "M8,8 L2,2 M2,7 L2,2 L7,2 M16,8 L22,2 M17,2 L22,2 L22,7 M8,16 L2,22 M2,17 L2,22 L7,22 M16,16 L22,22 M17,22 L22,22 L22,17";
    internal const string Inward = "M2,2 L8,8 M3,8 L8,8 L8,3 M22,2 L16,8 M16,3 L16,8 L21,8 M2,22 L8,16 M3,16 L8,16 L8,21 M22,22 L16,16 M16,21 L16,16 L21,16";
    internal Button ExitButton { get; }
    internal TextBlock Hint { get; }
    internal System.Windows.Shapes.Path Feedback { get; }
    internal PlayerFullscreenOverlay(Action exit)
    {
        Background = Brushes.Transparent;
        ExitButton = new Button { Width = 36, Height = 36, Margin = new(0, 16, 16, 0),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
            Padding = new(7), Content = Icon(Inward), ToolTip = "Exit Full Screen (Esc)", Visibility = Visibility.Collapsed };
        AutomationProperties.SetName(ExitButton, "Exit Full Screen");
        ExitButton.Click += (_, e) => { e.Handled = true; exit(); };
        Hint = new TextBlock { Text = "Press Esc to exit full screen", FontSize = 18, Foreground = Brushes.White,
            Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(170, 0, 0, 0)), Padding = new(16, 8, 16, 8),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top,
            Margin = new(0, 32, 0, 0), IsHitTestVisible = false, Visibility = Visibility.Collapsed };
        Feedback = new System.Windows.Shapes.Path { Width = 80, Height = 80, Stretch = Stretch.Uniform,
            Fill = Brushes.White, HorizontalAlignment = System.Windows.HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false, Visibility = Visibility.Collapsed };
        Children.Add(Hint); Children.Add(Feedback); Children.Add(ExitButton);
    }
    internal static System.Windows.Shapes.Path Icon(string data)
    {
        var icon = new System.Windows.Shapes.Path { Data = Geometry.Parse(data),
            StrokeThickness = 1.6, Stretch = Stretch.Uniform, Width = 20, Height = 20 };
        icon.SetBinding(System.Windows.Shapes.Shape.StrokeProperty, new System.Windows.Data.Binding("Foreground")
        { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor, typeof(Button), 1) });
        return icon;
    }
    internal void Enter(bool firstEntry) { if (firstEntry) Show(Hint, 2.5); }
    internal void PointerMoved() => Show(ExitButton, 2);
    internal void ShowPlayback(bool playing)
    {
        Feedback.Data = Geometry.Parse(playing ? "M0,0 L0,80 L65,40 Z" : "M0,0 H25 V80 H0 Z M45,0 H70 V80 H45 Z");
        Show(Feedback, 0.45, 0.65);
    }
    private static void Show(UIElement element, double hold, double opacity = 1)
    {
        element.Visibility = Visibility.Visible;
        element.Opacity = opacity;
        var animation = new DoubleAnimation(opacity, 0, TimeSpan.FromSeconds(0.4)) { BeginTime = TimeSpan.FromSeconds(hold) };
        animation.Completed += (_, _) => element.Visibility = Visibility.Collapsed;
        element.BeginAnimation(OpacityProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }
    internal void Reset()
    {
        foreach (UIElement child in Children) { child.BeginAnimation(OpacityProperty, null); child.Visibility = Visibility.Collapsed; }
    }
}

