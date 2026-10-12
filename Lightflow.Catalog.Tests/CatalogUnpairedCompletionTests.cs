using System.Diagnostics;
using System.Security.Cryptography;
using Lightflow.Application;
using LightflowStudio;
using Xunit;
using Xunit.Abstractions;

namespace Lightflow.Catalog.Tests;

public sealed class CatalogUnpairedCompletionTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "lf019-unpaired-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task UnpairedShutdownRetainsDirtyProfileAndEveryReopenValidatesBeforeSessionPublication()
    {
        var locations = NeutralCatalogLocations.Create(_root);
        var effects = new FixtureEvidenceEffects();
        Guid identity = Guid.Empty;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var lines = new List<string>();
            using var diagnostics = new StartupDiagnostics(lines.Add);
            var host = new UnpairedConsumers();
            var lifecycle = new CatalogLifecycle(host)
            {
                ProfileCompletion = new StartupSessionCompletion(effects, _root),
            };
            var first = attempt == 0;
            lifecycle.CatalogEvidence = StartupStoreEvidence.Begin(effects, locations.CatalogDatabasePath,
                "Catalog", createDirectory: first, lifecycle.ProfileCompletion)!;
            Assert.False(lifecycle.CatalogEvidence.KnownClean);
            CatalogLifecycle.RequireCompletePeers(lifecycle.CatalogEvidence, null);
            var timer = Stopwatch.StartNew();
            var database = new CatalogDatabaseService(locations)
            {
                CleanStartup = lifecycle.CatalogEvidence.KnownClean,
                ValidationReason = lifecycle.CatalogEvidence.Reason,
                ExpectedCatalogId = first ? null : identity,
            };
            var opened = first ? await database.CreateNewAsync() : await database.OpenExistingAsync();
            output.WriteLine($"attempt={attempt}; stage={(first ? "create" : "deep-reopen")}; elapsedMs={timer.Elapsed.TotalMilliseconds:F3}");
            Assert.True(opened.IsSuccess, opened.Diagnostic);
            if (first) identity = opened.Session!.Identity.CatalogId;
            else
            {
                Assert.Equal(identity, opened.Session!.Identity.CatalogId);
                Assert.Contains(lines, line => line.Contains("Catalog deep validation reason="));
                Assert.DoesNotContain(lines, line => line.Contains("fast structural reads succeeded"));
            }
            lifecycle.CatalogEvidence.MarkValidated();
            lifecycle.Session = opened.Session;
            // Stale consumers must not be able to reopen this session after disposal.
            var stale = opened.Session!;
            timer.Restart();
            await lifecycle.DisposeAsync();
            output.WriteLine($"attempt={attempt}; stage=shutdown; elapsedMs={timer.Elapsed.TotalMilliseconds:F3}");
            Assert.Equal("Dirty", File.ReadAllText(StartupSessionCompletion.PathFor(_root)));
            Assert.Contains("Validated", File.ReadAllText(StartupStoreEvidence.JournalPath(locations.CatalogDatabasePath)));
            Assert.DoesNotContain("Clean", File.ReadAllText(StartupStoreEvidence.JournalPath(locations.CatalogDatabasePath)));
            Assert.Equal(0, host.PeerCompletionAttempts);
            Assert.Equal(new[] { "cancel-reads", "dispose-caches", "stop-monitoring", "stop-derived", "peer-maintenance", "close-peer", "dispose-reads", "release-peer" }, host.Effects);
            Assert.Throws<ObjectDisposedException>(() => stale.OpenConnection());
            Assert.False(Directory.Exists(locations.PreviewsDirectory));
            using var released = StartupStoreEvidence.Begin(effects, locations.CatalogDatabasePath, "Catalog", false);
            Assert.NotNull(released);
        }
    }

    [Fact]
    public async Task MissingCatalogUnpairedOpenDoesNotCreateReplacementStorage()
    {
        var locations = NeutralCatalogLocations.Create(_root);
        var result = await new CatalogDatabaseService(locations).OpenExistingAsync();
        Assert.False(result.IsSuccess);
        Assert.Null(result.Session);
        Assert.False(Directory.Exists(locations.CatalogDirectory));
        Assert.False(Directory.Exists(locations.PreviewsDirectory));
    }

    // Test-only managed effects exercise the protocol; they do not qualify Mac native locking or durability.
    private sealed class FixtureEvidenceEffects : ICatalogEvidenceEffects
    {
        public FileStream OpenOwnershipJournal(string path) => new(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        public FileStream OpenProfileSession(string path) => new(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        public void RequireBoundOwnershipJournal(FileStream file, string path)
        {
            if (file.SafeFileHandle.IsClosed || file.Name != Path.GetFullPath(path)) throw new IOException("Fixture lease lost.");
        }
        public void RequireSingleRegularFile(FileStream file) { }
        public void RequireUnlinked(string path) { }
        public bool SameDatabase(string boundDatabase, string candidateDatabase) => boundDatabase == Path.GetFullPath(candidateDatabase);
        public string Fingerprint(FileStream database) => Convert.ToHexString(SHA256.HashData(database));
    }

    private sealed class UnpairedConsumers : ICatalogHostConsumerEffects
    {
        public List<string> Effects { get; } = [];
        public int PeerCompletionAttempts { get; private set; }
        public bool HasQualifiedCompletionPeer => false;
        public bool CompletionPeerCompleted => false;
        public void CancelReadUsage() => Effects.Add("cancel-reads");
        public void DisposeCaches() => Effects.Add("dispose-caches");
        public Task StopMonitoringAsync() { Effects.Add("stop-monitoring"); return Task.CompletedTask; }
        public Task StopDerivedWorkAsync() { Effects.Add("stop-derived"); return Task.CompletedTask; }
        public Task RestartAsync() => Task.CompletedTask;
        public Task<IDisposable> EnterCompletionPeerMaintenanceAsync() { Effects.Add("peer-maintenance"); return Task.FromResult<IDisposable>(new EmptyLease()); }
        public Task CloseCompletionPeerAsync() { Effects.Add("close-peer"); return Task.CompletedTask; }
        public void CompleteCompletionPeer() { PeerCompletionAttempts++; throw new InvalidOperationException("No qualified peer."); }
        public void ReleaseCompletionPeerEvidence() => Effects.Add("release-peer");
        public void DisposeReadUsage() => Effects.Add("dispose-reads");
        private sealed class EmptyLease : IDisposable { public void Dispose() { } }
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
