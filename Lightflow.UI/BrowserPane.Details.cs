using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
namespace Lightflow.UI;

public sealed partial class BrowserPane
{
    private BrowserLayoutMode _presentation;
    private IReadOnlyList<BrowserColumnPreference> _columns = BrowserDetailsLayout.Normalize(null);
    private readonly Grid _detailsHeaders = new();
    private readonly ScrollViewer _headerScroll = new() { HorizontalScrollBarVisibility=ScrollBarVisibility.Hidden,VerticalScrollBarVisibility=ScrollBarVisibility.Disabled,IsVisible=false };
    private readonly Dictionary<BrowserLayoutMode,ToggleButton> _layoutButtons=[];
    private IDataTemplate? _gridTemplate;
    private long _presentationRevision;
    public BrowserLayoutMode Presentation => _presentation;
    public IReadOnlyList<BrowserColumnPreference> DetailsColumns => _columns;
    private int PresentationColumns => _presentation==BrowserLayoutMode.Details ? 1 : _cardColumns;
    private double PresentationRowHeight => _presentation==BrowserLayoutMode.Details ? BrowserDetailsLayout.RowHeight : BrowserCardLayout.Height(_cardMode)+BrowserCardLayout.Gap;

    private Control CreateLayoutControls()
    {
        var group=new StackPanel { Orientation=Orientation.Horizontal,Spacing=1,Margin=new Thickness(0,0,8,0) };
        foreach(var mode in Enum.GetValues<BrowserLayoutMode>())
        {
            var button=new ToggleButton { Classes={"lf-grid-mode"},Width=32,Height=28 };
            button.Content=new Avalonia.Controls.Shapes.Path { Width=16,Height=16,Stretch=Stretch.Uniform,
                Stroke=new SolidColorBrush(Color.Parse("#AAB2BF")),StrokeThickness=1.5,
                Data=Geometry.Parse(mode==BrowserLayoutMode.Grid ? "M1,1 L6,1 L6,6 L1,6 Z M10,1 L15,1 L15,6 L10,6 Z M1,10 L6,10 L6,15 L1,15 Z M10,10 L15,10 L15,15 L10,15 Z" : "M1,2 L4,2 M7,2 L15,2 M1,8 L4,8 M7,8 L15,8 M1,14 L4,14 M7,14 L15,14") };
            AutomationProperties.SetName(button,$"{mode} layout"); AutomationProperties.SetAutomationId(button,$"Browser{mode}LayoutButton");
            ToolTip.SetTip(button,$"{mode} view");button.Click+=(_,_)=>SetPresentation(mode);
            _layoutButtons[mode]=button;group.Children.Add(button);
        }
        return group;
    }
    private void InitializeDetails(Grid media)
    {
        _gridTemplate=_assets.ItemTemplate;
        media.RowDefinitions=new RowDefinitions("Auto,*");
        Grid.SetRow(_assets,1); Grid.SetRow(_gridEmpty,1);
        _headerScroll.Content=_detailsHeaders; _headerScroll.Height=32;
        AutomationProperties.SetName(_detailsHeaders,"Details columns. Drag headers to reorder; drag separators to resize. Right-click for visibility.");
        media.Children.Add(_headerScroll);
        _assets.AddHandler(ScrollViewer.ScrollChangedEvent,(_,_)=>
        {
            if(_presentation==BrowserLayoutMode.Details && AssetScroll is { } scroll)
                _headerScroll.Offset=new Vector(scroll.Offset.X,0);
        });
        RebuildDetailsHeaders();
        SetPresentation(_preferred.Presentation,persist:false);
    }
    private ScrollViewer? AssetScroll => _assets.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
    private IEnumerable<BrowserDetailsRow> RealizedDetails => _assets.GetVisualDescendants().OfType<BrowserDetailsRow>();
    public void SetPresentation(BrowserLayoutMode mode,bool persist=true)
    {
        if(!Enum.IsDefined(mode))throw new ArgumentOutOfRangeException(nameof(mode));
        var revision=++_presentationRevision; var snapshot=_model.Assets; var epoch=_model.Epoch;
        var focus=_assets.IsKeyboardFocusWithin;
        var top=AssetScroll is { } old ? (int)(old.Offset.Y/PresentationRowHeight)*PresentationColumns : 0;
        var anchor=_model.Assets.ElementAtOrDefault(top)?.AssetId;
        _presentation=mode;
        _headerScroll.IsVisible=mode==BrowserLayoutMode.Details;
        _assets.Padding=mode==BrowserLayoutMode.Details ? new Thickness(0) : new Thickness(7);
        ScrollViewer.SetHorizontalScrollBarVisibility(_assets,mode==BrowserLayoutMode.Details ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled);
        _assets.ItemTemplate=mode==BrowserLayoutMode.Details
            ? new FuncDataTemplate<BrowserCardRow>((row,_)=> row is null ? null : new BrowserDetailsRow(_model,row.Assets[row.Start],_columns,SelectCardAsync))
            : _gridTemplate;
        foreach(var (value,button) in _layoutButtons)button.IsChecked=value==mode;
        foreach(var button in _modeButtons.Values)button.IsEnabled=mode==BrowserLayoutMode.Grid;
        AutomationProperties.SetName(_assets,mode==BrowserLayoutMode.Details ? "Media Details in current folder" : "Media grid in current folder");
        RebuildCardRows();
        Dispatcher.UIThread.Post(()=>
        {
            if(revision!=_presentationRevision || epoch!=_model.Epoch || !ReferenceEquals(snapshot,_model.Assets))return;
            var index=anchor is { } id ? IndexOfAsset(id) : 0;
            if(index>=0 && AssetScroll is { } scroll)scroll.Offset=new Vector(0,index/PresentationColumns*PresentationRowHeight);
            if(focus)KeepCardVisible(true);
            QueueViewport();
        },DispatcherPriority.Loaded);
        if(persist)SavePresentation();
    }
    private void SavePresentation()
    {
        _preferred=_preferred with { Presentation=_presentation,CardMode=_cardMode,DetailsColumns=_columns };
        LayoutChanged?.Invoke(_preferred);
    }
    public void ConfigureDetailsColumns(IReadOnlyList<BrowserColumnPreference> columns)
    {
        _columns=BrowserDetailsLayout.Normalize(columns); RebuildDetailsHeaders();
        foreach(var row in RealizedDetails)row.UpdateColumns(_columns);
        SavePresentation();
    }
    private void ResizeColumn(string id,double delta)
    {
        _columns=_columns.Select(c=>c.Id==id ? c with { Width=Math.Clamp(c.Width+delta,40,1200) } : c).ToArray();
        var visible=_columns.Where(c=>c.Visible).ToArray();
        for(var i=0;i<visible.Length;i++)_detailsHeaders.ColumnDefinitions[i].Width=new GridLength(visible[i].Width);
        _detailsHeaders.Width=visible.Sum(c=>c.Width);
        foreach(var row in RealizedDetails)row.ResizeColumns(_columns);
    }
    private void RebuildDetailsHeaders()
    {
        _detailsHeaders.Children.Clear();_detailsHeaders.ColumnDefinitions.Clear();
        var visible=_columns.Where(c=>c.Visible).ToArray();
        for(var i=0;i<visible.Length;i++)
        {
            var column=visible[i];_detailsHeaders.ColumnDefinitions.Add(new ColumnDefinition(column.Width,GridUnitType.Pixel));
            var header=new Grid();Grid.SetColumn(header,i);
            var label=Action(BrowserDetailsLayout.Columns.Single(c=>c.Id==column.Id).Title);
            label.Classes.Add("details-header");label.CornerRadius=new CornerRadius(0);label.BorderThickness=new Thickness(0,0,1,1);
            label.HorizontalAlignment=HorizontalAlignment.Stretch;label.HorizontalContentAlignment=HorizontalAlignment.Left;label.Padding=new Thickness(8,6,4,6);label.FontSize=12;
            ToolTip.SetTip(label,"Drag to reorder. Right-click to choose columns. Sorting will be available with Browser sorting.");
            Point? start=null;
            label.AddHandler(PointerPressedEvent,(_,e)=> { if(e.GetCurrentPoint(label).Properties.IsLeftButtonPressed) { label.Focus(NavigationMethod.Pointer);start=e.GetPosition(_detailsHeaders);e.Pointer.Capture(label);e.Handled=true; } },Avalonia.Interactivity.RoutingStrategies.Tunnel);
            label.AddHandler(PointerReleasedEvent,(_,e)=>
            {
                if(e.InitialPressMouseButton==MouseButton.Right)
                { label.Focus(NavigationMethod.Pointer);label.ContextMenu?.Open(label);e.Handled=true;return; }
                if(start is null)return;
                if(start is { } origin && Math.Abs(e.GetPosition(_detailsHeaders).X-origin.X)>8)
                {
                    var x=e.GetPosition(_detailsHeaders).X;var target=0;double end=0;
                    for(var j=0;j<visible.Length;j++){end+=visible[j].Width;if(x>=end)target=Math.Min(j+1,visible.Length-1);}
                    var order=visible.Select(c=>c.Id).ToList();order.Remove(column.Id);order.Insert(Math.Clamp(target,0,order.Count),column.Id);
                    var queue=new Queue<string>(order);var byId=_columns.ToDictionary(c=>c.Id);
                    e.Pointer.Capture(null);start=null;
                    ConfigureDetailsColumns(_columns.Select(c=>c.Visible ? byId[queue.Dequeue()] : c).ToArray());
                }
                else {e.Pointer.Capture(null);start=null;}
                e.Handled=true;
            },Avalonia.Interactivity.RoutingStrategies.Tunnel);
            var menu=new ContextMenu();
            foreach(var definition in BrowserDetailsLayout.Columns)
            {
                var item=new MenuItem { Header=(_columns.Single(c=>c.Id==definition.Id).Visible ? "✓  " : "    ")+definition.Title };
                item.Click+=(_,_)=> { var current=_columns.Single(c=>c.Id==definition.Id); if(current.Visible && _columns.Count(c=>c.Visible)==1)return; ConfigureDetailsColumns(_columns.Select(c=>c.Id==definition.Id ? c with { Visible=!c.Visible } : c).ToArray()); };
                menu.Items.Add(item);
            }
            label.ContextMenu=menu;
            label.AddHandler(KeyDownEvent,(_,e)=>
            {
                if(e.Key is Key.Enter or Key.Space or Key.Apps || (e.Key==Key.F10 && e.KeyModifiers==KeyModifiers.Shift))
                { menu.Open(label);e.Handled=true; }
            },Avalonia.Interactivity.RoutingStrategies.Tunnel);
            header.Children.Add(label);
            var grip=new Thumb { Width=6,HorizontalAlignment=HorizontalAlignment.Right,Cursor=new Cursor(StandardCursorType.SizeWestEast),Background=Brushes.Transparent,Focusable=true };
            grip.Template=new FuncControlTemplate<Thumb>((_,_)=>new Border {Background=Brushes.Transparent});
            AutomationProperties.SetName(grip,$"Resize {label.Content}");
            grip.AddHandler(PointerPressedEvent,(_,_)=>grip.Focus(NavigationMethod.Pointer),Avalonia.Interactivity.RoutingStrategies.Tunnel);
            grip.DragDelta+=(_,e)=>ResizeColumn(column.Id,e.Vector.X);grip.DragCompleted+=(_,_)=>SavePresentation();
            grip.KeyDown+=(_,e)=> {if(e.Key is Key.Left or Key.Right){ResizeColumn(column.Id,e.Key==Key.Left ? -8 : 8);SavePresentation();e.Handled=true;}};
            header.Children.Add(grip);_detailsHeaders.Children.Add(header);
        }
        _detailsHeaders.Width=visible.Sum(c=>c.Width);
    }
}
