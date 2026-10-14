using System.Text.Json;
using Lightflow.Application;
using Lightflow.Domain;
using LightflowStudio;
namespace Lightflow.Platform.MacOS;

public sealed record MacCatalogLocations(string ApplicationDataDirectory,string CatalogDirectory) : ILightflowStorageLocations
{
    public bool IsIsolated => true;
    public string CatalogDatabasePath=>Path.Combine(CatalogDirectory,CatalogLayout.CatalogFileName);
    public string CatalogBackupsDirectory=>Path.Combine(CatalogDirectory,"Backups");
    public string SettingsPath=>Path.Combine(ApplicationDataDirectory,"settings.json");
    public string StatePath=>Path.Combine(ApplicationDataDirectory,"state.json");
    public string WorkspaceStatePath=>Path.Combine(ApplicationDataDirectory,"workspace-state.json");
    public string JobHistoryPath=>Path.Combine(ApplicationDataDirectory,"job-history.json");
    public string FileOperationHistoryPath=>Path.Combine(ApplicationDataDirectory,"file-operation-history.json");
    public string TrimHistoryPath=>Path.Combine(ApplicationDataDirectory,"trim-history.json");
    public string OutputIdentityDirectory=>Path.Combine(ApplicationDataDirectory,"output-identities");
    public string PreviewsDirectory=>Path.Combine(ApplicationDataDirectory,"Previews");
    public string PreviewsDatabasePath=>Path.Combine(PreviewsDirectory,CatalogLayout.PreviewsFileName);
    public string ThumbnailCacheDirectory=>Path.Combine(PreviewsDirectory,"thumbnails");
    public string StandardPreviewCacheDirectory=>Path.Combine(PreviewsDirectory,"previews");
    public string TemporaryDirectory=>Path.Combine(ApplicationDataDirectory,"Temporary");
    public string LogsDirectory=>ApplicationDataDirectory;
    public string ActivityLogPath=>Path.Combine(ApplicationDataDirectory,"activity.log");
    public string MachineIdentityPath=>Path.Combine(ApplicationDataDirectory,"machine-id");
}

