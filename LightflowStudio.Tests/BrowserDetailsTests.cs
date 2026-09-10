using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using LightflowStudio;
using Xunit;
using Xunit.Abstractions;

namespace LightflowStudio.Tests;

public sealed class BrowserDetailsTests
{
    [Fact]
    public void LayoutRoundTrip_NormalizesUnknownColumnsAndRetainsQueryAndSharedSelection()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            var columns = BrowserDetails.Normalize([
                new() { Id = "rating", Width = 134, Visible = true },
                new() { Id = "future", Width = 55, Visible = true },
                new() { Id = "name", Width = 330, Visible = false },
                new() { Id = "rating", Width = 42, Visible = false }]);
            var asset = Guid.NewGuid();
            var service = new WorkspaceStateService(path);
            service.SetBrowserDetails(BrowserLayoutMode.Details, columns);
            service.SetContinuation(new() { Query = new() { SortMode = BrowserSortMode.FrameRate, SortDescending = true },
                Grid = new() { SelectedAssetIds = [asset], AnchorAssetId = asset, TopAssetId = asset, HorizontalOffset = 128 } });
            service.Save();
            var restored = WorkspaceStateStore.Load(path);
            Assert.Equal(BrowserLayoutMode.Details, restored.Layout!.BrowserLayoutMode);
            Assert.Equal(columns, restored.Layout.BrowserDetailsColumns);
            Assert.Equal("rating", columns[0].Id);
            Assert.Equal(134, columns[0].Width);
            Assert.False(columns.Single(c => c.Id == "name").Visible);
            Assert.DoesNotContain(columns, c => c.Id == "future");
            Assert.Equal(BrowserSortMode.FrameRate, restored.Continuation!.Query.SortMode);
            Assert.Equal(asset, restored.Continuation.Grid.AnchorAssetId);
            Assert.Equal(128, restored.Continuation.Grid.HorizontalOffset);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData((int)BrowserSortMode.Rating)]
    [InlineData((int)BrowserSortMode.Flag)]
    [InlineData((int)BrowserSortMode.FrameRate)]
    [InlineData((int)BrowserSortMode.Dimensions)]
    public void SharedSort_MissingLastBothDirectionsAndSelectionSurvives(int sortValue)
    {
        var sort = (BrowserSortMode)sortValue;
        var model = new BrowserGridModel();
        model.Populate(Entries(3));
        var tiles = model.Tiles.ToArray();
        tiles[1].SetClassification(new(tiles[1].AssetId!.Value, 1, AssetFlag.Rejected, null, []));
        tiles[2].SetClassification(new(tiles[2].AssetId!.Value, 5, AssetFlag.Picked, null, []));
        tiles[1].ApplyMetadata(new(null, 3, null, null, null, 640, 480, 24));
        tiles[2].ApplyMetadata(new(null, 6, null, null, null, 1920, 1080, 60));
        model.SelectSingle(1);
        var anchor = model.SelectionAnchorAssetId;
        model.SetQuery(BrowserDetails.Sort(model.Query, sort));
        Assert.Same(tiles[1], model.Tiles[0]);
        Assert.Same(tiles[0], model.Tiles[^1]);
        model.SetQuery(BrowserDetails.Sort(model.Query, sort));
        Assert.Same(tiles[2], model.Tiles[0]);
        Assert.Same(tiles[0], model.Tiles[^1]);
        Assert.Contains(anchor!.Value, model.SelectedAssetIdsInBrowserOrder);
    }

    [Fact]
    public void CellProjection_UnknownIsEmptyAndLivePublicationUsesSameTile()
    {
        var tile = new BrowserGridTile(Entries(1)[0], 0);
        Assert.Equal("", BrowserDetails.Text(tile, "frame-rate"));
        Assert.Equal("", BrowserDetails.Text(tile, "dimensions"));
        Assert.Equal("", BrowserDetails.Text(tile, "subclips"));
        var revision = tile.DetailsRevision;
        tile.ApplyMetadata(new(null, 5, "Sony", "A7", "Lens", 1920, 1080, 59.94));
        Assert.True(tile.DetailsRevision > revision);
        Assert.Equal("1920 × 1080", BrowserDetails.Text(tile, "dimensions"));
        Assert.Equal("59.94 fps", BrowserDetails.Text(tile, "frame-rate"));
        tile.SetClassification(new(tile.AssetId!.Value, 4, AssetFlag.Picked, null, ["Travel"]));
        Assert.Equal("★★★★", BrowserDetails.Text(tile, "rating"));
        Assert.Equal("Travel", BrowserDetails.Text(tile, "keywords"));
    }

    internal static MediaFolderEntry[] Entries(int count)
    {
        var root = Guid.NewGuid();
        return Enumerable.Range(0, count).Select(i => new MediaFolderEntry(root, $"clip{i:D5}.mp4", $"CLIP{i:D5}.MP4",
            $"clip{i:D5}.mp4", false, new(MediaTypeCategory.Video), 1234, DateTimeOffset.UtcNow, AssetId: Guid.NewGuid())).ToArray();
    }
}

