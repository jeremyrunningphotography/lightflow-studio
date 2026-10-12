using LightflowStudio;
using Xunit;
using Lightflow.Application;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace LightflowStudio.Tests;

public sealed class CatalogSelectionTests
{
    private readonly string _root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "artifacts", "lf012-selection", Guid.NewGuid().ToString("N")));
    private LightflowStorageLocations Profile => ApplicationDataProfile.Resolve(["--data-root", _root]);

    [Fact]
    public async Task ReceivingSelection_PreservesRootAssetAndLutIdentity_WithoutGuessingMachinePathsOrNames()
    {
        var origin = ApplicationDataProfile.Resolve(["--data-root", Path.Combine(_root, "origin")]);
        var media = Directory.CreateDirectory(Path.Combine(origin.ApplicationDataDirectory, "media")).FullName;
        await File.WriteAllTextAsync(Path.Combine(media, "clip.mp4"), "task-owned synthetic source bytes");
        var luts = Directory.CreateDirectory(Path.Combine(origin.ApplicationDataDirectory, "luts")).FullName;
        var cube = "LUT_3D_SIZE 2\n0 0 0\n1 0 0\n0 1 0\n1 1 0\n0 0 1\n1 0 1\n0 1 1\n1 1 1\n";
        await File.WriteAllTextAsync(Path.Combine(luts, "Camera.cube"), cube);
        Guid id, rootId, assetId, lutId;
        await using (var source = (await LightflowStorageCoordinator.StartAsync(profile: origin)).Coordinator!)
        {
            id = source.CatalogSession.Identity.CatalogId;
            rootId = (await source.MediaRoots.CreateAsync("Portable Root", media)).Root!.RootId;
            assetId = (await source.MediaAssets.CreateAsync(rootId, "clip.mp4", "video")).Asset!.Asset.AssetId;
            lutId = Assert.Single((await source.LutCache.RefreshAsync(ColorLutStage.Camera, luts)).Resources).LutId;
            await source.AssetColors.SetStageAsync([assetId], ColorLutStage.Camera, lutId);
        }
        var selected = await LightflowStorageCoordinator.StartAsync(profile: Profile, allowFirstCreation: false,
            existingCatalogDirectory: origin.CatalogDirectory, selectedCatalogId: id);
        Assert.True(selected.IsReady, selected.Diagnostic);
        await using var owner = selected.Coordinator!;
        var unmapped = Assert.Single(await owner.MediaRoots.ListAsync());
        Assert.Equal(rootId, unmapped.RootId); Assert.Equal(MediaRootAvailability.Unmapped, unmapped.Availability);
        Assert.Null(unmapped.PhysicalPath);
        var asset = await owner.MediaAssets.GetAsync(assetId);
        Assert.Equal(assetId, asset!.Asset.AssetId); Assert.Equal(rootId, asset.Asset.RootId);
        var missing = await owner.AssetColors.GetAsync(assetId);
        Assert.Equal(lutId, missing.Camera!.LutId); Assert.Equal(LutResourceAvailability.Missing, missing.Camera.Availability);
        Assert.Null(missing.Creative);
        var receivingMedia = Directory.CreateDirectory(Path.Combine(_root, "receiving-media")).FullName;
        await File.WriteAllTextAsync(Path.Combine(receivingMedia, "clip.mp4"), "task-owned synthetic source bytes");
        Assert.True((await owner.MediaRoots.RemapAsync(rootId, receivingMedia)).Succeeded);
        Assert.Equal(assetId, (await owner.MediaAssets.GetAsync(assetId))!.Asset.AssetId);
        var receivingLuts = Directory.CreateDirectory(Path.Combine(_root, "receiving-luts")).FullName;
        await File.WriteAllTextAsync(Path.Combine(receivingLuts, "Renamed.cube"), cube);
        var identical = Assert.Single((await owner.LutCache.RefreshAsync(ColorLutStage.Camera, receivingLuts)).Resources);
        Assert.Equal(lutId, identical.LutId);
        Assert.Equal(LutResourceAvailability.Available, (await owner.AssetColors.GetAsync(assetId)).Camera!.Availability);
        var creativeLuts = Directory.CreateDirectory(Path.Combine(_root, "creative-luts")).FullName;
        await File.WriteAllTextAsync(Path.Combine(creativeLuts, "Renamed.cube"), cube.Replace("1 1 1", "0.9 0.9 0.9"));
        var different = Assert.Single((await owner.LutCache.RefreshAsync(ColorLutStage.Creative, creativeLuts)).Resources);
        Assert.NotEqual(lutId, different.LutId);
        Assert.Null((await owner.AssetColors.GetAsync(assetId)).Creative);
        using var sql = owner.CatalogSession.OpenConnection(); using var mapping = sql.CreateCommand();
        mapping.CommandText = "SELECT count(*) FROM MediaRootMappings WHERE RootId=$id;";
        mapping.Parameters.AddWithValue("$id", rootId.ToString("D"));
        Assert.Equal(2L, mapping.ExecuteScalar()); // Original mapping retained alongside explicit receiving mapping.
    }

    [Fact]
    public async Task ReceivingProfile_RequiresExplicitChoice_AndNeverCreatesMissingSelection()
    {
        var first = await LightflowStorageCoordinator.StartAsync(profile: Profile, allowFirstCreation: false);
        Assert.Equal(StorageStartupStatus.FirstRunChoiceRequired, first.Status);
        await first.Coordinator!.DisposeAsync();
        Assert.False(File.Exists(Profile.CatalogDatabasePath));
        Assert.False(File.Exists(Profile.SettingsPath));
        var nonexistent = Path.Combine(_root, "missing-catalog");
        var selected = await LightflowStorageCoordinator.StartAsync(profile: Profile, allowFirstCreation: false,
            existingCatalogDirectory: nonexistent, selectedCatalogId: Guid.NewGuid());
        await selected.Coordinator!.DisposeAsync();
        Assert.Equal(StorageStartupStatus.FirstRunChoiceRequired, selected.Status);
        Assert.False(Directory.Exists(nonexistent));
        Assert.False(File.Exists(Profile.CatalogDatabasePath));
    }

    [Fact]
    public async Task RestartSelection_PreservesBothCatalogsAndAdoptsExplicitIdentity_WithLeaseBeforeActivation()
    {
        var initial = (await LightflowStorageCoordinator.StartAsync(profile: Profile)).Coordinator!;
        var sourceId = initial.CatalogSession.Identity.CatalogId;
        var sourceCollection = await initial.Collections.CreateCollectionAsync("Source Collection");
        var sourcePath = initial.Locations.CatalogDirectory;
        var candidateProfile = ApplicationDataProfile.Resolve(["--data-root", Path.Combine(_root, "candidate")]);
        Guid candidateId;
        MediaCollection candidateCollection;
        await using (var candidate = (await LightflowStorageCoordinator.StartAsync(profile: candidateProfile)).Coordinator!)
        {
            candidateId = candidate.CatalogSession.Identity.CatalogId;
            candidateCollection = await candidate.Collections.CreateCollectionAsync("Candidate Collection");
        }
        var selected = await initial.InspectCatalogSelectionAsync(candidateProfile.CatalogDirectory);
        Assert.True(selected.Succeeded, selected.Diagnostic);
        Assert.Equal(candidateId, selected.CatalogId);
        Assert.Equal(sourceId, initial.Settings.CatalogId);
        var stale = initial.CatalogSession;
        await initial.DisposeAsync();
        var activator = new LeaseCheckingActivator(Profile, sourceId);
        var activated = await LightflowStorageCoordinator.StartAsync(profile: Profile,
            existingCatalogDirectory: selected.Directory, selectedCatalogId: selected.CatalogId, activator: activator);
        Assert.True(activated.IsReady, activated.Diagnostic);
        await using (var owner = activated.Coordinator!)
        {
            Assert.True(activator.Called);
            Assert.Equal(candidateId, owner.CatalogSession.Identity.CatalogId);
            Assert.Equal(candidateId, owner.Settings.CatalogId);
            Assert.NotNull(await owner.Collections.GetCollectionAsync(candidateCollection.CollectionId));
            Assert.Null(await owner.Collections.GetCollectionAsync(sourceCollection.CollectionId));
            Assert.Throws<ObjectDisposedException>(() => stale.OpenConnection());
            Assert.Throws<IOException>(() => StartupStoreEvidence.Begin(owner.CatalogSession.DatabasePath, "Catalog", false));
        }
        var returned = await LightflowStorageCoordinator.StartAsync(profile: Profile,
            existingCatalogDirectory: sourcePath, selectedCatalogId: sourceId);
        Assert.True(returned.IsReady, returned.Diagnostic);
        await using var original = returned.Coordinator!;
        Assert.NotNull(await original.Collections.GetCollectionAsync(sourceCollection.CollectionId));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("foreign")]
    [InlineData("incomplete")]
    [InlineData("identity")]
    public async Task InvalidSelection_RetainsPreviousConfigurationAndAuthoredData(string kind)
    {
        Guid id;
        MediaCollection authored;
        await using (var source = (await LightflowStorageCoordinator.StartAsync(profile: Profile)).Coordinator!)
        { id = source.CatalogSession.Identity.CatalogId; authored = await source.Collections.CreateCollectionAsync("Retained"); }
        var candidateProfile = ApplicationDataProfile.Resolve(["--data-root", Path.Combine(_root, "invalid")]);
        var selectedId = Guid.NewGuid();
        if (kind != "missing") Directory.CreateDirectory(candidateProfile.CatalogDirectory);
        if (kind == "foreign") await File.WriteAllTextAsync(candidateProfile.CatalogDatabasePath, "not a Catalog");
        if (kind == "incomplete") await File.WriteAllTextAsync(Path.Combine(candidateProfile.CatalogDirectory, "pending.json"), "{}");
        if (kind == "identity")
            await using (var candidate = (await LightflowStorageCoordinator.StartAsync(profile: candidateProfile)).Coordinator!)
                Assert.NotEqual(selectedId, candidate.CatalogSession.Identity.CatalogId);
        var result = await LightflowStorageCoordinator.StartAsync(profile: Profile,
            existingCatalogDirectory: candidateProfile.CatalogDirectory, selectedCatalogId: selectedId);
        Assert.True(result.IsReady, result.Diagnostic); // Proven previous owner, not candidate success.
        Assert.Contains("not opened", result.Diagnostic!);
        await using var previous = result.Coordinator!;
        Assert.Equal(id, previous.Settings.CatalogId);
        Assert.Equal(id, previous.CatalogSession.Identity.CatalogId);
        Assert.NotNull(await previous.Collections.GetCollectionAsync(authored.CollectionId));
        if (kind is "missing" or "incomplete") Assert.False(File.Exists(candidateProfile.CatalogDatabasePath));
    }

    [Fact]
    public async Task PreviouslyMovedCatalog_WithNewEdits_CanBeSelectedAgain()
    {
        Guid id;
        MediaCollection authored;
        var moved = Path.Combine(_root, "moved");
        await using (var source = (await LightflowStorageCoordinator.StartAsync(profile: Profile)).Coordinator!)
        {
            id = source.CatalogSession.Identity.CatalogId;
            Assert.True((await source.RelocateCatalogAsync(moved)).Succeeded);
            authored = await source.Collections.CreateCollectionAsync("Edits after transfer snapshot");
        }
        var reopened = await LightflowStorageCoordinator.StartAsync(profile: Profile,
            existingCatalogDirectory: moved, selectedCatalogId: id);
        Assert.True(reopened.IsReady, reopened.Diagnostic);
        await using var owner = reopened.Coordinator!;
        Assert.Null(owner.RecoveryDiagnostic);
        Assert.NotNull(await owner.Collections.GetCollectionAsync(authored.CollectionId));
        Assert.True(File.Exists(Path.Combine(moved, "manifest.json")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NeverOpenedClosedUnit_VerifiesBeforeSQLite_AndFailedCheckCannotPromoteEvidence(bool tamper)
    {
        Guid id;
        int schema;
        await using (var source = (await LightflowStorageCoordinator.StartAsync(profile: Profile)).Coordinator!)
        {
            id = source.CatalogSession.Identity.CatalogId; schema = source.CatalogSession.SchemaVersion;
            await source.Collections.CreateCollectionAsync("Closed unit authored Collection");
        }
        var incoming = Path.Combine(_root, "incoming"); Directory.CreateDirectory(incoming);
        var request = new ClosedCatalogTransferRequest(Guid.NewGuid(), 0, ClosedCatalogTransferMode.MoveAndActivate,
            id, schema, CatalogDatabaseService.SqliteApplicationId);
        var manifest = await new SqliteCatalogRecoveryService(Profile).FinalizeTransferSnapshotAsync(request, incoming, default);
        var ready = new ClosedCatalogReadiness(Version: ClosedCatalogManifestFormat.ReadinessVersion, OperationId: request.OperationId,
            Generation: 0, CatalogId: id, ManifestDigest: ClosedCatalogManifestFormat.Digest(manifest), ArtifactCount: manifest.Artifacts.Count);
        WindowsClosedCatalogTransferOperation.WriteExclusive(Path.Combine(incoming, "pending.json"), JsonSerializer.SerializeToUtf8Bytes(request));
        WindowsClosedCatalogTransferOperation.WriteExclusive(Path.Combine(incoming, "manifest.json"), ClosedCatalogManifestFormat.Serialize(manifest));
        WindowsClosedCatalogTransferOperation.WriteExclusive(Path.Combine(incoming, "ready.json"), JsonSerializer.SerializeToUtf8Bytes(ready));
        if (tamper)
        {
            using var sql = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Combine(incoming, LightflowStorageLocations.CatalogFileName), Mode = SqliteOpenMode.ReadWrite, Pooling = false }.ToString());
            sql.Open(); using var command = sql.CreateCommand(); command.CommandText = "CREATE TABLE SyntheticUnverifiedChange(Value TEXT); INSERT INTO SyntheticUnverifiedChange VALUES('changed closed bytes');"; command.ExecuteNonQuery();
        }
        await using (var previous = (await LightflowStorageCoordinator.StartAsync(profile: Profile)).Coordinator!)
        {
            var inspected = await previous.InspectCatalogSelectionAsync(incoming);
            Assert.Equal(!tamper, inspected.Succeeded);
            if (tamper)
            {
                Assert.Contains("verify", inspected.Diagnostic!, StringComparison.OrdinalIgnoreCase);
                var second = await previous.InspectCatalogSelectionAsync(incoming);
                Assert.False(second.Succeeded);
                Assert.Contains("verify", second.Diagnostic!, StringComparison.OrdinalIgnoreCase);
                Assert.Single(await previous.Collections.ListCollectionsAsync());
                return;
            }
        }
        var selected = await LightflowStorageCoordinator.StartAsync(profile: Profile,
            existingCatalogDirectory: incoming, selectedCatalogId: id);
        Assert.True(selected.IsReady, selected.Diagnostic);
        await using var opened = selected.Coordinator!;
        Assert.Null(opened.RecoveryDiagnostic);
        Assert.Equal(incoming, opened.Locations.CatalogDirectory);
        Assert.Single(await opened.Collections.ListCollectionsAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SelectedConfigurationFailure_ProvesRollbackOrBlocksAllAuthoring(bool rollbackFails)
    {
        Guid priorId;
        await using (var source = (await LightflowStorageCoordinator.StartAsync(profile: Profile)).Coordinator!)
        { priorId = source.CatalogSession.Identity.CatalogId; await source.Collections.CreateCollectionAsync("Keep previous"); }
        var candidateProfile = ApplicationDataProfile.Resolve(["--data-root", Path.Combine(_root, "candidate")]);
        Guid candidateId;
        await using (var candidate = (await LightflowStorageCoordinator.StartAsync(profile: candidateProfile)).Coordinator!)
        { candidateId = candidate.CatalogSession.Identity.CatalogId; await candidate.Collections.CreateCollectionAsync("Keep candidate"); }
        var configuration = new CommitFault(Profile, candidateId, rollbackFails);
        var selected = await LightflowStorageCoordinator.StartAsync(profile: Profile, configuration: configuration,
            existingCatalogDirectory: candidateProfile.CatalogDirectory, selectedCatalogId: candidateId);
        if (rollbackFails)
        {
            Assert.Equal(StorageStartupStatus.InvalidConfiguration, selected.Status);
            Assert.Equal(CatalogRecoveryReason.PendingRecovery, selected.RecoveryReason);
            Assert.Null(selected.Coordinator);
            Assert.Contains("Recovery required", selected.Diagnostic!);
            await using var gate = new CatalogStartupGate(selected, Profile);
            await gate.OpenAsync(candidateProfile.CatalogDirectory);
            await gate.CreateAsync(Path.Combine(_root, "cannot-bypass-rollback"));
            Assert.False(gate.CanEnterWorkspace);
            Assert.Equal(CatalogRecoveryReason.PendingRecovery, gate.Current.RecoveryReason);
            Assert.False(Directory.Exists(Path.Combine(_root, "cannot-bypass-rollback")));
        }
        else
        {
            Assert.True(selected.IsReady, selected.Diagnostic);
            await using var proven = selected.Coordinator!;
            Assert.Equal(priorId, proven.CatalogSession.Identity.CatalogId);
            Assert.Single(await proven.Collections.ListCollectionsAsync());
            Assert.Throws<IOException>(() => StartupStoreEvidence.Begin(proven.CatalogSession.DatabasePath, "Catalog", false));
        }
        using var released = StartupStoreEvidence.Begin(candidateProfile.CatalogDatabasePath, "Catalog", false);
        Assert.NotNull(released);
        Assert.True(File.Exists(Profile.CatalogDatabasePath)); Assert.True(File.Exists(candidateProfile.CatalogDatabasePath));
    }

    private sealed class CommitFault(LightflowStorageLocations profile, Guid candidate, bool rollbackFails) : IStorageConfigurationStore
    {
        public bool TryLoad(out AppSettings settings, out string? diagnostic) => AppSettingsStore.TryLoadForStartup(profile.SettingsPath, out settings, out diagnostic, isolated: true);
        public void Save(AppSettings settings)
        {
            if (settings.CatalogId != candidate && rollbackFails) throw new IOException("Injected rollback save failure");
            AppSettingsStore.Save(profile.SettingsPath, settings, isolated: true);
            if (settings.CatalogId == candidate) throw new IOException("Injected failure after candidate save");
        }
    }

    private sealed class LeaseCheckingActivator(LightflowStorageLocations profile, Guid prior) : ICatalogSessionActivator
    {
        public bool Called;
        public CatalogDatabaseSession Activate(CatalogDatabaseSession session)
        {
            Assert.True(AppSettingsStore.TryLoadForStartup(profile.SettingsPath, out var settings, out _, isolated: true));
            Assert.Equal(prior, settings.CatalogId);
            Assert.Throws<IOException>(() => StartupStoreEvidence.Begin(session.DatabasePath, "Catalog", false));
            Called = true;
            return session;
        }
    }
}
