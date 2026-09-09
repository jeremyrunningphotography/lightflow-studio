using System.Diagnostics;

namespace LightflowStudio;

/// <summary>Opt-in, path-free stage timings; no allocation or clock reads without a listener.</summary>
internal static class BrowserPerformance
{
    public static readonly ActivitySource Source = new("LightflowStudio.Browser");
    public static Activity? Measure(string stage) => Source.StartActivity(stage);
}
