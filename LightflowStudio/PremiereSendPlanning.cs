namespace LightflowStudio;

/// <summary>Planning remains source-only today so #258 can add a separate Subclip plan without changing the Send surface.</summary>
internal static class PremiereSendPlanning
{
    public static bool IsVideo(string path) => System.IO.Path.GetExtension(path).ToLowerInvariant() is
        ".mov" or ".mp4" or ".mxf" or ".avi" or ".mts" or ".m2ts";

    public static IReadOnlyList<PremiereSource> Sources(IReadOnlyList<PremiereSource> selected, bool applyRanges) =>
        applyRanges ? selected : selected.Select(source => source.WithoutRange()).ToArray();

    public static bool CanApplyRanges(IReadOnlyList<PremiereSource> selected) => selected.Any(source => source.HasRange);
    public static bool HasRangeIssue(IReadOnlyList<PremiereSource> selected) => selected.Any(source => source.HasRangeIssue);
    public static bool HasTimingAdjustment(IReadOnlyList<PremiereSource> selected) => selected.Any(source => source.Range?.TimingAdjusted == true);
}
