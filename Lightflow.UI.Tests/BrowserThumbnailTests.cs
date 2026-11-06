using Avalonia.Headless.XUnit;
using LightflowStudio;
using Lightflow.UI;
using Xunit;
namespace Lightflow.UI.Tests;
public sealed partial class BrowserTests
{
    [AvaloniaFact]
    public async Task ThumbnailReadCompletingAfterNavigationCannotRepopulateOldScope()
    {
        var service=new ThumbnailBrowser();var model=new BrowserViewModel(service);await model.OpenAsync(service.CatalogId);
        var load=model.NavigateAsync(model.Roots[0]);var asset=Asset(service.RootId,"one.jpg");service.Folders[""].SetResult(new(service.RootId,"",false,MediaFolderEnumerationStatus.Succeeded,[],[asset]));await load;
        var viewport=model.SetThumbnailViewportAsync([asset.AssetId],[]);Assert.Equal(1,model.ThumbnailDemandCount);
        model.Suspend();service.Read.SetResult(new Dictionary<Guid,BrowserThumbnail>{{asset.AssetId,new(asset.AssetId,"/stale.jpg","Ready")}});await viewport;
        Assert.Empty(model.Thumbnails);Assert.True(service.Tokens.All(x=>x.IsCancellationRequested));
    }
    [AvaloniaFact]
    public async Task ThumbnailDemandIsBoundedAndPriorityIsVisibleThenOverscan()
    {
        var service=new ThumbnailBrowser();var model=new BrowserViewModel(service);await model.OpenAsync(service.CatalogId);
        var load=model.NavigateAsync(model.Roots[0]);service.Folders[""].SetResult(new(service.RootId,"",false,MediaFolderEnumerationStatus.Succeeded,[],[]));await load;
        service.Read.SetResult(new Dictionary<Guid,BrowserThumbnail>());
        var ids=Enumerable.Range(0,1000).Select(_=>Guid.NewGuid()).ToArray();await model.SetThumbnailViewportAsync(ids[..64],ids[64..]);
        Assert.Equal(192,model.ThumbnailDemandCount);Assert.Equal(new[]{DerivedWorkPriority.Visible,DerivedWorkPriority.Normal},service.Demands.Select(x=>x.Priority));Assert.Equal(64,service.Demands[0].Count);Assert.Equal(128,service.Demands[1].Count);
        model.Suspend();Assert.Empty(model.Thumbnails);
    }
    [AvaloniaFact]
    public async Task RealizedRowsBoundBitmapRetentionAndReleaseOnDetach()
    {
        var root=Path.Combine(Path.GetTempPath(),"lf036-rows-"+Guid.NewGuid());Directory.CreateDirectory(root);var path=Path.Combine(root,"cache.jpg");
        try
        {
            using(var bitmap=new SkiaSharp.SKBitmap(64,48)){bitmap.Erase(SkiaSharp.SKColors.Coral);using var image=SkiaSharp.SKImage.FromBitmap(bitmap);using var data=image.Encode(SkiaSharp.SKEncodedImageFormat.Jpeg,85);File.WriteAllBytes(path,data.ToArray());}
            var service=new ThumbnailBrowser();var model=new BrowserViewModel(service);await model.OpenAsync(service.CatalogId);
            var load=model.NavigateAsync(model.Roots[0]);var assets=Enumerable.Range(0,1000).Select(i=>Asset(service.RootId,$"{i}.jpg")).ToArray();service.Folders[""].SetResult(new(service.RootId,"",false,MediaFolderEnumerationStatus.Succeeded,[],assets));await load;
            service.Read.SetResult(assets.ToDictionary(x=>x.AssetId,x=>new BrowserThumbnail(x.AssetId,path,"Ready")));
            var pane=new BrowserPane(model);var window=new Avalonia.Controls.Window{Content=pane,Width=1100,Height=700};window.Show();
            await model.SetThumbnailViewportAsync(assets.Take(64).Select(x=>x.AssetId).ToArray(),assets.Skip(64).Take(128).Select(x=>x.AssetId).ToArray());
            window.UpdateLayout();Assert.InRange(BrowserThumbnailCell.RetainedBitmaps,1,64);Assert.Equal(192,model.ThumbnailDemandCount);
            window.Close();model.Suspend();Assert.Equal(0,BrowserThumbnailCell.RetainedBitmaps);
        }finally{Directory.Delete(root,true);}
    }
    private sealed class ThumbnailBrowser:FakeBrowser,IBrowserThumbnailSession
    {
        public List<CancellationToken> Tokens=[];
        public List<(DerivedWorkPriority Priority,int Count)> Demands=[];
        public TaskCompletionSource<IReadOnlyDictionary<Guid,BrowserThumbnail>> Read=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<IDerivedWorkBatch?> DemandThumbnailsAsync(IReadOnlyList<Guid> ids,DerivedWorkPriority priority,CancellationToken token){Tokens.Add(token);Demands.Add((priority,ids.Count));return Task.FromResult<IDerivedWorkBatch?>(null);}
        public Task<IReadOnlyDictionary<Guid,BrowserThumbnail>> ReadThumbnailsAsync(IReadOnlyList<Guid> ids,CancellationToken token)=>Read.Task;
    }
}
