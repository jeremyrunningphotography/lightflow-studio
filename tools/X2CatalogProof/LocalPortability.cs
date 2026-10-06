// Bounded persistence research only; no production policy or schema changes.
using LightflowStudio;
using Microsoft.Data.Sqlite;
using System.Diagnostics;
using System.Text.Json;

internal static partial class Program
{
    static LightflowStorageLocations PortableLocations(string local,string catalog) =>
        LightflowStorageLocations.CreateAtRoot(Owned(local),new(Owned(catalog),Path.Combine(Owned(local),"Previews")));
    static async Task LocalPortability(string local,string catalog,string source,string backup)
    {
        var l=PortableLocations(local,catalog);Directory.CreateDirectory(local);
        Assert(!File.Exists(l.CatalogDatabasePath),"fresh disposable Catalog");
        var timings=new List<object>();var timer=Stopwatch.StartNew();
        await using(var created=await Catalog(l,true))using(var c=created.OpenConnection())Assert((string?)Scalar(c,"PRAGMA journal_mode")=="wal","create WAL on candidate filesystem");
        SqliteConnection.ClearAllPools();ClosedCheckpoint(l.CatalogDatabasePath);
        var expected=Snapshot(source);var rec=new SqliteCatalogRecoveryService(l);
        var install=await rec.BeginRestoreAsync(source,true);Assert(install.Succeeded,"install representative SQLite-aware closed fixture");Assert((await install.Transaction!.CommitAsync()).Succeeded,"install committed");
        Assert(Equal(expected,Snapshot(l.CatalogDatabasePath)),"all incoming authored rows and identities preserved");
        await using var s=await Catalog(l,false);object runtime;using(var c=s.OpenConnection())runtime=Runtime(c);
        var assets=await new CatalogMediaAssetRepository(()=>s).ListAsync();Assert(assets.Count==32,"32 representative assets");
        var id=assets[0].AssetId;var descriptions=new CatalogAssetDescriptionStore(()=>s);var before=(await descriptions.GetAsync([id]))[id];
        timer.Restart();await descriptions.ApplyAsync(new Dictionary<Guid,long>{{id,before.Revision}},new(new Dictionary<AssetDescriptionField,string?>{{AssetDescriptionField.Notes,"X2 local portability committed note"}}));timings.Add(new{operation="authored commit",ms=timer.Elapsed.TotalMilliseconds});
        Assert((await descriptions.GetAsync([id]))[id].Notes=="X2 local portability committed note","authored readback");var authored=Snapshot(l.CatalogDatabasePath);
        using(var a=JsonDocument.Parse(JsonSerializer.Serialize(expected)))using(var b=JsonDocument.Parse(JsonSerializer.Serialize(authored)))foreach(var table in a.RootElement.EnumerateObject())if(table.Name!="MediaAssetDescriptions")Assert(Equal(table.Value,b.RootElement.GetProperty(table.Name)),"unchanged authored table "+table.Name);
        await using(var preview=new PreviewStoreService(l)){await preview.ObserveSourceAsync(id,new(20,1,1,"abcdef"));await preview.SetMetadataAsync(id,new(1,PreviewComponentState.Current,PayloadJson:"{\"localProof\":true}"));}
        SqliteConnection.ClearAllPools();Directory.Move(l.PreviewsDirectory,l.PreviewsDirectory+"-retained");
        await using(var preview=new PreviewStoreService(l)){Assert(await preview.GetAsync(id)==null,"fresh machine-local Preview has no imported state");await preview.ObserveSourceAsync(id,new(20,1,1,"abcdef"));await preview.SetMetadataAsync(id,new(1,PreviewComponentState.Current,PayloadJson:"{\"rebuilt\":true}"));}
        Assert(Equal(authored,Snapshot(l.CatalogDatabasePath)),"Preview replacement does not alter any Catalog-owned row");
        using(var c=s.OpenConnection())
        {
            Execute(c,"BEGIN IMMEDIATE");
            var start=new ProcessStartInfo(Environment.ProcessPath!){RedirectStandardOutput=true,RedirectStandardError=true};
            start.ArgumentList.Add(typeof(Program).Assembly.Location);start.ArgumentList.Add("portable-contend");start.ArgumentList.Add(l.CatalogDatabasePath);start.ArgumentList.Add(Path.Combine(local,"contender.json"));
            using var child=Process.Start(start)!;await child.WaitForExitAsync();Assert(child.ExitCode==0,"separate process reader succeeds/writer SQLITE_BUSY");Execute(c,"ROLLBACK");
        }
        using(var c=s.OpenConnection()){Execute(c,"BEGIN IMMEDIATE;ROLLBACK;");Assert(true,"writer reacquires after release");}
        var target=Path.GetFullPath(backup);
        if(!target.StartsWith(Workspace+Path.DirectorySeparatorChar,StringComparison.Ordinal) && !File.Exists(Path.Combine(target,"X2-BACKUP-ONLY")))throw new ArgumentException("External destination requires task-owned backup marker");
        var backupLocations=l with{CatalogBackupsDirectory=target};timer.Restart();var saved=await new SqliteCatalogRecoveryService(backupLocations).CreateBackupAsync(l.CatalogDatabasePath,CatalogBackupKind.UserRequested);timings.Add(new{operation="SQLite-aware backup",ms=timer.Elapsed.TotalMilliseconds});
        Save(Path.Combine(local,"BACKUP_ATTEMPT.json"),new{saved.Succeeded,saved.Diagnostic});Assert(saved.Succeeded,"SQLite-aware backup destination: "+saved.Diagnostic);Assert(Equal(authored,Snapshot(saved.Backup!.Path)),"backup all-table equality");
        var restored=Locations(Path.Combine(local,"restore-local"));var restore=await new SqliteCatalogRecoveryService(restored).BeginRestoreAsync(saved.Backup.Path);Assert(restore.Succeeded,"restore to native local storage");Assert((await restore.Transaction!.CommitAsync()).Succeeded,"local restore commit");
        await using(var reopened=await Catalog(restored,false)){Assert(reopened.Identity.CatalogId==s.Identity.CatalogId,"restored CatalogId");Assert(Equal(authored,Snapshot(restored.CatalogDatabasePath)),"restored every-table equality");}
        await s.DisposeAsync();SqliteConnection.ClearAllPools();ClosedCheckpoint(l.CatalogDatabasePath);SqliteConnection.ClearAllPools();
        Assert(!File.Exists(l.CatalogDatabasePath+"-wal")||new FileInfo(l.CatalogDatabasePath+"-wal").Length==0,"clean close leaves no uncheckpointed WAL");
        await using(var reopened=await Catalog(l,false))Assert(reopened.Identity.CatalogId==s.Identity.CatalogId,"clean reopen stable CatalogId");SqliteConnection.ClearAllPools();ClosedCheckpoint(l.CatalogDatabasePath);
        Save(Path.Combine(local,"PORTABILITY.json"),new{runtime,timings,checks=Checks,nativeImages=NativeImages(),catalogId=s.Identity.CatalogId,assetCount=assets.Count,preview="separate machine-local directory; rebuilt without Catalog mutation",sourceHash=Hash(source),backupHash=Hash(saved.Backup.Path),catalogHash=Hash(l.CatalogDatabasePath),limits=new[]{"one Mac process/driver proof, not physical SSD or Windows qualification","SIGKILL tested separately; no power failure or unsafe hardware removal","production instance coordinator/UI excluded"}});
    }
    static void PortableContend(string db,string output)
    {
        using var c=Open(db);Assert(Convert.ToInt64(Scalar(c,"SELECT count(*) FROM MediaAssets"))==32,"separate process reader during writer");
        using var command=c.CreateCommand();command.CommandText="BEGIN IMMEDIATE";command.CommandTimeout=1;
        try{command.ExecuteNonQuery();throw new Exception("unexpected second writer");}catch(SqliteException e){Assert(e.SqliteErrorCode==5,"separate process writer SQLITE_BUSY");Save(output,new{code=e.SqliteErrorCode,checks=Checks});}
    }
    static async Task PortablePaths(string local,string media)
    {
        var l=Locations(local);Directory.CreateDirectory(local);Directory.CreateDirectory(media);
        await using var s=await Catalog(l,true);
        var roots=new MediaRootService(()=>s,new MachineIdentityProvider(l.MachineIdentityPath),new MediaRootFileSystem());
        var root=await roots.CreateAsync("Portable path fixture",media);Assert(root.Succeeded,"create portable root");
        var assets=new MediaAssetService(new CatalogMediaAssetRepository(()=>s),roots,new SampledSourceFingerprintService());
        var rows=new List<object>();
        foreach(var pair in new[]{("case","Case.mov","case.mov"),("unicode","café.mov","cafe\u0301.mov"),("backslash","nested/name.mov","nested\\name.mov"),("spaces","trim.mov"," trim.mov ")})
        {
            try
            {
                var a=Path.Combine(media,pair.Item2);var b=Path.Combine(media,pair.Item3);Directory.CreateDirectory(Path.GetDirectoryName(a)!);
                File.WriteAllText(a,"first");File.WriteAllText(b,"second longer");
                var x=await assets.CreateAsync(root.Root!.RootId,pair.Item2,"video");var y=await assets.CreateAsync(root.Root.RootId,pair.Item3,"video");var z=await assets.FindAsync(root.Root.RootId,pair.Item3);
                rows.Add(new{scenario=pair.Item1,a=pair.Item2,b=pair.Item3,sameContents=File.ReadAllText(a)==File.ReadAllText(b),createFirst=x.Status.ToString(),createSecond=y.Status.ToString(),firstId=x.Asset?.Asset.AssetId,secondId=y.Asset?.Asset.AssetId,lookupId=z?.Asset.AssetId,requestedHash=Hash(b),lookupHash=z?.PhysicalPath is {} path && File.Exists(path)?Hash(path):null,keyA=MediaPathSemantics.RelativePathKey(pair.Item2),keyB=MediaPathSemantics.RelativePathKey(pair.Item3)});
            }
            catch(IOException e){rows.Add(new{scenario=pair.Item1,fileSystemRejected=true,errorType=e.GetType().Name});}
        }
        foreach(var name in new[]{"CON.mov","colon:name.mov","question?.mov","trailing-dot.mov.","emoji-😀.mov"})
        {try{File.WriteAllText(Path.Combine(media,name),"name fixture");var a=await assets.CreateAsync(root.Root!.RootId,name,"video");rows.Add(new{scenario="name",name,status=a.Status.ToString()});}catch(IOException e){rows.Add(new{scenario="name",name,fileSystemRejected=true,errorType=e.GetType().Name});}}
        var outside=Path.Combine(local,"outside");Directory.CreateDirectory(outside);File.WriteAllText(Path.Combine(outside,"escape.mov"),"outside fixture");
        try{Directory.CreateSymbolicLink(Path.Combine(media,"link-out"),outside);var a=await assets.CreateAsync(root.Root!.RootId,"link-out/escape.mov","video");rows.Add(new{scenario="symlink",created=true,importStatus=a.Status.ToString()});}
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or PlatformNotSupportedException){rows.Add(new{scenario="symlink",created=false,errorType=e.GetType().Name});}
        var second=new MediaRootService(()=>s,new FixedMachine("x2-portable-second-machine"),new MediaRootFileSystem());Assert((await second.GetAsync(root.Root!.RootId))!.Availability==MediaRootAvailability.Unmapped,"new machine unmapped");Assert((await second.RemapAsync(root.Root.RootId,media)).Succeeded,"existing RootId remapped without migration");
        Save(Path.Combine(local,"PATHS.json"),new{rows,checks=Checks});
    }

    static async Task PortableRestore(string local,string source)
    {
        var l=Locations(local);Directory.CreateDirectory(local);Assert(!File.Exists(l.CatalogDatabasePath),"fresh restore root");
        var expected=Snapshot(source);var r=await new SqliteCatalogRecoveryService(l).BeginRestoreAsync(source);Assert(r.Succeeded,"closed transfer restore");Assert((await r.Transaction!.CommitAsync()).Succeeded,"closed transfer installed");
        object runtime;await using(var s=await Catalog(l,false)){using var c=s.OpenConnection();runtime=Runtime(c);Assert(Equal(expected,Snapshot(l.CatalogDatabasePath)),"closed transfer all-table equality");}
        SqliteConnection.ClearAllPools();ClosedCheckpoint(l.CatalogDatabasePath);Save(Path.Combine(local,"RESTORE.json"),new{runtime,checks=Checks,sourceHash=Hash(source)});
    }

}
