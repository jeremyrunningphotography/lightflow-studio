namespace LightflowStudio;

internal sealed record PremiereSendItem(int Index, string SourceFileName, string PremiereItemText, bool HasRange, bool UseRange,
    bool RangeControlEnabled, string RangeAutomationName, double RangeSegmentLeft, double RangeSegmentWidth,
    string RangeToolTip, string TimelineAutomationName);

/// <summary>Source-only handoff planning mirrors Export's global/per-item review-range choices.</summary>
internal sealed class PremiereSendModel(IReadOnlyList<PremiereSource> sources)
{
    private readonly bool[] _useRanges = sources.Select(source => source.Range is not null && source.RangeIssue is null).ToArray();
    private bool _rangesGloballyEnabled = true;

    public IReadOnlyList<PremiereSource> PlannedSources => PremiereSendPlanning.Sources(sources, _useRanges);
    public IReadOnlyList<PremiereSendItem> Items => sources.Select(BuildItem).ToArray();
    public bool HasRangeIssue => sources.Any(source => source.RangeIssue is not null);
    public bool? GlobalUseRangeState
    {
        get
        {
            var applicable = Items.Where(item => item.HasRange).Select(item => item.UseRange).ToArray();
            if (!_rangesGloballyEnabled) return false;
            if (applicable.Length == 0 || applicable.All(value => value)) return true;
            if (applicable.All(value => !value)) return false;
            return null;
        }
    }

    public void SetGlobalUseRanges(bool use)
    {
        _rangesGloballyEnabled = use;
        for (var index = 0; index < sources.Count; index++)
            if (HasUsableRange(sources[index])) _useRanges[index] = use;
    }

    public void SetUseRange(int index, bool use)
    {
        if (!_rangesGloballyEnabled || index < 0 || index >= sources.Count || !HasUsableRange(sources[index])) return;
        _useRanges[index] = use;
    }

    private PremiereSendItem BuildItem(PremiereSource source, int index)
    {
        var hasRange = HasUsableRange(source);
        var range = hasRange && source.Range!.TryGetMediaRange(out var value) ? value : null;
        var presentation = MediaRangeTimelinePresentation.For(source.Name, range?.SourceDuration ?? TimeSpan.Zero,
            range, hasRange && _useRanges[index], "Premiere source");
        return new(index, source.Name, "→ Same source item in Premiere", hasRange, hasRange && _useRanges[index], hasRange && _rangesGloballyEnabled,
            hasRange ? $"Use In/Out for {source.Name}" : $"Use In/Out for {source.Name}, unavailable because no saved In/Out is defined",
            presentation.SegmentLeft, presentation.SegmentWidth, presentation.ToolTip, presentation.AutomationName);
    }

    private static bool HasUsableRange(PremiereSource source) => source.RangeIssue is null
        && source.Range is not null && source.Range.TryGetMediaRange(out _);
}
