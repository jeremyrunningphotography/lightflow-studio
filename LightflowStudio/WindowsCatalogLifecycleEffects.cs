using Lightflow.Application;
using Lightflow.Domain;
using System.IO;
using System.Text.Json;
namespace LightflowStudio;

internal sealed record WindowsCatalogConfigurationSnapshot(AppSettings Value) : ICatalogConfigurationSnapshot
{
    public string? CatalogDirectory => Value.CatalogDirectory;
    public string? PreviewsDirectory => Value.PreviewsDirectory;
    public Guid? CatalogId => Value.CatalogId;
    public string? CatalogBackupDirectory => Value.CatalogBackupDirectory;
    public string Serialize() => JsonSerializer.Serialize(Value);
}

internal sealed class WindowsCatalogConfigurationEffects(IStorageConfigurationStore store) : ICatalogConfigurationEffects
{
    public bool TryLoad(out ICatalogConfigurationSnapshot snapshot, out string? diagnostic)
    {
        var loaded = store.TryLoad(out var value, out diagnostic);
        snapshot = new WindowsCatalogConfigurationSnapshot(value);
        return loaded;
    }
    public ICatalogConfigurationSnapshot Stage(ICatalogConfigurationSnapshot previous, CatalogConfigurationUpdate update)
    {
        var value = ((WindowsCatalogConfigurationSnapshot)previous).Value;
        return new WindowsCatalogConfigurationSnapshot(value with
        {
            CatalogDirectory = update.SetCatalogDirectory ? update.CatalogDirectory : value.CatalogDirectory,
            CatalogId = update.SetCatalogId ? update.CatalogId : value.CatalogId,
            CatalogBackupDirectory = update.SetBackupDirectory ? update.CatalogBackupDirectory : value.CatalogBackupDirectory
        });
    }
    public ICatalogConfigurationSnapshot InitializeIsolatedDefaults(ICatalogConfigurationSnapshot previous, string profileRoot)
        => new WindowsCatalogConfigurationSnapshot(((WindowsCatalogConfigurationSnapshot)previous).Value with
        {
            LutFolder = null, CameraLutFolder = "", CreativeLutFolder = "",
            ScreengrabDirectory = Path.Combine(profileRoot, "Screengrabs")
        });
    public void Save(ICatalogConfigurationSnapshot snapshot) => store.Save(((WindowsCatalogConfigurationSnapshot)snapshot).Value);
}

