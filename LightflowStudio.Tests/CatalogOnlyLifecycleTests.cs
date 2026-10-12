using System.Diagnostics;
using Lightflow.Application;
using Lightflow.Domain;
using Xunit;
using Xunit.Abstractions;

namespace LightflowStudio.Tests;

public sealed class CatalogOnlyLifecycleTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "lf019-catalog-only-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SharedStartupOwnsUnpairedCreateRepeatedDeepReopenAndLeaseRelease()
    {
        var profile = LightflowStorageLocations.Create(_root);
        Guid identity = Guid.Empty;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var messages = new List<string>();
            using var diagnostics = new StartupDiagnostics(messages.Add);
            var effects = new CatalogOnlyEffects(new LightflowStorageCoordinator.WindowsCatalogLifecycleEffects(profile: profile));
            var timer = Stopwatch.StartNew();
            var result = await new CatalogLifecycle(effects).StartAsync();
            output.WriteLine($"attempt={attempt}; stage={(attempt == 0 ? "create" : "deep-reopen")}; elapsedMs={timer.Elapsed.TotalMilliseconds:F3}");
            Assert.True(result.IsReady, result.Diagnostic);
            var lifecycle = result.Coordinator!;
            if (attempt == 0) identity = lifecycle.CatalogSession.Identity.CatalogId;
            else
            {
                Assert.Equal(identity, lifecycle.CatalogSession.Identity.CatalogId);
                Assert.Contains(messages, line => line.Contains("Catalog deep validation reason="));
                Assert.DoesNotContain(messages, line => line.Contains("fast structural reads succeeded"));
            }
            Assert.False(lifecycle.CatalogEvidence!.KnownClean);
            Assert.True(lifecycle.VerifyWorkspaceReadiness());
            var stale = lifecycle.CatalogSession;
            timer.Restart();
            await lifecycle.DisposeAsync();
            output.WriteLine($"attempt={attempt}; stage=shutdown; elapsedMs={timer.Elapsed.TotalMilliseconds:F3}");
            Assert.Throws<ObjectDisposedException>(() => stale.OpenConnection());
            Assert.Equal("Dirty", File.ReadAllText(StartupSessionCompletion.PathFor(profile.ApplicationDataDirectory)));
            Assert.Contains("Validated", File.ReadAllText(StartupStoreEvidence.JournalPath(profile.CatalogDatabasePath)));
            Assert.DoesNotContain("Clean", File.ReadAllText(StartupStoreEvidence.JournalPath(profile.CatalogDatabasePath)));
            Assert.False(File.Exists(profile.PreviewsDatabasePath));
            Assert.Equal(0, effects.PeerAttempts);
        }
    }

    // Exercises the shared lifecycle with real Windows native effects and no Preview peer.
    // This does not qualify native Mac effects or claim equivalent startup performance.
    private sealed class CatalogOnlyEffects(ICatalogLifecycleEffects native) : ICatalogLifecycleEffects
    {
        public int PeerAttempts { get; private set; }
        public ILightflowStorageLocations Profile => native.Profile;
        public ICatalogConfigurationEffects Configuration => native.Configuration;
        public ICatalogHostConsumerEffects Consumers => native.Consumers;
        public ICatalogEvidenceEffects Evidence => native.Evidence;
        public ICatalogRecoveryNativeEffects RecoveryNative => native.RecoveryNative;
        public ICatalogTransferNativeEffects TransferNative => native.TransferNative;
        public ICatalogRecoveryService? RecoveryOverride => native.RecoveryOverride;
        public ICatalogRelocationTransfer? RelocationTransfer => native.RelocationTransfer;
        public bool SupportsCompletionPeer => false;
        public void RequireInitializedProfile() => native.RequireInitializedProfile();
        public ILightflowStorageLocations WithOverrides(ILightflowStorageLocations configured, string? catalogDirectory, string? previewsDirectory)
            => native.WithOverrides(configured, catalogDirectory, previewsDirectory);
        public void GuardConfiguredLeaves(ILightflowStorageLocations defaults, ILightflowStorageLocations configured) => native.GuardConfiguredLeaves(defaults, configured);
        public CatalogStorageAdmission CreateAdmission(ILightflowStorageLocations locations, StorageOperation operation, string? reservedCatalogDirectory = null)
            => native.CreateAdmission(locations, operation, reservedCatalogDirectory);
        public bool SamePath(string left, string right) => native.SamePath(left, right);
        public bool PathsOverlap(string left, string right) => native.PathsOverlap(left, right);
        public bool Contains(string root, string candidate) => native.Contains(root, candidate);
        public void RequireContained(string root, string candidate) => native.RequireContained(root, candidate);
        public CatalogDatabaseSession Activate(CatalogDatabaseSession session) => native.Activate(session);
        public StartupStoreEvidence? BeginCompletionPeer(ILightflowStorageLocations locations, ICatalogConfigurationSnapshot configuration, StartupSessionCompletion completion)
        { PeerAttempts++; throw new InvalidOperationException("A Catalog-only host must not create Preview evidence."); }
        public Task<(ICatalogCompletionPeerStore? Store, string? Diagnostic)> OpenCompletionPeerAsync(ICatalogConfigurationSnapshot configuration,
            ILightflowStorageLocations locations, CancellationToken token, StartupStoreEvidence? evidence = null)
        { PeerAttempts++; throw new InvalidOperationException("A Catalog-only host must not create a Preview store."); }
        public void ComposeConsumers(CatalogLifecycle lifecycle, ICatalogCompletionPeerStore? peer, string? diagnostic, StartupStoreEvidence? peerEvidence)
            => native.ComposeConsumers(lifecycle, peer, diagnostic, peerEvidence);
        public Task StartMonitoringAsync(CancellationToken token) => native.StartMonitoringAsync(token);
        public void NotifyCatalogRestored() => native.NotifyCatalogRestored();
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
}
