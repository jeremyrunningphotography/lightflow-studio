using System.Reflection;
using LightflowStudio;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class CatalogStartupGateTests
{
    [Fact]
    public async Task PreviousBackupReservationDoesNotQualifyAsFreshFirstLaunch()
    {
        var profile = Profile(); ApplicationDataProfile.Initialize(profile);
        AppSettingsStore.Save(profile.SettingsPath, new() { CatalogBackupDirectory = profile.CatalogBackupsDirectory }, isolated: true);
        var startup = await LightflowStorageCoordinator.StartAsync(profile: profile);
        Assert.False(startup.IsReady); Assert.Equal(CatalogRecoveryReason.LocationRefused, startup.RecoveryReason);
        Assert.False(File.Exists(profile.CatalogDatabasePath));
        if (startup.Coordinator is not null) await startup.Coordinator.DisposeAsync();
    }
    [Fact]
    public async Task AutomaticFreshStartupCreatesDefaultAndSecondLaunchReopensSameAuthoredCatalog()
    {
        var profile = Profile(); Guid id;
        var first = await LightflowStorageCoordinator.StartAsync(profile: profile);
        Assert.True(first.IsReady, first.Diagnostic);
        await using (var storage = first.Coordinator!)
        {
            Assert.Equal(profile.CatalogDirectory, storage.Locations.CatalogDirectory);
            id = storage.CatalogSession.Identity.CatalogId;
            await storage.Collections.CreateCollectionAsync("Automatic first launch");
            Assert.Throws<IOException>(() => StartupStoreEvidence.Begin(profile.CatalogDatabasePath, "Catalog", false));
        }
        var second = await LightflowStorageCoordinator.StartAsync(profile: profile);
        Assert.True(second.IsReady, second.Diagnostic);
        await using var reopened = second.Coordinator!;
        Assert.Equal(id, reopened.CatalogSession.Identity.CatalogId);
        Assert.Single(await reopened.Collections.ListCollectionsAsync());
        Assert.False(Directory.Exists(Path.Combine(profile.CatalogDirectory, "Catalog")));
    }
    [Theory]
    [InlineData("foreign.txt")]
    [InlineData("LightflowCatalog.db.startup-state")]
    public async Task AutomaticStartupRefusesOccupiedOrRecoveryReservedDefault(string name)
    {
        var profile = Profile(); ApplicationDataProfile.Initialize(profile);
        var evidence = Path.Combine(profile.CatalogDirectory, name);
        await File.WriteAllTextAsync(evidence, "Preserved evidence");
        var start = await LightflowStorageCoordinator.StartAsync(profile: profile);
        Assert.False(start.IsReady);
        Assert.Equal(CatalogRecoveryReason.LocationRefused, start.RecoveryReason);
        Assert.False(File.Exists(profile.CatalogDatabasePath));
        Assert.Equal("Preserved evidence", await File.ReadAllTextAsync(evidence));
        await using var gate = new CatalogStartupGate(start, profile);
        await gate.RetryAsync();
        Assert.False(gate.CanEnterWorkspace);
        Assert.Equal(CatalogRecoveryReason.LocationRefused, gate.Current.RecoveryReason);
        Assert.False(File.Exists(profile.CatalogDatabasePath));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExplicitCreationAndMoveUseExactlySelectedEmptyOrNewDirectory(bool existingEmpty)
    {
        var profile = Profile(); ApplicationDataProfile.Initialize(profile);
        var exact = Path.Combine(profile.ApplicationDataDirectory, "chosen", "Catalog");
        if (existingEmpty) Directory.CreateDirectory(exact);
        var created = await LightflowStorageCoordinator.CreateCatalogAsync(exact, profile);
        Assert.True(created.IsReady, created.Diagnostic);
        await using var storage = created.Coordinator!;
        var id = storage.CatalogSession.Identity.CatalogId;
        await storage.Collections.CreateCollectionAsync("Exact destination");
        Assert.Equal(exact, storage.Locations.CatalogDirectory);
        Assert.True(File.Exists(Path.Combine(exact, LightflowStorageLocations.CatalogFileName)));
        Assert.False(Directory.Exists(Path.Combine(exact, "Lightflow Catalog")));
        var moved = Path.Combine(profile.ApplicationDataDirectory, "moved", "Catalog");
        Directory.CreateDirectory(Path.GetDirectoryName(moved)!);
        var result = await storage.RelocateCatalogAsync(moved);
        Assert.True(result.Succeeded, result.Diagnostic);
        Assert.Equal(moved, storage.Locations.CatalogDirectory);
        Assert.Equal(id, storage.CatalogSession.Identity.CatalogId);
        Assert.Single(await storage.Collections.ListCollectionsAsync());
        Assert.False(Directory.Exists(Path.Combine(moved, "Catalog")));
        Assert.Throws<IOException>(() => StartupStoreEvidence.Begin(storage.CatalogSession.DatabasePath, "Catalog", false));
    }
    private static LightflowStorageLocations Profile()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "artifacts", "lf012-startup-gate-tests", Guid.NewGuid().ToString("N")));
        return ApplicationDataProfile.Resolve(["--data-root", root]);
    }
    [Fact]
    public async Task CreationDisabledPreflightRequiresExplicitChoiceAndVerifiedCommit()
    {
        var profile = Profile();
        var start = await LightflowStorageCoordinator.StartAsync(profile: profile, allowFirstCreation: false);
        await using var gate = new CatalogStartupGate(start, profile);
        Assert.Equal(StorageStartupStatus.FirstRunChoiceRequired, start.Status);
        Assert.False(gate.CanRetry); Assert.False(gate.CanEnterWorkspace);
        Assert.Equal(profile.CatalogDirectory, gate.ProposedNewDirectory);
        Assert.False(File.Exists(profile.CatalogDatabasePath));
        await gate.CreateAsync(gate.ProposedNewDirectory);
        Assert.True(gate.CanEnterWorkspace, gate.Current.Diagnostic);
        var ready = gate.TakeReady();
        await using var storage = ready.Coordinator!;
        Assert.True(storage.VerifyWorkspaceReadiness());
        Assert.True(AppSettingsStore.TryLoadForStartup(profile.SettingsPath, out var settings, out _, isolated: true));
        Assert.Equal(storage.CatalogSession.Identity.CatalogId, settings.CatalogId);
        Assert.Throws<IOException>(() => StartupStoreEvidence.Begin(storage.CatalogSession.DatabasePath, "Catalog", false));
    }
    [Fact]
    public async Task MissingPriorCatalogRetryThenReconnectPreservesAuthoredState()
    {
        var profile = Profile(); Guid id;
        var active = Path.Combine(profile.ApplicationDataDirectory, "active-catalog");
        await using (var storage = (await LightflowStorageCoordinator.StartAsync(profile: profile)).Coordinator!)
        {
            id = storage.CatalogSession.Identity.CatalogId; await storage.Collections.CreateCollectionAsync("Survives reconnect");
            Assert.True((await storage.RelocateCatalogAsync(active)).Succeeded);
        }
        var offline = active + "-offline";
        Directory.Move(active, offline);
        var start = await LightflowStorageCoordinator.StartAsync(profile: profile);
        await using var gate = new CatalogStartupGate(start, profile);
        Assert.False(gate.CanEnterWorkspace); Assert.True(gate.CanRetry);
        Assert.Equal(CatalogRecoveryReason.LocationUnavailable, start.RecoveryReason);
        Assert.Equal("Catalog unavailable", CatalogRecoveryPresentation.Create(start.Status, start.RecoveryReason, start.Diagnostic).Heading);
        await gate.RetryAsync(); Assert.False(gate.CanEnterWorkspace);
        Assert.False(File.Exists(Path.Combine(active, LightflowStorageLocations.CatalogFileName)));
        Directory.Move(offline, active);
        await gate.RetryAsync(); Assert.True(gate.CanEnterWorkspace, gate.Current.Diagnostic);
        await using var ready = gate.TakeReady().Coordinator!;
        Assert.Equal(id, ready.CatalogSession.Identity.CatalogId);
        Assert.Single(await ready.Collections.ListCollectionsAsync());
    }
    [Fact]
    public async Task ReservedMissingDefaultRequiresDistinctCreationAndFreshIdentity()
    {
        var profile = Profile(); var prior = Guid.NewGuid();
        ApplicationDataProfile.Initialize(profile);
        AppSettingsStore.Save(profile.SettingsPath, new() { CatalogId = prior, CatalogDirectory = profile.CatalogDirectory }, isolated: true);
        var refused = await LightflowStorageCoordinator.CreateCatalogAsync(profile.CatalogDirectory, profile);
        Assert.False(refused.IsReady); Assert.False(File.Exists(profile.CatalogDatabasePath));
        var start = await LightflowStorageCoordinator.StartAsync(profile: profile, allowFirstCreation: false);
        await using var gate = new CatalogStartupGate(start, profile);
        Assert.NotEqual(profile.CatalogDirectory, gate.ProposedNewDirectory);
        await gate.CreateAsync(gate.ProposedNewDirectory);
        Assert.True(gate.CanEnterWorkspace, gate.Current.Diagnostic);
        await using var ready = gate.TakeReady().Coordinator!;
        Assert.NotEqual(prior, ready.CatalogSession.Identity.CatalogId);
        Assert.False(File.Exists(profile.CatalogDatabasePath));
    }
    [Fact]
    public async Task OccupiedDefaultIsNeverOverwrittenAndIndependentCatalogCanBeCreated()
    {
        var profile = Profile(); ApplicationDataProfile.Initialize(profile);
        await File.WriteAllTextAsync(profile.CatalogDatabasePath, "Retained foreign bytes");
        var start = await LightflowStorageCoordinator.StartAsync(profile: profile, allowFirstCreation: false);
        await using var gate = new CatalogStartupGate(start, profile);
        Assert.False(gate.CanEnterWorkspace);
        await gate.CreateAsync(profile.CatalogDirectory); Assert.False(gate.CanEnterWorkspace);
        Assert.Equal("Retained foreign bytes", await File.ReadAllTextAsync(profile.CatalogDatabasePath));
        await gate.CreateAsync(gate.ProposedNewDirectory); Assert.True(gate.CanEnterWorkspace, gate.Current.Diagnostic);
        await using var ready = gate.TakeReady().Coordinator!;
        Assert.Equal("Retained foreign bytes", await File.ReadAllTextAsync(profile.CatalogDatabasePath));
    }
    [Fact]
    public async Task FailedSelectionRemainsAtGateThenOpenPreservesCandidateIdentityAndData()
    {
        var profile = Profile();
        var candidate = ApplicationDataProfile.Resolve(["--data-root", Path.Combine(profile.ApplicationDataDirectory, "incoming")]);
        Guid id;
        await using (var storage = (await LightflowStorageCoordinator.StartAsync(profile: candidate)).Coordinator!)
        { id = storage.CatalogSession.Identity.CatalogId; await storage.Collections.CreateCollectionAsync("Incoming authored data"); }
        var start = await LightflowStorageCoordinator.StartAsync(profile: profile, allowFirstCreation: false);
        await using var gate = new CatalogStartupGate(start, profile);
        var missing = Path.Combine(profile.ApplicationDataDirectory, "missing");
        await gate.OpenAsync(missing);
        Assert.False(gate.CanEnterWorkspace); Assert.False(File.Exists(profile.CatalogDatabasePath));
        Assert.Equal(missing, gate.DisplayCatalogDirectory);
        await gate.OpenAsync(candidate.CatalogDirectory); Assert.True(gate.CanEnterWorkspace, gate.Current.Diagnostic);
        Assert.Equal(candidate.CatalogDirectory, gate.DisplayCatalogDirectory);
        await using var ready = gate.TakeReady().Coordinator!;
        Assert.Equal(id, ready.CatalogSession.Identity.CatalogId); Assert.Single(await ready.Collections.ListCollectionsAsync());
        Assert.False(File.Exists(profile.CatalogDatabasePath));
    }
    [Fact]
    public async Task PendingJournalCannotBeBypassedByRetryOpenOrCreate()
    {
        var profile = Profile(); ApplicationDataProfile.Initialize(profile);
        var evidence = ClosedCatalogTransferJournal.DirectoryPath(profile);
        Directory.CreateDirectory(evidence);
        var journal = Path.Combine(evidence, Guid.NewGuid().ToString("N") + ".journal");
        await File.WriteAllTextAsync(journal, "Retained truncated recovery evidence");
        var prior = await File.ReadAllBytesAsync(journal);
        var start = await LightflowStorageCoordinator.StartAsync(profile: profile, allowFirstCreation: false);
        await using var gate = new CatalogStartupGate(start, profile);
        await gate.RetryAsync(); await gate.OpenAsync(profile.CatalogDirectory);
        await gate.CreateAsync(Path.Combine(profile.ApplicationDataDirectory, "new"));
        Assert.False(gate.CanEnterWorkspace); Assert.Equal(CatalogRecoveryReason.PendingRecovery, gate.Current.RecoveryReason);
        Assert.Equal(prior, await File.ReadAllBytesAsync(journal)); Assert.False(File.Exists(profile.CatalogDatabasePath));
        Assert.False(Directory.Exists(Path.Combine(profile.ApplicationDataDirectory, "new")));
        Assert.False((await LightflowStorageCoordinator.CreateCatalogAsync(Path.Combine(profile.ApplicationDataDirectory, "direct"), profile)).IsReady);
    }
    [Fact]
    public async Task ClosedGateRefusesLateOperationsAndReleasesCandidateLease()
    {
        var profile = Profile();
        var start = await LightflowStorageCoordinator.StartAsync(profile: profile, allowFirstCreation: false);
        var gate = new CatalogStartupGate(start, profile);
        await gate.CreateAsync(gate.ProposedNewDirectory); Assert.True(gate.CanEnterWorkspace);
        await gate.DisposeAsync();
        await gate.RetryAsync(); await gate.CreateAsync(Path.Combine(profile.ApplicationDataDirectory, "late"));
        Assert.False(gate.CanEnterWorkspace); Assert.Throws<InvalidOperationException>(() => gate.TakeReady());
        Assert.False(Directory.Exists(Path.Combine(profile.ApplicationDataDirectory, "late")));
        using var released = StartupStoreEvidence.Begin(profile.CatalogDatabasePath, "Catalog", false); Assert.NotNull(released);
    }
    [Fact]
    public async Task LostLeaseInvalidatesReadinessAndBlocksAuthoringBeforeAnyNewConnection()
    {
        var profile = Profile(); var ready = await LightflowStorageCoordinator.StartAsync(profile: profile);
        await using var storage = ready.Coordinator!;
        var lease = (StartupStoreEvidence)typeof(LightflowStorageCoordinator).GetField("_catalogStartup", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(storage)!;
        lease.Dispose();
        Assert.False(storage.VerifyWorkspaceReadiness()); Assert.False(storage.CatalogAvailable);
        await Assert.ThrowsAnyAsync<Exception>(() => storage.Collections.CreateCollectionAsync("Must refuse"));
    }
    [Fact]
    public async Task FailedActivationRetainsNewArtifactsAndPriorConfigurationWithoutWorkspace()
    {
        var profile = Profile(); ApplicationDataProfile.Initialize(profile);
        var path = Path.Combine(profile.ApplicationDataDirectory, "failed-creation");
        var result = await LightflowStorageCoordinator.CreateCatalogAsync(path, profile, activator: new RejectActivation());
        Assert.False(result.IsReady); Assert.Null(result.Coordinator);
        Assert.True(File.Exists(Path.Combine(path, LightflowStorageLocations.CatalogFileName)));
        Assert.True(AppSettingsStore.TryLoadForStartup(profile.SettingsPath, out var settings, out _, isolated: true));
        Assert.Null(settings.CatalogId); Assert.Null(settings.CatalogDirectory);
        using var released = StartupStoreEvidence.Begin(Path.Combine(path, LightflowStorageLocations.CatalogFileName), "Catalog", false); Assert.NotNull(released);
    }
    private sealed class RejectActivation : ICatalogSessionActivator
    {
        public CatalogDatabaseSession Activate(CatalogDatabaseSession session)
        { Assert.Throws<IOException>(() => StartupStoreEvidence.Begin(session.DatabasePath, "Catalog", false)); throw new IOException("Injected creation activation failure"); }
    }

    [Fact]
    public async Task CompetingWriterGateCannotOpenOrCreateOverTheBusyCatalog()
    {
        var profile = Profile();
        var inner = ApplicationDataProfile.Resolve(["--data-root", Path.Combine(profile.ApplicationDataDirectory, "writer")]);
        await using var writer = (await LightflowStorageCoordinator.StartAsync(profile: inner)).Coordinator!;
        await writer.Collections.CreateCollectionAsync("Held by first owner");
        ApplicationDataProfile.Initialize(profile);
        AppSettingsStore.Save(profile.SettingsPath, new() { CatalogDirectory = inner.CatalogDirectory, CatalogId = writer.CatalogSession.Identity.CatalogId }, isolated: true);
        var start = await LightflowStorageCoordinator.StartAsync(profile: profile, allowFirstCreation: false);
        await using var gate = new CatalogStartupGate(start, profile);
        Assert.Equal(CatalogRecoveryReason.CompetingOwner, start.RecoveryReason);
        await gate.RetryAsync(); await gate.OpenAsync(inner.CatalogDirectory); await gate.CreateAsync(inner.CatalogDirectory);
        Assert.False(gate.CanEnterWorkspace); Assert.Single(await writer.Collections.ListCollectionsAsync());
    }
    [Fact]
    public async Task UnsupportedCreationIsRefusedBeforeDatabaseAndConfiguration()
    {
        var profile = Profile(); ApplicationDataProfile.Initialize(profile);
        var result = await LightflowStorageCoordinator.CreateCatalogAsync(profile.CatalogDirectory, profile,
            assessor: new WindowsCatalogAdmissionTests.Facts("network-alias"));
        Assert.False(result.IsReady); Assert.Equal(CatalogRecoveryReason.UnsupportedStorage, result.RecoveryReason);
        Assert.False(File.Exists(profile.CatalogDatabasePath));
        if (result.Coordinator is not null) await result.Coordinator.DisposeAsync();
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CreationSaveFailureNeverPublishesWorkspaceAndReleasesLease(bool rollbackFails)
    {
        var profile = Profile(); ApplicationDataProfile.Initialize(profile);
        var path = Path.Combine(profile.ApplicationDataDirectory, "configuration-failure");
        var result = await LightflowStorageCoordinator.CreateCatalogAsync(path, profile,
            configuration: new CreationConfigurationFault(profile, path, rollbackFails));
        Assert.False(result.IsReady); Assert.Null(result.Coordinator);
        if (rollbackFails) Assert.Equal(CatalogRecoveryReason.PendingRecovery, result.RecoveryReason);
        else
        {
            Assert.True(AppSettingsStore.TryLoadForStartup(profile.SettingsPath, out var settings, out _, isolated: true));
            Assert.Null(settings.CatalogId); Assert.Null(settings.CatalogDirectory);
        }
        Assert.True(File.Exists(Path.Combine(path, LightflowStorageLocations.CatalogFileName)));
        using var lease = StartupStoreEvidence.Begin(Path.Combine(path, LightflowStorageLocations.CatalogFileName), "Catalog", false);
        Assert.NotNull(lease);
    }
    private sealed class CreationConfigurationFault(LightflowStorageLocations profile, string candidate, bool rollbackFails) : IStorageConfigurationStore
    {
        private bool _committed;
        public bool TryLoad(out AppSettings settings, out string? diagnostic) => AppSettingsStore.TryLoadForStartup(profile.SettingsPath, out settings, out diagnostic, isolated: true);
        public void Save(AppSettings settings)
        {
            if (_committed && rollbackFails) throw new IOException("Injected rollback failure");
            AppSettingsStore.Save(profile.SettingsPath, settings, isolated: true);
            if (settings.CatalogDirectory == candidate) { _committed = true; throw new IOException("Injected failure after creation configuration save"); }
        }
    }
}
