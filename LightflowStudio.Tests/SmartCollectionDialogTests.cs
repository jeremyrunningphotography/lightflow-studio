using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using LightflowStudio;
using Xunit;

namespace LightflowStudio.Tests;

[Collection("STA dispatcher tests")]
public sealed class SmartCollectionDialogTests
{
    [Fact]
    public Task BlankCreationAndInlineAlternativesStayEditable() => StaDispatcher.RunAsync(() =>
    {
        TestWpfApplication.EnsureLoaded();
        var blank = new SmartCollectionDialog(new NoLocations(), [], [new(null, "Top level")], [], "", null, null, new(), false);
        var rows = (StackPanel)blank.FindName("FilterRows");
        Assert.Empty(rows.Children); Assert.Empty(blank.Query.Filters);
        ((Button)blank.FindName("AddFilterButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var row = Assert.IsType<BrowserFilterRowEditor>(Assert.Single(rows.Children.Cast<object>()));
        Assert.False(row.IsValid); Assert.Empty(blank.Query.Filters);
        row.Children.OfType<Button>().Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Empty(rows.Children);

        var query = BrowserQueryIntent.Capture(new() { SearchText = "ceremony", Filters =
            [BrowserFilterPredicate.ForMediaType(MediaTypeCategory.StillImage), BrowserFilterPredicate.ForMediaType(MediaTypeCategory.RawImage),
                BrowserFilterPredicate.ForText(BrowserFilterField.Flag, "Picked")] });
        var dialog = new SmartCollectionDialog(new NoLocations(), [], [new(null, "Top level")], [], "Picks", null, null, query, true);
        var savedRows = ((StackPanel)dialog.FindName("FilterRows")).Children.OfType<BrowserFilterRowEditor>().ToArray();
        Assert.Equal(3, savedRows.Length); Assert.Equal(2, savedRows[0].Predicates.Count);
        Assert.All(savedRows, r => Assert.True(r.IsValid));
        var host = (ContentControl)savedRows[0].Children[2];
        var choices = (Grid)host.Content;
        var popup = choices.Children.OfType<System.Windows.Controls.Primitives.Popup>().Single();
        var checks = (StackPanel)((ScrollViewer)((Border)popup.Child).Child).Content;
        var raw = checks.Children.OfType<CheckBox>().Single(c => (string)c.Content == "RAW");
        raw.IsChecked = false; raw.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));
        Assert.Single(savedRows[0].Predicates);
        Assert.DoesNotContain(dialog.Query.Filters, p => p.MediaTypeValue == MediaTypeCategory.RawImage);
        return Task.CompletedTask;
    });

