using LightflowStudio;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class AppSettingsStoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"LightflowStudio-{Guid.NewGuid():N}");
    private string SettingsPath => Path.Combine(_folder, "settings.json");

    [Fact]
    public void SettingsPath_UsesLightflowStudioBrandFolder()
    {
        Assert.EndsWith(Path.Combine("Jeremy Running Photography", "Lightflow Studio", "settings.json"), AppSettingsStore.SettingsPath);
    }

    [Fact]
    public void SaveAndLoad_RoundTripsConfiguredLutFolder()
    {
        var expected = @"D:\Custom LUTs";

        AppSettingsStore.Save(SettingsPath, new AppSettings(expected));
        var actual = AppSettingsStore.Load(SettingsPath);

        Assert.Equal(expected, actual.CameraLutFolder);
        Assert.Equal(expected, actual.CreativeLutFolder);
    }

    [Fact]
    public void Load_UsesDefaultFolderWhenSettingsDoNotExist()
    {
        var settings = AppSettingsStore.Load(SettingsPath);
        Assert.Equal(LutCatalog.DefaultFolder, settings.CameraLutFolder);
        Assert.Equal(LutCatalog.DefaultFolder, settings.CreativeLutFolder);
        Assert.False(settings.CameraLutIncludeSubfolders);
        Assert.False(settings.CreativeLutIncludeSubfolders);
    }

    [Fact]
    public void Load_UsesDefaultFolderWhenSettingsAreInvalid()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(SettingsPath, "not json");

        Assert.Equal(LutCatalog.DefaultFolder, AppSettingsStore.Load(SettingsPath).CameraLutFolder);
    }

    [Fact]
    public void Save_AtomicallyReplacesExistingSettingsWithoutLeavingTemporaryFiles()
    {
        AppSettingsStore.Save(SettingsPath, new AppSettings(@"D:\First"));

        AppSettingsStore.Save(SettingsPath, new AppSettings(@"D:\Second"));

        Assert.Equal(@"D:\Second", AppSettingsStore.Load(SettingsPath).CameraLutFolder);
        Assert.Empty(Directory.EnumerateFiles(_folder, "*.tmp"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }
}
