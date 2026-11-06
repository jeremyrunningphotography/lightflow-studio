using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LightflowStudio;

namespace Lightflow.UI;

public sealed partial class BrowserPane
{
    private int _cardColumns = 1;
    private Guid? _focusedCard;
    private readonly TextBlock _gridEmpty = LightflowTheme.Text("Choose a location", "muted");
    private BrowserCardMode _cardMode;
    private readonly Dictionary<BrowserCardMode, ToggleButton> _modeButtons = [];
    public BrowserCardMode CardMode => _cardMode;
    public int CardColumns => _cardColumns;

    private Grid CreateCardGrid()
    {
        var media = new Grid { RowDefinitions = new("Auto,*") };
        var modes = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(7) };
        foreach (var mode in Enum.GetValues<BrowserCardMode>())
        {
            var button = new ToggleButton { Content = mode.ToString(), Classes = { "lf-button", "scope" } };
            AutomationProperties.SetName(button, $"Grid {mode} mode");
            ToolTip.SetTip(button, $"Grid {mode}");
            button.Click += (_, _) => SetCardMode(mode);
            _modeButtons[mode] = button; modes.Children.Add(button);
        }
        media.Children.Add(modes);
        _assets.Classes.Add("lf-card-rows");
        _assets.Padding = new Thickness(7);
        ScrollViewer.SetHorizontalScrollBarVisibility(_assets, ScrollBarVisibility.Disabled);
        _assets.ItemsPanel = new FuncTemplate<Panel?>(() => new VirtualizingStackPanel());
        _assets.ItemTemplate = new FuncDataTemplate<BrowserCardRow>((row, _) =>
        {
            var cards = new StackPanel { Orientation = Orientation.Horizontal, Height = BrowserCardLayout.Height(_cardMode) + BrowserCardLayout.Gap };
            if (row is not null)
                for (var i = row.Start; i < row.Start + row.Count; i++)
                    cards.Children.Add(new BrowserGridCard(_model, row.Assets[i], _cardMode, SelectCardAsync));
            return cards;
        });
        _assets.SelectionChanged += async (_, _) =>
        {
            if (!_updating && _assets.SelectedItem is BrowserCardRow row)
                await _model.SelectAsync(row.Assets[row.Start].AssetId);
        };
        _assets.SizeChanged += (_, _) => ReflowCards();
        _assets.AddHandler(KeyDownEvent, CardKeyDown, RoutingStrategies.Tunnel);
        AutomationProperties.SetAutomationId(_assets, "CatalogAssets");
        AutomationProperties.SetName(_assets, "Media grid in current folder");
        AutomationProperties.SetHelpText(_assets, "Arrow keys move through cards. Home and End move to the first and last asset. Page Up and Page Down move a page.");
        SetCardMode(BrowserCardMode.Preview);
        Grid.SetRow(_assets, 1); media.Children.Add(_assets);
        _gridEmpty.HorizontalAlignment = HorizontalAlignment.Center; _gridEmpty.VerticalAlignment = VerticalAlignment.Center;
        _gridEmpty.TextWrapping = TextWrapping.Wrap; _gridEmpty.MaxWidth = 360; _gridEmpty.IsHitTestVisible = false;
        Grid.SetRow(_gridEmpty, 1); media.Children.Add(_gridEmpty);
        return media;
    }

    public void SetCardMode(BrowserCardMode mode)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        _cardMode = mode;
        foreach (var (value, button) in _modeButtons)
        { button.IsChecked = value == mode; LightflowTheme.State(button, "active", value == mode); }
        // Existing realized cards update in place: no bitmap disposal, discovery or new demand.
        foreach (var card in _assets.GetVisualDescendants().OfType<BrowserGridCard>()) card.SetMode(mode);
        foreach (var panel in _assets.GetVisualDescendants().OfType<StackPanel>().Where(x => x.DataContext is BrowserCardRow))
            panel.Height = BrowserCardLayout.Height(mode) + BrowserCardLayout.Gap;
        KeepCardVisible(focus: false);
    }

    private void ReflowCards()
    {
        // Match Windows' scrollbar allowance and trailing twelve-pixel footprint.
        var columns = BrowserCardLayout.Columns(Math.Max(0, _assets.Bounds.Width - 14 - 20));
        if (columns == _cardColumns) return;
        _cardColumns = columns;
        RebuildCardRows();
        KeepCardVisible(_assets.IsKeyboardFocusWithin);
    }

    private void UpdateCardRows()
    {
        _gridEmpty.IsVisible = _model.Assets.Count == 0;
        _gridEmpty.Text = _model.IsLoading ? "Loading media…" : _model.Location is null ? "Choose a location to browse media." : _model.Message;
        if (ReferenceEquals(_shownAssets, _model.Assets)) return;
        if (_focusedCard is { } focused && !_model.Assets.Any(a => a.AssetId == focused)) _focusedCard = null;
        _shownAssets = _model.Assets;
        _viewportAssets = _model.Assets;
        _viewportIndices = _model.Assets.Select((asset, index) => (asset.AssetId, index)).ToDictionary(x => x.AssetId, x => x.index);
        var scroll = _assets.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        var offset = scroll?.Offset;
        RebuildCardRows();
        if (scroll is not null && offset is { } previous)
            Dispatcher.UIThread.Post(() => scroll.Offset = previous, DispatcherPriority.Loaded);
    }

    private void RebuildCardRows()
    {
        var updating = _updating; _updating = true;
        try { _assets.ItemsSource = new BrowserCardRows(_model.Assets, _cardColumns); }
        finally { _updating = updating; }
    }

    private async Task SelectCardAsync(MediaAsset asset, KeyModifiers modifiers)
    {
        _focusedCard = asset.AssetId;
        await _model.SelectGridAsync(asset.AssetId, (modifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0, (modifiers & KeyModifiers.Shift) != 0);
    }

    private async void CardKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.A && e.KeyModifiers == (OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control))
        { e.Handled = true; _model.SelectAllGrid(); return; }
        if ((e.KeyModifiers & ~KeyModifiers.Shift) != 0 || e.Key is not (Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End or Key.PageUp or Key.PageDown)) return;
        e.Handled = true;
        var current = _focusedCard is { } focused ? IndexOfAsset(focused) : _model.SelectedAsset is { } selected ? IndexOfAsset(selected.AssetId) : 0;
        var next = BrowserCardLayout.Move(current, _model.Assets.Count, _cardColumns, e.Key,
            Math.Max(1, (int)(_assets.Bounds.Height / (BrowserCardLayout.Height(_cardMode) + BrowserCardLayout.Gap))));
        if (next < 0) return;
        _focusedCard = _model.Assets[next].AssetId;
        await _model.SelectGridAsync(_focusedCard.Value, range: (e.KeyModifiers & KeyModifiers.Shift) != 0);
        KeepCardVisible(focus: true);
    }

    private int IndexOfAsset(Guid id) => _viewportIndices.GetValueOrDefault(id, -1);
    private void KeepCardVisible(bool focus)
    {
        var id = _focusedCard ?? _model.SelectedAssetId;
        var index = id is { } value ? IndexOfAsset(value) : -1;
        if (index < 0) return;
        _assets.ScrollIntoView(index / _cardColumns);
        if (focus) Dispatcher.UIThread.Post(() =>
            _assets.GetVisualDescendants().OfType<BrowserGridCard>().FirstOrDefault(x => x.AssetId == id)?.Focus(NavigationMethod.Directional), DispatcherPriority.Loaded);
    }
}
