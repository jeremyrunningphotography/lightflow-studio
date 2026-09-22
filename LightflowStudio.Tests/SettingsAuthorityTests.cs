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

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
}
