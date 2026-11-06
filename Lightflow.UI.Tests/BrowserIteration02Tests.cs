using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Lightflow.UI;
using LightflowStudio;
using Xunit;
namespace Lightflow.UI.Tests;
public sealed partial class BrowserTests
{
    [AvaloniaFact]
    public async Task SelectedChromeRemainsOrangeWithKeyboardFocusAndPointerOverlap()
    {
        var service = new FakeBrowser(); var model = new BrowserViewModel(service); await model.OpenAsync(service.CatalogId);
        var load = model.NavigateAsync(model.Roots[0]); var assets = Enumerable.Range(0, 40).Select(i => Asset(service.RootId, $"{i:D3}.jpg")).ToArray();
        service.Folders[""].SetResult(new(service.RootId, "", false, MediaFolderEnumerationStatus.Succeeded, [], assets)); await load; service.Source.SetResult(true);
        var pane = new BrowserPane(model); var window = new Window { Content = pane, Width = 1440, Height = 800 }; LightflowTheme.Apply(window); window.Show(); window.UpdateLayout();
        BrowserGridCard Card(int index) => pane.GetVisualDescendants().OfType<BrowserGridCard>().First(c => c.AssetId == assets[index].AssetId && c.IsEffectivelyVisible);
        var first = Card(0); var point = first.TranslatePoint(new Point(30,30), window)!.Value;
        window.MouseMove(point); window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); window.UpdateLayout();
        void Orange(BrowserGridCard card)
        {
            var chrome = card.GetVisualDescendants().OfType<Border>().FirstOrDefault(b => b.Name == "Chrome");
            Assert.True(chrome is not null, $"Custom Chrome missing; template descendants={string.Join(",",card.GetVisualDescendants().Select(c => c.GetType().Name+":"+(c as Control)?.Name))}; FocusAdorner={card.FocusAdorner}; style key={card.StyleKey}");
            Assert.True((chrome.BorderBrush as ISolidColorBrush)?.Color == Color.Parse("#FF9A66"), $"Chrome={chrome.BorderBrush}; control={card.BorderBrush}; focused={card.IsFocused}; pseudo={string.Join(',',card.Classes)}");
        }
        Orange(first);
        first.Focus(NavigationMethod.Directional); window.KeyPressQwerty(PhysicalKey.ArrowRight, 0); window.KeyReleaseQwerty(PhysicalKey.ArrowRight, 0); Dispatcher.UIThread.RunJobs();window.UpdateLayout();
        Assert.True(model.IsAssetSelected(assets[1].AssetId)); Orange(Card(1));
        window.MouseMove(Card(1).TranslatePoint(new Point(30,30),window)!.Value); window.UpdateLayout(); Orange(Card(1));
        window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.Shift); window.KeyReleaseQwerty(PhysicalKey.ArrowRight, RawInputModifiers.Shift);
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); Assert.Equal(2, model.SelectedAssetKeys.Count);
        Orange(Card(1)); Orange(Card(2));
        window.KeyPressQwerty(PhysicalKey.ArrowLeft, RawInputModifiers.Meta); window.KeyReleaseQwerty(PhysicalKey.ArrowLeft, RawInputModifiers.Meta);
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
        foreach (var card in pane.GetVisualDescendants().OfType<BrowserGridCard>().Where(c => model.IsAssetSelected(c.AssetId))) Orange(card);
        await model.SelectAsync(assets[1].AssetId);
        foreach (var mode in Enum.GetValues<BrowserCardMode>()) { pane.SetCardMode(mode);window.UpdateLayout();Orange(Card(1)); }
        var focused = Card(2); focused.Focus(NavigationMethod.Directional); await model.SelectAsync(assets[1].AssetId);window.UpdateLayout();
        var neutralChrome = focused.GetVisualDescendants().OfType<Border>().Single(b=>b.Name=="Chrome");
        Assert.False(model.IsAssetSelected(focused.AssetId)); Assert.NotEqual(Color.Parse("#FF9A66"),(neutralChrome.BorderBrush as ISolidColorBrush)?.Color);
        var refresh=model.RefreshAsync();service.Folders[""].SetResult(new(service.RootId,"",false,MediaFolderEnumerationStatus.Succeeded,[],assets.ToArray()));await refresh;
        window.Width=1120; Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); Orange(Card(1));
        var list=pane.GetVisualDescendants().OfType<ListBox>().Single(); list.ScrollIntoView(list.ItemCount-1);window.UpdateLayout();list.ScrollIntoView(0);window.UpdateLayout();Orange(Card(1));
        window.Close(); model.Suspend();
    }
    [AvaloniaFact]
    public async Task CompactModeControlsHaveOnePermanentStatusHomeAndRemainBounded()
    {
        var service = new FakeShellBrowser(); var shell = new ShellViewModel(service,new FakeLocations());
        var window = new ShellWindow(shell);window.Show();await Dispatcher.UIThread.InvokeAsync(()=>{},DispatcherPriority.Background);window.UpdateLayout();
        var pane=window.Browser!;
        var group=window.GetVisualDescendants().OfType<Grid>().Single(c=>Avalonia.Automation.AutomationProperties.GetAutomationId(c)=="BrowserPresentationControls");
        Assert.Contains(pane.StatusView,group.GetVisualAncestors());
        Assert.DoesNotContain(pane.GetVisualDescendants(),c=>ReferenceEquals(c,group));
        var buttons=group.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.ToggleButton>().ToArray();
        Assert.Equal(3,buttons.Length); Assert.Equal(new[]{"Preview view","Info view","Hybrid view"},buttons.Select(Avalonia.Automation.AutomationProperties.GetName));
        foreach(var (button,mode) in buttons.Zip(Enum.GetValues<BrowserCardMode>()))
        {
            Assert.Equal(27,button.Width);Assert.Equal(24,button.Height);Assert.Equal($"{mode} view",ToolTip.GetTip(button));
            button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));window.UpdateLayout();
            Assert.Equal(mode,pane.CardMode);Assert.True(button.IsChecked);Assert.Single(buttons,b=>b.IsChecked==true);
            Assert.Equal(Color.Parse("#FF9A66"),(button.BorderBrush as ISolidColorBrush)?.Color);
            Assert.Equal(button.Foreground,((Avalonia.Controls.Shapes.Path)button.Content!).Stroke);
        }
        foreach(var width in new[]{1120d,1440d,1800d})
        {
            window.Width=width;
            foreach(var open in new[]{true,false})
            {
                if(pane.RightPanelOpen!=open)pane.ToggleRightPanel();window.UpdateLayout();
                var point=group.TranslatePoint(default,window)!.Value;
                Assert.True(point.X>=0 && point.X+group.Bounds.Width<=window.Bounds.Width);
                Assert.True(point.Y>window.Bounds.Height-40); Assert.InRange(group.Bounds.Width,81,82);
                Assert.Equal(3,window.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.ToggleButton>().Count(b=>Avalonia.Automation.AutomationProperties.GetName(b)?.EndsWith(" view")==true));
            }
        }
        window.Close();shell.Browser?.Suspend();
    }

}
