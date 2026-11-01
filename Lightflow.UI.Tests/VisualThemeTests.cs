using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using Lightflow.UI;
using Xunit;

namespace Lightflow.UI.Tests;

public sealed class VisualThemeTests
{
    [AvaloniaFact]
    public void SharedResourcesApplyToRealTemplatesAndExistingVisualStates()
    {
        var primary=LightflowTheme.Button("Create Catalog","primary");
        var quiet=LightflowTheme.Button("Refresh","quiet");
        var disabled=LightflowTheme.Button("Unavailable");disabled.IsEnabled=false;
        var active=LightflowTheme.Button("Right Panel");LightflowTheme.State(active,"active",true);
        var text=LightflowTheme.Text("Secondary metadata","muted");
        var icon=BrowserPresentation.Icon("folder",true);
        var input=new TextBox { Classes={ "lf-input" },Text="Folder path" };
        var panel=LightflowTheme.Panel(new StackPanel { Children={primary,quiet,disabled,active,text,icon,input} });
        var window=new Window { Width=800,Height=600,Content=panel };
        LightflowTheme.Apply(window);window.Show();window.UpdateLayout();
        Assert.Equal(Color.Parse("#0B0D11"),((ISolidColorBrush)window.Background!).Color);
        Assert.Equal(Color.Parse("#171A20"),((ISolidColorBrush)panel.Background!).Color);
        Assert.Equal(new CornerRadius(8),panel.CornerRadius);Assert.Equal(new Thickness(9),panel.Padding);
        Assert.Equal(Color.Parse("#FF713D"),((ISolidColorBrush)primary.Background!).Color);
        Assert.True(Contrast(((ISolidColorBrush)primary.Foreground!).Color,((ISolidColorBrush)primary.Background!).Color)>=4.5);
        Assert.True(Contrast(((ISolidColorBrush)text.Foreground!).Color,((ISolidColorBrush)panel.Background!).Color)>=4.5);
        Assert.Equal(32,primary.MinHeight);Assert.Equal(13,primary.FontSize);
        Assert.Equal(Color.Parse("#282129"),((ISolidColorBrush)active.Background!).Color);
        Assert.Equal(Color.Parse("#FF9A66"),((ISolidColorBrush)active.BorderBrush!).Color);
        Assert.Equal(0.4,disabled.Opacity,2);Assert.False(disabled.IsEnabled);
        Assert.Equal(12,text.FontSize);Assert.Equal(Color.Parse("#AAB2BF"),((ISolidColorBrush)text.Foreground!).Color);
        Assert.Equal(16,icon.Width);Assert.Equal(1.5,icon.StrokeThickness);
        Assert.Equal(Color.Parse("#FF9A66"),((ISolidColorBrush)icon.Fill!).Color);
        Assert.Equal(Color.Parse("#0B0D11"),((ISolidColorBrush)input.Background!).Color);
        quiet.Focus(NavigationMethod.Tab);window.UpdateLayout();
        var chrome=quiet.GetVisualDescendants().OfType<Border>().Single(c=>c.Name=="Chrome");
        Assert.Null(quiet.FocusAdorner);
        Assert.Equal(new Thickness(2),chrome.BorderThickness);
        Assert.Equal(Color.Parse("#FF9A66"),((ISolidColorBrush)chrome.BorderBrush!).Color);
        window.Close();
    }
    private static double Contrast(Color foreground,Color background)
    {
        static double Channel(byte value) { var s=value/255d;return s<=0.04045 ? s/12.92 : Math.Pow((s+0.055)/1.055,2.4); }
        static double Luminance(Color c)=>0.2126*Channel(c.R)+0.7152*Channel(c.G)+0.0722*Channel(c.B);
        var a=Luminance(foreground);var b=Luminance(background);return (Math.Max(a,b)+0.05)/(Math.Min(a,b)+0.05);
    }
}
