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
        content.Measure(new Size(602, double.PositiveInfinity));
        content.Arrange(new Rect(new Point(), new Size(602, content.DesiredSize.Height))); content.UpdateLayout();
        Assert.InRange(content.ActualHeight, 400, 780);
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
    private static string Root()
    {
        var path = new DirectoryInfo(AppContext.BaseDirectory);
        while (path is not null && !File.Exists(Path.Combine(path.FullName, "AGENTS.md"))) path = path.Parent;
        return path?.FullName ?? throw new InvalidOperationException("Repository not found.");
    }
}
