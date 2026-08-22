using System.Windows;
using System.Windows.Media;

namespace LightflowStudio;

internal sealed class TrimRangeIndicator : FrameworkElement
{
    public static readonly DependencyProperty HasActiveTrimProperty = DependencyProperty.Register(
        nameof(HasActiveTrim), typeof(bool), typeof(TrimRangeIndicator), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty HasProportionsProperty = DependencyProperty.Register(
        nameof(HasProportions), typeof(bool), typeof(TrimRangeIndicator), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty StartFractionProperty = DependencyProperty.Register(
        nameof(StartFraction), typeof(double), typeof(TrimRangeIndicator), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty WidthFractionProperty = DependencyProperty.Register(
        nameof(WidthFraction), typeof(double), typeof(TrimRangeIndicator), new FrameworkPropertyMetadata(1d, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ShowBoundariesProperty = DependencyProperty.Register(
        nameof(ShowBoundaries), typeof(bool), typeof(TrimRangeIndicator), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public bool HasActiveTrim { get => (bool)GetValue(HasActiveTrimProperty); set => SetValue(HasActiveTrimProperty, value); }
    public bool HasProportions { get => (bool)GetValue(HasProportionsProperty); set => SetValue(HasProportionsProperty, value); }
    public double StartFraction { get => (double)GetValue(StartFractionProperty); set => SetValue(StartFractionProperty, value); }
    public double WidthFraction { get => (double)GetValue(WidthFractionProperty); set => SetValue(WidthFractionProperty, value); }
    public bool ShowBoundaries { get => (bool)GetValue(ShowBoundariesProperty); set => SetValue(ShowBoundariesProperty, value); }

    protected override System.Windows.Size MeasureOverride(System.Windows.Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 120 : availableSize.Width, 7);

    protected override void OnRender(DrawingContext drawingContext)
    {
        var width = Math.Max(0, ActualWidth);
        var center = ActualHeight / 2;
        var neutral = new System.Windows.Media.Pen(
            new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(64, 70, 82)), 2);
        neutral.Freeze();
        drawingContext.DrawLine(neutral, new System.Windows.Point(0, center), new System.Windows.Point(width, center));
        if (!HasActiveTrim || !HasProportions) return;
        var start = Math.Clamp(StartFraction, 0, 1) * width;
        var end = Math.Clamp(StartFraction + WidthFraction, 0, 1) * width;
        var active = new System.Windows.Media.Pen(
            (System.Windows.Media.Brush)System.Windows.Application.Current.FindResource("OrangeBrush"), 4)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };
        drawingContext.DrawLine(active, new System.Windows.Point(start, center), new System.Windows.Point(end, center));
        if (ShowBoundaries)
        {
            drawingContext.DrawLine(active, new System.Windows.Point(start, 0), new System.Windows.Point(start, ActualHeight));
            drawingContext.DrawLine(active, new System.Windows.Point(end, 0), new System.Windows.Point(end, ActualHeight));
        }
    }
}
