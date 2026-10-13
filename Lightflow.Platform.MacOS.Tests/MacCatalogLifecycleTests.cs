using System.Text.Json;
using Lightflow.Platform.MacOS;
using LightflowStudio;
using Xunit;

namespace Lightflow.Platform.MacOS.Tests;

[Collection("Native storage descriptor ownership")]
public sealed class MacCatalogLifecycleTests : IDisposable
{
    private readonly string _root=Path.Combine((Path.GetTempPath().StartsWith("/var/",StringComparison.Ordinal)?"/private"+Path.GetTempPath():Path.GetTempPath()),"lf015-native-"+Guid.NewGuid().ToString("N"));
    private MacCatalogLifecycleEffects Effects()=>new(_root);
    private async Task<CatalogLifecycle> Create()
    {
        Directory.CreateDirectory(Path.Combine(_root,"Catalog"));
        var result=await new CatalogLifecycle(Effects()).StartAsync();
        Assert.True(result.IsReady,result.Diagnostic);
        return result.Coordinator!;
    }
    [Fact]
    public async Task ProductionCreateAuthorShutdownDeepReopenRetainsIdentityAndNoPreview()
    {
        var owner=await Create();
        var identity=owner.CatalogSession.Identity.CatalogId;
        Assert.Equal(19,owner.CatalogSession.SchemaVersion);
        Assert.Equal("wal",owner.CatalogSession.RuntimePolicy.JournalMode);
        Assert.Equal(2,owner.CatalogSession.RuntimePolicy.SynchronousLevel);
        Assert.True(owner.CatalogSession.RuntimePolicy.ForeignKeysEnabled);
        var store=new CatalogCollectionOrganizationStore(()=>owner.Session);
        var authored=await store.CreateCollectionAsync("LF015 authored collection");
        var stale=owner.CatalogSession;
        await owner.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(()=>new CatalogCollectionOrganizationStore(()=>stale).ListCollectionsAsync());
        Assert.Equal("Dirty",File.ReadAllText(StartupSessionCompletion.PathFor(_root)));
        var evidence=File.ReadAllText(StartupStoreEvidence.JournalPath(Path.Combine(_root,"Catalog",CatalogLayout.CatalogFileName)));
        Assert.Contains("Validated",evidence);Assert.DoesNotContain("Clean",evidence);
        for(var i=0;i<2;i++)
        {
            var opened=await new CatalogLifecycle(Effects()).StartAsync(allowFirstCreation:false);
            Assert.True(opened.IsReady,opened.Diagnostic);
            await using var current=opened.Coordinator!;
            Assert.False(current.CatalogEvidence!.KnownClean);
            Assert.Equal(identity,current.CatalogSession.Identity.CatalogId);
            Assert.Equal(authored,Assert.Single(await new CatalogCollectionOrganizationStore(()=>current.Session).ListCollectionsAsync()));
            Assert.False(File.Exists(current.Locations.PreviewsDatabasePath));
        }
    }
    [Fact]
    public async Task MissingConfiguredDatabaseNeverCreatesReplacement()
    {
        var owner=await Create();var path=owner.Locations.CatalogDatabasePath;await owner.DisposeAsync();
        File.Move(path,path+".retained");
        var result=await new CatalogLifecycle(Effects()).StartAsync(allowFirstCreation:false);
        if(result.Coordinator is not null)await result.Coordinator.DisposeAsync();
        Assert.False(result.IsReady);Assert.False(File.Exists(path));Assert.True(File.Exists(path+".retained"));
    }
    [Fact]
    public async Task InvalidDatabaseRefusesWithoutReplacement()
    {
        Directory.CreateDirectory(Path.Combine(_root,"Catalog"));
        var path=Path.Combine(_root,"Catalog",CatalogLayout.CatalogFileName);
        var invalid="LF015 not a SQLite Catalog";File.WriteAllText(path,invalid);
        var result=await new CatalogLifecycle(Effects()).StartAsync(allowFirstCreation:false);
        if(result.Coordinator is not null)await result.Coordinator.DisposeAsync();
        Assert.False(result.IsReady);Assert.Equal(invalid,File.ReadAllText(path));
    }
    [Fact]
    public async Task DatabaseReplacementRevokesAuthoring()
    {
        var owner=await Create();var path=owner.Locations.CatalogDatabasePath;
        File.Move(path,path+".retained");File.WriteAllText(path,"LF015 replacement diagnostic");
        Assert.False(owner.VerifyWorkspaceReadiness());
        await Assert.ThrowsAsync<ObjectDisposedException>(()=>new CatalogCollectionOrganizationStore(()=>owner.Session).CreateCollectionAsync("refused"));
        await owner.DisposeAsync();Assert.Equal("LF015 replacement diagnostic",File.ReadAllText(path));
    }
    [Fact]
    public void NativeLeaseRetainsDirectoryNamespaceAndReleasesOnDispose()
    {
        var directory=Path.Combine(_root,"Catalog");Directory.CreateDirectory(directory);
        var native=new MacCatalogNativeEffects(_root);var path=Path.Combine(directory,"LightflowCatalog.db.startup-state");
        using(var first=native.OpenOwnershipJournal(path))
        {
            Assert.Throws<IOException>(()=>native.OpenOwnershipJournal(path));
            File.Move(path,path+".retained");
            Assert.Throws<IOException>(()=>native.OpenOwnershipJournal(path));
            Assert.Throws<IOException>(()=>native.RequireBoundOwnershipJournal(first,path));
        }
        using var released=native.OpenOwnershipJournal(path);native.RequireBoundOwnershipJournal(released,path);
    }
    [Fact]
    public void NativeJournalExclusiveCreationFlushAndReadback()
    {
        Directory.CreateDirectory(_root);var native=new MacCatalogNativeEffects(_root);var path=Path.Combine(_root,"operation.journal");
        using(var stream=native.OpenJournal(path)){stream.Write("LF015 journal durable readback"u8);stream.Flush(true);stream.Position=0;using var reader=new StreamReader(stream,leaveOpen:true);Assert.Equal("LF015 journal durable readback",reader.ReadToEnd());}
        Assert.Throws<IOException>(()=>native.OpenJournal(path));
        using var input=native.OpenClosedRead(path);native.RequireSingleRegularFile(input);
    }
    [Fact]
    public async Task TornTransferJournalRefusesWriterBeforeSqlite()
    {
        var owner=await Create();var database=owner.Locations.CatalogDatabasePath;await owner.DisposeAsync();
        var directory=Path.Combine(_root,"Catalog Transfers");Directory.CreateDirectory(directory);
        var journal=Path.Combine(directory,Guid.NewGuid().ToString("N")+".journal");File.WriteAllBytes(journal,[7,0,0]);
        var original=File.ReadAllBytes(database);
        var result=await new CatalogLifecycle(Effects()).StartAsync(allowFirstCreation:false);
        if(result.Coordinator is not null)await result.Coordinator.DisposeAsync();
        Assert.False(result.IsReady);Assert.Equal(CatalogRecoveryReason.PendingRecovery,result.RecoveryReason);
        Assert.Equal(original,File.ReadAllBytes(database));Assert.Equal(new byte[]{7,0,0},File.ReadAllBytes(journal));
    }
    [Fact]
    public async Task LocalBackupPreservesSchemaIdentityAndAuthoredCollections()
    {
        await using var owner=await Create();
        await new CatalogCollectionOrganizationStore(()=>owner.Session).CreateCollectionAsync("LF015 backup authored collection");
        var backup=await owner.Recovery.CreateBackupAsync(owner.Locations.CatalogDatabasePath,CatalogBackupKind.UserRequested);
        Assert.True(backup.Succeeded,backup.Diagnostic);
        var integrity=await owner.Recovery.CheckIntegrityAsync(backup.Backup!.Path);
        Assert.True(integrity.IsValid,integrity.Diagnostic);Assert.Equal(owner.CatalogSession.Identity.CatalogId,integrity.CatalogId);
        Assert.Equal(owner.CatalogSession.SchemaVersion,integrity.SchemaVersion);Assert.True(File.Exists(backup.Backup.Path+".metadata.json"));
        var store=new CatalogCollectionOrganizationStore(()=>owner.Session);
        await store.CreateCollectionAsync("LF015 state after backup");
        var restored=await owner.RestoreCatalogAsync(backup.Backup.Path);
        Assert.True(restored.Succeeded,restored.Diagnostic);
        Assert.Equal("LF015 backup authored collection",Assert.Single(await store.ListCollectionsAsync()).Name);
    }
    [Fact]
    public void UnsupportedPublicationIsExplicitAndRetainsSource()
    {
        Directory.CreateDirectory(_root);var native=new MacCatalogNativeEffects(_root);
        Assert.Throws<IOException>(()=>native.AcquireDirectoryLease(_root,true));Assert.True(Directory.Exists(_root));
    }
    [Fact]
    public async Task SymbolicCatalogPathRefusesStorageAdmission()
    {
        var directory=Path.Combine(_root,"Catalog");Directory.CreateDirectory(_root);Directory.CreateDirectory(Path.Combine(_root,"target"));Directory.CreateSymbolicLink(directory,Path.Combine(_root,"target"));
        var result=await new CatalogLifecycle(Effects()).StartAsync();
        if(result.Coordinator is not null)await result.Coordinator.DisposeAsync();Assert.False(result.IsReady);
        Assert.False(File.Exists(Path.Combine(directory,CatalogLayout.CatalogFileName)));
    }
    [Fact]
    public async Task FutureSchemaIsRefusedAndRetained()
    {
        var owner=await Create();var database=owner.Locations.CatalogDatabasePath;await owner.DisposeAsync();
        using(var connection=new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder{DataSource=database,Mode=Microsoft.Data.Sqlite.SqliteOpenMode.ReadWrite,Pooling=false}.ToString()))
        {connection.Open();using var command=connection.CreateCommand();command.CommandText="PRAGMA user_version=20;";command.ExecuteNonQuery();}
        var result=await new CatalogLifecycle(Effects()).StartAsync(allowFirstCreation:false);
        if(result.Coordinator is not null)await result.Coordinator.DisposeAsync();
        Assert.False(result.IsReady);Assert.Contains("requires a newer",result.Diagnostic);
        using var read=new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder{DataSource=database,Mode=Microsoft.Data.Sqlite.SqliteOpenMode.ReadOnly,Pooling=false}.ToString());
        read.Open();using var version=read.CreateCommand();version.CommandText="PRAGMA user_version;";Assert.Equal(20L,version.ExecuteScalar());
    }
    [Fact]
    public async Task SharedJournalFramingNativeFlushReadbackAndPendingIntentRefusal()
    {
        var owner=await Create();var locations=owner.Locations;await owner.DisposeAsync();
        var effects=new MacCatalogNativeEffects(_root);
        var directory=CatalogTransferJournal.DirectoryPath(effects,locations);Directory.CreateDirectory(directory);
        var id=Guid.NewGuid();
        var request=new Lightflow.Application.ClosedCatalogTransferRequest(id,0,Lightflow.Application.ClosedCatalogTransferMode.ClosedTransport,Guid.NewGuid(),19,0x4C465343);
        var intent=new Lightflow.Application.ClosedCatalogJournalRecord(1,request,0,Lightflow.Application.ClosedCatalogTransferState.Preflight,true,"",null,null);
        await using(var journal=new CatalogTransferJournal(effects,locations,id))await journal.AppendAsync(intent,CancellationToken.None);
        var path=Path.Combine(directory,id.ToString("N")+".journal");
        var read=Assert.Single(CatalogTransferJournal.Read(effects,path));
        Assert.Equal(Lightflow.Application.ClosedCatalogJournalFormat.Digest(intent),Lightflow.Application.ClosedCatalogJournalFormat.Digest(read));
        var started=await new CatalogLifecycle(Effects()).StartAsync(allowFirstCreation:false);
        if(started.Coordinator is not null)await started.Coordinator.DisposeAsync();
        Assert.False(started.IsReady);Assert.Equal(CatalogRecoveryReason.PendingRecovery,started.RecoveryReason);
        Assert.True(File.Exists(path));
    }
    public void Dispose(){if(Directory.Exists(_root))Directory.Delete(_root,true);}
}
