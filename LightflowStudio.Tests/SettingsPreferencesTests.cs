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
            DefaultVideoFolder = @"D:\Video Projects",
            ScreengrabDirectory = @"D:\Screengrabs",
            LutFolder = @"D:\LUT Library",
            FfmpegPath = @"D:\Tools\ffmpeg.exe",
            DefaultResolution = OutputResolution.UltraHd,
            DefaultRecovery = RecoveryStrategy.Salvage,
            IncludeSubfolders = true,
            PreserveFolderStructure = false,
            OverwriteExistingFiles = true,
            DetailedActivityLogging = true,
            PreviewCacheQuotaGb = 64,
            EncodingPreset = EncodingPreset.EfficientHevc,
            Encoding = EncodingPresetCatalog.Get(EncodingPreset.EfficientHevc) with
            {
                RateControl = RateControlMode.VariableBitrate,
                TargetBitrateMbps = 35,
                MaxBitrateMbps = 70,
                Container = OutputContainer.Mkv
            }
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

        Assert.Equal(@"D:\Legacy LUTs", settings.LutFolder);
        Assert.Equal("", settings.DefaultVideoFolder);
        Assert.Equal(AppSettings.DefaultScreengrabDirectory, settings.ScreengrabDirectory);
        Assert.Equal("", settings.FfmpegPath);
        Assert.Equal(OutputResolution.FullHd, settings.DefaultResolution);
        Assert.Equal(RecoveryStrategy.Normal, settings.DefaultRecovery);
        Assert.False(settings.IncludeSubfolders);
        Assert.True(settings.PreserveFolderStructure);
        Assert.False(settings.OverwriteExistingFiles);
        Assert.False(settings.DetailedActivityLogging);
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
    public void Normalize_TrimsPathsAndRepairsInvalidEnumValues()
    {
        var settings = AppSettings.Normalize(new AppSettings
        {
            DefaultVideoFolder = "  D:\\Videos  ",
            ScreengrabDirectory = "  D:\\Screengrabs  ",
            LutFolder = "  D:\\LUTs  ",
            FfmpegPath = "  D:\\ffmpeg.exe  ",
            DefaultResolution = (OutputResolution)99,
            DefaultRecovery = (RecoveryStrategy)99
        });

        Assert.Equal(@"D:\Videos", settings.DefaultVideoFolder);
        Assert.Equal(@"D:\Screengrabs", settings.ScreengrabDirectory);
        Assert.Equal(@"D:\LUTs", settings.LutFolder);
        Assert.Equal(@"D:\ffmpeg.exe", settings.FfmpegPath);
        Assert.Equal(OutputResolution.FullHd, settings.DefaultResolution);
        Assert.Equal(RecoveryStrategy.Normal, settings.DefaultRecovery);
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
