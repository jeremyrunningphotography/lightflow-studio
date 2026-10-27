using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Controls.Presenters;
using LightflowStudio;

namespace Lightflow.UI;

/// <summary>One shared Browser presentation, with application requests only and no filesystem/Catalog dependency.</summary>
public sealed class BrowserPane : UserControl
{
    private readonly BrowserViewModel _model;
    private readonly StackPanel _roots = new() { Spacing = 0 };
    private readonly Grid _layout;
    private readonly Control _right;
    private readonly GridSplitter _rightSplitter;
    private readonly Button _panelToggle = Action("Right Panel");
    private readonly TextBlock _count = Text("",12);
    private readonly ProgressBar _progress = new() { IsIndeterminate=true, Height=2 };
    private double _rightWeight = .24;
    public event Action<BrowserLayout>? LayoutChanged;
    private readonly ListBox _assets = new() { SelectionMode = SelectionMode.Single, Background=Brush("#11141A"), BorderThickness=new Thickness(0) };
    private readonly TextBlock _location=Text("Choose a location",14), _message=Text("",13), _summary=Text("",13), _source=Text("",13);
    private readonly Button _back=Action("←"), _forward=Action("→"), _up=Action("↑"), _refresh=Action("↻"), _go=Action("Go"), _cancel=Action("Cancel"), _recursive=Action("Include Subfolders");
    private readonly TextBox _address = new() { Watermark="Folder path", MinWidth=100, VerticalAlignment=VerticalAlignment.Center };
    private IReadOnlyList<BrowserFolderNode>? _shownRoots;
    private IReadOnlyList<MediaAsset>? _shownAssets;
    private bool _updating;
    private sealed class FolderRow(string treeKey, BrowserFolderNode node, StackPanel group, StackPanel children, Button choose, Button toggle)
    {
        public string TreeKey = treeKey;
        public BrowserFolderNode Node = node;
        public StackPanel Group = group, Children = children;
        public Button Choose = choose, Toggle = toggle;
        public bool Expanded;
        public long Generation;
        public IReadOnlyList<BrowserFolderNode> Folders = [];
    }
    private readonly Dictionary<string, FolderRow> _rows = [];
    private readonly ScrollViewer _sideScroll;
    private long _treeGeneration;
    public BrowserPane(BrowserViewModel model, BrowserLayout? savedLayout = null)
    {
        _model=model;
        var saved=(savedLayout ?? new()).Validated();
        _rightWeight=saved.Right;
        var layout=_layout=new Grid { ColumnDefinitions=new("*,8,*,8,*"), RowDefinitions=new("*,Auto") };
        layout.ColumnDefinitions[0].Width=new GridLength(saved.Navigation,GridUnitType.Star);
        layout.ColumnDefinitions[0].MinWidth=180; layout.ColumnDefinitions[0].MaxWidth=520;
        layout.ColumnDefinitions[2].Width=new GridLength(saved.Assets,GridUnitType.Star);
        layout.ColumnDefinitions[2].MinWidth=340;
        layout.ColumnDefinitions[4].Width=new GridLength(saved.Right,GridUnitType.Star);
        layout.ColumnDefinitions[4].MinWidth=200; layout.ColumnDefinitions[4].MaxWidth=480;
        var side=new StackPanel { Spacing=6, Children={Text("Locations",14), _roots} };
        _sideScroll=new ScrollViewer { Content=side, HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled };
        var left=Panel(_sideScroll); layout.Children.Add(left);
        var navigationSplitter=Splitter("Resize folder navigation"); Grid.SetColumn(navigationSplitter,1); layout.Children.Add(navigationSplitter);
        var center=new Grid { RowDefinitions=new("Auto,4,*") };
        var toolbar=new Grid { ColumnDefinitions=new("Auto,Auto,Auto,*,Auto,Auto"), Margin=new Thickness(0,0,0,4) };
        var controls = new Control[] { _back, _forward, _up, _address, _go, _refresh };
        for (var i=0; i<controls.Length; i++) { Grid.SetColumn(controls[i],i); controls[i].Margin=new Thickness(1,0); toolbar.Children.Add(controls[i]); }
        foreach (var (button, icon, label) in new[] { (_back,"back","Back"), (_forward,"forward","Forward"), (_up,"up","Up one folder"), (_refresh,"refresh","Refresh current folder"), (_recursive,"scope","Include Subfolders"), (_cancel,"cancel","Stop loading"), (_panelToggle,"panel","Toggle Right Panel") })
        { button.Content=BrowserPresentation.Icon(icon); button.Width=32; button.Padding=new Thickness(7); ToolTip.SetTip(button,label); AutomationProperties.SetName(button,label); }
        ToolTip.SetTip(_go,"Open folder path"); AutomationProperties.SetName(_go,"Open folder path");
        _address.MinWidth=70; _address.Height=32; _address.Padding=new Thickness(6,4);
        _address.Background=Brush(BrowserPresentation.Surface); _address.BorderBrush=Brush(BrowserPresentation.Divider);
        AutomationProperties.SetName(_address,"Current folder path");
        var scope=new Grid { ColumnDefinitions=new("*,Auto,Auto,Auto") };
        _location.TextTrimming=TextTrimming.CharacterEllipsis; _location.TextWrapping=TextWrapping.NoWrap;
        _location.VerticalAlignment=VerticalAlignment.Center; scope.Children.Add(_location);
        Grid.SetColumn(_recursive,1); scope.Children.Add(_recursive);
        Grid.SetColumn(_cancel,2); scope.Children.Add(_cancel);
        Grid.SetColumn(_panelToggle,3); scope.Children.Add(_panelToggle);
        var toolbarContent=new StackPanel { Children={toolbar,scope} };
        center.Children.Add(Panel(toolbarContent));
        var media=new Grid { RowDefinitions=new("28,*") };
        var columns=new Grid { ColumnDefinitions=new("*,74,90"), Margin=new Thickness(12,0) };
        foreach(var (label,index) in new[] { ("Name",0), ("Kind",1), ("Size",2) })
        { var text=Text(label,12); text.VerticalAlignment=VerticalAlignment.Center; text.Foreground=Brush("#9DA5B3"); Grid.SetColumn(text,index); columns.Children.Add(text); }
        media.Children.Add(columns);
        _assets.ItemTemplate=new FuncDataTemplate<MediaAsset>((asset,_)=>
        {
            if(asset is null)return new TextBlock();
            var row=new Grid { ColumnDefinitions=new("*,74,90"), Margin=new Thickness(8,2), MinHeight=26 };
            var name=Text(asset.RelativePath.Split('/')[^1],13); name.TextWrapping=TextWrapping.NoWrap; name.TextTrimming=TextTrimming.CharacterEllipsis;
            row.Children.Add(name);
            var kind=Text(asset.MediaType,12); kind.Foreground=Brush("#B9BEC8"); Grid.SetColumn(kind,1); row.Children.Add(kind);
            var size=Text($"{asset.FileSizeBytes:N0} bytes",12); size.Foreground=Brush("#B9BEC8"); size.HorizontalAlignment=HorizontalAlignment.Right; Grid.SetColumn(size,2); row.Children.Add(size);
            ToolTip.SetTip(row,asset.RelativePath);
            AutomationProperties.SetName(row,$"{asset.RelativePath}, {asset.MediaType}, Catalog asset {asset.AssetId}");
            return row;
        });
        _assets.Styles.Add(new Style(selector=>selector.OfType<ListBoxItem>()) { Setters={new Setter(PaddingProperty,new Thickness(4,0)),new Setter(MinHeightProperty,30.0),new Setter(HorizontalContentAlignmentProperty,HorizontalAlignment.Stretch)} });
        _assets.Styles.Add(new Style(selector=>selector.OfType<ListBoxItem>().Class(":selected").Template().OfType<ContentPresenter>().Name("PART_ContentPresenter")) { Setters={new Setter(BackgroundProperty,Brush(BrowserPresentation.Selection))} });
        _assets.Styles.Add(new Style(selector=>selector.OfType<ListBoxItem>().Class(":selected")) { Setters={new Setter(BackgroundProperty,Brush(BrowserPresentation.Selection)),new Setter(BorderBrushProperty,Brush(BrowserPresentation.Accent)),new Setter(BorderThicknessProperty,new Thickness(1))} });
        _assets.Styles.Add(new Style(selector=>selector.OfType<ListBoxItem>().Class(":selected").Class(":pointerover").Template().OfType<ContentPresenter>().Name("PART_ContentPresenter")) { Setters={new Setter(BackgroundProperty,Brush(BrowserPresentation.Selection))} });
        _assets.ContainerPrepared += (_, e) =>
        {
            if (e.Index >= 0 && e.Index < model.Assets.Count)
            {
                var asset = model.Assets[e.Index];
                AutomationProperties.SetName(e.Container, $"{asset.RelativePath}, {asset.MediaType}, Catalog asset {asset.AssetId}");
            }
        };
        _assets.SelectionChanged+=async (_,_)=> { if(!_updating)await model.SelectAsync((_assets.SelectedItem as MediaAsset)?.AssetId); };
        AutomationProperties.SetAutomationId(_assets,"CatalogAssets");
        Grid.SetRow(_assets,1);media.Children.Add(_assets);
        var mediaPanel=Panel(media,false); Grid.SetRow(mediaPanel,2); center.Children.Add(mediaPanel); Grid.SetColumn(center,2);layout.Children.Add(center);
        var details=new StackPanel { Spacing=12, Children={_source,_summary} };
        var rightContent=new Grid { RowDefinitions=new("32,*") };
        var rightTitle=Text("Selection",13); rightTitle.FontWeight=FontWeight.SemiBold;
        rightTitle.VerticalAlignment=VerticalAlignment.Center; rightContent.Children.Add(rightTitle);
        var detailsScroll=new ScrollViewer { Content=details, HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled };
        Grid.SetRow(detailsScroll,1); rightContent.Children.Add(detailsScroll);
        _right=Panel(rightContent); Grid.SetColumn(_right,4);layout.Children.Add(_right);
        _rightSplitter=Splitter("Resize Right Panel"); Grid.SetColumn(_rightSplitter,3);layout.Children.Add(_rightSplitter);
        _panelToggle.Click+=(_,_)=> { SetRightOpen(!_right.IsVisible); SaveLayout(); };
        SetRightOpen(saved.RightOpen);
        var status=new Grid { ColumnDefinitions=new("*,Auto"), RowDefinitions=new("2,Auto"), Margin=new Thickness(0,4,0,0) };
        AutomationProperties.SetName(_progress,"Loading folder");
        Grid.SetColumnSpan(_progress,2); status.Children.Add(_progress);
        _message.Margin=new Thickness(0,4,8,0); _message.Foreground=Brush("#B9BEC8"); Grid.SetRow(_message,1);status.Children.Add(_message);
        _count.Margin=new Thickness(0,4,0,0); Grid.SetRow(_count,1); Grid.SetColumn(_count,1); status.Children.Add(_count);
        Grid.SetRow(status,1);Grid.SetColumnSpan(status,5);layout.Children.Add(status);
        _cancel.Click+=(_,_)=>model.CancelNavigation();
        _back.Click+=async(_,_)=>await model.BackAsync(); _forward.Click+=async(_,_)=>await model.ForwardAsync();
        _go.Click+=async(_,_)=>await model.GoPathAsync(_address.Text ?? "");
        _address.KeyDown+=async(_,e)=> { if(e.Key==Avalonia.Input.Key.Enter) { e.Handled=true; await model.GoPathAsync(_address.Text ?? ""); } };
        _recursive.Click+=async(_,_)=>await model.ToggleRecursiveAsync();
        KeyDown+=async(_,e)=> {
            var primary=(e.KeyModifiers & (Avalonia.Input.KeyModifiers.Meta | Avalonia.Input.KeyModifiers.Control)) != 0;
            if(e.Key==Avalonia.Input.Key.F5 || primary && e.Key==Avalonia.Input.Key.R) { e.Handled=true; await model.RefreshAsync(); }
            else if(primary && e.Key==Avalonia.Input.Key.I) { e.Handled=true; SetRightOpen(!_right.IsVisible); SaveLayout(); }
            else if(primary && e.Key==Avalonia.Input.Key.L) { e.Handled=true; _address.Focus(); _address.SelectAll(); }
            else if((e.KeyModifiers & Avalonia.Input.KeyModifiers.Alt)!=0 && e.Key==Avalonia.Input.Key.Left) { e.Handled=true; await model.BackAsync(); }
            else if((e.KeyModifiers & Avalonia.Input.KeyModifiers.Alt)!=0 && e.Key==Avalonia.Input.Key.Right) { e.Handled=true; await model.ForwardAsync(); }
        };
        _up.Click+=async(_,_)=>await model.UpAsync(); _refresh.Click+=async(_,_)=>await model.RefreshAsync();
        model.PropertyChanged+=(_,_)=> { if(Dispatcher.UIThread.CheckAccess())Update();else Dispatcher.UIThread.Post(Update); };
        Content=layout; Update();
    }
    private FolderRow Folder(BrowserFolderNode node, string ancestry = "")
    {
        var key = ancestry + ">" + node.Key;
        if (_rows.TryGetValue(key, out var retained)) { retained.Node = node with { CanPrefetch=node.CanPrefetch || retained.Node.CanPrefetch }; return retained; }
        var children=new StackPanel { Margin=new Thickness(12,2,0,2), Spacing=0, IsVisible=false };
        var choose=Action(node.Name);
        choose.HorizontalAlignment=HorizontalAlignment.Stretch; choose.HorizontalContentAlignment=HorizontalAlignment.Left;
        choose.Padding=new Thickness(4,0); choose.MinHeight=32; choose.BorderThickness=new Thickness(0); choose.Background=Brush("#171A20");
        var toggle=Action(""); toggle.Content=BrowserPresentation.Icon("right"); toggle.Padding=new Thickness(0); toggle.BorderThickness=new Thickness(0);
        toggle.Background=Brush("#171A20"); toggle.FontSize=18; toggle.IsVisible=true;
        toggle.MinWidth=32; toggle.MinHeight=32; toggle.HorizontalContentAlignment=HorizontalAlignment.Center;
        var row=new Grid { ColumnDefinitions=new("32,*") }; row.Children.Add(toggle); Grid.SetColumn(choose,1);row.Children.Add(choose);
        AutomationProperties.SetName(toggle,$"Expand {node.Name}");
        ToolTip.SetTip(choose,node.AbsolutePath ?? node.Name);
        var state=new FolderRow(key,node,new StackPanel { Spacing=2, Children={row,children} },children,choose,toggle);
        _rows.Add(key,state);
        choose.Click+=async(_,_)=> { await _model.NavigateAsync(state.Node); await LoadRowAsync(state, userInitiated: true); };
        toggle.Click+=async(_,_)=>
        {
            state.Expanded=!state.Expanded;
            await LoadRowAsync(state, userInitiated: true);
        };
        _ = LoadRowAsync(state);
        return state;
    }
    private async Task LoadRowAsync(FolderRow row, bool userInitiated = false)
    {
        if (!userInitiated && !row.Node.CanPrefetch) return;
        var generation=++row.Generation; var tree=_treeGeneration; var epoch=_model.Epoch;
        var folders=await _model.ExpandAsync(row.Node, userInitiated);
        if(generation!=row.Generation || tree!=_treeGeneration || epoch!=_model.Epoch)return;
        row.Folders=folders;
        if (userInitiated && folders.All(folder=>folder.Availability!="unavailable"))
            row.Node=row.Node with { CanPrefetch=true };
        SetFolderContent(row);
        ToolTip.SetTip(row.Choose,row.Node.AbsolutePath ?? $"{row.Node.Name} /{row.Node.RelativeFolder}");
        // Failure is retryable through disclosure; a known leaf has no affordance or filler text.
        row.Toggle.IsVisible=folders.Count>0;
        row.Toggle.Content=BrowserPresentation.Icon(row.Expanded ? "down" : "right");
        AutomationProperties.SetName(row.Toggle,$"{(row.Expanded ? "Collapse" : "Expand")} {row.Node.Name}");
        row.Children.Children.Clear(); row.Children.IsVisible=row.Expanded;
        if(!row.Expanded)return;
        foreach(var folder in folders)
            row.Children.Children.Add(folder.Availability=="unavailable" ? Text(folder.Name,12) : Folder(folder,row.TreeKey).Group);
    }
    private async Task RefreshTreeAsync()
    {
        var tree=++_treeGeneration; var offset=_sideScroll.Offset;
        // Keep row instances and expanded flags, so ScrollViewer and keyboard focus remain stable.
        var previousRows=_rows.Values.ToArray();
        var rootRows=_model.Roots.Select(node => Folder(node)).ToArray();
        _roots.Children.Clear();
        foreach(var row in rootRows)_roots.Children.Add(row.Group);
        await Task.WhenAll(previousRows.Select(row=>LoadRowAsync(row)));
        if (tree == _treeGeneration)
            Dispatcher.UIThread.Post(()=> { if(tree==_treeGeneration) _sideScroll.Offset=offset; },DispatcherPriority.Loaded);
    }
    private void Update()
    {
        _cancel.IsVisible=_model.IsLoading;
        _back.IsEnabled=_model.CanGoBack; _forward.IsEnabled=_model.CanGoForward;
        _recursive.IsEnabled=_model.CanChangeRecursive; _recursive.Content=BrowserPresentation.Icon("scope",_model.Recursive);
        _recursive.Background=Brush(_model.Recursive ? BrowserPresentation.Selection : BrowserPresentation.Raised);
        AutomationProperties.SetHelpText(_recursive,_model.Recursive ? "Include Subfolders on" : "Include Subfolders off");
        _progress.IsVisible=_model.IsLoading;
        _count.Text=$"{_model.Assets.Count:N0} items";
        if (!_address.IsFocused) _address.Text=_model.Location?.AbsolutePath ?? "";
        _up.IsEnabled=_model.CanGoUp; _refresh.IsEnabled=_model.CanRefresh;
        _location.Text=_model.Location?.Name ?? "Choose a location";
        ToolTip.SetTip(_location,_model.Location?.AbsolutePath);
        _message.Text=_model.Message;
        if(!ReferenceEquals(_shownRoots,_model.Roots))
        {
            _shownRoots=_model.Roots;
            if (!_model.IsAvailable) { _treeGeneration++; _rows.Clear(); _roots.Children.Clear(); }
            else _ = RefreshTreeAsync();
        }
        foreach (var row in _rows.Values)
        {
            row.Choose.Background = _model.Location?.Key == row.Node.Key ? Brush(BrowserPresentation.Selection) : Brushes.Transparent;
            SetFolderContent(row);
        }
        _updating=true;
        try
        {
            if(!ReferenceEquals(_shownAssets,_model.Assets))
            {
                var scroll=_assets.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
                var offset=scroll?.Offset;
                _shownAssets=_model.Assets;_assets.ItemsSource=_model.Assets;
                if(scroll is not null && offset is { } previous)
                    Dispatcher.UIThread.Post(()=>scroll.Offset=previous, DispatcherPriority.Loaded);
            }
            _assets.SelectedItem=_model.SelectedAsset;
        }
        finally { _updating=false; }
        _source.Text=_model.SelectedAsset is null ? "Select an item to see its file information." : _model.SourceMessage;
        _summary.Text=_model.SelectedAsset is { } asset ?
            $"{asset.RelativePath.Split('/')[^1]}\n\n{asset.MediaType}\n{asset.FileSizeBytes:N0} bytes\n\nModified (UTC)\n{Modified(asset.LastWriteUtcTicks)}\n\nLocation\n{asset.RelativePath}" : "";
    }
    private void SetFolderContent(FolderRow row)
    {
        var label=Text(row.Node.Name+(row.Node.Availability.Length==0 ? "" : $" · {row.Node.Availability}"),13);
        label.TextWrapping=TextWrapping.NoWrap; label.TextTrimming=TextTrimming.CharacterEllipsis;
        label.VerticalAlignment=VerticalAlignment.Center;
        var content=new Grid { ColumnDefinitions=new("18,*") };
        var icon=BrowserPresentation.Icon("folder",_model.Location?.Key==row.Node.Key);
        icon.VerticalAlignment=VerticalAlignment.Center; content.Children.Add(icon);
        Grid.SetColumn(label,1); label.Margin=new Thickness(8,0,0,0); content.Children.Add(label);
        row.Choose.Content=content; AutomationProperties.SetName(row.Choose,row.Node.Name);
    }
    private GridSplitter Splitter(string label)
    {
        var splitter=new GridSplitter { Width=8, HorizontalAlignment=HorizontalAlignment.Stretch,
            VerticalAlignment=VerticalAlignment.Stretch, ResizeDirection=GridResizeDirection.Columns,
            ResizeBehavior=GridResizeBehavior.PreviousAndNext, Background=Brush(BrowserPresentation.Canvas),
            Focusable=true, KeyboardIncrement=16, DragIncrement=1 };
        AutomationProperties.SetName(splitter,label); AutomationProperties.SetHelpText(splitter,"Drag or use Left and Right arrow keys to resize");
        ToolTip.SetTip(splitter,label); splitter.DragCompleted+=(_,_)=>SaveLayout();
        splitter.KeyUp+=(_,e)=> { if(e.Key is Key.Left or Key.Right) SaveLayout(); };
        splitter.DoubleTapped+=(_,_)=> { ResetLayout(); SaveLayout(); };
        return splitter;
    }
    private void SetRightOpen(bool open)
    {
        if (!open && _right.IsVisible) NormalizeLayout();
        if (!open && _right.IsKeyboardFocusWithin) _panelToggle.Focus();
        _right.IsVisible=_rightSplitter.IsVisible=open;
        _layout.ColumnDefinitions[3].Width=new GridLength(open ? 8 : 0);
        _layout.ColumnDefinitions[4].MinWidth=open ? 200 : 0;
        _layout.ColumnDefinitions[4].Width=new GridLength(open ? _rightWeight : 0,GridUnitType.Star);
        _panelToggle.Content=BrowserPresentation.Icon("panel",open);
        AutomationProperties.SetHelpText(_panelToggle,open ? "Right Panel shown" : "Right Panel hidden");
    }
    private void ResetLayout()
    {
        _layout.ColumnDefinitions[0].Width=new GridLength(.24,GridUnitType.Star);
        _layout.ColumnDefinitions[2].Width=new GridLength(.52,GridUnitType.Star);
        _rightWeight=.24;
        if (_right.IsVisible) _layout.ColumnDefinitions[4].Width=new GridLength(.24,GridUnitType.Star);
    }
    private BrowserLayout NormalizeLayout()
    {
        var left=_layout.ColumnDefinitions[0].Width.Value;
        var center=_layout.ColumnDefinitions[2].Width.Value;
        var right=_right.IsVisible ? _layout.ColumnDefinitions[4].Width.Value : _rightWeight;
        if (!_right.IsVisible)
        {
            var remaining=left+center;
            left=left/remaining*(1-right); center=center/remaining*(1-right);
        }
        var total=left+center+right;
        var result=new BrowserLayout(left/total,center/total,right/total,_right.IsVisible).Validated();
        _layout.ColumnDefinitions[0].Width=new GridLength(result.Navigation,GridUnitType.Star);
        _layout.ColumnDefinitions[2].Width=new GridLength(result.Assets,GridUnitType.Star);
        _rightWeight=result.Right;
        if (_right.IsVisible) _layout.ColumnDefinitions[4].Width=new GridLength(result.Right,GridUnitType.Star);
        return result;
    }
    private void SaveLayout() => LayoutChanged?.Invoke(NormalizeLayout());
    private static string Modified(long ticks) => ticks >= DateTime.MinValue.Ticks && ticks <= DateTime.MaxValue.Ticks
        ? new DateTime(ticks,DateTimeKind.Utc).ToString("yyyy-MM-dd HH:mm:ss") : "Not available";
    private static IBrush Brush(string color)=>new SolidColorBrush(Color.Parse(color));
    private static TextBlock Text(string text,double size)=>new(){Text=text,FontSize=size,TextWrapping=TextWrapping.Wrap};
    private static Button Action(string text)=>new(){Content=text,Padding=new Thickness(8,4),Background=Brush("#1D2129"),Foreground=Brush("#F0F0F2"),BorderBrush=Brush("#3B404A"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(4)};
    private static Border Panel(Control child,bool padding=true)=>new(){Child=child,Padding=new Thickness(padding ? 9 : 0),Background=Brush("#171A20"),BorderBrush=Brush("#2B303A"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(8)};
}
