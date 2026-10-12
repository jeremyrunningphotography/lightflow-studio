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
    public async Task SuccessfulExistingCatalogValidationClosesWithoutAnotherUserConfirmation()
    {
        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();
            var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var dialog = new CatalogOperationDialog("Validate existing Catalog", "Explicitly selected Catalog directory",
                (_, _) => Task.FromResult("The selected Catalog is valid."))
                { CloseOnSuccess = true, ShowActivated = false, ShowInTaskbar = false };
            dialog.Closed += (_, _) => closed.TrySetResult();
            dialog.Show();
            await closed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(dialog.CompletedSuccessfully);
        });
    }
    [Fact]
    public async Task CreateExactFolderAndMoveParentShowTheirDistinctFinalPaths()
    {
        await StaDispatcher.RunAsync(() =>
        {
            TestWpfApplication.EnsureLoaded();
            var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "artifacts", "lf012-startup-gate-presentation"));
            foreach (var move in new[] { false, true })
            {
                var exact = Path.Combine(root, Guid.NewGuid().ToString("N"), "Catalog");
                Directory.CreateDirectory(Path.GetDirectoryName(exact)!);
                var dialog = new CatalogDestinationDialog(move ? Path.GetDirectoryName(exact)! : exact, move) { ShowActivated = false, ShowInTaskbar = false };
                try
                {
                    dialog.Show(); dialog.UpdateLayout();
                    if (move)
                    {
                        Assert.Equal("Lightflow Catalog", dialog.ChildName.Text);
                        dialog.SelectFolder(Path.GetDirectoryName(exact)!);
                        Assert.Equal(Path.Combine(Path.GetDirectoryName(exact)!, "Lightflow Catalog"), dialog.FinalLocation.Text);
                        dialog.ChildName.Text = "Catalog";
                        Assert.Equal(exact, dialog.FinalLocation.Text);
                        Assert.Equal("Destination parent folder", dialog.LocationLabel.Text);
                    }
                    else Assert.Equal(exact, dialog.Destination.Text);
                    Assert.True(dialog.TryAccept()); Assert.Equal(exact, dialog.SelectedDirectory);
                    Assert.False(Directory.Exists(exact));
                    Assert.Equal(move ? "Move Catalog" : "Create New Catalog", dialog.ActionButton.Content);
                    dialog.UpdateLayout();
                    Capture(dialog, Path.Combine(root, move ? "exact-move.png" : "exact-create.png"));
                    if (move)
                    {
                        Directory.CreateDirectory(exact);
                        Assert.False(dialog.TryAccept()); Assert.Null(dialog.SelectedDirectory);
                        Assert.Contains("already exists", dialog.ErrorText.Text);
                        Assert.Same(dialog.FindResource("RedBrush"), dialog.ErrorText.Foreground);
                        Assert.Same(dialog.FindResource("MutedTextBrush"), dialog.LocationHint.Foreground);
                        Assert.Empty(Directory.EnumerateFileSystemEntries(exact));
                        dialog.UpdateLayout(); Capture(dialog, Path.Combine(root, "move-child-collision.png"));
                        dialog.ChildName.Text = "Other Catalog";
                        Assert.True(dialog.TryAccept());
                        Assert.Equal(dialog.FinalLocation.Text, dialog.SelectedDirectory);
                        Assert.False(Directory.Exists(dialog.SelectedDirectory));
                        dialog.UpdateLayout(); Capture(dialog, Path.Combine(root, "move-renamed-child.png"));
                        var file = Path.Combine(Path.GetDirectoryName(exact)!, "File child");
                        File.WriteAllText(file, "Retained foreign bytes");
                        dialog.ChildName.Text = "File child";
                        Assert.False(dialog.TryAccept()); Assert.Null(dialog.SelectedDirectory);
                        Assert.Equal("Retained foreign bytes", File.ReadAllText(file));
                        dialog.ChildName.Text = "../escape";
                        Assert.False(dialog.TryAccept()); Assert.Null(dialog.SelectedDirectory);
                    }
                }
                finally { dialog.Close(); }
            }
            return Task.CompletedTask;
        });
    }
    [Fact]
    public async Task FinalChildAppearingAfterDialogAcceptanceRefusesWithoutChangingEitherCatalog()
    {
        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();
            var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "artifacts", "lf012-parent-move-late-collision", Guid.NewGuid().ToString("N")));
            var profile = ApplicationDataProfile.Resolve(["--data-root", root]);
            var startup = await LightflowStorageCoordinator.StartAsync(profile: profile);
            Assert.True(startup.IsReady, startup.Diagnostic);
            await using var storage = startup.Coordinator!;
            await storage.Collections.CreateCollectionAsync("Retained after late collision");
            var id = storage.CatalogSession.Identity.CatalogId;
            var parent = Directory.CreateDirectory(Path.Combine(root, "Selected parent")).FullName;
            var dialog = new CatalogDestinationDialog(parent, true);
            Assert.True(dialog.TryAccept());
            var final = dialog.SelectedDirectory!;
            Assert.Equal(final, dialog.FinalLocation.Text);
            Directory.CreateDirectory(final);
            var foreign = Path.Combine(final, "foreign.txt");
            await File.WriteAllTextAsync(foreign, "Retained foreign bytes");
            var move = await storage.RelocateCatalogAsync(final);
            Assert.False(move.Succeeded);
            Assert.Equal("The resulting Catalog folder already exists. Choose another name or parent folder.", move.Diagnostic);
            Assert.True(storage.VerifyWorkspaceReadiness());
            Assert.Equal(profile.CatalogDirectory, storage.Locations.CatalogDirectory);
            Assert.Equal(id, storage.CatalogSession.Identity.CatalogId);
            Assert.Equal("Retained after late collision", Assert.Single(await storage.Collections.ListCollectionsAsync()).Name);
            Assert.Equal("Retained foreign bytes", await File.ReadAllTextAsync(foreign));
            Assert.False(File.Exists(Path.Combine(final, LightflowStorageLocations.CatalogFileName)));
        });
    }

    [Fact]
    public async Task ActualMissingGateRefreshesFailureReasonAndTroubleshootingAfterSelectionAndRetry()
    {
        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();
            var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "artifacts", "lf012-startup-gate-presentation"));
            var profile = ApplicationDataProfile.Resolve(["--data-root", Path.Combine(root, Guid.NewGuid().ToString("N"))]);
            ApplicationDataProfile.Initialize(profile);
            var missing = Path.Combine(profile.ApplicationDataDirectory, "Missing previous Catalog");
            AppSettingsStore.Save(profile.SettingsPath, new() { CatalogDirectory = missing, CatalogId = Guid.NewGuid() }, isolated: true);
            var start = await LightflowStorageCoordinator.StartAsync(profile: profile);
            await using var gate = new CatalogStartupGate(start, profile);
            var dialog = new CatalogSetupDialog(gate, profile) { ShowActivated = false, ShowInTaskbar = false };
            try
            {
                dialog.Show(); dialog.GateDetails.IsExpanded = true; dialog.UpdateLayout();
                Assert.Equal("Catalog unavailable", dialog.GateHeading.Text);
                Assert.Contains("Failure category: LocationUnavailable", dialog.GateDiagnostic.Text);
                Assert.DoesNotContain("exclusively", dialog.GateDiagnostic.Text);
                Assert.DoesNotContain("Another Lightflow", dialog.GateDiagnostic.Text);
                Capture(dialog, Path.Combine(root, "actual-missing-troubleshooting.png"));
                var emptySelection = Directory.CreateDirectory(Path.Combine(profile.ApplicationDataDirectory, "Selected empty folder")).FullName;
                await gate.OpenAsync(emptySelection);
                dialog.RefreshPresentation();
                Assert.Equal(CatalogRecoveryReason.CatalogDirectoryRequired, gate.Current.RecoveryReason);
                Assert.Equal("Select an existing Catalog", dialog.GateHeading.Text);
                Assert.Same(dialog.FindResource("TextBrush"), dialog.GateHeading.Foreground);
                Assert.Equal("Lightflow Catalog Location", dialog.GateLocationLabel.Text);
                Assert.Contains("Failure category: CatalogDirectoryRequired", dialog.GateDiagnostic.Text);
                Assert.False(dialog.GateDetails.IsExpanded);
                await gate.RetryAsync(); dialog.RefreshPresentation();
                Assert.Equal("Catalog unavailable", dialog.GateHeading.Text);
                Assert.Same(dialog.FindResource("RedBrush"), dialog.GateHeading.Foreground);
                Assert.Equal(missing, dialog.NewLocation.Text);
                Assert.Equal("Expected Lightflow Catalog Location", dialog.GateLocationLabel.Text);
                Assert.DoesNotContain("CatalogDirectoryRequired", dialog.GateDiagnostic.Text);
                Assert.False(File.Exists(Path.Combine(missing, LightflowStorageLocations.CatalogFileName)));
                Assert.False(File.Exists(profile.CatalogDatabasePath));
            }
            finally { dialog.Close(); }
        });
    }
    [Fact]
    public async Task CreateCopyMatchesRecoveryContextWithoutClaimingPriorFilesAccessible()
    {
        await StaDispatcher.RunAsync(() =>
        {
            TestWpfApplication.EnsureLoaded();
            var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "artifacts", "lf012-startup-gate-presentation"));
            foreach (var recovery in new[] { false, true })
            {
                var dialog = new CatalogDestinationDialog(Path.Combine(root, "New Catalog"), false, recovery) { ShowActivated = false, ShowInTaskbar = false };
                try
                {
                    dialog.Show(); dialog.UpdateLayout();
                    Assert.Equal(recovery ? "Create a new Catalog to continue." : "Create a new Catalog at the location below.", dialog.Description.Text);
                    Assert.DoesNotContain("preserved", dialog.Description.Text);
                    Assert.DoesNotContain("independent", dialog.Description.Text);
                    Assert.Equal("Choose an empty folder or enter a new folder location for your Catalog.", dialog.LocationHint.Text);
                    Assert.Equal(Visibility.Collapsed, dialog.MoveChildFields.Visibility);
                    Capture(dialog, Path.Combine(root, recovery ? "create-recovery.png" : "create-context-healthy.png"));
                }
                finally { dialog.Close(); }
            }
            return Task.CompletedTask;
        });
    }
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
                    Assert.Equal("Catalog setup required", dialog.GateHeading.Text);
                    Assert.Equal(Visibility.Collapsed, dialog.GateRetryButton.Visibility);
                    Assert.Equal("Exit", dialog.GateExitButton.Content);
                    Assert.Equal("Create New Catalog…", dialog.GateCreateButton.Content);
                    Assert.Equal("Open Existing Catalog…", dialog.GateOpenButton.Content);
                    Assert.False(File.Exists(profile.CatalogDatabasePath));
                    Capture(dialog, Path.Combine(root, "creation-disabled-setup.png"));
                }
                finally { dialog.Close(); }
                Assert.False(gate.CanEnterWorkspace);
            }
            foreach (var variant in new[] {
                (StorageStartupStatus.CatalogUnavailable, CatalogRecoveryReason.LocationUnavailable),
                (StorageStartupStatus.CatalogUnavailable, CatalogRecoveryReason.CompetingOwner),
                (StorageStartupStatus.CatalogUnavailable, CatalogRecoveryReason.PendingRecovery),
                (StorageStartupStatus.CatalogUnavailable, CatalogRecoveryReason.UnsupportedStorage),
                (StorageStartupStatus.CatalogUnavailable, CatalogRecoveryReason.LocationRefused),
                (StorageStartupStatus.CatalogUnavailable, CatalogRecoveryReason.InitialLocationUnavailable),
                (StorageStartupStatus.CatalogUnreadable, CatalogRecoveryReason.CatalogDirectoryRequired),
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
                    Assert.Contains("Failure category: " + reason, dialog.GateDiagnostic.Text);
                    Assert.Contains("Expected path: " + gate.DisplayCatalogDirectory, dialog.GateDiagnostic.Text);
                    Assert.Equal(reason == CatalogRecoveryReason.LocationUnavailable ? "Expected Lightflow Catalog Location" : "Lightflow Catalog Location", dialog.GateLocationLabel.Text);
                    if (reason == CatalogRecoveryReason.LocationUnavailable)
                    {
                        Assert.Same(dialog.FindResource("RedBrush"), dialog.GateHeading.Foreground);
                        Assert.Equal("Open an existing Lightflow Catalog or create a new one to continue.", dialog.GateAssurance.Text);
                    }
                    Assert.DoesNotContain("ownership", dialog.GateHeading.Text, StringComparison.OrdinalIgnoreCase);
                    Assert.DoesNotContain("Browser", dialog.GateDescription.Text);
                    if (status == StorageStartupStatus.CatalogCorrupt)
                        Assert.Equal("This Catalog is damaged and could not be opened. Your Catalog files have been preserved.", dialog.GateDescription.Text);
                    Capture(dialog, Path.Combine(root, "recovery-" + reason + (status == StorageStartupStatus.CatalogCorrupt ? "-corrupt" : "") + ".png"));
                    if (reason == CatalogRecoveryReason.LocationUnavailable)
                    { dialog.GateDetails.IsExpanded = true; dialog.UpdateLayout(); Capture(dialog, Path.Combine(root, "missing-troubleshooting.png")); }
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
                var lease = storage.Lifecycle.CatalogEvidence!;
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
