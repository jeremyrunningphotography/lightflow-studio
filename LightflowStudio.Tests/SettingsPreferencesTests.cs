using LightflowStudio;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class SettingsPreferencesTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"LightflowStudio-{Guid.NewGuid():N}");
    private string SettingsPath => Path.Combine(_folder, "settings.json");

    [Fact]
    public void SaveAndLoad_RoundTripsAllUserPreferences()
    {
        var expected = new AppSettings
        {
            ScreengrabDirectory = @"D:\Screengrabs",
            LutFolder = null,
            CameraLutFolder = @"D:\Camera LUTs",
            CameraLutIncludeSubfolders = true,
            CreativeLutFolder = @"D:\Creative LUTs",
            CreativeLutIncludeSubfolders = true,
            FfmpegPath = @"D:\Tools\ffmpeg.exe",
            PreviewCacheQuotaGb = 64
        };

        AppSettingsStore.Save(SettingsPath, expected);

        Assert.Equal(expected, AppSettingsStore.Load(SettingsPath));
    }

    [Fact]
    public void Load_MigratesLegacyLutOnlySettingsWithNewDefaults()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(SettingsPath, "{\"LutFolder\":\"D:\\\\Legacy LUTs\"}");

        var settings = AppSettingsStore.Load(SettingsPath);

        Assert.Null(settings.LutFolder);
        Assert.Equal(@"D:\Legacy LUTs", settings.CameraLutFolder);
        Assert.Equal(@"D:\Legacy LUTs", settings.CreativeLutFolder);
        Assert.False(settings.CameraLutIncludeSubfolders);
        Assert.False(settings.CreativeLutIncludeSubfolders);
        Assert.Equal(AppSettings.DefaultScreengrabDirectory, settings.ScreengrabDirectory);
        Assert.Equal("", settings.FfmpegPath);
        Assert.Equal(20, settings.PreviewCacheQuotaGb);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(2048, 1024)]
    public void NormalizeBoundsPreviewCacheQuota(int configured, int expected)
    {
        Assert.Equal(expected, AppSettings.Normalize(new AppSettings
        {
            PreviewCacheQuotaGb = configured
        }).PreviewCacheQuotaGb);
    }

    [Fact]
    public void Normalize_TrimsRetainedPaths()
    {
        var settings = AppSettings.Normalize(new AppSettings
        {
            ScreengrabDirectory = "  D:\\Screengrabs  ",
            LutFolder = null,
            CameraLutFolder = "  D:\\Camera LUTs  ",
            CreativeLutFolder = "  D:\\Creative LUTs  ",
            FfmpegPath = "  D:\\ffmpeg.exe  ",
        });

        Assert.Equal(@"D:\Screengrabs", settings.ScreengrabDirectory);
        Assert.Equal(@"D:\Camera LUTs", settings.CameraLutFolder);
        Assert.Equal(@"D:\Creative LUTs", settings.CreativeLutFolder);
        Assert.Equal(@"D:\ffmpeg.exe", settings.FfmpegPath);
    }

    [Fact]
    public void LutFolderPickerStartsAtConfiguredFolderOrNearestExistingParent()
    {
        var camera = Directory.CreateDirectory(Path.Combine(_folder, "Camera")).FullName;
        var creative = Directory.CreateDirectory(Path.Combine(_folder, "Creative")).FullName;

        Assert.Equal(camera, MainWindow.ResolveFolderPickerInitialDirectory(camera));
        Assert.Equal(creative, MainWindow.ResolveFolderPickerInitialDirectory(creative));
        Assert.Equal(camera, MainWindow.ResolveFolderPickerInitialDirectory(Path.Combine(camera, "Unavailable", "Nested")));
        Assert.Null(MainWindow.ResolveFolderPickerInitialDirectory("\0invalid"));
    }

    [Fact]
    public void ConfiguredExecutableTakesPrecedenceOverBundledCopy()
    {
        Directory.CreateDirectory(_folder);
        var configured = Path.Combine(_folder, "configured.exe");
        var bundled = Path.Combine(_folder, "bundled.exe");
        File.WriteAllText(configured, "configured");
        File.WriteAllText(bundled, "bundled");

        Assert.Equal(configured, ExecutableLocator.Find("ffmpeg.exe", bundled, _folder, configured));
    }

    [Fact]
    public void MissingConfiguredExecutableFallsBackToBundledCopy()
    {
        Directory.CreateDirectory(_folder);
        var bundled = Path.Combine(_folder, "bundled.exe");
        File.WriteAllText(bundled, "bundled");

        Assert.Equal(bundled, ExecutableLocator.Find("ffmpeg.exe", bundled, _folder, Path.Combine(_folder, "missing.exe")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }
}
