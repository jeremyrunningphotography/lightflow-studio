using System.Xml.Linq;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class FileOperationPresentationRegressionTests
{
    [Fact]
    public void FileDragFeedback_UsesDedicatedTopmostBrowserSurfaceAndNativeDragRenderTicks()
    {
        var root = FindRepositoryRoot();
        var document = XDocument.Load(Path.Combine(root, "LightflowStudio", "MainWindow.xaml"));
        var target = Named(document, "BrowserFileDragAdornerTarget");
        var decorator = target.Parent!;
        Assert.Equal("AdornerDecorator", decorator.Name.LocalName);
        Assert.Equal("100", (string?)decorator.Attribute("Panel.ZIndex"));
        Assert.Equal("False", (string?)decorator.Attribute("IsHitTestVisible"));
        Assert.Equal("BrowserFileDrag_GiveFeedback", (string?)Named(document, "BrowserFolderTree").Attribute("GiveFeedback"));
        Assert.Contains(document.Descendants().Where(element => element.Name.LocalName == "Border"),
            element => (string?)element.Attribute("MouseMove") == "BrowserGridTile_MouseMove" &&
                       (string?)element.Attribute("GiveFeedback") == "BrowserFileDrag_GiveFeedback");

        var code = File.ReadAllText(Path.Combine(root, "LightflowStudio", "MainWindow.xaml.cs"));
        Assert.Contains("GetAdornerLayer(BrowserFileDragAdornerTarget)", code);
        Assert.Contains("new FileDragAdorner(BrowserFileDragAdornerTarget", code);
        Assert.Contains("_fileDragAdorner?.RefreshPosition();", code);
    }

    [Fact]
    public void DirectAndPromotedOperationsSynchronizeOneCompletedMutationBatchBeforeTerminalPresentation()
    {
        var root = FindRepositoryRoot();
        var main = File.ReadAllText(Path.Combine(root, "LightflowStudio", "MainWindow.xaml.cs"));
        var operations = File.ReadAllText(Path.Combine(root, "LightflowStudio", "FileOperations.cs"));
        Assert.DoesNotContain("MutationCompleted", operations);
        Assert.Contains("await SynchronizeFileSystemMutationsAsync(result.CompletedMutations);", main);
        Assert.Contains("synchronizePresentation", operations);
        Assert.Contains("await _synchronizePresentation(result)", operations);
        Assert.True(operations.IndexOf("await _synchronizePresentation(result)", StringComparison.Ordinal) <
                    operations.IndexOf("State = result.State", StringComparison.Ordinal));
    }

    [Fact]
    public void TileDragResolvesSourcesFromTheComputedDragAssetIds()
    {
        var code = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "LightflowStudio", "MainWindow.xaml.cs"));
        var start = code.IndexOf("private async void BrowserGridTile_MouseMove", StringComparison.Ordinal);
        var end = code.IndexOf("private void BrowserGridTile_DragOver", start, StringComparison.Ordinal);
        var body = code[start..end];
        Assert.Contains("FileOperationSourcesAsync(ids)", body);
        Assert.DoesNotContain("SelectedFileOperationSourcesAsync()", body);
    }

    private static XElement Named(XDocument document, string name) => document.Descendants().Single(element =>
        element.Attributes().Any(attribute => attribute.Name.LocalName == "Name" && attribute.Value == name));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Directory.Build.props"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not find the Lightflow Studio repository root.");
    }
}
