using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using Binding = System.Windows.Data.Binding;
using Image = System.Windows.Controls.Image;

namespace LightflowStudio;

public partial class MainWindow
{
    private BrowserLayoutMode _browserLayoutMode;
    private readonly GridView _browserDetailsView = new();
    private readonly Dictionary<string, GridViewColumn> _detailsColumns = [];
    private IReadOnlyList<WorkspaceDetailsColumn> _detailsLayout = [];
    private bool _changingDetailsColumns;
    private Guid? _browserKeyboardCurrentAssetId;
    private int _browserLayoutRevision;
    public GridViewColumnCollection BrowserDetailsColumns => _browserDetailsView.Columns;

    private void InitializeBrowserDetails()
    {
        _detailsLayout = BrowserDetails.Normalize(_workspaceState.Current.Layout?.BrowserDetailsColumns);
        foreach (var definition in BrowserDetails.Columns)
        {
            var header = new GridViewColumnHeader { Content = definition.Title, Tag = definition.Id,
                Style = (Style)FindResource("BrowserDetailsHeaderStyle"),
                Background = (System.Windows.Media.Brush)FindResource("ShellSurfaceBrush"),
                Foreground = (System.Windows.Media.Brush)FindResource("TextBrush"),
                BorderBrush = (System.Windows.Media.Brush)FindResource("ShellDividerBrush"),
                HorizontalContentAlignment = System.Windows.HorizontalAlignment.Left, Padding = new Thickness(8, 6, 4, 6) };
            header.Click += (_, _) =>
            {
                if (definition.Sort is { } sort) ApplyBrowserQuery(query => BrowserDetails.Sort(query, sort));
            };
            var column = new GridViewColumn { Header = header, Width = _detailsLayout.First(c => c.Id == definition.Id).Width };
            var cell = new FrameworkElementFactory(definition.Id == "preview" ? typeof(Image) : typeof(TextBlock));
            cell.SetValue(FrameworkElement.MarginProperty, new Thickness(8, 0, 6, 0));
            cell.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            if (definition.Id == "preview")
            {
                cell.SetBinding(Image.SourceProperty, new Binding(nameof(BrowserGridTile.ThumbnailPath)));
                cell.SetValue(FrameworkElement.HeightProperty, 30d);
                cell.SetValue(Image.StretchProperty, System.Windows.Media.Stretch.Uniform);
            }
            else
            {
                var text = new MultiBinding { Converter = (IMultiValueConverter)FindResource("BrowserDetailsTextConverter"), ConverterParameter = definition.Id };
                text.Bindings.Add(new Binding());
                text.Bindings.Add(new Binding(nameof(BrowserGridTile.DetailsRevision)));
                cell.SetBinding(TextBlock.TextProperty, text);
                cell.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
                cell.SetValue(TextBlock.ForegroundProperty, FindResource("TextBrush"));
                cell.SetValue(TextBlock.FontSizeProperty, 12d);
                cell.SetBinding(FrameworkElement.ToolTipProperty, new Binding("Text") { RelativeSource = RelativeSource.Self });
            }
            column.CellTemplate = definition.Id == "preview" ? (DataTemplate)FindResource("BrowserDetailsPreviewTemplate") : new DataTemplate { VisualTree = cell };
            _detailsColumns.Add(definition.Id, column);
            DependencyPropertyDescriptor.FromProperty(GridViewColumn.WidthProperty, typeof(GridViewColumn))
                .AddValueChanged(column, (_, _) => SaveDetailsColumns());
        }
        BrowserDetailsHeaders.Columns = BrowserDetailsColumns;
        ((INotifyCollectionChanged)BrowserDetailsColumns).CollectionChanged += (_, _) => SaveDetailsColumns();
        var menu = new ContextMenu { Style = (Style)FindResource("LightflowContextMenuStyle") };
        menu.Opened += (_, _) =>
        {
            menu.Items.Clear();
            foreach (var definition in BrowserDetails.Columns)
            {
                var item = new MenuItem { Header = definition.Title, IsCheckable = true,
                    IsChecked = BrowserDetailsColumns.Contains(_detailsColumns[definition.Id]),
                    Style = (Style)FindResource("LightflowMenuItemStyle") };
                item.Click += (_, _) =>
                {
                    if (!item.IsChecked && BrowserDetailsColumns.Count == 1) { item.IsChecked = true; return; }
                    _detailsLayout = _detailsLayout.Select(c => c.Id == definition.Id ? c with { Visible = item.IsChecked } : c).ToArray();
                    RebuildDetailsColumns();
                    SaveDetailsColumns();
                };
                menu.Items.Add(item);
            }
        };
        BrowserDetailsHeaders.ContextMenu = menu;
        RebuildDetailsColumns();
        BrowserGridRows.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((_, _) =>
        {
            if (_browserLayoutMode == BrowserLayoutMode.Details && FindBrowserGridScrollViewer() is { } viewer)
                BrowserDetailsHeaderScroll.ScrollToHorizontalOffset(viewer.HorizontalOffset);
        }));
        ApplyBrowserLayout(_workspaceState.Current.Layout?.BrowserLayoutMode ?? BrowserLayoutMode.Grid, false);
    }

    private void RebuildDetailsColumns()
    {
        _changingDetailsColumns = true;
        try
        {
            BrowserDetailsColumns.Clear();
            foreach (var column in _detailsLayout.Where(c => c.Visible)) BrowserDetailsColumns.Add(_detailsColumns[column.Id]);
            Resources["BrowserDetailsWidth"] = BrowserDetailsColumns.Sum(c => c.Width);
        }
        finally { _changingDetailsColumns = false; }
    }

    private void SaveDetailsColumns()
    {
        if (_changingDetailsColumns || _detailsColumns.Count != BrowserDetails.Columns.Count) return;
        _changingDetailsColumns = true;
        try
        {
            foreach (var pair in _detailsColumns)
            {
                var width = pair.Value.Width;
                if (!double.IsFinite(width)) width = Math.Max(40, pair.Value.ActualWidth);
                width = Math.Clamp(width, 40, 1200);
                if (pair.Value.Width != width) pair.Value.Width = width;
            }
        }
        finally { _changingDetailsColumns = false; }
        // Visible slots take the header's current order; hidden columns retain their remembered positions.
        var visible = new Queue<string>(BrowserDetailsColumns.Select(c => (string)((GridViewColumnHeader)c.Header).Tag));
        _detailsLayout = _detailsLayout.Select(c => c.Visible && visible.Count > 0 ? c with { Id = visible.Dequeue() } : c)
            .Select(c => c with { Width = _detailsColumns[c.Id].Width }).ToArray();
        Resources["BrowserDetailsWidth"] = BrowserDetailsColumns.Sum(c => c.Width);
        _workspaceState.SetBrowserDetails(_browserLayoutMode, _detailsLayout);
        ScheduleWorkspaceCapture();
    }

    private void BrowserLayout_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton { Tag: string mode } && Enum.TryParse<BrowserLayoutMode>(mode, out var parsed))
            ApplyBrowserLayout(parsed, true);
    }

    // Header input must not bubble to the canvas's clear-selection gesture.
    private void BrowserDetailsHeader_MouseDown(object sender, MouseButtonEventArgs e) => ResetBrowserAssetGesture();

    internal void ApplyBrowserLayout(BrowserLayoutMode mode, bool preserveContext)
    {
        var revision = ++_browserLayoutRevision;
        var generation = _browserUiGeneration;
        var saved = preserveContext ? CaptureWorkspaceGrid() : null;
        _browserLayoutMode = mode;
        BrowserGridLayoutButton.IsChecked = mode == BrowserLayoutMode.Grid;
        BrowserDetailsLayoutButton.IsChecked = mode == BrowserLayoutMode.Details;
        BrowserDetailsHeaderScroll.Visibility = mode == BrowserLayoutMode.Details ? Visibility.Visible : Visibility.Collapsed;
        BrowserGridRows.ItemTemplate = (DataTemplate)FindResource(mode == BrowserLayoutMode.Details
            ? "BrowserDetailsRowsTemplate" : "BrowserThumbnailRowsTemplate");
        UpdateBrowserGridColumns();
        BrowserGridRows.ApplyTemplate();
        if (FindBrowserGridScrollViewer() is { } viewer)
            viewer.HorizontalScrollBarVisibility = mode == BrowserLayoutMode.Details ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
        SyncDetailsSortHeaders();
        _workspaceState.SetBrowserDetails(mode, _detailsLayout);
        if (saved is not null)
            Dispatcher.BeginInvoke(() =>
            {
                if (_browserLayoutRevision != revision || _browserUiGeneration != generation) return;
                BrowserGridRows.UpdateLayout();
                RestoreBrowserContentOffset(saved);
                BrowserGridRows.Focus();
            }, DispatcherPriority.Loaded);
        ScheduleWorkspaceCapture();
    }

    private void SyncDetailsSortHeaders()
    {
        foreach (var definition in BrowserDetails.Columns)
            if (_detailsColumns.TryGetValue(definition.Id, out var column))
                ((GridViewColumnHeader)column.Header).Content = definition.Title +
                    (definition.Sort == _browserGrid.Query.SortMode ? _browserGrid.Query.SortDescending ? " ▼" : " ▲" : "");
    }

    private void RestoreBrowserContentOffset(WorkspaceGridState saved)
    {
        if (FindBrowserGridScrollViewer() is not { } viewer || _browserGrid.Rows.Count == 0) return;
        var row = _browserGrid.Rows.Select((value, index) => (value, index))
            .FirstOrDefault(pair => saved.TopAssetId is not null && pair.value.Tiles.Any(tile => tile.AssetId == saved.TopAssetId));
        _browserGridScrollOffset = saved.RestoreOffset(row.value is null ? null : row.index,
            _browserGrid.Rows.Count, viewer.ExtentHeight, viewer.ScrollableHeight);
        viewer.ScrollToVerticalOffset(_browserGridScrollOffset);
        viewer.ScrollToHorizontalOffset(saved.HorizontalOffset);
    }

    private bool IsDetailsHeaderSource(DependencyObject source)
    {
        for (var node = source; node is not null; node = System.Windows.Media.VisualTreeHelper.GetParent(node))
            if (ReferenceEquals(node, BrowserDetailsHeaderScroll)) return true;
        return false;
    }

    private bool NavigateBrowserKeyboard(System.Windows.Input.KeyEventArgs e)
    {
        if (_browserGrid.Tiles.Count == 0 || e.Key is not (Key.Up or Key.Down or Key.Left or Key.Right or Key.Home or Key.End or Key.PageUp or Key.PageDown)) return false;
        var current = _browserGrid.Tiles.FirstOrDefault(t => t.AssetId == _browserKeyboardCurrentAssetId)
            ?? _browserGrid.Tiles.FirstOrDefault(t => t.IsSelected) ?? _browserGrid.Tiles[0];
        var columns = _browserGrid.Rows[0].Tiles.Count;
        var step = e.Key is Key.Up or Key.Down ? columns : e.Key is Key.PageUp or Key.PageDown ?
            columns * Math.Max(1, (int)((FindBrowserGridScrollViewer()?.ViewportHeight ?? 380) /
                (_browserLayoutMode == BrowserLayoutMode.Details ? BrowserDetails.RowHeight : 150))) : 1;
        var index = e.Key switch { Key.Home => 0, Key.End => _browserGrid.Tiles.Count - 1,
            Key.Up or Key.Left or Key.PageUp => current.Index - step, _ => current.Index + step };
        index = Math.Clamp(index, 0, _browserGrid.Tiles.Count - 1);
        var accepted = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? _browserGrid.SelectRange(index) : _browserGrid.SelectSingle(index);
        if (accepted)
        {
            _browserKeyboardCurrentAssetId = _browserGrid.Tiles[index].AssetId;
            if (_browserKeyboardCurrentAssetId is { } id) RevealBrowserAsset(id);
            UpdateBrowserStatusText();
        }
        return true;
    }
}
