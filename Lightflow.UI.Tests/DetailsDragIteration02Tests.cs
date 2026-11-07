using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Lightflow.Desktop.MacOS;
using Lightflow.UI;
using LightflowStudio;
using Xunit;
namespace Lightflow.UI.Tests;
public sealed partial class BrowserTests
{
    [AvaloniaTheory]
    [InlineData("name","media-type",true,"media-type,name,camera,file-size")]
    [InlineData("name","file-size",true,"media-type,camera,file-size,name")]
    [InlineData("file-size","name",false,"file-size,name,media-type,camera")]
    [InlineData("camera","name",false,"camera,name,media-type,file-size")]
    [InlineData("media-type","file-size",true,"name,camera,file-size,media-type")]
    public async Task PointerDragKeepsFieldWidthDataAndVisualAlignmentAcrossLifecycle(string source,string target,bool after,string expectedOrder)
    {
        var service=new FakeBrowser();var model=new BrowserViewModel(service);await model.OpenAsync(service.CatalogId);
        var load=model.NavigateAsync(model.Roots[0]);var assets=Enumerable.Range(0,1000).Select(i=>Asset(service.RootId,$"take-{i:D4}.jpg") with {FileSizeBytes=3145728}).ToArray();
        service.Folders[""].SetResult(new(service.RootId,"",false,MediaFolderEnumerationStatus.Succeeded,[],assets));await load;service.Source.SetResult(true);
        var root=Path.Combine(Path.GetTempPath(),"lf040-i02-columns-"+Guid.NewGuid().ToString("N"));
        var pane=new BrowserPane(model);pane.LayoutChanged+=layout=>BrowserLayoutStore.Save(root,layout);
        var window=new Window {Content=pane,Width=1440,Height=800};LightflowTheme.Apply(window);window.Show();
        void Layout(){window.UpdateLayout();Dispatcher.UIThread.RunJobs();window.UpdateLayout();}
        Button Header(string id)=>pane.GetVisualDescendants().OfType<Button>().Single(b=>Equals(b.Tag,id));
        try
        {
            pane.SetPresentation(BrowserLayoutMode.Details);
            var columns=BrowserDetailsLayout.Normalize([new("name",215,true),new("rating",88,false),new("media-type",109,true),new("camera",163,true),new("file-size",123,true)])
                .Select(c=>c with {Visible=c.Id is "name" or "media-type" or "camera" or "file-size"}).ToArray();
            pane.ConfigureDetailsColumns(columns);Layout();await model.SelectGridAsync(assets[2].AssetId);await model.SelectGridAsync(assets[4].AssetId,range:true);
            var selection=model.SelectedAssetKeys.Order().ToArray();
            var moving=Header(source);var destination=Header(target);
            var origin=moving.TranslatePoint(new Point(12,12),window)!.Value;
            var drop=destination.TranslatePoint(new Point(after ? destination.Bounds.Width-10 : 10,12),window)!.Value;
            window.MouseMove(origin);window.MouseDown(origin,MouseButton.Left);window.MouseMove(drop);Layout();
            var marker=pane.GetVisualDescendants().OfType<Border>().Single(b=>AutomationProperties.GetAutomationId(b)=="BrowserColumnDropIndicator");
            Assert.True(marker.IsVisible);Assert.Contains("Move",AutomationProperties.GetName(marker));
            window.MouseUp(drop,MouseButton.Left);Layout();Assert.False(marker.IsVisible);
            Assert.True(Header(source).IsFocused);Assert.Equal(selection,model.SelectedAssetKeys.Order());
            Assert.Equal(expectedOrder.Split(','),pane.DetailsColumns.Where(c=>c.Visible).Select(c=>c.Id));
            Assert.Equal("rating",pane.DetailsColumns[1].Id);Assert.False(pane.DetailsColumns[1].Visible);
            foreach(var c in pane.DetailsColumns)Assert.Equal(columns.Single(old=>old.Id==c.Id).Width,c.Width);
            void Associations()
            {
                var expected=pane.DetailsColumns.Where(c=>c.Visible).ToArray();
                foreach(var row in pane.GetVisualDescendants().OfType<BrowserDetailsRow>())
                {
                    var cells=row.GetVisualDescendants().OfType<TextBlock>().Where(t=>t.Tag is string).ToArray();
                    Assert.Equal(4,cells.Length);
                    Assert.Equal(expected.Select(c=>c.Id),cells.Select(c=>(string)c.Tag!));
                    for(var i=0;i<expected.Length;i++)
                    {
                        var id=expected[i].Id;var cell=Assert.Single(cells,c=>Equals(c.Tag,id));
                        Assert.Equal(i,Grid.GetColumn(cell));Assert.Equal("BrowserDetailsCell-"+id,AutomationProperties.GetAutomationId(cell));
                        Assert.Equal(id switch {"name"=>assets.Single(a=>a.AssetId==row.AssetId).RelativePath,"media-type"=>"Image","file-size"=>"3 MB",_=>""},cell.Text);
                        var headerX=Header(id).TranslatePoint(default,window)!.Value.X;
                        Assert.True(Math.Abs(cell.TranslatePoint(default,window)!.Value.X-headerX-8)<=1,$"{id}: cell X={cell.TranslatePoint(default,window)!.Value.X}, header X={headerX}; body/header offsets={pane.GetVisualDescendants().OfType<ListBox>().Single().GetVisualDescendants().OfType<ScrollViewer>().First().Offset.X}/{pane.GetVisualDescendants().OfType<ScrollViewer>().Single(s=>s.Content is Grid g && AutomationProperties.GetName(g)?.StartsWith("Details columns")==true).Offset.X}");
                    }
                }
            }
            Associations();var list=pane.GetVisualDescendants().OfType<ListBox>().Single();list.ScrollIntoView(600);Layout();Associations();list.ScrollIntoView(0);Layout();Associations();
            pane.SetPresentation(BrowserLayoutMode.Grid);Layout();pane.SetPresentation(BrowserLayoutMode.Details);Layout();Associations();
            var refresh=model.RefreshAsync();service.Folders[""].SetResult(new(service.RootId,"",false,MediaFolderEnumerationStatus.Succeeded,[],assets.ToArray()));await refresh;Layout();Associations();
            var navigate=model.NavigateAsync(new(service.RootId,"next","next"));service.Folders["next"].SetResult(new(service.RootId,"next",false,MediaFolderEnumerationStatus.Succeeded,[],assets.Take(5).ToArray()));await navigate;Layout();Associations();
            window.Content=null;var restored=BrowserLayoutStore.Load(root);var reopened=new BrowserPane(model,restored);window.Content=reopened;pane=reopened;Layout();Associations();
            Assert.Equal(expectedOrder.Split(','),pane.DetailsColumns.Where(c=>c.Visible).Select(c=>c.Id));
        }
        finally {window.Close();model.Suspend();if(Directory.Exists(root))Directory.Delete(root,true);}
    }
    [AvaloniaFact]
    public void DetachedRetainedRowsReadTheCurrentColumnStateWhenReattached()
    {
        var model=new BrowserViewModel(new FakeBrowser());var state=new BrowserDetailsColumnState();
        state.Set(BrowserDetailsLayout.Normalize([new("name",230,true),new("media-type",95,true)]).Select(c=>c with {Visible=c.Id is "name" or "media-type"}).ToArray());
        var row=new BrowserDetailsRow(model,Asset(Guid.NewGuid(),"one.jpg"),state,(_,_)=>Task.CompletedTask);
        var window=new Window {Content=row};LightflowTheme.Apply(window);window.Show();window.UpdateLayout();
        try
        {
            var cell=row.GetVisualDescendants().OfType<TextBlock>().Single(t=>Equals(t.Tag,"name"));window.Content=null;
            state.Set(BrowserColumnReorder.Move(state.Columns,"media-type",0));window.Content=row;window.UpdateLayout();
            Assert.Same(cell,row.GetVisualDescendants().OfType<TextBlock>().Single(t=>Equals(t.Tag,"name")));Assert.Equal(1,Grid.GetColumn(cell));
            Assert.Equal(0,Grid.GetColumn(row.GetVisualDescendants().OfType<TextBlock>().Single(t=>Equals(t.Tag,"media-type"))));
        }
        finally {window.Close();}
    }
    [AvaloniaFact]
    public void DragIndicatorTracksScrolledHiddenColumnBoundariesAndCancelsWithoutChangingOrder()
    {
        var pane=new BrowserPane(new BrowserViewModel(new FakeBrowser()));var window=new Window {Content=pane,Width=1120,Height=720};LightflowTheme.Apply(window);window.Show();
        void Layout(){window.UpdateLayout();Dispatcher.UIThread.RunJobs();window.UpdateLayout();}
        Button Header(string id)=>pane.GetVisualDescendants().OfType<Button>().Single(b=>Equals(b.Tag,id));
        try
        {
            pane.SetPresentation(BrowserLayoutMode.Details);pane.ConfigureDetailsColumns(pane.DetailsColumns.Select(c=>c with {Visible=c.Id!="rating"}).ToArray());Layout();
            var scroll=pane.GetVisualDescendants().OfType<ScrollViewer>().Single(s=>s.Content is Grid g && AutomationProperties.GetName(g)?.StartsWith("Details columns") ==true);
            var body=pane.GetVisualDescendants().OfType<ListBox>().Single().GetVisualDescendants().OfType<ScrollViewer>().First();
            body.Offset=new Vector(200,0);Layout();
            var order=pane.DetailsColumns.ToArray();var name=Header("name");var capture=Header("capture-date");
            var from=name.TranslatePoint(new Point(100,12),window)!.Value;var to=capture.TranslatePoint(new Point(2,12),window)!.Value;
            window.MouseMove(from);window.MouseDown(from,MouseButton.Left);window.MouseMove(to);Layout();
            var marker=pane.GetVisualDescendants().OfType<Border>().Single(b=>AutomationProperties.GetAutomationId(b)=="BrowserColumnDropIndicator");
            Assert.True(marker.IsVisible);Assert.True(Math.Abs(marker.TranslatePoint(default,window)!.Value.X-capture.TranslatePoint(default,window)!.Value.X)<=2,$"marker X={marker.TranslatePoint(default,window)!.Value.X}, capture X={capture.TranslatePoint(default,window)!.Value.X}, pointer X={to.X}, offset={scroll.Offset.X}, margin={marker.Margin.Left}, {AutomationProperties.GetName(marker)}");
            window.MouseMove(new Point(to.X,10));Layout();Assert.Equal("Column cannot be dropped here",AutomationProperties.GetName(marker));
            name.RaiseEvent(new KeyEventArgs {RoutedEvent=InputElement.KeyDownEvent,Key=Key.Escape});Layout();Assert.False(marker.IsVisible);
            window.MouseUp(to,MouseButton.Left);Assert.Equal(order,pane.DetailsColumns);
        }
        finally {window.Close();}
    }
}
