using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Lightflow.UI;

public sealed partial class ShellWindow : Window
{
    private readonly ShellViewModel _model;
    private readonly StackPanel _gate = new() { Spacing = 20, MaxWidth = 660, Margin = new Thickness(36) };
    private readonly Grid _workspace = new() { Margin = new Thickness(14,8,14,12) };
    private readonly TextBlock _title = Text("", "heading"), _message = Text("", "body"), _status = Text("", "status");
    public BrowserPane? Browser { get; }
    private BrowserLayout _preferences;
    public event Action<BrowserLayout>? LayoutChanged;
    private readonly Avalonia.Controls.Shapes.Ellipse _health = new() { Width=7,Height=7,Margin=new Thickness(0,0,8,0),VerticalAlignment=VerticalAlignment.Center };
    private readonly TextBlock _selected = Text("", "body");
    private readonly Button _catalogButton = Action("Catalog…");
    private readonly Button _retry = Action("Retry"), _create = Action("Create New Catalog…", true), _open = Action("Open Existing Catalog…");
    private readonly Button _confirm = Action("Create Catalog", true), _cancel = Action("Cancel");
    private readonly StackPanel _confirmation = new() { Spacing = 12 };
    private readonly StackPanel _actions = new() { Orientation = Orientation.Horizontal, Spacing = 12 };
    private readonly DispatcherTimer _readiness = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly Bitmap _brand;
    private string? _preparedFolder;
    private bool _catalogPanel;
    private Avalonia.Input.InputElement? _browserFocus;

