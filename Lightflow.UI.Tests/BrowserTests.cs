using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Lightflow.Application;
using Lightflow.UI;
using LightflowStudio;
using Xunit;
namespace Lightflow.UI.Tests;

public sealed class BrowserTests
{
    [Fact]
    public async Task FolderSwitchRejectsLateResultsAndRetiresOldSelection()
    {
        var service=new FakeBrowser();var model=new BrowserViewModel(service);await model.OpenAsync(service.CatalogId);
        var a=new BrowserFolderNode(service.RootId,"A","A");var b=new BrowserFolderNode(service.RootId,"B","B");
        var first=model.NavigateAsync(a);var second=model.NavigateAsync(b);
        var asset=Asset(service.RootId,"B/selected.png");service.Folders["B"].SetResult(new(service.RootId,"B",false,MediaFolderEnumerationStatus.Succeeded,[],[asset]));await second;
        service.Folders["A"].SetResult(new(service.RootId,"A",false,MediaFolderEnumerationStatus.Succeeded,[],[Asset(service.RootId,"old.png")]));await first;
        Assert.Equal("B",model.Location!.RelativeFolder);Assert.Equal(asset,Assert.Single(model.Assets));
        var selected=model.SelectAsync(asset.AssetId);Assert.Equal(asset.AssetId,model.SelectedAssetId);
        var third=model.NavigateAsync(a);service.Folders["A"].SetResult(new(service.RootId,"A",false,MediaFolderEnumerationStatus.Succeeded,[],[]));await third;
        service.Source.SetResult(true);await selected;Assert.Null(model.SelectedAssetId);Assert.DoesNotContain("Source available",model.SourceMessage);
    }
    [Fact]
    public async Task StableSingleSelectionAndAvailabilityDoNotMutateCatalogRecords()
    {
        var service=new FakeBrowser();var model=new BrowserViewModel(service);await model.OpenAsync(service.CatalogId);
        var load=model.NavigateAsync(model.Roots[0]);var a=Asset(service.RootId,"one.png");var b=Asset(service.RootId,"two.png");
        service.Folders[""].SetResult(new(service.RootId,"",false,MediaFolderEnumerationStatus.Succeeded,[],[a,b]));await load;
        service.Source.SetResult(false);await model.SelectAsync(b.AssetId);Assert.Equal(b.AssetId,model.SelectedAssetId);Assert.Equal(b,model.SelectedAsset);Assert.Contains("unavailable",model.SourceMessage);
        await model.SelectAsync(a.AssetId);Assert.Equal(a.AssetId,model.SelectedAssetId);Assert.Equal(2,model.Assets.Count);
        await model.SelectAsync(Guid.NewGuid());Assert.Null(model.SelectedAssetId);
        model.Suspend();Assert.Empty(model.Assets);Assert.False(model.IsAvailable);
    }
    [Fact]
    public async Task ReadinessLossRejectsPendingFolderAndRootExpansion()
    {
        var service=new FakeBrowser();var model=new BrowserViewModel(service);await model.OpenAsync(service.CatalogId);
        var navigate=model.NavigateAsync(model.Roots[0]);var expand=model.ExpandAsync(model.Roots[0]);model.Suspend();
        service.Folders[""].SetResult(new(service.RootId,"",false,MediaFolderEnumerationStatus.Succeeded,[],[Asset(service.RootId,"late.png")]));
        service.Expansion.SetResult(new(MediaFolderEnumerationStatus.Succeeded,"",[]));await Task.WhenAll(navigate,expand);
        Assert.Empty(model.Assets);Assert.Empty(model.Roots);Assert.False(model.IsAvailable);
    }
    [AvaloniaFact]
    public async Task RealBrowserListUsesSingleSelectionAndShowsEmptyState()
    {
        var service=new FakeBrowser();var model=new BrowserViewModel(service);await model.OpenAsync(service.CatalogId);
        var pane=new BrowserPane(model);var window=new Window {Content=pane,Width=1100,Height=700};window.Show();
        var list=pane.GetVisualDescendants().OfType<ListBox>().Single();Assert.Equal(SelectionMode.Single,list.SelectionMode);
        Assert.Contains("Choose a Media Root",model.Message);window.Close();model.Suspend();
    }
    private static MediaAsset Asset(Guid root,string path)=>new(Guid.NewGuid(),root,path,path.ToUpperInvariant(),"image",68,DateTime.UtcNow.Ticks,null,MediaAssetSourceStatus.Available,DateTimeOffset.UtcNow,DateTimeOffset.UtcNow,null);
    private sealed class FakeBrowser : IBrowserFolderSession
    {
        public Guid CatalogId=Guid.NewGuid(),RootId=Guid.NewGuid();
        public Dictionary<string,TaskCompletionSource<BrowserFolderSnapshot>> Folders=[];
        public TaskCompletionSource<bool> Source=new();public TaskCompletionSource<MediaFolderEnumerationResult> Expansion=new();
        public Task<BrowserCatalogSnapshot> ReadBrowserCatalogAsync(Guid? rootId=null,string folder="",bool recursive=false,CancellationToken cancellationToken=default)=>Task.FromResult(new BrowserCatalogSnapshot(CatalogId,[new(RootId,"Media","/task/Media",MediaRootAvailability.Online)],[]));
        public Task<BrowserFolderSnapshot> ReadBrowserFolderAsync(Guid rootId,string folder,CancellationToken token=default) {var pending=new TaskCompletionSource<BrowserFolderSnapshot>();Folders[folder]=pending;return pending.Task;}
        public Task<bool> IsBrowserSourceAvailableAsync(Guid rootId,Guid assetId,CancellationToken token=default)=>Source.Task;
        public Task<MediaFolderEnumerationResult> ReadBrowserFoldersAsync(Guid rootId,string folder,CancellationToken token=default)=>Expansion.Task;
    }
}
