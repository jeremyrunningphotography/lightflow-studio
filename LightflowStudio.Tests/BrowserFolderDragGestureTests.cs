using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Xunit;

namespace LightflowStudio.Tests;

[Collection("STA dispatcher tests")]
public sealed class BrowserFolderDragGestureTests
{
    [Fact]
    public Task RowHitTestingIncludesHighlightPaddingButRejectsWhitespaceAndExpander() => StaDispatcher.RunAsync(() =>
    {
        TestWpfApplication.EnsureLoaded();
        var node = new BrowserTreeNode("Source", @"C:\Source") { IsSelected = true };
        var label = new TextBlock { Text = "Source" };
        var item = new TreeViewItem
        {
            DataContext = node, Header = label, IsSelected = true,
            Style = (Style)Application.Current.FindResource("BrowserTreeItemStyle")
        };
        var tree = new TreeView();
        tree.Items.Add(item);
        tree.Measure(new Size(400, 400));
        tree.Arrange(new Rect(0, 0, 400, 400));
        tree.UpdateLayout();

        Assert.Same(node, BrowserFolderDragGesture.HeaderNode(label));
        Assert.Same(node, BrowserFolderDragGesture.HeaderNode((DependencyObject)item.Template.FindName("HeaderChrome", item)));
        var gesture = new BrowserFolderDragGesture();
        var nonItems = new DependencyObject[]
        {
            tree, item,
            (DependencyObject)item.Template.FindName("Expander", item),
            (DependencyObject)item.Template.FindName("ItemsHost", item),
            new Border { DataContext = node }
        };
        foreach (var nonItem in nonItems)
        {
            for (var repeat = 0; repeat < 3; repeat++)
            {
                gesture.Begin(node, new Point()); // previous selection/aborted press
                var hit = BrowserFolderDragGesture.HeaderNode(nonItem);
                Assert.Null(hit);
                gesture.Begin(hit, new Point());
                Assert.Null(gesture.Take(new Point(100, 100), true));
            }
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task WhitespacePressesAreHandledBeforeItemSelectionAndBringIntoView() => StaDispatcher.RunAsync(() =>
    {
        TestWpfApplication.EnsureLoaded();
        var root = new TreeViewItem
        {
            Header = new TextBlock { Text = "Drive" }, DataContext = new BrowserTreeNode("Drive", @"C:\"),
            Style = (Style)Application.Current.FindResource("BrowserTreeItemStyle"), IsExpanded = true
        };
        var selected = new TreeViewItem
        {
            Header = new TextBlock { Text = "Selected folder" },
            DataContext = new BrowserTreeNode("Selected folder", @"C:\Selected"), Style = root.Style
        };
        root.Items.Add(selected);
        var tree = new TreeView();
        tree.Items.Add(root);
        tree.Measure(new Size(400, 400));
        tree.Arrange(new Rect(0, 0, 400, 400));
        tree.UpdateLayout();
        selected.IsSelected = true;
        var bringIntoView = 0;
        tree.RequestBringIntoView += (_, _) => bringIntoView++;
        tree.AddHandler(Mouse.PreviewMouseDownEvent,
            new MouseButtonEventHandler((_, e) => BrowserFolderDragGesture.GuardSelectionPress(e)));
        var surfaces = new UIElement[]
        {
            tree, root, (UIElement)VisualTreeHelper.GetChild(root, 0),
            (UIElement)root.Template.FindName("ItemsHost", root)
        };
        foreach (var surface in surfaces)
        {
            for (var repeat = 0; repeat < 3; repeat++)
            {
                var press = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                    { RoutedEvent = Mouse.PreviewMouseDownEvent };
                surface.RaiseEvent(press);
                Assert.True(press.Handled);
                press.RoutedEvent = Mouse.MouseDownEvent;
                surface.RaiseEvent(press);
                Assert.Same(selected, tree.SelectedItem);
                Assert.Equal(0, bringIntoView);
            }
        }

        foreach (var eligible in new[] { "HeaderChrome", "Expander" })
        {
            var press = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                { RoutedEvent = Mouse.PreviewMouseDownEvent };
            ((UIElement)root.Template.FindName(eligible, root)).RaiseEvent(press);
            Assert.False(press.Handled); // Row selection and disclosure still reach their normal WPF handlers.
        }
        return Task.CompletedTask;
    });

    [Fact]
    public void OrdinarySelectionAndSubthresholdPressCannotStartDrag()
    {
        var gesture = new BrowserFolderDragGesture();
        var first = new BrowserTreeNode("First", @"C:\First") { IsSelected = true };
        var second = new BrowserTreeNode("Second", @"C:\Second");
        foreach (var node in new[] { first, second })
        {
            gesture.Begin(node, new Point(10, 10));
            Assert.Null(gesture.Take(new Point(10, 10), true));
            Assert.Null(gesture.Take(new Point(11, 11), true));
            Assert.Null(gesture.Take(new Point(100, 100), false));
            Assert.Null(gesture.Take(new Point(100, 100), true));
        }
    }

    [Fact]
    public void InterruptionDisarmsAndInvalidatesTakenGesture()
    {
        var gesture = new BrowserFolderDragGesture();
        var node = new BrowserTreeNode("Source", @"C:\Source");
        gesture.Begin(node, new Point());
        gesture.Reset();
        Assert.Null(gesture.Take(new Point(100, 100), true));
        gesture.Begin(node, new Point());
        Assert.Same(node, gesture.Take(new Point(100, 100), true));
        var generation = gesture.Generation;
        Assert.False(gesture.IsCurrent(generation, false));
        gesture.Reset();
        Assert.False(gesture.IsCurrent(generation, true));
        Assert.Null(gesture.Take(new Point(100, 100), true));
    }

    [Theory]
    [InlineData(@"C:\Destination", false, false, "Move")]
    [InlineData(@"D:\Destination", false, false, "Copy")]
    [InlineData(@"C:\Destination", true, false, "Copy")]
    [InlineData(@"D:\Destination", false, true, "Move")]
    public void ValidGestureConsumesExactSourceAndPreservesOperationSemantics(string destination,
        bool control, bool shift, string expected)
    {
        var node = new BrowserTreeNode("Source", @"C:\Source");
        var gesture = new BrowserFolderDragGesture();
        gesture.Begin(node, new Point());
        var source = gesture.Take(new Point(SystemParameters.MinimumHorizontalDragDistance, 0), true);
        Assert.Same(node, source);
        Assert.True(gesture.IsCurrent(gesture.Generation, true));
        Assert.Null(gesture.Take(new Point(100, 100), true));
        var kind = FileOperationPathSemantics.DragKind(source!.AbsolutePath!, destination, control, shift);
        Assert.Equal(expected, kind.ToString());
    }

    [Fact]
    public void IntentionalFolderDragCanPlanAValidMove()
    {
        var root = Path.Combine(Path.GetTempPath(), $"lightflow-folder-gesture-{Guid.NewGuid():N}");
        var sourcePath = Path.Combine(root, "Source");
        var destination = Path.Combine(root, "Destination");
        Directory.CreateDirectory(sourcePath);
        Directory.CreateDirectory(destination);
        try
        {
            var gesture = new BrowserFolderDragGesture();
            gesture.Begin(new BrowserTreeNode("Source", sourcePath), new Point());
            var source = gesture.Take(new Point(100, 100), true);
            Assert.NotNull(source);
            var kind = FileOperationPathSemantics.DragKind(source.AbsolutePath!, destination, false, false);
            Assert.Equal(FileOperationKind.Move, kind);
            Assert.NotNull(FileOperationPlanner.Plan(kind,
                [new FileOperationSource(null, source.AbsolutePath!, null, true)], destination));
        }
        finally { Directory.Delete(root, true); }
    }
}
