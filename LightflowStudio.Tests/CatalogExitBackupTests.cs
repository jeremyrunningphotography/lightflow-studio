using Microsoft.Data.Sqlite;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class CatalogExitBackupTests : IAsyncLifetime
{
    private readonly string _root = CreateOwnedRoot();
    private LightflowStorageLocations Locations => LightflowStorageLocations.CreateAtRoot(_root) with { IsIsolated = true };
    private static string CreateOwnedRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "LightflowStudio"))) directory = directory.Parent;
        if (directory is null) throw new InvalidOperationException("Tests require their owning source workspace.");
        return Path.Combine(directory.FullName, "artifacts", "catalog-backup-tests", Guid.NewGuid().ToString("N"));
    }
    public Task InitializeAsync() { Directory.CreateDirectory(_root); return Task.CompletedTask; }
    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task RealExitSnapshotWaitsForWholeOperationAndContainsItsFinalTransactions()
    {
        var startup = await LightflowStorageCoordinator.StartAsync(profile: Locations);
        await using var storage = startup.Coordinator!;
        Assert.True(startup.IsReady);
        Assert.Empty(storage.CatalogBackups); // No routine startup copy.
        var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var accepted = storage.Mutations.RunAsync(async () =>
        {
            await storage.Collections.CreateSetAsync("First transaction");
            first.SetResult();
            await finish.Task;
            await storage.Collections.CreateSetAsync("Final transaction");
        });
        await first.Task;
        var backup = storage.BackupForExitAsync(storage.BackupDirectory, true);
        Assert.False(backup.IsCompleted);
        var later = storage.Collections.CreateSetAsync("After cancelled exit");
        Assert.False(later.IsCompleted);
        finish.SetResult();
        await accepted;
        var result = await backup.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.True(result.Succeeded, result.Diagnostic);
        using (var copy = new SqliteConnection($"Data Source={result.Backup!.Path};Mode=ReadOnly;Pooling=False"))
        {
            copy.Open();
            using var query = copy.CreateCommand();
            query.CommandText = "SELECT count(*) FROM CollectionSets;";
            Assert.Equal(2L, query.ExecuteScalar());
        }
        Assert.False(later.IsCompleted);
        storage.CancelPreparedExit();
        await later.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(3, (await storage.Collections.ListSetsAsync()).Count);
    }

    [Fact]
    public async Task FailedBackupAndCancelledDrainRestoreNormalMutationAdmission()
    {
        var startup = await LightflowStorageCoordinator.StartAsync(profile: Locations);
        await using var storage = startup.Coordinator!;
        var file = Path.Combine(_root, "not-a-folder");
        await File.WriteAllTextAsync(file, "owned fixture");
        Assert.False((await storage.BackupForExitAsync(file, true)).Succeeded);
        await storage.Collections.CreateSetAsync("After failure");
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var mutation = storage.Mutations.RunAsync(() => finish.Task);
        using var cancel = new CancellationTokenSource();
        var backup = storage.BackupForExitAsync(storage.BackupDirectory, true, cancellationToken: cancel.Token);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => backup);
        await storage.Collections.CreateSetAsync("After cancellation");
        finish.SetResult();
        await mutation;
        Assert.False(Directory.Exists(storage.BackupDirectory));
    }

    [Fact]
    public async Task CancellationAfterCopyLeavesNoCompletedOrPartialOutput()
    {
        var created = await new CatalogDatabaseService(Locations).CreateNewAsync();
        await using var session = created.Session!;
        using var cancel = new CancellationTokenSource();
        var directory = CatalogBackupDestination.Default(Locations);
        var progress = new InlineProgress(message => { if (message.StartsWith("Validating")) cancel.Cancel(); });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new SqliteCatalogRecoveryService(Locations)
            .CreateUserBackupAsync(session.DatabasePath, directory, session.Identity.CatalogId, session.SchemaVersion, progress, cancel.Token));
        Assert.Empty(Directory.EnumerateFiles(directory));
    }

    [Fact]
    public async Task CorruptSourceIsNotPublishedAndSourceBytesArePreserved()
    {
        Directory.CreateDirectory(Locations.CatalogDirectory);
        await File.WriteAllTextAsync(Locations.CatalogDatabasePath, "not a sqlite database");
        var result = await new SqliteCatalogRecoveryService(Locations).CreateUserBackupAsync(
            Locations.CatalogDatabasePath, CatalogBackupDestination.Default(Locations), Guid.NewGuid(), 1);
        Assert.False(result.Succeeded);
        Assert.Equal("not a sqlite database", await File.ReadAllTextAsync(Locations.CatalogDatabasePath));
        Assert.Empty(Directory.EnumerateFiles(CatalogBackupDestination.Default(Locations)));
    }

    [Fact]
    public async Task UserCopiesAreUniqueRecoverableAndNotSubjectToAutomaticRetention()
    {
        var created = await new CatalogDatabaseService(Locations).CreateNewAsync();
        await using var session = created.Session!;
        var instant = new DateTimeOffset(2026, 9, 22, 23, 59, 59, TimeSpan.Zero);
        var recovery = new SqliteCatalogRecoveryService(Locations, () => instant);
        var destination = Path.Combine(_root, "保存された Catalog", new string('x', 110), new string('y', 110));
        var first = await recovery.CreateUserBackupAsync(session.DatabasePath, destination, session.Identity.CatalogId, session.SchemaVersion);
        var second = await recovery.CreateUserBackupAsync(session.DatabasePath, destination, session.Identity.CatalogId, session.SchemaVersion);
        Assert.True(first.Succeeded, first.Diagnostic);
        Assert.True(second.Succeeded, second.Diagnostic);
        Assert.EndsWith("20260922T235959Z_00000001.db", second.Backup!.Path);
        Assert.True((await recovery.CheckIntegrityAsync(first.Backup!.Path)).IsValid);
        // Deliberately put a user copy in the managed recovery folder to prove old retention can't recognize it.
        Directory.CreateDirectory(Locations.CatalogBackupsDirectory);
        var retained = Path.Combine(Locations.CatalogBackupsDirectory, Path.GetFileName(first.Backup.Path));
        File.Copy(first.Backup.Path, retained);
        for (var index = 0; index < 14; index++)
        {
            instant = instant.AddDays(1);
            Assert.True((await recovery.CreateBackupAsync(session.DatabasePath, CatalogBackupKind.Migration)).Succeeded);
        }
        Assert.True(File.Exists(retained));
        Assert.DoesNotContain(Directory.EnumerateFiles(destination), path => path.Contains("incomplete"));
    }

    [Fact]
    public async Task SettingsMigrateDeterministicallyAndStayProfileOwned()
    {
        var first = await LightflowStorageCoordinator.StartAsync(profile: Locations);
        await using var a = first.Coordinator!;
        Assert.True(a.Settings.BackupCatalogOnClose);
        Assert.Equal(CatalogBackupDestination.Default(Locations), a.Settings.CatalogBackupDirectory);
        var selected = Path.Combine(_root, "selected");
        a.SaveSettings(a.Settings with { BackupCatalogOnClose = false, CatalogBackupDirectory = selected });
        var persisted = AppSettingsStore.Load(Locations.SettingsPath);
        Assert.False(persisted.BackupCatalogOnClose);
        Assert.Equal(selected, persisted.CatalogBackupDirectory);
        var other = Locations with { }; // Separate process-profile path, not another normal-user profile.
        other = LightflowStorageLocations.CreateAtRoot(Path.Combine(_root, "other")) with { IsIsolated = true };
        var second = await LightflowStorageCoordinator.StartAsync(profile: other);
        await using var b = second.Coordinator!;
        Assert.True(b.Settings.BackupCatalogOnClose);
        Assert.NotEqual(a.BackupDirectory, b.BackupDirectory);
        Assert.Throws<ArgumentException>(() => CatalogBackupDestination.Validate(other, selected));
    }

    [Fact]
    public void DestinationRejectsManagedOverlapAndEscapingIsolatedProfile()
    {
        foreach (var path in new[] { _root, Locations.CatalogDirectory, Locations.CatalogBackupsDirectory,
                     Locations.PreviewsDirectory, Locations.TemporaryDirectory, Path.GetDirectoryName(_root)! })
            Assert.Throws<ArgumentException>(() => CatalogBackupDestination.Validate(Locations, path));
        Assert.Throws<ArgumentException>(() => CatalogBackupDestination.Validate(Locations, "relative"));
    }

    private sealed class InlineProgress(Action<string> action) : IProgress<string>
    { public void Report(string value) => action(value); }

    [Fact]
    public async Task BackgroundReconciliationAcceptedBeforeQuiescenceFinishesAllCatalogPublication()
    {
        var created = await new CatalogDatabaseService(Locations).CreateNewAsync();
        await using var session = created.Session!;
        var media = Path.Combine(_root, "media");
        Directory.CreateDirectory(media);
        File.WriteAllText(Path.Combine(media, "first.jpg"), "disposable source one");
        File.WriteAllText(Path.Combine(media, "last.jpg"), "disposable source two");
        var roots = new MediaRootService(() => session, new MachineIdentityProvider(Locations.MachineIdentityPath), new MediaRootFileSystem());
        var root = (await roots.CreateAsync("Owned media", media)).Root!;
        var assets = new MediaAssetService(new CatalogMediaAssetRepository(() => session), roots, new SampledSourceFingerprintService());
        var folders = new PausedEnumeration(new MediaFolderEnumerator(roots, MediaTypeRegistry.CreateDefault(), new MediaFolderFileSystem()));
        var reconciliation = new CatalogReconciliationService(folders, assets);
        var background = reconciliation.ReconcileAsync(new(root.RootId));
        await folders.Entered.Task;
        var drain = session.Mutations.QuiesceAsync();
        Assert.False(drain.IsCompleted);
        folders.Continue.SetResult();
        var observed = await background.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(observed.Succeeded, observed.Diagnostic);
        using var quiet = await drain;
        var result = await new SqliteCatalogRecoveryService(Locations).CreateUserBackupAsync(session.DatabasePath,
            CatalogBackupDestination.Default(Locations), session.Identity.CatalogId, session.SchemaVersion);
        Assert.True(result.Succeeded, result.Diagnostic);
        using var copy = new SqliteConnection($"Data Source={result.Backup!.Path};Mode=ReadOnly;Pooling=False");
        copy.Open();
        using var command = copy.CreateCommand();
        command.CommandText = "SELECT count(*) FROM MediaAssets;";
        Assert.Equal(2L, command.ExecuteScalar());
    }

    private sealed class PausedEnumeration(IMediaFolderEnumerator inner) : IMediaFolderEnumerator
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Continue { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<MediaFolderEnumerationResult> EnumerateAsync(MediaFolderEnumerationRequest request, CancellationToken cancellationToken = default)
        {
            Entered.SetResult();
            await Continue.Task.WaitAsync(cancellationToken);
            return await inner.EnumerateAsync(request, cancellationToken);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DestinationDisappearingOrBecomingAFileAtCopyFailsWithoutFinalOutput(bool replaceWithFile)
    {
        var created = await new CatalogDatabaseService(Locations).CreateNewAsync();
        await using var session = created.Session!;
        var destination = CatalogBackupDestination.Default(Locations);
        var progress = new InlineProgress(message =>
        {
            if (message.StartsWith("Copying"))
            {
                Directory.Delete(destination);
                if (replaceWithFile) File.WriteAllText(destination, "destination changed after the write probe");
            }
        });
        var result = await new SqliteCatalogRecoveryService(Locations).CreateUserBackupAsync(
            session.DatabasePath, destination, session.Identity.CatalogId, session.SchemaVersion, progress);
        Assert.False(result.Succeeded);
        Assert.False(Directory.Exists(destination));
        Assert.True((await new SqliteCatalogRecoveryService(Locations).CheckIntegrityAsync(session.DatabasePath)).IsValid);
    }

    [Fact]
    public async Task TruncatedStagingIsNeverReportedAsASuccessfulBackup()
    {
        var created = await new CatalogDatabaseService(Locations).CreateNewAsync();
        await using var session = created.Session!;
        var destination = CatalogBackupDestination.Default(Locations);
        var progress = new InlineProgress(message =>
        {
            if (message.StartsWith("Validating"))
                File.WriteAllBytes(Assert.Single(Directory.GetFiles(destination, "*.incomplete")), [1, 2, 3]);
        });
        var result = await new SqliteCatalogRecoveryService(Locations).CreateUserBackupAsync(session.DatabasePath,
            destination, session.Identity.CatalogId, session.SchemaVersion, progress);
        Assert.False(result.Succeeded);
        Assert.Empty(Directory.GetFiles(destination));
        Assert.True((await new SqliteCatalogRecoveryService(Locations).CheckIntegrityAsync(session.DatabasePath)).IsValid);
    }

    [Fact]
    public async Task DialogFailureRequiresExplicitSkipAndDoesNotDisablePreference()
    {
        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();
            var startup = await LightflowStorageCoordinator.StartAsync(profile: Locations);
            await using var storage = startup.Coordinator!;
            var dialog = new CatalogBackupDialog(storage, _ => { });
            var file = Path.Combine(_root, "not-a-directory");
            File.WriteAllText(file, "owned");
            ((System.Windows.Controls.TextBox)dialog.FindName("Destination")).Text = file;
            Click(dialog, "BackupButton");
            await Until(() => ((System.Windows.Controls.Button)dialog.FindName("SkipButton")).Content?.ToString() == "Exit Without Backup");
            Assert.False(dialog.ExitApproved);
            Assert.True(storage.Settings.BackupCatalogOnClose);
            await storage.Collections.CreateSetAsync("Still writable after failure");
            Click(dialog, "SkipButton");
            Assert.True(dialog.ExitApproved);
            Assert.True(storage.Settings.BackupCatalogOnClose);
        });
    }

    [Fact]
    public async Task DialogCancellationReopensAndSuccessfulRetryKeepsSnapshotBoundary()
    {
        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();
            var startup = await LightflowStorageCoordinator.StartAsync(profile: Locations);
            await using var storage = startup.Coordinator!;
            var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var admitted = storage.Mutations.RunAsync(() => finish.Task);
            var dialog = new CatalogBackupDialog(storage, _ => { });
            Click(dialog, "BackupButton");
            Click(dialog, "CancelBackupButton");
            await Until(() => ((System.Windows.Controls.TextBlock)dialog.FindName("StatusText")).Text.StartsWith("Backup cancelled"));
            Assert.False(dialog.ExitApproved);
            Assert.False(admitted.IsCompleted);
            await storage.Collections.CreateSetAsync("After cancellation");
            finish.SetResult();
            await admitted;
            Click(dialog, "BackupButton");
            await Until(() => dialog.ExitApproved);
            var later = storage.Collections.CreateSetAsync("New work cannot race successful exit");
            Assert.False(later.IsCompleted);
            storage.CompletePreparedExit();
            await Assert.ThrowsAsync<ObjectDisposedException>(() => later);
            var files = Directory.GetFiles(storage.BackupDirectory);
            Assert.Single(files, path => path.EndsWith(".db"));
            Assert.DoesNotContain(files, path => path.Contains("previews", StringComparison.OrdinalIgnoreCase));
        });
    }

    [Fact]
    public async Task DialogUsesApplicationResourcesAndRendersWithoutDesktopInteraction()
    {
        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();
            var startup = await LightflowStorageCoordinator.StartAsync(profile: Locations);
            await using var storage = startup.Coordinator!;
            var dialog = new CatalogBackupDialog(storage, _ => { });
            var content = (System.Windows.FrameworkElement)dialog.Content;
            content.Measure(new System.Windows.Size(600, double.PositiveInfinity));
            content.Arrange(new System.Windows.Rect(new System.Windows.Point(), content.DesiredSize));
            content.UpdateLayout();
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(600, (int)Math.Ceiling(content.ActualHeight), 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            bitmap.Render(content);
            var png = new System.Windows.Media.Imaging.PngBitmapEncoder();
            png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using (var file = File.Create(Path.Combine(Path.GetDirectoryName(_root)!, "backup-dialog.png"))) png.Save(file);
            Assert.True(content.ActualHeight > 100);
            Assert.Equal("Back Up & Exit", ((System.Windows.Controls.Button)dialog.FindName("BackupButton")).Content);
            dialog.Close();
        });
    }

    [Fact]
    public void BackupChoiceShowsLocalDateAndActualFileSize()
    {
        var path = Path.Combine(_root, "backup.db");
        File.WriteAllBytes(path, new byte[256 * 1024]);
        var backup = new CatalogBackup(path, 18, DateTimeOffset.UtcNow, CatalogBackupKind.UserRequested);
        Assert.Equal($"{backup.CreatedUtc.LocalDateTime:g} — 256 KB", MainWindow.CatalogBackupDisplayName(backup));
        File.Delete(path);
        Assert.Equal($"{backup.CreatedUtc.LocalDateTime:g} — Size unavailable", MainWindow.CatalogBackupDisplayName(backup));
    }

    [Fact]
    public async Task ManualBackupUsesAndPersistsUnsavedDestinationWithoutSavingOtherSettings()
    {
        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();
            var startup = await LightflowStorageCoordinator.StartAsync(profile: Locations);
            await using var storage = startup.Coordinator!;
            var original = storage.BackupDirectory;
            var edited = Path.Combine(_root, "Edited destination");
            var cancelled = new CatalogBackupDialog(storage, _ => { }, exit: false, destination: edited);
            Assert.Equal(edited, ((System.Windows.Controls.TextBox)cancelled.FindName("Destination")).Text);
            Click(cancelled, "StayButton");
            Assert.Equal(original, storage.BackupDirectory);

            var dialog = new CatalogBackupDialog(storage, _ => { }, exit: false, destination: edited);
            Click(dialog, "BackupButton");
            await Until(() => dialog.ExitApproved);
            Assert.Equal(edited, storage.BackupDirectory);
            Assert.Single(Directory.GetFiles(edited, "*.db"));
            Assert.True(storage.Settings.BackupCatalogOnClose);
            await storage.Collections.CreateSetAsync("Manual backup leaves Catalog writable");
        });
    }

    [Fact]
    public async Task RestoreFromPreviousFolderKeepsDestinationAndBacksUpCurrentState()
    {
        var startup = await LightflowStorageCoordinator.StartAsync(profile: Locations);
        await using var storage = startup.Coordinator!;
        await storage.Collections.CreateSetAsync("Original state");
        var original = await storage.BackupCatalogAsync();
        Assert.True(original.Succeeded);
        var newerFolder = Path.Combine(_root, "New backup folder");
        await storage.SaveBackupDestinationAsync(newerFolder, CancellationToken.None);
        await storage.Collections.CreateSetAsync("Later edit");
        Assert.True((await storage.BackupCatalogAsync()).Succeeded);
        Assert.DoesNotContain(storage.CatalogBackups, x => x.Path == original.Backup!.Path);
        var restored = await storage.RestoreCatalogAsync(original.Backup!.Path);
        Assert.True(restored.Succeeded, restored.Diagnostic);
        Assert.Single(await storage.Collections.ListSetsAsync());
        Assert.Equal(newerFolder, storage.BackupDirectory);
        var safety = Assert.Single(storage.CatalogBackups, x => x.Kind == CatalogBackupKind.Recovery);
        using var copy = new SqliteConnection($"Data Source={safety.Path};Mode=ReadOnly;Pooling=False");
        copy.Open();
        using var query = copy.CreateCommand();
        query.CommandText = "SELECT count(*) FROM CollectionSets;";
        Assert.Equal(2L, query.ExecuteScalar());
    }

    [Fact]
    public async Task ExpandedBackupSectionAndRestoreConfirmationRenderWithLightflowResources()
    {
        await StaDispatcher.RunAsync(() =>
        {
            TestWpfApplication.EnsureLoaded();
            var dialog = new ConfirmationDialog("Restore Backup", "Restore your Catalog from this backup?",
                "Your Catalog will return to the state saved in this backup. Lightflow will first save a safety backup of its current state.",
                "9/22/2026 11:11 AM — 332 KB", "Restore Backup");
            var expander = new System.Windows.Controls.Expander
            {
                Header = "Catalog backup and recovery", IsExpanded = true,
                Style = (System.Windows.Style)System.Windows.Application.Current.FindResource("SettingsExpanderStyle"),
                Content = new System.Windows.Controls.TextBlock { Text = "9/22/2026 11:11 AM — 332 KB", Foreground = System.Windows.Media.Brushes.White }
            };
            var panel = new System.Windows.Controls.StackPanel { Background = (System.Windows.Media.Brush)System.Windows.Application.Current.FindResource("ShellSurfaceBrush") };
            var content = (System.Windows.FrameworkElement)dialog.Content;
            dialog.Content = null;
            panel.Children.Add(content);
            panel.Children.Add(expander);
            panel.Measure(new System.Windows.Size(650, double.PositiveInfinity));
            panel.Arrange(new System.Windows.Rect(new System.Windows.Point(), panel.DesiredSize));
            panel.UpdateLayout();
            var expanded = (System.Windows.Controls.Border)expander.Template.FindName("ExpandSite", expander);
            Assert.Equal(System.Windows.Visibility.Visible, expanded.Visibility);
            Assert.True(expanded.ActualHeight > 0);
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(650, (int)Math.Ceiling(panel.ActualHeight), 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            bitmap.Render(panel);
            var png = new System.Windows.Media.Imaging.PngBitmapEncoder();
            png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using (var file = File.Create(Path.Combine(Path.GetDirectoryName(_root)!, "restore-and-expander.png"))) png.Save(file);
            dialog.Close();
            return Task.CompletedTask;
        });
    }

    private static void Click(CatalogBackupDialog dialog, string name) =>
        ((System.Windows.Controls.Button)dialog.FindName(name)).RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
    private static async Task Until(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("Backup dialog did not reach its terminal state.");
            await Task.Delay(10);
        }
    }
}
