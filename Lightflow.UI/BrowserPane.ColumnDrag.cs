using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
namespace Lightflow.UI;

public sealed partial class BrowserPane
{
    private readonly Border _columnDropIndicator=new()
    {
        Width=2,HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Stretch,
        IsVisible=false,IsHitTestVisible=false,Background=new SolidColorBrush(Color.Parse("#FF9A66"))
    };
    private sealed record ColumnDrag(string Id,Button Header,IPointer Pointer,Point Origin)
    {
        public bool Started;
        public BrowserColumnDrop? Target;
    }
    private ColumnDrag? _columnDrag;
    private void WireColumnDrag(Button label,string id)
    {
        label.AddHandler(PointerPressedEvent,(_,e)=>
        {
            if(!e.GetCurrentPoint(label).Properties.IsLeftButtonPressed)return;
            CancelColumnDrag();label.Focus(NavigationMethod.Pointer);
            _columnDrag=new(id,label,e.Pointer,e.GetPosition(_detailsHeaders));e.Pointer.Capture(label);e.Handled=true;
        },RoutingStrategies.Tunnel);
        label.AddHandler(PointerMovedEvent,(_,e)=>
        {
            if(_columnDrag is not { } drag || drag.Header!=label)return;
            UpdateColumnDrag(drag,e.GetPosition(_detailsHeaders),e.GetPosition(_headerScroll));
        },RoutingStrategies.Tunnel);
        label.AddHandler(PointerReleasedEvent,(_,e)=>
        {
            if(e.InitialPressMouseButton==MouseButton.Right)
            {label.Focus(NavigationMethod.Pointer);label.ContextMenu?.Open(label);e.Handled=true;return;}
            if(_columnDrag is not { } drag || drag.Header!=label)return;
            UpdateColumnDrag(drag,e.GetPosition(_detailsHeaders),e.GetPosition(_headerScroll));
            var target=drag.Started ? drag.Target : null;
            CancelColumnDrag();
            if(target is {Valid:true})ConfigureDetailsColumns(BrowserColumnReorder.Move(_columns,id,target.Boundary));
            e.Handled=true;
        },RoutingStrategies.Tunnel);
        label.PointerCaptureLost+=(_,_)=> {if(_columnDrag?.Header==label)CancelColumnDrag();};
        label.AddHandler(KeyDownEvent,(_,e)=> {if(e.Key==Key.Escape && _columnDrag is not null){CancelColumnDrag();e.Handled=true;}},RoutingStrategies.Tunnel);
    }
    private void UpdateColumnDrag(ColumnDrag drag,Point content,Point viewport)
    {
        if(!drag.Started && Math.Abs(content.X-drag.Origin.X)<=8 && Math.Abs(content.Y-drag.Origin.Y)<=8)return;
        drag.Started=true;
        var inside=viewport.X>=0 && viewport.X<=_headerScroll.Bounds.Width && viewport.Y>=0 && viewport.Y<=_headerScroll.Bounds.Height;
        var target=drag.Target=BrowserColumnReorder.Target(_columns,drag.Id,content.X,inside);
        _columnDropIndicator.IsVisible=true;
        _columnDropIndicator.Margin=new Thickness(Math.Clamp(target.X-1,0,Math.Max(0,_detailsHeaders.Width-2)),0,0,0);
        _columnDropIndicator.Background=new SolidColorBrush(Color.Parse(target.Valid ? "#FF9A66" : "#AAB2BF"));
        AutomationProperties.SetAutomationId(_columnDropIndicator,"BrowserColumnDropIndicator");
        AutomationProperties.SetName(_columnDropIndicator,target.Valid ? $"Move {drag.Id} to column boundary {target.Boundary}" : "Column cannot be dropped here");
        drag.Header.Cursor=new Cursor(target.Valid ? StandardCursorType.DragMove : StandardCursorType.No);
    }
    private void CancelColumnDrag()
    {
        var drag=_columnDrag;_columnDrag=null;_columnDropIndicator.IsVisible=false;
        if(drag is null)return;
        drag.Header.Cursor=null;
        if(drag.Pointer.Captured==drag.Header)drag.Pointer.Capture(null);
    }
}
