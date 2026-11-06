using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Lightflow.UI;
using LightflowStudio;
using Xunit;
namespace Lightflow.UI.Tests;
public sealed partial class BrowserTests
{
    [AvaloniaTheory]
    [InlineData("Cached",true,false)]
    [InlineData("Cached",false,true)]
    [InlineData("Unavailable",true,true)]
    [InlineData("Unsupported",false,true)]
    [InlineData("Preview failed",false,true)]
    [InlineData("Loading",false,true)]
    public async Task ThumbnailCellHidesCacheLifecycleButKeepsMediaStates(string state,bool hasImage,bool visibleState)
    {
        var root=Path.Combine(Path.GetTempPath(),"lf036-label-"+Guid.NewGuid());Directory.CreateDirectory(root);var path=Path.Combine(root,"preview.jpg");
        try
        {
            using(var bitmap=new SkiaSharp.SKBitmap(64,48)){bitmap.Erase(SkiaSharp.SKColors.Coral);using var image=SkiaSharp.SKImage.FromBitmap(bitmap);using var data=image.Encode(SkiaSharp.SKEncodedImageFormat.Jpeg,85);File.WriteAllBytes(path,data.ToArray());}
            var service=new ThumbnailBrowser();var model=new BrowserViewModel(service);await model.OpenAsync(service.CatalogId);var asset=Asset(service.RootId,"one.jpg");
            var navigation=model.NavigateAsync(model.Roots[0]);service.Folders[""].SetResult(new(service.RootId,"",false,MediaFolderEnumerationStatus.Succeeded,[],[asset]));await navigation;
            var cell=new BrowserThumbnailCell(model,asset.AssetId);var window=new Window{Content=cell,Width=300,Height=200};window.Show();
            try
            {
                service.Read.SetResult(new Dictionary<Guid,BrowserThumbnail>{{asset.AssetId,new(asset.AssetId,hasImage?path:null,state,Diagnostic:state=="Cached"?"Cached preview · source changes are being checked.":state)}});
                await model.SetThumbnailViewportAsync([asset.AssetId],[]);window.UpdateLayout();
                var label=Assert.Single(cell.GetVisualDescendants().OfType<TextBlock>());Assert.Equal(visibleState,label.IsVisible);
                Assert.DoesNotContain("Cached",label.Text??"");Assert.Equal(state=="Cached"?"Preview":state,AutomationProperties.GetName(cell));Assert.DoesNotContain("Cached",ToolTip.GetTip(cell)?.ToString()??"");
                Assert.Equal(hasImage,Assert.Single(cell.GetVisualDescendants().OfType<Image>()).Source is not null);
                if(visibleState)Assert.Equal(state=="Cached"?"Loading":state,label.Text);
            }
            finally{window.Close();model.Suspend();}
        }
        finally{Directory.Delete(root,true);}
    }
}