internal sealed partial class LightflowStorageCoordinator
{
    private sealed class WindowsCatalogCompletionPeer(IPreviewStoreService store) : ICatalogCompletionPeerStore
    {
        public IPreviewStoreService Store => store;
        public ValueTask DisposeAsync() => store.DisposeAsync();
    }
    internal sealed class WindowsCatalogLifecycleEffects : ICatalogLifecycleEffects, ICatalogHostConsumerEffects
    {
        private readonly IStorageConfigurationStore _configuration;
        private readonly ICatalogSessionActivator _activator;
        private readonly IStorageLocationAssessor? _assessor;
        private InitializedDataProfile? _initialized;
        private WindowsCatalogHostConsumers? _consumers;
        public LightflowStorageCoordinator? Owner { get; private set; }
        public ILightflowStorageLocations Profile { get; }
        public ICatalogConfigurationEffects Configuration { get; }
        public ICatalogHostConsumerEffects Consumers => this;
        public ICatalogEvidenceEffects Evidence => WindowsCatalogEvidenceEffects.Instance;
        public ICatalogRecoveryNativeEffects RecoveryNative => WindowsCatalogRecoveryEffects.Instance;
        public ICatalogTransferNativeEffects TransferNative => WindowsCatalogTransferEffects.Instance;
        public ICatalogRecoveryService? RecoveryOverride { get; }
        public ICatalogRelocationTransfer? RelocationTransfer { get; }
        public bool SupportsCompletionPeer => true;
        public WindowsCatalogLifecycleEffects(string? localApplicationData = null, ICatalogRelocationTransfer? transfer = null,
            IStorageConfigurationStore? configuration = null, ICatalogSessionActivator? activator = null,
            ICatalogRecoveryService? recovery = null, LightflowStorageLocations? profile = null,
            InitializedDataProfile? initializedProfile = null, IStorageLocationAssessor? assessor = null)
        {
            Profile = profile ?? (localApplicationData is null ? LightflowStorageLocations.Current : LightflowStorageLocations.Create(localApplicationData));
            _configuration = configuration ?? new AppSettingsStorageConfigurationStore(Profile.SettingsPath, Profile.IsIsolated);
            Configuration = new WindowsCatalogConfigurationEffects(_configuration);
            _activator = activator ?? new CatalogSessionActivator();
            _assessor = assessor; _initialized = initializedProfile;
            RecoveryOverride = recovery; RelocationTransfer = transfer ?? new SqliteCatalogRelocationTransfer();
        }
        public void RequireInitializedProfile()
        {
            _initialized ??= ApplicationDataProfile.Initialize((LightflowStorageLocations)Profile);
            _initialized.RequireProfile((LightflowStorageLocations)Profile);
        }
        public ILightflowStorageLocations WithOverrides(ILightflowStorageLocations configured, string? catalogDirectory, string? previewsDirectory)
            => ((LightflowStorageLocations)configured).WithOverrides(new(catalogDirectory, previewsDirectory));
        public void GuardConfiguredLeaves(ILightflowStorageLocations defaults, ILightflowStorageLocations configured)
        {
            if (!defaults.IsIsolated) return;
            foreach (var database in new[] { configured.CatalogDatabasePath, configured.PreviewsDatabasePath }
                .Except(new[] { defaults.CatalogDatabasePath, defaults.PreviewsDatabasePath }, StringComparer.OrdinalIgnoreCase))
            foreach (var suffix in new[] { "", "-wal", "-shm", "-journal", ".startup-state" })
                ApplicationDataProfile.GuardAccess(database + suffix);
        }
        public CatalogStorageAdmission CreateAdmission(ILightflowStorageLocations locations, StorageOperation operation, string? reservedCatalogDirectory = null)
            => new CatalogLocationAdmission((LightflowStorageLocations)locations, operation, _assessor, reservedCatalogDirectory);
        public bool SamePath(string left, string right) => LightflowStorageCoordinator.SamePath(left, right);
        public bool PathsOverlap(string left, string right) => LightflowStorageLocations.PathsOverlap(left, right);
        public bool Contains(string root, string candidate) => MediaPathSemantics.Contains(root, candidate);
        public void RequireContained(string root, string candidate) => ApplicationDataProfile.RequireContained(root, candidate);
        public CatalogDatabaseSession Activate(CatalogDatabaseSession session) => _activator.Activate(session);
        public StartupStoreEvidence? BeginCompletionPeer(ILightflowStorageLocations locations, ICatalogConfigurationSnapshot configuration, StartupSessionCompletion completion)
            => StartupStoreEvidence.Begin(Evidence, locations.PreviewsDatabasePath, "Previews", configuration.PreviewsDirectory is null, completion);
        public async Task<(ICatalogCompletionPeerStore? Store, string? Diagnostic)> OpenCompletionPeerAsync(
            ICatalogConfigurationSnapshot configuration, ILightflowStorageLocations locations, CancellationToken token, StartupStoreEvidence? evidence = null)
        {
            var result = await OpenPreviewsAsync(((WindowsCatalogConfigurationSnapshot)configuration).Value,
                (LightflowStorageLocations)locations, token, evidence).ConfigureAwait(false);
            return (result.Service is null ? null : new WindowsCatalogCompletionPeer(result.Service), result.Diagnostic);
        }
        public void ComposeConsumers(CatalogLifecycle lifecycle, ICatalogCompletionPeerStore? peer, string? diagnostic, StartupStoreEvidence? peerEvidence)
        {
            Owner = new LightflowStorageCoordinator(_configuration, ((WindowsCatalogConfigurationSnapshot)lifecycle.Settings).Value,
                (LightflowStorageLocations)lifecycle.Locations, lifecycle.Session, RelocationTransfer!, _activator, lifecycle.Recovery,
                (peer as WindowsCatalogCompletionPeer)?.Store, diagnostic, lifecycle.CatalogEvidence, peerEvidence,
                lifecycle.ProfileCompletion, _assessor, lifecycle);
            _consumers = new WindowsCatalogHostConsumers(Owner);
        }
        public Task StartMonitoringAsync(CancellationToken token) => Owner!.MediaMonitoring!.StartAsync(token);
        public void NotifyCatalogRestored() => Owner!._videoRotations.NotifyCatalogRestored();
        public void CancelReadUsage() => _consumers!.CancelReadUsage();
        public void DisposeCaches() => _consumers!.DisposeCaches();
        public Task StopMonitoringAsync() => _consumers!.StopMonitoringAsync();
        public Task StopDerivedWorkAsync() => _consumers!.StopDerivedWorkAsync();
        public Task RestartAsync() => _consumers!.RestartAsync();
        public Task<IDisposable> EnterCompletionPeerMaintenanceAsync() => _consumers!.EnterCompletionPeerMaintenanceAsync();
        public bool HasQualifiedCompletionPeer => _consumers?.HasQualifiedCompletionPeer == true;
        public Task CloseCompletionPeerAsync() => _consumers!.CloseCompletionPeerAsync();
        public void CompleteCompletionPeer() => _consumers!.CompleteCompletionPeer();
        public bool CompletionPeerCompleted => _consumers?.CompletionPeerCompleted == true;
        public void ReleaseCompletionPeerEvidence() => _consumers!.ReleaseCompletionPeerEvidence();
        public void DisposeReadUsage() => _consumers!.DisposeReadUsage();
    }
}
