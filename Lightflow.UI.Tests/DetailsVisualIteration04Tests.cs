using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Lightflow.UI;
using Xunit;
namespace Lightflow.UI.Tests;
public sealed partial class BrowserTests
{
    [AvaloniaFact]
    public void PointerReorderPreservesPointerFocusAppearanceAndKeyboardFocusRemainsDistinct()
    {
        var pane=new BrowserPane(new BrowserViewModel(new FakeBrowser()));var window=new Window{Content=pane,Width=1440,Height=800};LightflowTheme.Apply(window);window.Show();
        void Layout(){window.UpdateLayout();Dispatcher.UIThread.RunJobs();window.UpdateLayout();}
        Button Header(string id)=>pane.GetVisualDescendants().OfType<Button>().Single(b=>Equals(b.Tag,id));
        Border Chrome(string id)=>Header(id).GetVisualDescendants().OfType<Border>().Single(b=>b.Name=="Chrome");
        try
        {
            pane.SetPresentation(BrowserLayoutMode.Details);Layout();CaptureColumnEvidence(window,"i04-before-drag.png");
            for(int pass=0;pass<2;pass++)
            {
                var source=Header("name");var destination=Header(pass==0 ? "capture-date" : "preview");
                var from=source.TranslatePoint(new Point(12,12),window)!.Value;var to=destination.TranslatePoint(new Point(12,12),window)!.Value;
                window.MouseMove(from);window.MouseDown(from,MouseButton.Left);window.MouseMove(to);window.MouseUp(to,MouseButton.Left);Layout();
                Assert.True(Header("name").IsFocused);CaptureColumnEvidence(window,"i04-after-pointer-drag.png");
                Assert.Equal(new Thickness(0,0,1,1),Chrome("name").BorderThickness);
                Assert.Null(Header("name").FocusAdorner);
            }
            Header("preview").Focus(NavigationMethod.Tab);
            Header("name").Focus(NavigationMethod.Tab);Layout();CaptureColumnEvidence(window,"i04-keyboard-focus.png");
            Assert.Equal(new Thickness(2),Chrome("name").BorderThickness);
            Assert.Equal(Color.Parse("#FF9A66"),((ISolidColorBrush)Chrome("name").BorderBrush!).Color);
            pane.SetPresentation(BrowserLayoutMode.Grid);Layout();pane.SetPresentation(BrowserLayoutMode.Details);Layout();
            Assert.Equal(new Thickness(0,0,1,1),Chrome("name").BorderThickness);
        }
        finally{window.Close();}
    }
}

public sealed partial class BrowserTests
{
    [AvaloniaTheory]
    [InlineData(false,154,108,38,7)]
    [InlineData(false,154,78,38,7)]
    [InlineData(false,200,108,38,7)]
    [InlineData(true,98,54,30,0)]
    [InlineData(true,20,54,20,0)]
    public void PreviewActivityUsesWindowsFixedTrackGeometryAndContainsNarrowCells(bool details,double width,double height,double expectedWidth,double inset)
    {
        var model=new BrowserViewModel(new FakeBrowser());var cell=new BrowserThumbnailCell(model,Guid.NewGuid(),details){Width=width,Height=height};
        var window=new Window{Content=cell,Width=400,Height=240};LightflowTheme.Apply(window);window.Show();window.UpdateLayout();
        try
        {
            var activity=cell.GetVisualDescendants().OfType<BrowserPreviewActivity>().Single();activity.SetGenerating(true);window.UpdateLayout();window.UpdateLayout();
            Assert.Equal(4,activity.Bounds.Height);Assert.Equal(expectedWidth,activity.Bounds.Width);
            Assert.Equal((width-expectedWidth)/2,activity.TranslatePoint(default,cell)!.Value.X,1);
            Assert.Equal(height-inset-4,activity.TranslatePoint(default,cell)!.Value.Y,1);
            Assert.Equal(new CornerRadius(6),activity.CornerRadius);Assert.True(activity.ClipToBounds);Assert.False(activity.IsHitTestVisible);
            var pulse=Assert.IsType<Border>(activity.Child);Assert.Equal(60,pulse.Width);Assert.Contains("generating",pulse.Classes);
            Assert.IsType<LinearGradientBrush>(pulse.Background);
            CaptureColumnEvidence(window,$"i04-activity-{details}-{width}-{height}.png");
            activity.SetGenerating(false);Assert.False(activity.IsVisible);Assert.DoesNotContain("generating",pulse.Classes);
            activity.SetGenerating(true);window.Content=null;Assert.False(activity.IsVisible);
        }
        finally{window.Close();}
    }
    [AvaloniaTheory]
    [InlineData(BrowserCardMode.Preview,false)]
    [InlineData(BrowserCardMode.Info,false)]
    [InlineData(BrowserCardMode.Hybrid,false)]
    [InlineData(BrowserCardMode.Preview,true)]
    public async Task ActivePreviewTrackMatchesReferenceAcrossRealGridModesAndDetails(BrowserCardMode mode,bool details)
    {
        var service=new ThumbnailBrowser();var model=new BrowserViewModel(service);await model.OpenAsync(service.CatalogId);
        var load=model.NavigateAsync(model.Roots[0]);var asset=Asset(service.RootId,"generated.jpg");service.Folders[""].SetResult(new(service.RootId,"",false,LightflowStudio.MediaFolderEnumerationStatus.Succeeded,[],[asset]));await load;
        service.Read.SetResult(new Dictionary<Guid,LightflowStudio.BrowserThumbnail>{{asset.AssetId,new(asset.AssetId,null,"Loading")}});
        var pane=new BrowserPane(model);var window=new Window{Content=pane,Width=1440,Height=800};LightflowTheme.Apply(window);window.Show();
        void Layout(){window.UpdateLayout();Dispatcher.UIThread.RunJobs();window.UpdateLayout();}
        try
        {
            pane.SetCardMode(mode);pane.SetPresentation(details ? BrowserLayoutMode.Details : BrowserLayoutMode.Grid);Layout();await model.SetThumbnailViewportAsync([asset.AssetId],[]);Layout();
            var activity=pane.GetVisualDescendants().OfType<BrowserPreviewActivity>().Single();Assert.False(activity.IsVisible);
            using(var generation=service.Activity.Begin(asset.AssetId))
            {
                Layout();Assert.True(activity.IsVisible);Assert.Equal(details?30:38,activity.Bounds.Width);Assert.Equal(4,activity.Bounds.Height);
                Assert.Equal(details?0:7,activity.Margin.Bottom);
                CaptureColumnEvidence(window,$"i04-active-{mode}-{details}.png");
                window.Width=1120;Layout();activity=pane.GetVisualDescendants().OfType<BrowserPreviewActivity>().Single();Assert.True(activity.IsVisible);Assert.Equal(details?30:38,activity.Bounds.Width);
            }
            Layout();Assert.False(activity.IsVisible);
            using(var generation=service.Activity.Begin(asset.AssetId)){Layout();Assert.True(activity.IsVisible);model.ClearThumbnailDemand();Layout();Assert.False(activity.IsVisible);}
        }
        finally{window.Close();model.Suspend();}
    }

}
