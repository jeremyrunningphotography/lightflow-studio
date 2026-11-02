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
public sealed class VolumeLifecycleTests
{
    private static readonly BrowserFileLocation System=new("/","Macintosh HD",false,BrowserLocationSection.Devices,BrowserLocationKind.SystemVolume);
    private static readonly BrowserFileLocation Field=new("/Volumes/Field","Field",false,BrowserLocationSection.Devices,BrowserLocationKind.ExternalVolume);
    private static readonly BrowserFileLocation Share=new("/Volumes/JRP","JRP",false,BrowserLocationSection.Network,BrowserLocationKind.NetworkShare,"vault");
    [Fact]
    public async Task UnmountAndRemountPreserveContextAndNeverDiscoverOrMutateCatalog()
    {
        var session=new Session();var locations=new Locations();var model=new BrowserViewModel(session,locations);await model.OpenAsync(session.Id);
        await model.GoPathAsync("/Users/task");await model.GoPathAsync("/Volumes/Field/Child");await model.SelectAsync(session.Asset.AssetId);
        var before=(session.CatalogReads,session.Discoveries,session.SourceReads,locations.FolderReads);
        var identity=model.Location;var assets=model.Assets;var selected=model.SelectedAssetId;
        locations.Current=[System,Share];await model.ReconcileLocationsAsync();
        Assert.DoesNotContain(model.Roots,node=>node.AbsolutePath==Field.Path);
        Assert.Equal(identity,model.Location);Assert.Same(assets,model.Assets);Assert.Equal(selected,model.SelectedAssetId);Assert.True(model.CanGoBack);
        Assert.Contains("no longer mounted",model.Message);Assert.Contains("Source unavailable",model.SourceMessage);
        Assert.Equal(before,(session.CatalogReads,session.Discoveries,session.SourceReads,locations.FolderReads));
        // Even if the old mount-point directory still exists, Refresh must not ingest it as local storage.
        await model.RefreshAsync();Assert.Equal(before.Discoveries,session.Discoveries);Assert.Equal(before.FolderReads,locations.FolderReads);
        locations.Current=[System,Field,Share];await model.ReconcileLocationsAsync();
        Assert.Contains(model.Roots,node=>node.AbsolutePath==Field.Path);Assert.Equal(selected,model.SelectedAssetId);
        Assert.Contains("available again",model.Message);Assert.Equal(before.Discoveries,session.Discoveries);
        await model.RefreshAsync();Assert.Equal(before.Discoveries+1,session.Discoveries);Assert.Equal(selected,model.SelectedAssetId);
        // Mounted network shares follow the same path-preserving policy.
        await model.GoPathAsync(Share.Path);var shareIdentity=model.Location;var discoveries=session.Discoveries;
        locations.Current=[System,Field];await model.ReconcileLocationsAsync();Assert.Equal(shareIdentity,model.Location);
        Assert.DoesNotContain(model.Roots,node=>node.AbsolutePath==Share.Path);Assert.Contains("no longer mounted",model.Message);
        locations.Current=[System,Field,Share];await model.ReconcileLocationsAsync();Assert.Contains(model.Roots,node=>node.AbsolutePath==Share.Path);
        Assert.Equal(discoveries,session.Discoveries);model.Suspend();
    }
    [Fact]
    public async Task RenameChangesOnlyPresentationAndDuplicateSnapshotsDoNotRepublishRoots()
    {
        var session=new Session();var locations=new Locations();var model=new BrowserViewModel(session,locations);await model.OpenAsync(session.Id);
        await model.NavigateAsync(model.Roots.Single(node=>node.AbsolutePath==Field.Path));var before=model.Location!;var reads=session.Discoveries;
        locations.Current=[System,Field with {Name="Renamed Field"},Share];await model.ReconcileLocationsAsync();
        Assert.Equal("Renamed Field",model.Location!.Name);Assert.Equal(before.Key,model.Location.Key);
        Assert.Equal(before.RootId,model.Location.RootId);Assert.Equal(before.RelativeFolder,model.Location.RelativeFolder);
        var roots=model.Roots;await model.ReconcileLocationsAsync();Assert.Same(roots,model.Roots);Assert.Equal(reads,session.Discoveries);model.Suspend();
    }
    [Fact]
    public async Task SupersededNotificationsManualRefreshAndSuspensionRejectLateSnapshots()
    {
        var session=new Session();var locations=new Locations();var model=new BrowserViewModel(session,locations);await model.OpenAsync(session.Id);
        var late=locations.Next=new(TaskCreationOptions.RunContinuationsAsynchronously);var old=model.ReconcileLocationsAsync();
        locations.Current=[System,Share];await model.ReconcileLocationsAsync();late.SetResult([System,Field]);await old;
        Assert.Contains(model.Roots,node=>node.AbsolutePath==Share.Path);Assert.DoesNotContain(model.Roots,node=>node.AbsolutePath==Field.Path);
        late=locations.Next=new(TaskCreationOptions.RunContinuationsAsynchronously);old=model.ReconcileLocationsAsync();
        locations.Current=[System,Field,Share];await model.RefreshAsync();late.SetResult([System]);await old;
        Assert.Contains(model.Roots,node=>node.AbsolutePath==Field.Path);
        late=locations.Next=new(TaskCreationOptions.RunContinuationsAsynchronously);old=model.ReconcileLocationsAsync();model.Suspend();late.SetResult([System]);await old;
        Assert.Empty(model.Roots);Assert.False(model.IsAvailable);Assert.Equal(0,session.Discoveries);
    }
    [Fact]
    public async Task FailedOrStoppedSnapshotRetainsLocationsAndOffersManualRetryWithoutScanning()
    {
        var session=new Session();var locations=new Locations();var model=new BrowserViewModel(session,locations);await model.OpenAsync(session.Id);
        var roots=model.Roots;locations.Failure=new OperationCanceledException();await model.ReconcileLocationsAsync();
        Assert.Same(roots,model.Roots);Assert.Contains("Refresh",model.Message);Assert.Equal(0,session.Discoveries);
        locations.Failure=new UnauthorizedAccessException();await model.ReconcileLocationsAsync();
        Assert.Same(roots,model.Roots);Assert.Contains("Refresh",model.Message);Assert.Equal(0,session.Discoveries);model.Suspend();
    }
    [AvaloniaFact]
    public async Task BackgroundNotificationsDispatchToSharedModelWithoutReloadingRetainedFolderTrees()
    {
        var session=new Session();var locations=new Locations();var model=new BrowserViewModel(session,locations);await model.OpenAsync(session.Id);
        var pane=new BrowserPane(model);var window=new Window {Content=pane};window.Show();
        var source=new Source();await using var monitor=new BrowserLocationMonitor(source,async token=>
        {
            await Dispatcher.UIThread.InvokeAsync(()=>model.ReconcileLocationsAsync(token));
        },TimeSpan.FromMilliseconds(10));
        try
        {
            Button Find(string name)=>pane.GetVisualDescendants().OfType<Button>().Single(button=>AutomationProperties.GetName(button)==name);
            Find("Expand Field").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Dispatcher.UIThread.RunJobs();
            var child=Find("Child");var content=child.Content;var folderReads=locations.FolderReads;var roots=model.Roots;
            var uiPublications=0;model.PropertyChanged+=(_,_)=>{Assert.True(Dispatcher.UIThread.CheckAccess());uiPublications++;};
            locations.Current=[System,Field,Share,new("/Volumes/Local","Local",false,BrowserLocationSection.Devices,BrowserLocationKind.LocalVolume)];
            await Task.Run(()=>{for(var i=0;i<20;i++)source.Fire();});await monitor.WhenIdleAsync().WaitAsync(TimeSpan.FromSeconds(5));Dispatcher.UIThread.RunJobs();
            Assert.True(Find("Local").IsEffectivelyVisible);Assert.Same(child,Find("Child"));Assert.Same(content,child.Content);
            Assert.Equal(folderReads,locations.FolderReads);Assert.Equal(0,session.Discoveries);Assert.Equal(1,session.CatalogReads);Assert.True(uiPublications>0);
            roots=model.Roots;source.Fire();await monitor.WhenIdleAsync().WaitAsync(TimeSpan.FromSeconds(5));Assert.Same(roots,model.Roots);
        }
        finally {await monitor.DisposeAsync();window.Close();model.Suspend();}
    }
    private sealed class Source : IBrowserLocationChangeSource
    {public event Action? Changed;public void Fire()=>Changed?.Invoke();public void Dispose(){} }
    private sealed class Locations : IBrowserLocationProvider
    {
        public IReadOnlyList<BrowserFileLocation> Current=[System,Field,Share];
        public TaskCompletionSource<IReadOnlyList<BrowserFileLocation>>? Next;
        public int FolderReads;
        public Exception? Failure;
        public Task<IReadOnlyList<BrowserFileLocation>> GetLocationsAsync(CancellationToken token)
        {if(Failure is { } error)return Task.FromException<IReadOnlyList<BrowserFileLocation>>(error);var pending=Next;Next=null;return pending?.Task ?? Task.FromResult(Current);}
        public Task<BrowserLocationListing> GetFoldersAsync(string path,CancellationToken token,bool userInitiated=false)
        {FolderReads++;return Task.FromResult(new BrowserLocationListing(MediaFolderEnumerationStatus.Succeeded,path==Field.Path ? [new(Field.Path+"/Child","Child",false)] : []));}
    }
    private sealed class Session : IBrowserDiscoverySession
    {
        public Guid Id=Guid.NewGuid(),Root=Guid.NewGuid();public int CatalogReads,Discoveries,SourceReads;
        public MediaAsset Asset=new(Guid.NewGuid(),Guid.NewGuid(),"Child/forest.png","CHILD/FOREST.PNG","image",1,DateTime.UtcNow.Ticks,null,MediaAssetSourceStatus.Available,DateTimeOffset.UtcNow,DateTimeOffset.UtcNow,null);
        public Task<BrowserCatalogSnapshot> ReadBrowserCatalogAsync(Guid? rootId=null,string folder="",bool recursive=false,CancellationToken cancellationToken=default)
        {CatalogReads++;return Task.FromResult(new BrowserCatalogSnapshot(Id,[new(Root,"Field",Field.Path,MediaRootAvailability.Online)],[]));}
        public Task<BrowserFolderSnapshot> DiscoverBrowserFolderAsync(string? path,Guid rootId,string folder,CancellationToken token=default)
        {Discoveries++;return Task.FromResult(new BrowserFolderSnapshot(rootId==Guid.Empty?Root:rootId,folder,false,MediaFolderEnumerationStatus.Succeeded,[],[Asset]));}
        public Task<BrowserFolderSnapshot> ReadBrowserFolderAsync(Guid rootId,string folder,CancellationToken token=default)=>throw new InvalidOperationException("Unexpected Catalog scan");
        public Task<MediaFolderEnumerationResult> ReadBrowserFoldersAsync(Guid rootId,string folder,CancellationToken token=default)=>throw new InvalidOperationException("Unexpected Catalog expansion");
        public Task<bool> IsBrowserSourceAvailableAsync(Guid rootId,Guid assetId,CancellationToken token=default){SourceReads++;return Task.FromResult(true);}
        public Task SetBrowserRecursiveAsync(Guid rootId,string folder,bool enabled,CancellationToken token=default)=>Task.CompletedTask;
    }
}
