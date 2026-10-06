using LightflowStudio;
using Microsoft.Data.Sqlite;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

internal static partial class Program
{
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    static string Workspace = "";
    static readonly List<object> Checks = [];
    static readonly List<object> Paths = [];
    static void Assert(bool condition, string name) { Checks.Add(new { name, passed = condition }); if (!condition) throw new Exception(name); }
    static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    static void Save(string path, object value) => File.WriteAllText(path, JsonSerializer.Serialize(value, Json));
    static object? Scalar(SqliteConnection c, string sql) { using var cmd = c.CreateCommand(); cmd.CommandText = sql; return cmd.ExecuteScalar(); }
    static void Execute(SqliteConnection c, string sql, params (string, object?)[] args) { using var cmd = c.CreateCommand(); cmd.CommandText = sql; foreach (var (k,v) in args) cmd.Parameters.AddWithValue(k, v ?? DBNull.Value); cmd.ExecuteNonQuery(); }
    static SqliteConnection Open(string path, SqliteOpenMode mode = SqliteOpenMode.ReadWrite) { var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = mode, Pooling = false }.ToString()); c.Open(); return c; }
    static string Owned(string path) { var full = Path.GetFullPath(path); if (!full.StartsWith(Workspace + Path.DirectorySeparatorChar, StringComparison.Ordinal)) throw new ArgumentException("Proof paths must be inside this clone: " + full); return full; }
    static LightflowStorageLocations Locations(string path) => LightflowStorageLocations.CreateAtRoot(Owned(path));
    static async Task<CatalogDatabaseSession> Catalog(LightflowStorageLocations l, bool create)
    {
        var service = new CatalogDatabaseService(l, new SqliteCatalogRecoveryService(l));
        var r = create ? await service.CreateNewAsync() : await service.OpenExistingAsync();
        Assert(r.IsSuccess && r.Session is not null, "production Catalog " + (create ? "create" : "open") + ": " + r.Status + " " + r.Diagnostic);
        return r.Session!;
    }
    static object Snapshot(string path)
    {
        using var c = Open(path, SqliteOpenMode.ReadOnly);
        var tables = new SortedDictionary<string, object>(StringComparer.Ordinal);
        using var list = c.CreateCommand(); list.CommandText = "SELECT name FROM sqlite_schema WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name";
        var names = new List<string>(); using (var r=list.ExecuteReader()) while(r.Read()) names.Add(r.GetString(0));
        foreach(var name in names)
        {
            using var cmd=c.CreateCommand(); cmd.CommandText="SELECT * FROM \""+name.Replace("\"","\"\"")+"\"";
            using var r=cmd.ExecuteReader(); var columns=Enumerable.Range(0,r.FieldCount).Select(r.GetName).ToArray(); var rows=new List<string>();
            while(r.Read()) rows.Add(JsonSerializer.Serialize(Enumerable.Range(0,r.FieldCount).Select(i=>r.IsDBNull(i)?null:r.GetValue(i)).ToArray()));
            rows.Sort(StringComparer.Ordinal); tables[name]=new { columns, rows };
        }
        return tables;
    }
    static object Runtime(SqliteConnection c)
    {
        var options=new List<string>(); using var cmd=c.CreateCommand(); cmd.CommandText="PRAGMA compile_options"; using(var r=cmd.ExecuteReader()) while(r.Read()) options.Add(r.GetString(0));
        return new { version=Scalar(c,"SELECT sqlite_version()"), sourceId=Scalar(c,"SELECT sqlite_source_id()"), compileOptions=options,
            schema=Scalar(c,"PRAGMA user_version"), applicationId=Scalar(c,"PRAGMA application_id"), journal=Scalar(c,"PRAGMA journal_mode"), locking=Scalar(c,"PRAGMA locking_mode"), synchronous=Scalar(c,"PRAGMA synchronous"), foreignKeys=Scalar(c,"PRAGMA foreign_keys"), busyTimeout=Scalar(c,"PRAGMA busy_timeout"), integrity=Scalar(c,"PRAGMA integrity_check") };
    }
    [DllImport("/usr/lib/libSystem.B.dylib")] static extern uint _dyld_image_count();
    [DllImport("/usr/lib/libSystem.B.dylib")] static extern IntPtr _dyld_get_image_name(uint index);
    static object[] NativeImages()
    {
        if(OperatingSystem.IsWindows()) return System.Diagnostics.Process.GetCurrentProcess().Modules.Cast<System.Diagnostics.ProcessModule>().Where(m=>m.ModuleName.Contains("sqlite",StringComparison.OrdinalIgnoreCase)).Select(m=>(object)new {path=m.FileName,sha256=Hash(m.FileName),onDisk=true}).ToArray();
        if(!OperatingSystem.IsMacOS()) return [];
        var images=new List<object>(); for(uint i=0;i<_dyld_image_count();i++) { var p=Marshal.PtrToStringUTF8(_dyld_get_image_name(i))!; if(p.Contains("sqlite",StringComparison.OrdinalIgnoreCase)) images.Add(new {path=p, sha256=File.Exists(p)?Hash(p):null, onDisk=File.Exists(p)}); } return images.ToArray();
    }
    static void ClosedCheckpoint(string path)
    {
        SqliteConnection.ClearAllPools(); using var c=Open(path); using var cmd=c.CreateCommand(); cmd.CommandText="PRAGMA wal_checkpoint(TRUNCATE)"; using var r=cmd.ExecuteReader(); Assert(r.Read() && r.GetInt32(0)==0,"closed-owner checkpoint");
    }
    static async Task Seed(string data, string output)
    {
        var l=Locations(data); Directory.CreateDirectory(l.ApplicationDataDirectory);
        var media=Owned(Path.Combine(data,"media")); Directory.CreateDirectory(media);
        await using var s=await Catalog(l,true);
        var roots=new MediaRootService(()=>s,new MachineIdentityProvider(l.MachineIdentityPath),new MediaRootFileSystem());
        var root=await roots.CreateAsync("X2 portable corpus",media); Assert(root.Succeeded,"create corpus root");
        var assets=new MediaAssetService(new CatalogMediaAssetRepository(()=>s),roots,new SampledSourceFingerprintService());
        var ids=new List<Guid>();
        for(var i=0;i<32;i++)
        {
            var name=$"asset-{i:00}.mov"; File.WriteAllText(Path.Combine(media,name),$"X2 disposable synthetic asset {i}\n");
            File.SetLastWriteTimeUtc(Path.Combine(media,name),new DateTime(2026,10,5,12,0,0,DateTimeKind.Utc));
            var a=await assets.CreateAsync(root.Root!.RootId,name,"video"); Assert(a.Succeeded,"create corpus asset "+i); var id=a.Asset!.Asset.AssetId; ids.Add(id);
            await new CatalogAssetClassificationStore(()=>s).SaveAsync(new(id,i%6, i%2==0?AssetFlag.Picked:AssetFlag.Unflagged,AssetColorLabel.Blue,["X2","café","東京"]));
            await new CatalogAssetDescriptionStore(()=>s).ApplyAsync(new Dictionary<Guid,long>{{id,0}},new(new Dictionary<AssetDescriptionField,string?>{{AssetDescriptionField.Title,$"X2 title {i}"},{AssetDescriptionField.Description,"Unicode café / cafe\u0301 / 東京"},{AssetDescriptionField.Notes,"Catalog-owned\nNo media sidecars"},{AssetDescriptionField.CreatorOverride,"Jeremy"},{AssetDescriptionField.CreditOverride,"X2 proof"}}));
            var marker=await new CatalogMarkerService(()=>s).CreateAsync(id,TimeSpan.FromSeconds(2));
            await new CatalogMarkerService(()=>s).RenameAsync(marker.Marker.MarkerId,marker.Marker.Revision,"X2 marker");
            await new CatalogAssetVideoRotationStore(()=>s).RotateAsync(new Dictionary<Guid,long>{{id,0}},true);
        }
        var org=new CatalogCollectionOrganizationService(()=>s); var set=await org.CreateSetAsync("X2 Set"); var col=await org.CreateCollectionAsync("X2 Static",set.CollectionSetId); await org.AddMembershipsAsync(col.CollectionId,ids);
        await new CatalogBrowserRecursiveRootRepository(()=>s).CreateAsync(root.Root!.RootId,"");
        // Supplemental rows exercise persistence of otherwise UI/media-bound authoring contracts.
        // These are fixture SQL, not production service qualification or NLE dispatch.
        using(var c=s.OpenConnection())
        {
            var now=DateTime.UtcNow.ToString("O"); var lut=Guid.NewGuid().ToString("D");
            var cube="TITLE \"X2 identity\"\nLUT_3D_SIZE 2\n0 0 0\n1 0 0\n0 1 0\n1 1 0\n0 0 1\n1 0 1\n0 1 1\n1 1 1\n";
            Directory.CreateDirectory(Owned(output)); File.WriteAllText(Path.Combine(output,"X2.cube"),cube);
            Execute(c,"INSERT INTO LutResources VALUES($id,'X2 LUT','X2.cube',$hash,'3d',2,$now,$now)",("$id",lut),("$hash",Hash(Path.Combine(output,"X2.cube")).ToLowerInvariant()),("$now",now));
            var smart=Guid.NewGuid().ToString("D"); Execute(c,"INSERT INTO Collections VALUES($id,$parent,'X2 Smart',1,1,$now,$now,1)",("$id",smart),("$parent",set.CollectionSetId.ToString("D")),("$now",now));
            Execute(c,"INSERT INTO SmartCollectionDefinitions VALUES($id,0,$root,'',NULL,1,$query)",("$id",smart),("$root",root.Root.RootId.ToString("D")),("$query","{\"Version\":3,\"MatchMode\":\"All\",\"Filters\":[{\"Field\":\"Rating\",\"NumberValue\":3,\"Comparison\":\"GreaterThanOrEqual\"}]}"));
            foreach(var id in ids)
            {
                var a=id.ToString("D"); Execute(c,"INSERT INTO MediaAssetRanges VALUES($id,$a,'primary',0,10000000,50000000,100000000,$now,$now)",("$id",Guid.NewGuid().ToString("D")),("$a",a),("$now",now));
                Execute(c,"INSERT INTO Subclips VALUES($id,$a,'X2 Subclip',0,10000000,50000000,100000000,1,$now,$now)",("$id",Guid.NewGuid().ToString("D")),("$a",a),("$now",now));
                Execute(c,"INSERT INTO MediaAssetPreferredFrames VALUES($a,20000000,1,$now,$now)",("$a",a),("$now",now));
                Execute(c,"INSERT INTO MediaAssetColor VALUES($a,1,$lut,NULL,$now,$now)",("$a",a),("$lut",lut),("$now",now));
            }
            Execute(c,"INSERT INTO PremiereHandoffs VALUES('x2-asset-op','x2-disposable-destination',$a,'{\"fixture\":true}',NULL,0)",("$a",ids[0].ToString("D")));
            Execute(c,"INSERT INTO PremiereSubclipHandoffs VALUES('x2-subclip-op','x2-disposable-destination',$a,'x2-key','{\"fixture\":true}',NULL,0)",("$a",ids[0].ToString("D")));
            Execute(c,"INSERT INTO PremiereMarkerHandoffs VALUES('x2-marker-op','x2-disposable-destination',$a,$marker,'x2-key','{\"fixture\":true}',NULL,0)",("$a",ids[0].ToString("D")),("$marker",(await new CatalogMarkerService(()=>s).ListAsync(ids[0]))[0].MarkerId.ToString("D")));
        }
        using(var q=await s.Mutations.QuiesceAsync())
        {
            var backup=await new SqliteCatalogRecoveryService(l).CreateBackupAsync(l.CatalogDatabasePath,CatalogBackupKind.UserRequested); Assert(backup.Succeeded,"production online backup");
            Directory.CreateDirectory(Owned(output)); File.Copy(backup.Backup!.Path,Path.Combine(output,"catalog.db"),false);
            Save(Path.Combine(output,"snapshot.json"),Snapshot(Path.Combine(output,"catalog.db")));
            Save(Path.Combine(output,"manifest.json"),new { phase=OperatingSystem.IsWindows()?"windows-origin":"mac-local-control-only", catalogId=s.Identity.CatalogId, rootId=root.Root.RootId, assetIds=ids, databaseSha256=Hash(Path.Combine(output,"catalog.db")), source="aed6c2906637c8a5a9d71c1c18ac24bed48a8724", mediaRoot=media, count=32 });
        }
        await s.DisposeAsync(); ClosedCheckpoint(l.CatalogDatabasePath);
    }
    static async Task Probe(string data, string media)
    {
        var l=Locations(data); media=Owned(media); Directory.CreateDirectory(media);
        await using var s=await Catalog(l,true);
        var roots=new MediaRootService(()=>s,new MachineIdentityProvider(l.MachineIdentityPath),new MediaRootFileSystem()); var root=await roots.CreateAsync("Path probes",media); Assert(root.Succeeded,"create path root");
        var assets=new MediaAssetService(new CatalogMediaAssetRepository(()=>s),roots,new SampledSourceFingerprintService());
        async Task Pair(string label,string a,string b)
        {
            var pa=Path.Combine(media,a);var pb=Path.Combine(media,b); Directory.CreateDirectory(Path.GetDirectoryName(pa)!); Directory.CreateDirectory(Path.GetDirectoryName(pb)!);
            File.WriteAllText(pa,"FIRST "+label);File.WriteAllText(pb,"SECOND "+label+" longer");
            var samePhysical=File.ReadAllText(pa)==File.ReadAllText(pb);
            var first=await assets.CreateAsync(root.Root!.RootId,a,"video"); var second=await assets.CreateAsync(root.Root.RootId,b,"video"); var found=await assets.FindAsync(root.Root.RootId,b);
            Paths.Add(new {label,a,b,samePhysical,firstStatus=first.Status.ToString(),secondStatus=second.Status.ToString(),requestedHash=Hash(pb),resolvedHash=found?.PhysicalPath is {} p && File.Exists(p)?Hash(p):null,firstId=first.Asset?.Asset.AssetId,lookupId=found?.Asset.AssetId,physicalPath=found?.PhysicalPath,keyA=MediaPathSemantics.RelativePathKey(a),keyB=MediaPathSemantics.RelativePathKey(b)});
        }
        await Pair("case-only","Case.mov","case.mov");
        await Pair("NFC-NFD","café.mov","cafe\u0301.mov");
        await Pair("literal-backslash","nested/name.mov","nested\\name.mov");
        await Pair("leading-trailing-space","trim.mov"," trim.mov ");
        foreach(var name in new[]{"CON.mov","colon:name.mov","question?.mov","trailing-dot.mov.","emoji-😀.mov"}) {File.WriteAllText(Path.Combine(media,name),name);var a=await assets.CreateAsync(root.Root!.RootId,name,"video");Paths.Add(new{label="legal-name",name,status=a.Status.ToString(),id=a.Asset?.Asset.AssetId});}
        var outside=Owned(Path.Combine(Path.GetDirectoryName(media)!,"outside-fixture"));Directory.CreateDirectory(outside);File.WriteAllText(Path.Combine(outside,"escape.mov"),"task-owned outside logical root");
        Directory.CreateSymbolicLink(Path.Combine(media,"link-out"),outside);
        var linked=await assets.CreateAsync(root.Root!.RootId,"link-out/escape.mov","video"); Paths.Add(new{label="symlink escape",status=linked.Status.ToString(),path=linked.Asset?.PhysicalPath,target=outside});
        var rootId=root.Root.RootId; var before=await assets.ListAsync(); Directory.Move(media,media+"-offline"); var offline=await roots.GetAsync(rootId); var known=await assets.ListAsync(); Assert(offline!.Availability==MediaRootAvailability.Unavailable && known.Count==before.Count,"offline root retains assets"); Directory.Move(media+"-offline",media);Assert((await roots.GetAsync(rootId))!.Availability==MediaRootAvailability.Online,"remounted root becomes online");
        var secondMachine=new MediaRootService(()=>s,new FixedMachine("x2-second-machine"),new MediaRootFileSystem());Assert((await secondMachine.GetAsync(rootId))!.Availability==MediaRootAvailability.Unmapped,"new machine has no physical mapping");Assert((await secondMachine.RemapAsync(rootId,media)).Succeeded,"second machine remaps existing RootId");Assert((await secondMachine.GetAsync(rootId))!.RootId==rootId,"remap preserves RootId");
        object catalogRuntime; using(var c=s.OpenConnection()){catalogRuntime=Runtime(c);Assert((string?)Scalar(c,"SELECT sqlite_version()") == "3.53.3","actual pinned native SQLite version");}
        var previewId=before[0].AssetId; await using(var preview=new PreviewStoreService(l)) {await preview.ObserveSourceAsync(previewId,new(20,1,1,"abcdef"));await preview.SetMetadataAsync(previewId,new(1,PreviewComponentState.Current,PayloadJson:"{\"x2\":true}"));await preview.SetArtifactAsync(previewId,PreviewArtifactKind.Thumbnail,new(1,PreviewComponentState.Current,"thumbnails/x2.bin"));}
        object previewRuntime; await using(var preview=new PreviewStoreService(l)){Assert((await preview.GetAsync(previewId))!.MetadataJson=="{\"x2\":true}","production Preview persistence reopen"); var method=typeof(PreviewStoreService).GetMethod("OpenConnection",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!;using var c=(SqliteConnection)method.Invoke(preview,null)!;previewRuntime=Runtime(c);}
        Save(Path.Combine(data,"runtime.json"),new {architecture=RuntimeInformation.ProcessArchitecture.ToString(),os=RuntimeInformation.OSDescription,framework=RuntimeInformation.FrameworkDescription,catalog=catalogRuntime,preview=previewRuntime,nativeImages=NativeImages(),checks=Checks}); Save(Path.Combine(data,"paths.json"),Paths);
        var backup=await new SqliteCatalogRecoveryService(l).CreateBackupAsync(l.CatalogDatabasePath,CatalogBackupKind.UserRequested);Assert(backup.Succeeded,"path Catalog backup");var original=JsonSerializer.Serialize(Snapshot(backup.Backup!.Path));
        await s.DisposeAsync();ClosedCheckpoint(l.CatalogDatabasePath);
        var restore=await new SqliteCatalogRecoveryService(l).BeginRestoreAsync(backup.Backup.Path,true);Assert(restore.Succeeded,"closed-owner protected restore");Assert((await restore.Transaction!.RollbackAsync()).Succeeded,"restore rollback");
        await using(var reopened=await Catalog(l,false)) Assert(reopened.Identity.CatalogId==s.Identity.CatalogId,"reopen identity stable");
        Assert(JsonSerializer.Serialize(Snapshot(l.CatalogDatabasePath))==original,"backup restore rollback all-table equality"); Save(Path.Combine(data,"checks.json"),Checks);
    }
    sealed class FixedMachine(string value):IMachineIdentityProvider {public string GetMachineId()=>value;}
    static async Task Crash(string mode,string data,string signal,string? catalog=null)
    {
        var l=catalog is null?Locations(data):PortableLocations(data,catalog);await using var s=await Catalog(l,false);var assets=await new CatalogMediaAssetRepository(()=>s).ListAsync();var id=assets[0].AssetId;
        var store=new CatalogAssetDescriptionStore(()=>s);
        if(mode=="crash-child")
        {
            var current=(await store.GetAsync([id]))[id];await store.ApplyAsync(new Dictionary<Guid,long>{{id,current.Revision}},new(new Dictionary<AssetDescriptionField,string?>{{AssetDescriptionField.Notes,"X2 committed before kill"}}));
            using var c=s.OpenConnection();using var tx=c.BeginTransaction();using var cmd=c.CreateCommand();cmd.Transaction=tx;cmd.CommandText="UPDATE MediaAssetDescriptions SET Notes='X2 uncommitted before kill' WHERE AssetId=$id";cmd.Parameters.AddWithValue("$id",id.ToString("D"));cmd.ExecuteNonQuery();
            Save(signal,new{pid=Environment.ProcessId,assetId=id,ready=true});await Task.Delay(TimeSpan.FromSeconds(45));throw new Exception("Parent failed to terminate owned crash child");
        }
        var after=(await store.GetAsync([id]))[id];Assert(after.Notes=="X2 committed before kill","committed edit survives and uncommitted edit rolls back after SIGKILL");using(var c=s.OpenConnection())Assert((string?)Scalar(c,"PRAGMA integrity_check")=="ok","post-kill integrity");await s.DisposeAsync();ClosedCheckpoint(l.CatalogDatabasePath);Save(signal,new{checks=Checks,assetId=id,after});
    }
    static async Task RoundTrip(string phase,string data,string input,string output)
    {
        input=Owned(input); output=Owned(output); var l=Locations(data);
        using var manifest=JsonDocument.Parse(File.ReadAllText(Path.Combine(input,"manifest.json")));
        Assert(manifest.RootElement.GetProperty("phase").GetString()==(phase switch {"mac-leg"=>"windows-origin","windows-nas-edit"=>"mac-nas-origin","mac-nas-return"=>"windows-nas-return",_=>"mac-return"}),"correct prior round-trip leg");
        Assert(Hash(Path.Combine(input,"catalog.db"))==manifest.RootElement.GetProperty("databaseSha256").GetString(),"incoming database SHA256");
        Assert(File.ReadAllText(Path.Combine(input,"snapshot.json"))==JsonSerializer.Serialize(Snapshot(Path.Combine(input,"catalog.db")),Json),"incoming snapshot equality");
        Directory.CreateDirectory(l.ApplicationDataDirectory);
        var installed=await new SqliteCatalogRecoveryService(l).BeginRestoreAsync(Path.Combine(input,"catalog.db"));Assert(installed.Succeeded,"install incoming SQLite snapshot");Assert((await installed.Transaction!.CommitAsync()).Succeeded,"commit incoming installation");
        await using var s=await Catalog(l,false);Assert(s.Identity.CatalogId.ToString("D")==manifest.RootElement.GetProperty("catalogId").GetString(),"incoming CatalogId preserved");
        Assert(JsonSerializer.Serialize(Snapshot(l.CatalogDatabasePath),Json)==File.ReadAllText(Path.Combine(input,"snapshot.json")),"before-operation all-table equality");
        var editing=phase is "mac-leg" or "windows-nas-edit";var note=phase=="mac-leg"?"X2 Mac return-leg authored note":"X2 Windows NAS-fixture return note";
        var root=manifest.RootElement.GetProperty("rootId").GetGuid();var ids=manifest.RootElement.GetProperty("assetIds").EnumerateArray().Select(x=>x.GetGuid()).ToArray();
        if(editing)
        {
            if(phase=="mac-leg") Assert(OperatingSystem.IsMacOS() && RuntimeInformation.ProcessArchitecture==Architecture.Arm64,"Mac leg executes on arm64 Mac");else Assert(OperatingSystem.IsWindows(),"Windows fixture leg executes on Windows");
            var media=Owned(Path.Combine(data,"media"));Directory.CreateDirectory(media);
            for(var i=0;i<32;i++){var path=Path.Combine(media,$"asset-{i:00}.mov");File.WriteAllText(path,$"X2 disposable synthetic asset {i}\n");File.SetLastWriteTimeUtc(path,new DateTime(2026,10,5,12,0,0,DateTimeKind.Utc));}
            var roots=new MediaRootService(()=>s,new MachineIdentityProvider(l.MachineIdentityPath),new MediaRootFileSystem());Assert((await roots.GetAsync(root))!.Availability==MediaRootAvailability.Unmapped,"foreign machine mapping not used");Assert((await roots.RemapAsync(root,media)).Succeeded,"map portable root on this machine");
            var assets=new MediaAssetService(new CatalogMediaAssetRepository(()=>s),roots,new SampledSourceFingerprintService());
            for(var i=0;i<32;i++){var a=await assets.GetAsync(ids[i]);Assert(a?.SourceExists==true && File.ReadAllText(a.PhysicalPath!)==$"X2 disposable synthetic asset {i}\n","stable AssetId resolves expected bytes "+i);}
            var store=new CatalogAssetDescriptionStore(()=>s);var current=(await store.GetAsync([ids[0]]))[ids[0]];await store.ApplyAsync(new Dictionary<Guid,long>{{ids[0],current.Revision}},new(new Dictionary<AssetDescriptionField,string?>{{AssetDescriptionField.Notes,note}}));
        }
        else if(phase=="mac-nas-return") Assert(OperatingSystem.IsMacOS() && RuntimeInformation.ProcessArchitecture==Architecture.Arm64,"returned fixture inspected on arm64 Mac");else Assert(OperatingSystem.IsWindows(),"return inspection executes on Windows");
        using(var before=JsonDocument.Parse(File.ReadAllText(Path.Combine(input,"snapshot.json"))))
        using(var after=JsonDocument.Parse(JsonSerializer.Serialize(Snapshot(l.CatalogDatabasePath))))
        {
            foreach(var table in before.RootElement.EnumerateObject())
            {
                var expected=table.Value;var actual=after.RootElement.GetProperty(table.Name);
                if(editing && table.Name=="MediaRootMappings")
                {
                    var old=expected.GetProperty("rows").EnumerateArray().Select(x=>x.GetString()!).ToArray();var next=actual.GetProperty("rows").EnumerateArray().Select(x=>x.GetString()!).ToArray();Assert(next.Length==old.Length+1 && old.All(next.Contains),"only one mapping added, original mappings unchanged");
                }
                else if(editing && table.Name=="MediaAssetDescriptions")
                {
                    var old=expected.GetProperty("rows").EnumerateArray().Select(x=>x.GetString()!).ToArray();var next=actual.GetProperty("rows").EnumerateArray().Select(x=>x.GetString()!).ToArray();Assert(next.Length==old.Length,"description row count unchanged");
                    for(var i=0;i<old.Length;i++)
                    {
                        using var a=JsonDocument.Parse(old[i]);using var b=JsonDocument.Parse(next[i]);var ar=a.RootElement;var br=b.RootElement;
                        if(ar[0].GetString()==ids[0].ToString("D"))
                        {
                            for(var j=0;j<ar.GetArrayLength();j++)if(j is not (3 or 6 or 8))Assert(ar[j].GetRawText()==br[j].GetRawText(),"authored description field preserved "+j);
                            Assert(br[3].GetString()==note && br[6].GetInt64()==ar[6].GetInt64()+1,"expected Mac authored mutation");
                        }
                        else Assert(old[i]==next[i],"other asset description unchanged");
                    }
                }
                else Assert(JsonSerializer.Serialize(expected)==JsonSerializer.Serialize(actual),"round-trip table unchanged "+table.Name);
            }
        }
        Directory.CreateDirectory(output);
        object runtime;using(var c=s.OpenConnection())runtime=Runtime(c);Save(Path.Combine(output,"runtime.json"),new{runtime,nativeImages=NativeImages(),processArchitecture=RuntimeInformation.ProcessArchitecture.ToString()});
        using(var q=await s.Mutations.QuiesceAsync())
        {
            var b=await new SqliteCatalogRecoveryService(l).CreateBackupAsync(l.CatalogDatabasePath,CatalogBackupKind.UserRequested);Assert(b.Succeeded,"outgoing consistent snapshot");File.Copy(b.Backup!.Path,Path.Combine(output,"catalog.db"),false);Save(Path.Combine(output,"snapshot.json"),Snapshot(Path.Combine(output,"catalog.db")));
            File.Copy(Path.Combine(input,"X2.cube"),Path.Combine(output,"X2.cube"),false);
            Save(Path.Combine(output,"manifest.json"),new{phase=phase switch {"mac-leg"=>"mac-return","windows-nas-edit"=>"windows-nas-return","mac-nas-return"=>"mac-nas-return-verified",_=>"windows-return-verified"},catalogId=s.Identity.CatalogId,rootId=root,assetIds=ids,databaseSha256=Hash(Path.Combine(output,"catalog.db")),inputDatabaseSha256=Hash(Path.Combine(input,"catalog.db")),source="aed6c2906637c8a5a9d71c1c18ac24bed48a8724",count=32,protocolDifferences=editing?new[]{"MediaRootMappings: one current-machine row added; original mappings unchanged","MediaAssetDescriptions: first asset Notes/Revision/UpdatedUtc only"}:Array.Empty<string>()});
        }
        await s.DisposeAsync();ClosedCheckpoint(l.CatalogDatabasePath);Save(Path.Combine(output,"checks.json"),Checks);
    }
    public static async Task<int> Main(string[] args)
    {
        Workspace=Path.GetFullPath(Environment.GetEnvironmentVariable("X2_WORKSPACE") ?? Directory.GetCurrentDirectory());
        try
        {
            if(args.Length<3) throw new ArgumentException("seed <task-data-root> <output-dir> | probe <task-data-root> <media-dir> | snapshot <db> <json>");
            if(args[0]=="local-portability") await LocalPortability(Owned(args[1]),Owned(args[2]),Owned(args[3]),args[4]);
            else if(args[0] is "portable-crash-child" or "portable-crash-recover") await Crash(args[0].Replace("portable-",""),Owned(args[1]),Owned(args[3]),Owned(args[2]));
            else if(args[0]=="portable-restore") await PortableRestore(Owned(args[1]),Owned(args[2]));
            else if(args[0]=="portable-paths") await PortablePaths(Owned(args[1]),Owned(args[2]));
            else if(args[0]=="portable-contend") PortableContend(Owned(args[1]),Owned(args[2]));
            else if(args[0]=="nas-vfs") NasVfs(Owned(args[1]),args[2]);
            else if(args[0]=="nas-alternative") await NasAlternative(Owned(args[1]),args[2]);
            else if(args[0]=="nas-live") await NasLive(Owned(args[1]),args[2]);
            else if(args[0] is "nas-crash-child" or "nas-crash-recover") await NasCrash(args[0],Owned(args[1]),args[2],Owned(args[3]),args[4]);
            else if(args[0]=="seed") await Seed(Owned(args[1]),Owned(args[2]));
            else if(args[0]=="probe") await Probe(Owned(args[1]),Owned(args[2]));
            else if(args[0]=="snapshot") Save(Owned(args[2]),Snapshot(Owned(args[1])));
            else if(args[0] is "mac-leg" or "windows-return" or "windows-nas-edit" or "mac-nas-return") {if(args.Length!=4)throw new ArgumentException("phase data input output");await RoundTrip(args[0],Owned(args[1]),Owned(args[2]),Owned(args[3]));}
            else if(args[0] is "crash-child" or "crash-recover") await Crash(args[0],Owned(args[1]),Owned(args[2]));
            else throw new ArgumentException("Unknown mode");
            Console.WriteLine(JsonSerializer.Serialize(new{mode=args[0],checks=Checks.Count,status="completed"}));return 0;
        }
        catch(Exception e){Console.Error.WriteLine(e);return 1;}
        finally{SqliteConnection.ClearAllPools();}
    }
}
