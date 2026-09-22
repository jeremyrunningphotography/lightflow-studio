using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using LightflowStudio;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class SettingsAuthorityTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Lightflow-Settings-" + Guid.NewGuid().ToString("N"));
    private string SettingsPath => Path.Combine(_root, "settings.json");

    [Fact]
    public void OldProfileMigratesEffectiveEncodingBeforeNewSaveAndNeverReimportsIt()
    {
        Directory.CreateDirectory(_root);
        var encoding = EncodingPresetCatalog.Get(EncodingPreset.MaximumQuality) with { Quality = 17 };
        var oldJson = JsonSerializer.Serialize(new
        {
            DefaultVideoFolder = "old-input", DefaultResolution = 0, DefaultRecovery = 2,
            IncludeSubfolders = true, PreserveFolderStructure = true, OverwriteExistingFiles = true,
            DetailedActivityLogging = true, EncodingPreset = 1, Encoding = encoding,
            ScreengrabDirectory = "captures", PreviewCacheQuotaGb = 42, MaxSimultaneousExports = 7,
            IsExportQueuePaused = true, CatalogId = Guid.NewGuid()
        });
        File.WriteAllText(SettingsPath, oldJson);
        Assert.True(AppSettingsStore.TryLoadForStartup(SettingsPath, out var settings, out var diagnostic, isolated: true), diagnostic);
        Assert.Equal(encoding, ExportDefaultsStore.Load(SettingsPath));
        Assert.Equal(oldJson, File.ReadAllText(SettingsPath)); // migration never rewrites the source file
        AppSettingsStore.Save(SettingsPath, settings, isolated: true);
        using var saved = JsonDocument.Parse(File.ReadAllText(SettingsPath));
        foreach (var removed in new[] { "DefaultVideoFolder", "DefaultResolution", "DefaultRecovery",
                     "IncludeSubfolders", "PreserveFolderStructure", "OverwriteExistingFiles",
                     "DetailedActivityLogging", "EncodingPreset", "Encoding" })
            Assert.False(saved.RootElement.TryGetProperty(removed, out _));
        Assert.True(AppSettingsStore.TryLoadForStartup(SettingsPath, out var restarted, out diagnostic, isolated: true), diagnostic);
        Assert.Equal(settings, restarted);
        Assert.Equal(encoding, ExportDefaultsStore.Load(SettingsPath));
        File.WriteAllText(SettingsPath, oldJson.Replace("\"Quality\":17", "\"Quality\":25"));
        Assert.True(AppSettingsStore.TryLoadForStartup(SettingsPath, out _, out diagnostic, isolated: true), diagnostic);
        Assert.Equal(17, ExportDefaultsStore.Load(SettingsPath).Quality);
    }

    [Fact]
    public void FailedMigrationBlocksStartupWithoutLosingOriginalSettings()
    {
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(ExportDefaultsStore.PathFor(SettingsPath)); // unavailable file destination
        const string json = "{\"Encoding\":{\"Quality\":17},\"CatalogDirectory\":\"precious\"}";
        File.WriteAllText(SettingsPath, json);
        Assert.False(AppSettingsStore.TryLoadForStartup(SettingsPath, out _, out var diagnostic, isolated: true));
        Assert.NotNull(diagnostic);
        Assert.Equal(json, File.ReadAllText(SettingsPath));
        Assert.Empty(Directory.GetFiles(_root, "*.tmp"));
    }

    [Fact]
    public void SavingOrRestoringPreferencesPreservesAdministrationAndQueueState()
    {
        var current = AppSettings.Normalize(new AppSettings
        {
            CatalogId = Guid.NewGuid(), CatalogDirectory = "catalog", PreviewsDirectory = "previews",
            MaxSimultaneousExports = 7, IsExportQueuePaused = true,
            ScreengrabDirectory = "custom-captures", PreviewCacheQuotaGb = 90
        });
        var defaults = AppSettings.Normalize(new AppSettings());
        var reset = AppSettings.ApplyPreferences(current, defaults);
        Assert.Equal(current.CatalogId, reset.CatalogId);
        Assert.Equal(current.CatalogDirectory, reset.CatalogDirectory);
        Assert.Equal(current.PreviewsDirectory, reset.PreviewsDirectory);
        Assert.Equal(current.MaxSimultaneousExports, reset.MaxSimultaneousExports);
        Assert.True(reset.IsExportQueuePaused);
        Assert.Equal(defaults.ScreengrabDirectory, reset.ScreengrabDirectory);
        Assert.Equal(defaults.PreviewCacheQuotaGb, reset.PreviewCacheQuotaGb);
        Assert.False(Directory.Exists(_root)); // pure preference operation, no Catalog/filesystem side effects
    }

    [Fact]
    public void FreshProfilesDoNotCreateExportDefaultsOrInheritAnotherProfile()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(SettingsPath, "{}");
        Assert.True(AppSettingsStore.TryLoadForStartup(SettingsPath, out var settings, out _, isolated: true));
        Assert.Empty(settings.CameraLutFolder);
        Assert.Empty(settings.CreativeLutFolder);
        Assert.False(File.Exists(ExportDefaultsStore.PathFor(SettingsPath)));
        Assert.Equal(EncodingPresetCatalog.Recommended, ExportDefaultsStore.Load(SettingsPath));
    }

    [Fact]
    public async Task ReconnectTargetsUnmappedLocationsWithoutAPath()
    {
        await StaDispatcher.RunAsync(() =>
        {
                var offline = new BrowserTreeNode("Disconnected", null,
                    new BrowserStorageEntry("offline", "Disconnected", null, BrowserStorageKind.ManagedRoot,
                        MediaRootAvailability.Unmapped, Guid.NewGuid()));
                var item = new TreeViewItem { DataContext = offline };
                Assert.Same(offline, MainWindow.LocationNodeFromElement(item));
            return Task.CompletedTask;
        });
    }

    [Theory]
    [InlineData(840, 1)]
    [InlineData(896, 2)]
    [InlineData(1600, 2)]
    public void CardsReflowWithoutRecreatingControls(double width, int expectedColumns)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var panel = new SettingsCardsPanel();
                var first = new Border { Height = 120 };
                var second = new Border { Height = 200 };
                var third = new Border { Height = 100 };
                panel.Children.Add(first); panel.Children.Add(second); panel.Children.Add(third);
                foreach (var scale in new[] { 1.0, 1.25, 1.5, 2.0 })
                {
                    System.Windows.Media.VisualTreeHelper.SetRootDpi(panel, new DpiScale(scale, scale));
                    var dips = width;
                    panel.Measure(new Size(dips, double.PositiveInfinity));
                    panel.Arrange(new Rect(0, 0, dips, panel.DesiredSize.Height));
                    var secondPosition = second.TranslatePoint(new Point(), panel);
                    Assert.Equal(expectedColumns, SettingsCardsPanel.ColumnsFor(dips));
                    Assert.Equal(expectedColumns == 2, secondPosition.X > 0);
                    Assert.Equal(expectedColumns == 1, secondPosition.Y > 0);
                    Assert.True(secondPosition.X + second.ActualWidth <= dips);
                    Assert.True(third.TranslatePoint(new Point(), panel).Y > 0);
                }
                panel.Measure(new Size(500, double.PositiveInfinity));
                panel.Arrange(new Rect(0, 0, 500, panel.DesiredSize.Height));
                Assert.Same(second, panel.Children[1]);
                Assert.Equal(0, second.TranslatePoint(new Point(), panel).X);
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start(); thread.Join();
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
}
