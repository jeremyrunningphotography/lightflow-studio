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
using Lightflow.Application;

namespace Lightflow.UI;

/// <summary>One shared Browser presentation, with application requests only and no filesystem/Catalog dependency.</summary>
public sealed partial class BrowserPane : UserControl
{
    private readonly BrowserViewModel _model;
    private readonly StackPanel _roots = new() { Spacing = 0 };
    private readonly Grid _layout;
    private readonly Control _right;
    private readonly GridSplitter _rightSplitter;
    private readonly Button _panelToggle = Action("Right Panel");
    private readonly TextBlock _count = Text("","status");
    private readonly ProgressBar _progress = new() { IsIndeterminate=true, Height=2, Classes={ "lf-progress" } };
    private BrowserLayout _preferred;
    private bool _applyingLayout;
    public Button RightPanelToggle => _panelToggle;
    public Control StatusView { get; }
    public bool RightPanelOpen => _preferred.RightOpen;
    public event Action<BrowserLayout>? LayoutChanged;
    private readonly ListBox _assets = new() { SelectionMode = SelectionMode.Single, Classes={ "lf-assets" } };
    private readonly TextBlock _location=Text("Choose a location","location-heading"), _message=Text("","status"), _summary=Text(""), _source=Text("");
    private readonly ToggleButton _recursive=new() {Classes={"lf-button","scope"}};
    private readonly Avalonia.Controls.Shapes.Path _scopeIcon=BrowserPresentation.Icon("folder");
    private readonly Button _back=Action("←"), _forward=Action("→"), _up=Action("↑"), _refresh=Action("↻"), _go=Action("Go"), _cancel=Action("Cancel");
    private readonly TextBox _address = new() { Watermark="Folder path", MinWidth=100, VerticalAlignment=VerticalAlignment.Center };
    private IReadOnlyList<BrowserFolderNode>? _shownRoots;
    private IReadOnlyList<MediaAsset>? _shownAssets;
    private bool _updating;
    private string? _displayedLocation;
    private sealed class FolderRow(string treeKey, BrowserFolderNode node, StackPanel group, StackPanel children, Button choose, Button toggle)
    {
        public string TreeKey = treeKey;
        public BrowserFolderNode Node = node;
        public StackPanel Group = group, Children = children;
        public Button Choose = choose, Toggle = toggle;
        public TextBlock Label = null!;
        public Avalonia.Controls.Shapes.Path Icon = null!;
        public string IconKind = "";
        public bool Expanded;
        public long Generation;
        public IReadOnlyList<BrowserFolderNode> Folders = [];
        public bool ChildrenLoaded, RevealOnly;
        public CancellationToken RevealToken;
        public Task Loading = Task.CompletedTask;
    }
    private readonly Dictionary<string, FolderRow> _rows = [];
    private sealed record LocationGroup(StackPanel View, StackPanel Children, Action? Reveal = null);
    private readonly Dictionary<string, LocationGroup> _groups = [];
    private LocationGroup Group(string key, string name, bool server = false)
    {
        if (_groups.TryGetValue(key, out var retained)) return retained;
        var children = new StackPanel { Spacing=0, Margin=new Thickness(server ? 12 : 0,0,0,4) };
        var label=Text(name, server ? "navigation" : "muted");
        label.TextWrapping=TextWrapping.NoWrap; label.TextTrimming=TextTrimming.CharacterEllipsis;
        label.VerticalAlignment=VerticalAlignment.Center;
        var view=new StackPanel { Spacing=0 };
        Action? reveal=null;
        if(!server)
        {
            // Permanent section headings have no disclosure, input handler or collapse state.
            label.Classes.Add("location-section-heading");
            AutomationProperties.SetName(label,name);
            view.Margin=new Thickness(0,4,0,4);
            if(key!="section:"+BrowserLocationSection.Favorites)
                view.Children.Add(new Border { Height=1, Margin=new Thickness(0,2,0,4), Classes={"lf-location-separator"} });
            view.Children.Add(new Border { Child=label, Classes={"lf-location-heading"} });
        }
        else
        {
            // A verified server remains an expandable organizational tree parent.
            var header=new Grid { ColumnDefinitions=new("24,24,*"), MinHeight=32 };
            var disclosure=BrowserPresentation.Disclosure(expanded:true);
            disclosure.VerticalAlignment=VerticalAlignment.Center; header.Children.Add(disclosure);
            var icon=BrowserPresentation.Icon("server"); icon.VerticalAlignment=VerticalAlignment.Center;
            Grid.SetColumn(icon,1); header.Children.Add(icon); Grid.SetColumn(label,2); header.Children.Add(label);
            var button=Action(""); button.Content=header; button.Classes.Add("quiet");
            button.HorizontalAlignment=HorizontalAlignment.Stretch; button.HorizontalContentAlignment=HorizontalAlignment.Stretch;
            ToolTip.SetTip(button,name); AutomationProperties.SetName(button,$"Collapse {name}");
            var expanded=true;
            reveal=()=> { expanded=true; children.IsVisible=true; disclosure.Data=BrowserPresentation.Icon("down").Data; AutomationProperties.SetName(button,$"Collapse {name}"); AutomationProperties.SetHelpText(button,"Expanded"); };
            button.Click+=(_,_)=>
            {
                CancelRestoredFolderRevealForUser();
                expanded=!expanded; children.IsVisible=expanded;
                disclosure.Data=BrowserPresentation.Icon(expanded ? "down" : "right").Data;
                AutomationProperties.SetName(button,$"{(expanded ? "Collapse" : "Expand")} {name}");
                AutomationProperties.SetHelpText(button,expanded ? "Expanded" : "Collapsed");
            };
            view.Children.Add(button);
        }
        view.Children.Add(children);
        var group=new LocationGroup(view,children,reveal); _groups.Add(key,group);
        return group;
    }
    private static void Reconcile(StackPanel panel, IReadOnlyList<Control> desired)
    {
        // Retain visual children, focus and scroll anchors when their keys survive.
        for(var i=panel.Children.Count-1;i>=0;i--)
            if(!desired.Contains(panel.Children[i]))panel.Children.RemoveAt(i);
        for(var i=0;i<desired.Count;i++)
        {
            if(i<panel.Children.Count && ReferenceEquals(panel.Children[i],desired[i]))continue;
            if(desired[i].Parent is StackPanel previous && !ReferenceEquals(previous,panel)) previous.Children.Remove(desired[i]);
            panel.Children.Remove(desired[i]); panel.Children.Insert(i,desired[i]);
        }
    }
    private readonly ScrollViewer _sideScroll;
    private long _treeGeneration, _rootGeneration, _shownFolderRefreshRevision;
    public BrowserPane(BrowserViewModel model, BrowserLayout? savedLayout = null)
    {
        _model=model;
        InitializeThumbnailDemand();
        var saved=(savedLayout ?? new()).Validated();
        _preferred=saved;
        _columnState.Set(BrowserDetailsLayout.Normalize(saved.DetailsColumns));
        _cardMode=saved.CardMode;
        model.RestoreLocation=saved.LastLocation;
        model.LocationRemembered+=bookmark=> { _preferred=_preferred with { LastLocation=bookmark }; LayoutChanged?.Invoke(_preferred); };
        var layout=_layout=new Grid { ColumnDefinitions=new("280,8,*,0,0") };
        var side=new StackPanel { Spacing=6, Children={Text("Locations","panel-heading"), _roots} };
        _sideScroll=new ScrollViewer { Content=side, HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled };
        var left=Panel(_sideScroll); layout.Children.Add(left);
        var navigationSplitter=Splitter("Resize folder navigation"); Grid.SetColumn(navigationSplitter,1); layout.Children.Add(navigationSplitter);
        var center=new Grid { RowDefinitions=new("Auto,4,*") };
        var toolbar=new Grid { ColumnDefinitions=new("Auto,Auto,Auto,*,Auto,Auto"), Margin=new Thickness(0,0,0,4) };
        var controls = new Control[] { _back, _forward, _up, _address, _go, _refresh };
        for (var i=0; i<controls.Length; i++) { Grid.SetColumn(controls[i],i); controls[i].Margin=new Thickness(1,0); toolbar.Children.Add(controls[i]); }
        foreach (var (button, icon, label) in new[] { (_back,"back","Back"), (_forward,"forward","Forward"), (_up,"up","Up one folder"), (_refresh,"refresh","Refresh current folder"), (_cancel,"cancel","Stop loading"), (_panelToggle,"panel","Toggle Right Panel") })
        { button.Content=BrowserPresentation.Icon(icon); button.Width=32; button.Classes.Add("quiet"); ToolTip.SetTip(button,label); AutomationProperties.SetName(button,label); }
        _recursive.Content=new StackPanel {Orientation=Orientation.Horizontal,Spacing=6,Children={_scopeIcon,Text("Subfolders","scope")}};
        _recursive.Padding=new Thickness(5,4); _recursive.FontSize=12;
        AutomationProperties.SetName(_recursive,"Include Subfolders");
        ToolTip.SetTip(_go,"Open folder path"); AutomationProperties.SetName(_go,"Open folder path");
        _address.MinWidth=70; _address.Height=32; _address.Classes.Add("lf-input");
        AutomationProperties.SetName(_address,"Current folder path");
        var scope=new Grid { ColumnDefinitions=new("Auto,*,Auto") };
        _location.TextTrimming=TextTrimming.CharacterEllipsis; _location.TextWrapping=TextWrapping.NoWrap;
        _location.VerticalAlignment=VerticalAlignment.Center; Grid.SetColumn(_location,1); scope.Children.Add(_location);
        scope.Children.Add(CreateLayoutControls());
        Grid.SetColumn(_recursive,2); scope.Children.Add(_recursive);

        var toolbarContent=new StackPanel { Children={toolbar,scope} };
        var toolbarPanel=Panel(toolbarContent);toolbarPanel.Classes.Add("toolbar");center.Children.Add(toolbarPanel);
        var media=CreateCardGrid();
        var mediaPanel=Panel(media,false); Grid.SetRow(mediaPanel,2); center.Children.Add(mediaPanel); Grid.SetColumn(center,2);layout.Children.Add(center);
        var details=new StackPanel { Spacing=12, Children={_source,_summary} };
        var rightContent=new Grid { RowDefinitions=new("32,*") };
        var rightTitle=Text("Selection","panel-heading");
        rightTitle.VerticalAlignment=VerticalAlignment.Center; rightContent.Children.Add(rightTitle);
        var detailsScroll=new ScrollViewer { Content=details, HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled };
        Grid.SetRow(detailsScroll,1); rightContent.Children.Add(detailsScroll);
        _right=Panel(rightContent); Grid.SetColumn(_right,4);layout.Children.Add(_right);
        _rightSplitter=Splitter("Resize Right Panel"); Grid.SetColumn(_rightSplitter,3);layout.Children.Add(_rightSplitter);
        _panelToggle.Click+=(_,_)=>ToggleRightPanel();
        _right.IsVisible=_rightSplitter.IsVisible=saved.RightOpen;
        var status=new Grid { ColumnDefinitions=new("Auto,*,Auto,Auto,Auto"), VerticalAlignment=VerticalAlignment.Center };
        _progress.Width=28; _progress.VerticalAlignment=VerticalAlignment.Center;
        AutomationProperties.SetName(_progress,"Loading folder"); status.Children.Add(_progress);
        _message.Margin=new Thickness(8,0);
        _message.TextWrapping=TextWrapping.NoWrap; _message.TextTrimming=TextTrimming.CharacterEllipsis;
        Grid.SetColumn(_message,1);status.Children.Add(_message);
        _cancel.Margin=new Thickness(4,0); _cancel.Height=26; Grid.SetColumn(_cancel,2);status.Children.Add(_cancel);
        _count.Margin=new Thickness(8,0,0,0); _count.VerticalAlignment=VerticalAlignment.Center;
        Grid.SetColumn(_count,3); status.Children.Add(_count);
        var presentationControls = CreatePresentationControls();
        presentationControls.Margin = new Thickness(14,0,0,0);
        Grid.SetColumn(presentationControls,4); status.Children.Add(presentationControls);
        StatusView=status;
        _cancel.Click+=(_,_)=>model.CancelNavigation();
        _back.Click+=async(_,_)=>await NavigateFromToolbarAsync(model.BackAsync);
        _forward.Click+=async(_,_)=>await NavigateFromToolbarAsync(model.ForwardAsync);
        _go.Click+=async(_,_)=>await SubmitAddressAsync();
        _address.KeyDown+=async(_,e)=>
        {
            if(e.KeyModifiers!=KeyModifiers.None)return;
            if(e.Key==Key.Enter) { e.Handled=true; await SubmitAddressAsync(); }
            else if(e.Key==Key.Escape) { e.Handled=true; RestoreAddress(); _address.SelectAll(); }
        };
        _recursive.Click+=async(_,_)=>await model.ToggleRecursiveAsync();
        _up.Click+=async(_,_)=>await NavigateFromToolbarAsync(model.UpAsync);
        _refresh.Click+=async(_,_)=>await NavigateFromToolbarAsync(model.RefreshAsync);
        model.PropertyChanged+=(_,_)=> { if(Dispatcher.UIThread.CheckAccess())Update();else Dispatcher.UIThread.Post(Update); };
        layout.SizeChanged+=(_,_)=>ApplyPaneLayout();
        DetachedFromVisualTree+=(_,_)=> { _treeReveal?.Cancel();CancelColumnDrag(); };
        Content=layout; ApplyPaneLayout(); Update();
    }
    private FolderRow Folder(BrowserFolderNode node, string ancestry = "", bool allowPrefetch = true)
    {
        var key = ancestry + ">" + node.Key;
        if (_rows.TryGetValue(key, out var retained)) { retained.Node = node with { CanPrefetch=(allowPrefetch && node.CanPrefetch) || retained.Node.CanPrefetch }; SetFolderContent(retained); return retained; }
        // Automatically revealing a path must not authorize later prefetch of its unvisited siblings.
        if(!allowPrefetch)node=node with {CanPrefetch=false};
        var children=new StackPanel { Margin=new Thickness(12,2,0,2), Spacing=0, IsVisible=false };
        var choose=Action(node.Name);
        choose.HorizontalAlignment=HorizontalAlignment.Stretch; choose.HorizontalContentAlignment=HorizontalAlignment.Left;
        choose.Classes.Add("quiet");choose.Classes.Add("navigation");
        var toggle=Action(""); toggle.Content=BrowserPresentation.Disclosure(); toggle.Classes.Add("quiet");toggle.Classes.Add("disclosure");
        toggle.IsVisible=true;
        toggle.MinWidth=32; toggle.MinHeight=32; toggle.HorizontalContentAlignment=HorizontalAlignment.Center;
        var row=new Grid { ColumnDefinitions=new("32,*") }; row.Children.Add(toggle); Grid.SetColumn(choose,1);row.Children.Add(choose);
        AutomationProperties.SetName(toggle,$"Expand {node.Name}");
        ToolTip.SetTip(choose,node.AbsolutePath ?? node.Name);
        var state=new FolderRow(key,node,new StackPanel { Spacing=2, Children={row,children} },children,choose,toggle);
        _rows.Add(key,state);
        SetFolderContent(state);
        choose.Click+=async(_,_)=> { await _model.NavigateAsync(state.Node); await LoadRowAsync(state, userInitiated: true); };
        toggle.Click+=async(_,_)=>
        {
            CancelRestoredFolderRevealForUser();
            state.Expanded=!state.Expanded;
            state.Children.IsVisible=state.Expanded;
            SetDisclosure(state);
            if(state.Expanded) await LoadRowAsync(state, userInitiated: true);
        };
        if(allowPrefetch)_ = LoadRowAsync(state);
        return state;
    }
    private Task LoadRowAsync(FolderRow row, bool userInitiated = false, bool revealing = false, CancellationToken token = default)
    {
        if(revealing) { row.RevealOnly=true;row.RevealToken=token; }
        else row.RevealOnly=false;
        if(!revealing && !userInitiated && !row.Node.CanPrefetch)return Task.CompletedTask;
        if(revealing && !row.Loading.IsCompleted)return row.Loading.WaitAsync(token);
        if(revealing && row.ChildrenLoaded) { ShowFolderChildren(row,allowPrefetch:false);return Task.CompletedTask; }
        return row.Loading=LoadFolderRowAsync(row,userInitiated,revealing,token);
    }
    private async Task LoadFolderRowAsync(FolderRow row,bool userInitiated,bool revealing,CancellationToken token)
    {
        var generation=++row.Generation; var tree=_treeGeneration; var epoch=_model.Epoch;
        var folders=await _model.ExpandAsync(row.Node,userInitiated,token).WaitAsync(token);
        if(token.IsCancellationRequested || (row.RevealOnly && row.RevealToken.IsCancellationRequested) || generation!=row.Generation || tree!=_treeGeneration || epoch!=_model.Epoch)return;
        row.Folders=folders;
        row.ChildrenLoaded=folders.All(folder=>folder.Availability!="unavailable");
        if (userInitiated && row.ChildrenLoaded)row.Node=row.Node with { CanPrefetch=true };
        SetFolderContent(row);
        ToolTip.SetTip(row.Choose,row.Node.AbsolutePath ?? $"{row.Node.Name} /{row.Node.RelativeFolder}");
        row.Toggle.IsVisible=folders.Count>0;
        SetDisclosure(row);
        row.Children.IsVisible=row.Expanded;
        ShowFolderChildren(row,allowPrefetch:!revealing);
    }
    private void ShowFolderChildren(FolderRow row,bool allowPrefetch)
    {
        row.Children.IsVisible=row.Expanded;SetDisclosure(row);
        if(!row.Expanded)return;
        allowPrefetch &= !row.RevealOnly;
        var desired=row.Folders.Select(folder=>folder.Availability=="unavailable"
            ? (Control)Text(folder.Name,"muted") : Folder(folder,row.TreeKey,allowPrefetch).Group).ToArray();
        Reconcile(row.Children,desired);
        var liveKeys=row.Folders.Where(folder=>folder.Availability!="unavailable").Select(folder=>row.TreeKey+">"+folder.Key).ToArray();
        foreach(var old in _rows.Values.Where(old=>old.TreeKey.StartsWith(row.TreeKey+">",StringComparison.Ordinal) &&
            !liveKeys.Any(key=>old.TreeKey==key || old.TreeKey.StartsWith(key+">",StringComparison.Ordinal))).ToArray())
        { old.Generation++; _rows.Remove(old.TreeKey); }
    }
    private static void SetDisclosure(FolderRow row)
    {
        ((Avalonia.Controls.Shapes.Path)row.Toggle.Content!).Data=BrowserPresentation.Icon(row.Expanded ? "down" : "right").Data;
        AutomationProperties.SetName(row.Toggle,$"{(row.Expanded ? "Collapse" : "Expand")} {row.Node.Name}");
        AutomationProperties.SetHelpText(row.Toggle,row.Expanded ? "Expanded" : "Collapsed");
    }
    private async Task RefreshTreeAsync(bool refreshFolders)
    {
        var rootRevision=++_rootGeneration;
        if(refreshFolders)_treeGeneration++;
        var offset=_sideScroll.Offset;
        var previousRows=_rows.Values.ToArray();
        var rootRows=_model.Roots.Select(node => Folder(node)).ToArray();
        var desired=rootRows.Where(row=>row.Node.Section==BrowserLocationSection.None).Select(row=>(Control)row.Group).ToList();
        if(_model.Roots.Any(node=>node.Section!=BrowserLocationSection.None))
        {
            foreach(var (section,name) in new[] { (BrowserLocationSection.Favorites,"Favorites"),
                (BrowserLocationSection.Devices,"Devices & Volumes"), (BrowserLocationSection.Network,"Network") })
            {
                var group=Group("section:"+section,name);
                var entries=new List<Control>();
                var sectionRows=rootRows.Where(row=>row.Node.Section==section).ToArray();
                foreach(var row in sectionRows.Where(row=>row.Node.Server is null))entries.Add(row.Group);
                foreach(var server in sectionRows.Where(row=>row.Node.Server is not null).GroupBy(row=>row.Node.Server!,StringComparer.OrdinalIgnoreCase))
                {
                    var parent=Group("server:"+server.Key,server.Key,true);
                    Reconcile(parent.Children,server.Select(row=>(Control)row.Group).ToArray()); entries.Add(parent.View);
                }
                if(section==BrowserLocationSection.Network && entries.Count==0)
                    entries.Add(Text("No mounted network shares","muted"));
                Reconcile(group.Children,entries); desired.Add(group.View);
            }
        }

        Reconcile(_roots,desired);
        // Disconnected roots and their old asynchronous rows cannot publish back into the live list.
        bool Live(FolderRow row) => rootRows.Any(root=>row.TreeKey==root.TreeKey || row.TreeKey.StartsWith(root.TreeKey+">",StringComparison.Ordinal));
        foreach(var row in previousRows.Where(row=>!Live(row))) { row.Generation++; _rows.Remove(row.TreeKey); }
        if(refreshFolders)
            await Task.WhenAll(previousRows.Where(row=>Live(row) && _rows.ContainsKey(row.TreeKey)).Select(row=>LoadRowAsync(row)));
        if (rootRevision == _rootGeneration)
            Dispatcher.UIThread.Post(()=> { if(rootRevision==_rootGeneration) _sideScroll.Offset=offset; },DispatcherPriority.Loaded);
    }
    private void Update()
    {
        if (!_model.IsLoading && _cancel.IsKeyboardFocusWithin) _refresh.Focus(NavigationMethod.Tab);
        _cancel.IsVisible=_model.IsLoading;
        _back.IsEnabled=_model.CanGoBack; _forward.IsEnabled=_model.CanGoForward;
        _recursive.IsEnabled=_model.CanChangeRecursive; _recursive.IsChecked=_model.Recursive;
        LightflowTheme.State(_scopeIcon,"active",_model.Recursive);
        ToolTip.SetTip(_recursive,_model.HasNoSubfolders && !_model.IsLoading
            ? "No subfolders" : "Include Subfolders — browse this folder and every descendant folder as one media set");
        LightflowTheme.State(_recursive,"active",_model.Recursive);
        AutomationProperties.SetHelpText(_recursive,_model.Recursive ? "Include Subfolders on" : "Include Subfolders off");
        _progress.IsVisible=_model.IsLoading;
        _count.Text=$"{_model.Assets.Count:N0} {(_model.Assets.Count==1 ? "item" : "items")}"+(_model.SelectedAssetKeys.Count == 0 ? "" : $" · {_model.SelectedAssetKeys.Count:N0} selected");
        var currentPath=_model.Location?.AbsolutePath ?? "";
        // Focus is not evidence of an edit. Only navigation replaces an uncommitted draft;
        // selection/progress publications must leave intentional text alone.
        if (_displayedLocation!=currentPath) RestoreAddress();
        _up.IsEnabled=_model.CanGoUp; _refresh.IsEnabled=_model.CanRefresh;
        _location.Text=_model.Location?.Name ?? "Choose a location";
        ToolTip.SetTip(_location,_model.Location?.AbsolutePath);
        UpdateBrowserMessage();
        if(!ReferenceEquals(_shownRoots,_model.Roots))
        {
            var refreshFolders=_shownFolderRefreshRevision!=_model.FolderRefreshRevision;
            _shownFolderRefreshRevision=_model.FolderRefreshRevision;
            _shownRoots=_model.Roots;
            if (!_model.IsAvailable) { _treeGeneration++; _rows.Clear(); _groups.Clear(); _roots.Children.Clear(); }
            else _treeRefresh=RefreshTreeAsync(refreshFolders);
        }
        RequestRestoredFolderReveal();
        foreach (var row in _rows.Values)
        {
            LightflowTheme.State(row.Choose,"active",_model.Location?.Key==row.Node.Key);
            SetFolderContent(row);
        }
        _updating=true;
        try
        {
            UpdateCardRows();
        }
        finally { _updating=false; }
        _source.Text=_model.SelectedAsset is null ? "Select an item to see its file information." : _model.SourceMessage;
        _summary.Text=_model.SelectedAsset is { } asset ?
            $"{asset.RelativePath.Split('/')[^1]}\n\n{asset.MediaType}\n{asset.FileSizeBytes:N0} bytes\n\nModified (UTC)\n{Modified(asset.LastWriteUtcTicks)}\n\nLocation\n{asset.RelativePath}" : "";
    }
    private void SetFolderContent(FolderRow row)
    {
        LightflowTheme.State(row.Choose,"active",_model.Location?.Key==row.Node.Key);
        var kind=row.Node.Kind switch
        {
            BrowserLocationKind.Home=>"home", BrowserLocationKind.Desktop=>"desktop",
            BrowserLocationKind.Documents=>"documents", BrowserLocationKind.Downloads=>"downloads",
            BrowserLocationKind.SystemVolume or BrowserLocationKind.LocalVolume=>"disk",
            BrowserLocationKind.ExternalVolume=>"external", BrowserLocationKind.NetworkShare=>"share", _=>"folder"
        };
        var recursive=_model.IsRecursiveScope(row.Node);
        // Scope uses Windows' filled folder silhouette; native device/server glyphs remain semantic.
        if(recursive && row.Node.Kind is BrowserLocationKind.Home or BrowserLocationKind.Desktop or BrowserLocationKind.Documents or BrowserLocationKind.Downloads)kind="folder";
        if(row.Label is null)
        {
            row.Label=Text("","navigation"); row.Label.TextWrapping=TextWrapping.NoWrap;
            row.Label.TextTrimming=TextTrimming.CharacterEllipsis; row.Label.VerticalAlignment=VerticalAlignment.Center;
            row.Label.Margin=new Thickness(8,0,0,0);
            row.Icon=BrowserPresentation.Icon(kind); row.IconKind=kind; row.Icon.VerticalAlignment=VerticalAlignment.Center;
            var content=new Grid { ColumnDefinitions=new("18,*") };
            content.Children.Add(row.Icon); Grid.SetColumn(row.Label,1); content.Children.Add(row.Label);
            row.Choose.Content=content;
        }
        if(row.IconKind!=kind)
        {
            var replacement=BrowserPresentation.Icon(kind);
            row.Icon.Data=replacement.Data; row.Icon.Classes.Clear();
            foreach(var value in replacement.Classes)row.Icon.Classes.Add(value);
            row.IconKind=kind;
        }
        row.Label.Text=row.Node.Name+(row.Node.Availability.Length==0 ? "" : $" · {row.Node.Availability}");
        LightflowTheme.State(row.Icon,"active",_model.IsFolderIconFilled(row.Node));
        AutomationProperties.SetName(row.Choose,row.Node.Name);
        AutomationProperties.SetHelpText(row.Choose,
            (_model.Location?.Key==row.Node.Key ? "Current folder. " : "")+
            (recursive ? "Include Subfolders on (recursive scope). " : "")+
            (row.Node.AbsolutePath ?? row.Node.RelativeFolder)+
            (row.Node.Availability.Length==0 ? "" : $". {row.Node.Availability}"));
    }
    private GridSplitter Splitter(string label)
    {
        var splitter=new GridSplitter { Width=8, HorizontalAlignment=HorizontalAlignment.Stretch,
            VerticalAlignment=VerticalAlignment.Stretch, ResizeDirection=GridResizeDirection.Columns,
            ResizeBehavior=GridResizeBehavior.PreviousAndNext, Classes={ "lf-splitter" },
            Focusable=true, KeyboardIncrement=16, DragIncrement=1 };
        AutomationProperties.SetName(splitter,label); AutomationProperties.SetHelpText(splitter,"Drag or use Left and Right arrow keys to resize");
        ToolTip.SetTip(splitter,label); splitter.DragCompleted+=(_,_)=>CommitPaneWidth(splitter);
        splitter.KeyUp+=(_,e)=> { if(e.Key is Key.Left or Key.Right) CommitPaneWidth(splitter); };
        splitter.DoubleTapped+=(_,_)=> { ResetLayout(); };
        return splitter;
    }
    public void ToggleRightPanel()
    {
        var open=!_preferred.RightOpen;
        if (!open && (_right.IsKeyboardFocusWithin || _rightSplitter.IsKeyboardFocusWithin))
            _panelToggle.Focus(NavigationMethod.Tab);
        _preferred=_preferred with { RightOpen=open };
        ApplyPaneLayout(); LayoutChanged?.Invoke(_preferred);
    }
    internal async Task HandleShellKeyAsync(KeyEventArgs e)
    {
        if(e.Handled)return;
        var primary=OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
        if(e.KeyModifiers==primary && e.Key==Key.I)
        { e.Handled=true; ToggleRightPanel(); }
        else if(e.KeyModifiers==primary && e.Key==Key.L)
        { e.Handled=true; FocusAddress(); }
        else if((e.KeyModifiers==primary && e.Key==Key.R) ||
                (e.KeyModifiers==KeyModifiers.None && e.Key==Key.F5))
        { e.Handled=true; await NavigateFromToolbarAsync(_model.RefreshAsync); }
        // Option-arrow belongs to the native text editor (word movement), not history.
        else if(!_address.IsKeyboardFocusWithin && e.KeyModifiers==KeyModifiers.Alt && e.Key==Key.Left)
        { e.Handled=true; await NavigateFromToolbarAsync(_model.BackAsync); }
        else if(!_address.IsKeyboardFocusWithin && e.KeyModifiers==KeyModifiers.Alt && e.Key==Key.Right)
        { e.Handled=true; await NavigateFromToolbarAsync(_model.ForwardAsync); }
    }
    internal void FocusAddress() { _address.Focus(NavigationMethod.Tab); _address.SelectAll(); }
    private void RestoreAddress()
    {
        _displayedLocation=_model.Location?.AbsolutePath ?? "";
        _address.Text=_displayedLocation;
    }
    private Task NavigateFromToolbarAsync(Func<Task> navigate)
    {
        // Explicit navigation abandons the editor draft, including refresh of the same path.
        RestoreAddress();
        return navigate();
    }
    private Task SubmitAddressAsync()
    {
        var draft=_address.Text;
        return string.IsNullOrWhiteSpace(draft) ? Task.CompletedTask : _model.GoPathAsync(draft);
    }
    private void ApplyPaneLayout()
    {
        if (_applyingLayout) return;
        _applyingLayout=true;
        try
        {
            var available=_layout.Bounds.Width>0 ? _layout.Bounds.Width : 1412;
            var fitted=BrowserPaneWidths.Fit(available,_preferred);
            var open=_preferred.RightOpen;
            _right.IsVisible=_rightSplitter.IsVisible=open;
            var left=_layout.ColumnDefinitions[0]; var center=_layout.ColumnDefinitions[2]; var right=_layout.ColumnDefinitions[4];
            left.MinWidth=Math.Min(180,fitted.Navigation); left.MaxWidth=Math.Max(left.MinWidth,Math.Min(520,available-(open ? 16 : 8)-fitted.CenterMinimum-fitted.Right));
            left.Width=new GridLength(fitted.Navigation);
            center.MinWidth=fitted.CenterMinimum; center.Width=new GridLength(1,GridUnitType.Star);
            right.MinWidth=open ? Math.Min(200,fitted.Right) : 0;
            right.MaxWidth=open ? Math.Max(right.MinWidth,Math.Min(600,available-16-fitted.CenterMinimum-fitted.Navigation)) : 0;
            right.Width=new GridLength(fitted.Right);
            _layout.ColumnDefinitions[3].Width=new GridLength(open ? 8 : 0);
            _panelToggle.Content=BrowserPresentation.Icon("panel",open);
            LightflowTheme.State(_panelToggle,"active",open);
            AutomationProperties.SetHelpText(_panelToggle,open ? "Right Panel shown" : "Right Panel hidden");
        }
        finally { _applyingLayout=false; }
    }
    private void CommitPaneWidth(GridSplitter splitter)
    {
        if(_applyingLayout)return;
        _preferred=(Grid.GetColumn(splitter)==1
            ? _preferred with { NavigationWidth=_layout.ColumnDefinitions[0].Width.Value }
            : _preferred with { RightWidth=_layout.ColumnDefinitions[4].Width.Value }).Validated();
        ApplyPaneLayout(); LayoutChanged?.Invoke(_preferred);
    }
    private void ResetLayout()
    {
        _preferred=_preferred with { NavigationWidth=280, RightWidth=360 };
        ApplyPaneLayout(); LayoutChanged?.Invoke(_preferred);
    }
    private static string Modified(long ticks) => ticks >= DateTime.MinValue.Ticks && ticks <= DateTime.MaxValue.Ticks
        ? new DateTime(ticks,DateTimeKind.Utc).ToString("yyyy-MM-dd HH:mm:ss") : "Not available";
    private static TextBlock Text(string text,string role="body")=>LightflowTheme.Text(text,role);
    private static Button Action(string text)=>LightflowTheme.Button(text);
    private static Border Panel(Control child,bool padding=true)=>LightflowTheme.Panel(child,padding);
}
