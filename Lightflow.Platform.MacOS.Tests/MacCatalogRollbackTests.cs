using Lightflow.Application;
using Lightflow.Domain;
using Lightflow.Platform.MacOS;
using LightflowStudio;
using Xunit;

namespace Lightflow.Platform.MacOS.Tests;

[Collection("Native storage descriptor ownership")]
public sealed class MacCatalogRollbackTests
{
    private static string Root()
    {
        var temp=Path.GetTempPath();if(temp.StartsWith("/var/",StringComparison.Ordinal))temp="/private"+temp;
        return Path.Combine(temp,"lf015-rollback-"+Guid.NewGuid().ToString("N"));
    }
    [Fact]
    public async Task NativeRestoreActivationFaultRollsBackToPriorOwnerAndAuthoredState()
    {
        var root=Root();Directory.CreateDirectory(Path.Combine(root,"Catalog"));CatalogLifecycle? owner=null;
        try
        {
            var effects=new FaultingEffects(new MacCatalogLifecycleEffects(root));
            var started=await new CatalogLifecycle(effects).StartAsync();Assert.True(started.IsReady,started.Diagnostic);owner=started.Coordinator!;
            var store=new CatalogCollectionOrganizationStore(()=>owner.Session);
            await store.CreateCollectionAsync("Protected snapshot");
            var backup=await owner.Recovery.CreateBackupAsync(owner.Locations.CatalogDatabasePath,CatalogBackupKind.UserRequested);Assert.True(backup.Succeeded,backup.Diagnostic);
            var inputs=Path.Combine(root,"Restore Inputs");Directory.CreateDirectory(inputs);
            var selected=Path.Combine(inputs,"selected.db");File.Copy(backup.Backup!.Path,selected);
            var selectedBytes=File.ReadAllBytes(selected);
            Assert.Equal(File.ReadAllBytes(backup.Backup.Path),selectedBytes);
            await store.CreateCollectionAsync("Current state retained after activation fault");
            var before=await store.ListCollectionsAsync();var id=owner.CatalogSession.Identity.CatalogId;
            effects.FailNextActivation=true;
            var result=await owner.RestoreCatalogAsync(selected);
            Assert.False(result.Succeeded);Assert.Contains("previous Catalog was restored and reactivated",result.Diagnostic);
            Assert.True(owner.CatalogAvailable);Assert.Equal(id,owner.CatalogSession.Identity.CatalogId);
            Assert.Equal(before,await store.ListCollectionsAsync());Assert.True(File.Exists(selected));
            Assert.Equal(selectedBytes,File.ReadAllBytes(selected));
        }
        finally{if(owner is not null)await owner.DisposeAsync();Directory.Delete(root,true);}
    }
    [Fact]
    public async Task NativeExplicitCreateCommitReadbackAndPostSaveFaultRollbackRetainCatalogArtifacts()
    {
        var root=Root();var target=Path.Combine(root,"SelectedCatalog");Directory.CreateDirectory(target);
        try
        {
            var effects=new FaultingEffects(new MacCatalogLifecycleEffects(root));
            Assert.True(effects.Configuration.TryLoad(out var before,out _));effects.FailNextSave=true;
            var failed=await new CatalogLifecycle(effects).CreateCatalogAsync(target);
            Assert.False(failed.IsReady);Assert.True(File.Exists(Path.Combine(target,CatalogLayout.CatalogFileName)));
            Assert.True(effects.Configuration.TryLoad(out var after,out var diagnostic),diagnostic);Assert.Equal(before.Serialize(),after.Serialize());
            Assert.False(File.Exists(effects.Profile.CatalogDatabasePath));
            var successful=Path.Combine(root,"CommittedCatalog");Directory.CreateDirectory(successful);
            var ready=await new CatalogLifecycle(effects).CreateCatalogAsync(successful);Assert.True(ready.IsReady,ready.Diagnostic);
            await using var owner=ready.Coordinator!;
            Assert.True(effects.Configuration.TryLoad(out var persisted,out diagnostic),diagnostic);
            Assert.Equal(owner.CatalogSession.Identity.CatalogId,persisted.CatalogId);Assert.Equal(successful,persisted.CatalogDirectory);
            Assert.False(File.Exists(owner.Locations.PreviewsDatabasePath));
        }
        finally{Directory.Delete(root,true);}
    }
    // Fault injection supplies effects failures; the unchanged shared owner sequences rollback.
    private sealed class FaultingEffects(ICatalogLifecycleEffects native) : ICatalogLifecycleEffects
    {
        public bool FailNextActivation {get;set;}
        public bool FailNextSave {get;set;}
        private ICatalogConfigurationEffects? _configuration;
        public ILightflowStorageLocations Profile=>native.Profile;
        public ICatalogConfigurationEffects Configuration=>_configuration??=new FaultingConfiguration(this,native.Configuration);
        public ICatalogHostConsumerEffects Consumers=>native.Consumers;
        public ICatalogEvidenceEffects Evidence=>native.Evidence;
        public ICatalogRecoveryNativeEffects RecoveryNative=>native.RecoveryNative;
        public ICatalogTransferNativeEffects TransferNative=>native.TransferNative;
        public ICatalogRecoveryService? RecoveryOverride=>native.RecoveryOverride;
        public ICatalogRelocationTransfer? RelocationTransfer=>native.RelocationTransfer;
        public bool SupportsCompletionPeer=>native.SupportsCompletionPeer;
        public void RequireInitializedProfile()=>native.RequireInitializedProfile();
        public ILightflowStorageLocations WithOverrides(ILightflowStorageLocations configured,string? catalogDirectory,string? previewsDirectory)=>native.WithOverrides(configured,catalogDirectory,previewsDirectory);
        public void GuardConfiguredLeaves(ILightflowStorageLocations defaults,ILightflowStorageLocations configured)=>native.GuardConfiguredLeaves(defaults,configured);
        public CatalogStorageAdmission CreateAdmission(ILightflowStorageLocations locations,StorageOperation operation,string? reservedCatalogDirectory=null)=>native.CreateAdmission(locations,operation,reservedCatalogDirectory);
        public bool SamePath(string a,string b)=>native.SamePath(a,b);
        public bool PathsOverlap(string a,string b)=>native.PathsOverlap(a,b);
        public bool Contains(string a,string b)=>native.Contains(a,b);
        public void RequireContained(string a,string b)=>native.RequireContained(a,b);
        public CatalogDatabaseSession Activate(CatalogDatabaseSession session)
        {
            var activated=native.Activate(session);
            if(FailNextActivation){FailNextActivation=false;throw new IOException("LF015 injected host activation failure after native binding.");}
            return activated;
        }
        public StartupStoreEvidence? BeginCompletionPeer(ILightflowStorageLocations locations,ICatalogConfigurationSnapshot configuration,StartupSessionCompletion completion)=>native.BeginCompletionPeer(locations,configuration,completion);
        public Task<(ICatalogCompletionPeerStore? Store,string? Diagnostic)> OpenCompletionPeerAsync(ICatalogConfigurationSnapshot configuration,ILightflowStorageLocations locations,CancellationToken token,StartupStoreEvidence? evidence=null)=>native.OpenCompletionPeerAsync(configuration,locations,token,evidence);
        public void ComposeConsumers(CatalogLifecycle lifecycle,ICatalogCompletionPeerStore? peer,string? diagnostic,StartupStoreEvidence? evidence)=>native.ComposeConsumers(lifecycle,peer,diagnostic,evidence);
        public Task StartMonitoringAsync(CancellationToken token)=>native.StartMonitoringAsync(token);
        public void NotifyCatalogRestored()=>native.NotifyCatalogRestored();
        private sealed class FaultingConfiguration(FaultingEffects owner,ICatalogConfigurationEffects original) : ICatalogConfigurationEffects
        {
            public bool TryLoad(out ICatalogConfigurationSnapshot snapshot,out string? diagnostic)=>original.TryLoad(out snapshot,out diagnostic);
            public ICatalogConfigurationSnapshot Stage(ICatalogConfigurationSnapshot previous,CatalogConfigurationUpdate update)=>original.Stage(previous,update);
            public ICatalogConfigurationSnapshot InitializeIsolatedDefaults(ICatalogConfigurationSnapshot previous,string root)=>original.InitializeIsolatedDefaults(previous,root);
            public void Save(ICatalogConfigurationSnapshot snapshot){original.Save(snapshot);if(owner.FailNextSave){owner.FailNextSave=false;throw new IOException("LF015 injected failure after native configuration save.");}}
        }
    }
}
