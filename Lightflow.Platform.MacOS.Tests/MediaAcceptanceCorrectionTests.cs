using System.Diagnostics;
using System.Runtime.InteropServices;
using Lightflow.Application;
using Lightflow.Domain;
using LightflowStudio;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Lightflow.Platform.MacOS.Tests;

[Collection("Native storage descriptor ownership")]
public sealed class MediaAcceptanceCorrectionTests
{
    private static string Fixture() => Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory,"I04",Guid.NewGuid().ToString("N"))).FullName;
    [DllImport("libLightflowVolumeTests.dylib",EntryPoint="lf_test_cloud_fixture")]
    private static extern void Cloud(string path,int mode);
    [DllImport("libLightflowVolumeTests.dylib",EntryPoint="lf_test_unguarded_cloud_checks")]
    private static extern int Unguarded();
    [DllImport("libLightflowVolumeTests.dylib",EntryPoint="lf_test_materialization_policy")]
    private static extern int Policy();

    [Fact]
    public async Task ResidentCloudMediaAndUnsupportedFoldersPreserveIdentityAndCatalogAdmission()
    {
        var data=Fixture();var media=Directory.CreateDirectory(Path.Combine(data,"Desktop")).FullName;
        var catalog=Directory.CreateDirectory(Path.Combine(data,"Catalog")).FullName;
        var file=Path.Combine(media,"resident.jpg");await File.WriteAllBytesAsync(file,Enumerable.Range(0,150000).Select(i=>(byte)i).ToArray());
        Cloud(media,1);Cloud(file,1);
        try
        {
            await using var shell=new MacShellCatalogSession(data);Assert.True((await shell.CreateAsync(catalog)).IsReady);
            var first=await shell.DiscoverBrowserFolderAsync(media,Guid.Empty,"");var asset=Assert.Single(first.Assets);
            var again=await shell.DiscoverBrowserFolderAsync(media,Guid.Empty,"");Assert.Equal(asset.AssetId,Assert.Single(again.Assets).AssetId);Assert.Equal(first.RootId,again.RootId);
            using var assessor=new MacStorageLocationAssessor([catalog],allowMediaSourceAncestor:true);
            var before=Policy();var unguarded=Unguarded();
            Assert.Equal(await new SampledSourceFingerprintService().CreateAsync(file),assessor.ResidentFingerprint(file,default));
            Assert.Equal(before,Policy());Assert.Equal(unguarded,Unguarded());
            var request=new StorageAssessmentRequest(Guid.NewGuid(),0,StorageRole.ActiveCatalog,StorageOperation.Open,media);
            Assert.NotEqual(StorageLocationDecision.Eligible,StorageLocationPolicy.Evaluate(request,await assessor.AssessAsync(request),DateTimeOffset.UtcNow).Decision);
            var unsupported=Directory.CreateDirectory(Path.Combine(media,"Unsupported")).FullName;
            await File.WriteAllTextAsync(Path.Combine(unsupported,"notes.txt"),"unsupported");
            Assert.Empty((await shell.DiscoverBrowserFolderAsync(unsupported,Guid.Empty,"")).Assets);
            Cloud(file,2);
            await File.WriteAllTextAsync(Path.Combine(media,"local.jpg"),"another resident source");
            var unavailable=await shell.DiscoverBrowserFolderAsync(media,Guid.Empty,"");Assert.EndsWith("local.jpg",Assert.Single(unavailable.Assets).RelativePath);Assert.Null(unavailable.Diagnostic);Assert.Equal(1,unavailable.UnavailableSourceCount);Assert.False(unavailable.Recursive);
            var preserved=(await shell.ReadBrowserCatalogAsync(first.RootId,first.RelativeFolder)).Assets.Single(a=>a.AssetId==asset.AssetId);
            Assert.Equal(asset.AssetId,preserved.AssetId);Assert.Equal(MediaAssetSourceStatus.Available,preserved.SourceStatus);
            Cloud(file,1);Assert.Contains((await shell.DiscoverBrowserFolderAsync(media,Guid.Empty,"")).Assets,a=>a.AssetId==asset.AssetId);
        }
        finally {Cloud(file,0);Cloud(media,0);}
    }

    [Fact]
    public async Task CloudExclusionsAndRealRefusalsRemainDistinctInDirectAndRecursiveDiscovery()
    {
        var data=Fixture();var catalog=Directory.CreateDirectory(Path.Combine(data,"Catalog")).FullName;
        var media=Directory.CreateDirectory(Path.Combine(data,"Documents")).FullName;
        var resident=Path.Combine(media,"resident.jpg");var placeholder=Path.Combine(media,"placeholder.jpg");var alias=Path.Combine(media,"alias.jpg");
        await File.WriteAllTextAsync(resident,"resident source");await File.WriteAllTextAsync(placeholder,"test-only cloud metadata");await File.WriteAllTextAsync(alias,"alias fixture");
        Cloud(media,1);Cloud(resident,1);Cloud(placeholder,2);Cloud(alias,3);
        try
        {
            await using var shell=new MacShellCatalogSession(data);Assert.True((await shell.CreateAsync(catalog)).IsReady);
            var first=await shell.DiscoverBrowserFolderAsync(media,Guid.Empty,"");var asset=Assert.Single(first.Assets);
            Assert.False(first.Recursive);Assert.Equal(1,first.UnavailableSourceCount);Assert.Contains("alias.jpg",first.Diagnostic);Assert.DoesNotContain("placeholder.jpg",first.Diagnostic);
            File.CreateSymbolicLink(Path.Combine(media,"linked.jpg"),resident);
            var partial=await shell.DiscoverBrowserFolderAsync(media,Guid.Empty,"");Assert.Equal(asset.AssetId,Assert.Single(partial.Assets).AssetId);Assert.Contains("link",partial.Diagnostic);
            var nested=Directory.CreateDirectory(Path.Combine(media,"Nested")).FullName;await File.WriteAllTextAsync(Path.Combine(nested,"local.jpg"),"nested resident");
            await shell.SetBrowserRecursiveAsync(first.RootId,first.RelativeFolder,true);
            var recursive=await shell.DiscoverBrowserFolderAsync(media,Guid.Empty,"");Assert.True(recursive.Recursive);Assert.Equal(2,recursive.Assets.Count);Assert.Equal(1,recursive.UnavailableSourceCount);Assert.Contains("alias.jpg",recursive.Diagnostic);Assert.Contains(recursive.Assets,a=>a.AssetId==asset.AssetId);
            await shell.SetBrowserRecursiveAsync(first.RootId,first.RelativeFolder,false);
            var direct=await shell.DiscoverBrowserFolderAsync(media,Guid.Empty,"");Assert.False(direct.Recursive);Assert.Equal(asset.AssetId,Assert.Single(direct.Assets).AssetId);
            Assert.Equal("test-only cloud metadata",await File.ReadAllTextAsync(placeholder));
            await shell.DisposeAsync();
            var started=await new CatalogLifecycle(new MacCatalogLifecycleEffects(data)).StartAsync(allowFirstCreation:false);
            await using(var owner=started.Coordinator!)
            {await new CatalogAssetClassificationStore(()=>owner.Session).UpdateAsync(asset.AssetId,p=>p with {Rating=4});await new CatalogCollectionOrganizationStore(()=>owner.Session).CreateCollectionAsync("I05 retained authored state");}
            await using(var reopened=new MacShellCatalogSession(data))
            {Assert.True((await reopened.StartAsync()).IsReady);Assert.Equal(asset.AssetId,Assert.Single((await reopened.DiscoverBrowserFolderAsync(media,Guid.Empty,"")).Assets).AssetId);}
            var checkedState=await new CatalogLifecycle(new MacCatalogLifecycleEffects(data)).StartAsync(allowFirstCreation:false);
            await using(var owner=checkedState.Coordinator!)
            {Assert.Equal(4,(await new CatalogAssetClassificationStore(()=>owner.Session).GetAsync([asset.AssetId]))[asset.AssetId].Rating);Assert.Single(await new CatalogCollectionOrganizationStore(()=>owner.Session).ListCollectionsAsync());}
        }
        finally {Cloud(alias,0);Cloud(placeholder,0);Cloud(resident,0);Cloud(media,0);}
    }

    [Theory]
    [InlineData(2,"locally")]
    [InlineData(3,"identity")]
    [InlineData(4,"residency")]
    public async Task PlaceholderAliasAndUnknownResidencyRefuseWithoutChangingThreadPolicy(int mode,string message)
    {
        var data=Fixture();var catalog=Directory.CreateDirectory(Path.Combine(data,"Catalog")).FullName;
        var file=Path.Combine(data,"source.jpg");await File.WriteAllTextAsync(file,"must not hydrate");Cloud(file,mode);
        try
        {
            using var assessor=new MacStorageLocationAssessor([catalog],allowMediaSourceAncestor:true);
            var before=Policy();var checks=Unguarded();
            var error=Assert.Throws<IOException>(()=>assessor.ResidentFingerprint(file,default));Assert.Contains(message,error.Message);
            Assert.Equal(before,Policy());Assert.Equal(checks,Unguarded());Assert.Equal("must not hydrate",await File.ReadAllTextAsync(file));
        }
        finally {Cloud(file,0);}
    }

    [Fact]
    public async Task SymlinkAndMissingOrSameVolumeEvidenceNeverExcludeMappings()
    {
        var data=Fixture();var catalog=Directory.CreateDirectory(Path.Combine(data,"Catalog")).FullName;
        var source=Path.Combine(data,"source.jpg");await File.WriteAllTextAsync(source,"resident");
        var link=Path.Combine(data,"link.jpg");File.CreateSymbolicLink(link,source);
        using var assessor=new MacStorageLocationAssessor([catalog],allowMediaSourceAncestor:true);
        Assert.Throws<IOException>(()=>assessor.ResidentFingerprint(link,default));
        Assert.False(assessor.IsUnrelatedOfflineRoot(data,Path.Combine(data,"missing"),default));
        Assert.False(assessor.IsUnrelatedOfflineRoot(data,catalog,default));
        Assert.Throws<IOException>(()=>assessor.IsUnrelatedOfflineRoot(Path.Combine(data,"missing"),"/Volumes/LF030Absent",default));
    }

    [Fact]
    public async Task UnrelatedAbsentVolumeDoesNotBlockNewAnchorAndOfflineAuthoredStateSurvivesRemount()
    {
        var data=Fixture();var catalog=Directory.CreateDirectory(Path.Combine(data,"Catalog")).FullName;
        var label="LF030I04"+Guid.NewGuid().ToString("N")[..10];var image=Path.Combine(data,"media.dmg");var mount="/Volumes/"+label;
        await Run("/usr/bin/hdiutil","create","-quiet","-size","64m","-fs","APFS","-volname",label,image);
        bool attached=false;Guid asset=Guid.Empty,root=Guid.Empty;
        try
        {
            await Run("/usr/bin/hdiutil","attach","-quiet","-nobrowse",image);attached=true;
            var media=Directory.CreateDirectory(Path.Combine(mount,"Media")).FullName;await File.WriteAllTextAsync(Path.Combine(media,"photo.jpg"),"task-owned resident source");
            var absent="/Volumes/LF030Offline"+Guid.NewGuid().ToString("N");
            await using(var shell=new MacShellCatalogSession(data))
            {
                Assert.True((await shell.CreateAsync(catalog)).IsReady);
                await shell.DiscoverBrowserFolderAsync(Directory.CreateDirectory(Path.Combine(data,"Empty")).FullName,Guid.Empty,"");
                // An unavailable persisted mapping is the regression precondition, not a real mount operation.
                using(var db=new SqliteConnection($"Data Source={Path.Combine(catalog,"LightflowCatalog.db")}"))
                {
                    db.Open();using var cmd=db.CreateCommand();cmd.CommandText="INSERT INTO MediaRoots SELECT $id,'Offline preserved','online',CreatedUtc,UpdatedUtc FROM MediaRoots LIMIT 1; INSERT INTO MediaRootMappings SELECT $mapping,$id,MachineId,$path,'online',CreatedUtc,UpdatedUtc FROM MediaRootMappings LIMIT 1;";
                    cmd.Parameters.AddWithValue("$id",Guid.NewGuid().ToString());cmd.Parameters.AddWithValue("$mapping",Guid.NewGuid().ToString());cmd.Parameters.AddWithValue("$path",absent);cmd.ExecuteNonQuery();
                }
                var first=await shell.DiscoverBrowserFolderAsync(media,Guid.Empty,"");asset=Assert.Single(first.Assets).AssetId;root=first.RootId;
                Assert.Contains((await shell.ReadBrowserCatalogAsync()).Roots,r=>r.PhysicalPath==absent);
                Assert.Equal(asset,Assert.Single((await shell.DiscoverBrowserFolderAsync(media,Guid.Empty,"")).Assets).AssetId);
                using var assessor=new MacStorageLocationAssessor([catalog],allowMediaSourceAncestor:true);
                Assert.True(assessor.IsUnrelatedOfflineRoot(mount,absent,default));
                Assert.False(assessor.IsUnrelatedOfflineRoot(mount,Path.Combine(mount,"missing"),default));
                Assert.False(assessor.IsUnrelatedOfflineRoot(mount,mount,default));
                using var paths=new MacBrowserMediaEffects([catalog]).OpenAdmission(mount);
                await paths.ValidateAsync(default);
                Assert.Throws<MediaPathIdentityException>(()=>paths.ConflictingMapping(Guid.NewGuid(),Guid.NewGuid(),mount,true,default));
            }
            var started=await new CatalogLifecycle(new MacCatalogLifecycleEffects(data)).StartAsync(allowFirstCreation:false);
            await using(var owner=started.Coordinator!) {await new CatalogAssetClassificationStore(()=>owner.Session).UpdateAsync(asset,p=>p with {Rating=5});await new CatalogCollectionOrganizationStore(()=>owner.Session).CreateCollectionAsync("I04 authored state");}
            await Run("/usr/bin/hdiutil","detach","-quiet",mount);attached=false;
            await Run("/usr/bin/hdiutil","attach","-quiet","-nobrowse",image);attached=true;
            await using(var shell=new MacShellCatalogSession(data))
            {Assert.True((await shell.StartAsync()).IsReady);var again=await shell.DiscoverBrowserFolderAsync(media,Guid.Empty,"");Assert.Equal(root,again.RootId);Assert.Equal(asset,Assert.Single(again.Assets).AssetId);Assert.Contains((await shell.ReadBrowserCatalogAsync()).Roots,r=>r.PhysicalPath==absent);}
            var reopened=await new CatalogLifecycle(new MacCatalogLifecycleEffects(data)).StartAsync(allowFirstCreation:false);
            await using(var owner=reopened.Coordinator!)
            {Assert.Equal(5,(await new CatalogAssetClassificationStore(()=>owner.Session).GetAsync([asset]))[asset].Rating);Assert.Single(await new CatalogCollectionOrganizationStore(()=>owner.Session).ListCollectionsAsync());}
        }
        finally {if(attached)await Run("/usr/bin/hdiutil","detach","-quiet",mount);}
    }
    private static async Task Run(string program,params string[] args)
    {
        var info=new ProcessStartInfo(program){RedirectStandardError=true,RedirectStandardOutput=true};foreach(var arg in args)info.ArgumentList.Add(arg);
        using var process=Process.Start(info)!;var stderr=process.StandardError.ReadToEndAsync();await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(60));Assert.True(process.ExitCode==0,await stderr);
    }
}
