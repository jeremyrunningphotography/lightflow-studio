using Avalonia.Headless.XUnit;
using Lightflow.Application;
using Lightflow.UI;
using LightflowStudio;
using Xunit;
namespace Lightflow.UI.Tests;
public sealed partial class BrowserTests
{
    [AvaloniaFact]
    public async Task KnownAssetsAndCachePublishBeforeDiscoveryWithoutSchedulingUnverifiedWork()
    {
        var service=new CachedBrowser();var asset=Asset(service.RootId,"one.jpg");service.Known[""]=[asset];service.Cached[asset.AssetId]=new(asset.AssetId,"/cache/one.jpg","Ready");
        var model=new BrowserViewModel(service);await model.OpenAsync(service.CatalogId);var navigation=model.NavigateAsync(model.Roots[0]);
        Assert.True(model.IsLoading);Assert.True(model.IsProvisional);Assert.Equal(asset,Assert.Single(model.Assets));Assert.Equal("Cached",model.Thumbnails[asset.AssetId].State);Assert.Contains("Checking for changes",model.Message);
        await model.SetThumbnailViewportAsync([asset.AssetId],[]);Assert.Equal(0,service.Demands);
        await model.SelectAsync(asset.AssetId);Assert.Equal(asset.AssetId,model.SelectedAssetId);Assert.Contains("not been verified",model.SourceMessage);
        service.Source.SetResult(true);service.Folders[""].SetResult(new(service.RootId,"",false,MediaFolderEnumerationStatus.Succeeded,[],[asset]));await navigation;
        Assert.False(model.IsLoading);Assert.False(model.IsProvisional);Assert.Equal("Ready",model.Thumbnails[asset.AssetId].State);Assert.Equal(asset.AssetId,model.SelectedAssetId);Assert.Empty(model.Message);
    }
    [AvaloniaFact]
    public async Task ReconciliationReplacesRemovedAndChangedCachedIdentitiesWithoutLosingSelection()
    {
        var service=new CachedBrowser();var removed=Asset(service.RootId,"removed.jpg");var changed=Asset(service.RootId,"changed.jpg");var added=Asset(service.RootId,"added.jpg");service.Known[""]=[removed,changed];
        foreach(var a in service.Known[""])service.Cached[a.AssetId]=new(a.AssetId,"/cache/old.jpg","Ready");
        var model=new BrowserViewModel(service);await model.OpenAsync(service.CatalogId);var navigation=model.NavigateAsync(model.Roots[0]);
        await model.SelectAsync(changed.AssetId);service.Source.SetResult(true);service.Cached.Clear();service.Cached[changed.AssetId]=new(changed.AssetId,null,"Loading");
        var replacement=changed with{Fingerprint=new(1,"new source"),LastWriteUtcTicks=changed.LastWriteUtcTicks+1};
        service.Folders[""].SetResult(new(service.RootId,"",false,MediaFolderEnumerationStatus.Succeeded,[],[replacement,added]));await navigation;
        Assert.Equal(new[]{replacement,added},model.Assets);Assert.Equal(changed.AssetId,model.SelectedAssetId);Assert.DoesNotContain(removed.AssetId,model.Thumbnails.Keys);Assert.Null(model.Thumbnails[changed.AssetId].Path);Assert.False(model.IsProvisional);
    }
    [AvaloniaFact]
    public async Task LateCachedReadAndReconciliationCannotOverwriteNewNavigation()
    {
        var service=new CachedBrowser();var first=Asset(service.RootId,"First/a.jpg");var second=Asset(service.RootId,"Second/b.jpg");service.Known["First"]=[first];service.Known["Second"]=[second];
        service.PendingCache=new(TaskCreationOptions.RunContinuationsAsynchronously);
        var model=new BrowserViewModel(service);await model.OpenAsync(service.CatalogId);var old=model.NavigateAsync(new(service.RootId,"First","First"));
        var pending=service.PendingCache;service.PendingCache=null;
        var next=model.NavigateAsync(new(service.RootId,"Second","Second"));service.Folders["Second"].SetResult(new(service.RootId,"Second",false,MediaFolderEnumerationStatus.Succeeded,[],[second]));await next;await old;
        pending.SetResult(new Dictionary<Guid,BrowserThumbnail>{{first.AssetId,new(first.AssetId,"/cache/stale.jpg","Ready")}});
        Assert.Equal(second,Assert.Single(model.Assets));Assert.DoesNotContain(first.AssetId,model.Thumbnails.Keys);Assert.Equal("Second",model.Location!.RelativeFolder);
        var oldScan=model.NavigateAsync(new(service.RootId,"First","First"));var oldResult=service.Folders["First"];
        var newScan=model.NavigateAsync(new(service.RootId,"Second","Second"));service.Folders["Second"].SetResult(new(service.RootId,"Second",false,MediaFolderEnumerationStatus.Succeeded,[],[second]));await newScan;await oldScan;
        oldResult.SetResult(new(service.RootId,"First",false,MediaFolderEnumerationStatus.Succeeded,[],[first]));Assert.Equal(second,Assert.Single(model.Assets));
    }
    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task FailedOrCanceledVerificationRetainsHonestCachedStateAndDoesNotGenerate(bool cancel)
    {
        var service=new CachedBrowser();var asset=Asset(service.RootId,"one.jpg");service.Known[""]=[asset];service.Cached[asset.AssetId]=new(asset.AssetId,"/cache/one.jpg","Ready");
        var model=new BrowserViewModel(service);await model.OpenAsync(service.CatalogId);var navigation=model.NavigateAsync(model.Roots[0]);
        if(cancel)model.CancelNavigation();else service.Folders[""].SetException(new IOException("Source unavailable."));await navigation;
        Assert.False(model.IsLoading);Assert.True(model.IsProvisional);Assert.Equal("Unverified",model.Thumbnails[asset.AssetId].State);Assert.Contains("not been verified",model.SourceMessage);Assert.False(model.CanChangeRecursive);
        await model.SetThumbnailViewportAsync([asset.AssetId],[]);Assert.Equal(0,service.Demands);
    }
    [AvaloniaFact]
    public async Task ProvisionalPreviewReadsRemainBoundedAndRecursiveScopeComesFromCatalog()
    {
        var service=new CachedBrowser{Recursive=true};service.Known[""]=Enumerable.Range(0,1204).Select(i=>Asset(service.RootId,$"child/{i}.jpg")).ToArray();
        var model=new BrowserViewModel(service);await model.OpenAsync(service.CatalogId);var navigation=model.NavigateAsync(model.Roots[0]);Assert.True(model.Recursive);Assert.Equal(1204,model.Assets.Count);Assert.Equal(192,service.ReadCounts.Single());
        model.Suspend();await navigation;Assert.Empty(model.Assets);Assert.Empty(model.Thumbnails);
    }
    private sealed class CachedBrowser:FakeBrowser,IBrowserDiscoverySession,IBrowserCachedFolderSession,IBrowserThumbnailSession
    {
        public Dictionary<string,IReadOnlyList<MediaAsset>> Known=[];public Dictionary<Guid,BrowserThumbnail> Cached=[];public List<int> ReadCounts=[];public int Demands;public bool Recursive;
        public TaskCompletionSource<IReadOnlyDictionary<Guid,BrowserThumbnail>>? PendingCache;
        public Task<BrowserFolderSnapshot> ReadCachedBrowserFolderAsync(Guid rootId,string folder,CancellationToken token=default)=>Task.FromResult(new BrowserFolderSnapshot(rootId,folder,Recursive,MediaFolderEnumerationStatus.Succeeded,[],Known.GetValueOrDefault(folder,[])));
        public Task<BrowserFolderSnapshot> DiscoverBrowserFolderAsync(string? path,Guid rootId,string folder,CancellationToken token=default)=>ReadBrowserFolderAsync(rootId,folder,token);
        public Task SetBrowserRecursiveAsync(Guid rootId,string folder,bool enabled,CancellationToken token=default)=>Task.CompletedTask;
        public Task<IReadOnlyDictionary<Guid,BrowserThumbnail>> ReadThumbnailsAsync(IReadOnlyList<Guid> ids,CancellationToken token){ReadCounts.Add(ids.Count);return PendingCache?.Task??Task.FromResult<IReadOnlyDictionary<Guid,BrowserThumbnail>>(Cached.Where(x=>ids.Contains(x.Key)).ToDictionary(x=>x.Key,x=>x.Value));}
        public Task<IDerivedWorkBatch?> DemandThumbnailsAsync(IReadOnlyList<Guid> ids,DerivedWorkPriority priority,CancellationToken token){Demands++;return Task.FromResult<IDerivedWorkBatch?>(null);}
    }
}
