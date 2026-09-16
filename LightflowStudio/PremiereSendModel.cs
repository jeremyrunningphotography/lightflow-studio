namespace LightflowStudio;

internal sealed record PremiereSendItem(int Index, string SourceFileName, bool HasRange, bool UseRange,
    bool RangeControlEnabled, string RangeAutomationName, double RangeSegmentLeft, double RangeSegmentWidth,
    string RangeToolTip, string TimelineAutomationName, string DetailText = "", bool ShowRangeControl = true,
    bool IsWholeSourceFallback = false);

/// <summary>Source-only handoff planning mirrors Export's global/per-item review-range choices.</summary>
internal sealed class PremiereSendModel
{
    private readonly IReadOnlyList<PremiereSource> sources;
    private readonly IReadOnlyList<PremierePlannedSubclip> subclips;
    private readonly bool[] _useRanges;
    private bool _rangesGloballyEnabled = true;
    private PremiereSendMode _mode;

    public PremiereSendModel(IReadOnlyList<PremiereSource> sources)
    { this.sources = sources; subclips = []; _useRanges = sources.Select(source => source.Range is not null && source.RangeIssue is null).ToArray(); }
    public PremiereSendModel(IReadOnlyList<PremiereSource> sources, IReadOnlyList<PremierePlannedSubclip> subclips)
    {
        this.sources = sources; this.subclips = subclips;
        _useRanges = sources.Select(source => source.Range is not null && source.RangeIssue is null).ToArray();
    }

    public IReadOnlyList<PremiereSource> PlannedSources => PremiereSendPlanning.Sources(sources, _useRanges);
    public IReadOnlyList<PremierePlannedSubclip> PlannedSubclips => subclips;
    public PremiereSendMode Mode => _mode;
    public IReadOnlyList<PremiereSendItem> Items => Mode == PremiereSendMode.Sources
        ? sources.Select(BuildItem).ToArray() : subclips.Select(BuildSubclipItem).ToArray();
    public bool HasRangeIssue => sources.Any(source => source.RangeIssue is not null);
    public void SelectMode(PremiereSendMode mode)
    {
        if (mode == PremiereSendMode.Subclips && subclips.Count == 0)
            throw new InvalidOperationException("Subclip planning is unavailable for this selection.");
        _mode = mode;
    }
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
        // A source without saved points is still visibly a full-source handoff. Its timeline
        // intentionally fills the same shared range track rather than disappearing.
        var fullSource = !hasRange;
        return new(index, source.Name, hasRange, hasRange && _useRanges[index], hasRange && _rangesGloballyEnabled,
            hasRange ? $"Use In/Out for {source.Name}" : $"Use In/Out for {source.Name}, unavailable because no saved In/Out is defined",
            fullSource ? 0 : presentation.SegmentLeft, fullSource ? MediaRangeTimelinePresentation.Width : presentation.SegmentWidth,
            fullSource ? "Full source" : presentation.ToolTip,
            fullSource ? $"Full Premiere source handoff for {source.Name}" : presentation.AutomationName);
    }

    private static PremiereSendItem BuildSubclipItem(PremierePlannedSubclip item, int index)
    {
        MediaRange? range = null;
        item.Projection.Range?.TryGetMediaRange(out range);
        var presentation = MediaRangeTimelinePresentation.For(item.Projection.Name,
            range?.SourceDuration ?? TimeSpan.Zero, range, range is not null, "Premiere Subclip");
        return new(index, item.Projection.IsSourceFallback ? item.Source.Name : item.Projection.Name,
            range is not null, true, false, "",
            range is null ? 0 : presentation.SegmentLeft,
            range is null ? MediaRangeTimelinePresentation.Width : presentation.SegmentWidth,
            range is null ? "Full source" : presentation.ToolTip,
            range is null ? $"Full-source Premiere Subclip {item.Projection.Name}" : presentation.AutomationName,
            item.Projection.IsSourceFallback ? "Whole source · no saved Subclips" : item.Source.Name,
            ShowRangeControl: false, IsWholeSourceFallback: item.Projection.IsSourceFallback);
    }

    private static bool HasUsableRange(PremiereSource source) => source.RangeIssue is null
        && source.Range is not null && source.Range.TryGetMediaRange(out _);
}
