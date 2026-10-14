using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Lightflow.Platform.MacOS;
using LightflowStudio;

if(args.Length<3||args[0]!="--data-root"||!Path.IsPathFullyQualified(args[1]))
{
    Console.Error.WriteLine("Usage: MacCatalogHost --data-root <absolute task-owned root> create|inspect|collection-add <name>|hold|backup|restore <backup>|move <destination>");
    return 64;
}
if(!OperatingSystem.IsMacOS()||RuntimeInformation.ProcessArchitecture!=Architecture.Arm64) {Console.Error.WriteLine("Apple Silicon macOS is required.");return 69;}
var command=args[2];
if(!new[]{"create","inspect","collection-add","hold","backup","restore","move","lease-probe"}.Contains(command))return 64;
if(new[]{"collection-add","restore","move"}.Contains(command)&&args.Length!=4)return 64;
var effects=new MacCatalogLifecycleEffects(args[1]);

CatalogLifecycle? owner=null;
var timer=Stopwatch.StartNew();
try
{
    if(command=="lease-probe")
    {
        effects.RequireInitializedProfile();
        using var lease=effects.Evidence.OpenOwnershipJournal(effects.Profile.CatalogDatabasePath+".startup-state");
        effects.Evidence.RequireBoundOwnershipJournal(lease,effects.Profile.CatalogDatabasePath+".startup-state");
        Console.WriteLine(JsonSerializer.Serialize(new{result="NATIVE LEASE PROBE",effects.LastOwnershipOpenMilliseconds,sqliteOpened=false}));
        return 0;
    }
    if(command=="create")
    {
        if(!Directory.Exists(effects.Profile.CatalogDirectory)) {Console.Error.WriteLine("REFUSED: prepare an empty Catalog directory under the data root before creation; missing-directory binding transition is unqualified.");return 2;}
        if(File.Exists(effects.Profile.CatalogDatabasePath)||effects.Configuration.TryLoad(out var existing,out _)&&existing.CatalogId is not null) {Console.Error.WriteLine("REFUSED: existing Catalog/configuration retained.");return 2;}
    }
    var started=await new CatalogLifecycle(effects).StartAsync(allowFirstCreation:command=="create");
    owner=started.Coordinator;
    if(!started.IsReady){Console.Error.WriteLine(JsonSerializer.Serialize(new{result="REFUSED",started.Status,started.RecoveryReason,started.Diagnostic}));return 2;}
    var session=owner!.CatalogSession;
    Console.WriteLine(JsonSerializer.Serialize(new{result="OPEN",session.Identity,session.SchemaVersion,session.RuntimePolicy,elapsedMs=timer.Elapsed.TotalMilliseconds,ownershipOpenMs=effects.LastOwnershipOpenMilliseconds,completion="unpaired; every reopen deeply validates"}));
    var collections=new CatalogCollectionOrganizationStore(()=>owner.Session);
    switch(command)
    {
        case "collection-add": Console.WriteLine(JsonSerializer.Serialize(await collections.CreateCollectionAsync(args[3])));break;
        case "inspect":
            Console.WriteLine(JsonSerializer.Serialize(new{collections=await collections.ListCollectionsAsync(),assets=await new CatalogMediaAssetRepository(()=>owner.Session).ListAsync()}));
            var integrity=await owner.Recovery.CheckIntegrityAsync(owner.Locations.CatalogDatabasePath);
            Console.WriteLine(JsonSerializer.Serialize(integrity));if(!integrity.IsValid)return 2;break;
        case "hold": Console.WriteLine("READY: writer held until one line arrives on stdin.");Console.Out.Flush();Console.ReadLine();break;
        case "backup":
            var backup=await owner.BackupForExitAsync(Path.Combine(args[1],"Closed Backups"),keepQuiescent:false);
            Console.WriteLine(JsonSerializer.Serialize(backup));if(!backup.Succeeded)return 2;break;
        case "restore":
            var restore=await owner.RestoreCatalogAsync(args[3]);Console.WriteLine(JsonSerializer.Serialize(restore));if(!restore.Succeeded)return 2;break;
        case "move":
            var moved=await owner.RelocateCatalogAsync(args[3]);Console.WriteLine(JsonSerializer.Serialize(moved));if(!moved.Succeeded)return 2;break;
    }
    return 0;
}
catch(Exception e){Console.Error.WriteLine(JsonSerializer.Serialize(new{result="REFUSED",error=e.GetType().Name,diagnostic=e.Message}));return 2;}
finally
{
    if(owner is not null){timer.Restart();await owner.DisposeAsync(Console.Error.WriteLine);Console.WriteLine(JsonSerializer.Serialize(new{result="CLOSED",elapsedMs=timer.Elapsed.TotalMilliseconds}));}
}
