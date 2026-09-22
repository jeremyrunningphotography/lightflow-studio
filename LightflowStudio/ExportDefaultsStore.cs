using System.IO;
using System.Text.Json;

namespace LightflowStudio;

// Export owns its starting encoding recipe. Settings only supplies the one-time legacy
// migration input; it never reads or edits this file after migration.
internal static class ExportDefaultsStore
{
    internal static string PathFor(string settingsPath) =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(settingsPath))!, "export-defaults.json");

    internal static EncodingOptions Load(string settingsPath)
    {
        var path = PathFor(settingsPath);
        try
        {
            return File.Exists(path)
                ? EncodingOptions.Normalize(JsonSerializer.Deserialize<EncodingOptions>(File.ReadAllText(path)))
                : EncodingPresetCatalog.Recommended;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The saved Export defaults could not be read. Restore the profile's export-defaults.json before exporting.", exception);
        }
    }

    internal static void Migrate(string settingsPath, JsonElement legacySettings)
    {
        var path = PathFor(settingsPath);
        if (File.Exists(path) || !legacySettings.TryGetProperty("Encoding", out var encoding)
            || encoding.ValueKind is JsonValueKind.Null) return;
        var value = EncodingOptions.Normalize(encoding.Deserialize<EncodingOptions>());
        var temporary = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, path, overwrite: false);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
