using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Lightflow.Application;
using Lightflow.UI;
using LightflowStudio;
using Xunit;

namespace Lightflow.UI.Tests;
public sealed class LocationsTests
{
    private static Button Button(Control pane,string name)=>pane.GetVisualDescendants().OfType<Button>()
        .Single(button=>AutomationProperties.GetName(button)==name);
    private static void Click(Button button)=>button.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
    private static async Task Drain(Func<bool> ready)
    {
        var end=DateTime.UtcNow.AddSeconds(5);
        while(!ready()) { Dispatcher.UIThread.RunJobs(); Assert.True(DateTime.UtcNow<end,"Async tree did not settle"); await Task.Delay(5); }
        Dispatcher.UIThread.RunJobs();
    }
    [AvaloniaFact]
    public async Task CompleteRowsExistBeforeDiscoveryAndRetainIconLabelGeometryThroughLeafResolutionAndRefresh()
    {
        var service=new Session(); var provider=new Locations { Delay=true };
        var model=new BrowserViewModel(service,provider); await model.OpenAsync(service.Id);
        var pane=new BrowserPane(model); var window=new Window { Content=pane,Width=1120,Height=720 }; LightflowTheme.Apply(window); window.Show();
        try
        {
            var home=Button(pane,"account"); var content=Assert.IsType<Grid>(home.Content);
            var icon=content.Children[0]; var label=Assert.IsType<TextBlock>(content.Children[1]);
            Assert.Equal("account",label.Text); Assert.Equal(16,icon.Width);
            Assert.Empty(provider.Requests); // no protected built-in Favorites prefetched
            Click(Button(pane,"Expand account"));
            Assert.Same(content,home.Content); Assert.Single(provider.Requests);
            provider.Pending.SetResult(new(MediaFolderEnumerationStatus.Succeeded,[new("/Users/account/Child","Child",false)]));
            await Drain(()=>pane.GetVisualDescendants().OfType<Button>().Any(button=>AutomationProperties.GetName(button)=="Child"));
            var child=Button(pane,"Child"); var childContent=Assert.IsType<Grid>(child.Content);
            Assert.Equal("Child",Assert.IsType<TextBlock>(childContent.Children[1]).Text);
            // Children are born complete even while their disclosure has never been inspected.
            window.UpdateLayout(); var x=childContent.Children[1].Bounds.X;
            var childToggle=Button(pane,"Expand Child"); Click(childToggle);
            await Drain(()=>!childToggle.IsVisible);
            window.UpdateLayout(); Assert.Equal(x,childContent.Children[1].Bounds.X);
            provider.Delay=false;
            await model.RefreshAsync(); await Drain(()=>ReferenceEquals(content,home.Content));
            Assert.Same(icon,content.Children[0]); Assert.Same(label,content.Children[1]);
            Assert.Same(child,Button(pane,"Child")); Assert.Same(childContent,child.Content);
        }
        finally { window.Close();model.Suspend(); }
    }
    [AvaloniaFact]
    public async Task SectionCollapseIsSessionOnlyRetainsTreeAndNeverChangesNavigationOrReopensOnAddressHistoryRefresh()
    {
        var service=new Session();var provider=new Locations();var model=new BrowserViewModel(service,provider);await model.OpenAsync(service.Id);
        var pane=new BrowserPane(model);var window=new Window { Content=pane,Width=1120,Height=720 };window.Show();
        try
        {
            Click(Button(pane,"Expand account")); await Drain(()=>pane.GetVisualDescendants().OfType<Button>().Any(button=>AutomationProperties.GetName(button)=="Child"));
            var child=Button(pane,"Child");await model.GoPathAsync("/Users/account/Child");var current=model.Location;
            Click(Button(pane,"Collapse Favorites")); Assert.False(child.IsEffectivelyVisible); Assert.Same(current,model.Location);
            await model.GoPathAsync("/");await model.BackAsync();await model.RefreshAsync();
            Assert.Equal("/Users/account/Child",model.Location!.AbsolutePath); Assert.False(child.IsEffectivelyVisible);
            Click(Button(pane,"Expand Favorites"));Assert.True(child.IsEffectivelyVisible);Assert.Same(child,Button(pane,"Child"));
            var fresh=new BrowserPane(model);var second=new Window {Content=fresh};second.Show();
            Assert.NotNull(Button(fresh,"Collapse Favorites"));Assert.NotNull(Button(fresh,"Collapse Devices & Volumes"));Assert.NotNull(Button(fresh,"Collapse Network"));second.Close();
        }
        finally { window.Close();model.Suspend(); }
    }
    [AvaloniaFact]
    public async Task NetworkServerIsOrganizationalAndDisconnectRefreshRemovesLiveSharePreservingCurrentAssetAndHistory()
    {
        var service=new Session();var provider=new Locations();var model=new BrowserViewModel(service,provider);await model.OpenAsync(service.Id);
        var pane=new BrowserPane(model);var window=new Window { Content=pane,Width=1120,Height=720 };window.Show();
        try
        {
            Assert.NotNull(Button(pane,"Collapse vault")); Assert.NotNull(Button(pane,"Flat share"));
            await model.GoPathAsync("/Users/account");await model.NavigateAsync(model.Roots.Single(node=>node.AbsolutePath=="/Volumes/JRP"));
            Assert.Equal(BrowserLocationKind.NetworkShare,model.Location!.Kind);
            await model.SelectAsync(service.Asset.AssetId);provider.Connected=false;await model.RefreshAsync();Dispatcher.UIThread.RunJobs();
            Assert.Equal("/Volumes/JRP",model.Location!.AbsolutePath);Assert.Equal(service.Asset.AssetId,model.SelectedAssetId);
            Assert.Contains("missing",model.Message);Assert.True(model.CanGoBack);
            Assert.DoesNotContain(model.Roots,node=>node.AbsolutePath=="/Volumes/JRP");
            Assert.DoesNotContain(pane.GetVisualDescendants().OfType<Button>(),button=>AutomationProperties.GetName(button)=="Mounted share");
            provider.Connected=true;await model.RefreshAsync();Dispatcher.UIThread.RunJobs();
            Assert.Equal(service.Asset.AssetId,model.SelectedAssetId);Assert.NotNull(Button(pane,"Mounted share"));
            await model.BackAsync();Assert.Equal("/Users/account",model.Location!.AbsolutePath);
            await model.ForwardAsync();Assert.Equal("/Volumes/JRP",model.Location!.AbsolutePath);
        }
        finally {window.Close();model.Suspend();}
    }
    [AvaloniaFact]
    public async Task LateExpansionCannotRestoreDisconnectedNetworkRows()
    {
        var service=new Session();var provider=new Locations { DelayShare=true };var model=new BrowserViewModel(service,provider);await model.OpenAsync(service.Id);
        var pane=new BrowserPane(model);var window=new Window {Content=pane};window.Show();
        try
        {
            Click(Button(pane,"Expand Mounted share"));provider.Connected=false;await model.RefreshAsync();
            provider.Pending.SetResult(new(MediaFolderEnumerationStatus.Succeeded,[new("/Volumes/JRP/Late","Late",false)]));
            await Task.Delay(20);Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain(pane.GetVisualDescendants().OfType<Button>(),button=>AutomationProperties.GetName(button)=="Late");
        }
        finally {window.Close();model.Suspend();}
    }
    private sealed class Locations : IBrowserLocationProvider
    {
        public bool Connected=true,Delay,DelayShare;
        public List<string> Requests=[];
        public TaskCompletionSource<BrowserLocationListing> Pending=new();
        public Task<IReadOnlyList<BrowserFileLocation>> GetLocationsAsync(CancellationToken token)
        {
            var result=new List<BrowserFileLocation> {
                new("/Users/account","account",false,BrowserLocationSection.Favorites,BrowserLocationKind.Home),
                new("/Users/account/Desktop","Desktop",false,BrowserLocationSection.Favorites,BrowserLocationKind.Desktop),
                new("/Users/account/Documents","Documents",false,BrowserLocationSection.Favorites,BrowserLocationKind.Documents),
                new("/Users/account/Downloads","Downloads",false,BrowserLocationSection.Favorites,BrowserLocationKind.Downloads),
                new("/","Macintosh HD",false,BrowserLocationSection.Devices,BrowserLocationKind.SystemVolume),
                new("/Volumes/Flat","Flat share",false,BrowserLocationSection.Network,BrowserLocationKind.NetworkShare) };
            if(Connected)result.Add(new("/Volumes/JRP","Mounted share",false,BrowserLocationSection.Network,BrowserLocationKind.NetworkShare,"vault"));
            return Task.FromResult<IReadOnlyList<BrowserFileLocation>>(result);
        }
        public Task<BrowserLocationListing> GetFoldersAsync(string path,CancellationToken token,bool userInitiated=false)
        {
            Requests.Add(path);
            if((Delay && path=="/Users/account") || (DelayShare && path=="/Volumes/JRP"))return Pending.Task;
            return Task.FromResult(new BrowserLocationListing(!Connected && path=="/Volumes/JRP" ? MediaFolderEnumerationStatus.FolderNotFound : MediaFolderEnumerationStatus.Succeeded,
                path=="/Users/account" ? [new("/Users/account/Child","Child",false)] : []));
        }
    }
    private sealed class Session : IBrowserFolderSession
    {
        public Guid Id=Guid.NewGuid(), Root=Guid.NewGuid();
        public MediaAsset Asset=new(Guid.NewGuid(),Guid.NewGuid(),"one.png","ONE.PNG","image",1,DateTime.UtcNow.Ticks,null,MediaAssetSourceStatus.Available,DateTimeOffset.UtcNow,DateTimeOffset.UtcNow,null);
        public Task<BrowserCatalogSnapshot> ReadBrowserCatalogAsync(Guid? rootId=null,string folder="",bool recursive=false,CancellationToken cancellationToken=default)
            =>Task.FromResult(new BrowserCatalogSnapshot(Id,[new(Root,"Mounted share","/Volumes/JRP",MediaRootAvailability.Online)],[]));
        public Task<BrowserFolderSnapshot> ReadBrowserFolderAsync(Guid rootId,string folder,CancellationToken token=default)
            =>Task.FromResult(new BrowserFolderSnapshot(rootId,folder,false,MediaFolderEnumerationStatus.Succeeded,[],[Asset]));
        public Task<MediaFolderEnumerationResult> ReadBrowserFoldersAsync(Guid rootId,string folder,CancellationToken token=default)
            =>Task.FromResult(new MediaFolderEnumerationResult(MediaFolderEnumerationStatus.Succeeded,folder,[]));
        public Task<bool> IsBrowserSourceAvailableAsync(Guid rootId,Guid assetId,CancellationToken token=default)=>Task.FromResult(true);
    }
}