    public ShellWindow(ShellViewModel model, BrowserLayout? browserLayout = null)
    {
        _model = model;
        _preferences=(browserLayout ?? new()).Validated();
        Title = "Lightflow Studio";
        InitializeWindowGeometry();
        LightflowTheme.Apply(this);
        _brand = new Bitmap(AssetLoader.Open(new Uri("avares://Lightflow.UI/Assets/lightflow-header-lockup-480x96.png")));
        using (var icon = AssetLoader.Open(new Uri("avares://Lightflow.UI/Assets/lightflow-icon-256x256.png"))) Icon = new WindowIcon(icon);
        var layout = new Grid { RowDefinitions = new RowDefinitions("60,*,34") };
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), Margin = new Thickness(18, 6) };
        header.Children.Add(new Image { Source = _brand, Width = 240, Height = 48, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left });
        Grid.SetColumn(_catalogButton, 2); header.Children.Add(_catalogButton);
        _catalogButton.Click += (_, _) =>
        {
            _catalogPanel = !_catalogPanel; Update();
            if (_catalogPanel) _create.Focus(Avalonia.Input.NavigationMethod.Tab);
            else if (_browserFocus is not { IsEffectivelyVisible:true, IsEffectivelyEnabled:true } ||
                     _browserFocus.GetVisualRoot()!=this || !_browserFocus.Focus(Avalonia.Input.NavigationMethod.Tab))
                Browser?.FocusAddress();
        };
        layout.Children.Add(new Border { Classes={ "lf-header" }, Child = header });
        var body = new Grid(); Grid.SetRow(body, 1); layout.Children.Add(body);
        var card = new Border { Classes={ "lf-panel" }, Margin = new Thickness(32), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Child = _gate };
        body.Children.Add(card); body.Children.Add(_workspace);
        AutomationProperties.SetAutomationId(_workspace, "ReadyWorkspace");
        _gate.Children.Add(_title); _gate.Children.Add(_message);
        var instructions = Text("Choose a Catalog on local APFS storage inside this test profile. To create one, select a prepared empty folder. Hover here for the profile location.", "muted");
        _gate.Children.Add(instructions);
        ToolTip.SetTip(instructions,model.DataRoot);
        _actions.Children.Add(_create); _actions.Children.Add(_open); _actions.Children.Add(_retry); _gate.Children.Add(_actions);
        _confirmation.Children.Add(Text("Create a Catalog in this prepared empty folder?", "panel-heading")); _confirmation.Children.Add(_selected);
        var confirmActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 }; confirmActions.Children.Add(_confirm); confirmActions.Children.Add(_cancel); _confirmation.Children.Add(confirmActions); _gate.Children.Add(_confirmation);
        var bottom = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), Margin = new Thickness(14,0) };
        _status.VerticalAlignment=VerticalAlignment.Center;
        _status.TextWrapping=TextWrapping.NoWrap; _status.TextTrimming=TextTrimming.CharacterEllipsis;
        var applicationStatus=new Grid { ColumnDefinitions=new("Auto,*") };
        applicationStatus.Children.Add(_health);Grid.SetColumn(_status,1);applicationStatus.Children.Add(_status);
        bottom.Children.Add(applicationStatus);
        var quit=Action("Quit Lightflow Studio"); _actions.Children.Add(quit); quit.Click+=(_,_)=>Close();
        if (model.Browser is { } browser)
        {
            Browser=new BrowserPane(browser,_preferences);
            Browser.LayoutChanged+=layout=> { _preferences=layout with { Window=_preferences.Window, Zoomed=_preferences.Zoomed }; LayoutChanged?.Invoke(_preferences); };
            _workspace.Children.Add(Browser);
            Browser.RightPanelToggle.Width=36;Browser.RightPanelToggle.Height=36;Browser.RightPanelToggle.Margin=new Thickness(8,0);
            Grid.SetColumn(Browser.RightPanelToggle,1);header.Children.Add(Browser.RightPanelToggle);
            var divider=new Border { Width=1, Height=16, Classes={ "lf-divider" },Margin=new Thickness(12,0) };
            Grid.SetColumn(divider,1);bottom.Children.Add(divider);
            Browser.StatusView.MaxWidth=680;
            Grid.SetColumn(Browser.StatusView,2);bottom.Children.Add(Browser.StatusView);
        }
        var footer=new Border { Classes={ "lf-footer" },Child=bottom };
        AutomationProperties.SetAutomationId(footer,"ShellStatusRegion");Grid.SetRow(footer,2);layout.Children.Add(footer);
        KeyDown+=async(_,e)=>
        {
            if (Browser is not null && _workspace.IsVisible)
                await Browser.HandleShellKeyAsync(e);
        };
        GotFocus+=(_,e)=>
        {
            if(e.Source is Control control && control.GetVisualAncestors().Contains(_workspace))
                _browserFocus=control;
        };
        Content = layout;
        _retry.Click += async (_, _) => await InvokeAsync(model.StartAsync);
        _open.Click += async (_, _) => { var folder = await PickFolderAsync("Open Existing Catalog"); if (folder is not null) { _preparedFolder = null; await InvokeAsync(() => model.OpenAsync(folder)); } };
        _create.Click += async (_, _) => { var folder = await PickFolderAsync("Choose a prepared empty Catalog folder"); if (folder is not null) { _preparedFolder = folder; _selected.Text = folder; Update(); } };
        _confirm.Click += async (_, _) => { var folder = _preparedFolder; _preparedFolder = null; if (folder is not null) await InvokeAsync(() => model.CreateAsync(folder)); };
        _cancel.Click += (_, _) => { _preparedFolder = null; Update(); };
        model.PropertyChanged += (_, _) => { if (Dispatcher.UIThread.CheckAccess()) Update(); else Dispatcher.UIThread.Post(Update); };
        _readiness.Tick += async (_, _) => await InvokeAsync(model.VerifyAsync, keepPanel:true);
        Opened += async (_, _) => { await InvokeAsync(model.StartAsync); _readiness.Start(); };
        Activated += async (_, _) => await InvokeAsync(model.VerifyAsync, keepPanel:true);
        Closed += (_, _) => { _readiness.Stop(); _brand.Dispose(); };
        Update();
    }
    private async Task<string?> PickFolderAsync(string title)
    {
        if (!_model.CanChoose || !StorageProvider.CanPickFolder) return null;
        var root = await StorageProvider.TryGetFolderFromPathAsync(new Uri(_model.DataRoot));
        var result = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title, AllowMultiple = false, SuggestedStartLocation = root });
        try { return result.Count == 1 ? result[0].TryGetLocalPath() : null; }
        finally { foreach (var folder in result) folder.Dispose(); root?.Dispose(); }
    }
    private async Task InvokeAsync(Func<Task> action, bool keepPanel = false)
    {
        try { await action(); if (!keepPanel && _model.WorkspaceAvailable) _catalogPanel = false; }
        catch (Exception error) { Console.Error.WriteLine(error); }
        Update();
    }
    private void Update()
    {
        _title.Text = _model.Title; _message.Text = _model.Message;
        _status.Text = _model.WorkspaceAvailable ? "Ready" : _model.Title;
        _health.Classes.Add("lf-health");
        LightflowTheme.State(_health,"ready",_model.WorkspaceAvailable);
        LightflowTheme.State(_health,"busy",_model.IsBusy);
        ToolTip.SetTip(_status,_model.Message);
        _workspace.IsVisible = _model.WorkspaceAvailable && !_catalogPanel;
        _gate.Parent!.SetValue(IsVisibleProperty, !_workspace.IsVisible);
        if(Browser is not null)
        {
            Browser.RightPanelToggle.IsVisible=_workspace.IsVisible;
            Browser.StatusView.IsVisible=_workspace.IsVisible;
        }
        _catalogButton.IsEnabled = _model.WorkspaceAvailable;
        _catalogButton.Content = _catalogPanel ? "Back to Browser" : "Catalog…";
        _create.IsEnabled = _open.IsEnabled = _model.CanChoose && _preparedFolder is null;
        _retry.IsEnabled = !_model.IsBusy && !_model.IsClosing && _preparedFolder is null;
        _retry.IsVisible = !_model.Snapshot.IsReady;
        _confirmation.IsVisible = _preparedFolder is not null;
        _confirm.IsEnabled = _cancel.IsEnabled = _model.CanChoose;
    }
    private static TextBlock Text(string text,string role)=>LightflowTheme.Text(text,role);
    private static Button Action(string label,bool primary=false)=>LightflowTheme.Button(label,primary ? "primary" : "secondary");
}
