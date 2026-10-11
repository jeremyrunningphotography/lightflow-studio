using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LightflowStudio;
using Xunit;

namespace LightflowStudio.Tests;

[Collection("STA dispatcher tests")]
public sealed class CatalogSettingsPresentationTests
{
    [Fact]
    public async Task UnifiedGateAndHealthySettings_RenderWithoutWorkspaceBypass()
    {
        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();
            var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "artifacts", "lf012-startup-gate-presentation"));
            var profile = ApplicationDataProfile.Resolve(["--data-root", Path.Combine(root, Guid.NewGuid().ToString("N"))]);
            var start = await LightflowStorageCoordinator.StartAsync(profile: profile, allowFirstCreation: false);
            await using (var gate = new CatalogStartupGate(start, profile))
            {
                Assert.False(gate.CanEnterWorkspace);
                Assert.Throws<InvalidOperationException>(() => new MainWindow(start.Coordinator!, start.Status, start.Diagnostic));
                Assert.Throws<InvalidOperationException>(() => gate.TakeReady());
                var dialog = new CatalogSetupDialog(gate, profile) { ShowInTaskbar = false, ShowActivated = false };
                try
                {
                    dialog.Show(); dialog.UpdateLayout();
                    Assert.Equal("Welcome to Lightflow Studio", dialog.GateHeading.Text);
                    Assert.Equal(Visibility.Collapsed, dialog.GateRetryButton.Visibility);
                    Assert.Equal("Exit", dialog.GateExitButton.Content);
                    Assert.Equal("Create New Catalog…", dialog.GateCreateButton.Content);
                    Assert.Equal("Open Existing Catalog…", dialog.GateOpenButton.Content);
                    Assert.False(File.Exists(profile.CatalogDatabasePath));
                    Capture(dialog, Path.Combine(root, "first-run.png"));
                }
                finally { dialog.Close(); }
                Assert.False(gate.CanEnterWorkspace);
            }
            foreach (var variant in new[] {
                (StorageStartupStatus.CatalogUnavailable, CatalogRecoveryReason.LocationUnavailable),
                (StorageStartupStatus.CatalogUnavailable, CatalogRecoveryReason.CompetingOwner),
                (StorageStartupStatus.CatalogUnavailable, CatalogRecoveryReason.PendingRecovery),
                (StorageStartupStatus.CatalogUnavailable, CatalogRecoveryReason.UnsupportedStorage),
                (StorageStartupStatus.CatalogUnreadable, CatalogRecoveryReason.ValidationFailed),
                (StorageStartupStatus.CatalogCorrupt, CatalogRecoveryReason.ValidationFailed),
                (StorageStartupStatus.CatalogUnavailable, CatalogRecoveryReason.OwnershipUnavailable) })
            {
                var (status, reason) = variant;
                var failure = await LightflowStorageCoordinator.StartAsync(profile: profile, allowFirstCreation: false);
                await using var gate = new CatalogStartupGate(failure with { Status = status, RecoveryReason = reason, Diagnostic = "Retained technical diagnostic " + reason }, profile);
                var dialog = new CatalogSetupDialog(gate, profile) { ShowInTaskbar = false, ShowActivated = false };
                try
                {
                    dialog.Show(); dialog.UpdateLayout();
                    Assert.Equal(CatalogRecoveryPresentation.Create(gate.Current.Status, reason, gate.Current.Diagnostic).Heading, dialog.GateHeading.Text);
                    Assert.Equal(Visibility.Visible, dialog.GateRetryButton.Visibility);
                    Assert.False(dialog.GateDetails.IsExpanded);
                    Assert.Contains("Retained technical", dialog.GateDiagnostic.Text);
                    Assert.DoesNotContain("ownership", dialog.GateHeading.Text, StringComparison.OrdinalIgnoreCase);
                    Assert.DoesNotContain("Browser", dialog.GateDescription.Text);
                    if (status == StorageStartupStatus.CatalogCorrupt)
                        Assert.Equal("This Catalog is damaged and could not be opened. Your Catalog files have been preserved.", dialog.GateDescription.Text);
                    Capture(dialog, Path.Combine(root, "recovery-" + reason + (status == StorageStartupStatus.CatalogCorrupt ? "-corrupt" : "") + ".png"));
                }
                finally { dialog.Close(); }
            }
            var created = await LightflowStorageCoordinator.CreateCatalogAsync(profile.CatalogDirectory, profile);
            Assert.True(created.IsReady, created.Diagnostic);
            await using var storage = created.Coordinator!;
            storage.SaveSettings(storage.Settings with { BackupCatalogOnClose = false });
            var window = new MainWindow(storage, created.Status, created.Diagnostic) { Width = 1440, Height = 960, ShowInTaskbar = false, ShowActivated = false };
            try
            {
                window.Show(); Assert.True(await window.StartupCompletion.WaitAsync(TimeSpan.FromSeconds(30)));
                window.MainTabs.SelectedIndex = ShellDestinationSelection.Index(ShellDestination.Settings);
                window.SettingsCategoryList.SelectedIndex = 2; window.UpdateLayout();
                Assert.True(window.EnsureCatalogWorkspace());
                Assert.Equal("Move Catalog…", window.MoveCatalogButton.Content);
                Assert.Equal("Lightflow Catalog Location", window.SettingsCatalogLocationLabel.Text);
                Assert.Null(window.FindName("RetryCatalogButton"));
                Assert.Null(window.FindName("SettingsRecoveryBanner"));
                Assert.Equal(profile.CatalogDirectory, window.SettingsCatalogDirectory.Text);
                Capture(window, Path.Combine(root, "healthy-settings.png"));
            }
            finally { window.Close(); }
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

    [Fact]
    public async Task LostReadinessBlocksBackNavigationAndShortcutsThenRetiresWorkspace()
    {
        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();
            var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "artifacts", "lf012-navigation-gate", Guid.NewGuid().ToString("N")));
            var profile = ApplicationDataProfile.Resolve(["--data-root", root]);
            var start = await LightflowStorageCoordinator.StartAsync(profile: profile);
            await using var storage = start.Coordinator!;
            storage.SaveSettings(storage.Settings with { BackupCatalogOnClose = false });
            var window = new MainWindow(storage, start.Status, start.Diagnostic) { ShowActivated = false, ShowInTaskbar = false };
            try
            {
                window.Show(); Assert.True(await window.StartupCompletion.WaitAsync(TimeSpan.FromSeconds(30)));
                window.MainTabs.SelectedIndex = ShellDestinationSelection.Index(ShellDestination.Settings);
                var lease = (StartupStoreEvidence)typeof(LightflowStorageCoordinator).GetField("_catalogStartup", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(storage)!;
                lease.Dispose();
                typeof(MainWindow).GetMethod("UtilityBackToBrowser_Click", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                    .Invoke(window, [window, new RoutedEventArgs()]);
                Assert.NotEqual(ShellDestinationSelection.Index(ShellDestination.Home), window.MainTabs.SelectedIndex);
                var key = new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,
                    PresentationSource.FromVisual(window)!, 0, System.Windows.Input.Key.B) { RoutedEvent = System.Windows.Input.Keyboard.PreviewKeyDownEvent };
                typeof(MainWindow).GetMethod("MainWindow_PreviewKeyDown", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(window, [window, key]);
                Assert.True(key.Handled); Assert.False(window.EnsureCatalogWorkspace());
                await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
                Assert.False(window.IsVisible); Assert.False(window.IsLoaded);
                await Assert.ThrowsAnyAsync<Exception>(() => storage.Collections.CreateCollectionAsync("Stale authoring callback"));
            }
            finally { window.Close(); (System.Windows.Application.Current as App)?.CancelCatalogRestart(); }
        });
    }
}
