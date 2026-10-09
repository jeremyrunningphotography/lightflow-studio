using System.Security.Cryptography;
using Lightflow.Application;
using Lightflow.Domain;
using LightflowStudio;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class WindowsCatalogAdmissionTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"lightflow-admission-{Guid.NewGuid():N}");

    [Fact]
    public async Task NativeLocal_CreateAndReopenRemainSupported()
    {
        var start = await LightflowStorageCoordinator.StartAsync(_root);
        Assert.True(start.IsReady, start.Diagnostic);
        var id = start.Coordinator!.CatalogSession.Identity.CatalogId;
        await start.Coordinator.DisposeAsync();
        var reopen = await LightflowStorageCoordinator.StartAsync(_root);
        Assert.True(reopen.IsReady, reopen.Diagnostic);
        Assert.Equal(id, reopen.Coordinator!.CatalogSession.Identity.CatalogId);
        await reopen.Coordinator.DisposeAsync();
    }

    [Theory]
    [InlineData("unc")]
    [InlineData("mapped")]
    [InlineData("network-alias")]
    [InlineData("unavailable")]
    [InlineData("readonly")]
    [InlineData("unknown-filesystem")]
    [InlineData("unsupported-filesystem")]
    [InlineData("ambiguous")]
    [InlineData("unknown-locality")]
    public async Task RefusedExistingCatalog_PreservesBytesIdentityAndConfiguration(string scenario)
    {
        var locations = LightflowStorageLocations.Create(_root);
        var created = await new CatalogDatabaseService(locations).CreateNewAsync();
        Assert.True(created.IsSuccess);
        var id = created.Session!.Identity.CatalogId;
        var assetId = Guid.NewGuid();
        var rootId = Guid.NewGuid();
        using (var connection = created.Session.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                INSERT INTO MediaRoots (RootId,DisplayName,SourceStatus,CreatedUtc,UpdatedUtc) VALUES ($root,'Admission fixture','online',$now,$now);
                INSERT INTO MediaAssets (AssetId,RootId,RelativePath,RelativePathKey,MediaType,FileSizeBytes,LastWriteUtcTicks,SourceStatus,CreatedUtc,UpdatedUtc)
                VALUES ($asset,$root,'clip.mp4','CLIP.MP4','video',1,1,'available',$now,$now);
                """;
            command.Parameters.AddWithValue("$root", rootId.ToString("D"));
            command.Parameters.AddWithValue("$asset", assetId.ToString("D"));
            command.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O"));
            command.ExecuteNonQuery();
        }
        await new CatalogAssetClassificationStore(() => created.Session).SaveAsync(
            new(assetId, 4, AssetFlag.Picked, AssetColorLabel.Blue, ["precious authored fixture"]));
        await created.Session.DisposeAsync();
        var bytes = SHA256.HashData(await File.ReadAllBytesAsync(locations.CatalogDatabasePath));
        var store = new Configuration(new() { CatalogDirectory = locations.CatalogDirectory, CatalogId = id });
        var start = await LightflowStorageCoordinator.StartAsync(_root, configuration: store, assessor: new Facts(scenario));
        Assert.False(start.IsReady);
        Assert.NotNull(start.Coordinator);
        Assert.False(start.Coordinator!.CatalogAvailable);
        Assert.Equal(0, store.Saves);
        Assert.Equal(id, start.Coordinator.Settings.CatalogId);
        Assert.Equal(locations.CatalogDirectory, start.Coordinator.Locations.CatalogDirectory);
        Assert.Equal(bytes, SHA256.HashData(await File.ReadAllBytesAsync(locations.CatalogDatabasePath)));
        Assert.False(File.Exists(locations.CatalogDatabasePath + ".startup-state"));
        if (scenario is "unc" or "mapped" or "network-alias")
            Assert.Contains("Network locations are supported for media and Catalog backups", start.Diagnostic);
        await start.Coordinator.DisposeAsync();
        Assert.Equal(bytes, SHA256.HashData(await File.ReadAllBytesAsync(locations.CatalogDatabasePath)));
        var reopened = await new CatalogDatabaseService(locations).OpenExistingAsync();
        Assert.Equal(id, reopened.Session!.Identity.CatalogId);
        var authored = (await new CatalogAssetClassificationStore(() => reopened.Session).GetAsync([assetId]))[assetId];
        Assert.Equal(4, authored.Rating); Assert.Equal(AssetFlag.Picked, authored.Flag);
        Assert.Equal(AssetColorLabel.Blue, authored.ColorLabel); Assert.Equal(["precious authored fixture"], authored.Keywords);
        await reopened.Session.DisposeAsync();
    }

    [Fact]
    public async Task ChangedMountBeforeCreate_DoesNotCreateCatalogOrSaveConfiguration()
    {
        var store = new Configuration(new());
        var result = await LightflowStorageCoordinator.StartAsync(_root, configuration: store, assessor: new Facts("changed"));
        Assert.False(result.IsReady);
        Assert.Contains("mount changed", result.Diagnostic);
        Assert.False(File.Exists(LightflowStorageLocations.Create(_root).CatalogDatabasePath));
        Assert.Equal(0, store.Saves);
        await result.Coordinator!.DisposeAsync();
    }

    [Fact]
    public async Task CancellationDuringAssessment_DoesNotCreateCatalog()
    {
        using var cancellation = new CancellationTokenSource();
        var assessor = new Facts("local", () => cancellation.Cancel());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            LightflowStorageCoordinator.StartAsync(_root, assessor: assessor, cancellationToken: cancellation.Token));
        Assert.False(File.Exists(LightflowStorageLocations.Create(_root).CatalogDatabasePath));
    }

    [Theory]
    [InlineData("internal")]
    [InlineData("external")]
    public async Task QualifiedLocalFacts_DoNotDependOnTransport(string transport)
    {
        var result = await LightflowStorageCoordinator.StartAsync(_root, assessor: new Facts(transport));
        Assert.True(result.IsReady, result.Diagnostic);
        await result.Coordinator!.DisposeAsync();
    }

    [Fact]
    public async Task NativeJunction_ResolvesTargetAndPinsAliasUntilDisposed()
    {
        var target = Path.Combine(_root, "target");
        var alias = Path.Combine(_root, "alias");
        Directory.CreateDirectory(target);
        using (var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe")
        {
            Arguments = $"/c mklink /J \"{alias}\" \"{target}\"", UseShellExecute = false,
            CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        })!)
        {
            await process.WaitForExitAsync();
            Assert.Equal(0, process.ExitCode);
        }
        using (var assessor = new WindowsStorageLocationAssessor())
        {
            var request = new StorageAssessmentRequest(Guid.NewGuid(), 0, StorageRole.ActiveCatalog, StorageOperation.Open, alias);
            var first = await assessor.AssessAsync(request);
            Assert.Equal(StorageLocationDecision.Eligible, StorageLocationPolicy.Evaluate(request, first, DateTimeOffset.UtcNow).Decision);
            var resolved = await assessor.AssessAsync(request with { RequestedLocation = target });
            Assert.Equal(resolved.Identity!.CanonicalLocation, first.Identity!.CanonicalLocation);
            Assert.Throws<IOException>(() => Directory.Move(alias, alias + "-changed"));
            var current = await assessor.AssessAsync(request);
            Assert.Equal(StorageLocationDecision.Eligible, StorageLocationPolicy.Revalidate(request, first, current, DateTimeOffset.UtcNow).Decision);
        }
        Directory.Move(alias, alias + "-changed");
        Directory.Delete(alias + "-changed"); // Delete only the task-owned junction, preserving target.
        Assert.True(Directory.Exists(target));
    }

    [Fact]
    public async Task NativeCreationParent_BindsAndPinsNewDirectoryBeforeUse()
    {
        var future = Path.Combine(_root, "future", "catalog");
        var request = new StorageAssessmentRequest(Guid.NewGuid(), 0, StorageRole.ActiveCatalog, StorageOperation.Create, future);
        using (var assessor = new WindowsStorageLocationAssessor())
        {
            var first = await assessor.AssessAsync(request);
            Assert.False(Directory.Exists(future)); // Assessment itself never creates storage.
            Assert.Equal(StorageLocationDecision.Eligible, StorageLocationPolicy.Evaluate(request, first, DateTimeOffset.UtcNow).Decision);
            Directory.CreateDirectory(future);
            var current = await assessor.AssessAsync(request);
            Assert.Equal(StorageLocationDecision.Eligible, StorageLocationPolicy.Revalidate(request, first, current, DateTimeOffset.UtcNow).Decision);
            Assert.Throws<IOException>(() => Directory.Move(future, future + "-replaced"));
        }
        Directory.Move(future, future + "-replaced");
    }

    [Fact]
    public async Task RestoreAndRelocationRefusal_DoNotCloseWriterOrMutateCatalog()
    {
        var facts = new Facts("local");
        var started = await LightflowStorageCoordinator.StartAsync(_root, assessor: facts);
        Assert.True(started.IsReady, started.Diagnostic);
        var coordinator = started.Coordinator!;
        var session = coordinator.CatalogSession;
        var id = session.Identity.CatalogId;
        facts.Scenario = "network-alias";
        var destination = Path.Combine(_root, "refused-destination");
        var move = await coordinator.RelocateCatalogAsync(destination);
        Assert.False(move.Succeeded);
        Assert.False(Directory.Exists(destination));
        Assert.Same(session, coordinator.CatalogSession);
        var restore = await coordinator.RestoreCatalogAsync(Path.Combine(_root, "unused-backup.db"));
        Assert.False(restore.Succeeded);
        Assert.Contains("Network locations", restore.Diagnostic);
        Assert.Same(session, coordinator.CatalogSession);
        Assert.Equal(id, coordinator.CatalogSession.Identity.CatalogId);
        await coordinator.DisposeAsync();
    }

    private sealed class Configuration(AppSettings settings) : IStorageConfigurationStore
    {
        internal int Saves { get; private set; }
        public bool TryLoad(out AppSettings loaded, out string? diagnostic) { loaded = settings; diagnostic = null; return true; }
        public void Save(AppSettings saved) { settings = saved; Saves++; }
    }
    internal sealed class Facts(string scenario, Action? assessed = null) : IStorageLocationAssessor
    {
        private int _calls;
        internal string Scenario { get; set; } = scenario;
        public Task<StorageLocationAssessment> AssessAsync(StorageAssessmentRequest request, CancellationToken cancellationToken = default)
        {
            _calls++; assessed?.Invoke(); cancellationToken.ThrowIfCancellationRequested();
            var scenario = Scenario;
            var now = DateTimeOffset.UtcNow;
            var supported = StorageCapability.Supported;
            return Task.FromResult(new StorageLocationAssessment(request, Guid.NewGuid(), now, now.AddMinutes(1), StorageLocationPolicy.Version,
                StorageAssessmentStatus.Complete,
                new(request.RequestedLocation, "target", scenario == "changed" && _calls > 1 ? "new-mount" : "mount", "NTFS-instance"),
                scenario is "unc" or "mapped" or "network-alias" ? StorageLocality.Network : scenario == "unknown-locality" ? StorageLocality.Unknown : StorageLocality.Local,
                scenario == "unavailable" ? StorageAvailability.Unavailable : StorageAvailability.Available,
                StorageAvailability.Available, scenario == "ambiguous" ? StorageResolutionConfidence.Ambiguous : StorageResolutionConfidence.Resolved,
                StorageResolutionConfidence.Resolved,
                new(supported, scenario == "readonly" ? StorageCapability.Unsupported : supported,
                    scenario == "unknown-filesystem" ? StorageCapability.Unknown : scenario == "unsupported-filesystem" ? StorageCapability.Unsupported : supported,
                    supported, supported, supported)));
        }
    }
    public Task InitializeAsync() { Directory.CreateDirectory(_root); return Task.CompletedTask; }
    public Task DisposeAsync() { if (Directory.Exists(_root)) Directory.Delete(_root, true); return Task.CompletedTask; }
}
