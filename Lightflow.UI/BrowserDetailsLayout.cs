using Lightflow.Domain;
using LightflowStudio;

namespace Lightflow.UI;

public enum BrowserLayoutMode { Grid, Details }
public sealed record BrowserColumnPreference(string Id, double Width, bool Visible);
public sealed record BrowserDetailsColumn(string Id, string Title, double Width, bool Visible = true);

/// <summary>Shipping Windows BrowserDetails inventory. Presentation only, never a query owner.</summary>
public static class BrowserDetailsLayout
{
    public const double RowHeight = 64;
    public static IReadOnlyList<BrowserDetailsColumn> Columns { get; } = [
        new("preview","Preview",112), new("name","Name",240), new("rating","Rating",88), new("flag","Flag",86),
        new("capture-date","Capture Date",158), new("media-type","Media Type",95), new("dimensions","Dimensions",112),
        new("duration","Duration",88), new("frame-rate","Frame Rate",95), new("file-size","File Size",95),
        new("modified-date","Modified Date",158,false), new("color-label","Color Label",100,false),
        new("camera","Camera",180,false), new("lens","Lens",180,false), new("color","Color / LUT",180,false),
        new("range","Saved Range",100,false), new("subclips","Subclips",80,false), new("keywords","Keywords",220,false) ];
    public static IReadOnlyList<BrowserColumnPreference> Normalize(IReadOnlyList<BrowserColumnPreference>? saved)
    {
        var result = new List<BrowserColumnPreference>(); var seen = new HashSet<string>();
        foreach (var value in saved ?? [])
            if (value is not null && Columns.FirstOrDefault(c => c.Id == value.Id) is { } definition && seen.Add(value.Id))
                result.Add(value with { Width = double.IsFinite(value.Width) && value.Width > 0 ? Math.Clamp(value.Width,40,1200) : definition.Width });
        result.AddRange(Columns.Where(c => !seen.Contains(c.Id)).Select(c => new BrowserColumnPreference(c.Id,c.Width,c.Visible)));
        if (!result.Any(c => c.Visible)) { var i=result.FindIndex(c=>c.Id=="name"); result[i]=result[i] with { Visible=true }; }
        return result;
    }
    public static string Text(MediaAsset asset, BrowserThumbnail? preview, string id)
    {
        var metadata=preview?.Metadata; var classification=preview?.Classification;
        var width=metadata?.Image?.Width ?? metadata?.Video?.Width; var height=metadata?.Image?.Height ?? metadata?.Video?.Height;
        return id switch
        {
            "name" => asset.RelativePath.Split('/')[^1],
            "rating" => classification?.Rating is > 0 ? new string('★',classification.Rating) : "",
            "flag" => classification is { Flag: not AssetFlag.Unflagged } ? classification.Flag.ToString() : "",
            "capture-date" => DateTime.TryParse(metadata?.Image?.CapturedAt, out var date) ? date.ToString("g") : "",
            "media-type" => asset.MediaType switch { "image"=>"Image", "raw-image"=>"RAW", "video"=>"Video", _=>asset.MediaType },
            "dimensions" => width is > 0 && height is > 0 ? $"{width} × {height}" : "",
            "duration" => metadata?.DurationSeconds is > 0 and < 86400000 ? TimeSpan.FromSeconds(metadata.DurationSeconds.Value).ToString(@"hh\:mm\:ss") : "",
            "frame-rate" => metadata?.Video?.FrameRate is > 0 and < 100000 ? $"{metadata.Video.FrameRate:0.###} fps" : "",
            "file-size" => asset.FileSizeBytes>=1048576 ? $"{asset.FileSizeBytes/1048576d:0.#} MB" : $"{asset.FileSizeBytes/1024d:0.#} KB",
            "modified-date" => asset.LastWriteUtcTicks>0 && asset.LastWriteUtcTicks<=DateTime.MaxValue.Ticks ? new DateTime(asset.LastWriteUtcTicks,DateTimeKind.Utc).ToLocalTime().ToString("g") : "",
            "color-label" => classification?.ColorLabel?.ToString() ?? "",
            "camera" => string.Join(' ',new[]{metadata?.Image?.CameraMake,metadata?.Image?.CameraModel}.Where(x=>!string.IsNullOrWhiteSpace(x))),
            "lens" => metadata?.Image?.LensModel ?? "", "keywords" => string.Join(", ",classification?.Keywords ?? []),
            _ => "" // Full Color/range/subclip projections are #485, never inferred.
        };
    }
}
