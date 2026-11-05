using System.Diagnostics;
namespace LightflowStudio;

/// <summary>Opt-in, path-free thumbnail diagnostics. No clock reads or activities without a listener.</summary>
public static class ThumbnailPerformance
{
    public static readonly ActivitySource Source = new("LightflowStudio.Thumbnails");
    public static Activity? Measure(string stage, Guid? assetId = null)
    {
        var activity = Source.StartActivity(stage);
        if (assetId is { } id) activity?.SetTag("asset.id", id.ToString("N"));
        return activity;
    }
}
