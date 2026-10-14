using System.Text.Json;
using Lightflow.Domain;
using Lightflow.Platform.MacOS;
using LightflowStudio;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Lightflow.Platform.MacOS.Tests;

[Collection("Native storage descriptor ownership")]
public sealed class MacCatalogAuthoredStateTests
{
    [Fact]
    public async Task NativeCurrentSchemaReopeningPreservesNonemptyAuthoredStateAndBoundedNotesEdit()
    {
        var temp=Path.GetTempPath();if(temp.StartsWith("/var/",StringComparison.Ordinal))temp="/private"+temp;
        var root=Path.Combine(temp,"lf015-authored-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root,"Catalog"));
        CatalogLifecycle? owner=null;
        try
        {
            var opened=await new CatalogLifecycle(new MacCatalogLifecycleEffects(root)).StartAsync();
            Assert.True(opened.IsReady,opened.Diagnostic);owner=opened.Coordinator!;
            var catalogId=owner.CatalogSession.Identity.CatalogId;
            var rootId=Guid.NewGuid();var assetId=Guid.NewGuid();var copiedId=Guid.NewGuid();
            var now=DateTimeOffset.UtcNow;
            // Test-only baseline Root/machine mapping. No Mac root creation/remap qualification is inferred.
            await owner.Mutations.RunAsync(()=>Task.Run(()=>
            {
                using var connection=owner.CatalogSession.OpenConnection();using var transaction=connection.BeginTransaction();
                using var command=connection.CreateCommand();command.Transaction=transaction;
                command.CommandText="INSERT INTO MediaRoots VALUES($id,'LF015 fixture Root','online',$now,$now); INSERT INTO MediaRootMappings VALUES($mapping,$id,'LF015 fixture machine',$path,'online',$now,$now);";
                command.Parameters.AddWithValue("$id",rootId.ToString("D"));command.Parameters.AddWithValue("$mapping",Guid.NewGuid().ToString("D"));command.Parameters.AddWithValue("$path",Path.Combine(root,"fixture-media"));command.Parameters.AddWithValue("$now",now.UtcDateTime.ToString("O"));command.ExecuteNonQuery();transaction.Commit();
            }));
            var assets=new CatalogMediaAssetRepository(()=>owner.Session);
            Assert.Equal(MediaAssetOperationStatus.Succeeded,await assets.CreateAsync(new(assetId,rootId,"fixture.mov","FIXTURE.MOV","video",32,now.UtcTicks,new(1,"LF015 fixture fingerprint"),MediaAssetSourceStatus.Available,now,now,now)));
            Assert.Equal(MediaAssetOperationStatus.Succeeded,await assets.CreateAsync(new(copiedId,rootId,"copy.mov","COPY.MOV","video",32,now.UtcTicks,new(1,"LF015 copy fixture fingerprint"),MediaAssetSourceStatus.Available,now,now,now)));
            var classification=new CatalogAssetClassificationStore(()=>owner.Session);
            await classification.SaveAsync(new(assetId,4,AssetFlag.Picked,AssetColorLabel.Blue,["LF015","native persistence"]));
            var descriptions=new CatalogAssetDescriptionStore(()=>owner.Session);
            await descriptions.ApplyAsync(new Dictionary<Guid,long>{{assetId,0}},new(new Dictionary<AssetDescriptionField,string?>{{AssetDescriptionField.Title,"LF015 fixture title"},{AssetDescriptionField.Description,"Executed native qualification fixture"},{AssetDescriptionField.Notes,"Before controlled edit"},{AssetDescriptionField.CreatorOverride,"LF015"},{AssetDescriptionField.CreditOverride,"Task-owned synthetic media"}}));
            var range=new MediaRange(TimeSpan.FromSeconds(10),TimeSpan.FromSeconds(1),TimeSpan.FromSeconds(4));
            await new CatalogMediaRangeStore(()=>owner.Session).SaveAsync(assetId,range);
            var subclips=new CatalogSubclipService(()=>owner.Session);await subclips.CreateAsync(assetId,range);
            await new CatalogMarkerService(()=>owner.Session).CreateAsync(assetId,TimeSpan.FromSeconds(2));
            // Contract-level timestamp fixture; no decoded media/Preview peer qualification.
            await new CatalogPreferredPreviewFrameStore(()=>owner.Session).SetAsync(assetId,new(TimeSpan.FromSeconds(3)),TimeSpan.FromSeconds(10));
            var lutDirectory=Path.Combine(root,"external-luts");Directory.CreateDirectory(lutDirectory);
            File.WriteAllText(Path.Combine(lutDirectory,"fixture.cube"),"TITLE \"LF015 identity fixture\"\nLUT_3D_SIZE 2\n0 0 0\n1 0 0\n0 1 0\n1 1 0\n0 0 1\n1 0 1\n0 1 1\n1 1 1\n");
            using var cache=new ApplicationLutLibraryCache(new CatalogFolderLutLibrary(()=>owner.Session));
            await cache.InitializeAsync(lutDirectory,lutDirectory);
            var lut=Assert.Single(cache.Snapshot(ColorLutStage.Camera).Resources);
            var color=new CatalogAssetColorStore(()=>owner.Session,cache);
            await color.SetStageAsync([assetId],ColorLutStage.Camera,lut.LutId);
            await color.SetStageAsync([assetId],ColorLutStage.Creative,lut.LutId);
            var collections=new CatalogCollectionOrganizationStore(()=>owner.Session);
            var collection=await collections.CreateCollectionAsync("LF015 authored members");await collections.AddMembershipAsync(collection.CollectionId,assetId);
            await new CatalogAssetCopyStore(()=>owner.Session).CloneCatalogAsync(assetId,copiedId);
            Assert.Equal(range,await new CatalogMediaRangeStore(()=>owner.Session).RestoreAsync(copiedId));
            Assert.Single(await new CatalogSubclipService(()=>owner.Session).ListAsync(copiedId));
            var before=Logical(owner.CatalogSession);
            foreach(var table in new[]{"MediaRoots","MediaRootMappings","MediaAssets","Collections","CollectionAssets","MediaAssetClassifications","MediaAssetDescriptions","MediaAssetRanges","Subclips","TimelineMarkers","MediaAssetPreferredFrames","MediaAssetColor","LutResources"})
                Assert.NotEmpty(before[table]);
            var backup=await owner.Recovery.CreateBackupAsync(owner.Locations.CatalogDatabasePath,CatalogBackupKind.UserRequested);Assert.True(backup.Succeeded,backup.Diagnostic);
            var metadata=File.ReadAllText(backup.Backup!.Path+".metadata.json");
            await owner.DisposeAsync();owner=null;
            opened=await new CatalogLifecycle(new MacCatalogLifecycleEffects(root)).StartAsync(allowFirstCreation:false);Assert.True(opened.IsReady,opened.Diagnostic);owner=opened.Coordinator!;
            Assert.Equal(catalogId,owner.CatalogSession.Identity.CatalogId);EqualLogical(before,Logical(owner.CatalogSession));
            Assert.Equal(metadata,File.ReadAllText(backup.Backup.Path+".metadata.json"));
            Assert.Equal(rootId,(await new CatalogMediaAssetRepository(()=>owner.Session).GetAsync(assetId))!.RootId);
            descriptions=new CatalogAssetDescriptionStore(()=>owner.Session);var description=(await descriptions.GetAsync([assetId]))[assetId];
            await descriptions.ApplyAsync(new Dictionary<Guid,long>{{assetId,description.Revision}},new(new Dictionary<AssetDescriptionField,string?>{{AssetDescriptionField.Notes,"After controlled native edit"}}));
            var changed=Logical(owner.CatalogSession);foreach(var table in before.Keys.Where(n=>n!="MediaAssetDescriptions"))Assert.Equal(before[table],changed[table]);
            await owner.DisposeAsync();owner=null;
            opened=await new CatalogLifecycle(new MacCatalogLifecycleEffects(root)).StartAsync(allowFirstCreation:false);Assert.True(opened.IsReady,opened.Diagnostic);owner=opened.Coordinator!;
            EqualLogical(changed,Logical(owner.CatalogSession));
            Assert.Equal("After controlled native edit",(await new CatalogAssetDescriptionStore(()=>owner.Session).GetAsync([assetId]))[assetId].Notes);
            Assert.Equal(catalogId,owner.CatalogSession.Identity.CatalogId);Assert.False(owner.CatalogEvidence!.KnownClean);
            Assert.False(File.Exists(owner.Locations.PreviewsDatabasePath));
        }
        finally {if(owner is not null)await owner.DisposeAsync();Directory.Delete(root,true);}
    }
    private static Dictionary<string,string[]> Logical(CatalogDatabaseSession session)
    {
        using var connection=session.OpenConnection();using var command=connection.CreateCommand();
        command.CommandText="SELECT name FROM sqlite_schema WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name";
        var tables=new List<string>();using(var reader=command.ExecuteReader())while(reader.Read())tables.Add(reader.GetString(0));
        var result=new Dictionary<string,string[]>();
        foreach(var table in tables)
        {
            command.CommandText="SELECT * FROM \""+table.Replace("\"","\"\"")+"\"";using var reader=command.ExecuteReader();var rows=new List<string>();
            while(reader.Read()){var values=new object[reader.FieldCount];reader.GetValues(values);rows.Add(JsonSerializer.Serialize(values.Select(v=>v is DBNull?null:v).ToArray()));}
            result[table]=rows.Order(StringComparer.Ordinal).ToArray();
        }
        return result;
    }
    private static void EqualLogical(Dictionary<string,string[]> expected,Dictionary<string,string[]> actual)
    {Assert.Equal(expected.Keys,actual.Keys);foreach(var table in expected.Keys)Assert.Equal(expected[table],actual[table]);}
}
