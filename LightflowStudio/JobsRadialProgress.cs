using System.Windows;
using System.Windows.Media;
using Color = System.Windows.Media.Color;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace LightflowStudio;

public sealed class JobsRadialProgress : FrameworkElement
{
    public static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(nameof(Progress), typeof(double),
        typeof(JobsRadialProgress), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty StateProperty = DependencyProperty.Register(nameof(State), typeof(string),
        typeof(JobsRadialProgress), new FrameworkPropertyMetadata("Waiting", FrameworkPropertyMetadataOptions.AffectsRender));

    public double Progress { get => (double)GetValue(ProgressProperty); set => SetValue(ProgressProperty, value); }
    public string State { get => (string)GetValue(StateProperty); set => SetValue(StateProperty, value); }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var center = new Point(RenderSize.Width / 2, RenderSize.Height / 2);
        var radius = Math.Max(1, Math.Min(RenderSize.Width, RenderSize.Height) / 2 - 2);
        dc.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromRgb(91, 98, 108)), 2), center, radius, radius);
        if (State == "Exporting" && Progress > 0)
        {
            var sweep = Math.Clamp(Progress, 0, 100) * 3.6;
            if (sweep >= 359.99) sweep = 359.99;
            var start = new Point(center.X, center.Y - radius);
            var radians = (sweep - 90) * Math.PI / 180;
            var end = new Point(center.X + radius * Math.Cos(radians), center.Y + radius * Math.Sin(radians));
            var figure = new PathFigure(start, [new ArcSegment(end, new Size(radius, radius), 0, sweep > 180,
                SweepDirection.Clockwise, true)], false);
            dc.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromRgb(255, 139, 31)), 3)
            { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, new PathGeometry([figure]));
        }
        var mark = State switch { "Completed" or "Completed with warnings" => "✓", "Paused" => "Ⅱ", "Failed" or "Needs attention" => "!", "Cancelled" => "×", _ => "" };
        if (mark.Length == 0) return;
        var brush = new SolidColorBrush(State.StartsWith("Completed", StringComparison.Ordinal) ? Color.FromRgb(69, 191, 120) : Color.FromRgb(235, 235, 238));
        var text = new FormattedText(mark, System.Globalization.CultureInfo.CurrentCulture, System.Windows.FlowDirection.LeftToRight,
            new Typeface("Segoe UI Semibold"), 11, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(text, new Point(center.X - text.Width / 2, center.Y - text.Height / 2));
    }
}
