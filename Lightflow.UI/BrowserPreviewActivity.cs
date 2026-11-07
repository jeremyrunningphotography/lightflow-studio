using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
namespace Lightflow.UI;

/// <summary>Windows MainWindow.xaml/App.xaml Preview progress geometry, without another work owner.</summary>
internal sealed class BrowserPreviewActivity : Border
{
    private readonly Border _pulse;
    public BrowserPreviewActivity(bool details)
    {
        Width=details ? 30 : 38; Height=4;
        HorizontalAlignment=HorizontalAlignment.Center; VerticalAlignment=VerticalAlignment.Bottom;
        Margin=new Thickness(0,0,0,details ? 0 : 7); CornerRadius=new CornerRadius(6);
        ClipToBounds=true; IsHitTestVisible=false; IsVisible=false;
        Background=new SolidColorBrush(Color.Parse("#0C0E12"));
        // WPF's centered 60-DIP pulse is clipped by its narrower rounded track.
        _pulse=new Border {Width=60,HorizontalAlignment=HorizontalAlignment.Center,CornerRadius=new CornerRadius(6),
            Classes={"lf-preview-pulse"},Background=new LinearGradientBrush
            {
                StartPoint=new RelativePoint(0,0,RelativeUnit.Relative),EndPoint=new RelativePoint(1,1,RelativeUnit.Relative),
                GradientStops={new(Color.Parse("#FF713D"),0),new(Color.Parse("#FF3F46"),.48),new(Color.Parse("#C42D78"),1)}
            }};
        Child=_pulse;
    }
    public void SetGenerating(bool active)
    {
        IsVisible=active;_pulse.Classes.Set("generating",active);
    }
}
