using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Xunit;

namespace LightflowStudio.Tests;

[Collection("STA dispatcher tests")]
public sealed class CatalogMoveCompletionPresentationTests
{
    private static string EvidenceRoot => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "artifacts", "lf012-final-completion-presentation"));

    [Theory]
    [InlineData("activation")]
    [InlineData("configuration")]
    public async Task MoveSuccessIsShownOnlyAfterVerifiedActivationConfigurationAndOwnership(string boundary)
    {
        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();
            var profile = ApplicationDataProfile.Resolve(["--data-root", Path.Combine(EvidenceRoot, Guid.NewGuid().ToString("N"))]);
            ApplicationDataProfile.Initialize(profile);
            var parent = Directory.CreateDirectory(Path.Combine(profile.ApplicationDataDirectory, "Selected parent")).FullName;
            var destination = Path.Combine(parent, "Lightflow Catalog");
            using var pause = new BoundaryPause(boundary);
            var activator = new CompletionActivator(pause, false);
            var startup = await LightflowStorageCoordinator.StartAsync(profile: profile,
                activator: activator, configuration: new CompletionConfiguration(profile, destination, pause, false));
            Assert.True(startup.IsReady, startup.Diagnostic);
            activator.MoveEnabled = true;
            await using var storage = startup.Coordinator!;
            await storage.Collections.CreateCollectionAsync("Preserved through success presentation");
            var id = storage.CatalogSession.Identity.CatalogId;
            var dialog = CatalogOperationDialog.ForMove(storage, destination);
            dialog.ShowActivated = false; dialog.ShowInTaskbar = false;
            try
            {
                dialog.Show();
                await pause.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
                Assert.False(dialog.CompletedSuccessfully);
                Assert.Equal(Visibility.Collapsed, dialog.MoveSuccessPanel.Visibility);
                Assert.Throws<IOException>(() => StartupStoreEvidence.Begin(WindowsCatalogEvidenceEffects.Instance, Path.Combine(destination, LightflowStorageLocations.CatalogFileName), "Catalog", false));
                pause.Release.Set();
                await dialog.Completion.WaitAsync(TimeSpan.FromSeconds(15));
                dialog.UpdateLayout();
                Assert.True(dialog.CompletedSuccessfully, dialog.StatusText.Text);
                Assert.Equal(Visibility.Visible, dialog.MoveSuccessPanel.Visibility);
                Assert.Equal("Catalog moved successfully", dialog.SuccessHeading.Text);
                Assert.Equal("You can continue using Lightflow Studio.", dialog.SuccessMessage.Text);
                Assert.Equal("New Catalog location", dialog.SuccessLocationLabel.Text);
                Assert.Equal(destination, dialog.SuccessLocation.Text);
                Assert.Equal("Continue", dialog.CloseButton.Content);
                Assert.Same(dialog.FindResource("PrimaryButton"), dialog.CloseButton.Style);
                Assert.Same(dialog.FindResource("SuccessBrush"), dialog.SuccessIcon.Foreground);
                Assert.Equal(Visibility.Collapsed, dialog.CancelButton.Visibility);
                Assert.Equal(Visibility.Collapsed, dialog.Progress.Visibility);
                Assert.Equal("", dialog.Detail.Text); Assert.Equal("", dialog.StatusText.Text);
                Assert.DoesNotContain("retained", dialog.SuccessMessage.Text, StringComparison.OrdinalIgnoreCase);
                Assert.Equal(id, storage.CatalogSession.Identity.CatalogId);
                Assert.Single(await storage.Collections.ListCollectionsAsync());
                Assert.True(File.Exists(profile.CatalogDatabasePath));
                Assert.True(storage.VerifyWorkspaceReadiness());
                Capture(dialog, "move-success-" + boundary + ".png");
            }
            finally { pause.Release.Set(); if (!dialog.Completion.IsCompleted) await dialog.Completion.WaitAsync(TimeSpan.FromSeconds(15)); dialog.Close(); }
        });
    }

    [Theory]
    [InlineData("activation")]
    [InlineData("configuration")]
    [InlineData("collision")]
    [InlineData("rollback")]
    public async Task FailedMoveNeverPresentsSuccessAndRetainsSource(string failure)
    {
        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();
            var profile = ApplicationDataProfile.Resolve(["--data-root", Path.Combine(EvidenceRoot, Guid.NewGuid().ToString("N"))]);
            ApplicationDataProfile.Initialize(profile);
            var parent = Directory.CreateDirectory(Path.Combine(profile.ApplicationDataDirectory, "Selected parent")).FullName;
            var destination = Path.Combine(parent, "Lightflow Catalog");
            var activator = new CompletionActivator(null, failure == "activation");
            var startup = await LightflowStorageCoordinator.StartAsync(profile: profile,
                activator: activator,
                configuration: new CompletionConfiguration(profile, destination, null, failure is "configuration" or "rollback", failure == "rollback"));
            Assert.True(startup.IsReady, startup.Diagnostic);
            activator.MoveEnabled = true;
            await using var storage = startup.Coordinator!;
            await storage.Collections.CreateCollectionAsync("Preserved through failed Move");
            if (failure == "collision")
            { Directory.CreateDirectory(destination); await File.WriteAllTextAsync(Path.Combine(destination, "foreign.txt"), "Retained bytes"); }
            var dialog = CatalogOperationDialog.ForMove(storage, destination);
            dialog.ShowActivated = false; dialog.ShowInTaskbar = false;
            try
            {
                dialog.Show(); await dialog.Completion.WaitAsync(TimeSpan.FromSeconds(15)); dialog.UpdateLayout();
                Assert.False(dialog.CompletedSuccessfully);
                Assert.Equal(Visibility.Collapsed, dialog.MoveSuccessPanel.Visibility);
                Assert.Equal("Close", dialog.CloseButton.Content);
                Assert.Same(dialog.FindResource("RedBrush"), dialog.StatusText.Foreground);
                if (failure == "rollback")
                {
                    Assert.False(storage.VerifyWorkspaceReadiness());
                    await Assert.ThrowsAnyAsync<Exception>(() => storage.Collections.CreateCollectionAsync("Must refuse authoring"));
                }
                else
                {
                    Assert.True(storage.VerifyWorkspaceReadiness(), dialog.StatusText.Text);
                    Assert.Single(await storage.Collections.ListCollectionsAsync());
                }
                Assert.Equal(profile.CatalogDirectory, storage.Locations.CatalogDirectory);
                Assert.True(File.Exists(profile.CatalogDatabasePath));
                if (failure == "collision") Assert.Equal("Retained bytes", await File.ReadAllTextAsync(Path.Combine(destination, "foreign.txt")));
                Capture(dialog, "move-failure-" + failure + ".png");
            }
            finally { dialog.Close(); }
        });
    }

    [Fact]
    public async Task CancelledMoveDoesNotPresentSuccessAndKeepsSource()
    {
        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();
            var profile = ApplicationDataProfile.Resolve(["--data-root", Path.Combine(EvidenceRoot, Guid.NewGuid().ToString("N"))]);
            var startup = await LightflowStorageCoordinator.StartAsync(profile: profile);
            await using var storage = startup.Coordinator!;
            await storage.Collections.CreateCollectionAsync("Retained after cancellation");
            var destination = Path.Combine(profile.ApplicationDataDirectory, "Cancelled Catalog");
            using var heldRead = storage.CatalogSession.OpenConnection();
            var dialog = CatalogOperationDialog.ForMove(storage, destination);
            dialog.ShowActivated = false; dialog.ShowInTaskbar = false;
            try
            {
                dialog.Show();
                Assert.False(dialog.CompletedSuccessfully);
                dialog.CancelButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                heldRead.Close();
                await dialog.Completion.WaitAsync(TimeSpan.FromSeconds(15));
                Assert.False(dialog.CompletedSuccessfully);
                Assert.Equal(Visibility.Collapsed, dialog.MoveSuccessPanel.Visibility);
                Assert.Equal("Close", dialog.CloseButton.Content);
                Assert.True(storage.VerifyWorkspaceReadiness(), dialog.StatusText.Text);
                Assert.Equal(profile.CatalogDirectory, storage.Locations.CatalogDirectory);
                Assert.Single(await storage.Collections.ListCollectionsAsync());
                Assert.True(File.Exists(profile.CatalogDatabasePath));
            }
            finally { heldRead.Close(); if (!dialog.Completion.IsCompleted) await dialog.Completion.WaitAsync(TimeSpan.FromSeconds(15)); dialog.Close(); }
        });
    }

    [Fact]
    public async Task OutsideWorkspaceRecoveryIsActionableAndSafeCreationStillSucceeds()
    {
        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();
            var evidence = Path.Combine(EvidenceRoot, Guid.NewGuid().ToString("N"));
            var profile = ApplicationDataProfile.Resolve(["--data-root", Path.Combine(evidence, "profile")]);
            ApplicationDataProfile.Initialize(profile);
            var missing = Path.Combine(profile.ApplicationDataDirectory, "Missing Catalog");
            AppSettingsStore.Save(profile.SettingsPath, new() { CatalogDirectory = missing, CatalogId = Guid.NewGuid(), BackupCatalogOnClose = false }, isolated: true);
            var start = await LightflowStorageCoordinator.StartAsync(profile: profile);
            await using var gate = new CatalogStartupGate(start, profile);
            var dialog = new CatalogSetupDialog(gate, profile) { ShowActivated = false, ShowInTaskbar = false };
            try
            {
                dialog.Show();
                await gate.CreateAsync(Path.Combine(evidence, "Outside selected folder")); dialog.RefreshPresentation();
                Assert.Equal(CatalogRecoveryReason.OutsideWorkspace, gate.Current.RecoveryReason);
                Assert.Contains("Failure category: OutsideWorkspace", dialog.GateDiagnostic.Text);
                Assert.DoesNotContain("Failure category: None", dialog.GateDiagnostic.Text);
                Assert.DoesNotContain("--data-root", dialog.GateDescription.Text);
                Assert.Contains("inside", dialog.GateDescription.Text);
                Assert.Equal(profile.ApplicationDataDirectory, dialog.AllowedWorkspace.Text);
                Assert.Equal(Visibility.Visible, dialog.AllowedWorkspacePanel.Visibility);
                dialog.GateDetails.IsExpanded = true; dialog.UpdateLayout(); Capture(dialog, "outside-workspace-recovery.png");
                var safe = Path.Combine(profile.ApplicationDataDirectory, "New Catalog");
                await gate.CreateAsync(safe); Assert.True(gate.CanEnterWorkspace, gate.Current.Diagnostic);
                dialog.RefreshPresentation(); Assert.Equal(Visibility.Collapsed, dialog.AllowedWorkspacePanel.Visibility);
                await using var storage = gate.TakeReady().Coordinator!;
                Assert.Equal(safe, storage.Locations.CatalogDirectory);
                Assert.False(File.Exists(Path.Combine(missing, LightflowStorageLocations.CatalogFileName)));
                Assert.False(File.Exists(profile.CatalogDatabasePath));
            }
            finally { dialog.Close(); }
        });
    }

    [Fact]
    public async Task MoveOutsideWorkspaceKeepsInternalFlagsInTroubleshootingOnly()
    {
        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();
            var root = Path.Combine(EvidenceRoot, Guid.NewGuid().ToString("N"));
            var profile = ApplicationDataProfile.Resolve(["--data-root", Path.Combine(root, "profile")]);
            var start = await LightflowStorageCoordinator.StartAsync(profile: profile);
            await using var storage = start.Coordinator!;
            var destination = Path.Combine(root, "Outside Catalog");
            var dialog = CatalogOperationDialog.ForMove(storage, destination);
            dialog.ShowActivated = false; dialog.ShowInTaskbar = false;
            try
            {
                dialog.Show(); await dialog.Completion.WaitAsync(TimeSpan.FromSeconds(15)); dialog.UpdateLayout();
                Assert.False(dialog.CompletedSuccessfully);
                Assert.Equal(Visibility.Collapsed, dialog.MoveSuccessPanel.Visibility);
                Assert.DoesNotContain("--data-root", dialog.StatusText.Text);
                Assert.Contains("--data-root", dialog.FailureDiagnostic.Text);
                Assert.False(dialog.FailureDetails.IsExpanded);
                Assert.True(storage.VerifyWorkspaceReadiness());
                Assert.False(Directory.Exists(destination));
                Capture(dialog, "move-outside-workspace-failure.png");
            }
            finally { dialog.Close(); }
        });
    }

    [Fact]
    public async Task FailedStartupWithAbsentReasonDoesNotDisplayNoneOrInternalFlags()
    {
        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();
            var profile = ApplicationDataProfile.Resolve(["--data-root", Path.Combine(EvidenceRoot, Guid.NewGuid().ToString("N"))]);
            ApplicationDataProfile.Initialize(profile);
            await File.WriteAllTextAsync(profile.SettingsPath, "not valid settings JSON");
            var failed = await LightflowStorageCoordinator.StartAsync(profile: profile);
            Assert.False(failed.IsReady); Assert.Equal(StorageStartupStatus.InvalidConfiguration, failed.Status);
            await using var gate = new CatalogStartupGate(failed, profile);
            var dialog = new CatalogSetupDialog(gate, profile) { ShowActivated = false, ShowInTaskbar = false };
            try
            {
                dialog.Show(); dialog.GateDetails.IsExpanded = true; dialog.UpdateLayout();
                Assert.Contains("Failure category: ConfigurationInvalid", dialog.GateDiagnostic.Text);
                Assert.DoesNotContain("Failure category: None", dialog.GateDiagnostic.Text);
                Assert.Equal("Catalog settings need attention", dialog.GateHeading.Text);
                Assert.DoesNotContain("--data-root", dialog.GateDescription.Text);
                Assert.False(File.Exists(profile.CatalogDatabasePath));
                Capture(dialog, "invalid-configuration-recovery.png");
            }
            finally { dialog.Close(); }
        });
    }

    private sealed class BoundaryPause(string boundary) : IDisposable
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal ManualResetEventSlim Release { get; } = new(false);
        internal void Observe(string current)
        {
            if (current != boundary) return;
            Entered.TrySetResult();
            if (!Release.Wait(TimeSpan.FromSeconds(15))) throw new TimeoutException("Test-owned boundary was not released.");
        }
        public void Dispose() => Release.Dispose();
    }
    private sealed class CompletionActivator(BoundaryPause? pause, bool fail) : ICatalogSessionActivator
    {
        internal bool MoveEnabled;
        private bool _moveObserved;
        public CatalogDatabaseSession Activate(CatalogDatabaseSession session)
        {
            if (MoveEnabled && !_moveObserved)
            { _moveObserved = true; pause?.Observe("activation"); if (fail) throw new IOException("Injected destination activation failure"); }
            return session;
        }
    }
    private sealed class CompletionConfiguration(LightflowStorageLocations profile, string destination, BoundaryPause? pause, bool fail, bool failRollback = false) : IStorageConfigurationStore
    {
        private bool _committed;
        public bool TryLoad(out AppSettings settings, out string? diagnostic) => AppSettingsStore.TryLoadForStartup(profile.SettingsPath, out settings, out diagnostic, isolated: true);
        public void Save(AppSettings settings)
        {
            if (_committed && failRollback) throw new IOException("Injected source configuration restoration failure");
            if (settings.CatalogDirectory == destination)
            {
                pause?.Observe("configuration");
                if (fail)
                {
                    if (failRollback) { AppSettingsStore.Save(profile.SettingsPath, settings, isolated: true); _committed = true; }
                    throw new IOException("Injected configuration commit failure");
                }
            }
            AppSettingsStore.Save(profile.SettingsPath, settings, isolated: true);
        }
    }
    private static void Capture(Window window, string name)
    {
        Directory.CreateDirectory(EvidenceRoot);
        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(Path.Combine(EvidenceRoot, name)); encoder.Save(output);
    }
}
