using System.IO;
using System.Text.Json;

namespace LightflowStudio;

internal sealed record AppState
{
    public string LastLutPath { get; init; } = "";
    public bool HasBatchState { get; init; }
    public string LastVideoFolder { get; init; } = "";
    public OutputResolution LastResolution { get; init; } = OutputResolution.FullHd;
    public RecoveryStrategy LastRecovery { get; init; } = RecoveryStrategy.Normal;
    public bool LastIncludeSubfolders { get; init; }
    public bool LastPreserveFolderStructure { get; init; } = true;
    public bool LastOverwriteExistingFiles { get; init; }
    public OutputDestinationMode LastOutputMode { get; init; } = OutputDestinationMode.Subfolder;
    public string LastOutputSubfolder { get; init; } = "";
    public bool LastOutputSubfolderUsesResolutionDefault { get; init; } = true;
    public string LastSpecificOutputFolder { get; init; } = "";
    public string LastFilenameSuffix { get; init; } = "";
    public bool LastFilenameSuffixUsesResolutionDefault { get; init; } = true;

    public AppState() { }
    public AppState(string lastLutPath) => LastLutPath = lastLutPath;
}

internal static class AppStateStore
{
    public static string StatePath => LightflowStorageLocations.Current.StatePath;

    public static AppState Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return new AppState();
            return JsonSerializer.Deserialize<AppState>(File.ReadAllText(path)) ?? new AppState();
        }
        catch (JsonException)
        {
            return new AppState();
        }
        catch (IOException)
        {
            return new AppState();
        }
        catch (UnauthorizedAccessException)
        {
            return new AppState();
        }
    }

    public static void Save(string path, AppState state)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        File.WriteAllText(path, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
    }
}
