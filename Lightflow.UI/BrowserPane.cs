using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LightflowStudio;

namespace Lightflow.UI;

/// <summary>One shared Browser presentation, with application requests only and no filesystem/Catalog dependency.</summary>
public sealed class BrowserPane : UserControl
{
    private readonly BrowserViewModel _model;
    private readonly StackPanel _roots = new() { Spacing = 4 };
    private readonly ListBox _assets = new() { SelectionMode = SelectionMode.Single, Background=Brush("#11141A"), BorderThickness=new Thickness(0) };
    private readonly TextBlock _location=Text("Choose a location",18), _message=Text("",13), _summary=Text("",13), _source=Text("",13);
    private readonly TextBlock _path=Text("",12);
    private readonly Button _up=Action("↑  Parent"), _refresh=Action("Refresh");
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
    public BrowserPane(BrowserViewModel model)
    {
        _model=model;
        var layout=new Grid { ColumnDefinitions=new("250,12,*,12,250"), RowDefinitions=new("52,*,64") };
        var heading=Text("Browser",24); heading.VerticalAlignment=VerticalAlignment.Center; layout.Children.Add(heading);
        var toolbar=new StackPanel { Orientation=Orientation.Horizontal, Spacing=10, VerticalAlignment=VerticalAlignment.Center };
        toolbar.Children.Add(_up); toolbar.Children.Add(_refresh); Grid.SetColumn(toolbar,2); layout.Children.Add(toolbar);
        var side=new StackPanel { Spacing=14, Children={Text("LOCATIONS",12), _roots} };
        _sideScroll=new ScrollViewer { Content=side, HorizontalScrollBarVisibility=Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        var left=Panel(_sideScroll); Grid.SetRow(left,1); layout.Children.Add(left);
        var center=new Grid { RowDefinitions=new("Auto,36,*") };
        _path.TextWrapping=TextWrapping.NoWrap; _path.TextTrimming=TextTrimming.CharacterEllipsis; _path.Foreground=Brush("#9DA5B3");
        var locationHeading=new StackPanel { Spacing=6, Margin=new Thickness(16,14), Children={_location,_path} }; center.Children.Add(locationHeading);
        var columns=Text("CATALOG ASSETS",11); columns.Margin=new Thickness(16,4); columns.Foreground=Brush("#9DA5B3"); Grid.SetRow(columns,1);center.Children.Add(columns);
        _assets.ItemTemplate=new FuncDataTemplate<MediaAsset>((asset,_)=>
        {
            if(asset is null)return new TextBlock();
            var row=new StackPanel { Spacing=5, Margin=new Thickness(8,6) };
            row.Children.Add(Text(asset.RelativePath.Split('/')[^1],15));
            var detail=Text($"{asset.RelativePath}  ·  {asset.MediaType}  ·  {asset.FileSizeBytes:N0} bytes",12); detail.Foreground=Brush("#B9BEC8"); row.Children.Add(detail);
            AutomationProperties.SetName(row,$"{asset.RelativePath}, {asset.MediaType}, Catalog asset {asset.AssetId}");
            return row;
        });
        _assets.Styles.Add(new Style(selector=>selector.OfType<ListBoxItem>().Class(":selected")) { Setters={new Setter(BackgroundProperty,Brush("#553321")),new Setter(BorderBrushProperty,Brush("#E68B5B")),new Setter(BorderThicknessProperty,new Thickness(1))} });
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
        Grid.SetRow(_assets,2);center.Children.Add(_assets); var main=Panel(center,false); Grid.SetRow(main,1);Grid.SetColumn(main,2);layout.Children.Add(main);
        var details=new StackPanel { Spacing=18, Children={Text("SELECTED ASSET",12),_source,_summary} };
        _source.Foreground=Brush("#DFA273"); var right=Panel(new ScrollViewer { Content=details }); Grid.SetRow(right,1);Grid.SetColumn(right,4);layout.Children.Add(right);
        _message.Margin=new Thickness(0,14,0,0); _message.Foreground=Brush("#B9BEC8"); Grid.SetRow(_message,2);Grid.SetColumnSpan(_message,5);layout.Children.Add(_message);
        _up.Click+=async(_,_)=>await model.UpAsync(); _refresh.Click+=async(_,_)=>await model.RefreshAsync();
        model.PropertyChanged+=(_,_)=> { if(Dispatcher.UIThread.CheckAccess())Update();else Dispatcher.UIThread.Post(Update); };
        Content=layout; Update();
    }
    private FolderRow Folder(BrowserFolderNode node, string ancestry = "")
    {
        var key = ancestry + ">" + node.Key;
        if (_rows.TryGetValue(key, out var retained)) { retained.Node = node with { CanPrefetch=node.CanPrefetch || retained.Node.CanPrefetch }; return retained; }
        var children=new StackPanel { Margin=new Thickness(12,2,0,2), Spacing=3, IsVisible=false };
        var choose=Action(node.Name);
        choose.HorizontalAlignment=HorizontalAlignment.Stretch; choose.HorizontalContentAlignment=HorizontalAlignment.Left;
        choose.Padding=new Thickness(6,8); choose.BorderThickness=new Thickness(0); choose.Background=Brush("#171A20");
        var toggle=Action("›"); toggle.Padding=new Thickness(0); toggle.BorderThickness=new Thickness(0);
        toggle.Background=Brush("#171A20"); toggle.FontSize=18; toggle.IsVisible=true;
        var row=new Grid { ColumnDefinitions=new("24,*") }; row.Children.Add(toggle); Grid.SetColumn(choose,1);row.Children.Add(choose);
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
        row.Choose.Content=Text(row.Node.Name+(row.Node.Availability.Length==0 ? "" : $" · {row.Node.Availability}"),13);
        ToolTip.SetTip(row.Choose,row.Node.AbsolutePath ?? $"{row.Node.Name} /{row.Node.RelativeFolder}");
        // Failure is retryable through disclosure; a known leaf has no affordance or filler text.
        row.Toggle.IsVisible=folders.Count>0;
        row.Toggle.Content=row.Expanded ? "⌄" : "›";
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
        _up.IsEnabled=_model.CanGoUp; _refresh.IsEnabled=_model.CanRefresh;
        _location.Text=_model.Location?.Name ?? "Choose a location";
        _path.Text=_model.Location is { } node ? node.AbsolutePath ?? "/" + node.RelativeFolder : "";
        ToolTip.SetTip(_path,_path.Text);
        _message.Text=_model.Message;
        if(!ReferenceEquals(_shownRoots,_model.Roots))
        {
            _shownRoots=_model.Roots;
            if (!_model.IsAvailable) { _treeGeneration++; _rows.Clear(); _roots.Children.Clear(); }
            else _ = RefreshTreeAsync();
        }
        foreach (var row in _rows.Values)
            row.Choose.Background = _model.Location?.Key == row.Node.Key ? Brush("#553321") : Brush("#171A20");
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
        _source.Text=_model.SourceMessage;
        _summary.Text=_model.SelectedAsset is { } asset ?
            $"{asset.RelativePath.Split('/')[^1]}\n\n{asset.MediaType}\n{asset.FileSizeBytes:N0} bytes\n\nRecorded file modification (UTC)\n{Modified(asset.LastWriteUtcTicks)}\n\nLocation\n{asset.RelativePath}\n\nAsset ID\n{asset.AssetId}" : "";
    }
    private static string Modified(long ticks) => ticks >= DateTime.MinValue.Ticks && ticks <= DateTime.MaxValue.Ticks
        ? new DateTime(ticks,DateTimeKind.Utc).ToString("yyyy-MM-dd HH:mm:ss") : "Not available";
    private static IBrush Brush(string color)=>new SolidColorBrush(Color.Parse(color));
    private static TextBlock Text(string text,double size)=>new(){Text=text,FontSize=size,TextWrapping=TextWrapping.Wrap};
    private static Button Action(string text)=>new(){Content=text,Padding=new Thickness(12,7),Background=Brush("#1D2129"),Foreground=Brush("#F0F0F2"),BorderBrush=Brush("#3B404A"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(5)};
    private static Border Panel(Control child,bool padding=true)=>new(){Child=child,Padding=new Thickness(padding ? 14 : 0),Background=Brush("#171A20"),BorderBrush=Brush("#2B303A"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(7)};
}
