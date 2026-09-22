using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LightflowStudio;

internal sealed record AppSettings
{
    public static string DefaultScreengrabDirectory
    {
        get
        {
            if (LightflowStorageLocations.Current.IsIsolated)
                return Path.Combine(LightflowStorageLocations.Current.ApplicationDataDirectory, "Screengrabs");
            var pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            if (string.IsNullOrWhiteSpace(pictures))
                pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            return Path.Combine(pictures, "Lightflow Studio", "Screengrabs");
        }
    }

    public string DefaultVideoFolder { get; init; } = "";
    public string ScreengrabDirectory { get; init; } = DefaultScreengrabDirectory;
    // LutFolder is retained only as the read-time migration source for pre-#146 settings files.
    // New saves use the two stage-specific preferences below.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LutFolder { get; init; } = LightflowStorageLocations.Current.IsIsolated ? "" : LutCatalog.DefaultFolder;
    public string CameraLutFolder { get; init; } = "";
    public bool CameraLutIncludeSubfolders { get; init; }
    public string CreativeLutFolder { get; init; } = "";
    public bool CreativeLutIncludeSubfolders { get; init; }
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
    public int MaxSimultaneousExports { get; init; } = EncodingJobConcurrency.Default;
    public bool IsExportQueuePaused { get; init; }
    public Guid? CatalogId { get; init; }
    public bool BackupCatalogOnClose { get; init; } = true;
    public string? CatalogBackupDirectory { get; init; }

    public AppSettings() { }
    public AppSettings(string lutFolder) => CameraLutFolder = CreativeLutFolder = LutFolder = lutFolder;

    public static AppSettings Normalize(AppSettings? settings, bool? isolated = null)
    {
        var isIsolated = isolated ?? LightflowStorageLocations.Current.IsIsolated;
        if (settings is null) return new AppSettings();
        return settings with
        {
            DefaultVideoFolder = settings.DefaultVideoFolder?.Trim() ?? "",
            ScreengrabDirectory = string.IsNullOrWhiteSpace(settings.ScreengrabDirectory)
                ? DefaultScreengrabDirectory
                : settings.ScreengrabDirectory.Trim(),
            LutFolder = null,
            CameraLutFolder = NormalizeLutFolder(settings.CameraLutFolder, settings.LutFolder, isIsolated),
            CreativeLutFolder = NormalizeLutFolder(settings.CreativeLutFolder, settings.LutFolder, isIsolated),
            FfmpegPath = settings.FfmpegPath?.Trim() ?? "",
            CatalogDirectory = NormalizeStorageDirectory(settings.CatalogDirectory),
            PreviewsDirectory = NormalizeStorageDirectory(settings.PreviewsDirectory),
            CatalogBackupDirectory = NormalizeStorageDirectory(settings.CatalogBackupDirectory),
            PreviewCacheQuotaGb = Math.Clamp(settings.PreviewCacheQuotaGb, 1, 1024),
            MaxSimultaneousExports = Math.Clamp(settings.MaxSimultaneousExports,
                EncodingJobConcurrency.Minimum, EncodingJobConcurrency.Maximum),
            DefaultResolution = Enum.IsDefined(settings.DefaultResolution) ? settings.DefaultResolution : OutputResolution.FullHd,
            DefaultRecovery = Enum.IsDefined(settings.DefaultRecovery) ? settings.DefaultRecovery : RecoveryStrategy.Normal,
            EncodingPreset = Enum.IsDefined(settings.EncodingPreset) ? settings.EncodingPreset : EncodingPreset.Recommended,
            Encoding = EncodingOptions.Normalize(settings.Encoding)
        };
    }

    private static string? NormalizeStorageDirectory(string? path) =>
        string.IsNullOrWhiteSpace(path) ? null : path.Trim();

    private static string NormalizeLutFolder(string? stageFolder, string? legacyFolder, bool isolated) =>
        !string.IsNullOrWhiteSpace(stageFolder) ? stageFolder.Trim()
        : isolated ? ""
        : !string.IsNullOrWhiteSpace(legacyFolder) ? legacyFolder.Trim()
        : LutCatalog.DefaultFolder;
}

internal static class AppSettingsStore
{
    public static string SettingsPath => LightflowStorageLocations.Current.SettingsPath;

    public static AppSettings Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return AppSettings.Normalize(new AppSettings());
            return AppSettings.Normalize(JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)));
        }
        catch (JsonException)
        {
            return AppSettings.Normalize(new AppSettings());
        }
        catch (IOException)
        {
            return AppSettings.Normalize(new AppSettings());
        }
        catch (UnauthorizedAccessException)
        {
            return AppSettings.Normalize(new AppSettings());
        }
    }

    public static void Save(string path, AppSettings settings, bool? isolated = null)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        var temporaryPath = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(AppSettings.Normalize(settings, isolated), new JsonSerializerOptions { WriteIndented = true }));
            if (File.Exists(path)) File.Replace(temporaryPath, path, null);
            else File.Move(temporaryPath, path);
        }
        finally
        {
            try { File.Delete(temporaryPath); } catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
    }

    public static bool TryLoadForStartup(string path, out AppSettings settings, out string? diagnostic, bool? isolated = null)
    {
        settings = AppSettings.Normalize(new AppSettings(), isolated);
        diagnostic = null;
        try
        {
            if (!File.Exists(path)) return true;
            settings = AppSettings.Normalize(JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)), isolated);
            return true;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            diagnostic = $"Lightflow could not safely read its storage configuration: {exception.Message}";
            return false;
        }
    }
}
