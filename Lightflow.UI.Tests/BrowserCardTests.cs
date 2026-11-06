using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Lightflow.UI;
using LightflowStudio;
using Xunit;

namespace Lightflow.UI.Tests;

public sealed partial class BrowserTests
{
    [Theory]
    [InlineData(179, 1)] [InlineData(180, 1)] [InlineData(359, 1)] [InlineData(360, 2)] [InlineData(900, 5)]
    public void GridColumnsRespectWindowsTrailingGutter(double width, int expected) => Assert.Equal(expected, BrowserCardLayout.Columns(width));

    [Fact]
    public void LargeFolderRowsAreLazyViewsOverTheSameAssetSnapshot()
    {
        var assets = Enumerable.Range(0, 20000).Select(i => Asset(Guid.Empty, $"{i}.jpg")).ToArray();
        var rows = new BrowserCardRows(assets, 5);
        Assert.Equal(4000, rows.Count);
        var end = Assert.IsType<BrowserCardRow>(rows[3999]);
        Assert.Same(assets, end.Assets); Assert.Equal(19995, end.Start); Assert.Equal(5, end.Count);
        var reflow = new BrowserCardRows(assets, 3);
        var last = Assert.IsType<BrowserCardRow>(reflow[reflow.Count - 1]);
        Assert.Same(assets, last.Assets); Assert.Equal(2, last.Count);
        Assert.Equal(assets[^1].AssetId, last.Assets[last.Start + last.Count - 1].AssetId);
    }

