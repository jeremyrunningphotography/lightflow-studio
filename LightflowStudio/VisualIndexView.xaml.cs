using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace LightflowStudio;

public partial class VisualIndexView : System.Windows.Controls.UserControl
{
    public static readonly DependencyProperty FrameWidthProperty = DependencyProperty.Register(nameof(FrameWidth), typeof(double), typeof(VisualIndexView), new PropertyMetadata(160d));
    public static readonly DependencyProperty FrameHeightProperty = DependencyProperty.Register(nameof(FrameHeight), typeof(double), typeof(VisualIndexView), new PropertyMetadata(90d));
    public double FrameWidth { get => (double)GetValue(FrameWidthProperty); private set => SetValue(FrameWidthProperty, value); }
    public double FrameHeight { get => (double)GetValue(FrameHeightProperty); private set => SetValue(FrameHeightProperty, value); }
    internal event EventHandler? DensityChanged;
    internal Func<VisualIndexCard, Task>? Seek;
    internal int Count => Density.SelectedItem is int count ? count : 24;
    public VisualIndexView()
    {
        InitializeComponent();
        Density.ItemsSource = VisualIndexSampling.Counts;
        Density.SelectedItem = 24;
    }
    internal void Initialize(VisualIndexModel model, int count)
    {
        Density.SelectedItem = VisualIndexSampling.NormalizeCount(count);
        model.ProgressChanged += (_, _) => GenerationStatus.Text = model.Status;
        model.Changed += (_, _) =>
        {
            Frames.ItemsSource = model.Cards;
            EmptyState.Visibility = model.Cards.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        };
    }
    private void Density_SelectionChanged(object sender, SelectionChangedEventArgs e) => DensityChanged?.Invoke(this, EventArgs.Empty);
    private async void Frame_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: VisualIndexCard card } && Seek is not null) await Seek(card);
    }
    private void Grid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateGrid(Math.Max(0, e.NewSize.Width - 18));
    }
    private void Frames_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateGrid(Frames.ActualWidth);
    }
    private void UpdateGrid(double width)
    {
        var columns = VisualIndexSampling.Columns(width);
        FrameWidth = Math.Max(80, width / columns - 18);
        FrameHeight = FrameWidth * 9 / 16;
        if (FindGrid(Frames) is { } grid) grid.Columns = columns;
    }
    private static UniformGrid? FindGrid(DependencyObject parent)
    {
        if (parent is UniformGrid grid) return grid;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            if (FindGrid(VisualTreeHelper.GetChild(parent, i)) is { } child) return child;
        return null;
    }
}
