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
        Assert.Contains("Choose a location",model.Message);window.Close();model.Suspend();
    }
    [Fact]
    public async Task RefreshRetainsFolderAndAssetButNewNavigationWins()
    {
        var service=new FakeBrowser(); var model=new BrowserViewModel(service);
        await model.OpenAsync(service.CatalogId);
        var load=model.NavigateAsync(model.Roots[0]); var asset=Asset(service.RootId,"kept.png");
        service.Folders[""].SetResult(new(service.RootId,"",false,MediaFolderEnumerationStatus.Succeeded,[],[asset])); await load;
        service.Source.SetResult(true); await model.SelectAsync(asset.AssetId);
        var epoch=model.Epoch; var refresh=model.RefreshAsync();
        service.Folders[""].SetResult(new(service.RootId,"",false,MediaFolderEnumerationStatus.Succeeded,[],[asset])); await refresh;
        Assert.Equal(epoch,model.Epoch); Assert.Equal(asset.AssetId,model.SelectedAssetId);
        var removed=model.RefreshAsync(); service.Folders[""].SetResult(new(service.RootId,"",false,MediaFolderEnumerationStatus.Succeeded,[],[])); await removed;
        Assert.Null(model.SelectedAssetId); Assert.NotNull(model.Location);
        var stale=model.RefreshAsync(); var old=service.Folders[""];
        var navigate=model.NavigateAsync(new(service.RootId,"Other","Other"));
        service.Folders["Other"].SetResult(new(service.RootId,"Other",false,MediaFolderEnumerationStatus.Succeeded,[],[])); await navigate;
        old.SetResult(new(service.RootId,"",false,MediaFolderEnumerationStatus.Succeeded,[],[asset])); await stale;
        Assert.Equal("Other",model.Location!.Name); Assert.Empty(model.Assets);
    }
    [Fact]
    public async Task UncatalogedNavigationNeverCallsCatalogFolderReaderAndMappedScopeUsesExistingIdentity()
    {
        var service=new FakeBrowser(); var provider=new FakeLocations(); var model=new BrowserViewModel(service,provider);
        await model.OpenAsync(service.CatalogId);
        Assert.Contains(model.Roots,node=>node.Name=="Home");
        await model.NavigateAsync(model.Roots.Single(node=>node.Name=="Home"));
        Assert.Contains("not cataloged",model.Message); Assert.Empty(service.Folders); Assert.Empty(model.Assets);
        var folders=await model.ExpandAsync(model.Location!); Assert.Single(folders);
        var load=model.NavigateAsync(folders[0]); var asset=Asset(service.RootId,"one.png");
        service.Folders[""].SetResult(new(service.RootId,"",false,MediaFolderEnumerationStatus.Succeeded,[],[asset])); await load;
        Assert.Equal(service.RootId,model.Location!.RootId); Assert.Equal(asset,Assert.Single(model.Assets));
        provider.Status=MediaFolderEnumerationStatus.AccessDenied;
        await model.NavigateAsync(new(Guid.Empty,"","Denied",AbsolutePath:"/denied"));
        Assert.Contains("permissions",model.Message); Assert.Empty(model.Assets);
    }
    [AvaloniaFact]
    public async Task LeafHasNoDisclosureAndRefreshRetainsExpandedBranch()
    {
        var service=new FakeBrowser(); var provider=new FakeLocations { Delayed=true }; var model=new BrowserViewModel(service,provider);
        await model.OpenAsync(service.CatalogId);
        var pane=new BrowserPane(model); var window=new Window {Content=pane,Width=1100,Height=700}; window.Show();
        await Task.Delay(30); Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var expand=pane.GetVisualDescendants().OfType<Button>().Single(button=>Avalonia.Automation.AutomationProperties.GetName(button)=="Expand Home");
        Assert.True(expand.IsVisible);
        Assert.True(expand.Bounds.Width >= 32 && expand.Bounds.Height >= 32);
        expand.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        await Task.Delay(30); Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var leaves=pane.GetVisualDescendants().OfType<Button>().Where(button=>Avalonia.Automation.AutomationProperties.GetName(button)=="Expand Media").ToArray();
        Assert.NotEmpty(leaves); Assert.All(leaves, leaf=>Assert.False(leaf.IsVisible));
        await model.RefreshAsync(); await Task.Delay(30); Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Same(expand,pane.GetVisualDescendants().OfType<Button>().Single(button=>Avalonia.Automation.AutomationProperties.GetName(button)=="Collapse Home"));
        Assert.DoesNotContain(pane.GetVisualDescendants().OfType<TextBlock>(),text=>text.Text?.Contains("No subfolders")==true || text.Text?.Contains("Media Root")==true);
        window.Close(); model.Suspend();
    }
    [AvaloniaFact]
    public async Task PermissionSensitiveLocationsWaitForExplicitDemand()
    {
        var service=new FakeBrowser();var provider=new FakeLocations { ProtectedHome=true };
        var model=new BrowserViewModel(service,provider);await model.OpenAsync(service.CatalogId);
        var pane=new BrowserPane(model);var window=new Window { Content=pane,Width=1100,Height=700 };window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.DoesNotContain(provider.Requests,request=>request.Path=="/task");
        var expand=pane.GetVisualDescendants().OfType<Button>().Single(button=>Avalonia.Automation.AutomationProperties.GetName(button)=="Expand Home");
        expand.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Contains(provider.Requests,request=>request.Path=="/task" && request.UserInitiated);
        window.Close();model.Suspend();
    }
    [Fact]
    public async Task DiscoveryNavigationRejectsLateCommitAndExpansionNeverIngests()
    {
        var service = new FakeDiscovery(); var provider = new FakeLocations();
        var model = new BrowserViewModel(service, provider); await model.OpenAsync(service.CatalogId);
        await model.ExpandAsync(model.Roots.Single(node => node.Name == "Home"));
        Assert.Empty(service.Discoveries);
        var first = model.GoPathAsync("/task/one"); var second = model.GoPathAsync("/task/two");
        var selected = Asset(service.RootId, "two.jpg");
        service.Discoveries["/task/two"].SetResult(new(service.RootId,"two",false,MediaFolderEnumerationStatus.Succeeded,[],[selected]));
        await second;
        service.Discoveries["/task/one"].SetResult(new(service.RootId,"one",false,MediaFolderEnumerationStatus.Succeeded,[],[Asset(service.RootId,"stale.jpg")]));
        await first;
        Assert.Equal("/task/two", model.Location!.AbsolutePath); Assert.Equal(selected, Assert.Single(model.Assets));
        service.Source.SetResult(true); await model.SelectAsync(selected.AssetId);
        var refresh = model.RefreshAsync();
        service.Discoveries["/task/two"].SetResult(new(service.RootId,"two",false,MediaFolderEnumerationStatus.Succeeded,[],[selected]));
        await refresh; Assert.Equal(selected.AssetId, model.SelectedAssetId);
        var back = model.BackAsync(); service.Discoveries["/task/one"].SetResult(new(service.RootId,"one",false,MediaFolderEnumerationStatus.Succeeded,[],[])); await back;
        Assert.Equal("/task/one", model.Location!.AbsolutePath); Assert.True(model.CanGoForward);
        var forward = model.ForwardAsync(); service.Discoveries["/task/two"].SetResult(new(service.RootId,"two",false,MediaFolderEnumerationStatus.Succeeded,[],[selected])); await forward;
        Assert.Equal("/task/two",model.Location!.AbsolutePath);
    }
    [Fact]
    public async Task PartialScanDiagnosticsStayConciseAndNeverClaimCompleteReconciliation()
    {
        var service = new FakeDiscovery(); var model = new BrowserViewModel(service);
        await model.OpenAsync(service.CatalogId);
        var loading = model.GoPathAsync("/task/partial");
        var details = string.Join(" ", Enumerable.Range(0, 100).Select(i => $"internal-{i}: Filesystem links were skipped; Catalog refresh refused."));
        service.Discoveries["/task/partial"].SetResult(new(service.RootId, "partial", true,
            MediaFolderEnumerationStatus.Succeeded, [], [Asset(service.RootId, "safe.jpg")], details));
        await loading;
        Assert.StartsWith("Scan incomplete", model.Message);
        Assert.Contains("Catalog records are preserved", model.Message);
        Assert.DoesNotContain("internal-", model.Message);
        Assert.True(model.Message.Length < 200);
        Assert.Single(model.Assets);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task SuccessfulBrowseAndRefreshLeaveStatusQuiet(bool recursive, bool empty)
    {
        var service = new FakeDiscovery(); var model = new BrowserViewModel(service);
        await model.OpenAsync(service.CatalogId);
        var loading = model.GoPathAsync("/task/ordinary");
        Assert.True(model.IsLoading); Assert.False(string.IsNullOrWhiteSpace(model.Message));
        var content = new BrowserFolderSnapshot(service.RootId, "ordinary", recursive,
            MediaFolderEnumerationStatus.Succeeded, [], empty ? [] : [Asset(service.RootId, "photo.jpg")]);
        service.Discoveries["/task/ordinary"].SetResult(content);
        await loading;
        Assert.False(model.IsLoading); Assert.Empty(model.Message);
        var refresh = model.RefreshAsync();
        service.Discoveries["/task/ordinary"].SetResult(content);
        await refresh;
        Assert.Empty(model.Message);
    }

    [Fact]
    public async Task QuietSuccessDoesNotSuppressCancellationOrRefusal()
    {
        var service = new FakeDiscovery(); var model = new BrowserViewModel(service);
        await model.OpenAsync(service.CatalogId);
        var loading = model.GoPathAsync("/task/canceled");
        model.CancelNavigation(); await loading;
        Assert.Contains("Loading stopped", model.Message);
        var refused = model.GoPathAsync("/task/denied");
        service.Discoveries["/task/denied"].SetResult(new(service.RootId, "denied", false,
            MediaFolderEnumerationStatus.AccessDenied, [], []));
        await refused;
        Assert.Contains("permissions", model.Message);
        Assert.False(model.IsLoading);
    }

    private sealed class FakeDiscovery : FakeBrowser, IBrowserDiscoverySession
    {
        public Dictionary<string,TaskCompletionSource<BrowserFolderSnapshot>> Discoveries = [];
        public Task<BrowserFolderSnapshot> DiscoverBrowserFolderAsync(string? path, Guid rootId, string folder, CancellationToken token = default)
        {
            var pending = new TaskCompletionSource<BrowserFolderSnapshot>(); Discoveries[path!] = pending; return pending.Task;
        }
        public Task SetBrowserRecursiveAsync(Guid rootId, string folder, bool enabled, CancellationToken token = default) => Task.CompletedTask;
    }
    private sealed class FakeLocations : IBrowserLocationProvider
    {
        public bool Delayed, ProtectedHome;
        public List<(string Path,bool UserInitiated)> Requests = [];
        public MediaFolderEnumerationStatus Status=MediaFolderEnumerationStatus.Succeeded;
        public Task<IReadOnlyList<BrowserFileLocation>> GetLocationsAsync(CancellationToken token)=>Task.FromResult<IReadOnlyList<BrowserFileLocation>>([new("/task","Home",CanPrefetch:!ProtectedHome)]);
        public async Task<BrowserLocationListing> GetFoldersAsync(string path,CancellationToken token,bool userInitiated=false)
        {
            Requests.Add((path,userInitiated));
            if (Delayed) await Task.Delay(1,token);
            return new(Status,path=="/task" ? [new("/task/Media","Media")] : []);
        }
    }
    private static MediaAsset Asset(Guid root,string path)=>new(Guid.NewGuid(),root,path,path.ToUpperInvariant(),"image",68,DateTime.UtcNow.Ticks,null,MediaAssetSourceStatus.Available,DateTimeOffset.UtcNow,DateTimeOffset.UtcNow,null);
    private class FakeBrowser : IBrowserFolderSession
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
