using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Point = System.Windows.Point;
using TreeView = System.Windows.Controls.TreeView;

namespace LightflowStudio;

/// <summary>A folder drag belongs to one header press, never to the tree selection.</summary>
internal sealed class BrowserFolderDragGesture
{
    private BrowserTreeNode? _source;
    private Point _start;
    internal long Generation { get; private set; }

    internal void Reset()
    {
        _source = null;
        Generation++;
    }

    internal void Begin(BrowserTreeNode? source, Point point)
    {
        Reset();
        if (source is { IsPlaceholder: false, AbsolutePath: not null }) _source = source;
        _start = point;
    }

    internal BrowserTreeNode? Take(Point point, bool leftPressed)
    {
        if (!leftPressed) { Reset(); return null; }
        if (_source is null ||
            (Math.Abs(point.X - _start.X) < SystemParameters.MinimumHorizontalDragDistance &&
             Math.Abs(point.Y - _start.Y) < SystemParameters.MinimumVerticalDragDistance)) return null;
        var source = _source;
        _source = null;
        return source;
    }

    internal bool IsCurrent(long generation, bool leftPressed) => generation == Generation && leftPressed;

    // DataContext alone is insufficient: the transparent item grid, child indentation,
    // expander and header chrome all inherit the node without being folder content.
    internal static BrowserTreeNode? HeaderNode(DependencyObject? element)
    {
        while (element is not null)
        {
            if (element is ContentPresenter { Name: "PART_Header", TemplatedParent: TreeViewItem item } &&
                item.DataContext is BrowserTreeNode { IsPlaceholder: false, AbsolutePath: not null } node)
                return node;
            if (element is TreeViewItem or TreeView) return null;
            element = element is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element);
        }
        return null;
    }
}
