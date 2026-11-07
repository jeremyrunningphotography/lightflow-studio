using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Lightflow.Application;
using Lightflow.Desktop.MacOS;
using Lightflow.UI;
using LightflowStudio;
using Xunit;
namespace Lightflow.UI.Tests;
public sealed partial class BrowserTests
{
    [AvaloniaFact]
    public async Task DetailsSwitchesWithoutRequeryAndKeepsMultiselectionKeyboardAndBoundedRealization()
    {
        var service=new FakeBrowser();var model=new BrowserViewModel(service);await model.OpenAsync(service.CatalogId);
        var load=model.NavigateAsync(model.Roots[0]);var assets=Enumerable.Range(0,10000).Select(i=>Asset(service.RootId,$"{i:D5}.jpg")).ToArray();
        service.Folders[""].SetResult(new(service.RootId,"",false,MediaFolderEnumerationStatus.Succeeded,[],assets));await load;service.Source.SetResult(true);
        var pane=new BrowserPane(model);var window=new Window {Content=pane,Width=1440,Height=800};LightflowTheme.Apply(window);window.Show();
        void Layout(){window.UpdateLayout();Dispatcher.UIThread.RunJobs();window.UpdateLayout();}
        Layout();await model.SelectGridAsync(assets[4].AssetId);await model.SelectGridAsync(assets[7].AssetId,range:true);
        var selection=model.SelectedAssetKeys.ToArray();var snapshot=model.Assets;
        foreach(var mode in Enum.GetValues<BrowserCardMode>())
        {
            pane.SetCardMode(mode);pane.SetPresentation(BrowserLayoutMode.Details);Layout();
            Assert.Same(snapshot,model.Assets);Assert.Equal(selection.Order(),model.SelectedAssetKeys.Order());
            var rows=pane.GetVisualDescendants().OfType<BrowserDetailsRow>().ToArray();Assert.InRange(rows.Length,1,32);
            Assert.All(rows,r=>Assert.Equal(64,r.Height));Assert.Empty(pane.GetVisualDescendants().OfType<BrowserGridCard>());
            pane.SetPresentation(BrowserLayoutMode.Grid);Layout();Assert.Equal(mode,pane.CardMode);
        }
        pane.SetPresentation(BrowserLayoutMode.Details);Layout();var list=pane.GetVisualDescendants().OfType<ListBox>().Single();
        list.RaiseEvent(new KeyEventArgs {RoutedEvent=InputElement.KeyDownEvent,Key=Key.End});Layout();Assert.Equal(assets[^1].AssetId,model.SelectedAssetId);
        list.RaiseEvent(new KeyEventArgs {RoutedEvent=InputElement.KeyDownEvent,Key=Key.Up,KeyModifiers=KeyModifiers.Shift});Layout();Assert.Equal(2,model.SelectedAssetKeys.Count);
        Assert.InRange(pane.GetVisualDescendants().OfType<BrowserDetailsRow>().Count(),1,32);
        pane.ToggleRightPanel();window.Width=1120;Layout();Assert.Same(snapshot,model.Assets);
        var viewer=list.GetVisualDescendants().OfType<ScrollViewer>().First();
        Assert.True(viewer.Extent.Width>viewer.Viewport.Width);
        viewer.Offset=new Vector(300,viewer.Offset.Y);Layout();
        Assert.True(viewer.Offset.X>0);
        var resized=pane.DetailsColumns.Select(c=>c.Id=="name" ? c with {Width=377} : c).ToArray();
        pane.ConfigureDetailsColumns(resized);Layout();
        Assert.All(pane.GetVisualDescendants().OfType<BrowserDetailsRow>(),r=>Assert.Equal(resized.Where(c=>c.Visible).Sum(c=>c.Width),r.Width));
        window.Close();model.Suspend();Assert.Equal(0,BrowserThumbnailCell.RetainedBitmaps);
    }
    [AvaloniaFact]
    public void DetailsColumnConfigurationAndPresentationRoundTripThroughExistingProfileStore()
    {
        var root=Path.Combine(Path.GetTempPath(),"lf040-preferences-"+Guid.NewGuid().ToString("N"));
        try
        {
            var model=new BrowserViewModel(new FakeBrowser());var pane=new BrowserPane(model);
            pane.LayoutChanged+=layout=>BrowserLayoutStore.Save(root,layout);
            pane.SetCardMode(BrowserCardMode.Hybrid);pane.SetPresentation(BrowserLayoutMode.Details);
            var configured=BrowserDetailsLayout.Normalize([new("file-size",345,true),new("name",400,true),new("preview",112,false)]);
            pane.ConfigureDetailsColumns(configured);
            var restored=BrowserLayoutStore.Load(root);Assert.Equal(BrowserLayoutMode.Details,restored.Presentation);Assert.Equal(BrowserCardMode.Hybrid,restored.CardMode);
            Assert.Equal(configured,restored.DetailsColumns);
            var reopened=new BrowserPane(new BrowserViewModel(new FakeBrowser()),restored);
            Assert.Equal(BrowserLayoutMode.Details,reopened.Presentation);Assert.Equal(BrowserCardMode.Hybrid,reopened.CardMode);Assert.Equal(configured,reopened.DetailsColumns);
        }
        finally {if(Directory.Exists(root))Directory.Delete(root,true);}
    }

