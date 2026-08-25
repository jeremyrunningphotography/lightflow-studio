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
    public void ModalUsesDarkChromeAndCompactPrimaryLayoutWithAutomaticContentScrolling()
    {
        var root = FindRepositoryRoot();
        var xaml = XDocument.Load(Path.Combine(root, "LightflowStudio", "ExportDialog.xaml"));
        var source = File.ReadAllText(Path.Combine(root, "LightflowStudio", "ExportDialog.xaml.cs"));
        Assert.Equal("1120", (string?)xaml.Root!.Attribute("Width"));
        Assert.Equal("860", (string?)xaml.Root.Attribute("Height"));
        Assert.Contains("WindowAppearance.EnableDarkTitleBar(this)", source);
        var composer = Named(xaml, "NamePartsComposer");
        Assert.Equal("ItemsControl", composer.Name.LocalName);
        Assert.Contains(composer.Descendants(), x => x.Name.LocalName == "WrapPanel");
        Assert.DoesNotContain(xaml.Descendants(), x => x.Name.LocalName == "ListBox");
        var contentScroll = Named(xaml, "ExportContentScroll");
        Assert.Equal("Auto", (string?)contentScroll.Attribute("VerticalScrollBarVisibility"));
        Assert.Contains(contentScroll.Descendants(), x => x == composer);
        Assert.DoesNotContain(Named(xaml, "ExportButton").Ancestors(), x => x == contentScroll);
        Assert.Contains("ConstrainToCurrentWorkArea", source);
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
        Assert.Contains("UnifiedAdvancedBox", text);
        var expandedTrigger = xaml.Descendants().Single(x => x.Name.LocalName == "Trigger" &&
            (string?)x.Attribute("Property") == "IsExpanded" && (string?)x.Attribute("Value") == "True");
        Assert.Contains(expandedTrigger.Elements(), x => (string?)x.Attribute("TargetName") == "UnifiedAdvancedBox" &&
            (string?)x.Attribute("Property") == "BorderBrush" && ((string?)x.Attribute("Value"))?.Contains("ShellFocusBrush") == true);
        var contentBorder = advanced.Elements().Single();
        Assert.Equal("0,1,0,0", (string?)contentBorder.Attribute("BorderThickness"));
        Assert.Equal("0", (string?)contentBorder.Attribute("Margin") ?? "0");
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
        var chipStyle = xaml.Descendants().Single(x => x.Name.LocalName == "Style" && (string?)x.Attribute(XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml")) == "NamePartChip");
        Assert.Contains(chipStyle.Elements(), x => (string?)x.Attribute("Property") == "Width" && (string?)x.Attribute("Value") == "112");
        var customText = Named(xaml, "CustomPartText");
        Assert.Equal("88", (string?)customText.Attribute("Width"));
        Assert.Equal("28", (string?)customText.Attribute("Height"));
        Assert.Equal("0", (string?)customText.Attribute("Margin"));
        Assert.Equal("Auto", (string?)customText.Attribute("HorizontalScrollBarVisibility"));
        Assert.Equal("{Binding Part.Text}", (string?)customText.Attribute("ToolTip"));
        var extension = Named(xaml, "ExtensionPreview");
        Assert.DoesNotContain(extension.Ancestors(), x => x == composer);
        Assert.Null(extension.Attribute("AllowDrop"));
        var add = xaml.Descendants().Single(x => (string?)x.Attribute("Click") == "AddPart_Click");
        Assert.Equal("+ Add", (string?)add.Attribute("Content"));
        Assert.Equal((string?)Named(xaml, "AddPartCombo").Attribute("Height"), (string?)add.Attribute("Height"));
    }

    [Fact]
    public void OutputExampleShowsAndWrapsCompletePath()
    {
        var root = FindRepositoryRoot();
        var xaml = XDocument.Load(Path.Combine(root, "LightflowStudio", "ExportDialog.xaml"));
        var preview = Named(xaml, "PathPreview");
        Assert.Equal("None", (string?)preview.Attribute("TextTrimming"));
        Assert.Equal("Wrap", (string?)preview.Attribute("TextWrapping"));
        var source = File.ReadAllText(Path.Combine(root, "LightflowStudio", "ExportDialog.xaml.cs"));
        Assert.Contains("PathPreview.Text = _model.PreviewPath", source);
        Assert.Contains("OutputExampleBorder.ToolTip = _model.PreviewPath", source);
        Assert.True(int.Parse((string?)Named(xaml, "OutputExampleBorder").Attribute("MinHeight") ?? "0") >= 50);
        Assert.Equal("{DynamicResource TextBrush}", (string?)preview.Attribute("Foreground"));
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
    public void EveryPrimarySettingHasAccessibleLabelInfoHelp()
    {
        var root = FindRepositoryRoot();
        var xaml = XDocument.Load(Path.Combine(root, "LightflowStudio", "ExportDialog.xaml"));
        foreach (var name in new[] { "FormatInfo", "CodecInfo", "ResolutionInfo", "FrameRateInfo", "RateControlInfo",
                     "QualityInfo", "TargetInfo", "MaxInfo", "CbrInfo", "AudioInfo", "EncoderInfo", "ParallelInfo" })
        {
            var info = Named(xaml, name);
            var tooltip = (string?)info.Attribute("ToolTip");
            Assert.True(tooltip?.Length > 30, $"{name} needs a meaningful tooltip.");
            Assert.False(string.IsNullOrWhiteSpace((string?)info.Attribute("AutomationProperties.Name")));
            Assert.Contains("InfoButton", (string?)info.Attribute("Style"));
        }
        var source = File.ReadAllText(Path.Combine(root, "LightflowStudio", "ExportDialog.xaml.cs"));
        Assert.Contains("InfoButton_GotKeyboardFocus", source);
        Assert.Contains("toolTip.IsOpen = true", source);
    }

    [Fact]
    public void LightflowHelpUsesDarkReusableTooltipAndExplicitInfoChrome()
    {
        var root = FindRepositoryRoot();
        var app = XDocument.Load(Path.Combine(root, "LightflowStudio", "App.xaml"));
        var dialog = XDocument.Load(Path.Combine(root, "LightflowStudio", "ExportDialog.xaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var tooltip = app.Descendants().Single(element => element.Name.LocalName == "Style" && (string?)element.Attribute(x + "Key") == "LightflowToolTipStyle");
        Assert.Contains(tooltip.Elements(), element => (string?)element.Attribute("Property") == "Foreground" && ((string?)element.Attribute("Value"))?.Contains("TextBrush") == true);
        Assert.Contains(tooltip.Elements(), element => (string?)element.Attribute("Property") == "Background" && ((string?)element.Attribute("Value"))?.Contains("ElevatedBrush") == true);
        Assert.Contains(tooltip.Descendants(), element => element.Name.LocalName == "TextBlock" && (string?)element.Attribute("TextWrapping") == "Wrap");
        Assert.Contains(tooltip.Elements(), element => (string?)element.Attribute("Property") == "BorderBrush" &&
            ((string?)element.Attribute("Value"))?.Contains("ToolTipBorderBrush") == true);
        var tooltipBorder = app.Descendants().Single(element => element.Name.LocalName == "SolidColorBrush" && (string?)element.Attribute(x + "Key") == "ToolTipBorderBrush");
        Assert.Contains("FFFFFF", (string?)tooltipBorder.Attribute("Color"));
        var infoStyle = dialog.Descendants().Single(element => element.Name.LocalName == "Style" && (string?)element.Attribute(x + "Key") == "InfoHelpButtonStyle");
        Assert.Contains(infoStyle.Descendants(), element => element.Name.LocalName == "ControlTemplate");
        Assert.Contains(infoStyle.Descendants(), element => element.Name.LocalName == "Trigger" && (string?)element.Attribute("Property") == "IsKeyboardFocused");
        Assert.DoesNotContain(infoStyle.Descendants(), element => (string?)element.Attribute("Background") is "White" or "LightBlue");
    }

    [Fact]
    public void AdvancedPresetAndAqUseBoundedTypedControls()
    {
        var root = FindRepositoryRoot();
        var xaml = XDocument.Load(Path.Combine(root, "LightflowStudio", "ExportDialog.xaml"));
        var source = File.ReadAllText(Path.Combine(root, "LightflowStudio", "ExportDialog.xaml.cs"));
        Assert.Equal("ComboBox", Named(xaml, "PresetCombo").Name.LocalName);
        Assert.DoesNotContain(xaml.Descendants(), element => element.Attributes().Any(attribute => attribute.Name.LocalName == "Name" && attribute.Value == "PresetText"));
        Assert.Contains("Select(PresetCombo, ExportPresentation.EncoderPresets, model.Encoding.EncoderPreset)", source);
        Assert.Contains("preset.Value", source);
        var slider = Named(xaml, "AqStrengthSlider");
        Assert.Equal("Slider", slider.Name.LocalName);
        Assert.Contains("AqSliderStyle", (string?)slider.Attribute("Style"));
        var style = xaml.Descendants().Single(element => element.Name.LocalName == "Style" &&
            element.Attributes().Any(attribute => attribute.Name.LocalName == "Key" && attribute.Value == "AqSliderStyle"));
        Assert.Contains(style.Elements(), element => (string?)element.Attribute("Property") == "Minimum" && (string?)element.Attribute("Value") == "1");
        Assert.Contains(style.Elements(), element => (string?)element.Attribute("Property") == "Maximum" && (string?)element.Attribute("Value") == "15");
        Assert.Contains(style.Elements(), element => (string?)element.Attribute("Property") == "IsSnapToTickEnabled" && (string?)element.Attribute("Value") == "True");
        Assert.NotNull(Named(xaml, "AqStrengthValue"));
        Assert.DoesNotContain("int.TryParse(AqText", source);
        Assert.Contains("AqStrengthPanel.IsEnabled = ExportPresentation.IsAqStrengthEnabled", source);
        Assert.Contains("AqStrengthSlider.Value = ExportPresentation.AqStrength(model.Encoding.AqStrength)", source);
    }

    [Fact]
    public void ExportInteractiveControlsUseExplicitLightflowStateTemplates()
    {
        var root = FindRepositoryRoot();
        var app = XDocument.Load(Path.Combine(root, "LightflowStudio", "App.xaml"));
        var dialog = XDocument.Load(Path.Combine(root, "LightflowStudio", "ExportDialog.xaml"));
        var allMarkup = app + dialog.ToString();
        Assert.DoesNotContain("Blue", allMarkup, StringComparison.OrdinalIgnoreCase);
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var chip = dialog.Descendants().Single(element => element.Name.LocalName == "Style" && (string?)element.Attribute(x + "Key") == "ChipButton");
        Assert.Contains("{x:Type Button}", (string?)chip.Attribute("BasedOn"));
        var textBox = app.Descendants().First(element => element.Name.LocalName == "Style" && (string?)element.Attribute("TargetType") == "TextBox");
        Assert.Contains(textBox.Descendants(), element => element.Name.LocalName == "ControlTemplate");
        Assert.Contains(textBox.Descendants(), element => element.Name.LocalName == "Trigger" && (string?)element.Attribute("Property") == "IsKeyboardFocused");
        foreach (var key in new[] { "InfoHelpButtonStyle", "AdvancedToggle", "AqSliderStyle" })
        {
            var style = dialog.Descendants().Single(element => element.Name.LocalName == "Style" && (string?)element.Attribute(x + "Key") == key);
            Assert.Contains(style.Descendants(), element => element.Name.LocalName == "ControlTemplate");
        }
    }

    [Fact]
    public void ComboBoxPopupAlignsBelowAndCannotBeNarrowerThanOwner()
    {
        var app = XDocument.Load(Path.Combine(FindRepositoryRoot(), "LightflowStudio", "App.xaml"));
        var comboStyle = app.Descendants().First(element => element.Name.LocalName == "Style" && (string?)element.Attribute("TargetType") == "ComboBox");
        var popup = comboStyle.Descendants().Single(element => element.Name.LocalName == "Popup" &&
            element.Attributes().Any(attribute => attribute.Name.LocalName == "Name" && attribute.Value == "PART_Popup"));
        Assert.Equal("Bottom", (string?)popup.Attribute("Placement"));
        Assert.Equal("{Binding RelativeSource={RelativeSource TemplatedParent}}", (string?)popup.Attribute("PlacementTarget"));
        var surface = popup.Elements().Single(element => element.Name.LocalName == "Grid");
        Assert.Contains("PlacementTarget.ActualWidth", (string?)surface.Attribute("MinWidth"));
        Assert.Null(surface.Attribute("Width"));
    }

    [Fact]
    public void EveryAdvancedSettingHasAccessibleLabelInfoHelp()
    {
        var xaml = XDocument.Load(Path.Combine(FindRepositoryRoot(), "LightflowStudio", "ExportDialog.xaml"));
        foreach (var name in new[] { "PresetInfo", "TuneInfo", "MultipassInfo", "AqInfo", "PixelFormatInfo",
                     "SpatialAqInfo", "TemporalAqInfo", "DeinterlaceInfo", "FastStartInfo" })
        {
            var info = Named(xaml, name);
            Assert.True(((string?)info.Attribute("ToolTip"))?.Length > 30, $"{name} needs explanatory help.");
            Assert.False(string.IsNullOrWhiteSpace((string?)info.Attribute("AutomationProperties.Name")));
            Assert.Contains("InfoButton", (string?)info.Attribute("Style"));
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
