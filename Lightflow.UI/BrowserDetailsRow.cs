using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using LightflowStudio;
namespace Lightflow.UI;

/// <summary>Only realized rows own cells/bitmaps; identity and selection stay in BrowserViewModel.</summary>
internal sealed class BrowserDetailsRow : Button
{
    protected override Type StyleKeyOverride => typeof(Button);
    private readonly BrowserViewModel _model;
    private readonly MediaAsset _asset;
    private readonly BrowserDetailsColumnState _columns;
    private readonly Grid _cells = new();
    private readonly Dictionary<string, Control> _content = [];
    public Guid AssetId => _asset.AssetId;
    public BrowserDetailsRow(BrowserViewModel model, MediaAsset asset, BrowserDetailsColumnState columns,
        Func<MediaAsset,KeyModifiers,Task> select)
    {
        _model=model; _asset=asset; _columns=columns;
        Classes.Add("lf-details-row"); Height=BrowserDetailsLayout.RowHeight;
        Padding=new Thickness(0); BorderThickness=new Thickness(0,0,0,1); CornerRadius=new CornerRadius(0);
        HorizontalAlignment=HorizontalAlignment.Left; HorizontalContentAlignment=HorizontalAlignment.Stretch;
        VerticalContentAlignment=VerticalAlignment.Center; Content=_cells;
        var modifiers=KeyModifiers.None;
        AddHandler(PointerPressedEvent,(_,e)=>modifiers=e.KeyModifiers,Avalonia.Interactivity.RoutingStrategies.Tunnel);
        Click+=async(_,_)=> { var used=modifiers; modifiers=KeyModifiers.None; await select(asset,used); };
        AttachedToVisualTree+=(_,_)=> { model.PropertyChanged+=Changed; _columns.Changed+=ApplyColumns; ApplyColumns(); };
        DetachedFromVisualTree+=(_,_)=> { model.PropertyChanged-=Changed; _columns.Changed-=ApplyColumns; };
        AutomationProperties.SetName(this,asset.RelativePath);
        ApplyColumns();
    }
    private void ApplyColumns()
    {
        var visible=_columns.Columns.Where(c=>c.Visible).ToArray();
        // Position is derived from field identity, never from visual child/enumerator order.
        // Do not detach surviving cells: a reorder must retain row focus and Preview bitmaps.
        foreach(var (id,cell) in _content)
            if(!visible.Any(c=>c.Id==id))_cells.Children.Remove(cell);
        while(_cells.ColumnDefinitions.Count>visible.Length)_cells.ColumnDefinitions.RemoveAt(_cells.ColumnDefinitions.Count-1);
        for(var i=0;i<visible.Length;i++)
        {
            var column=visible[i];
            if(i==_cells.ColumnDefinitions.Count)_cells.ColumnDefinitions.Add(new ColumnDefinition());
            _cells.ColumnDefinitions[i].Width=new GridLength(column.Width);
            if(!_content.TryGetValue(column.Id,out var cell))
            {
                cell=column.Id=="preview" ? new BrowserThumbnailCell(_model,AssetId) { Width=double.NaN,Height=54,HorizontalAlignment=HorizontalAlignment.Stretch }
                    : LightflowTheme.Text("","metadata");
                cell.Margin=new Thickness(8,0,6,0); cell.VerticalAlignment=VerticalAlignment.Center;
                cell.Tag=column.Id;
                AutomationProperties.SetAutomationId(cell,"BrowserDetailsCell-"+column.Id);
                if(cell is TextBlock text) { text.TextWrapping=TextWrapping.NoWrap; text.TextTrimming=TextTrimming.CharacterEllipsis; }
                _content[column.Id]=cell;
            }
            Grid.SetColumn(cell,i);
            if(!_cells.Children.Contains(cell))_cells.Children.Add(cell);
            var previous=_cells.Children.IndexOf(cell);
            if(previous!=i)_cells.Children.Move(previous,i);
        }
        Width=visible.Sum(c=>c.Width); Apply();
    }
    private void Changed(object? sender,PropertyChangedEventArgs e)=>Apply();
    private void Apply()
    {
        var selected=_model.IsAssetSelected(AssetId); Classes.Set("selected",selected);
        Background=new SolidColorBrush(Color.Parse(selected ? "#282129" : "#0B0D11"));
        BorderBrush=new SolidColorBrush(Color.Parse(selected ? "#FF9A66" : "#2B303A"));
        AutomationProperties.SetHelpText(this,selected ? "Selected. Arrow keys navigate Details." : "Arrow keys navigate Details.");
        foreach(var (id,cell) in _content)
            if(cell is TextBlock text) { text.Text=BrowserDetailsLayout.Text(_asset,_model.Thumbnails.GetValueOrDefault(AssetId),id); ToolTip.SetTip(text,text.Text); }
    }
}
