using System.Text.Json;
using Lightflow.UI;

namespace Lightflow.Desktop.MacOS;

// Optional presentation preferences in the explicit isolated profile, never in the Catalog.
// No filesystem navigation, Catalog state, selection or schema is persisted here.
internal static class BrowserLayoutStore
{
    public static BrowserLayout Load(string root)
    {
        try { return (JsonSerializer.Deserialize<BrowserLayout>(File.ReadAllText(Path.Combine(root,"browser-layout.json"))) ?? new()).Validated(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }
    public static void Save(string root, BrowserLayout layout)
    {
        var temporary=Path.Combine(root,$"browser-layout.{Guid.NewGuid():N}.tmp");
        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(temporary,JsonSerializer.Serialize(layout.Validated()));
            File.Move(temporary,Path.Combine(root,"browser-layout.json"),overwrite:true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { Console.Error.WriteLine($"Browser layout preferences could not be saved: {error.Message}"); }
        finally { try { File.Delete(temporary); } catch (Exception error) when (error is IOException or UnauthorizedAccessException) { } }
    }
}
