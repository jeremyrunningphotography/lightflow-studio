using System.Xml.Linq;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class SettingsExperienceTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void SettingsUsesKeyboardAccessibleCategoryNavigationWithOneContextualPage()
    {
        var document = LoadWindow();
        var categories = Named(document, "SettingsCategoryList");
        var items = categories.Elements().ToList();

        Assert.Equal("0", (string?)categories.Attribute("SelectedIndex"));
        Assert.Equal("SettingsCategoryList_SelectionChanged", (string?)categories.Attribute("SelectionChanged"));
        Assert.Equal(["General", "Color", "Export", "Storage", "Tools"],
            items.Select(item => (string?)item.Attribute("Tag")));
        Assert.All(items, item => Assert.False(string.IsNullOrWhiteSpace(
            (string?)item.Attribute("AutomationProperties.Name"))));

        Assert.Null(Named(document, "SettingsGeneralPage").Attribute("Visibility"));
        Assert.All(new[] { "SettingsColorPage", "SettingsExportPage", "SettingsStoragePage", "SettingsToolsPage" },
            name => Assert.Equal("Collapsed", (string?)Named(document, name).Attribute("Visibility")));
    }

    [Fact]
    public void SettingsSeparatesRoutinePreferencesFromMaintenanceAndAdvancedEncoding()
    {
        var document = LoadWindow();

        Assert.Contains(Named(document, "SettingsGeneralPage").Descendants(),
            element => Name(element) == "SettingsDefaultVideoFolder");
        Assert.Contains(Named(document, "SettingsColorPage").Descendants(),
            element => Name(element) == "SettingsCameraLutFolder");
        Assert.Contains(Named(document, "SettingsExportPage").Descendants(),
            element => Name(element) == "SettingsAdvancedExportOptions");
        Assert.Contains(Named(document, "SettingsStoragePage").Descendants(),
            element => Name(element) == "ClearPreviewsButton");
        Assert.Contains(Named(document, "SettingsToolsPage").Descendants(),
            element => Name(element) == "SettingsFfmpegPath");

        var advanced = Named(document, "SettingsAdvancedExportOptions");
        Assert.Equal("Advanced encoder options", (string?)advanced.Attribute("AutomationProperties.Name"));
        Assert.Null(advanced.Attribute("IsExpanded"));
    }

    [Fact]
    public void PathFieldsAndPersistentFooterExposeAccessibleActionsAndHonestStatus()
    {
        var document = LoadWindow();
        foreach (var name in new[] { "SettingsDefaultVideoFolder", "SettingsScreengrabDirectory", "SettingsFfmpegPath" })
        {
            var field = Named(document, name);
            Assert.False(string.IsNullOrWhiteSpace((string?)field.Attribute("AutomationProperties.Name")));
            Assert.Equal("{StaticResource SettingsPathTextBoxStyle}", (string?)field.Attribute("Style"));
        }

        Assert.NotNull(Named(document, "SettingsDefaultVideoFolderStatus"));
        Assert.NotNull(Named(document, "SettingsScreengrabDirectoryStatus"));
        Assert.NotNull(Named(document, "SettingsFfmpegPathStatus"));
        Assert.Equal("Save settings", (string?)Named(document, "SaveSettingsButton")
            .Attribute("AutomationProperties.Name"));
    }

    private static XElement Named(XDocument document, string name) => document.Descendants().Single(element =>
        Name(element) == name);

    private static string? Name(XElement element) => (string?)element.Attribute(Xaml + "Name");

    private static XDocument LoadWindow() => XDocument.Load(Path.Combine(FindRepositoryRoot(),
        "LightflowStudio", "MainWindow.xaml"));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "LightflowStudio")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
