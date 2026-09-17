namespace LightflowStudio;

internal enum PremiereSendMode { Sources, Subclips }
internal sealed record PremierePlannedSubclip(PremiereSource Source, PremiereSubclipProjection Projection);

internal static class PremiereSendPlanning
{
    public static bool IsVideo(string path) => System.IO.Path.GetExtension(path).ToLowerInvariant() is
        ".mov" or ".mp4" or ".mxf" or ".avi" or ".mts" or ".m2ts";

    public static IReadOnlyList<PremiereSource> Sources(IReadOnlyList<PremiereSource> selected, bool applyRanges) =>
        applyRanges ? selected : selected.Select(source => source.WithoutRange()).ToArray();

    public static IReadOnlyList<PremiereSource> Sources(IReadOnlyList<PremiereSource> selected, IReadOnlyList<bool> useRanges)
    {
        if (selected.Count != useRanges.Count) throw new ArgumentException("Every selected source needs a range choice.", nameof(useRanges));
        return selected.Select((source, index) => useRanges[index] ? source : source.WithoutRange()).ToArray();
    }

    public static bool CanApplyRanges(IReadOnlyList<PremiereSource> selected) => selected.Any(source => source.HasRange);
    public static bool HasRangeIssue(IReadOnlyList<PremiereSource> selected) => selected.Any(source => source.HasRangeIssue);

    public static IReadOnlyList<PremierePlannedSubclip> Subclips(IReadOnlyList<PremiereSource> selected,
        IReadOnlyDictionary<Guid, IReadOnlyList<Subclip>> saved)
    {
        var output = new List<PremierePlannedSubclip>();
        foreach (var source in selected)
        {
            var current = saved.GetValueOrDefault(source.AssetId) ?? [];
            if (current.Count == 0)
            {
                output.Add(new(source, new(null, System.IO.Path.GetFileNameWithoutExtension(source.Path), 1,
                    null, "", IsSourceFallback: true)));
                continue;
            }
            foreach (var subclip in SubclipCurrentOrder.Apply(current))
            {
                var range = new MediaRange(subclip.SourceDuration, subclip.In, subclip.Out);
                if (!PremiereRangeProjection.TryCreate(range, out var projection))
                    throw new InvalidOperationException($"{subclip.Name} has a range that Premiere cannot represent.");
                output.Add(new(source, new(subclip.SubclipId, subclip.Name, subclip.Revision, projection!, "")));
            }
        }
        return output;
    }
}
