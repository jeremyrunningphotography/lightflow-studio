using System.Text.Json;
using Lightflow.UI;

namespace Lightflow.Desktop.MacOS;

// Optional presentation preferences in the explicit isolated profile, never in the Catalog.
// No filesystem navigation, Catalog state, selection or schema is persisted here.
internal static class BrowserLayoutStore
{
    public static BrowserLayout Load(string root)
    {
        try
        {
            var json=File.ReadAllText(Path.Combine(root,"browser-layout.json"));
            using var document=JsonDocument.Parse(json);
            // Migrate the initial draft's ratios against its 1280-point reference window.
            if(document.RootElement.TryGetProperty("Navigation",out var navigation))
            {
                var left=navigation.GetDouble(); var right=document.RootElement.GetProperty("Right").GetDouble();
                var center=document.RootElement.GetProperty("Assets").GetDouble();
                if(!double.IsFinite(left+right+center) || left<=0 || right<=0 || center<=0 || Math.Abs(left+right+center-1)>.01) return new();
                return new BrowserLayout(left*1236,right*1236,document.RootElement.GetProperty("RightOpen").GetBoolean()).Validated();
            }
            return (JsonSerializer.Deserialize<BrowserLayout>(json) ?? new()).Validated();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or KeyNotFoundException or FormatException) { return new(); }
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
