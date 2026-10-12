using LightflowStudio;
using Lightflow.Domain;
using Lightflow.Application;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class CatalogLeaseTransitionTests
{
    [Fact]
    public async Task ReconnectedRecovery_AcquiresSameLeaseBeforeRestore_RefusesAnotherOwner()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "artifacts", "lf012-recovery-lease", Guid.NewGuid().ToString("N")));
        var profile = ApplicationDataProfile.Resolve(["--data-root", Path.Combine(root, "inner")]);
        var destination = Path.Combine(profile.ApplicationDataDirectory, "moved");
        Guid id; string backupPath;
        await using (var source = (await LightflowStorageCoordinator.StartAsync(profile: profile)).Coordinator!)
        {
            id = source.CatalogSession.Identity.CatalogId;
            await source.Collections.CreateCollectionAsync("Retain across reconnected recovery");
            var backup = await new SqliteCatalogRecoveryService(profile).CreateBackupAsync(source.CatalogSession.DatabasePath, CatalogBackupKind.Recovery);
            Assert.True(backup.Succeeded, backup.Diagnostic); backupPath = backup.Backup!.Path;
            Assert.True((await source.RelocateCatalogAsync(destination)).Succeeded);
        }
        var offline = destination + "-disconnected";
        Directory.Move(destination, offline);
        var unavailableStartup = await LightflowStorageCoordinator.StartAsync(profile: profile, activator: new FaultingActivator(false));
        Assert.False(unavailableStartup.IsReady);
        await using var unavailable = unavailableStartup.Coordinator!;
        Assert.False(unavailable.CatalogAvailable);
        Directory.Move(offline, destination);
        var competingProfile = ApplicationDataProfile.Resolve(["--data-root", root]);
        var competingStartup = await LightflowStorageCoordinator.StartAsync(profile: competingProfile,
            existingCatalogDirectory: destination, selectedCatalogId: id);
        Assert.True(competingStartup.IsReady, competingStartup.Diagnostic);
        await using (var competing = competingStartup.Coordinator!)
        {
            var refused = await unavailable.RestoreCatalogAsync(backupPath);
            Assert.False(refused.Succeeded);
            Assert.Contains("ownership", refused.Diagnostic!, StringComparison.OrdinalIgnoreCase);
            Assert.False(unavailable.CatalogAvailable);
            Assert.Single(await competing.Collections.ListCollectionsAsync());
        }
        var restored = await unavailable.RestoreCatalogAsync(backupPath);
        Assert.True(restored.Succeeded, restored.Diagnostic);
        Assert.Equal(id, unavailable.CatalogSession.Identity.CatalogId);
        Assert.Single(await unavailable.Collections.ListCollectionsAsync());
        Assert.Throws<IOException>(() => StartupStoreEvidence.Begin(unavailable.CatalogSession.DatabasePath, "Catalog", false));
    }

    [Fact]
    public async Task UnprovableConfigurationRollback_BlocksCallbacksAndReleasesBothLeases()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "artifacts", "lf012-recovery-required", Guid.NewGuid().ToString("N")));
        var profile = ApplicationDataProfile.Resolve(["--data-root", root]);
        var configuration = new FaultingConfiguration(profile);
        await using var owner = (await LightflowStorageCoordinator.StartAsync(profile: profile, configuration: configuration)).Coordinator!;
        var source = owner.Locations.CatalogDatabasePath;
        await owner.Collections.CreateCollectionAsync("Retain under unresolved configuration");
        var destination = Path.Combine(root, "destination");
        configuration.FailDestination = destination; configuration.FailRollbackAfterCommit = true;
        var result = await owner.RelocateCatalogAsync(destination);
        Assert.False(result.Succeeded);
        Assert.Contains("RecoveryRequired", result.Diagnostic!);
        Assert.False(owner.CatalogAvailable);
        Assert.NotNull(owner.RecoveryDiagnostic);
        await Assert.ThrowsAsync<InvalidOperationException>(() => owner.Collections.CreateCollectionAsync("Blocked stale callback"));
        foreach (var database in new[] { source, Path.Combine(destination, LightflowStorageLocations.CatalogFileName) })
        {
            Assert.True(File.Exists(database));
            using var released = StartupStoreEvidence.Begin(database, "Catalog", false);
            Assert.NotNull(released);
        }
        Assert.NotNull(ClosedCatalogTransferJournal.InspectPending(profile));
    }
    [Theory]
    [InlineData("destination")]
    [InlineData("activation")]
    [InlineData("configuration")]
    public async Task FailedMove_ProvesSourceOwnerAndRetainsAuthoredData(string fault)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "artifacts", "lf012-lease-faults", Guid.NewGuid().ToString("N")));
        var profile = ApplicationDataProfile.Resolve(["--data-root", root]);
        var destination = Path.Combine(root, "destination");
        using var assessor = new DestinationContender(destination, fault == "destination");
        var configuration = new FaultingConfiguration(profile);
        var activator = new FaultingActivator(fault == "activation");
        await using var owner = (await LightflowStorageCoordinator.StartAsync(profile: profile,
            configuration: configuration, activator: activator, assessor: assessor)).Coordinator!;
        var source = owner.Locations.CatalogDatabasePath;
        var id = owner.CatalogSession.Identity.CatalogId;
        var collection = await owner.Collections.CreateCollectionAsync("Retain after failed move");
        if (fault == "configuration") configuration.FailDestination = destination;
        var result = await owner.RelocateCatalogAsync(destination);
        Assert.False(result.Succeeded);
        Assert.True(owner.CatalogAvailable, result.Diagnostic);
        Assert.Equal(source, owner.CatalogSession.DatabasePath);
        Assert.Equal(id, owner.CatalogSession.Identity.CatalogId);
        Assert.NotNull(await owner.Collections.GetCollectionAsync(collection.CollectionId));
        await owner.Collections.CreateCollectionAsync("Authoring after proven rollback");
        Assert.Throws<IOException>(() => StartupStoreEvidence.Begin(source, "Catalog", false));
        Assert.Throws<IOException>(() => File.Delete(StartupStoreEvidence.JournalPath(source)));
        Assert.True(AppSettingsStore.TryLoadForStartup(profile.SettingsPath, out var saved, out _, isolated: true));
        Assert.Equal(id, saved.CatalogId);
        if (fault != "destination")
        {
            using var released = StartupStoreEvidence.Begin(Path.Combine(destination, LightflowStorageLocations.CatalogFileName), "Catalog", false);
            Assert.NotNull(released);
        }
    }

    [Fact]
    public async Task SuccessfulMove_RejectsRetainedOldSessionAndReleasesSourceLease()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "artifacts", "lf012-stale-session", Guid.NewGuid().ToString("N")));
        var profile = ApplicationDataProfile.Resolve(["--data-root", root]);
        await using var owner = (await LightflowStorageCoordinator.StartAsync(profile: profile)).Coordinator!;
        var stale = owner.CatalogSession;
        Assert.True((await owner.RelocateCatalogAsync(Path.Combine(root, "moved"))).Succeeded);
        Assert.Throws<ObjectDisposedException>(() => stale.OpenConnection());
        using var released = StartupStoreEvidence.Begin(stale.DatabasePath, "Catalog", false);
        Assert.NotNull(released);
        Assert.Throws<IOException>(() => StartupStoreEvidence.Begin(owner.CatalogSession.DatabasePath, "Catalog", false));
    }

    private sealed class FaultingActivator(bool fail) : ICatalogSessionActivator
    {
        public CatalogDatabaseSession Activate(CatalogDatabaseSession session)
        {
            Assert.Throws<IOException>(() => StartupStoreEvidence.Begin(session.DatabasePath, "Catalog", false));
            if (fail) throw new IOException("Injected activation failure");
            return session;
        }
    }
    private sealed class FaultingConfiguration(LightflowStorageLocations profile) : IStorageConfigurationStore
    {
        public string? FailDestination;
        public bool FailRollbackAfterCommit;
        private bool _committed;
        public bool TryLoad(out AppSettings settings, out string? diagnostic) => AppSettingsStore.TryLoadForStartup(profile.SettingsPath, out settings, out diagnostic, isolated: true);
        public void Save(AppSettings settings)
        {
            if (_committed && FailRollbackAfterCommit) throw new IOException("Injected source configuration rollback failure");
            if (settings.CatalogDirectory == FailDestination && FailDestination is not null)
            {
                if (FailRollbackAfterCommit) { AppSettingsStore.Save(profile.SettingsPath, settings, isolated: true); _committed = true; }
                throw new IOException("Injected configuration failure");
            }
            AppSettingsStore.Save(profile.SettingsPath, settings, isolated: true);
        }
    }
    private sealed class DestinationContender(string destination, bool contend) : IStorageLocationAssessor, IDisposable
    {
        private readonly WindowsStorageLocationAssessor _native = new();
        private StartupStoreEvidence? _contender;
        public async Task<StorageLocationAssessment> AssessAsync(StorageAssessmentRequest request, CancellationToken cancellationToken = default)
        {
            var result = await _native.AssessAsync(request, cancellationToken);
            var database = Path.Combine(destination, LightflowStorageLocations.CatalogFileName);
            if (contend && request.RequestedLocation == destination && File.Exists(database) && _contender is null)
                _contender = StartupStoreEvidence.Begin(database, "Catalog", false);
            return result;
        }
        public void Dispose() { _contender?.Dispose(); _native.Dispose(); }
    }
}
