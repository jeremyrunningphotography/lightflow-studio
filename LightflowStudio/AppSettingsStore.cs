using System.IO;
using System.Text.Json;

namespace LightflowStudio;

internal sealed record AppSettings
{
    public string DefaultVideoFolder { get; init; } = "";
    public string LutFolder { get; init; } = LutCatalog.DefaultFolder;
    public string FfmpegPath { get; init; } = "";
    public OutputResolution DefaultResolution { get; init; } = OutputResolution.FullHd;
    public RecoveryStrategy DefaultRecovery { get; init; } = RecoveryStrategy.Normal;
    public bool IncludeSubfolders { get; init; }
    public bool PreserveFolderStructure { get; init; } = true;
    public bool OverwriteExistingFiles { get; init; }
    public bool DetailedActivityLogging { get; init; }
    public EncodingPreset EncodingPreset { get; init; } = EncodingPreset.Recommended;
    public EncodingOptions Encoding { get; init; } = EncodingPresetCatalog.Recommended;
    public string? CatalogDirectory { get; init; }
    public string? PreviewsDirectory { get; init; }
    public int PreviewCacheQuotaGb { get; init; } = 20;
    public Guid? CatalogId { get; init; }

    public AppSettings() { }
    public AppSettings(string lutFolder) => LutFolder = lutFolder;

    public static AppSettings Normalize(AppSettings? settings)
    {
        if (settings is null) return new AppSettings();
        return settings with
        {
            DefaultVideoFolder = settings.DefaultVideoFolder?.Trim() ?? "",
            LutFolder = string.IsNullOrWhiteSpace(settings.LutFolder) ? LutCatalog.DefaultFolder : settings.LutFolder.Trim(),
            FfmpegPath = settings.FfmpegPath?.Trim() ?? "",
            CatalogDirectory = NormalizeStorageDirectory(settings.CatalogDirectory),
            PreviewsDirectory = NormalizeStorageDirectory(settings.PreviewsDirectory),
            PreviewCacheQuotaGb = Math.Clamp(settings.PreviewCacheQuotaGb, 1, 1024),
            DefaultResolution = Enum.IsDefined(settings.DefaultResolution) ? settings.DefaultResolution : OutputResolution.FullHd,
            DefaultRecovery = Enum.IsDefined(settings.DefaultRecovery) ? settings.DefaultRecovery : RecoveryStrategy.Normal,
            EncodingPreset = Enum.IsDefined(settings.EncodingPreset) ? settings.EncodingPreset : EncodingPreset.Recommended,
            Encoding = EncodingOptions.Normalize(settings.Encoding)
        };
    }

    private static string? NormalizeStorageDirectory(string? path) =>
        string.IsNullOrWhiteSpace(path) ? null : path.Trim();
}

internal static class AppSettingsStore
{
    public static string SettingsPath => LightflowStorageLocations.Current.SettingsPath;

    public static AppSettings Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return new AppSettings();
            return AppSettings.Normalize(JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)));
        }
        catch (JsonException)
        {
            return new AppSettings();
        }
        catch (IOException)
        {
            return new AppSettings();
        }
        catch (UnauthorizedAccessException)
        {
            return new AppSettings();
        }
    }

    public static void Save(string path, AppSettings settings)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        var temporaryPath = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(AppSettings.Normalize(settings), new JsonSerializerOptions { WriteIndented = true }));
            if (File.Exists(path)) File.Replace(temporaryPath, path, null);
            else File.Move(temporaryPath, path);
        }
        finally
        {
            try { File.Delete(temporaryPath); } catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
    }

    public static bool TryLoadForStartup(string path, out AppSettings settings, out string? diagnostic)
    {
        settings = new AppSettings();
        diagnostic = null;
        try
        {
            if (!File.Exists(path)) return true;
            settings = AppSettings.Normalize(JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)));
            return true;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            diagnostic = $"Lightflow could not safely read its storage configuration: {exception.Message}";
            return false;
        }
    }
}
