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
    public static string PathFor(string outputPath) => outputPath + ".lightflow.json";

    public static bool Matches(string outputPath, EncodingOutputIdentity expected)
    {
        try
        {
            var path = PathFor(outputPath);
            return File.Exists(path)
                && JsonSerializer.Deserialize<EncodingOutputIdentity>(File.ReadAllText(path)) == expected;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return false;
        }
    }

    public static void Save(string outputPath, EncodingOutputIdentity identity)
    {
        var path = PathFor(outputPath);
        var temporary = path + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(identity, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, path, true);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
        }
    }

    public static void Delete(string outputPath)
    {
        try { File.Delete(PathFor(outputPath)); } catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }
}
