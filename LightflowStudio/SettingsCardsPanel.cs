using System.Windows;
using System.Windows.Controls;
using Panel = System.Windows.Controls.Panel;
using Size = System.Windows.Size;

namespace LightflowStudio;

/// <summary>Settings peer cards reflow in DIPs without rebuilding controls or losing focus.</summary>
public sealed class SettingsCardsPanel : Panel
{
    internal const double Gap = 16;
    internal const double MinimumColumnWidth = 440;
    internal static int ColumnsFor(double width) => width >= MinimumColumnWidth * 2 + Gap ? 2 : 1;

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? MinimumColumnWidth : availableSize.Width;
        var columns = ColumnsFor(width);
        var cardWidth = Math.Max(0, (width - Gap * (columns - 1)) / columns);
        double height = 0;
        for (var index = 0; index < InternalChildren.Count; index += columns)
        {
            double rowHeight = 0;
            for (var column = 0; column < columns && index + column < InternalChildren.Count; column++)
            {
                var child = InternalChildren[index + column];
                child.Measure(new Size(cardWidth, double.PositiveInfinity));
                rowHeight = Math.Max(rowHeight, child.DesiredSize.Height);
            }
            height += rowHeight + (index == 0 ? 0 : Gap);
        }
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var columns = ColumnsFor(finalSize.Width);
        var cardWidth = Math.Max(0, (finalSize.Width - Gap * (columns - 1)) / columns);
        double top = 0;
        for (var index = 0; index < InternalChildren.Count; index += columns)
        {
            double rowHeight = 0;
            for (var column = 0; column < columns && index + column < InternalChildren.Count; column++)
                rowHeight = Math.Max(rowHeight, InternalChildren[index + column].DesiredSize.Height);
            for (var column = 0; column < columns && index + column < InternalChildren.Count; column++)
                InternalChildren[index + column].Arrange(new Rect(column * (cardWidth + Gap), top, cardWidth, rowHeight));
            top += rowHeight + Gap;
        }
        return finalSize;
    }
}
