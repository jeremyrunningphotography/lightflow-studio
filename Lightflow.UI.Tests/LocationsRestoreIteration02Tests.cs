using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Lightflow.Application;
using Lightflow.UI;
using LightflowStudio;
using Xunit;
namespace Lightflow.UI.Tests;
public sealed class LocationsRestoreIteration02Tests
{
    private static async Task Settle(Window window,Func<bool> ready)
    {
        var until=DateTime.UtcNow.AddSeconds(8);
        while(!ready()) {window.UpdateLayout();Dispatcher.UIThread.RunJobs();Assert.True(DateTime.UtcNow<until,"Restored tree did not settle");await Task.Delay(5);}
        window.UpdateLayout();Dispatcher.UIThread.RunJobs();window.UpdateLayout();
    }
    private static Button? Named(Control pane,string name)=>pane.GetVisualDescendants().OfType<Button>().SingleOrDefault(b=>AutomationProperties.GetName(b)==name);
    [AvaloniaTheory]
    [InlineData("Selected",false)]
    [InlineData("a/b/Selected",false)]
    [InlineData("a/b/Selected",true)]
    public async Task RestartRevealsOnlyTheMatchingAncestorChainAndScrollsToItsSelectedFolder(string relative,bool network)
    {
        var service=new RestoreSession();var provider=new RestoreLocations(relative,network);var model=new BrowserViewModel(service,provider);
        var saved=new BrowserLayout(Presentation:BrowserLayoutMode.Details,LastLocation:new(service.Id,service.Root,relative,RestoreLocations.Root+"/"+relative));
        var pane=new BrowserPane(model,saved);var window=new Window {Content=pane,Width=1120,Height=720};LightflowTheme.Apply(window);window.Show();
        try
        {
            await model.OpenAsync(service.Id);await Settle(window,()=>Named(pane,"Selected")?.IsEffectivelyVisible==true);
            var selected=Named(pane,"Selected")!;Assert.Contains("active",selected.Classes);Assert.Contains("Current folder",AutomationProperties.GetHelpText(selected));
            Assert.Equal(service.Root,model.Location!.RootId);Assert.Equal(relative,model.Location.RelativeFolder);Assert.True(model.Recursive);Assert.True(model.IsProvisional);
            Assert.Equal(0,service.Discoveries);Assert.Equal(0,service.Mutations);Assert.Equal(1,service.CachedReads);
            var expected=new List<string>{RestoreLocations.Root+"/"+relative};var path=RestoreLocations.Root;
            foreach(var part in relative.Split('/')) {expected.Add(path);path+="/"+part;}
            Assert.Equal(expected.Order(),provider.Requests.Select(r=>r.Path).Order());Assert.All(provider.Requests,r=>Assert.False(r.User));
            Assert.NotNull(Named(pane,"Expand Unrelated"));Assert.Contains(pane.GetVisualDescendants().OfType<Button>(),b=>AutomationProperties.GetName(b)=="Expand Sibling00");
            Assert.DoesNotContain(provider.Requests,r=>r.Path.Contains("Sibling") || r.Path=="/Unrelated");
            var side=pane.GetVisualDescendants().OfType<ScrollViewer>().Single(s=>s.Content is StackPanel p && p.Children.OfType<TextBlock>().Any(t=>t.Text=="Locations"));
            Assert.True(side.Offset.Y>0);Assert.InRange(selected.TranslatePoint(default,side)!.Value.Y,0,side.Bounds.Height-selected.Bounds.Height+1);
            // A live root snapshot change keeps already-loaded ancestors and the selected row, without relisting.
            var before=provider.Requests.Count;provider.AdditionalRoot=true;await model.ReconcileLocationsAsync();await Settle(window,()=>Named(pane,"Additional") is not null);
            Assert.Same(selected,Named(pane,"Selected"));Assert.True(selected.IsEffectivelyVisible);Assert.Equal(before,provider.Requests.Count);
            pane.SetPresentation(BrowserLayoutMode.Grid);pane.SetPresentation(BrowserLayoutMode.Details);Assert.Same(selected,Named(pane,"Selected"));
        }
        finally {window.Close();model.Suspend();}
    }
    [AvaloniaFact]
    public async Task PendingAncestorEnumerationIsAwaitedWithoutExpandingItsSiblings()
    {
        var service=new RestoreSession();var provider=new RestoreLocations("a/b/Selected",true) {PendingRoot=new(TaskCreationOptions.RunContinuationsAsynchronously)};
        var model=new BrowserViewModel(service,provider);var pane=new BrowserPane(model,new BrowserLayout(LastLocation:new(service.Id,service.Root,"a/b/Selected",RestoreLocations.Root+"/a/b/Selected")));
        var window=new Window {Content=pane,Width=1120,Height=720};window.Show();
        try
        {
            await model.OpenAsync(service.Id);Assert.True(model.IsAvailable);Assert.False(model.IsLoading);Assert.Null(Named(pane,"Selected"));
            provider.PendingRoot.SetResult(new(MediaFolderEnumerationStatus.Succeeded,provider.Folders[RestoreLocations.Root]));
            await Settle(window,()=>Named(pane,"Selected")?.IsEffectivelyVisible==true);
            Assert.Single(provider.Requests,r=>r.Path==RestoreLocations.Root);Assert.Equal(0,service.Discoveries);Assert.Equal(0,service.Mutations);
        }
        finally {window.Close();model.Suspend();}
    }
    [AvaloniaTheory]
    [InlineData(true)] [InlineData(false)]
    public async Task UnavailableOrRemovedCatalogLocationUsesExistingFallbackAndDoesNotRevealATree(bool unavailable)
    {
        var service=new RestoreSession {HasRoot=unavailable};var provider=new RestoreLocations("a/b/Selected",true) {Unavailable=unavailable};
        var model=new BrowserViewModel(service,provider);var pane=new BrowserPane(model,new BrowserLayout(LastLocation:new(service.Id,service.Root,"a/b/Selected",RestoreLocations.Root+"/a/b/Selected")));
        var window=new Window {Content=pane};window.Show();
        try
        {
            await model.OpenAsync(service.Id);window.UpdateLayout();Dispatcher.UIThread.RunJobs();
            Assert.Null(model.Location);Assert.Null(model.RestoredLocation);Assert.Empty(model.Assets);Assert.Contains("unavailable",model.Message);
            Assert.Null(Named(pane,"Selected"));Assert.NotNull(Named(pane,"Expand Photos"));Assert.Equal(0,service.Discoveries);Assert.Equal(0,service.CachedReads);
            Assert.All(provider.Requests,r=>Assert.Equal(RestoreLocations.Root+"/a/b/Selected",r.Path));
        }
        finally {window.Close();model.Suspend();}
    }
    [AvaloniaFact]
    public async Task LateAncestorCompletionCannotRevealRowsAfterCatalogSuspension()
    {
        var service=new RestoreSession();var provider=new RestoreLocations("a/b/Selected",true) {PendingRoot=new(TaskCreationOptions.RunContinuationsAsynchronously)};
        var model=new BrowserViewModel(service,provider);var pane=new BrowserPane(model,new BrowserLayout(LastLocation:new(service.Id,service.Root,"a/b/Selected",RestoreLocations.Root+"/a/b/Selected")));
        var window=new Window {Content=pane};window.Show();
        try
        {
            await model.OpenAsync(service.Id);model.Suspend();provider.PendingRoot.SetResult(new(MediaFolderEnumerationStatus.Succeeded,provider.Folders[RestoreLocations.Root]));
            await Task.Delay(20);Dispatcher.UIThread.RunJobs();Assert.Null(Named(pane,"Selected"));Assert.Null(model.Location);Assert.Empty(model.Assets);
        }
        finally {window.Close();model.Suspend();}
    }
    [AvaloniaFact]
    public async Task TreeRevealDeadlineDoesNotBlockCatalogOpenOrPublishLateChildren()
    {
        var service=new RestoreSession();var provider=new RestoreLocations("a/b/Selected",true) {PendingRoot=new(TaskCreationOptions.RunContinuationsAsynchronously)};
        var model=new BrowserViewModel(service,provider);var pane=new BrowserPane(model,new BrowserLayout(LastLocation:new(service.Id,service.Root,"a/b/Selected",RestoreLocations.Root+"/a/b/Selected")));
        var window=new Window {Content=pane};window.Show();
        try
        {
            await model.OpenAsync(service.Id).WaitAsync(TimeSpan.FromSeconds(2));Assert.False(model.IsLoading);
            await provider.CancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(7));Dispatcher.UIThread.RunJobs();
            await Settle(window,()=>pane.StatusView.GetVisualDescendants().OfType<TextBlock>().Any(t=>t.Text?.Contains("could not be fully revealed")==true));
            provider.PendingRoot.SetResult(new(MediaFolderEnumerationStatus.Succeeded,provider.Folders[RestoreLocations.Root]));
            await Task.Delay(20);Dispatcher.UIThread.RunJobs();Assert.Null(Named(pane,"Selected"));Assert.Equal("a/b/Selected",model.Location!.RelativeFolder);
            Assert.True(model.IsAvailable);Assert.Equal(0,service.Discoveries);Assert.Equal(0,service.Mutations);
        }
        finally {window.Close();model.Suspend();}
    }
    [AvaloniaFact]
    public async Task ExplicitUserCollapseCancelsPendingAutomaticRevealWithoutReopeningTheBranch()
    {
        var service=new RestoreSession();var provider=new RestoreLocations("a/b/Selected",true) {PendingRoot=new(TaskCreationOptions.RunContinuationsAsynchronously)};
        var model=new BrowserViewModel(service,provider);var pane=new BrowserPane(model,new BrowserLayout(LastLocation:new(service.Id,service.Root,"a/b/Selected",RestoreLocations.Root+"/a/b/Selected")));
        var window=new Window {Content=pane};window.Show();
        try
        {
            await model.OpenAsync(service.Id);var collapse=Named(pane,"Collapse Photos")!;
            collapse.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            provider.PendingRoot.SetResult(new(MediaFolderEnumerationStatus.Succeeded,provider.Folders[RestoreLocations.Root]));
            await Task.Delay(20);Dispatcher.UIThread.RunJobs();provider.AdditionalRoot=true;await model.ReconcileLocationsAsync();Dispatcher.UIThread.RunJobs();
            Assert.NotNull(Named(pane,"Expand Photos"));Assert.Null(Named(pane,"Selected"));Assert.Equal("a/b/Selected",model.Location!.RelativeFolder);
            Assert.DoesNotContain(provider.Requests,r=>r.Path==RestoreLocations.Root+"/a");Assert.Equal(0,service.Discoveries);
        }
        finally {window.Close();model.Suspend();}
    }
    private sealed class RestoreLocations:IBrowserLocationProvider
    {
        public const string Root="/Volumes/Photos";
        private readonly bool _network;public bool Unavailable,AdditionalRoot;
        public TaskCompletionSource<BrowserLocationListing>? PendingRoot;
        public TaskCompletionSource<bool> CancellationObserved=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Dictionary<string,IReadOnlyList<BrowserFileLocation>> Folders=[];
        public List<(string Path,bool User)> Requests=[];
        public RestoreLocations(string relative,bool network)
        {
            _network=network;var path=Root;
            foreach(var part in relative.Split('/'))
            {
                var children=Enumerable.Range(0,25).Select(i=>new BrowserFileLocation(path+$"/Sibling{i:D2}",$"Sibling{i:D2}",false)).ToList();
                children.Add(new(path+"/"+part,part,false));Folders[path]=children;path+="/"+part;
            }
            Folders[path]=[];
        }
        public Task<IReadOnlyList<BrowserFileLocation>> GetLocationsAsync(CancellationToken token)
        {
            var result=new List<BrowserFileLocation>{new(Root,"Photos",false,_network ? BrowserLocationSection.Network : BrowserLocationSection.Devices,_network ? BrowserLocationKind.NetworkShare : BrowserLocationKind.ExternalVolume,_network ? "vault" : null),new("/Unrelated","Unrelated",false,BrowserLocationSection.Favorites,BrowserLocationKind.Home)};
            if(AdditionalRoot)result.Add(new("/Additional","Additional",false,BrowserLocationSection.Devices,BrowserLocationKind.LocalVolume));
            return Task.FromResult<IReadOnlyList<BrowserFileLocation>>(result);
        }
        public Task<BrowserLocationListing> GetFoldersAsync(string path,CancellationToken token,bool userInitiated=false)
        {
            Requests.Add((path,userInitiated));
            if(Unavailable)return Task.FromResult(new BrowserLocationListing(MediaFolderEnumerationStatus.FolderUnavailable,[]));
            if(path==Root && PendingRoot is not null) {token.Register(()=>CancellationObserved.TrySetResult(true));return PendingRoot.Task;}
            return Task.FromResult(new BrowserLocationListing(MediaFolderEnumerationStatus.Succeeded,Folders.GetValueOrDefault(path,[])));
        }
    }
    private sealed class RestoreSession:IBrowserFolderSession,IBrowserCachedFolderSession,IBrowserDiscoverySession
    {
        public Guid Id=Guid.NewGuid(),Root=Guid.NewGuid();public bool HasRoot=true;public int Discoveries,Mutations,CachedReads;
        public Task<BrowserCatalogSnapshot> ReadBrowserCatalogAsync(Guid? rootId=null,string folder="",bool recursive=false,CancellationToken cancellationToken=default)=>Task.FromResult(new BrowserCatalogSnapshot(Id,HasRoot ? [new(Root,"Photos",RestoreLocations.Root,MediaRootAvailability.Online)] : [],[]));
        public Task<BrowserFolderSnapshot> ReadCachedBrowserFolderAsync(Guid rootId,string folder,CancellationToken token=default){CachedReads++;return Task.FromResult(new BrowserFolderSnapshot(rootId,folder,true,MediaFolderEnumerationStatus.Succeeded,[],[]));}
        public Task<BrowserFolderSnapshot> ReadBrowserFolderAsync(Guid rootId,string folder,CancellationToken token=default)=>throw new InvalidOperationException("Tree reveal must not read/discover Browser assets");
        public Task<BrowserFolderSnapshot> DiscoverBrowserFolderAsync(string? path,Guid rootId,string folder,CancellationToken token=default){Discoveries++;throw new InvalidOperationException("No tree-driven discovery");}
        public Task SetBrowserRecursiveAsync(Guid rootId,string folder,bool enabled,CancellationToken token=default){Mutations++;return Task.CompletedTask;}
        public Task<bool> IsBrowserSourceAvailableAsync(Guid rootId,Guid assetId,CancellationToken token=default)=>Task.FromResult(true);
        public Task<MediaFolderEnumerationResult> ReadBrowserFoldersAsync(Guid rootId,string folder,CancellationToken token=default)=>throw new InvalidOperationException("Use the existing Locations directory provider");
    }
}
