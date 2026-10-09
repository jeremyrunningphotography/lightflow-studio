using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LightflowStudio;
using Xunit;

namespace LightflowStudio.Tests;

[Collection("STA dispatcher tests")]
public sealed class WindowsCatalogAdmissionPresentationTests
{
    [Fact]
    public async Task SyntheticNetworkRefusal_UsesExistingSettingsPresentationAndKeepsWorkspaceUsable()
    {
        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();
            var owner = new DirectoryInfo(AppContext.BaseDirectory);
            while (owner is not null && !File.Exists(Path.Combine(owner.FullName, "Directory.Build.props"))) owner = owner.Parent;
            Assert.NotNull(owner);
            var root = Path.Combine(owner!.FullName, "artifacts", "admission-presentation", Guid.NewGuid().ToString("N"));
            var profile = LightflowStorageLocations.CreateAtRoot(root) with { IsIsolated = true };
            var start = await LightflowStorageCoordinator.StartAsync(profile: profile,
                assessor: new WindowsCatalogAdmissionTests.Facts("network-alias"), configuration: new PresentationConfiguration());
            await using var storage = start.Coordinator!;
            var window = new MainWindow(storage, start.Status, start.Diagnostic)
            {
                Width = 1440, Height = 960, Left = -32000, Top = -32000,
                ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual
            };
            try
            {
                window.Show();
                Assert.True(await window.StartupCompletion.WaitAsync(TimeSpan.FromSeconds(30)));
                window.MainTabs.SelectedIndex = ShellDestinationSelection.Index(ShellDestination.Settings);
                window.UpdateLayout();
                Assert.Contains("Lightflow Catalogs must be stored on a supported local drive", window.SettingsMessage.Text);
                Assert.Contains("Network locations are supported for media and Catalog backups", window.SettingsMessage.Text);
                Assert.False(storage.CatalogAvailable);
                Assert.True(storage.PreviewAvailable);
                Assert.False(File.Exists(profile.CatalogDatabasePath));
                var capture = Path.Combine(owner.FullName, "artifacts", "admission-presentation", "network-refusal.png");
                var image = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                image.Render(window);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
                using var output = File.Create(capture); encoder.Save(output);
            }
            finally { window.Close(); }
        });
    }
    private sealed class PresentationConfiguration : IStorageConfigurationStore
    {
        public bool TryLoad(out AppSettings settings, out string? diagnostic)
        {
            settings = new() { BackupCatalogOnClose = false };
            diagnostic = null; return true;
        }
        public void Save(AppSettings settings) { }
    }
}
