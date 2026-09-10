using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Xunit;

namespace LightflowStudio.Tests;

[Collection("STA dispatcher tests")]
public sealed class ApplicationIdentityTests
{
    [Theory]
    [InlineData("lightflow-header-lockup-480x96.png", "4807F72391BCE4965FC1D4F3C544A208C9B7AE55239BD2FD4299333507AA7D57")]
    [InlineData("lightflow-splash-1280x720.png", "B7D1DA7E0470E0119DB3FB223261AAA04CF9313E82ECD900A3EE557E8A28EB1F")]
    [InlineData("LightflowStudio.ico", "8DFF99A5D180E47818A83E0A17FA5A72F62E2D3165AF57F22D6AB87C809DB507")]
    public async Task EmbeddedAssets_AreUnmodifiedApprovedSources(string name, string expectedHash)
    {
        await StaDispatcher.RunAsync(() =>
        {
            TestWpfApplication.EnsureLoaded();
            using var stream = Application.GetResourceStream(new Uri(
                $"pack://application:,,,/LightflowStudio;component/Assets/Branding/{name}"))!.Stream;
            Assert.Equal(expectedHash, Convert.ToHexString(SHA256.HashData(stream)));
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Splash_CanConstructWithoutStorage_AndDoesNotOwnApplicationLifetime()
    {
        await StaDispatcher.RunAsync(() =>
        {
            TestWpfApplication.EnsureLoaded();
            var app = (App)Application.Current;
            Assert.Null(app.Storage);
            var mode = app.ShutdownMode;
            var splash = new StartupSplash();
            Assert.Equal(WindowStyle.None, splash.WindowStyle);
            Assert.False(splash.ShowInTaskbar);
            Assert.False(splash.ShowActivated);
            Assert.False(splash.Focusable);
            var artwork = Assert.IsType<Image>(splash.Content);
            Assert.Equal(Stretch.Uniform, artwork.Stretch);
            var bitmap = Assert.IsType<BitmapImage>(artwork.Source);
            Assert.Equal(1280, bitmap.PixelWidth);
            Assert.Equal(720, bitmap.PixelHeight);
            splash.Close();
            Assert.Equal(mode, app.ShutdownMode);
            Assert.Null(app.Storage);
            return Task.CompletedTask;
        });
    }
}