    [AvaloniaFact]
    public void HeaderRightClickAndKeyboardGripKeepColumnConfigurationReachable()
    {
        var pane=new BrowserPane(new BrowserViewModel(new FakeBrowser()));
        var window=new Window {Content=pane,Width=1440,Height=800};LightflowTheme.Apply(window);window.Show();
        pane.SetPresentation(BrowserLayoutMode.Details);window.UpdateLayout();Dispatcher.UIThread.RunJobs();window.UpdateLayout();
        var header=pane.GetVisualDescendants().OfType<Button>().Single(b=>b.Content?.ToString()=="Name");
        var position=header.TranslatePoint(new Point(20,10),window)!.Value;
        window.MouseDown(position,MouseButton.Right);window.MouseUp(position,MouseButton.Right);Dispatcher.UIThread.RunJobs();
        Assert.NotNull(header.ContextMenu);Assert.True(header.ContextMenu!.IsOpen);
        header.ContextMenu.Close();
        var grip=pane.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.Thumb>().Single(t=>Avalonia.Automation.AutomationProperties.GetName(t)=="Resize Name");
        grip.Focus();grip.RaiseEvent(new KeyEventArgs {RoutedEvent=InputElement.KeyDownEvent,Key=Key.Right});
        Assert.Equal(248,pane.DetailsColumns.Single(c=>c.Id=="name").Width);
        window.UpdateLayout();
        var resize=grip.TranslatePoint(new Point(3,12),window)!.Value;
        window.MouseMove(resize);window.MouseDown(resize,MouseButton.Left);window.MouseMove(resize+new Vector(48,0));window.MouseUp(resize+new Vector(48,0),MouseButton.Left);window.UpdateLayout();
        Assert.Equal(296,pane.DetailsColumns.Single(c=>c.Id=="name").Width);
        position=header.TranslatePoint(new Point(20,10),window)!.Value;
        var preview=pane.GetVisualDescendants().OfType<Button>().Single(b=>b.Content?.ToString()=="Preview");
        var destination=preview.TranslatePoint(new Point(20,10),window)!.Value;
        window.MouseMove(position);window.MouseDown(position,MouseButton.Left);window.MouseMove(destination);window.MouseUp(destination,MouseButton.Left);window.UpdateLayout();
        Assert.Equal("name",pane.DetailsColumns.First(c=>c.Visible).Id);
        window.Close();
    }
    [Fact]
    public void InvalidColumnsCannotHideEverythingOrProduceUnboundedWidths()
    {
        var normalized=BrowserDetailsLayout.Normalize([new("unknown",100,true),new("name",double.NaN,false),new("name",9999,true),new("preview",99999,false)]);
        Assert.Equal(18,normalized.Count);Assert.Equal(18,normalized.Select(c=>c.Id).Distinct().Count());Assert.Equal(240,normalized.Single(c=>c.Id=="name").Width);Assert.Equal(1200,normalized.Single(c=>c.Id=="preview").Width);
        Assert.Contains(BrowserDetailsLayout.Normalize(normalized.Select(c=>c with {Visible=false}).ToArray()),c=>c.Visible && c.Id=="name");
    }
    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task RestoreReadsOnlyExistingCatalogScopeAndNeverDiscoversOrGenerates(bool unavailable)
    {
        var service=new CachedBrowser();var assets=new[]{Asset(service.RootId,"sub/a.jpg")};service.Known["sub"]=assets;
        var locations=new FakeLocations {Status=unavailable ? MediaFolderEnumerationStatus.FolderUnavailable : MediaFolderEnumerationStatus.Succeeded};
        var model=new BrowserViewModel(service,locations) {RestoreLocation=new(service.CatalogId,service.RootId,"sub","/task/Media/sub")};
        await model.OpenAsync(service.CatalogId);
        Assert.Empty(service.Folders);Assert.Equal(0,service.Demands);
        if(unavailable) {Assert.Null(model.Location);Assert.Empty(model.Assets);Assert.Contains("unavailable",model.Message);}
        else {Assert.Equal("sub",model.Location!.RelativeFolder);Assert.Same(assets,model.Assets);Assert.True(model.IsProvisional);Assert.Contains("Refresh",model.Message);}
        Assert.All(locations.Requests,r=>Assert.False(r.UserInitiated));model.Suspend();
    }

    [AvaloniaFact]
    public async Task StalledNetworkRestorationIsBoundedAndFallsBackWithoutDiscovery()
    {
        var service=new CachedBrowser();var model=new BrowserViewModel(service,new StalledRestoreLocations())
            {RestoreLocation=new(service.CatalogId,service.RootId,"","/task/Media")};
        await model.OpenAsync(service.CatalogId).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(model.IsLoading);Assert.Null(model.Location);Assert.Empty(model.Assets);Assert.Empty(service.Folders);Assert.Contains("unavailable",model.Message);
        model.Suspend();
    }
    private sealed class StalledRestoreLocations:IBrowserLocationProvider
    {
        public Task<IReadOnlyList<BrowserFileLocation>> GetLocationsAsync(CancellationToken token)=>Task.FromResult<IReadOnlyList<BrowserFileLocation>>([]);
        public Task<BrowserLocationListing> GetFoldersAsync(string path,CancellationToken token,bool userInitiated=false)=>new TaskCompletionSource<BrowserLocationListing>().Task;
    }
    [AvaloniaFact]
    public async Task CatalogMismatchAndRemappingCannotRestoreAnotherLocation()
    {
        var service=new CachedBrowser();var locations=new FakeLocations();
        var model=new BrowserViewModel(service,locations) {RestoreLocation=new(Guid.NewGuid(),service.RootId,"","/task/Media")};
        await model.OpenAsync(service.CatalogId);Assert.Null(model.Location);Assert.Empty(locations.Requests);
        model.RestoreLocation=new(service.CatalogId,service.RootId,"","/old/mapping");await model.OpenAsync(service.CatalogId);
        Assert.Null(model.Location);Assert.Empty(locations.Requests);Assert.Empty(service.Folders);model.Suspend();
    }
}