    [AvaloniaFact]
    public async Task GridReflowModesScrollAndRefreshRetainSharedIdentityAndBoundVisuals()
    {
        var service = new FakeBrowser(); var model = new BrowserViewModel(service);
        await model.OpenAsync(service.CatalogId);
        var loading = model.NavigateAsync(model.Roots[0]);
        var assets = Enumerable.Range(0, 10000).Select(i => Asset(service.RootId, $"folder/{i:D5}.jpg")).ToArray();
        service.Folders[""].SetResult(new(service.RootId, "", false, MediaFolderEnumerationStatus.Succeeded, [], assets));
        await loading; service.Source.SetResult(true);
        var pane = new BrowserPane(model);
        var window = new Window { Content = pane, Width = 1440, Height = 800 }; LightflowTheme.Apply(window); window.Show();
        void Layout() { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
        Layout();
        var list = pane.GetVisualDescendants().OfType<ListBox>().Single();
        Assert.InRange(pane.CardColumns, 3, 7);
        Assert.InRange(list.GetVisualDescendants().OfType<BrowserGridCard>().Count(), 1, 64);
        await model.SelectAsync(assets[12].AssetId); Layout();
        var originalSnapshot = model.Assets;
        foreach (var mode in Enum.GetValues<BrowserCardMode>())
        {
            pane.SetCardMode(mode); Layout();
            Assert.Same(originalSnapshot, model.Assets); Assert.Equal(assets[12].AssetId, model.SelectedAssetId);
            var card = list.GetVisualDescendants().OfType<BrowserGridCard>().First();
            Assert.Equal(BrowserCardLayout.Height(mode), card.Height);
            var filename = card.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text?.EndsWith(".jpg") == true);
            Assert.Equal(mode != BrowserCardMode.Preview, filename.IsVisible);
        }
        var columns = pane.CardColumns; window.Width = 1050; Layout(); Assert.True(pane.CardColumns < columns);
        Assert.Same(originalSnapshot, model.Assets); Assert.Equal(assets[12].AssetId, model.SelectedAssetId);
        list.Focus(); list.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Down }); Layout();
        Assert.Equal(assets[12 + pane.CardColumns].AssetId, model.SelectedAssetId);
        list.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.End }); Layout();
        Assert.Equal(assets[^1].AssetId, model.SelectedAssetId);
        Assert.InRange(list.GetVisualDescendants().OfType<BrowserGridCard>().Count(), 1, 64);
        var refresh = model.RefreshAsync(); service.Folders[""].SetResult(new(service.RootId, "", false, MediaFolderEnumerationStatus.Succeeded, [], assets.ToArray())); await refresh; Layout();
        Assert.Equal(assets[^1].AssetId, model.SelectedAssetId);
        window.Close(); model.Suspend(); Assert.Equal(0, BrowserThumbnailCell.RetainedBitmaps);
    }

    [Fact]
    public async Task DesktopRangeToggleAndRefreshUseTheSharedSelectionSet()
    {
        var service = new FakeBrowser(); var model = new BrowserViewModel(service); await model.OpenAsync(service.CatalogId);
        var loading = model.NavigateAsync(model.Roots[0]);
        var assets = Enumerable.Range(0, 10).Select(i => Asset(service.RootId, $"{i}.jpg")).ToArray();
        service.Folders[""].SetResult(new(service.RootId, "", false, MediaFolderEnumerationStatus.Succeeded, [], assets));
        await loading; service.Source.SetResult(true);
        await model.SelectGridAsync(assets[2].AssetId); await model.SelectGridAsync(assets[5].AssetId, range: true);
        Assert.Equal(4, model.SelectedAssetKeys.Count); Assert.True(model.IsAssetSelected(assets[2].AssetId));
        await model.SelectGridAsync(assets[3].AssetId, toggle: true); Assert.Equal(3, model.SelectedAssetKeys.Count);
        var refresh = model.RefreshAsync(); service.Folders[""].SetResult(new(service.RootId, "", false, MediaFolderEnumerationStatus.Succeeded, [], assets.Where((_, i) => i != 2).ToArray()));
        await refresh; Assert.Equal(2, model.SelectedAssetKeys.Count); Assert.True(model.IsAssetSelected(assets[5].AssetId));
        model.SelectAllGrid(); Assert.Equal(9, model.SelectedAssetKeys.Count);
        model.Suspend(); Assert.Empty(model.SelectedAssetKeys);
    }

    [AvaloniaFact]
    public async Task NativePointerModifiersReachSharedSelectionWithoutRowOverwrite()
    {
        var service = new FakeBrowser(); var model = new BrowserViewModel(service); await model.OpenAsync(service.CatalogId);
        var load = model.NavigateAsync(model.Roots[0]); var assets = Enumerable.Range(0, 20).Select(i => Asset(service.RootId, $"{i}.jpg")).ToArray();
        service.Folders[""].SetResult(new(service.RootId, "", false, MediaFolderEnumerationStatus.Succeeded, [], assets)); await load; service.Source.SetResult(true);
        var pane = new BrowserPane(model); var window = new Window { Content = pane, Width = 1440, Height = 800 }; LightflowTheme.Apply(window); window.Show(); window.UpdateLayout();
        void Click(int index, RawInputModifiers modifiers = 0)
        {
            var card = pane.GetVisualDescendants().OfType<BrowserGridCard>().Single(c => c.AssetId == assets[index].AssetId);
            var point = card.TranslatePoint(new Point(30, 30), window)!.Value;
            window.MouseDown(point, MouseButton.Left, modifiers); window.MouseUp(point, MouseButton.Left, modifiers); window.UpdateLayout();
        }
        Click(0); Assert.Single(model.SelectedAssetKeys);
        Click(1, RawInputModifiers.Meta); Assert.Equal(2, model.SelectedAssetKeys.Count);
        Click(3, RawInputModifiers.Shift); Assert.Equal(3, model.SelectedAssetKeys.Count);
        Assert.True(model.IsAssetSelected(assets[1].AssetId)); Assert.True(model.IsAssetSelected(assets[3].AssetId));
        window.Close(); model.Suspend();
    }

    [Theory]
    [InlineData(3, 12, 5, Key.Down, 8)] [InlineData(8, 12, 5, Key.Down, 11)]
    [InlineData(0, 12, 5, Key.Left, 0)] [InlineData(11, 12, 5, Key.Right, 11)]
    public void KeyboardMovesByVisualColumnsAndClampsIncompleteRows(int index, int count, int columns, Key key, int expected) =>
        Assert.Equal(expected, BrowserCardLayout.Move(index, count, columns, key));
}
