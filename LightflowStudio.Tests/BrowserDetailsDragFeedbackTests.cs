using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LightflowStudio;
using Xunit;

namespace LightflowStudio.Tests;

[Collection("STA dispatcher tests")]
public sealed class BrowserDetailsDragFeedbackTests
{
    [Theory]
    [InlineData(0, 2)]
    [InlineData(0, 5)]
    [InlineData(4, 0)]
    [InlineData(2, 4)]
    public async Task NativeDrag_KeepsDestinationAndOrangePaintAndClearsAfterDrop(int source, int boundary)
    {
        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();
            // Establish capture availability independently of Lightflow's presenter.
            var probeButton = new Button { Content = "Stock WPF capture probe" };
            var probeWindow = new Window { Content = probeButton, Width = 240, Height = 100, Left = 40, Top = 40,
                ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual };
            bool captureAvailable;
            try
            {
                probeWindow.Show(); probeWindow.Activate(); probeButton.Focus();
                captureAvailable = probeButton.CaptureMouse();
                File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "lf042-capture-probe.txt"),
                    $"Stock WPF Button capture: {captureAvailable}; source={source}; boundary={boundary}{Environment.NewLine}");
                probeButton.ReleaseMouseCapture();
            }
            finally { probeWindow.Close(); }
            var view = new GridView();
            for (var i = 0; i < 5; i++)
                view.Columns.Add(new() { Header = new GridViewColumnHeader { Content = "Column " + i }, Width = 120 });
            var presenter = new BrowserDetailsHeaderPresenter { Columns = view.Columns, AllowsColumnReorder = true };
            var scroll = new ScrollViewer { Content = presenter, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Height = 32 };
            var window = new Window { Content = scroll, Width = 410, Height = 100, Left = 40, Top = 40,
                ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual };
            window.Resources["ShellFocusBrush"] = new SolidColorBrush(Color.FromRgb(240, 151, 63));
            try
            {
                window.Show();
                window.UpdateLayout();
                await System.Windows.Threading.Dispatcher.Yield();
                var original = view.Columns.ToArray();
                var separator = Enumerable.Range(0, VisualTreeHelper.GetChildrenCount(presenter))
                    .Select(i => VisualTreeHelper.GetChild(presenter, i)).OfType<Separator>().Single();
                Assert.Equal(0, separator.Opacity);
                var native = typeof(GridViewHeaderRowPresenter);
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                object? Call(string name, params object[] args) => native.GetMethod(name, flags)!.Invoke(presenter, args);
                void Set(string name, object value) => native.GetField(name, flags)!.SetValue(presenter, value);
                for (var repeat = 0; repeat < 2; repeat++)
                {
                    Call("PrepareHeaderDrag", (GridViewColumnHeader)view.Columns[source].Header,
                        new Point(source * 120 + 20, 10), new Point(20, 10), false);
                    Set("_currentPos", new Point(source * 120 + 30, 10));
                    Call("StartHeaderDrag");
                    Set("_desColumnIndex", boundary);
                    Set("_currentPos", new Point(boundary * 120, 10));
                    separator.Visibility = Visibility.Visible;
                    presenter.InvalidateMeasure();
                    window.UpdateLayout();
                    scroll.ScrollToHorizontalOffset(Math.Max(0, boundary * 120 - 200));
                    window.UpdateLayout();
                    var point = new Point(scroll.HorizontalOffset + 60, 10);
                    presenter.UpdateFeedback(point);
                    Assert.Equal(1, separator.Opacity);
                    separator.ApplyTemplate();
                    var paint = (Border)VisualTreeHelper.GetChild(separator, 0);
                    Assert.Equal(Color.FromRgb(240, 151, 63), ((SolidColorBrush)paint.Background).Color);
                    Assert.Equal(2, separator.ActualWidth);
                    Assert.InRange(Math.Abs(separator.TransformToAncestor(presenter).Transform(new Point()).X - boundary * 120), 0, 1);
                    // Render actual WPF pixels inside the isolated desktop; no visible-desktop capture.
                    if (repeat == 0)
                    {
                        var bitmap = new RenderTargetBitmap(640, 40, 96, 96, PixelFormats.Pbgra32);
                        bitmap.Render(presenter);
                        var evidence = Path.Combine(AppContext.BaseDirectory, "lf042-rendered-feedback.png");
                        using var file = File.Create(evidence);
                        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); encoder.Save(file);
                    }
                    foreach (var invalid in new[] { new Point(point.X, -1), new Point(point.X, 40), new Point(-1, 10), new Point(900, 10) })
                    {
                        presenter.UpdateFeedback(invalid);
                        Assert.Equal(0, separator.Opacity);
                        presenter.UpdateFeedback(point);
                        Assert.Equal(1, separator.Opacity);
                    }
                    var ownFlags = BindingFlags.Instance | BindingFlags.NonPublic;
                    void RaisePaintEvent(string name) => typeof(BrowserDetailsHeaderPresenter).GetMethod(name, ownFlags)!
                        .Invoke(presenter, [new MouseEventArgs(Mouse.PrimaryDevice, 0)]);
                    RaisePaintEvent("OnMouseLeave");
                    Assert.Equal(0, separator.Opacity);
                    presenter.UpdateFeedback(point);
                    RaisePaintEvent("OnLostMouseCapture");
                    Assert.Equal(0, separator.Opacity);
                    presenter.UpdateFeedback(point);
                    if (repeat == 1)
                    {
                        // Actual WPF capture and owning-Window Escape route, even with row/window focus.
                        var header = (GridViewColumnHeader)view.Columns[source].Header;
                        window.Activate();
                        header.Focus();
                        Assert.Equal(captureAvailable, header.CaptureMouse());
                        var escape = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), 0, Key.Escape)
                            { RoutedEvent = Keyboard.PreviewKeyDownEvent };
                        window.RaiseEvent(escape);
                        Assert.Equal(captureAvailable, escape.Handled);
                        Assert.Null(Mouse.Captured);
                        if (captureAvailable) Assert.Equal(0, separator.Opacity);
                        else
                        {
                            // The stock control probe establishes that capture is unavailable here.
                            // No-capture Escape must remain available to the rest of the application.
                            presenter.CancelFeedback();
                            Assert.Equal(0, separator.Opacity);
                        }
                    }
                    Call("FinishHeaderDrag", repeat == 1);
                    Assert.Equal(Visibility.Hidden, separator.Visibility);
                }
                var target = source >= boundary ? boundary : boundary - 1;
                Assert.Same(original[source], view.Columns[target]);
                Assert.All(view.Columns, column => Assert.Equal(120, column.Width));
                // Hidden columns simply leave the native collection, retaining identity and widths.
                var hidden = view.Columns[1]; view.Columns.Remove(hidden);
                presenter.UpdateLayout();
                Assert.DoesNotContain(hidden, view.Columns);
                presenter.CancelFeedback();
                Assert.Equal(0, separator.Opacity);
            }
            finally { window.Close(); }
        });
    }
}
