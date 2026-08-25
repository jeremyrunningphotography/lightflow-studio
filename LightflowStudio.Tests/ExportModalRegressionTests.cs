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

    [Fact]
    public void ModalUsesDarkChromeAndCompactPrimaryLayoutWithoutLeftScrollbar()
    {
        var root = FindRepositoryRoot();
        var xaml = XDocument.Load(Path.Combine(root, "LightflowStudio", "ExportDialog.xaml"));
        var source = File.ReadAllText(Path.Combine(root, "LightflowStudio", "ExportDialog.xaml.cs"));
        Assert.Equal("1120", (string?)xaml.Root!.Attribute("Width"));
        Assert.Equal("800", (string?)xaml.Root.Attribute("Height"));
        Assert.Contains("WindowAppearance.EnableDarkTitleBar(this)", source);
        var composer = Named(xaml, "NamePartsComposer");
        Assert.Equal("ItemsControl", composer.Name.LocalName);
        Assert.Contains(composer.Descendants(), x => x.Name.LocalName == "WrapPanel");
        Assert.DoesNotContain(xaml.Descendants(), x => x.Name.LocalName == "ListBox");
        Assert.DoesNotContain(composer.Ancestors(), x => x.Name.LocalName == "ScrollViewer");
        Assert.NotNull(Named(xaml, "NamePreview"));
        Assert.NotNull(Named(xaml, "CameraCombo"));
    }

    [Fact]
    public void ComposerHasDirectRemoveDragAndKeyboardReorderAccessibility()
    {
        var xaml = XDocument.Load(Path.Combine(FindRepositoryRoot(), "LightflowStudio", "ExportDialog.xaml"));
        var composer = Named(xaml, "NamePartsComposer");
        Assert.Contains(composer.Descendants(), x => (string?)x.Attribute("Drop") == "NamePart_Drop");
        Assert.Contains(composer.Descendants(), x => (string?)x.Attribute("PreviewMouseMove") == "NamePart_MouseMove");
        Assert.Contains(composer.Descendants(), x => (string?)x.Attribute("Click") == "MovePartEarlier_Click" && ((string?)x.Attribute("AutomationProperties.Name"))?.Contains("MoveEarlierAutomationName") == true);
        Assert.Contains(composer.Descendants(), x => (string?)x.Attribute("Click") == "MovePartLater_Click" && ((string?)x.Attribute("AutomationProperties.Name"))?.Contains("MoveLaterAutomationName") == true);
        Assert.Contains(composer.Descendants(), x => (string?)x.Attribute("Click") == "RemovePart_Click" && ((string?)x.Attribute("AutomationProperties.Name"))?.Contains("RemoveAutomationName") == true);
        Assert.Contains(composer.Descendants(), x => (string?)x.Attribute("TextChanged") == "CustomPartText_Changed");
    }

    [Fact]
    public void OverwriteAndFriendlyChoicePresentationReplaceRawEnums()
    {
        var root = FindRepositoryRoot();
        var xaml = XDocument.Load(Path.Combine(root, "LightflowStudio", "ExportDialog.xaml"));
        var source = File.ReadAllText(Path.Combine(root, "LightflowStudio", "ExportDialog.xaml.cs"));
        Assert.Equal("Overwrite existing files", (string?)Named(xaml, "OverwriteExistingCheck").Attribute("Content"));
        Assert.DoesNotContain(xaml.Descendants(), x => x.Name.LocalName == "ComboBox" && (string?)x.Attribute("Name") == "ExistingCombo");
        foreach (var name in new[] { "ContainerCombo", "CodecCombo", "RateControlCombo", "ResolutionCombo", "EncoderCombo", "TuneCombo", "MultipassCombo", "PixelFormatCombo" })
            Assert.Equal("Label", (string?)Named(xaml, name).Attribute("DisplayMemberPath"));
        Assert.DoesNotContain("Enum.GetValues", source);
    }

    [Fact]
    public void AdvancedDisclosureAndContentsUseReadableThemeResources()
    {
        var xaml = XDocument.Load(Path.Combine(FindRepositoryRoot(), "LightflowStudio", "ExportDialog.xaml"));
        var advanced = Named(xaml, "AdvancedExpander");
        Assert.Contains("AdvancedDisclosure", (string?)advanced.Attribute("Style"));
        var text = xaml.ToString();
        Assert.Contains("▸", text); Assert.Contains("▾", text);
        Assert.Contains("AdvancedLabel", text);
        Assert.Contains("NavigationTextBrush", text);
        Assert.DoesNotContain("Foreground=\"Black\"", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Advanced export settings", text);
    }

    private static XElement Named(XDocument document, string name) => document.Descendants().Single(x =>
        x.Attributes().Any(attribute => attribute.Name.LocalName == "Name" && attribute.Value == name));

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
