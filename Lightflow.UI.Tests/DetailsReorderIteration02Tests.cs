using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Lightflow.UI;
using LightflowStudio;
using Xunit;
namespace Lightflow.UI.Tests;
public sealed partial class BrowserTests
{
    [AvaloniaFact]
    public async Task ReorderingMovesEveryRealizedAndRecycledCellWithItsHeader()
    {
        var service=new FakeBrowser();var model=new BrowserViewModel(service);await model.OpenAsync(service.CatalogId);
        var loading=model.NavigateAsync(model.Roots[0]);var assets=Enumerable.Range(0,1000).Select(i=>Asset(service.RootId,$"{i:D4}.jpg")).ToArray();
        service.Folders[""].SetResult(new(service.RootId,"",false,MediaFolderEnumerationStatus.Succeeded,[],assets));await loading;service.Source.SetResult(true);
        var pane=new BrowserPane(model);var window=new Window {Content=pane,Width=1440,Height=800};LightflowTheme.Apply(window);window.Show();
        void Layout(){window.UpdateLayout();Dispatcher.UIThread.RunJobs();window.UpdateLayout();}
        try
        {
            pane.SetPresentation(BrowserLayoutMode.Details);Layout();
            var columns=BrowserDetailsLayout.Normalize([new("name",240,true),new("media-type",95,true),new("file-size",95,true)])
                .Select(c=>c with {Visible=c.Id is "name" or "media-type" or "file-size"}).ToArray();
            pane.ConfigureDetailsColumns(columns);Layout();
            var rows=pane.GetVisualDescendants().OfType<BrowserDetailsRow>().ToArray();Assert.NotEmpty(rows);
            foreach(var row in rows)
            {
                var name=row.GetVisualDescendants().OfType<TextBlock>().Single(t=>t.Text?.EndsWith(".jpg")==true);
                Assert.Equal(0,Grid.GetColumn(name));
                Assert.Equal(1,Grid.GetColumn(row.GetVisualDescendants().OfType<TextBlock>().Single(t=>t.Text=="Image")));
            }
            var reordered=new[]{columns[2],columns[0],columns[1]}.Concat(columns.Skip(3)).ToArray();pane.ConfigureDetailsColumns(reordered);Layout();
            void Associations()
            {
                foreach(var row in pane.GetVisualDescendants().OfType<BrowserDetailsRow>())
                {
                    var name=row.GetVisualDescendants().OfType<TextBlock>().Single(t=>t.Text?.EndsWith(".jpg")==true);
                    Assert.Equal(1,Grid.GetColumn(name));
                    Assert.Equal(2,Grid.GetColumn(row.GetVisualDescendants().OfType<TextBlock>().Single(t=>t.Text=="Image")));
                    Assert.Equal(0,Grid.GetColumn(row.GetVisualDescendants().OfType<TextBlock>().Single(t=>t.Text?.EndsWith(" KB")==true)));
                }
            }
            Associations();
            var list=pane.GetVisualDescendants().OfType<ListBox>().Single();list.ScrollIntoView(600);Layout();Associations();
            list.ScrollIntoView(0);Layout();Associations();
            pane.SetPresentation(BrowserLayoutMode.Grid);Layout();pane.SetPresentation(BrowserLayoutMode.Details);Layout();Associations();
        }
        finally {window.Close();model.Suspend();}
    }
}
