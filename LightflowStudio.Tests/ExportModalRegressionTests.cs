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
        Assert.DoesNotContain("Estimate unavailable", xaml.ToString());
        Assert.DoesNotContain("Ready to export", xaml.ToString());
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
        Assert.Contains("AdvancedToggle", text);
        Assert.DoesNotContain("Blue", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ComposerIsCompactShowsNonInteractiveTerminalExtensionAndAlignsAddRow()
    {
        var xaml = XDocument.Load(Path.Combine(FindRepositoryRoot(), "LightflowStudio", "ExportDialog.xaml"));
        var composer = Named(xaml, "NamePartsComposer");
        var template = composer.Descendants().Single(x => x.Name.LocalName == "DataTemplate");
        var mainStack = template.Descendants().First(x => x.Name.LocalName == "StackPanel");
        Assert.Null(mainStack.Attribute("Orientation"));
        Assert.Contains(mainStack.Descendants(), x => x.Name.LocalName == "StackPanel" && (string?)x.Attribute("Orientation") == "Horizontal");
        var extension = Named(xaml, "ExtensionPreview");
        Assert.DoesNotContain(extension.Ancestors(), x => x == composer);
        Assert.Null(extension.Attribute("AllowDrop"));
        var add = xaml.Descendants().Single(x => (string?)x.Attribute("Click") == "AddPart_Click");
        Assert.Equal("+ Add", (string?)add.Attribute("Content"));
        Assert.Equal((string?)Named(xaml, "AddPartCombo").Attribute("Height"), (string?)add.Attribute("Height"));
    }

    [Fact]
    public void OutputExampleIsSingleLineEllipsizedWithFullPathTooltipWiring()
    {
        var root = FindRepositoryRoot();
        var xaml = XDocument.Load(Path.Combine(root, "LightflowStudio", "ExportDialog.xaml"));
        var preview = Named(xaml, "PathPreview");
        Assert.Equal("CharacterEllipsis", (string?)preview.Attribute("TextTrimming"));
        Assert.Equal("NoWrap", (string?)preview.Attribute("TextWrapping"));
        var source = File.ReadAllText(Path.Combine(root, "LightflowStudio", "ExportDialog.xaml.cs"));
        Assert.Contains("OutputExampleBorder.ToolTip = _model.PreviewPath", source);
        Assert.Equal("Right", (string?)Named(xaml, "OutputFilenamePreview").Attribute("DockPanel.Dock"));
    }

    [Fact]
    public void ValidationCollapsesWhenEmptyAndNoPermanentPreflightCardExists()
    {
        var root = FindRepositoryRoot();
        var xaml = XDocument.Load(Path.Combine(root, "LightflowStudio", "ExportDialog.xaml"));
        Assert.Equal("Collapsed", (string?)Named(xaml, "ValidationBorder").Attribute("Visibility"));
        Assert.DoesNotContain(xaml.Descendants(), x => (string?)x.Attribute("Text") == "Preflight");
        var source = File.ReadAllText(Path.Combine(root, "LightflowStudio", "ExportDialog.xaml.cs"));
        Assert.Contains("lines.Count == 0 ? Visibility.Collapsed : Visibility.Visible", source);
    }

    [Fact]
    public void PrimarySettingsAreSingleColumnAndExposeConditionalQualityParameters()
    {
        var xaml = XDocument.Load(Path.Combine(FindRepositoryRoot(), "LightflowStudio", "ExportDialog.xaml"));
        var form = Named(xaml, "PrimarySettings");
        Assert.Equal(2, form.Elements().Single(x => x.Name.LocalName == "Grid.ColumnDefinitions").Elements().Count());
        Assert.DoesNotContain(form.Descendants(), x => x.Name.LocalName == "UniformGrid");
        foreach (var name in new[] { "QualityText", "TargetText", "MaxText", "CbrText" }) Assert.NotNull(Named(xaml, name));
        var source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "LightflowStudio", "ExportDialog.xaml.cs"));
        Assert.Contains("RateControlMode.ConstantQuality", source);
        Assert.Contains("RateControlMode.VariableBitrate", source);
        Assert.Contains("RateControlMode.ConstantBitrate", source);
    }

    [Fact]
    public void EveryPrimarySettingHasMeaningfulTooltip()
    {
        var xaml = XDocument.Load(Path.Combine(FindRepositoryRoot(), "LightflowStudio", "ExportDialog.xaml"));
        foreach (var name in new[] { "ContainerCombo", "CodecCombo", "ResolutionCombo", "FrameRateCombo", "RateControlCombo",
                     "QualityText", "TargetText", "MaxText", "CbrText", "AudioCombo", "EncoderCombo", "ParallelCombo" })
        {
            var tooltip = (string?)Named(xaml, name).Attribute("ToolTip");
            Assert.True(tooltip?.Length > 30, $"{name} needs a meaningful tooltip.");
        }
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
