using Lightflow.Application;
using Lightflow.Platform.MacOS;
using LightflowStudio;
using Xunit;

namespace Lightflow.Platform.MacOS.Tests;

[Collection("Native storage descriptor ownership")]
public sealed class MacBrowserCatalogTests
{
    // Keep default fixtures in this test build's own output. /var is a macOS alias and is
    // deliberately refused by the production unlinked-path guard; do not weaken that guard.
    private static string FixtureRoot(string prefix) => Path.Combine(
        Environment.GetEnvironmentVariable("LIGHTFLOW_TEST_ROOT") ?? Path.Combine(AppContext.BaseDirectory,"BrowserFixtures"),
        prefix + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task RealHierarchyRecursiveScopesMissingSourcesAndReadOnlyReopen()
    {
        var root=FixtureRoot("lf025-browser-");
        Directory.CreateDirectory(Path.Combine(root,"Catalog"));
        Guid catalogId, rootId=Guid.NewGuid(), offline=Guid.NewGuid();
        var media=Path.Combine(root,"Media");Directory.CreateDirectory(Path.Combine(media,"Trip","Day 2"));Directory.CreateDirectory(Path.Combine(media,"Empty"));
        var png=Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+ip1sAAAAASUVORK5CYII=");
        var names=new[]{"cover.png","Trip/sunrise.png","Trip/Day 2/forest.png","Trip/missing.png"};
        foreach(var name in names.Take(3)) await File.WriteAllBytesAsync(Path.Combine(media,name),png);
        await using(var shell=new MacShellCatalogSession(root))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(()=>shell.ReadBrowserCatalogAsync());
            Assert.False((await shell.StartAsync()).IsReady);
            var ready=await shell.CreateAsync(Path.Combine(root,"Catalog"));Assert.True(ready.IsReady,ready.Diagnostic);
            catalogId=ready.CatalogId!.Value;
            Assert.Empty((await shell.ReadBrowserCatalogAsync()).Roots);
        }
        var seed=await new CatalogLifecycle(new MacCatalogLifecycleEffects(root)).StartAsync(allowFirstCreation:false);
        List<MediaAsset> expected=[];string before;
        await using(var owner=seed.Coordinator!)
        {
            var machine=new MachineIdentityProvider(Path.Combine(root,"machine-id")).GetMachineId();
            foreach(var mapping in new[]{(rootId,"Field Notes",media),(offline,"Disconnected media",Path.Combine(root,"Disconnected"))})
            {
                using var connection=owner.CatalogSession.OpenConnection();using var command=connection.CreateCommand();
                command.CommandText="INSERT INTO MediaRoots(RootId,DisplayName,SourceStatus,CreatedUtc,UpdatedUtc) VALUES($root,$name,'online',$now,$now); INSERT INTO MediaRootMappings(MappingId,RootId,MachineId,PhysicalPath,SourceStatus,CreatedUtc,UpdatedUtc) VALUES($mapping,$root,$machine,$path,'online',$now,$now);";
                command.Parameters.AddWithValue("$root",mapping.Item1.ToString("D"));command.Parameters.AddWithValue("$name",mapping.Item2);command.Parameters.AddWithValue("$path",mapping.Item3);command.Parameters.AddWithValue("$machine",machine);command.Parameters.AddWithValue("$mapping",Guid.NewGuid().ToString("D"));command.Parameters.AddWithValue("$now",DateTime.UtcNow.ToString("O"));command.ExecuteNonQuery();
            }
            var repository=new CatalogMediaAssetRepository(()=>owner.Session);
            foreach(var name in names)
            {
                var now=DateTimeOffset.UtcNow;
                var asset=new MediaAsset(Guid.NewGuid(),rootId,name,name.ToUpperInvariant(),"image",png.Length,DateTime.UtcNow.Ticks,null,MediaAssetSourceStatus.Available,now,now,now);
                Assert.Equal(MediaAssetOperationStatus.Succeeded,await repository.CreateAsync(asset));expected.Add(asset);
            }
            await new CatalogBrowserRecursiveRootRepository(()=>owner.Session).CreateAsync(rootId,"Trip");
            await new CatalogCollectionOrganizationStore(()=>owner.Session).CreateCollectionAsync("LF025 preserved field notes");
            before=LogicalSnapshot(owner);
        }
        await using(var shell=new MacShellCatalogSession(root))
        {
            Assert.Equal(catalogId,(await shell.StartAsync()).CatalogId);
            Assert.Equal(2,(await shell.ReadBrowserCatalogAsync()).Roots.Count);
            var locations = new MacBrowserLocationProvider();
            var uncataloged = Path.Combine(root,"Uncataloged"); Directory.CreateDirectory(Path.Combine(uncataloged,"Leaf"));
            await File.WriteAllBytesAsync(Path.Combine(uncataloged,"unimported.png"),png);
            var listing = await locations.GetFoldersAsync(uncataloged,default);
            Assert.Equal(MediaFolderEnumerationStatus.Succeeded,listing.Status);
            Assert.Equal("Leaf",Assert.Single(listing.Folders).Name);
            Assert.Null(BrowserLocationMapping.Find(uncataloged,(await shell.ReadBrowserCatalogAsync()).Roots,StringComparison.Ordinal));
            Assert.Equal(rootId,BrowserLocationMapping.Find(media,(await shell.ReadBrowserCatalogAsync()).Roots,StringComparison.Ordinal)!.RootId);
            var top=await shell.ReadBrowserFolderAsync(rootId,"");Assert.Equal(MediaFolderEnumerationStatus.Succeeded,top.Status);Assert.Equal(2,top.Folders.Count);Assert.Equal(expected[0].AssetId,Assert.Single(top.Assets).AssetId);Assert.False(top.Recursive);
            var trip=await shell.ReadBrowserFolderAsync(rootId,"Trip");Assert.True(trip.Recursive);Assert.Equal(3,trip.Assets.Count);
            Assert.Empty((await shell.ReadBrowserFolderAsync(rootId,"Empty")).Assets);
            Assert.Equal(MediaFolderEnumerationStatus.RootUnavailable,(await shell.ReadBrowserFolderAsync(offline,"")).Status);
            Assert.Equal(MediaFolderEnumerationStatus.RootNotFound,(await shell.ReadBrowserFolderAsync(Guid.NewGuid(),"")).Status);
            Assert.True(await shell.IsBrowserSourceAvailableAsync(rootId,expected[1].AssetId));
            Assert.False(await shell.IsBrowserSourceAvailableAsync(rootId,expected[3].AssetId));
            File.Move(Path.Combine(media,"Trip/sunrise.png"),Path.Combine(media,"Trip/sunrise.retained"));
            Assert.False(await shell.IsBrowserSourceAvailableAsync(rootId,expected[1].AssetId));
            File.Move(Path.Combine(media,"Trip/sunrise.retained"),Path.Combine(media,"Trip/sunrise.png"));
            Directory.Delete(Path.Combine(media,"Empty"));Assert.Equal(MediaFolderEnumerationStatus.FolderNotFound,(await shell.ReadBrowserFolderAsync(rootId,"Empty")).Status);Directory.CreateDirectory(Path.Combine(media,"Empty"));
            Directory.Move(media,media+"-offline");Assert.Equal(MediaFolderEnumerationStatus.RootUnavailable,(await shell.ReadBrowserFolderAsync(rootId,"")).Status);Directory.Move(media+"-offline",media);
            await Assert.ThrowsAsync<ArgumentException>(()=>shell.ReadBrowserFolderAsync(rootId,"../escape"));
        }
        var final=await new CatalogLifecycle(new MacCatalogLifecycleEffects(root)).StartAsync(allowFirstCreation:false);
        await using(var owner=final.Coordinator!)
        {
            Assert.Equal(catalogId,owner.CatalogSession.Identity.CatalogId);Assert.Equal(before,LogicalSnapshot(owner));
            Assert.Equal(expected.OrderBy(a=>a.AssetId), (await new CatalogMediaAssetRepository(()=>owner.Session).ListAsync()).OrderBy(a=>a.AssetId));
        }
        File.WriteAllText(Path.Combine(root,"LF025-fixture.txt"),$"Catalog {catalogId}\nRoot {rootId}\nDisconnected Root {offline}\nAssets: {string.Join(", ",expected.Select(a=>$"{a.RelativePath}={a.AssetId}"))}\n");
        Console.WriteLine($"LF025 acceptance fixture: {root}");
    }
    [Fact]
    public async Task NativeLocationsHandleMissingDeniedAndLinkedFoldersWithoutReadingMedia()
    {
        var root=FixtureRoot("lf026-locations-"); Directory.CreateDirectory(Path.Combine(root,"Leaf"));
        var provider=new MacBrowserLocationProvider();
        var locations=await provider.GetLocationsAsync(default);
        Assert.Contains(locations,location=>location.Name=="Home" && Path.IsPathFullyQualified(location.Path));
        Assert.Empty((await provider.GetFoldersAsync(Path.Combine(root,"Leaf"),default)).Folders);
        Assert.Equal(MediaFolderEnumerationStatus.FolderNotFound,(await provider.GetFoldersAsync(Path.Combine(root,"Missing"),default)).Status);
        var link=Path.Combine(root,"Linked"); Directory.CreateSymbolicLink(link,Path.Combine(root,"Leaf"));
        Assert.Equal(MediaFolderEnumerationStatus.LinkedPathRejected,(await provider.GetFoldersAsync(link,default)).Status);
        Assert.DoesNotContain((await provider.GetFoldersAsync(root,default)).Folders,folder=>folder.Name=="Linked");
        var denied=Path.Combine(root,"Denied");Directory.CreateDirectory(denied);
        File.SetUnixFileMode(denied,UnixFileMode.None);
        try { Assert.Equal(MediaFolderEnumerationStatus.AccessDenied,(await provider.GetFoldersAsync(denied,default)).Status); }
        finally { File.SetUnixFileMode(denied,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute); }
        using var cancelled=new CancellationTokenSource();cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>provider.GetFoldersAsync(root,cancelled.Token));
    }
    private static string LogicalSnapshot(CatalogLifecycle owner)
    {
        using var connection=owner.CatalogSession.OpenConnection();
        using var tables=connection.CreateCommand();tables.CommandText="SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name;";
        List<string> names=[];using(var reader=tables.ExecuteReader())while(reader.Read())names.Add(reader.GetString(0));
        List<string> rows=[];
        foreach(var name in names)
        {
            using var command=connection.CreateCommand();command.CommandText="SELECT * FROM \""+name.Replace("\"","\"\"")+"\";";
            using var reader=command.ExecuteReader();List<string> values=[];
            while(reader.Read())values.Add(System.Text.Json.JsonSerializer.Serialize(Enumerable.Range(0,reader.FieldCount).Select(i=>reader.IsDBNull(i)?null:reader.GetValue(i)).ToArray()));
            values.Sort(StringComparer.Ordinal);rows.Add(name+":"+string.Join("|",values));
        }
        return string.Join("\n",rows);
    }
    [Fact]
    public async Task RealCatalogProjectionUsesExistingOwnerAndRetainsIdentitiesAndAuthoredState()
    {
        var root = FixtureRoot("lf025-checkpoint-");
        Directory.CreateDirectory(Path.Combine(root,"Catalog"));
        var media = Path.Combine(root,"Media"); Directory.CreateDirectory(Path.Combine(media,"Trip"));
        await File.WriteAllBytesAsync(Path.Combine(media,"Trip","frame.png"), Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+ip1sAAAAASUVORK5CYII="));
        var created = await new CatalogLifecycle(new MacCatalogLifecycleEffects(root)).StartAsync();
        Assert.True(created.IsReady,created.Diagnostic);
        Guid catalogId, rootId = Guid.NewGuid(), assetId = Guid.NewGuid();
        await using(var owner = created.Coordinator!)
        {
            catalogId = owner.CatalogSession.Identity.CatalogId;
            var machine = new MachineIdentityProvider(Path.Combine(root,"machine-id")).GetMachineId();
            using(var connection=owner.CatalogSession.OpenConnection())
            using(var command=connection.CreateCommand())
            {
                command.CommandText="""
                    INSERT INTO MediaRoots(RootId,DisplayName,SourceStatus,CreatedUtc,UpdatedUtc) VALUES($root,'Checkpoint media','online',$now,$now);
                    INSERT INTO MediaRootMappings(MappingId,RootId,MachineId,PhysicalPath,SourceStatus,CreatedUtc,UpdatedUtc) VALUES($mapping,$root,$machine,$path,'online',$now,$now);
                    """;
                command.Parameters.AddWithValue("$root",rootId.ToString("D")); command.Parameters.AddWithValue("$mapping",Guid.NewGuid().ToString("D"));
                command.Parameters.AddWithValue("$machine",machine); command.Parameters.AddWithValue("$path",media); command.Parameters.AddWithValue("$now",DateTime.UtcNow.ToString("O")); command.ExecuteNonQuery();
            }
            var file=new FileInfo(Path.Combine(media,"Trip","frame.png")); var now=DateTimeOffset.UtcNow;
            Assert.Equal(MediaAssetOperationStatus.Succeeded, await new CatalogMediaAssetRepository(()=>owner.Session).CreateAsync(
                new(assetId,rootId,"Trip/frame.png","TRIP/FRAME.PNG","image",file.Length,file.LastWriteTimeUtc.Ticks,null,MediaAssetSourceStatus.Available,now,now,now)));
            await new CatalogCollectionOrganizationStore(()=>owner.Session).CreateCollectionAsync("LF025 preserved authored state");
        }
        await using(var shell=new MacShellCatalogSession(root))
        {
            Assert.True((await shell.StartAsync()).IsReady);
            var roots=await shell.ReadBrowserCatalogAsync(); Assert.Equal(catalogId,roots.CatalogId); Assert.Equal(rootId,Assert.Single(roots.Roots).RootId); Assert.Empty(roots.Assets);
            var content=await shell.ReadBrowserCatalogAsync(rootId,"Trip"); Assert.Equal(assetId,Assert.Single(content.Assets).AssetId);
            Assert.Empty((await shell.ReadBrowserCatalogAsync(rootId)).Assets);
            Assert.Equal(assetId,Assert.Single((await shell.ReadBrowserCatalogAsync(rootId,recursive:true)).Assets).AssetId);
            await using var competing=new MacShellCatalogSession(root); Assert.False((await competing.StartAsync()).IsReady);
        }
        var reopened=await new CatalogLifecycle(new MacCatalogLifecycleEffects(root)).StartAsync(allowFirstCreation:false);
        await using(var owner=reopened.Coordinator!)
        {
            Assert.Equal(catalogId,owner.CatalogSession.Identity.CatalogId);
            Assert.Equal("LF025 preserved authored state",Assert.Single(await new CatalogCollectionOrganizationStore(()=>owner.Session).ListCollectionsAsync()).Name);
            Assert.Equal(assetId,Assert.Single(await new CatalogMediaAssetRepository(()=>owner.Session).ListAsync()).AssetId);
            Assert.False(owner.CatalogEvidence!.KnownClean); Assert.False(File.Exists(owner.Locations.PreviewsDatabasePath));
        }
        Console.WriteLine($"LF025 real Catalog checkpoint: {root}; Catalog {catalogId}; Root {rootId}; Asset {assetId}");
    }
}
