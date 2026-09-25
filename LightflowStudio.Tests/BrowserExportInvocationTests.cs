using System.Xml.Linq;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class BrowserExportInvocationTests
{
    [Fact]
    public void ToolbarChoicesReuseExistingContextExportHandlersWithoutNewMaterialization()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "LightflowStudio", "MainWindow.xaml")))
            root = root.Parent;
        Assert.NotNull(root);
        var xaml = XDocument.Load(Path.Combine(root.FullName, "LightflowStudio", "MainWindow.xaml"));
        var source = File.ReadAllText(Path.Combine(root.FullName, "LightflowStudio", "MainWindow.xaml.cs"));
        var x = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
        var button = xaml.Descendants().Single(e => (string?)e.Attribute(x + "Name") == "BrowserExportButton");
        Assert.Equal("Export…", (string?)button.Attribute("Content"));
        Assert.Equal("BrowserExport_Click", (string?)button.Attribute("Click"));
        Assert.Equal("BrowserExport_ContextMenuOpening", (string?)button.Attribute("ContextMenuOpening"));
        var menu = button.Descendants().Single(e => e.Name.LocalName == "ContextMenu");
        Assert.Equal("{Binding ElementName=BrowserExportButton}", (string?)menu.Attribute("PlacementTarget"));
        var choices = menu.Elements().ToArray();
        Assert.Equal(new[] { "Export videos", "Export subclips" }, choices.Select(e => (string?)e.Attribute("Header")));
        Assert.Equal(new[] { "BrowserContextExport_Click", "BrowserContextExportSubclips_Click" },
            choices.Select(e => (string?)e.Attribute("Click")));
        Assert.Contains("BrowserContextExport_Click(object sender, RoutedEventArgs e) => await ExportBrowserSelectionAsync();", source);
        Assert.Contains("BrowserContextExportSubclips_Click(object sender, RoutedEventArgs e) =>\n        await ExportBrowserSubclipsAsync();",
            source.Replace("\r\n", "\n"));
        var click = source[source.IndexOf("private async void BrowserExport_Click", StringComparison.Ordinal)..
            source.IndexOf("private async void BrowserContextExport_Click", StringComparison.Ordinal)];
        Assert.Contains("CurrentBrowserSelectionActions()", click);
        Assert.Contains("if (!state.CanExport) return;", click);
        Assert.Contains("if (state.ShowExportMenu)", click);
        Assert.Contains("BrowserExportMenu.IsOpen = true;", click);
        Assert.Contains("await ExportBrowserSelectionAsync();", click);
        Assert.DoesNotContain("_storage", click);
        Assert.DoesNotContain("Materialize", click);
    }
}
