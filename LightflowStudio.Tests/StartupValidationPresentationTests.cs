using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LightflowStudio;
using Xunit;

namespace LightflowStudio.Tests;

[Collection("STA dispatcher tests")]
public sealed class StartupValidationPresentationTests
{
    [Fact]
    public async Task ExceptionalSplashFitsExistingFootprintAndProgressNeverClaimsCompletion()
    {
        await StaDispatcher.RunAsync(() =>
        {
            TestWpfApplication.EnsureLoaded();
            var splash = new StartupSplash();
            try
            {
                var content = (Grid)splash.Content;
                var status = content.Children.OfType<TextBlock>().First();
                var supporting = content.Children.OfType<TextBlock>().Last();
                var progress = Assert.Single(content.Children.OfType<ProgressBar>());
                foreach (var scale in new[] { 1d, 1.5d, 2d })
                foreach (var (store, reason) in new[] { ("Catalog", StartupValidationReason.UnexpectedShutdown),
                    ("Previews", StartupValidationReason.UnexpectedShutdown), ("Catalog", StartupValidationReason.Migration),
                    ("Catalog", StartupValidationReason.Restore), ("Catalog", StartupValidationReason.DatabaseAnomaly) })
                {
                    var message = StartupValidationProgress.For(store, reason);
                    splash.SetValidation(message);
                    VisualTreeHelper.SetRootDpi(content, new DpiScale(scale, scale));
                    content.Measure(new Size(splash.Width, splash.Height));
                    content.Arrange(new Rect(0, 0, splash.Width, splash.Height)); content.UpdateLayout();
                    Assert.Equal(440, splash.Width); Assert.Equal(440d * 480 / 680, splash.Height);
                    Assert.Equal(message.Primary, status.Text); Assert.Equal(message.Supporting, supporting.Text);
                    Assert.True(progress.IsIndeterminate); progress.ApplyTemplate();
                    var indicator = (FrameworkElement)progress.Template.FindName("PART_Indicator", progress);
                    var pulse = (FrameworkElement)progress.Template.FindName("IndeterminatePulse", progress);
                    Assert.Equal(Visibility.Collapsed, indicator.Visibility);
                    Assert.Equal(Visibility.Visible, pulse.Visibility); Assert.True(pulse.ActualWidth < progress.ActualWidth);
                    foreach (var text in new[] { status, supporting })
                    {
                        var shaped = new FormattedText(text.Text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                            new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch), text.FontSize, Brushes.White, scale);
                        Assert.True(shaped.Width <= splash.Width - text.Margin.Left - text.Margin.Right, text.Text);
                    }
                    var capture = Environment.GetEnvironmentVariable("LIGHTFLOW_STARTUP_CAPTURE");
                    if (capture is not null)
                    {
                        Directory.CreateDirectory(capture);
                        var image = new RenderTargetBitmap((int)(splash.Width * scale), (int)(splash.Height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
                        image.Render(content); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
                        using var file = File.Create(Path.Combine(capture, $"{store}-{reason}-{scale}.png")); encoder.Save(file);
                    }
                }
                splash.SetProgress("Opening storage…");
                Assert.Equal(Visibility.Collapsed, supporting.Visibility); Assert.Equal(Visibility.Collapsed, progress.Visibility);
                // Shared determinate presentation is retained for actual measurable work.
                progress.IsIndeterminate = false; progress.Value = 40; progress.UpdateLayout();
                Assert.Equal(Visibility.Visible, ((FrameworkElement)progress.Template.FindName("PART_Indicator", progress)).Visibility);
                Assert.Equal(Visibility.Collapsed, ((FrameworkElement)progress.Template.FindName("IndeterminatePulse", progress)).Visibility);
            }
            finally { splash.Close(); }
            return Task.CompletedTask;
        });
    }
}
