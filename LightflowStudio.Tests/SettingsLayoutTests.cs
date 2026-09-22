using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class SettingsLayoutTests
{
    [Fact]
    public async Task ActualSettingsMarkupFitsSupportedSizesAndRendersWithoutAWindow()
    {
        await StaDispatcher.RunAsync(() =>
        {
            TestWpfApplication.EnsureLoaded();
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "LightflowStudio"))) root = root.Parent;
            var document = XDocument.Load(Path.Combine(root!.FullName, "LightflowStudio", "MainWindow.xaml"));
            XNamespace ns = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
            XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
            var settings = new XElement(document.Descendants(ns + "Grid").Single(element =>
                (string?)element.Attribute("AutomationProperties.Name") == "Settings preferences"));
            settings.SetAttributeValue(XNamespace.Xmlns + "x", x.NamespaceName);
            settings.SetAttributeValue(XNamespace.Xmlns + "local", "clr-namespace:LightflowStudio;assembly=LightflowStudio");
            var resources = new XElement(ns + "Grid.Resources",
                document.Root!.Element(ns + "Window.Resources")!.Elements().Where(element =>
                    ((string?)element.Attribute(x + "Key")) is { } key &&
                    (key.StartsWith("Settings") || key == "RequirementInfoButton")).Select(element => new XElement(element)));
            var scoped = document.Descendants(ns + "TabItem.Resources").Single(element =>
                element.Elements().Any(style => (string?)style.Attribute("BasedOn") == "{StaticResource SettingsInputStyle}"));
            resources.Add(scoped.Elements().Select(element => new XElement(element)));
            settings.AddFirst(resources);
            foreach (var attribute in settings.DescendantsAndSelf().Attributes().Where(attribute =>
                         attribute.Name.LocalName is "Click" or "TextChanged" or "SelectionChanged" or "Checked" or
                             "Unchecked" or "MouseEnter" or "MouseLeave" or "MouseLeftButtonUp").ToArray()) attribute.Remove();
            var grid = (Grid)XamlReader.Parse(settings.ToString().Replace("clr-namespace:LightflowStudio\"", "clr-namespace:LightflowStudio;assembly=LightflowStudio\""));
            var host = new Grid { Background = (Brush)System.Windows.Application.Current.FindResource("WindowBrush") };
            host.Children.Add(grid);
            foreach (var name in new[] { "SettingsScreengrabDirectory", "SettingsCameraLutFolder", "SettingsCreativeLutFolder", "SettingsCatalogDirectory", "SettingsPreviewsDirectory" })
                ((TextBox)grid.FindName(name)).Text = @"C:\Lightflow isolated profile\Example collection\Long folder name";
            ((TextBox)grid.FindName("SettingsPreviewCacheQuotaGb")).Text = "20";
            ((ItemsControl)grid.FindName("DependencyResults")).ItemsSource = new[]
            {
                new { Name = "FFmpeg", Summary = "Bundled version available", Detail = "Version and executable path", Resolution = "Ready", IsReady = true },
                new { Name = "NVIDIA NVENC", Summary = "Hardware encoder unavailable", Detail = "Diagnostic details", Resolution = "Check the driver and supported hardware.", IsReady = false }
            };
            var pages = new[] { "General", "Color", "Storage", "Advanced" }
                .Select(category => (ScrollViewer)grid.FindName("Settings" + category + "Page")).ToArray();
            var capture = Environment.GetEnvironmentVariable("LIGHTFLOW_SETTINGS_CAPTURE");
            foreach (var (width, height, scale) in new[] { (1090d, 590d, 1d), (1890d, 890d, 1d), (1090d, 590d, 1.5d), (1090d, 590d, 2d) })
            {
                VisualTreeHelper.SetRootDpi(host, new DpiScale(scale, scale));
                foreach (var page in pages)
                {
                    foreach (var other in pages) other.Visibility = other == page ? Visibility.Visible : Visibility.Collapsed;
                    host.Measure(new Size(width, height)); host.Arrange(new Rect(0, 0, width, height)); host.UpdateLayout();
                    foreach (var expander in Descendants(page).OfType<Expander>()) expander.IsExpanded = true;
                    host.UpdateLayout();
                    var groupLeft = grid.TranslatePoint(new Point(), host).X;
                    Assert.Equal((width - grid.ActualWidth) / 2, groupLeft, 1);
                    var cards = ((StackPanel)page.Content).Children.OfType<StackPanel>().Single();
                    var borders = cards.Children.OfType<Border>().ToArray();
                    for (var i = 1; i < borders.Length; i++)
                    {
                        var previous = borders[i - 1].TranslatePoint(new Point(), cards);
                        var current = borders[i].TranslatePoint(new Point(), cards);
                        Assert.Equal(previous.X, current.X);
                        Assert.True(current.Y >= previous.Y + borders[i - 1].ActualHeight);
                    }
                    Assert.True(page.ViewportWidth > 0);
                    Assert.Equal(0, page.ScrollableWidth);
                    foreach (var control in Descendants(page).OfType<Control>().Where(control => control is TextBox or Button))
                    {
                        Assert.True(control.IsTabStop);
                        var position = control.TranslatePoint(new Point(), page);
                        Assert.True(position.X >= -1, control.Name);
                        Assert.True(position.X + control.ActualWidth <= page.ActualWidth + 1, control.Name);
                    }
                    if (capture is not null)
                    {
                        Directory.CreateDirectory(capture);
                        var bitmap = new RenderTargetBitmap((int)(width * scale), (int)(height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
                        bitmap.Render(host);
                        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                        using var output = File.Create(Path.Combine(capture, $"{page.Name}-{width}-{scale}.png")); encoder.Save(output);
                    }
                }
            }
            return Task.CompletedTask;
        });
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i); yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }
}
