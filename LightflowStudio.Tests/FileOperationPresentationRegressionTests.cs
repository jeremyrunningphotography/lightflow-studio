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
        Assert.Equal("BrowserFolderTree_PreviewMouseLeftButtonUp",
            (string?)Named(document, "BrowserFolderTree").Attribute("PreviewMouseLeftButtonUp"));
        var workspace = Named(document, "BrowserWorkspaceRoot");
        Assert.Null(workspace.Attribute("PreviewMouseMove"));
        Assert.Equal("BrowserGridRows_PreviewMouseMove", (string?)Named(document, "BrowserGridRows").Attribute("PreviewMouseMove"));
        Assert.Equal("BrowserFileDrag_GiveFeedback", (string?)workspace.Attribute("GiveFeedback"));

        var code = File.ReadAllText(Path.Combine(root, "LightflowStudio", "MainWindow.xaml.cs"));
        Assert.Contains("GetAdornerLayer(BrowserFileDragAdornerTarget)", code);
        Assert.Contains("new FileDragAdorner(BrowserFileDragAdornerTarget", code);
        Assert.Contains("tile.ThumbnailPath, tile.CategoryGlyph", code);
        Assert.Contains("var layers = Math.Min(_count, 3);", code);
        Assert.Contains("FileDragTargetState.AddToCollection => \"ADD TO COLLECTION\"", code);
        Assert.Contains("FileDragTargetState.Invalid => \"CAN'T DROP\"", code);
        Assert.Contains("var cursor = Forms.Cursor.Position;", code);
        Assert.Contains("e.UseDefaultCursors = false;", code);
        Assert.Contains("System.Windows.Threading.DispatcherPriority.Render", code);
        Assert.Contains("adorner.RefreshPosition();", code);
        Assert.Contains("private void BrowserFolderTree_PreviewMouseLeftButtonUp", code);
        Assert.Contains("var commitDeferredSelection = ReferenceEquals(tile, _browserAssetPendingSingleSelection);", code);
        Assert.Contains("_browserAssetDragTile = null;", code);
    }

    [Fact]
    public void DirectAndPromotedOperationsSynchronizeOneCompletedMutationBatchBeforeTerminalPresentation()
    {
        var root = FindRepositoryRoot();
        var main = File.ReadAllText(Path.Combine(root, "LightflowStudio", "MainWindow.xaml.cs"));
        var operations = File.ReadAllText(Path.Combine(root, "LightflowStudio", "FileOperations.cs"));
        Assert.DoesNotContain("MutationCompleted", operations);
        Assert.Contains("await SynchronizeFileSystemMutationsAsync(result.CompletedMutations);", main);
        Assert.Contains("result.Failures.Select(failure =>", main);
        Assert.Contains("NoticeDialog.Show(this, \"File operation\", heading, diagnostic);", main);
        Assert.Contains("await RefreshActiveDirectFolderAfterMutationAsync(location);", main);
        Assert.Contains("ApplyBrowserState(current with", main);
        Assert.Contains("_fileSystemMutationPresentationDepth++", main);
        Assert.Contains("finally { _fileSystemMutationPresentationDepth--; }", main);
        Assert.Contains("_activeCollectionScope is not null || _fileSystemMutationPresentationDepth > 0", main);
        Assert.Contains("synchronizePresentation", operations);
        Assert.Contains("await _synchronizePresentation(result)", operations);
        Assert.True(operations.IndexOf("await _synchronizePresentation(result)", StringComparison.Ordinal) <
                    operations.IndexOf("State = result.State", StringComparison.Ordinal));
    }

    [Fact]
    public void DerivedWorkCompletionProjectsFinalPreviewBeforeDetachingItsHandler()
    {
        var code = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "LightflowStudio", "MainWindow.xaml.cs"));
        var start = code.IndexOf("private async Task CompleteBrowserDerivedWorkProjectionAsync", StringComparison.Ordinal);
        var end = code.IndexOf("private Task ApplyBrowserDerivedWorkResultsAsync", start, StringComparison.Ordinal);
        var body = code[start..end];
        Assert.True(body.IndexOf("await batch.Completion", StringComparison.Ordinal) <
                    body.IndexOf("ApplyBrowserDerivedWorkResultsAsync(batch, generation)", StringComparison.Ordinal));
        Assert.True(body.IndexOf("ApplyBrowserDerivedWorkResultsAsync(batch, generation)", StringComparison.Ordinal) <
                    body.IndexOf("batch.ProgressChanged -= handler", StringComparison.Ordinal));
    }

    [Fact]
    public void TileDragResolvesSourcesFromTheComputedDragAssetIds()
    {
        var code = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "LightflowStudio", "MainWindow.xaml.cs"));
        var start = code.IndexOf("private async void BrowserGridRows_PreviewMouseMove", StringComparison.Ordinal);
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
