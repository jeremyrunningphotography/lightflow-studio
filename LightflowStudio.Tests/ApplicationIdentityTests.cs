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
    [InlineData("lightflow-header-lockup-480x96.png", "5EE8094B8A5DECF7BA06A0C166CC0581A7B01BCA81E1C3B20DA77E6A90558CFE")]
    [InlineData("lightflow-splash-1280x720.png", "B7D1DA7E0470E0119DB3FB223261AAA04CF9313E82ECD900A3EE557E8A28EB1F")]
    [InlineData("LightflowStudio.ico", "2A20E879577B4A21EACB9502D460B93FBCF1AA59BB4DBA41965B4D7E442E2772")]
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
            var viewport = Assert.IsType<CroppedBitmap>(artwork.Source);
            Assert.Equal(new Int32Rect(300, 110, 680, 480), viewport.SourceRect);
            Assert.Equal(440, splash.Width);
            Assert.Equal(680d / 480, splash.Width / splash.Height, 6);
            var bitmap = Assert.IsType<BitmapImage>(viewport.Source);
            Assert.Equal(1280, bitmap.PixelWidth);
            Assert.Equal(720, bitmap.PixelHeight);
            splash.Close();
            Assert.Equal(mode, app.ShutdownMode);
            Assert.Null(app.Storage);
            return Task.CompletedTask;
        });
    }
}