public sealed class MacCatalogLifecycleEffects : ICatalogLifecycleEffects
{
    private readonly MacCatalogNativeEffects _native;
    public ILightflowStorageLocations Profile {get;}
    public ICatalogConfigurationEffects Configuration {get;}
    public ICatalogHostConsumerEffects Consumers {get;} = new CatalogOnlyConsumers();
    public ICatalogEvidenceEffects Evidence=>_native;
    public double LastOwnershipOpenMilliseconds=>_native.LastOwnershipOpenMilliseconds;
    public ICatalogRecoveryNativeEffects RecoveryNative=>_native;
    public ICatalogTransferNativeEffects TransferNative=>_native;
    public ICatalogRecoveryService? RecoveryOverride=>null;
    public ICatalogRelocationTransfer? RelocationTransfer=>null;
    public bool SupportsCompletionPeer=>false;
    public MacCatalogLifecycleEffects(string dataRoot)
    {
        if(!OperatingSystem.IsMacOS()||!Path.IsPathFullyQualified(dataRoot))throw new ArgumentException("Use an absolute task-owned Mac data root.");
        dataRoot=Path.GetFullPath(dataRoot);
        _native=new(dataRoot);
        Profile=new MacCatalogLocations(dataRoot,Path.Combine(dataRoot,"Catalog"));
        Configuration=new ConfigurationStore(Profile,_native);
    }
    public void RequireInitializedProfile()
    {
        _native.RequireUnlinked(Profile.ApplicationDataDirectory);
        Directory.CreateDirectory(Profile.ApplicationDataDirectory);
        // Ownership boundaries only; no Preview database, certificate, cache or consumer is fabricated.
        Directory.CreateDirectory(Profile.PreviewsDirectory);
        Directory.CreateDirectory(Profile.TemporaryDirectory);
        Directory.CreateDirectory(Path.Combine(Profile.ApplicationDataDirectory,"Catalog Transfers"));
        _native.GuardAccess(Profile.ApplicationDataDirectory);
    }
    public ILightflowStorageLocations WithOverrides(ILightflowStorageLocations configured,string? catalogDirectory,string? previewsDirectory)
    {
        var catalog=Path.GetFullPath(catalogDirectory??configured.CatalogDirectory);
        RequireContained(Profile.ApplicationDataDirectory,catalog);
        if(PathsOverlap(catalog,configured.PreviewsDirectory)||PathsOverlap(catalog,configured.TemporaryDirectory))throw new IOException("Catalog overlaps another storage owner.");
        if(previewsDirectory is not null&&!SamePath(previewsDirectory,Profile.PreviewsDirectory))throw new IOException("Catalog-only host has no Preview selection capability.");
        return new MacCatalogLocations(Profile.ApplicationDataDirectory,catalog);
    }
    public void GuardConfiguredLeaves(ILightflowStorageLocations defaults,ILightflowStorageLocations configured)=>_native.GuardAccess(configured.CatalogDirectory);
    public CatalogStorageAdmission CreateAdmission(ILightflowStorageLocations locations,StorageOperation operation,string? reservedCatalogDirectory=null)
        =>new(locations,operation,new Admission(_native,locations,reservedCatalogDirectory));
    public bool SamePath(string a,string b)=>MacCatalogNativeEffects.SamePath(a,b);
    public bool Contains(string root,string candidate)=>MacCatalogNativeEffects.Contains(root,candidate);
    public bool PathsOverlap(string a,string b)=>_native.PathsOverlap(a,b);
    public void RequireContained(string root,string candidate) {if(!Contains(root,candidate))throw new IOException("Catalog path is outside the explicit host data root.");}
    public CatalogDatabaseSession Activate(CatalogDatabaseSession session){_native.ActivateAdmittedMain(session.DatabasePath);return session;}
    public StartupStoreEvidence? BeginCompletionPeer(ILightflowStorageLocations locations,ICatalogConfigurationSnapshot configuration,StartupSessionCompletion completion)=>throw new InvalidOperationException("No qualified Preview peer.");
    public Task<(ICatalogCompletionPeerStore? Store,string? Diagnostic)> OpenCompletionPeerAsync(ICatalogConfigurationSnapshot configuration,ILightflowStorageLocations locations,CancellationToken token,StartupStoreEvidence? evidence=null)=>throw new InvalidOperationException("No qualified Preview peer.");
    public void ComposeConsumers(CatalogLifecycle lifecycle,ICatalogCompletionPeerStore? peer,string? diagnostic,StartupStoreEvidence? peerEvidence)
    {if(peer is not null||peerEvidence is not null)throw new InvalidOperationException("Catalog-only composition refuses peer evidence.");}
    public Task StartMonitoringAsync(CancellationToken token) {token.ThrowIfCancellationRequested();return Task.CompletedTask;}
    public void NotifyCatalogRestored()=>_native.FinishRestoredBinding();
    private sealed class Admission : ICatalogAdmissionNativeEffects
    {
        private readonly MacCatalogNativeEffects _native;
        private readonly MacStorageLocationAssessor _assessor;
        private FileStream? _main;
        public Admission(MacCatalogNativeEffects native,ILightflowStorageLocations locations,string? reserved)
        {
            _native=native;
            _assessor=new(new[]{locations.PreviewsDirectory,locations.TemporaryDirectory}.Concat(reserved is null?[]:new[]{reserved}));
        }
        public IStorageLocationAssessor Assessor=>_assessor;
        public bool HasNativeBinding=>true;
        public void GuardAccess(string databasePath)
        {
            _native.GuardAccess(databasePath);
            if(_main is MacCatalogNativeEffects.NativeStream main)main.Revalidate(databasePath);
        }
        public ILightflowStorageLocations RouteForUse(ILightflowStorageLocations configured,StorageLocationAssessment assessment)
            =>new MacCatalogLocations(configured.ApplicationDataDirectory,assessment.Identity!.CanonicalLocation);
        public void GuardMainDatabase(string path) { _main??=MacCatalogNativeEffects.Open(path,false,false,0);var guard=(MacCatalogNativeEffects.NativeStream)_main;guard.Revalidate(path);_native.RegisterMainGuard(path,guard); }
        public void ReleaseMainDatabaseGuard()=>ReleaseMain(true);
        private void ReleaseMain(bool forReplacement){if(_main is MacCatalogNativeEffects.NativeStream stream){_native.RemoveMainGuard(stream.Name,stream,forReplacement);stream.Dispose();}_main=null;}
        public bool SameDatabase(string a,string b)=>MacCatalogNativeEffects.SamePath(a,b);
        public void Dispose(){ReleaseMain(false);_assessor.Dispose();}
    }
    public sealed record Snapshot(string? CatalogDirectory=null,string? PreviewsDirectory=null,Guid? CatalogId=null,string? CatalogBackupDirectory=null) : ICatalogConfigurationSnapshot
    {public string Serialize()=>JsonSerializer.Serialize(this);}
    private sealed class ConfigurationStore(ILightflowStorageLocations profile,MacCatalogNativeEffects native) : ICatalogConfigurationEffects
    {
        public bool TryLoad(out ICatalogConfigurationSnapshot snapshot,out string? diagnostic)
        {
            snapshot=new Snapshot();diagnostic=null;
            try {
                native.GuardAccess(profile.SettingsPath);
                if(File.Exists(profile.SettingsPath)) {
                    using var input=MacCatalogNativeEffects.Open(profile.SettingsPath,false,false,1);
                    if(input.Length>1024*1024)throw new IOException("Host configuration exceeds its bound.");
                    snapshot=JsonSerializer.Deserialize<Snapshot>(input)??throw new IOException("Host configuration is empty.");
                }
                return true;
            }catch(Exception e)when(e is IOException or JsonException){diagnostic=e.Message;return false;}
        }
        public ICatalogConfigurationSnapshot Stage(ICatalogConfigurationSnapshot previous,CatalogConfigurationUpdate update)
            =>((Snapshot)previous) with {CatalogDirectory=update.SetCatalogDirectory?update.CatalogDirectory:previous.CatalogDirectory,CatalogId=update.SetCatalogId?update.CatalogId:previous.CatalogId,CatalogBackupDirectory=update.SetBackupDirectory?update.CatalogBackupDirectory:previous.CatalogBackupDirectory};
        public ICatalogConfigurationSnapshot InitializeIsolatedDefaults(ICatalogConfigurationSnapshot previous,string profileRoot)=>previous;
        public void Save(ICatalogConfigurationSnapshot snapshot)
        {
            native.GuardAccess(profile.SettingsPath);
            var temporary=profile.SettingsPath+"."+Guid.NewGuid().ToString("N")+".tmp";
            using(var file=MacCatalogNativeEffects.Open(temporary,true,true,2)) {
                var bytes=System.Text.Encoding.UTF8.GetBytes(snapshot.Serialize());file.Write(bytes);file.Flush(true);
            }
            native.RequireUnlinked(profile.SettingsPath);
            File.Move(temporary,profile.SettingsPath,true);
            using var parent=MacCatalogNativeEffects.Native.Open(profile.ApplicationDataDirectory,false,false,0,true);
            MacCatalogNativeEffects.Check(MacCatalogNativeEffects.Native.SyncDirectory(MacCatalogNativeEffects.Fd(parent)),"Configuration namespace flush refused",profile.ApplicationDataDirectory);
        }
    }
    private sealed class CatalogOnlyConsumers : ICatalogHostConsumerEffects
    {
        public bool HasQualifiedCompletionPeer=>false;
        public bool CompletionPeerCompleted=>false;
        public void CancelReadUsage() { }
        public void DisposeCaches() { }
        public Task StopMonitoringAsync()=>Task.CompletedTask;
        public Task StopDerivedWorkAsync()=>Task.CompletedTask;
        public Task RestartAsync()=>Task.CompletedTask;
        public Task<IDisposable> EnterCompletionPeerMaintenanceAsync()=>Task.FromResult<IDisposable>(new EmptyScope());
        public Task CloseCompletionPeerAsync()=>Task.CompletedTask;
        public void CompleteCompletionPeer()=>throw new InvalidOperationException("No completion peer exists.");
        public void ReleaseCompletionPeerEvidence() { }
        public void DisposeReadUsage() { }
        private sealed class EmptyScope : IDisposable {public void Dispose() { }}
    }
}
