using LightflowStudio;
using Microsoft.Data.Sqlite;
using System.Diagnostics;
using System.Text.Json;
using System.Runtime.InteropServices;

internal static partial class Program
{
    static string NasOwned(string path)
    {
        var p=Path.GetFullPath(path);var allowed="/Volumes/Development/codex/";
        if(!p.StartsWith(allowed,StringComparison.Ordinal)||!Path.GetFileName(p).StartsWith("x2-live-",StringComparison.Ordinal)||!File.Exists(Path.Combine(p,"X2-OWNED.json")))throw new ArgumentException("Only the orchestrator-created NAS fixture is allowed");
        return p;
    }
    static LightflowStorageLocations NasLocations(string local,string nas)=>LightflowStorageLocations.CreateAtRoot(Owned(local),new(Path.Combine(NasOwned(nas),"catalog"),Path.Combine(local,"Previews")));
    static bool Equal(object a,object b)=>JsonSerializer.Serialize(a)==JsonSerializer.Serialize(b);
    static SqliteConnection DeleteConnection(string db)
    {
        var c=Open(db);Execute(c,"PRAGMA journal_mode=DELETE; PRAGMA synchronous=FULL; PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;");return c;
    }
    static async Task NasLive(string local,string nas)
    {
        nas=NasOwned(nas);Directory.CreateDirectory(local);var l=NasLocations(local,nas);var timings=new List<object>();var w=Stopwatch.StartNew();
        async Task<T> Timed<T>(string label,Func<Task<T>> f){w.Restart();var v=await f();timings.Add(new{operation=label,ms=w.Elapsed.TotalMilliseconds});return v;}
        var rec=new SqliteCatalogRecoveryService(l);object createdRuntime;
        await using(var created=await Timed("production create/migrate",()=>Catalog(l,true)))using(var c=created.OpenConnection())createdRuntime=Runtime(c);
        SqliteConnection.ClearAllPools();ClosedCheckpoint(l.CatalogDatabasePath);
        var source=Owned(Path.Combine(Workspace,"work/data/final-control-export/catalog.db"));var original=Snapshot(source);
        var install=await Timed("install representative closed fixture",()=>rec.BeginRestoreAsync(source,true));Assert(install.Succeeded,"NAS fixture install");Assert((await install.Transaction!.CommitAsync()).Succeeded,"NAS fixture installation commit");
        CatalogDatabaseSession s=await Timed("production open",()=>Catalog(l,false));
        object runtime;object before;using(var c=s.OpenConnection()){runtime=Runtime(c);before=Snapshot(l.CatalogDatabasePath);}
        Assert(Equal(original,before),"all representative authored tables/IDs preserved on NAS installation");
        var repository=new CatalogMediaAssetRepository(()=>s);var assets=await Timed("production list32 assets",()=>repository.ListAsync());Assert(assets.Count==32,"representative32 assets");var id=assets[0].AssetId;
        var descriptions=new CatalogAssetDescriptionStore(()=>s);var current=(await descriptions.GetAsync([id]))[id];
        await Timed("production Notes optimistic write/commit",async()=>{await descriptions.ApplyAsync(new Dictionary<Guid,long>{{id,current.Revision}},new(new Dictionary<AssetDescriptionField,string?>{{AssetDescriptionField.Notes,"X2 live NAS authored commit"}}));return true;});
        using(var c=s.OpenConnection()){w.Restart();var count=Scalar(c,"SELECT COUNT(*) FROM MediaAssetClassifications WHERE Rating>=3");timings.Add(new{operation="representative rating predicate SQL (not Smart evaluator)",ms=w.Elapsed.TotalMilliseconds,count});}
        var authored=Snapshot(l.CatalogDatabasePath);
        using(var a=JsonDocument.Parse(JsonSerializer.Serialize(original)))using(var b=JsonDocument.Parse(JsonSerializer.Serialize(authored)))foreach(var table in a.RootElement.EnumerateObject())if(table.Name!="MediaAssetDescriptions")Assert(Equal(table.Value,b.RootElement.GetProperty(table.Name)),"authored unchanged table "+table.Name);
        Assert((await descriptions.GetAsync([id]))[id].Notes=="X2 live NAS authored commit","production write readback");
        await using(var preview=new PreviewStoreService(l)){await preview.ObserveSourceAsync(id,new(20,1,1,"abcdef"));await preview.SetMetadataAsync(id,new(1,PreviewComponentState.Current,PayloadJson:"{\"x2Nas\":true}"));}
        object previewRuntime;await using(var preview=new PreviewStoreService(l)){Assert((await preview.GetAsync(id))!.MetadataJson=="{\"x2Nas\":true}","machine-local Preview reopen");var method=typeof(PreviewStoreService).GetMethod("OpenConnection",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!;using var c=(SqliteConnection)method.Invoke(preview,null)!;previewRuntime=Runtime(c);}
        var nasBackup=await Timed("live NAS source to NAS production backup",()=>rec.CreateBackupAsync(l.CatalogDatabasePath,CatalogBackupKind.UserRequested));Assert(nasBackup.Succeeded,"NAS to NAS live backup");Assert(Equal(authored,Snapshot(nasBackup.Backup!.Path)),"NAS backup every-table equality");
        var localLocations=l with {CatalogBackupsDirectory=Path.Combine(local,"LocalBackups")};var localRec=new SqliteCatalogRecoveryService(localLocations);
        var localBackup=await Timed("live NAS source to local production backup",()=>localRec.CreateBackupAsync(l.CatalogDatabasePath,CatalogBackupKind.UserRequested));Assert(localBackup.Succeeded,"NAS to local backup");Assert(Equal(authored,Snapshot(localBackup.Backup!.Path)),"local backup every-table equality");
        var reverse=await rec.CreateBackupAsync(source,CatalogBackupKind.UserRequested);Assert(reverse.Succeeded && Equal(original,Snapshot(reverse.Backup!.Path)),"local source to NAS SQLite API backup");
        await s.DisposeAsync();SqliteConnection.ClearAllPools();ClosedCheckpoint(l.CatalogDatabasePath);
        var replacement=await Timed("NAS protected replacement",()=>rec.BeginRestoreAsync(localBackup.Backup!.Path,true));Assert(replacement.Succeeded,"NAS protected replacement install");Assert((await replacement.Transaction!.RollbackAsync()).Succeeded,"NAS replacement rollback");Assert(Equal(authored,Snapshot(l.CatalogDatabasePath)),"replacement rollback all tables");
        await using(var reopened=await Timed("production clean reopen",()=>Catalog(l,false)))Assert(reopened.Identity.CatalogId==s.Identity.CatalogId,"NAS reopen stable CatalogId");
        SqliteConnection.ClearAllPools();ClosedCheckpoint(l.CatalogDatabasePath);
        var offline=l.CatalogDirectory+"-closed-unavailable";Directory.Move(l.CatalogDirectory,offline);
        var unavailable=await new CatalogDatabaseService(l,rec).OpenExistingAsync();var unavailableBackup=await localRec.CreateBackupAsync(l.CatalogDatabasePath,CatalogBackupKind.UserRequested);
        Assert(!unavailable.IsSuccess && !unavailableBackup.Succeeded,"missing closed Catalog fails open and backup");Directory.Move(offline,l.CatalogDirectory);
        await using(var restored=await Catalog(l,false))Assert(restored.Identity.CatalogId==s.Identity.CatalogId,"closed folder return reopen stable");SqliteConnection.ClearAllPools();ClosedCheckpoint(l.CatalogDatabasePath);
        var deleteDir=Path.Combine(nas,"delete-control");Directory.CreateDirectory(deleteDir);var deleteDb=Path.Combine(deleteDir,"catalog.db");File.Copy(localBackup.Backup!.Path,deleteDb,false);
        object deleteRuntime;using(var c=DeleteConnection(deleteDb)){deleteRuntime=Runtime(c);using var tx=c.BeginTransaction();using var cmd=c.CreateCommand();cmd.Transaction=tx;cmd.CommandText="UPDATE MediaAssetDescriptions SET Notes=Notes WHERE AssetId=$id";cmd.Parameters.AddWithValue("$id",id.ToString("D"));cmd.ExecuteNonQuery();tx.Commit();}
        using(var c=DeleteConnection(deleteDb))Assert((string?)Scalar(c,"PRAGMA integrity_check")=="ok","DELETE raw-provider reopen integrity");Assert(Equal(authored,Snapshot(deleteDb)),"DELETE control every-table equality");
        var deleteBackup=await localRec.CreateBackupAsync(deleteDb,CatalogBackupKind.UserRequested);Assert(deleteBackup.Succeeded && Equal(authored,Snapshot(deleteBackup.Backup!.Path)),"DELETE source production backup equality");
        Save(Path.Combine(local,"NAS_LIVE_RESULTS.json"),new{nas,local,architecture=RuntimeInformation.ProcessArchitecture.ToString(),createdRuntime,runtime,previewRuntime,deleteRuntime,nativeImages=NativeImages(),catalogId=s.Identity.CatalogId,assetIdEdited=id,before,after=authored,timings,nasBackup=nasBackup.Backup!.Path,localBackup=localBackup.Backup!.Path,unavailableStatus=unavailable.Status.ToString(),unavailableBackupDiagnostic=unavailableBackup.Diagnostic,checks=Checks,limits=new[]{"Current WAL/NORMAL topology observational only, unsupported by upstream network guidance","DELETE control uses raw pinned-provider transactions, not a modified production factory","folder rename is not SMB disconnect/remount","no kernel32 startup ownership/replace qualification","Preview is independent local root; no NAS Preview run"}});
    }
    static async Task NasAlternative(string local,string nas)
    {
        nas=NasOwned(nas);var l=NasLocations(local,nas);var rec=new SqliteCatalogRecoveryService(l);var times=new List<object>();var watch=Stopwatch.StartNew();
        async Task<T> Time<T>(string label,Func<Task<T>> f){watch.Restart();Console.WriteLine("START "+label);var x=await f();times.Add(new{operation=label,ms=watch.Elapsed.TotalMilliseconds});Console.WriteLine("DONE "+label+" "+watch.Elapsed.TotalMilliseconds);return x;}
        var diagnostic=Path.Combine(nas,"journal-diagnostic.db");object policy;object walResponse;
        using(var c=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=diagnostic,Mode=SqliteOpenMode.ReadWriteCreate,Pooling=false}.ToString()))
        {c.Open();Execute(c,"PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000; PRAGMA synchronous=FULL;");walResponse=Scalar(c,"PRAGMA journal_mode=WAL")!;policy=Runtime(c);}
        var source=Owned(Path.Combine(Workspace,"work/data/final-control-export/catalog.db"));var original=Snapshot(source);
        var install=await Time("production restore into NAS",()=>rec.BeginRestoreAsync(source));Assert(install.Succeeded,"production restore initial representative NAS fixture");Assert((await install.Transaction!.CommitAsync()).Succeeded,"restore commit");
        var currentOpen=await Time("current production NAS open attempt",()=>new CatalogDatabaseService(l,rec).OpenExistingAsync());Assert(!currentOpen.IsSuccess,"current production open fails policy on SMB");SqliteConnection.ClearAllPools();
        var deleteDir=Path.Combine(nas,"delete-control");Directory.CreateDirectory(deleteDir);var db=Path.Combine(deleteDir,"catalog.db");
        var localLocations=l with {CatalogBackupsDirectory=Path.Combine(local,"LocalBackups")};var localRec=new SqliteCatalogRecoveryService(localLocations);
        var installedBackup=await rec.CreateBackupAsync(l.CatalogDatabasePath,CatalogBackupKind.UserRequested);Assert(installedBackup.Succeeded,"restored source NAS production backup");File.Copy(installedBackup.Backup!.Path,db,false);
        Assert(Equal(original,Snapshot(db)),"DELETE input every authored table/ID matches preserved32-asset fixture");
        object runtime;string id;long revisionBefore;watch.Restart();
        using(var c=DeleteConnection(db))
        {
            runtime=Runtime(c);id=Convert.ToString(Scalar(c,"SELECT AssetId FROM MediaAssetDescriptions ORDER BY AssetId LIMIT 1"))!;revisionBefore=Convert.ToInt64(Scalar(c,"SELECT Revision FROM MediaAssetDescriptions WHERE AssetId='"+id+"'"));
            using var tx=c.BeginTransaction();using var cmd=c.CreateCommand();cmd.Transaction=tx;cmd.CommandText="UPDATE MediaAssetDescriptions SET Notes=$note,Revision=Revision+1,UpdatedUtc=$now WHERE AssetId=$id";cmd.Parameters.AddWithValue("$note","X2 DELETE NAS authored edit");cmd.Parameters.AddWithValue("$now",DateTime.UtcNow.ToString("O"));cmd.Parameters.AddWithValue("$id",id);cmd.ExecuteNonQuery();tx.Commit();
        }
        times.Add(new{operation="DELETE raw-provider open/read/write/commit",ms=watch.Elapsed.TotalMilliseconds});
        var authored=Snapshot(db);using(var a=JsonDocument.Parse(JsonSerializer.Serialize(original)))using(var b=JsonDocument.Parse(JsonSerializer.Serialize(authored)))foreach(var table in a.RootElement.EnumerateObject())if(table.Name!="MediaAssetDescriptions")Assert(Equal(table.Value,b.RootElement.GetProperty(table.Name)),"DELETE unchanged authored table "+table.Name);
        using(var c=DeleteConnection(db))
        {
            Assert((string?)Scalar(c,"PRAGMA integrity_check")=="ok","DELETE integrity on reopen");Assert(Convert.ToString(Scalar(c,"SELECT Notes FROM MediaAssetDescriptions WHERE AssetId='"+id+"'"))=="X2 DELETE NAS authored edit","DELETE committed edit reopen");Assert(Convert.ToInt64(Scalar(c,"SELECT Revision FROM MediaAssetDescriptions WHERE AssetId='"+id+"'"))==revisionBefore+1,"DELETE revision increment");
            watch.Restart();var query=Scalar(c,"SELECT COUNT(*) FROM MediaAssetClassifications WHERE Rating>=3");times.Add(new{operation="rating SQL predicate, not Smart service",ms=watch.Elapsed.TotalMilliseconds,count=query});
        }
        var nasBackup=await Time("DELETE live source to NAS production backup",()=>rec.CreateBackupAsync(db,CatalogBackupKind.UserRequested));Assert(nasBackup.Succeeded && Equal(authored,Snapshot(nasBackup.Backup!.Path)),"DELETE NAS backup full equality");
        var localBackup=await Time("DELETE live source to local production backup",()=>localRec.CreateBackupAsync(db,CatalogBackupKind.UserRequested));Assert(localBackup.Succeeded && Equal(authored,Snapshot(localBackup.Backup!.Path)),"DELETE local backup full equality");
        var reverse=await rec.CreateBackupAsync(source,CatalogBackupKind.UserRequested);Assert(reverse.Succeeded && Equal(original,Snapshot(reverse.Backup!.Path)),"local source to NAS SQLite backup full equality");
        var recoveryLocations=l with {CatalogDirectory=deleteDir,CatalogDatabasePath=db};var restoreRec=new SqliteCatalogRecoveryService(recoveryLocations);
        var restored=await Time("DELETE closed protected replacement",()=>restoreRec.BeginRestoreAsync(localBackup.Backup!.Path,true));Assert(restored.Succeeded,"DELETE protected restore");Assert((await restored.Transaction!.RollbackAsync()).Succeeded,"DELETE rollback replacement");Assert(Equal(authored,Snapshot(db)),"DELETE rollback preserves every table");
        object locked;using(var c=DeleteConnection(db))using(var tx=c.BeginTransaction())
        {
            Execute(c,"UPDATE MediaAssetDescriptions SET Notes=Notes WHERE AssetId=$id",("$id",id));
            using var other=Open(db);Execute(other,"PRAGMA busy_timeout=1000;");try{using var cmd=other.CreateCommand();cmd.CommandText="BEGIN IMMEDIATE";cmd.CommandTimeout=1;cmd.ExecuteNonQuery();locked=new{unexpected="acquired"};throw new Exception("contender acquired writer while transaction reserved");}catch(SqliteException ex){locked=new{code=ex.SqliteErrorCode,extended=ex.SqliteExtendedErrorCode,ex.Message};Assert(ex.SqliteErrorCode==5,"same-host writer lock SQLITE_BUSY");}
            tx.Rollback();
        }
        using(var c=DeleteConnection(db))using(var tx=c.BeginTransaction()){Execute(c,"UPDATE MediaAssetDescriptions SET Notes=Notes WHERE AssetId=$id",("$id",id));tx.Rollback();Assert(true,"writer reacquires after release");}
        var offline=deleteDir+"-closed-unavailable";Directory.Move(deleteDir,offline);var badBackup=await localRec.CreateBackupAsync(db,CatalogBackupKind.UserRequested);Assert(!badBackup.Succeeded,"missing source backup fails honestly");Directory.Move(offline,deleteDir);Assert(Equal(authored,Snapshot(db)),"closed folder return full equality");
        object previewRuntime;await using(var preview=new PreviewStoreService(l)){await preview.ObserveSourceAsync(Guid.Parse(id),new(20,1,1,"abcdef"));await preview.SetMetadataAsync(Guid.Parse(id),new(1,PreviewComponentState.Current,PayloadJson:"{\"x2Nas\":true}"));}
        await using(var preview=new PreviewStoreService(l)){Assert((await preview.GetAsync(Guid.Parse(id)))!.MetadataJson=="{\"x2Nas\":true}","independently local Preview reopen");var method=typeof(PreviewStoreService).GetMethod("OpenConnection",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!;using var c=(SqliteConnection)method.Invoke(preview,null)!;previewRuntime=Runtime(c);}
        Save(Path.Combine(local,"NAS_ALTERNATIVE_RESULTS.json"),new{nas,local,walResponse,diagnosticPolicy=policy,productionOpenStatus=currentOpen.Status.ToString(),productionOpenDiagnostic=currentOpen.Diagnostic,runtime,previewRuntime,nativeImages=NativeImages(),assetIdEdited=id,before=original,after=authored,times,locked,localBackup=localBackup.Backup!.Path,nasBackup=nasBackup.Backup!.Path,missingBackupDiagnostic=badBackup.Diagnostic,checks=Checks,limits=new[]{"DELETE is proof-only raw SQL, not the production Catalog connection factory","same-host locks do not qualify mixed-platform ownership","directory rename is not disconnect/remount","Preview local; NAS Preview not tested","network sync/cache/durability not qualified"}});
    }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FileControl(IntPtr db,[MarshalAs(UnmanagedType.LPStr)]string name,int op,out IntPtr value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void FreeSqlite(IntPtr value);
    static void NasVfs(string local,string nas)
    {
        var native=Owned(Path.Combine(Workspace,"tools/X2CatalogProof/bin/Release/net8.0/runtimes/osx-arm64/native/libe_sqlite3.dylib"));var library=NativeLibrary.Load(native);
        try
        {
            var control=Marshal.GetDelegateForFunctionPointer<FileControl>(NativeLibrary.GetExport(library,"sqlite3_file_control"));var free=Marshal.GetDelegateForFunctionPointer<FreeSqlite>(NativeLibrary.GetExport(library,"sqlite3_free"));
            using var c=Open(Path.Combine(NasOwned(nas),"delete-control/catalog.db"),SqliteOpenMode.ReadOnly);var code=control(c.Handle!.DangerousGetHandle(),"main",12,out var value);var vfs=value==IntPtr.Zero?null:Marshal.PtrToStringUTF8(value);if(value!=IntPtr.Zero)free(value);
            Save(Path.Combine(local,"NAS_VFS.json"),new{native,nativeSha256=Hash(native),fileControlCode=code,vfs,runtime=Runtime(c),fullfsync=Scalar(c,"PRAGMA fullfsync"),checkpointFullfsync=Scalar(c,"PRAGMA checkpoint_fullfsync")});
        }
        finally{NativeLibrary.Free(library);}
    }
    static async Task NasCrash(string mode,string local,string nas,string signal,string journal)
    {
        var l=NasLocations(local,nas);var db=journal=="delete"?Path.Combine(NasOwned(nas),"delete-control/catalog.db"):l.CatalogDatabasePath;
        await using var session=journal=="wal"?await Catalog(l,false):null;
        using var c=journal=="wal"?session!.OpenConnection():DeleteConnection(db);
        var id=Convert.ToString(Scalar(c,"SELECT AssetId FROM MediaAssetDescriptions ORDER BY AssetId LIMIT 1"))!;
        if(mode=="nas-crash-child")
        {
            Execute(c,"UPDATE MediaAssetDescriptions SET Notes=$note WHERE AssetId=$id",("$note","X2 "+journal+" committed before kill"),("$id",id));
            Execute(c,"PRAGMA cache_size=10;");using var tx=c.BeginTransaction();using var cmd=c.CreateCommand();cmd.Transaction=tx;cmd.CommandText="UPDATE MediaAssetDescriptions SET Notes=$note";cmd.Parameters.AddWithValue("$note","X2 uncommitted "+new string('z',8192));cmd.ExecuteNonQuery();
            Save(signal,new{pid=Environment.ProcessId,id,journal,ready=true,db,journalExists=File.Exists(db+"-journal"),walExists=File.Exists(db+"-wal")});await Task.Delay(TimeSpan.FromSeconds(45));throw new Exception("Owned parent did not stop child");
        }
        Assert(Convert.ToString(Scalar(c,"SELECT Notes FROM MediaAssetDescriptions WHERE AssetId='"+id+"'"))=="X2 "+journal+" committed before kill","NAS "+journal+" committed survived and uncommitted rollback");Assert((string?)Scalar(c,"PRAGMA integrity_check")=="ok","NAS "+journal+" crash integrity");
        Save(signal,new{runtime=Runtime(c),id,checks=Checks,snapshot=Snapshot(db)});
    }
}
