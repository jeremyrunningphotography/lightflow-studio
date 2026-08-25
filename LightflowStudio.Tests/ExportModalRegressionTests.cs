using System.Xml.Linq;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class ExportModalRegressionTests
{
    [Fact]
    public void ModalIsOwnedFocusedAndDoesNotPresentRuntimeProgress()
    {
        var root = FindRepositoryRoot();
        var xaml = XDocument.Load(Path.Combine(root, "LightflowStudio", "ExportDialog.xaml"));
        var text = File.ReadAllText(Path.Combine(root, "LightflowStudio", "ExportDialog.xaml.cs"));
        Assert.Equal("CenterOwner", (string?)xaml.Root!.Attribute("WindowStartupLocation"));
        Assert.Contains("Estimate unavailable", xaml.ToString());
        Assert.DoesNotContain("ProgressBar", xaml.Descendants().Select(x => x.Name.LocalName));
        Assert.Contains("_coordinator.Queue(plan); DialogResult=true", text);
        Assert.DoesNotContain("await runtime.Completion", text);
    }

    [Fact]
    public void BrowserAndPlayerShareModalPathWithoutEncodingWorkspaceNavigation()
    {
        var source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "LightflowStudio", "MainWindow.xaml.cs"));
        var start = source.IndexOf("private async Task ApplyEncodingHandoffAsync", StringComparison.Ordinal);
        var end = source.IndexOf("private async Task RefreshDependencyHealthAsync", start, StringComparison.Ordinal);
        var method = source[start..end];
        Assert.Contains("new ExportDialog", method);
        Assert.Contains("dialog.ShowDialog()", method);
        Assert.DoesNotContain("ShellWorkspace.Encoding", method);
        Assert.Contains("ExportBrowserAssetsAsync([e.AssetId])", source);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Directory.Build.props"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException();
    }
}
