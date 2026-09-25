using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml.Linq;
using Xunit;
using Xunit.Abstractions;

namespace LightflowStudio.Tests;

[Collection("STA dispatcher tests")]
public sealed class MenuPresentationTests(ITestOutputHelper output)
{
    [Fact]
    public async Task BrowserExportMenuUsesGearStylingAndNativeFocusableActionItems()
    {
        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();
            var menu = LoadMenu("BrowserExportMenu");
            Assert.Equal(PlacementMode.Bottom, menu.Placement);
            Assert.Same(Application.Current.FindResource("LightflowContextMenuStyle"), menu.Style);
            var items = menu.Items.Cast<MenuItem>().ToArray();
            Assert.Equal(new[] { "Export videos", "Export subclips" }, items.Select(item => item.Header));
            Assert.All(items, item => Assert.Same(Application.Current.FindResource("LightflowMenuItemStyle"), item.Style));
            try
            {
                OpenAt(menu, false);
                await Settle();
                AssertNoHorizontalScrolling(menu);
                foreach (var item in items)
                {
                    Assert.True(item.Focusable);
                    Assert.True(item.IsEnabled);
                    Assert.False(item.StaysOpenOnClick);
                    Assert.Equal(MenuItemRole.SubmenuItem, item.Role);
                }
            }
            finally { await CloseMenus(menu); }
        });
    }

    [Theory]
    [InlineData(150, 30, 180, 28)]
    [InlineData(350, 80, 180, 28)]
    [InlineData(450, 1000, 150, 24)]
    public async Task SharedPopupCandidatesPreferRightAndRetainVerticalAndLeftFallbacks(
        double width, double height, double targetWidth, double targetHeight)
    {
        await StaDispatcher.RunAsync(() =>
        {
            var popup = new LightflowSubmenuPopup();
            Assert.Equal(PlacementMode.Custom, popup.Placement);
            var candidates = popup.CustomPopupPlacementCallback!(
                new Size(width, height), new Size(targetWidth, targetHeight), new Point(7, 11));
            Assert.Equal(new[]
            {
                new Point(targetWidth, 0), new Point(targetWidth, targetHeight - height),
                new Point(-width, 0), new Point(-width, targetHeight - height)
            }, candidates.Select(candidate => candidate.Point));
            Assert.All(candidates, candidate => Assert.Equal(PopupPrimaryAxis.Vertical, candidate.PrimaryAxis));
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task GearMenuUsesMinimumWhileBrowserCanGrow()
    {
        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();
            var gear = LoadMenu("ApplicationMenu");
            var browser = LoadMenu("BrowserAssetContextMenu");
            try
            {
                OpenAt(gear, false);
                await Settle();
                Assert.Equal(150, gear.MinWidth);
                Assert.InRange(gear.ActualWidth, 150, 151);
                Assert.True(double.IsNaN(gear.Width));
                AssertNoHorizontalScrolling(gear);
                gear.IsOpen = false;
                OpenAt(browser, false);
                await Settle();
                Assert.True(browser.ActualWidth > gear.ActualWidth);
                AssertNoHorizontalScrolling(browser);
            }
            finally { await CloseMenus(gear, browser); }
        });
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task BrowserSubmenusPreferRightAndFlipAtWorkAreaEdge(bool atRightEdge, bool atBottomEdge)
    {
        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();
            var menu = LoadMenu("BrowserAssetContextMenu");
            try
            {
                OpenAt(menu, atRightEdge, atBottomEdge);
                await Settle();
                foreach (var header in new[] { "Send To", "Rating", "Flag", "Export", "Camera LUT", "Creative LUT" })
                {
                    var item = menu.Items.OfType<MenuItem>().Single(i => Equals(i.Header, header));
                    if (header.EndsWith("LUT"))
                    {
                        // Runtime LUT population uses the same keyed item style.
                        item.Items.Clear();
                        item.Items.Add(NewItem("No LUT"));
                    }
                    item.IsSubmenuOpen = true;
                    await Settle();
                    AssertPlacement(item, atRightEdge, header);
                    if (header.EndsWith("LUT"))
                    {
                        // Remeasure an already open popup, as generated LUT choices arrive.
                        item.Items.Add(NewItem("A deliberately long LUT name that must remain fully visible"));
                        await Settle();
                        AssertPlacement(item, atRightEdge, header + " populated");
                        var popup = (Popup)item.Template.FindName("PART_Popup", item);
                        Assert.True(((FrameworkElement)popup.Child).ActualWidth > 300);
                    }
                    item.IsSubmenuOpen = false;
                    await Settle();
                    Assert.False(((LightflowSubmenuPopup)item.Template.FindName("PART_Popup", item)).OpensLeft);
                }
            }
            finally { await CloseMenus(menu); }
        });
    }

    [Fact]
    public async Task DeeperSubmenusAndKeyboardNavigationRetainMenuItemBehavior()
    {
        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();
            var menu = LoadMenu("BrowserAssetContextMenu");
            var parent = menu.Items.OfType<MenuItem>().Single(i => Equals(i.Header, "Rating"));
            var nested = NewItem("Nested choices");
            nested.Items.Add(NewItem("Choice"));
            parent.Items.Add(nested);
            try
            {
                OpenAt(menu, false);
                await Settle();
                parent.Focus();
                Press(parent, Key.Right);
                await Settle();
                Assert.True(parent.IsSubmenuOpen);
                AssertPlacement(parent, false, "Rating keyboard open");
                nested.Focus();
                Press(nested, Key.Right);
                await Settle();
                Assert.True(nested.IsSubmenuOpen);
                AssertPlacement(nested, false, "deeper submenu");
                var child = (MenuItem)nested.Items[0];
                child.Focus();
                Press(child, Key.Left);
                await Settle();
                Assert.False(nested.IsSubmenuOpen);
                Assert.True(parent.IsSubmenuOpen);
                Press(nested, Key.Escape);
                await Settle();
                Assert.False(parent.IsSubmenuOpen);
            }
            finally { await CloseMenus(menu); }
        });
    }

    [Fact]
    public async Task OpenSubmenuUpdatesChevronWhenLongerContentRequiresChangingSides()
    {
        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();
            var menu = LoadMenu("BrowserAssetContextMenu");
            var item = menu.Items.OfType<MenuItem>().Single(i => Equals(i.Header, "Camera LUT"));
            try
            {
                OpenAt(menu, false);
                await Settle();
                menu.HorizontalOffset = SystemParameters.WorkArea.Right - menu.ActualWidth - 220;
                item.Items.Clear();
                item.Items.Add(NewItem("No LUT"));
                item.IsSubmenuOpen = true;
                await Settle();
                AssertPlacement(item, false, "short LUT fits right");
                item.Items.Add(NewItem("A deliberately long LUT name that must remain fully visible"));
                await Settle();
                AssertPlacement(item, true, "expanded LUT requires left");
                item.Items.RemoveAt(1);
                await Settle();
                AssertPlacement(item, false, "short LUT fits right again");
            }
            finally { await CloseMenus(menu); }
        });
    }

    [Fact]
    public async Task NativeRightPlacementFollowsWindowsHandednessDespiteAvailableSpace()
    {
        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();
            var menu = LoadMenu("BrowserAssetContextMenu");
            var item = menu.Items.OfType<MenuItem>().Single(i => Equals(i.Header, "Send To"));
            try
            {
                OpenAt(menu, false);
                await Settle();
                var popup = (Popup)item.Template.FindName("PART_Popup", item);
                // Reproduce the pre-fix template without changing the Windows setting.
                popup.Placement = PlacementMode.Right;
                item.IsSubmenuOpen = true;
                await Settle();
                var parentPoint = item.PointToScreen(new Point());
                var childPoint = popup.Child.PointToScreen(new Point());
                output.WriteLine($"MenuDropAlignment={SystemParameters.MenuDropAlignment}; parent={parentPoint}; child={childPoint}");
                Assert.Equal(SystemParameters.MenuDropAlignment, childPoint.X < parentPoint.X);
            }
            finally { await CloseMenus(menu); }
        });
    }

    private void AssertPlacement(MenuItem item, bool left, string label)
    {
        var popup = Assert.IsType<LightflowSubmenuPopup>(item.Template.FindName("PART_Popup", item));
        Assert.Equal(PlacementMode.Custom, popup.Placement);
        Assert.Same(item, popup.PlacementTarget);
        var child = (FrameworkElement)popup.Child;
        Assert.True(child.ActualWidth >= 150);
        Assert.True(double.IsNaN(child.Width));
        var targetTopLeft = item.PointToScreen(new Point());
        var childTopLeft = child.PointToScreen(new Point());
        var childBottomRight = child.PointToScreen(new Point(child.ActualWidth, child.ActualHeight));
        var screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)targetTopLeft.X, (int)targetTopLeft.Y));
        Assert.InRange(childTopLeft.X, screen.WorkingArea.Left - 1, screen.WorkingArea.Right);
        Assert.InRange(childBottomRight.X, screen.WorkingArea.Left, screen.WorkingArea.Right + 1);
        Assert.InRange(childTopLeft.Y, screen.WorkingArea.Top - 1, screen.WorkingArea.Bottom);
        Assert.InRange(childBottomRight.Y, screen.WorkingArea.Top, screen.WorkingArea.Bottom + 1);
        Assert.Equal(left, childTopLeft.X < targetTopLeft.X);
        var targetRight = item.PointToScreen(new Point(item.ActualWidth, 0)).X;
        Assert.InRange(left ? childBottomRight.X - targetTopLeft.X : childTopLeft.X - targetRight, -1, 1);
        Assert.Equal(left, popup.OpensLeft);
        var arrow = (System.Windows.Shapes.Path)item.Template.FindName("SubmenuArrow", item);
        var geometry = PathGeometry.CreateFromGeometry(arrow.Data);
        Assert.Equal(left ? 5 : 0, geometry.Figures[0].StartPoint.X);
        AssertNoHorizontalScrolling(child);
        output.WriteLine($"{label}: parent={targetTopLeft}; popup={childTopLeft}; width={child.ActualWidth:F1}; left={left}");
    }

    private static void Press(MenuItem item, Key key) => item.RaiseEvent(new KeyEventArgs(
        Keyboard.PrimaryDevice, PresentationSource.FromVisual(item), Environment.TickCount, key)
        { RoutedEvent = Keyboard.KeyDownEvent });

    private static MenuItem NewItem(string header) => new()
    {
        Header = header, Style = (Style)Application.Current.FindResource("LightflowMenuItemStyle")
    };

    private static void OpenAt(ContextMenu menu, bool rightEdge, bool bottomEdge = false)
    {
        var work = SystemParameters.WorkArea;
        menu.Placement = PlacementMode.Absolute;
        menu.HorizontalOffset = rightEdge ? work.Right - 5 : work.Left + work.Width / 3;
        menu.VerticalOffset = bottomEdge ? work.Bottom - 5 : work.Top + 100;
        menu.IsOpen = true;
    }

    private static async Task Settle()
    {
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        await Task.Delay(200); // Includes the shared popup fade before screen-coordinate assertions.
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
    }

    private static async Task CloseMenus(params ContextMenu[] menus)
    {
        var visuals = new List<Visual>(menus);
        void RememberPopups(ItemsControl owner)
        {
            foreach (var item in owner.Items.OfType<MenuItem>())
            {
                if (item.Template?.FindName("PART_Popup", item) is Popup { Child: { } child })
                    visuals.Add(child);
                RememberPopups(item);
            }
        }
        foreach (var menu in menus)
        {
            RememberPopups(menu);
            menu.IsOpen = false;
        }
        // Fade completion/native destruction is asynchronous even after IsOpen becomes false.
        // Do not let a previous fixture retain capture or callbacks into the next STA test.
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (visuals.Any(visual => PresentationSource.FromVisual(visual) is not null))
        {
            Assert.True(DateTime.UtcNow < deadline, "Menu popup windows did not finish closing.");
            await Task.Delay(10);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        }
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
    }

    private static ContextMenu LoadMenu(string name)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "LightflowStudio", "MainWindow.xaml"))) root = root.Parent;
        var document = XDocument.Load(Path.Combine(root!.FullName, "LightflowStudio", "MainWindow.xaml"));
        var x = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
        var element = new XElement(document.Descendants().Single(e => e.Name.LocalName == "ContextMenu" &&
            ((string?)e.Attribute(x + "Name") == name || (string?)e.Attribute(x + "Key") == name)));
        // Load the real menu/style tree without MainWindow, storage or command side effects.
        foreach (var attribute in element.DescendantsAndSelf().Attributes().Where(a =>
            a.Name.LocalName is "Click" or "Opened" or "SubmenuOpened" or "PlacementTarget" ||
            a.Name == x + "Shared" || a.Name == x + "Key").ToArray()) attribute.Remove();
        var menu = (ContextMenu)XamlReader.Parse(element.ToString());
        // These tests explicitly drive opening, resizing and keyboard input. A runner's stationary
        // cursor can intersect the initially placed menu and schedule WPF's sibling-hover timer;
        // that timer legitimately closes our submenu even after the fixture relocates the root.
        // Isolate geometry/keyboard assertions from ambient pointer input, not from WPF placement.
        menu.IsHitTestVisible = false;
        return menu;
    }

    private static void AssertNoHorizontalScrolling(DependencyObject element)
    {
        if (element is ScrollViewer scroll) Assert.InRange(scroll.ScrollableWidth, 0, 0.01);
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
            AssertNoHorizontalScrolling(VisualTreeHelper.GetChild(element, i));
    }
}
