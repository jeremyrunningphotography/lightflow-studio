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
                Assert.Contains("No replacement Catalog", window.CatalogRecoveryAssurance.Text);
                Assert.Equal(Visibility.Visible, window.SettingsRecoveryBanner.Visibility);
                Assert.False(window.CatalogRecoveryDetails.IsExpanded);
                Assert.Equal(profile.CatalogDirectory, window.SettingsCatalogDirectory.Text);
                Assert.DoesNotContain("configured Catalog drive", window.SettingsMessage.Text);
                Capture(window, Path.Combine(root, "recovery-settings.png"));
                foreach (var reason in new[] { CatalogRecoveryReason.CompetingOwner, CatalogRecoveryReason.PendingRecovery, CatalogRecoveryReason.ValidationFailed })
                {
                    window.CatalogRecoveryDetails.IsExpanded = true;
                    window.ApplyCatalogRecoveryPresentation(StorageStartupStatus.CatalogUnavailable, reason, "Distinct technical evidence: " + reason);
                    window.UpdateLayout();
                    var expected = CatalogRecoveryPresentation.Create(StorageStartupStatus.CatalogUnavailable, reason, "Distinct technical evidence: " + reason);
                    Assert.Equal(expected.Heading, window.CatalogRecoveryHeading.Text);
                    Assert.Equal(expected.Diagnostic, window.CatalogRecoveryDiagnostics.Text);
                    Assert.False(window.CatalogRecoveryDetails.IsExpanded);
                    Capture(window, Path.Combine(root, "recovery-" + reason + ".png"));
                }
                window.ApplyCatalogRecoveryPresentation(StorageStartupStatus.Ready, CatalogRecoveryReason.None, null);
                Assert.Equal(Visibility.Collapsed, window.SettingsRecoveryBanner.Visibility);
                Assert.Equal(Visibility.Collapsed, window.CatalogRecoveryPanel.Visibility);
                Assert.Equal(Visibility.Collapsed, window.RetryCatalogButton.Visibility);
                Assert.Equal("", window.CatalogRecoveryDiagnostics.Text);
                Assert.True(window.MoveCatalogButton.IsEnabled);
                Assert.DoesNotContain("recovery action", window.SettingsMessage.Text);
            }
            finally { storage.SaveSettings(storage.Settings with { BackupCatalogOnClose = false }); window.Close(); }
        });
    }
    [Theory]
    [InlineData((int)CatalogRecoveryReason.LocationUnavailable, "location is unavailable", "another Lightflow")]
    [InlineData((int)CatalogRecoveryReason.CompetingOwner, "another Lightflow instance", "Reconnect")]
    [InlineData((int)CatalogRecoveryReason.PendingRecovery, "recovery needs attention", "another Lightflow")]
    [InlineData((int)CatalogRecoveryReason.ValidationFailed, "validation failed", "Reconnect")]
    public void ActualReasonHasSpecificExplanation(int reasonValue, string heading, string speculative)
    {
        var reason = (CatalogRecoveryReason)reasonValue;
        var state = CatalogRecoveryPresentation.Create(StorageStartupStatus.CatalogUnavailable, reason, "raw diagnostic");
        Assert.Contains(heading, state.Heading);
        Assert.DoesNotContain(speculative, state.Description);
        Assert.Equal("raw diagnostic", state.Diagnostic);
        Assert.True(state.NoReplacementCreated);
        Assert.False(CatalogRecoveryPresentation.Create(StorageStartupStatus.InvalidConfiguration, reason, "raw").NoReplacementCreated);
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
