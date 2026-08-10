using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.IO;

namespace LightflowStudio;

internal sealed record EncodingOutputIdentity(
    string SourcePath,
    long SourceSizeBytes,
    long SourceLastWriteUtcTicks,
    long? InTicks,
    long? OutTicks,
    string OptionsHash)
{
    public static EncodingOutputIdentity Create(JobItemDefinition item, EncodingJobOptions options)
    {
        var optionText = JsonSerializer.Serialize(new
        {
            options.Resolution, options.Recovery, options.Encoding, options.LutPath,
            options.FilenameSuffix, options.PreserveFolderStructure
        });
        return new(Path.GetFullPath(item.SourceIdentity), item.SourceSizeBytes ?? 0,
            item.SourceLastWriteUtcTicks ?? 0, item.ResolvedRange?.RequestedRange.In?.Ticks,
            item.ResolvedRange?.RequestedRange.Out?.Ticks,
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(optionText))));
    }
}

internal static class EncodingOutputIdentityStore
{
    private const int SchemaVersion = 1;
    public static string CacheDirectory => LightflowStorageLocations.Current.OutputIdentityDirectory;

    public static string PathFor(string outputPath, string? cacheDirectory = null)
    {
        var normalized = Path.GetFullPath(outputPath).Trim().ToUpperInvariant();
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
        return Path.Combine(cacheDirectory ?? CacheDirectory, key + ".json");
    }

    internal static string LegacyPathFor(string outputPath) => outputPath + ".lightflow.json";

    public static bool Matches(string outputPath, EncodingOutputIdentity expected, string? cacheDirectory = null)
    {
        try
        {
            var path = PathFor(outputPath, cacheDirectory);
            if (File.Exists(path))
            {
                var cached = JsonSerializer.Deserialize<CachedOutputIdentity>(File.ReadAllText(path));
                return cached is { Version: SchemaVersion }
                    && string.Equals(cached.OutputPath, Path.GetFullPath(outputPath), StringComparison.OrdinalIgnoreCase)
                    && cached.Identity == expected;
            }

            var legacyPath = LegacyPathFor(outputPath);
            if (!File.Exists(legacyPath)) return false;
            var legacy = JsonSerializer.Deserialize<EncodingOutputIdentity>(File.ReadAllText(legacyPath));
            if (legacy != expected) return false;
            Save(outputPath, expected, cacheDirectory);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return false;
        }
    }

    public static void Save(string outputPath, EncodingOutputIdentity identity, string? cacheDirectory = null)
    {
        var path = PathFor(outputPath, cacheDirectory);
        var temporary = path + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var record = new CachedOutputIdentity(SchemaVersion, Path.GetFullPath(outputPath), identity);
            File.WriteAllText(temporary, JsonSerializer.Serialize(record, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, path, true);
            DeleteFileBestEffort(LegacyPathFor(outputPath));
        }
        finally
        {
            DeleteFileBestEffort(temporary);
        }
    }

    public static void Delete(string outputPath, string? cacheDirectory = null)
    {
        DeleteFileBestEffort(PathFor(outputPath, cacheDirectory));
        DeleteFileBestEffort(LegacyPathFor(outputPath));
    }

    private static void DeleteFileBestEffort(string path)
    {
        try { File.Delete(path); } catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }

    private sealed record CachedOutputIdentity(int Version, string OutputPath, EncodingOutputIdentity Identity);
}
