using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using LightflowStudio;

namespace Lightflow.UI;

/// <summary>Native presentation only; shared ViewModel owns identity, selection and Preview demand.</summary>
internal sealed class BrowserGridCard : Button
{
    // Avalonia selectors use StyleKey, not CLR inheritance. Opt into the scoped Button card template.
    protected override Type StyleKeyOverride => typeof(Button);
    private readonly BrowserViewModel _model;
    private readonly MediaAsset _asset;
    private readonly BrowserThumbnailCell _preview;
    private readonly Border _upper = new() { Height = 22, Padding = new Thickness(2) };
    private readonly Border _lower = new() { Height = 24, Padding = new Thickness(2) };
    private readonly TextBlock _filename;
    private readonly Border _surface;
    private readonly Grid _composition;
    private BrowserCardMode _mode;
    public Guid AssetId => _asset.AssetId;
    public BrowserGridCard(BrowserViewModel model, MediaAsset asset, BrowserCardMode mode, Func<MediaAsset, KeyModifiers, Task> select)
    {
        _model = model; _asset = asset;
        Classes.Add("lf-grid-card");
        Width = BrowserCardLayout.Width; Margin = new Thickness(0, 0, BrowserCardLayout.Gap, BrowserCardLayout.Gap);
        Padding = new Thickness(7); BorderThickness = new Thickness(1); CornerRadius = new CornerRadius(6);
        HorizontalContentAlignment = HorizontalAlignment.Stretch; VerticalContentAlignment = VerticalAlignment.Top;
        _preview = new BrowserThumbnailCell(model, asset.AssetId) { Width = double.NaN, Height = double.NaN, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
        _surface = new Border { Background = Brush("#05070A"), CornerRadius = new CornerRadius(4), ClipToBounds = true, Child = _preview };
        _filename = LightflowTheme.Text(asset.RelativePath.Split('/')[^1]);
        _filename.FontSize = 12; _filename.TextAlignment = TextAlignment.Center;
        _filename.TextWrapping = TextWrapping.NoWrap; _filename.TextTrimming = TextTrimming.CharacterEllipsis;
        _filename.Margin = new Thickness(1, 7, 1, 0);
        // Windows' Info frames reserve room for authored indicators. Those projections belong to #485;
        // do not fabricate ratings, camera data or status from filename/extension.
        _composition = new Grid { RowDefinitions = new("Auto,Auto,Auto,Auto") };
        _composition.Children.Add(_upper); Grid.SetRow(_surface, 1); _composition.Children.Add(_surface);
        Grid.SetRow(_lower, 2); _composition.Children.Add(_lower); Grid.SetRow(_filename, 3); _composition.Children.Add(_filename);
        Content = _composition;
        var label = $"{asset.RelativePath}, {asset.MediaType}, {asset.FileSizeBytes:N0} bytes";
        AutomationProperties.SetName(this, label); ToolTip.SetTip(this, label);
        var modifiers = KeyModifiers.None;
        AddHandler(PointerPressedEvent, (_, e) => modifiers = e.KeyModifiers, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        Click += async (_, _) => { var used = modifiers; modifiers = KeyModifiers.None; await select(asset, used); };
        AttachedToVisualTree += (_, _) => { model.PropertyChanged += Changed; ApplySelection(); };
        DetachedFromVisualTree += (_, _) => model.PropertyChanged -= Changed;
        GotFocus += (_, _) => ApplySelection(); LostFocus += (_, _) => ApplySelection();
        SetMode(mode);
    }
    public void SetMode(BrowserCardMode mode)
    {
        _mode = mode;
        Height = BrowserCardLayout.Height(mode);
        _upper.IsVisible = _lower.IsVisible = mode == BrowserCardMode.Info;
        _filename.IsVisible = mode != BrowserCardMode.Preview;
        _surface.Height = mode == BrowserCardMode.Info ? BrowserCardLayout.InfoPreviewHeight : BrowserCardLayout.PreviewHeight;
        ApplySelection();
    }
    private void Changed(object? sender, PropertyChangedEventArgs e)
    { if (e.PropertyName != nameof(BrowserViewModel.Thumbnails)) ApplySelection(); }
    private void ApplySelection()
    {
        var selected = _model.IsAssetSelected(AssetId);
        Classes.Set("selected", selected);
        BorderBrush = Brush(selected ? "#FF9A66" : "#2B303A");
        Background = Brush(selected ? "#282129" : _mode == BrowserCardMode.Info ? "#1B2028" : "#171A20");
        AutomationProperties.SetHelpText(this, $"{_mode} mode{(selected ? ", selected" : "")}. Arrow keys navigate the grid.");
    }
    private static SolidColorBrush Brush(string value) => new(Color.Parse(value));
}