    [Fact]
    public Task DateAlternativesRenderAndEditWithoutLosingSavedRanges() => StaDispatcher.RunAsync(() =>
    {
        TestWpfApplication.EnsureLoaded();
        var dates = new[] { BrowserFilterPredicate.ForDateRange(new(2026, 1, 1), new(2026, 1, 31)),
            BrowserFilterPredicate.ForDateRange(new(2026, 3, 1), new(2026, 3, 31)) };
        var row = new BrowserFilterRowEditor(BrowserFilterField.CaptureDate, dates, [], _ => true, () => { }, () => { });
        var host = new Border { Background = (Brush)Application.Current.FindResource("CardBrush"), Padding = new Thickness(12), Child = row };
        host.Measure(new Size(700, double.PositiveInfinity)); host.Arrange(new Rect(new Point(), new Size(700, host.DesiredSize.Height))); host.UpdateLayout();
        Assert.Equal(dates, row.Predicates);
        var panel = (StackPanel)((ContentControl)row.Children[2]).Content;
        var firstRange = panel.Children.OfType<StackPanel>().First();
        firstRange.Children.OfType<DatePicker>().First().SelectedDate = new(2026, 1, 2);
        Assert.Equal(new DateTime(2026, 1, 2), row.Predicates[0].DateFrom);
        Assert.Equal(dates[1], row.Predicates[1]);
        var bitmap = new RenderTargetBitmap(700, (int)Math.Ceiling(host.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(host); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(Root(), ".task-notes", "smart-date-rows.png")); encoder.Save(stream);
        return Task.CompletedTask;
    });

    [Fact]
    public Task DialogPrepopulationAndOffscreenLayoutUseSharedResourcesWithoutOpeningAWindow() => StaDispatcher.RunAsync(() =>
    {
        TestWpfApplication.EnsureLoaded();
        var root = Guid.NewGuid(); var parent = Guid.NewGuid(); var collection = Guid.NewGuid();
        var source = new SmartCollectionSource(SmartCollectionSourceKind.Folder, root, "Projects/2026", IncludeSubfolders: true);
        var query = new BrowserQueryIntent { SearchText = "portrait", Filters = [BrowserFilterPredicate.ForRating(BrowserNumberComparison.GreaterThanOrEqual, 4)] };
        var dialog = new SmartCollectionDialog(new NoLocations(), [], [new(null, "Top level"), new(parent, "Portfolio")],
            [(collection, "Favorites")], "Portrait picks", parent, source, query, true);
        Assert.Equal(source, dialog.Source); Assert.Equal(parent, dialog.ParentSetId);
        Assert.Equal(query.Serialize(), dialog.Query.Serialize());
        Assert.Equal("Portrait picks", dialog.CollectionName);
        var content = (FrameworkElement)dialog.Content;
        content.Measure(new Size(742, double.PositiveInfinity));
        content.Arrange(new Rect(new Point(), new Size(742, content.DesiredSize.Height))); content.UpdateLayout();
        var match = VisualChildren((ContentControl)dialog.FindName("MatchHost")).OfType<ComboBox>().Single();
        match.SelectedValue = BrowserMatchMode.Any;
        Assert.Equal(BrowserMatchMode.Any, dialog.Query.MatchMode);
        match.SelectedValue = BrowserMatchMode.All;
        Assert.InRange(content.ActualHeight, 300, 780);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(content);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var output = Path.Combine(Root(), ".task-notes", "smart-collection-dialog.png"); Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        using (var stream = File.Create(output)) encoder.Save(stream);
        var type = (ComboBox)dialog.FindName("SourceKindCombo"); type.SelectedIndex = 1;
        var choices = (ComboBox)dialog.FindName("SourceCollectionCombo"); choices.SelectedIndex = 0;
        Assert.Equal(collection, dialog.Source!.CollectionId); Assert.False(dialog.Source.IncludeSubfolders);
        type.SelectedIndex = -1; Assert.Null(dialog.Source);
        Assert.Equal(Visibility.Collapsed, ((StackPanel)dialog.FindName("FolderPanel")).Visibility);
        return Task.CompletedTask;
    });

    [Fact]
    public void CreationEntryPointsAndDistinctIconArePresentInExistingBrowserSurfaces()
    {
        var document = XDocument.Load(Path.Combine(Root(), "LightflowStudio", "MainWindow.xaml"));
        var x = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
        var creations = document.Descendants().Where(e => (string?)e.Attribute("Click") == "BrowserCreateSmartCollection_Click").ToArray();
        Assert.Contains(creations, e => e.Name.LocalName == "Button");
        Assert.Contains(creations, e => e.Name.LocalName == "MenuItem");
        var blankCreations = document.Descendants().Where(e => (string?)e.Attribute("Click") == "BrowserNewSmartCollection_Click").ToArray();
        Assert.Contains(blankCreations, e => e.Name.LocalName == "Button");
        Assert.Contains(blankCreations, e => (string?)e.Attribute(x + "Name") == "BrowserSetNewSmartMenu");
        Assert.Contains(document.Descendants(), e => (string?)e.Attribute(x + "Name") == "BrowserSetNewCollectionMenu" &&
            (string?)e.Attribute("Click") == "BrowserNewCollection_Click");
        Assert.Contains(document.Descendants(), e => (string?)e.Attribute("Binding") == "{Binding IsSmartCollection}");
        var grid = document.Descendants().Single(e => (string?)e.Attribute(x + "Name") == "BrowserGridRows");
        Assert.Equal("True", (string?)grid.Attribute("VirtualizingPanel.IsVirtualizing"));
    }

    private sealed class NoLocations : IBrowserLocationResolver
    {
        public Task<BrowserLocationResolution> ResolveAsync(string absoluteFolder, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
    private static IEnumerable<DependencyObject> VisualChildren(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i); yield return child;
            foreach (var descendant in VisualChildren(child)) yield return descendant;
        }
    }
    private static string Root()
    {
        var path = new DirectoryInfo(AppContext.BaseDirectory);
        while (path is not null && !File.Exists(Path.Combine(path.FullName, "AGENTS.md"))) path = path.Parent;
        return path?.FullName ?? throw new InvalidOperationException("Repository not found.");
    }
}
