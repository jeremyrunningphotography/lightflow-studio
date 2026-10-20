using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace Lightflow.UI;

public sealed class ShellWindow : Window
{
    private readonly ShellViewModel _model;
    private readonly StackPanel _gate = new() { Spacing = 20, MaxWidth = 660, Margin = new Thickness(36) };
    private readonly StackPanel _workspace = new() { Spacing = 12, Margin = new Thickness(24, 8, 24, 8) };
    private readonly TextBlock _title = Text("", 26), _message = Text("", 15), _context = Text("", 13), _status = Text("", 13);
    private readonly TextBlock _selected = Text("", 14);
    private readonly Button _catalogButton = Action("Catalog…");
    private readonly Button _retry = Action("Retry"), _create = Action("Create New Catalog…", true), _open = Action("Open Existing Catalog…");
    private readonly Button _confirm = Action("Create Catalog", true), _cancel = Action("Cancel");
    private readonly StackPanel _confirmation = new() { Spacing = 12 };
    private readonly StackPanel _actions = new() { Orientation = Orientation.Horizontal, Spacing = 12 };
    private readonly DispatcherTimer _readiness = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly Bitmap _brand;
    private string? _preparedFolder;
    private bool _catalogPanel;
    private static IBrush Brush(string color) => new SolidColorBrush(Color.Parse(color));

    public ShellWindow(ShellViewModel model)
    {
        _model = model;
        Title = "Lightflow Studio"; Width = 1120; Height = 720; MinWidth = 900; MinHeight = 620;
        Background = Brush("#0B0D11"); Foreground = Brush("#F0F0F2"); FontFamily = new FontFamily(".AppleSystemUIFont, Helvetica Neue, sans-serif");
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        _brand = new Bitmap(AssetLoader.Open(new Uri("avares://Lightflow.UI/Assets/lightflow-header-lockup-480x96.png")));
        using (var icon = AssetLoader.Open(new Uri("avares://Lightflow.UI/Assets/lightflow-icon-256x256.png"))) Icon = new WindowIcon(icon);
        var layout = new Grid { RowDefinitions = new RowDefinitions("72,*,44") };
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(24, 12) };
        header.Children.Add(new Image { Source = _brand, Width = 240, Height = 48, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left });
        Grid.SetColumn(_catalogButton, 1); header.Children.Add(_catalogButton);
        _catalogButton.Click += (_, _) => { _catalogPanel = !_catalogPanel; Update(); };
        layout.Children.Add(new Border { Background = Brush("#11141A"), BorderBrush = Brush("#2B303A"), BorderThickness = new Thickness(0,0,0,1), Child = header });
        var body = new Grid(); Grid.SetRow(body, 1); layout.Children.Add(body);
        var card = new Border { Background = Brush("#171A20"), BorderBrush = Brush("#2B303A"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Margin = new Thickness(32), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Child = _gate };
        body.Children.Add(card); body.Children.Add(_workspace);
        AutomationProperties.SetAutomationId(_workspace, "ReadyWorkspace");
        _gate.Children.Add(_title); _gate.Children.Add(_message);
        var instructions = Text("Use a Catalog folder inside this isolated data folder. To create a Catalog, first prepare an empty folder there, then select it. Supported local APFS storage only.", 13);
        instructions.Foreground = Brush("#B9BEC8"); _gate.Children.Add(instructions);
        var root = Text(model.DataRoot, 12); root.Foreground = Brush("#D8D8DC"); _gate.Children.Add(root);
        _actions.Children.Add(_create); _actions.Children.Add(_open); _actions.Children.Add(_retry); _gate.Children.Add(_actions);
        _confirmation.Children.Add(Text("Create a Catalog in this prepared empty folder?", 16)); _confirmation.Children.Add(_selected);
        var confirmActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 }; confirmActions.Children.Add(_confirm); confirmActions.Children.Add(_cancel); _confirmation.Children.Add(confirmActions); _gate.Children.Add(_confirmation);
        var bottom = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(24, 0) };
        _status.VerticalAlignment = VerticalAlignment.Center; bottom.Children.Add(_status);
        var quit = Action("Quit Lightflow Studio"); Grid.SetColumn(quit, 1); bottom.Children.Add(quit); quit.Click += (_, _) => Close();
        var footer = new Border { Background = Brush("#11141A"), BorderBrush = Brush("#2B303A"), BorderThickness = new Thickness(0,1,0,0), Child = bottom }; Grid.SetRow(footer,2);layout.Children.Add(footer);
        if (model.Browser is { } browser)
            _workspace.Children.Add(new BrowserPane(browser) { Height = 480 });
        _workspace.Children.Add(_context);
        SizeChanged += (_, _) =>
        {
            if (_workspace.Children.OfType<BrowserPane>().FirstOrDefault() is { } pane)
                pane.Height = Math.Max(340, Bounds.Height - 200);
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
        _status.Text = _model.WorkspaceAvailable ? "●  Catalog ready" : _model.Title;
        _context.Text = $"{_model.Snapshot.Directory}\nCatalog {_model.Snapshot.CatalogId}";
        _workspace.IsVisible = _model.WorkspaceAvailable && !_catalogPanel;
        _gate.Parent!.SetValue(IsVisibleProperty, !_workspace.IsVisible);
        _catalogButton.IsEnabled = _model.WorkspaceAvailable;
        _catalogButton.Content = _catalogPanel ? "Back to Browser" : "Catalog…";
        _create.IsEnabled = _open.IsEnabled = _model.CanChoose && _preparedFolder is null;
        _retry.IsEnabled = !_model.IsBusy && !_model.IsClosing && _preparedFolder is null;
        _retry.IsVisible = !_model.Snapshot.IsReady;
        _confirmation.IsVisible = _preparedFolder is not null;
        _confirm.IsEnabled = _cancel.IsEnabled = _model.CanChoose;
    }
    private static TextBlock Text(string text, double size) => new() { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap };
    private static Button Action(string label, bool primary = false) => new() { Content = label, Padding = new Thickness(16,10), CornerRadius = new CornerRadius(6), Background = Brush(primary ? "#D86B37" : "#1D2129"), Foreground = Brushes.White, BorderBrush = Brush("#3B404A"), BorderThickness = new Thickness(1), FontSize = 13 };
}
