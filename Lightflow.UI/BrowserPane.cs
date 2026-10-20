using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using LightflowStudio;

namespace Lightflow.UI;

/// <summary>One shared Browser presentation, with application requests only and no filesystem/Catalog dependency.</summary>
public sealed class BrowserPane : UserControl
{
    private readonly BrowserViewModel _model;
    private readonly StackPanel _roots = new() { Spacing = 4 };
    private readonly ListBox _assets = new() { SelectionMode = SelectionMode.Single, Background=Brush("#11141A"), BorderThickness=new Thickness(0) };
    private readonly TextBlock _location=Text("Choose a Media Root",18), _message=Text("",13), _summary=Text("",13), _source=Text("",13);
    private readonly Button _up=Action("↑  Parent"), _refresh=Action("Refresh");
    private IReadOnlyList<BrowserFolderNode>? _shownRoots;
    private IReadOnlyList<MediaAsset>? _shownAssets;
    private bool _updating;
    private readonly List<(BrowserFolderNode Node, Button Button)> _folderButtons = [];
    public BrowserPane(BrowserViewModel model)
    {
        _model=model;
        var layout=new Grid { ColumnDefinitions=new("250,12,*,12,250"), RowDefinitions=new("52,*,64") };
        var heading=Text("Browser",24); heading.VerticalAlignment=VerticalAlignment.Center; layout.Children.Add(heading);
        var toolbar=new StackPanel { Orientation=Orientation.Horizontal, Spacing=10, VerticalAlignment=VerticalAlignment.Center };
        toolbar.Children.Add(_up); toolbar.Children.Add(_refresh); Grid.SetColumn(toolbar,2); layout.Children.Add(toolbar);
        var side=new StackPanel { Spacing=14, Children={Text("MEDIA ROOTS",12), _roots} };
        var sideScroll=new ScrollViewer { Content=side, HorizontalScrollBarVisibility=Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        var left=Panel(sideScroll); Grid.SetRow(left,1); layout.Children.Add(left);
        var center=new Grid { RowDefinitions=new("Auto,36,*") };
        _location.Margin=new Thickness(16,14); center.Children.Add(_location);
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
    private Control Folder(BrowserFolderNode node)
    {
        var children=new StackPanel { Margin=new Thickness(12,2,0,2), Spacing=3, IsVisible=false };
        var choose=Action(node.Name+(node.Availability.Length==0 ? "" : $" · {node.Availability}"));
        choose.Content=Text(node.Name+(node.Availability.Length==0 ? "" : $" · {node.Availability}"),13);
        choose.HorizontalAlignment=HorizontalAlignment.Stretch; choose.HorizontalContentAlignment=HorizontalAlignment.Left;
        choose.Padding=new Thickness(6,8); choose.BorderThickness=new Thickness(0); choose.Background=Brush("#171A20");
        ToolTip.SetTip(choose,$"{node.Name}\nRoot {node.RootId}\n/{node.RelativeFolder}");
        choose.Click+=async(_,_)=>await _model.NavigateAsync(node);
        _folderButtons.Add((node,choose));
        var toggle=Action("›"); toggle.Padding=new Thickness(0); toggle.BorderThickness=new Thickness(0);
        toggle.Background=Brush("#171A20"); toggle.FontSize=18; toggle.HorizontalAlignment=HorizontalAlignment.Stretch;
        AutomationProperties.SetName(toggle,$"Expand {node.Name}");
        var row=new Grid { ColumnDefinitions=new("24,*") };row.Children.Add(toggle);Grid.SetColumn(choose,1);row.Children.Add(choose);
        var group=new StackPanel { Spacing=2, Children={row,children} };
        var expanded=false;
        toggle.Click+=async(_,_)=>
        {
            expanded=!expanded;children.IsVisible=expanded;toggle.Content=expanded ? "⌄" : "›";
            AutomationProperties.SetName(toggle,$"{(expanded ? "Collapse" : "Expand")} {node.Name}");
            if(!expanded)return;
            var epoch=_model.Epoch;
            _folderButtons.RemoveAll(child => child.Node.RootId == node.RootId && child.Node.RelativeFolder != node.RelativeFolder &&
                (node.RelativeFolder.Length == 0 || child.Node.RelativeFolder.StartsWith(node.RelativeFolder + "/", StringComparison.Ordinal)));
            children.Children.Clear();children.Children.Add(Text("Loading folders…",12));toggle.IsEnabled=false;
            var folders=await _model.ExpandAsync(node);
            toggle.IsEnabled=true;if(epoch!=_model.Epoch)return;
            children.Children.Clear();
            foreach(var folder in folders)children.Children.Add(folder.Availability=="unavailable" ? Text(folder.Name,12) : Folder(folder));
            if(folders.Count==0)children.Children.Add(Text("No subfolders",12));
        };
        return group;
    }
    private void Update()
    {
        _up.IsEnabled=_model.CanGoUp; _refresh.IsEnabled=_model.CanRefresh;
        _location.Text=_model.Location is { } node ? $"{node.Name}\n/{node.RelativeFolder}" : "Choose a Media Root";
        _message.Text=_model.Message;
        if(!ReferenceEquals(_shownRoots,_model.Roots))
        {
            _shownRoots=_model.Roots; _roots.Children.Clear(); _folderButtons.Clear();
            foreach(var root in _model.Roots)_roots.Children.Add(Folder(root));
            if(_model.Roots.Count==0)_roots.Children.Add(Text("No Media Roots",13));
        }
        foreach (var folder in _folderButtons)
            folder.Button.Background = _model.Location is { } location && location.RootId == folder.Node.RootId && location.RelativeFolder == folder.Node.RelativeFolder
                ? Brush("#553321") : Brush("#171A20");
        _updating=true;
        try
        {
            if(!ReferenceEquals(_shownAssets,_model.Assets)){_shownAssets=_model.Assets;_assets.ItemsSource=_model.Assets;}
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
