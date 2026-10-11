using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LightflowStudio;
using Xunit;

namespace LightflowStudio.Tests;

[Collection("STA dispatcher tests")]
public sealed class CatalogSettingsPresentationTests
{
    [Fact]
    public async Task FirstChoiceAndRecoveryActions_RenderWithSharedLightflowStyles()
    {
        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();
            var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "artifacts", "lf012-presentation"));
            var profile = ApplicationDataProfile.Resolve(["--data-root", Path.Combine(root, Guid.NewGuid().ToString("N"))]);
            var result = await LightflowStorageCoordinator.StartAsync(profile: profile, allowFirstCreation: false);
            await using var storage = result.Coordinator!;
            var setup = new CatalogSetupDialog(storage) { ShowInTaskbar = false, ShowActivated = false };
            try
            {
                setup.Show(); setup.UpdateLayout();
                Assert.False(File.Exists(profile.CatalogDatabasePath));
                Assert.InRange(setup.ActualHeight, 150, 450);
                Capture(setup, Path.Combine(root, "first-run.png"));
            }
            finally { setup.Close(); }
            var window = new MainWindow(storage, StorageStartupStatus.CatalogUnavailable, "The configured Catalog drive is unavailable.")
                { Width = 1440, Height = 960, ShowInTaskbar = false, ShowActivated = false };
            try
            {
                window.Show();
                Assert.True(await window.StartupCompletion.WaitAsync(TimeSpan.FromSeconds(30)));
                window.UpdateLayout();
                Assert.Equal(Visibility.Visible, window.RetryCatalogButton.Visibility);
                Assert.Equal(Visibility.Visible, window.CatalogRecoveryMessage.Visibility);
                Assert.Contains("No replacement Catalog", window.CatalogRecoveryMessage.Text);
                Capture(window, Path.Combine(root, "recovery-settings.png"));
            }
            finally { storage.SaveSettings(storage.Settings with { BackupCatalogOnClose = false }); window.Close(); }
        });
    }
    private static void Capture(Window window, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(path); encoder.Save(output);
    }
}
