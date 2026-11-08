using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Lightflow.UI;
using LightflowStudio;
using Xunit;
namespace Lightflow.UI.Tests;
public sealed partial class BrowserTests
{
    [AvaloniaFact]
    public async Task RightmostScrollAndKeyboardHeaderFocusMoveHeadersAndCellsTogether()
    {
        var service=new FakeBrowser();var model=new BrowserViewModel(service);await model.OpenAsync(service.CatalogId);
        var loading=model.NavigateAsync(model.Roots[0]);var asset=Asset(service.RootId,"one.jpg");service.Folders[""].SetResult(new(service.RootId,"",false,MediaFolderEnumerationStatus.Succeeded,[],[asset]));await loading;
        var pane=new BrowserPane(model,new BrowserLayout(RightOpen:true));var window=new Window {Content=pane,Width=1120,Height=720};LightflowTheme.Apply(window);window.Show();
        void Layout(){window.UpdateLayout();Dispatcher.UIThread.RunJobs();window.UpdateLayout();}
        try
        {
            pane.SetPresentation(BrowserLayoutMode.Details);Layout();
            var body=pane.GetVisualDescendants().OfType<ListBox>().Single().GetVisualDescendants().OfType<ScrollViewer>().First();body.Offset=new Vector(100000,0);Layout();
            Assert.True(body.Offset.X>0);
            void Aligned()
            {
                var row=Assert.Single(pane.GetVisualDescendants().OfType<BrowserDetailsRow>());
                foreach(var cell in row.GetVisualDescendants().OfType<Control>().Where(c=>c.Tag is string))
                {
                    var header=pane.GetVisualDescendants().OfType<Button>().Single(b=>Equals(b.Tag,cell.Tag));
                    Assert.InRange(Math.Abs(cell.TranslatePoint(default,window)!.Value.X-header.TranslatePoint(default,window)!.Value.X-8),0,1);
                }
            }
            Aligned();var name=pane.GetVisualDescendants().OfType<Button>().Single(b=>Equals(b.Tag,"name"));name.Focus(NavigationMethod.Directional);Layout();Aligned();
            var row=pane.GetVisualDescendants().OfType<BrowserDetailsRow>().Single();row.Focus();var before=model.SelectedAssetKeys.ToArray();
            pane.ConfigureDetailsColumns(BrowserColumnReorder.Move(pane.DetailsColumns,"name",0));Layout();Assert.True(row.IsFocused);Assert.Equal(before,model.SelectedAssetKeys);
        }
        finally {window.Close();model.Suspend();}
    }
    [AvaloniaFact]
    public async Task AvailableVideoMetadataAndMissingFieldsKeepTheirIdentityAfterReordering()
    {
        var service=new CachedBrowser();var asset=Asset(service.RootId,"take.mp4") with {MediaType="video"};service.Known[""]=[asset];
        service.Cached[asset.AssetId]=new(asset.AssetId,null,"Ready",Metadata:new(DerivedMediaKind.Video,"mp4",3,null,asset.FileSizeBytes,null,new("h264",null,1920,1080,29.97,null,null,null,null,null),null,null));
        var model=new BrowserViewModel(service);await model.OpenAsync(service.CatalogId);var load=model.NavigateAsync(model.Roots[0]);service.Folders[""].SetResult(new(service.RootId,"",false,MediaFolderEnumerationStatus.Succeeded,[],[asset]));await load;
        var pane=new BrowserPane(model);var window=new Window {Content=pane,Width=1440,Height=800};LightflowTheme.Apply(window);window.Show();
        void Layout(){window.UpdateLayout();Dispatcher.UIThread.RunJobs();window.UpdateLayout();}
        try
        {
            pane.SetPresentation(BrowserLayoutMode.Details);
            pane.ConfigureDetailsColumns(BrowserDetailsLayout.Normalize([new("duration",100,true),new("name",240,true),new("camera",150,true),new("dimensions",120,true),new("frame-rate",100,true)]).Select(c=>c with {Visible=c.Id is "duration" or "name" or "camera" or "dimensions" or "frame-rate"}).ToArray());Layout();
            pane.ConfigureDetailsColumns(BrowserColumnReorder.Move(pane.DetailsColumns,"name",5));Layout();
            var expected=new Dictionary<string,string>{{"duration","00:00:03"},{"name","take.mp4"},{"camera",""},{"dimensions","1920 × 1080"},{"frame-rate","29.97 fps"}};
            foreach(var cell in pane.GetVisualDescendants().OfType<BrowserDetailsRow>().Single().GetVisualDescendants().OfType<TextBlock>().Where(c=>c.Tag is string))Assert.Equal(expected[(string)cell.Tag!],cell.Text);
            model.ClearThumbnailDemand();Layout();
            foreach(var cell in pane.GetVisualDescendants().OfType<BrowserDetailsRow>().Single().GetVisualDescendants().OfType<TextBlock>().Where(c=>c.Tag is string && !Equals(c.Tag,"name")))Assert.Equal("",cell.Text);
        }
        finally {window.Close();model.Suspend();}
    }
    [AvaloniaFact]
    public async Task ReorderingRetainsTheSamePreviewBitmapAndFocusedRow()
    {
        var path=Path.Combine(Path.GetTempPath(),"lf040-i02-preview-"+Guid.NewGuid().ToString("N")+".jpg");
        using(var bitmap=new SkiaSharp.SKBitmap(64,48))
        {bitmap.Erase(SkiaSharp.SKColors.Coral);using var image=SkiaSharp.SKImage.FromBitmap(bitmap);using var encoded=image.Encode(SkiaSharp.SKEncodedImageFormat.Jpeg,85);File.WriteAllBytes(path,encoded.ToArray());}
        var service=new CachedBrowser();var asset=Asset(service.RootId,"one.jpg");service.Known[""]=[asset];service.Cached[asset.AssetId]=new(asset.AssetId,path,"Ready");
        var model=new BrowserViewModel(service);await model.OpenAsync(service.CatalogId);var navigation=model.NavigateAsync(model.Roots[0]);service.Folders[""].SetResult(new(service.RootId,"",false,MediaFolderEnumerationStatus.Succeeded,[],[asset]));await navigation;
        var state=new BrowserDetailsColumnState();state.Set(BrowserDetailsLayout.Normalize(null).Select(c=>c with {Visible=c.Id is "preview" or "name" or "media-type"}).ToArray());
        var row=new BrowserDetailsRow(model,asset,state,(_,_)=>Task.CompletedTask);var window=new Window {Content=row};LightflowTheme.Apply(window);window.Show();window.UpdateLayout();
        try
        {
            var preview=row.GetVisualDescendants().OfType<Image>().Single();var retained=preview.Source;Assert.NotNull(retained);row.Focus();
            state.Set(BrowserColumnReorder.Move(state.Columns,"preview",3));window.UpdateLayout();
            Assert.True(row.IsFocused);Assert.Same(retained,preview.Source);Assert.Equal(1,BrowserThumbnailCell.RetainedBitmaps);
        }
        finally {window.Close();model.Suspend();File.Delete(path);}
        Assert.Equal(0,BrowserThumbnailCell.RetainedBitmaps);
    }

}
