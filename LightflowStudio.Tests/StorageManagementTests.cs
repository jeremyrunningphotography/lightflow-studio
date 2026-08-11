using LightflowStudio;
using Microsoft.Data.Sqlite;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class StorageManagementTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"lightflow-location-{Guid.NewGuid():N}");

    [Fact]
    public async Task FirstStart_CreatesDefaultCatalogAndPersistsIdentity()
    {
        var result = await LightflowStorageCoordinator.StartAsync(_root);

        Assert.True(result.IsReady);
        Assert.Null(result.Coordinator!.Settings.CatalogDirectory);
        Assert.Null(result.Coordinator.Settings.PreviewsDirectory);
        Assert.NotNull(result.Coordinator.Settings.CatalogId);
        Assert.True(File.Exists(result.Coordinator.Locations.CatalogDatabasePath));
        await result.Coordinator.DisposeAsync();
    }

    [Fact]
    public async Task IndependentCustomLocations_SurviveRestart()
    {
        var first = (await LightflowStorageCoordinator.StartAsync(_root)).Coordinator!;
        var catalog = Path.Combine(_root, "catalog-on-ssd");
        var previews = Path.Combine(_root, "previews-on-large-drive");
        Assert.True((await first.RelocateCatalogAsync(catalog)).Succeeded);
        Assert.True((await first.RelocatePreviewsAsync(previews, PreviewRelocationMode.SwitchAndRebuild)).Succeeded);
        var identity = first.CatalogSession.Identity.CatalogId;
        await first.DisposeAsync();

        var restarted = await LightflowStorageCoordinator.StartAsync(_root);

        Assert.True(restarted.IsReady);
        Assert.Equal(Path.GetFullPath(catalog), restarted.Coordinator!.Locations.CatalogDirectory);
        Assert.Equal(Path.GetFullPath(previews), restarted.Coordinator.Locations.PreviewsDirectory);
        Assert.Equal(identity, restarted.Coordinator.CatalogSession.Identity.CatalogId);
        await restarted.Coordinator.DisposeAsync();
    }

    [Fact]
    public async Task CatalogRelocation_UsesSqliteBackup_PreservesIdentityAndRetainsSource()
    {
        var coordinator = (await LightflowStorageCoordinator.StartAsync(_root)).Coordinator!;
        var source = coordinator.Locations.CatalogDatabasePath;
        var identity = coordinator.CatalogSession.Identity.CatalogId;
        var destination = Path.Combine(_root, "relocated");

        var result = await coordinator.RelocateCatalogAsync(destination);

        Assert.True(result.Succeeded);
        Assert.True(File.Exists(source));
        Assert.Equal(identity, coordinator.CatalogSession.Identity.CatalogId);
        Assert.Equal(Path.Combine(destination, LightflowStorageLocations.CatalogFileName), coordinator.CatalogSession.DatabasePath);
        await coordinator.DisposeAsync();
    }

    [Fact]
    public async Task CatalogRelocation_IncludesCommittedWalData()
    {
        var coordinator = (await LightflowStorageCoordinator.StartAsync(_root)).Coordinator!;
        using var writer = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = coordinator.Locations.CatalogDatabasePath,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false
        }.ToString());
        writer.Open();
        using (var command = writer.CreateCommand())
        {
            command.CommandText = "PRAGMA wal_autocheckpoint=0; CREATE TABLE RelocationProbe (Value TEXT NOT NULL); INSERT INTO RelocationProbe VALUES ('committed-in-wal');";
            command.ExecuteNonQuery();
        }

        Assert.True((await coordinator.RelocateCatalogAsync(Path.Combine(_root, "wal-relocated"))).Succeeded);
        using var destination = new SqliteConnection($"Data Source={coordinator.Locations.CatalogDatabasePath};Mode=ReadOnly;Pooling=False");
        destination.Open();
        using var read = destination.CreateCommand();
        read.CommandText = "SELECT Value FROM RelocationProbe;";
        Assert.Equal("committed-in-wal", read.ExecuteScalar());
        await coordinator.DisposeAsync();
    }

    [Fact]
    public async Task CatalogTransferFailure_KeepsSourceConfigurationAndSessionActive()
    {
        var startup = await LightflowStorageCoordinator.StartAsync(_root, transfer: new FailingTransfer());
        var coordinator = startup.Coordinator!;
        var source = coordinator.Locations.CatalogDirectory;
        var identity = coordinator.CatalogSession.Identity.CatalogId;

        var result = await coordinator.RelocateCatalogAsync(Path.Combine(_root, "failed-destination"));

        Assert.False(result.Succeeded);
        Assert.Equal(source, coordinator.Locations.CatalogDirectory);
        Assert.Equal(identity, coordinator.CatalogSession.Identity.CatalogId);
        Assert.Equal(source, AppSettingsStore.Load(coordinator.Locations.SettingsPath).CatalogDirectory ?? source);
        await coordinator.DisposeAsync();
    }

    [Fact]
    public async Task CatalogValidationFailure_KeepsSourceAndConfigurationActive()
    {
        var startup = await LightflowStorageCoordinator.StartAsync(_root, transfer: new InvalidCatalogTransfer());
        var coordinator = startup.Coordinator!;
        var source = coordinator.Locations.CatalogDirectory;
        var identity = coordinator.CatalogSession.Identity.CatalogId;
        var destination = Path.Combine(_root, "invalid-copy");

        var result = await coordinator.RelocateCatalogAsync(destination);

        Assert.False(result.Succeeded);
        Assert.Equal(source, coordinator.Locations.CatalogDirectory);
        Assert.Equal(identity, coordinator.CatalogSession.Identity.CatalogId);
        Assert.False(File.Exists(Path.Combine(destination, LightflowStorageLocations.CatalogFileName)));
        await coordinator.DisposeAsync();
    }

    [Fact]
    public async Task ConfigurationWriteFailure_ReopensSourceAndRemovesUnswitchedDestination()
    {
        var defaults = LightflowStorageLocations.Create(_root);
        var configuration = new ControllableConfigurationStore(defaults.SettingsPath);
        var coordinator = (await LightflowStorageCoordinator.StartAsync(_root, configuration: configuration)).Coordinator!;
        var source = coordinator.Locations.CatalogDirectory;
        var identity = coordinator.CatalogSession.Identity.CatalogId;
        configuration.FailWrites = true;
        var destination = Path.Combine(_root, "configuration-failure");

        var result = await coordinator.RelocateCatalogAsync(destination);

        Assert.False(result.Succeeded);
        Assert.Equal(source, coordinator.Locations.CatalogDirectory);
        Assert.Equal(identity, coordinator.CatalogSession.Identity.CatalogId);
        Assert.Null(AppSettingsStore.Load(defaults.SettingsPath).CatalogDirectory);
        Assert.False(File.Exists(Path.Combine(destination, LightflowStorageLocations.CatalogFileName)));
        await coordinator.DisposeAsync();
    }

    [Fact]
    public async Task ConfigurationSwitch_HappensOnlyAfterDestinationIsOpenableAndIdentified()
    {
        var defaults = LightflowStorageLocations.Create(_root);
        var configuration = new ControllableConfigurationStore(defaults.SettingsPath);
        var coordinator = (await LightflowStorageCoordinator.StartAsync(_root, configuration: configuration)).Coordinator!;
        var expectedIdentity = coordinator.CatalogSession.Identity.CatalogId;
        configuration.OnSave = settings =>
        {
            if (settings.CatalogDirectory is null) return;
            var locations = LightflowStorageLocations.Create(_root,
                new(settings.CatalogDirectory, settings.PreviewsDirectory));
            var opened = new CatalogDatabaseService(locations).OpenExistingAsync().GetAwaiter().GetResult();
            Assert.True(opened.IsSuccess);
            Assert.Equal(expectedIdentity, opened.Session!.Identity.CatalogId);
            opened.Session.DisposeAsync().AsTask().GetAwaiter().GetResult();
        };

        Assert.True((await coordinator.RelocateCatalogAsync(Path.Combine(_root, "validated-before-switch"))).Succeeded);
        Assert.NotNull(configuration.LastSaved!.CatalogDirectory);
        Assert.Equal(expectedIdentity, coordinator.CatalogSession.Identity.CatalogId);
        await coordinator.DisposeAsync();
    }

    [Fact]
    public async Task PostSwitchActivationFailure_RestoresSourceConfigurationAndSession()
    {
        var defaults = LightflowStorageLocations.Create(_root);
        var configuration = new ControllableConfigurationStore(defaults.SettingsPath);
        var coordinator = (await LightflowStorageCoordinator.StartAsync(_root,
            configuration: configuration, activator: new FailingActivator())).Coordinator!;
        var source = coordinator.Locations.CatalogDirectory;
        var identity = coordinator.CatalogSession.Identity.CatalogId;
        var destination = Path.Combine(_root, "activation-failure");

        var result = await coordinator.RelocateCatalogAsync(destination);

        Assert.False(result.Succeeded);
        Assert.Equal(source, coordinator.Locations.CatalogDirectory);
        Assert.Equal(identity, coordinator.CatalogSession.Identity.CatalogId);
        Assert.Null(AppSettingsStore.Load(defaults.SettingsPath).CatalogDirectory);
        Assert.False(File.Exists(Path.Combine(destination, LightflowStorageLocations.CatalogFileName)));
        await coordinator.DisposeAsync();
    }

    [Fact]
    public async Task MissingConfiguredCatalog_NeverCreatesDefaultOrReplacement()
    {
        var defaults = LightflowStorageLocations.Create(_root);
        var missing = Path.Combine(_root, "missing-custom-catalog");
        AppSettingsStore.Save(defaults.SettingsPath, new AppSettings
        {
            CatalogDirectory = missing,
            CatalogId = Guid.NewGuid()
        });

        var result = await LightflowStorageCoordinator.StartAsync(_root);

        Assert.Equal(StorageStartupStatus.CatalogUnavailable, result.Status);
        Assert.False(result.Coordinator!.CatalogAvailable);
        Assert.False(File.Exists(Path.Combine(missing, LightflowStorageLocations.CatalogFileName)));
        Assert.False(File.Exists(defaults.CatalogDatabasePath));
        await result.Coordinator.DisposeAsync();
    }

    [Fact]
    public async Task MissingDatabaseInAvailableConfiguredDirectory_IsReportedWithoutReplacement()
    {
        var defaults = LightflowStorageLocations.Create(_root);
        var configured = Path.Combine(_root, "available-but-missing");
        Directory.CreateDirectory(configured);
        AppSettingsStore.Save(defaults.SettingsPath, new AppSettings
        {
            CatalogDirectory = configured,
            CatalogId = Guid.NewGuid()
        });

        var result = await LightflowStorageCoordinator.StartAsync(_root);

        Assert.Equal(StorageStartupStatus.CatalogMissing, result.Status);
        Assert.False(File.Exists(Path.Combine(configured, LightflowStorageLocations.CatalogFileName)));
        await result.Coordinator!.DisposeAsync();
    }

    [Fact]
    public async Task MalformedConfiguration_NeverFallsBackOrCreatesCatalog()
    {
        var defaults = LightflowStorageLocations.Create(_root);
        Directory.CreateDirectory(defaults.ApplicationDataDirectory);
        const string malformed = "{not-json";
        File.WriteAllText(defaults.SettingsPath, malformed);

        var result = await LightflowStorageCoordinator.StartAsync(_root);

        Assert.Equal(StorageStartupStatus.InvalidConfiguration, result.Status);
        Assert.Null(result.Coordinator);
        Assert.False(File.Exists(defaults.CatalogDatabasePath));
        Assert.Equal(malformed, File.ReadAllText(defaults.SettingsPath));
    }

    [Fact]
    public async Task InvalidStoragePath_DoesNotRewriteOtherwiseValidSettings()
    {
        var defaults = LightflowStorageLocations.Create(_root);
        AppSettingsStore.Save(defaults.SettingsPath, new AppSettings
        {
            LutFolder = @"D:\My LUTs",
            CatalogDirectory = "relative-catalog",
            CatalogId = Guid.NewGuid()
        });
        var before = File.ReadAllText(defaults.SettingsPath);

        var result = await LightflowStorageCoordinator.StartAsync(_root);

        Assert.Equal(StorageStartupStatus.InvalidConfiguration, result.Status);
        Assert.Null(result.Coordinator);
        Assert.Equal(before, File.ReadAllText(defaults.SettingsPath));
        Assert.False(File.Exists(defaults.CatalogDatabasePath));
    }

    [Fact]
    public async Task CorruptConfiguredCatalog_DoesNotFallBackToDefault()
    {
        var defaults = LightflowStorageLocations.Create(_root);
        var configured = Path.Combine(_root, "corrupt-catalog");
        Directory.CreateDirectory(configured);
        File.WriteAllText(Path.Combine(configured, LightflowStorageLocations.CatalogFileName), "not sqlite");
        AppSettingsStore.Save(defaults.SettingsPath, new AppSettings
        {
            CatalogDirectory = configured,
            CatalogId = Guid.NewGuid()
        });

        var result = await LightflowStorageCoordinator.StartAsync(_root);

        Assert.False(result.IsReady);
        Assert.False(result.Coordinator!.CatalogAvailable);
        Assert.False(File.Exists(defaults.CatalogDatabasePath));
        Assert.Equal("not sqlite", File.ReadAllText(Path.Combine(configured, LightflowStorageLocations.CatalogFileName)));
        await result.Coordinator.DisposeAsync();
    }

    [Fact]
    public async Task UnavailableConfiguredPreviews_AreReportedWithoutAffectingCatalog()
    {
        var first = (await LightflowStorageCoordinator.StartAsync(_root)).Coordinator!;
        var settingsPath = first.Locations.SettingsPath;
        var unavailable = Path.Combine(_root, "disconnected-previews");
        AppSettingsStore.Save(settingsPath, first.Settings with { PreviewsDirectory = unavailable });
        await first.DisposeAsync();

        var restarted = await LightflowStorageCoordinator.StartAsync(_root);

        Assert.True(restarted.IsReady);
        Assert.True(restarted.Coordinator!.CatalogAvailable);
        Assert.False(restarted.Coordinator.PreviewAvailable);
        Assert.Contains(unavailable, restarted.Coordinator.PreviewDiagnostic);
        Assert.False(Directory.Exists(unavailable));
        await restarted.Coordinator.DisposeAsync();
    }

    [Fact]
    public async Task CatalogRelocation_RejectsEquivalentOverlapAndConflictingCatalog()
    {
        var coordinator = (await LightflowStorageCoordinator.StartAsync(_root)).Coordinator!;
        Assert.Equal(StorageChangeStatus.EquivalentLocation,
            (await coordinator.RelocateCatalogAsync(coordinator.Locations.CatalogDirectory + Path.DirectorySeparatorChar)).Status);
        Assert.Equal(StorageChangeStatus.InvalidDestination,
            (await coordinator.RelocateCatalogAsync(Path.Combine(coordinator.Locations.PreviewsDirectory, "Catalog"))).Status);
        var conflict = Path.Combine(_root, "conflict");
        Directory.CreateDirectory(conflict);
        File.WriteAllText(Path.Combine(conflict, LightflowStorageLocations.CatalogFileName), "occupied");
        Assert.Equal(StorageChangeStatus.ConflictingCatalog,
            (await coordinator.RelocateCatalogAsync(conflict)).Status);
        var ambiguous = Path.Combine(_root, "ambiguous");
        Directory.CreateDirectory(ambiguous);
        File.WriteAllText(Path.Combine(ambiguous, "leftover.lightflow-moving"), "partial");
        Assert.Equal(StorageChangeStatus.ConflictingCatalog,
            (await coordinator.RelocateCatalogAsync(ambiguous)).Status);
        await coordinator.DisposeAsync();
    }

    [Fact]
    public async Task PreviewMove_CopiesDataSwitchesConfigurationAndDoesNotChangeCatalog()
    {
        var coordinator = (await LightflowStorageCoordinator.StartAsync(_root)).Coordinator!;
        Directory.CreateDirectory(coordinator.Locations.PreviewsDirectory);
        File.WriteAllText(Path.Combine(coordinator.Locations.PreviewsDirectory, "preview.bin"), "derived");
        var catalog = coordinator.CatalogSession.Identity.CatalogId;
        var destination = Path.Combine(_root, "new-previews");

        var result = await coordinator.RelocatePreviewsAsync(destination, PreviewRelocationMode.MoveExisting);

        Assert.True(result.Succeeded);
        Assert.Equal("derived", File.ReadAllText(Path.Combine(destination, "preview.bin")));
        Assert.Equal(catalog, coordinator.CatalogSession.Identity.CatalogId);
        Assert.False(Directory.Exists(Path.Combine(_root, "Jeremy Running Photography", "Lightflow Studio", "Previews")));
        await coordinator.DisposeAsync();
    }

    [Fact]
    public async Task PreviewSwitchAndRebuild_LeavesOldCacheAndChangesOnlyPreviewPath()
    {
        var coordinator = (await LightflowStorageCoordinator.StartAsync(_root)).Coordinator!;
        Directory.CreateDirectory(coordinator.Locations.PreviewsDirectory);
        var oldFile = Path.Combine(coordinator.Locations.PreviewsDirectory, "old.bin");
        File.WriteAllText(oldFile, "old");
        var catalogPath = coordinator.Locations.CatalogDatabasePath;
        var destination = Path.Combine(_root, "empty-previews");

        var result = await coordinator.RelocatePreviewsAsync(destination, PreviewRelocationMode.SwitchAndRebuild);

        Assert.True(result.Succeeded);
        Assert.True(File.Exists(oldFile));
        Assert.Equal(catalogPath, coordinator.Locations.CatalogDatabasePath);
        Assert.Equal(Path.GetFullPath(destination), coordinator.Locations.PreviewsDirectory);
        await coordinator.DisposeAsync();
    }

    [Fact]
    public async Task PreviewFailure_DoesNotChangeCatalogOrPreviewConfiguration()
    {
        var coordinator = (await LightflowStorageCoordinator.StartAsync(_root)).Coordinator!;
        var catalogPath = coordinator.Locations.CatalogDatabasePath;
        var previewPath = coordinator.Locations.PreviewsDirectory;
        var destination = Path.Combine(_root, "occupied-previews");
        Directory.CreateDirectory(destination);
        File.WriteAllText(Path.Combine(destination, "unrelated.bin"), "keep");

        var result = await coordinator.RelocatePreviewsAsync(destination, PreviewRelocationMode.SwitchAndRebuild);

        Assert.False(result.Succeeded);
        Assert.Equal(catalogPath, coordinator.Locations.CatalogDatabasePath);
        Assert.Equal(previewPath, coordinator.Locations.PreviewsDirectory);
        Assert.True(File.Exists(Path.Combine(destination, "unrelated.bin")));
        await coordinator.DisposeAsync();
    }

    [Theory]
    [InlineData(@"\\server\share\catalog")]
    [InlineData(@"\\?\UNC\server\share\catalog")]
    public async Task CatalogRelocation_RejectsNetworkLocations(string path)
    {
        var coordinator = (await LightflowStorageCoordinator.StartAsync(_root)).Coordinator!;
        var original = coordinator.Locations.CatalogDirectory;

        var result = await coordinator.RelocateCatalogAsync(path);

        Assert.Equal(StorageChangeStatus.InvalidDestination, result.Status);
        Assert.Equal(original, coordinator.Locations.CatalogDirectory);
        await coordinator.DisposeAsync();
    }

    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync()
    {
        try { Directory.Delete(_root, true); } catch { }
        return Task.CompletedTask;
    }

    private sealed class FailingTransfer : ICatalogRelocationTransfer
    {
        public void Backup(string sourceDatabasePath, string destinationDatabasePath) =>
            throw new IOException("simulated backup failure");
    }

    private sealed class InvalidCatalogTransfer : ICatalogRelocationTransfer
    {
        public void Backup(string sourceDatabasePath, string destinationDatabasePath) =>
            File.WriteAllText(destinationDatabasePath, "not a sqlite database");
    }

    private sealed class FailingActivator : ICatalogSessionActivator
    {
        public CatalogDatabaseSession Activate(CatalogDatabaseSession session) =>
            throw new IOException("simulated destination activation failure");
    }

    private sealed class ControllableConfigurationStore(string path) : IStorageConfigurationStore
    {
        public bool FailWrites { get; set; }
        public Action<AppSettings>? OnSave { get; set; }
        public AppSettings? LastSaved { get; private set; }
        public bool TryLoad(out AppSettings settings, out string? diagnostic) =>
            AppSettingsStore.TryLoadForStartup(path, out settings, out diagnostic);
        public void Save(AppSettings settings)
        {
            if (FailWrites) throw new IOException("simulated configuration write failure");
            OnSave?.Invoke(settings);
            AppSettingsStore.Save(path, settings);
            LastSaved = settings;
        }
    }
}