[Collection("STA dispatcher tests")]
public sealed class BrowserDetailsWpfTests(ITestOutputHelper output)
{
    [Fact]
    public async Task ActualRows_VirtualizeTenThousandAssetsAndShareSortSelectionPreviewAndColumnState()
    {
        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();
            var directory = Path.Combine(Path.GetTempPath(), "lightflow-details-" + Guid.NewGuid().ToString("N"));
            var startup = await LightflowStorageCoordinator.StartAsync(directory);
            var storage = startup.Coordinator!;
            var window = new MainWindow(storage, startup.Status, startup.Diagnostic)
            { Left = -32000, Top = -32000, ShowInTaskbar = false, Width = 1440, Height = 900, WindowStartupLocation = WindowStartupLocation.Manual };
            try
            {
                window.Show();
                Assert.True(await window.StartupCompletion.WaitAsync(TimeSpan.FromSeconds(30)));
                var model = (BrowserGridModel)typeof(MainWindow).GetField("_browserGrid", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                model.Populate(BrowserDetailsTests.Entries(10000));
                window.BrowserEmptyState.Visibility = Visibility.Collapsed;
                model.SelectSingle(4);
                model.ToggleCtrl(7);
                var selection = model.SelectedAssetIdsInBrowserOrder.ToArray();
                var anchor = model.SelectionAnchorAssetId;
                var first = model.Tiles[0];
                var query = model.Query;
                var watch = Stopwatch.StartNew();
                window.ApplyBrowserLayout(BrowserLayoutMode.Details, true);
                await Pump();
                output.WriteLine($"Grid → Details, 10,000 assets: {watch.Elapsed.TotalMilliseconds:F1} ms");
                Assert.Same(query, model.Query);
                Assert.Equal(selection, model.SelectedAssetIdsInBrowserOrder);
                Assert.Equal(anchor, model.SelectionAnchorAssetId);
                Assert.Same(first, model.Tiles[0]);
                var realized = Children(window.BrowserGridRows).OfType<GridViewRowPresenter>().Count();
                output.WriteLine($"Realized Details rows: {realized}");
                Assert.InRange(realized, 1, 100);
                Assert.All(model.Rows, row => Assert.Single(row.Tiles));
                var rowPresenter = Children(window.BrowserGridRows).OfType<GridViewRowPresenter>().First();
                var visibleTile = (BrowserGridTile)rowPresenter.Content;
                visibleTile.ApplyMetadata(new(null, 10, null, null, null, 1920, 1080, 24));
                await Pump();
                Assert.Contains(Children(rowPresenter).OfType<TextBlock>(), text => text.Text == "1920 × 1080");
                var columnMenu = window.BrowserDetailsHeaders.ContextMenu;
                columnMenu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent));
                var flagChoice = columnMenu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Flag"));
                flagChoice.ApplyTemplate();
                var checkmark = (System.Windows.Shapes.Path)flagChoice.Template.FindName("CheckedIndicator", flagChoice);
                Assert.Equal(Visibility.Visible, checkmark.Visibility);
                flagChoice.IsChecked = false;
                Assert.Equal(Visibility.Collapsed, checkmark.Visibility);
                flagChoice.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                Assert.DoesNotContain(window.BrowserDetailsColumns, c => Equals(((GridViewColumnHeader)c.Header).Tag, "flag"));
                flagChoice.IsChecked = true;
                Assert.Equal(Visibility.Visible, checkmark.Visibility);
                flagChoice.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                Assert.Equal(10, window.BrowserDetailsColumns.Count);
                var name = window.BrowserDetailsColumns.Single(c => Equals(((GridViewColumnHeader)c.Header).Tag, "name"));
                // Reproduce the reported narrow-column case: rows must not center in unused viewport space.
                name.Width = 40;
                await Pump();
                AssertColumnOrigins();
                Assert.Equal(64, ((FrameworkElement)VisualTreeHelper.GetParent(rowPresenter)).ActualHeight);
                var paddingHeader = Children(window.BrowserDetailsHeaders).OfType<GridViewColumnHeader>()
                    .Single(h => h.Role == GridViewColumnHeaderRole.Padding);
                Assert.Same(window.FindResource("BrowserDetailsHeaderStyle"), paddingHeader.Style);
                name.Width = 310;
                await Pump();
                AssertColumnOrigins();
                window.BrowserDetailsColumns.Move(1, 3);
                ((GridViewColumnHeader)name.Header).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Assert.True(model.Query.SortDescending);
                Assert.Equal((int)BrowserSortMode.Name, window.BrowserSortCombo.SelectedIndex);
                Assert.Null(model.SelectionAnchorAssetId); // Existing explicit-query semantics.
                model.ToggleCtrl(model.Tiles.First(t => t.AssetId == selection[0]).Index);
                model.ToggleCtrl(model.Tiles.First(t => t.AssetId == selection[0]).Index);
                anchor = model.SelectionAnchorAssetId;
                await Pump();
                var viewer = Children(window.BrowserGridRows).OfType<ScrollViewer>().First();
                viewer.ScrollToVerticalOffset(150000);
                await Pump();
                Assert.InRange(Children(window.BrowserGridRows).OfType<GridViewRowPresenter>().Count(), 1, 100);
                watch.Restart();
                window.ApplyBrowserLayout(BrowserLayoutMode.Grid, true);
                await Pump();
                output.WriteLine($"Details → Grid, 10,000 assets: {watch.Elapsed.TotalMilliseconds:F1} ms");
                Assert.True(model.Query.SortDescending);
                Assert.Equal(selection.Order(), model.SelectedAssetIdsInBrowserOrder.Order());
                Assert.Equal(anchor, model.SelectionAnchorAssetId);
                window.ApplyBrowserLayout(BrowserLayoutMode.Details, true);
                await Pump();
                Assert.Equal(310, name.Width);
                Assert.Same(name, window.BrowserDetailsColumns[3]);
                var state = (WorkspaceStateService)typeof(MainWindow).GetField("_workspaceState", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                Assert.Equal("name", state.Current.Layout!.BrowserDetailsColumns!.Where(c => c.Visible).ElementAt(3).Id);
                Assert.Equal(BrowserLayoutMode.Details, state.Current.Layout.BrowserLayoutMode);

                void AssertColumnOrigins()
                {
                    var presenter = Children(window.BrowserGridRows).OfType<GridViewRowPresenter>().First();
                    var header = (GridViewColumnHeader)window.BrowserDetailsColumns[0].Header;
                    var rowX = presenter.TransformToAncestor(window.BrowserGridHost).Transform(new Point()).X;
                    var headerX = header.TransformToAncestor(window.BrowserGridHost).Transform(new Point()).X;
                    Assert.InRange(Math.Abs(rowX - headerX), 0, 1);
                }
            }
            finally
            {
                window.Close();
                await storage.DisposeAsync();
                try { Directory.Delete(directory, true); } catch (IOException) { }
            }
        });
    }

    private static async Task Pump() { await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); await Task.Delay(30); }
    private static IEnumerable<DependencyObject> Children(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var descendant in Children(child)) yield return descendant;
        }
    }
}
