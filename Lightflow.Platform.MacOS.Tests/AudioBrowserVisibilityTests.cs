using Lightflow.Platform.MacOS;
using LightflowStudio;
using Xunit;
namespace Lightflow.Platform.MacOS.Tests;
[Collection("Native storage descriptor ownership")]
public sealed class AudioBrowserVisibilityTests
{
    [Fact]
    public async Task EveryBrowserProjectionMatchesWindowsWhileAudioIdentitiesAndAuthoredStateSurvive()
    {
        var data=Path.Combine(Environment.GetEnvironmentVariable("LIGHTFLOW_TEST_ROOT")??Path.Combine(AppContext.BaseDirectory,"BrowserFixtures"),"lf036-audio-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(data,"Catalog"));var media=Path.Combine(data,"Media");Directory.CreateDirectory(Path.Combine(media,"Child"));
        var visual=new[]{"01.jpg","03.png","05.dng","07.mp4","09.mov"};var audio=new[]{"00.mp3","02.wav","04.flac"};
        foreach(var folder in new[]{media,Path.Combine(media,"Child")})foreach(var name in visual.Concat(audio).Append("06.txt"))await File.WriteAllTextAsync(Path.Combine(folder,name),"disposable classification fixture");
        var types=MediaTypeRegistry.CreateDefault();Assert.All(audio,name=>{Assert.True(types.Classify(new(name)).IsKnown);Assert.Equal(MediaTypeCategory.Audio,types.Classify(new(name)).Category);});
        Guid catalog,root,collection;string scope;Dictionary<string,Guid> audioIds;Dictionary<string,Guid> visibleIds;
        void Visible(IReadOnlyList<MediaAsset> assets,int count){Assert.Equal(count,assets.Count);Assert.All(assets,a=>Assert.Contains(types.Classify(new(a.RelativePath)).Category,new[]{MediaTypeCategory.StillImage,MediaTypeCategory.RawImage,MediaTypeCategory.Video}));Assert.Equal(count,assets.Select(a=>a.AssetId).Distinct().Count());}
        await using(var shell=new MacShellCatalogSession(data))
        {
            var created=await shell.CreateAsync(Path.Combine(data,"Catalog"));Assert.True(created.IsReady);catalog=created.CatalogId!.Value;
            var scan=await shell.DiscoverBrowserFolderAsync(media,Guid.Empty,"");root=scan.RootId;scope=scan.RelativeFolder;Visible(scan.Assets,5);Assert.Equal(visual,scan.Assets.Select(a=>Path.GetFileName(a.RelativePath)));
            Visible((await shell.ReadBrowserCatalogAsync(root,scope)).Assets,5);Visible((await shell.ReadBrowserFolderAsync(root,scope)).Assets,5);Visible((await shell.ReadCachedBrowserFolderAsync(root,scope)).Assets,5);
            await shell.SetBrowserRecursiveAsync(root,scope,true);Visible((await shell.DiscoverBrowserFolderAsync(media,root,scope)).Assets,10);Visible((await shell.ReadCachedBrowserFolderAsync(root,scope)).Assets,10);Visible((await shell.ReadBrowserFolderAsync(root,scope)).Assets,10);
        }
        var seed=await new CatalogLifecycle(new MacCatalogLifecycleEffects(data)).StartAsync(allowFirstCreation:false);
        await using(var owner=seed.Coordinator!)
        {
            var all=await new CatalogMediaAssetRepository(()=>owner.Session).ListScopeAsync(root,scope,true);Assert.Equal(16,all.Count);audioIds=all.Where(a=>types.Classify(new(a.RelativePath)).Category==MediaTypeCategory.Audio).ToDictionary(a=>a.RelativePath,a=>a.AssetId);Assert.Equal(6,audioIds.Count);
            visibleIds=all.Where(a=>types.Classify(new(a.RelativePath)).Category!=MediaTypeCategory.Audio).ToDictionary(a=>a.RelativePath,a=>a.AssetId);
            foreach(var id in audioIds.Values)await new CatalogAssetClassificationStore(()=>owner.Session).UpdateAsync(id,prior=>prior with{Rating=4});
            var collections=new CatalogCollectionOrganizationStore(()=>owner.Session);collection=(await collections.CreateCollectionAsync("Retained audio")).CollectionId;foreach(var id in audioIds.Values)await collections.AddMembershipAsync(collection,id);
        }
        await using(var shell=new MacShellCatalogSession(data))
        {
            Assert.Equal(catalog,(await shell.StartAsync()).CatalogId);Visible((await shell.ReadCachedBrowserFolderAsync(root,scope)).Assets,10);Visible((await shell.ReadBrowserCatalogAsync(root,scope,true)).Assets,10);
            for(var i=0;i<2;i++){var scan=await shell.DiscoverBrowserFolderAsync(media,root,scope);Visible(scan.Assets,10);Assert.All(scan.Assets,a=>Assert.Equal(visibleIds[a.RelativePath],a.AssetId));}
        }
        var check=await new CatalogLifecycle(new MacCatalogLifecycleEffects(data)).StartAsync(allowFirstCreation:false);
        await using(var owner=check.Coordinator!)
        {
            var all=await new CatalogMediaAssetRepository(()=>owner.Session).ListScopeAsync(root,scope,true);Assert.Equal(16,all.Count);Assert.All(audioIds,p=>Assert.Equal(p.Value,all.Single(a=>a.RelativePath==p.Key).AssetId));
            var ratings=await new CatalogAssetClassificationStore(()=>owner.Session).GetAsync(audioIds.Values.ToArray());Assert.All(audioIds.Values,id=>Assert.Equal(4,ratings[id].Rating));Assert.Equal(audioIds.Values.OrderBy(x=>x), (await new CatalogCollectionOrganizationStore(()=>owner.Session).ListMembershipsAsync(collection)).Select(m=>m.AssetId).OrderBy(x=>x));
        }
    }
}
