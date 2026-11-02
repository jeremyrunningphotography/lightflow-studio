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
            var homeToggle=Button(pane,"Expand account");
            var disclosure=Assert.IsType<Avalonia.Controls.Shapes.Path>(homeToggle.Content);
            Assert.Equal(12,disclosure.Width); Assert.Equal(12,disclosure.Height);
            Assert.Equal(1.25,disclosure.StrokeThickness);
            Assert.True(homeToggle.Bounds.Width>=32 && homeToggle.Bounds.Height>=32);
            Assert.True(homeToggle.Focus(Avalonia.Input.NavigationMethod.Tab));
            Click(homeToggle);
            Assert.True(homeToggle.IsFocused);
            Assert.Same(content,home.Content); Assert.Single(provider.Requests);
            provider.Pending.SetResult(new(MediaFolderEnumerationStatus.Succeeded,[new("/Users/account/Child","Child",false)]));
            await Drain(()=>pane.GetVisualDescendants().OfType<Button>().Any(button=>AutomationProperties.GetName(button)=="Child"));
            var child=Button(pane,"Child"); var childContent=Assert.IsType<Grid>(child.Content);
            Assert.Equal("Child",Assert.IsType<TextBlock>(childContent.Children[1]).Text);
            // Children are born complete even while their disclosure has never been inspected.
            window.UpdateLayout(); var x=childContent.Children[1].Bounds.X; var chooseX=child.Bounds.X;
            var childToggle=Button(pane,"Expand Child"); Click(childToggle);
            await Drain(()=>!childToggle.IsVisible);
            window.UpdateLayout(); Assert.Equal(x,childContent.Children[1].Bounds.X); Assert.Equal(chooseX,child.Bounds.X);
            provider.Delay=false;
            await model.RefreshAsync(); await Drain(()=>ReferenceEquals(content,home.Content));
            Assert.Same(icon,content.Children[0]); Assert.Same(label,content.Children[1]);
            Assert.Same(disclosure,homeToggle.Content); Assert.True(homeToggle.IsFocused);
            Assert.Same(child,Button(pane,"Child")); Assert.Same(childContent,child.Content);
        }
        finally { window.Close();model.Suspend(); }
    }
    [AvaloniaFact]
    public async Task PermanentSectionHeadingsAndTopLevelLocationsSurviveNavigationHistoryAndRefresh()
    {
        var service=new Session();var provider=new Locations();var model=new BrowserViewModel(service,provider);await model.OpenAsync(service.Id);
        var pane=new BrowserPane(model);var window=new Window { Content=pane,Width=1120,Height=720 };LightflowTheme.Apply(window);window.Show();
        try
        {
            var names=new[] { "Favorites", "Devices & Volumes", "Network" };
            void PermanentSections()
            {
                var headings=pane.GetVisualDescendants().OfType<TextBlock>().Where(text=>text.Classes.Contains("location-section-heading")).ToArray();
                Assert.Equal(names,headings.Select(text=>text.Text));
                foreach(var heading in headings)
                {
                    Assert.True(heading.IsEffectivelyVisible); Assert.False(heading.Focusable);
                    Assert.Equal(heading.Text,AutomationProperties.GetName(heading));
                    var header=Assert.IsType<Border>(heading.Parent);
                    Assert.Empty(header.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>());
                }
                Assert.DoesNotContain(pane.GetVisualDescendants().OfType<Button>(),button=>names.Any(name=>
                    AutomationProperties.GetName(button) is { } label && (label==name || label=="Expand "+name || label=="Collapse "+name)));
                foreach(var name in new[] { "account", "Desktop", "Documents", "Downloads", "Macintosh HD", "Flat share", "Mounted share" })
                    Assert.True(Button(pane,name).IsEffectivelyVisible);
            }
            PermanentSections();
            Click(Button(pane,"Expand account")); await Drain(()=>pane.GetVisualDescendants().OfType<Button>().Any(button=>AutomationProperties.GetName(button)=="Child"));
            var child=Button(pane,"Child");await model.GoPathAsync("/Users/account/Child");
            await model.GoPathAsync("/");await model.BackAsync();await model.RefreshAsync();
            Assert.Equal("/Users/account/Child",model.Location!.AbsolutePath);
            PermanentSections(); Assert.True(child.IsEffectivelyVisible);Assert.Same(child,Button(pane,"Child"));
            // Tree-node collapse remains independent of permanent section headings.
            Click(Button(pane,"Collapse account"));Assert.False(child.IsEffectivelyVisible);PermanentSections();
            Assert.Equal("/Users/account/Child",model.Location.AbsolutePath);
            Click(Button(pane,"Expand account"));Assert.True(child.IsEffectivelyVisible);
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
            var server=Button(pane,"Collapse vault"); Assert.NotNull(Button(pane,"Flat share"));
            Click(server); Assert.False(Button(pane,"Mounted share").IsEffectivelyVisible);
            Assert.True(Button(pane,"Flat share").IsEffectivelyVisible);
            await model.RefreshAsync(); Assert.Same(server,Button(pane,"Expand vault"));
            Click(server); Assert.True(Button(pane,"Mounted share").IsEffectivelyVisible);
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
