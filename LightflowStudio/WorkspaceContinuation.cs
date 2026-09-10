namespace LightflowStudio;

/// <summary>Session intent only. Media identity and query semantics remain owned by their existing domains.</summary>
internal sealed record WorkspaceContinuationState
{
    public BrowserQuery Query { get; init; } = BrowserQuery.Default;
    public bool QueryLocked { get; init; }
    public IReadOnlyList<WorkspaceBrowserLocationState> ExpandedFolders { get; init; } = [];
    public double TreeVerticalOffset { get; init; }
    public double TreeHorizontalOffset { get; init; }
    public WorkspaceGridState Grid { get; init; } = new();
    public WorkspacePlayerState? Player { get; init; }

    public static WorkspaceContinuationState? Normalize(WorkspaceContinuationState? state)
    {
        if (state is null) return null;
        var query = state.Query ?? BrowserQuery.Default;
        return state with
        {
            Query = query with
            {
                SearchText = query.SearchText ?? "",
                SortMode = Enum.IsDefined(query.SortMode) ? query.SortMode : BrowserSortMode.Name,
                Filters = (query.Filters ?? []).Where(f => f is not null && Enum.IsDefined(f.Field) &&
                    Enum.IsDefined(f.Comparison) && (f.NumberValue is null || double.IsFinite(f.NumberValue.Value)) &&
                    (f.NumberValue2 is null || double.IsFinite(f.NumberValue2.Value))).ToArray()
            },
            ExpandedFolders = (state.ExpandedFolders ?? []).Select(folder =>
                WorkspaceState.Normalize(new() { Browser = folder }).Browser).OfType<WorkspaceBrowserLocationState>()
                .DistinctBy(folder => (folder.RootId, folder.RelativeFolder.ToUpperInvariant())).ToArray(),
            TreeVerticalOffset = SafeOffset(state.TreeVerticalOffset),
            TreeHorizontalOffset = SafeOffset(state.TreeHorizontalOffset),
            Grid = WorkspaceGridState.Normalize(state.Grid),
            Player = WorkspacePlayerState.Normalize(state.Player)
        };
    }

    internal static double SafeOffset(double value) => double.IsFinite(value) ? Math.Max(0, value) : 0;
}

internal sealed record WorkspaceGridState
{
    public IReadOnlyList<Guid> SelectedAssetIds { get; init; } = [];
    public Guid? AnchorAssetId { get; init; }
    public Guid? TopAssetId { get; init; }
    public double WithinRowOffset { get; init; }
    public double VerticalOffset { get; init; }

    internal double RestoreOffset(int? topRow, int rowCount, double extentHeight, double scrollableHeight)
    {
        var rowHeight = rowCount > 0 ? extentHeight / rowCount : 0;
        var offset = topRow is { } row && rowHeight > 0
            ? row * rowHeight + Math.Min(WithinRowOffset, rowHeight) : VerticalOffset;
        return Math.Clamp(WorkspaceContinuationState.SafeOffset(offset), 0,
            WorkspaceContinuationState.SafeOffset(scrollableHeight));
    }

    public static WorkspaceGridState Normalize(WorkspaceGridState? state) => (state ?? new()) with
    {
        SelectedAssetIds = (state?.SelectedAssetIds ?? []).Where(id => id != Guid.Empty).Distinct().ToArray(),
        WithinRowOffset = WorkspaceContinuationState.SafeOffset(state?.WithinRowOffset ?? 0),
        VerticalOffset = WorkspaceContinuationState.SafeOffset(state?.VerticalOffset ?? 0)
    };
}

internal sealed record WorkspacePlayerState
{
    public required PlayerViewerAsset Asset { get; init; }
    // Same elapsed, source-normalized display timeline used by DisplayedTimestamp.Position and SeekAsync.
    public TimeSpan Position { get; init; }
    public double? PixelZoom { get; init; }
    public double PanX { get; init; }
    public double PanY { get; init; }
    public bool Loop { get; init; }
    public double Speed { get; init; } = 1;
    public MediaFrameRate? Cadence { get; init; }
    public bool Muted { get; init; }
    public double Volume { get; init; } = 100;
    public Guid? ActiveSubclipId { get; init; }
    public IReadOnlyList<Guid> SelectedSubclipIds { get; init; } = [];

    public static WorkspacePlayerState? Normalize(WorkspacePlayerState? state)
    {
        if (state?.Asset is not { RootId: var rootId, AssetId: { } id } asset ||
            rootId == Guid.Empty || id == Guid.Empty || !Enum.IsDefined(asset.Kind)) return null;
        try { _ = MediaPathSemantics.NormalizeRelativePath(asset.RelativePath); }
        catch (ArgumentException) { return null; }
        return state with
        {
            Position = state.Position < TimeSpan.Zero ? TimeSpan.Zero : state.Position,
            PixelZoom = state.PixelZoom is 0.5 or 1 or 2 or 4 ? state.PixelZoom : null,
            PanX = double.IsFinite(state.PanX) ? state.PanX : 0,
            PanY = double.IsFinite(state.PanY) ? state.PanY : 0,
            Speed = PlaybackReviewOptions.Speeds.Contains(state.Speed) ? state.Speed : 1,
            Volume = double.IsFinite(state.Volume) ? Math.Clamp(state.Volume, 0, 100) : 100,
            SelectedSubclipIds = (state.SelectedSubclipIds ?? []).Where(id => id != Guid.Empty).Distinct().ToArray()
        };
    }

    internal TimeSpan PositionFor(TimeSpan duration) => TimeSpan.FromTicks(Math.Clamp(Position.Ticks, 0, Math.Max(0, duration.Ticks)));
}

/// <summary>A startup request is revocable permanently; no delayed callback can restart it.</summary>
internal sealed class WorkspaceRestorationRequest : IDisposable
{
    private readonly CancellationTokenSource _cancellation = new();
    public CancellationToken Token => _cancellation.Token;
    public bool IsCurrent => !_cancellation.IsCancellationRequested;
    public void Cancel() => _cancellation.Cancel();
    public void Dispose() => _cancellation.Dispose();
}
